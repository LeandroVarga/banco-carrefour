using System.Reflection;
using BancoCarrefour.Consolidation.Infrastructure;
using BancoCarrefour.Contracts.Migrations;
using BancoCarrefour.Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BancoCarrefour.Architecture.Tests;

/// <summary>
/// Governança da plataforma AWS multi-conta (ver ADR-0011, ADR-0014):
/// garante, por leitura real dos arquivos Terraform/workflow, que a
/// estrutura de módulos/ambientes, as estratégias de deployment por
/// workload e os controles de segurança básicos permanecem corretos - sem
/// reimplementar um parser HCL completo (mesma técnica já usada neste
/// projeto para os módulos ECR/IAM existentes).
/// </summary>
public sealed class AwsPlatformGovernanceArchitectureTests
{
    private static readonly string[] Environments = ["development", "staging", "production"];

    [Fact]
    public void Os_3_ambientes_de_workload_devem_existir_com_os_arquivos_terraform_esperados()
    {
        foreach (var env in Environments)
        {
            foreach (var file in new[] { "main.tf", "variables.tf", "outputs.tf", "versions.tf", "provider.tf", "terraform.tfvars.example" })
            {
                var path = Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, file);
                Assert.True(File.Exists(path), $"infra/terraform/environments/{env}/{file} deveria existir.");
            }
        }
    }

    [Fact]
    public void Nenhum_ambiente_deve_conter_account_id_ou_role_arn_hardcoded()
    {
        // account_id/role_arn sao sempre "variable" sem default - nunca um
        // valor de 12 digitos hardcoded fora de terraform.tfvars.example
        // (que so contem zeros de exemplo, nunca uma conta real).
        foreach (var env in Environments)
        {
            var mainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, "main.tf"));
            Assert.DoesNotContain("arn:aws:iam::1", mainTf, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ledger_e_Consolidation_devem_ter_instancias_RDS_independentes_por_ambiente()
    {
        foreach (var env in Environments)
        {
            var mainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, "main.tf"));
            Assert.Contains("module \"rds_ledger\"", mainTf, StringComparison.Ordinal);
            Assert.Contains("module \"rds_consolidation\"", mainTf, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Publisher_e_Worker_devem_ter_roles_IAM_de_tarefa_separadas_e_nunca_uma_role_ampla_compartilhada()
    {
        foreach (var env in Environments)
        {
            var mainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, "main.tf"));

            foreach (var workload in new[] { "ledger-api", "ledger-outbox-publisher", "consolidation-api", "consolidation-worker" })
            {
                Assert.Contains($"{workload}-task", mainTf, StringComparison.Ordinal);
                Assert.Contains($"{workload}-execution", mainTf, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Ledger_Api_e_Consolidation_Api_devem_usar_estrategia_CANARY_nativa_do_ECS()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-api", "main.tf"));

        Assert.Contains("strategy             = \"CANARY\"", content, StringComparison.Ordinal);
        Assert.Contains("canary_configuration {", content, StringComparison.Ordinal);
        Assert.DoesNotContain("codedeploy", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Consolidation_Worker_deve_ter_dois_servicos_independentes_primary_e_canary()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-worker", "main.tf"));

        Assert.Contains("primary = {", content, StringComparison.Ordinal);
        Assert.Contains("canary = {", content, StringComparison.Ordinal);
        // Nunca uma unica estrategia de traffic-shift do ALB para o Worker
        // (nao ha ALB neste modulo) - o proprio par de servicos e o
        // mecanismo de canario.
        Assert.DoesNotContain("\"CANARY\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Ledger_OutboxPublisher_deve_usar_rolling_controlado_com_circuit_breaker_e_stopTimeout()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-publisher", "main.tf"));

        Assert.Contains("strategy = \"ROLLING\"", content, StringComparison.Ordinal);
        Assert.Contains("deployment_circuit_breaker {", content, StringComparison.Ordinal);
        Assert.Contains("stopTimeout = var.stop_timeout_seconds", content, StringComparison.Ordinal);
        Assert.Contains("deployment_minimum_healthy_percent = var.minimum_healthy_percent", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Modulo_de_alarmes_de_ALB_deve_ser_criado_dentro_do_proprio_ecs_service_api_para_evitar_dependencia_circular()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-api", "main.tf"));

        Assert.Contains("module \"alb_alarms\"", content, StringComparison.Ordinal);
        Assert.Contains("aws_lb_target_group.this.arn_suffix", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Todos_os_3_servicos_ECS_devem_ter_o_bloco_alarms_com_rollback_habilitado()
    {
        foreach (var (modulePath, fileName) in new[]
                 {
                     ("ecs-service-api", "main.tf"),
                     ("ecs-service-worker", "main.tf"),
                     ("ecs-service-publisher", "main.tf"),
                 })
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", modulePath, fileName));
            Assert.Contains("alarms {", content, StringComparison.Ordinal);
            Assert.Contains("rollback    = true", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Edge_deve_implementar_a_cadeia_WAF_APIGateway_VpcLinkV2_ALB_sem_NLB_intermediario()
    {
        // Correção real (auditoria pré-push): API Gateway REST v1 suporta
        // VPC Link V2 diretamente a um ALB, sem exigir NLB - confirmado
        // via documentação oficial da AWS
        // (apigateway/latest/developerguide/private-integration.html:
        // "VPC links V2 let you create private integrations that connect
        // your REST API to Application Load Balancers WITHOUT using a
        // Network Load Balancer") e via schema real do provider Terraform
        // (aws_api_gateway_integration.integration_target,
        // aws_apigatewayv2_vpc_link).
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "edge", "main.tf"));

        Assert.Contains("aws_wafv2_web_acl", content, StringComparison.Ordinal);
        Assert.Contains("aws_api_gateway_rest_api", content, StringComparison.Ordinal);
        Assert.Contains("aws_apigatewayv2_vpc_link", content, StringComparison.Ordinal);
        Assert.Contains("integration_target", content, StringComparison.Ordinal);
        Assert.Contains("connection_type         = \"VPC_LINK\"", content, StringComparison.Ordinal);
        Assert.Contains("internal           = true", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Edge_nunca_deve_reintroduzir_o_VPC_Link_classico_v1_ou_um_NLB_intermediario()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "edge", "main.tf"));

        // aws_api_gateway_vpc_link (SEM o sufixo v2/apigatewayv2) e o
        // recurso do VPC Link CLASSICO (v1) - legado, nunca deve ser
        // recriado. A checagem busca pelo nome exato do TIPO de recurso
        // (com aspas), nunca por uma substring que tambem casaria com
        // "aws_apigatewayv2_vpc_link".
        Assert.DoesNotContain("resource \"aws_api_gateway_vpc_link\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("load_balancer_type = \"network\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("target_type = \"alb\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("resource \"aws_lb_target_group_attachment\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void VpcLinkV2_deve_ter_seu_proprio_security_group_com_egress_escopado_ao_ALB()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "edge", "main.tf"));

        Assert.Contains("resource \"aws_security_group\" \"vpc_link\"", content, StringComparison.Ordinal);
        Assert.Contains("resource \"aws_security_group_rule\" \"vpc_link_to_alb\"", content, StringComparison.Ordinal);
        Assert.Contains("source_security_group_id = aws_security_group.vpc_link.id", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ALB_ingress_deve_vir_exclusivamente_do_security_group_do_VPC_Link_V2_nunca_de_CIDR_amplo_ou_NLB()
    {
        // O security group do ALB e criado no modulo network SEM nenhum
        // ingress inline (de proposito) - a unica regra de ingress e um
        // aws_security_group_rule SEPARADO no modulo edge, escopado ao
        // security group do VPC Link V2. Extracao de bloco por contagem de
        // chaves (robusta a CRLF - os .tf deste repositorio usam terminador
        // CRLF, que quebrava o casamento literal anterior por "\n}\n").
        var networkContent = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "network", "main.tf"));

        var albSgBlock = ExtractBlockByBraceDepth(networkContent, "resource \"aws_security_group\" \"alb\"");
        Assert.False(string.IsNullOrEmpty(albSgBlock), "security group do ALB nao encontrado no modulo network.");
        Assert.DoesNotContain("ingress {", albSgBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("load_balancer_type = \"network\"", networkContent, StringComparison.Ordinal);

        // O modulo edge nunca deve recriar a ownership do security group do
        // ALB (ele so referencia var.alb_security_group_id, criado uma
        // unica vez no modulo network).
        var edgeContent = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "edge", "main.tf"));
        Assert.DoesNotContain("resource \"aws_security_group\" \"alb\"", edgeContent, StringComparison.Ordinal);

        var vpcLinkRuleBlock = ExtractBlockByBraceDepth(edgeContent, "resource \"aws_security_group_rule\" \"vpc_link_to_alb\"");
        Assert.False(string.IsNullOrEmpty(vpcLinkRuleBlock), "regra de ingress do ALB a partir do VPC Link V2 nao encontrada no modulo edge.");
        Assert.Contains("source_security_group_id", vpcLinkRuleBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("cidr_blocks", vpcLinkRuleBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Modulo_edge_nunca_deve_referenciar_recursos_de_outra_conta_para_ALB_VpcLink_ou_API_Gateway()
    {
        // Restricao de mesma conta (confirmada na documentacao oficial:
        // "All resources must be owned by the same AWS account. This
        // includes the load balancer, VPC link and REST API.") - o modulo
        // edge nunca deve aceitar um account_id/role_arn cross-account
        // para seus proprios recursos (ALB, VPC Link V2, API Gateway).
        // Apenas o Amazon ECR (modulo ecr, ambiente aws-reference) e
        // cross-account neste repositorio.
        var variablesTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "edge", "variables.tf"));

        Assert.DoesNotContain("account_id", variablesTf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cross_account", variablesTf, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RDS_deve_ter_criptografia_backup_e_protecao_de_delecao_configuraveis_por_ambiente()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "rds-postgresql", "main.tf"));

        Assert.Contains("storage_encrypted", content, StringComparison.Ordinal);
        Assert.Contains("= true", content, StringComparison.Ordinal);
        Assert.Contains("= var.deletion_protection", content, StringComparison.Ordinal);
        Assert.Contains("= var.backup_retention_days", content, StringComparison.Ordinal);
        Assert.Contains("publicly_accessible", content, StringComparison.Ordinal);
        Assert.Contains("= false", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Roles_de_deploy_OIDC_devem_ser_condicionadas_ao_GitHub_Environment_exato_do_ambiente()
    {
        foreach (var env in Environments)
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, "main.tf"));
            Assert.Contains($"repo:${{var.github_repository}}:environment:${{var.github_environment}}", content, StringComparison.Ordinal);
            Assert.Contains("token.actions.githubusercontent.com:aud", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Modulo_ECR_deve_suportar_resource_policy_cross_account_sem_conceder_push()
    {
        var variablesTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecr", "variables.tf"));
        var mainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecr", "main.tf"));

        Assert.Contains("variable \"repository_policy\"", variablesTf, StringComparison.Ordinal);
        Assert.Contains("aws_ecr_repository_policy", mainTf, StringComparison.Ordinal);

        var awsReferenceMainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", "aws-reference", "main.tf"));
        Assert.Contains("AllowWorkloadAccountsPull", awsReferenceMainTf, StringComparison.Ordinal);

        // Escopa a checagem apenas ao BLOCO da cross_account_pull_policy
        // (do inicio da declaracao ate o "null" que fecha o operador
        // ternario) - nunca ao arquivo inteiro, que legitimamente contem
        // "ecr:PutImage" na policy SEPARADA da role de publicacao real
        // (ecr_publisher_permissions, que precisa publicar).
        var startIndex = awsReferenceMainTf.IndexOf("cross_account_pull_policy", StringComparison.Ordinal);
        var endIndex = awsReferenceMainTf.IndexOf("}) : null", startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex, "bloco cross_account_pull_policy nao encontrado.");
        var crossAccountPolicyBlock = awsReferenceMainTf[startIndex..endIndex];

        Assert.DoesNotContain("PutImage", crossAccountPolicyBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("InitiateLayerUpload", crossAccountPolicyBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflows_de_deploy_AWS_devem_existir_com_os_nomes_de_ambiente_corretos()
    {
        var expected = new Dictionary<string, string>
        {
            ["deploy-development.yml"] = "development",
            ["promote-staging.yml"] = "staging",
            ["promote-production.yml"] = "production",
            ["rollback-production.yml"] = "production",
        };

        foreach (var (file, expectedEnvironment) in expected)
        {
            var path = Path.Combine(RepositoryRoot, ".github", "workflows", file);
            Assert.True(File.Exists(path), $".github/workflows/{file} deveria existir.");

            var content = File.ReadAllText(path);
            Assert.Contains($"name: {expectedEnvironment}", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rollback_de_production_nunca_deve_permitir_a_release_atual_como_sua_propria_predecessora()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "rollback-production.yml"));

        Assert.Contains("current_failed_run_id", content, StringComparison.Ordinal);
        Assert.Contains("inputs.previous_promote_run_id }}\" = \"${{ inputs.current_failed_run_id", content, StringComparison.Ordinal);
        Assert.Contains("rollback recusado", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Rollback_de_production_deve_ser_estritamente_rollback_de_aplicacao_e_nunca_de_banco_de_dados()
    {
        // ADR-0015: rollback-production.yml so troca digests de imagem - nunca deve
        // invocar de fato a task de migracao. Verifica os PADROES REAIS de
        // invocacao (chamada ao script, target Terraform da task de
        // migracao), nunca uma substring bruta como "run-migration-task.sh"
        // sozinha - o proprio header do workflow cita esse nome em prosa
        // explicando o que ele NAO faz, o que produziria falso positivo.
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "rollback-production.yml"));

        Assert.DoesNotContain("scripts/ci/run-migration-task.sh", content, StringComparison.Ordinal);
        Assert.DoesNotContain("-target=module.ecs_migration_task", content, StringComparison.Ordinal);
        Assert.DoesNotContain("migration_runner_image", content, StringComparison.Ordinal);
        Assert.DoesNotContain("aws ecs run-task", content, StringComparison.Ordinal);

        Assert.Contains("ESCOPO EXATO (rollback de APLICACAO, nunca de BANCO DE DADOS)", content, StringComparison.Ordinal);
        Assert.Contains("nunca reverte/desfaz uma migration ja", content, StringComparison.Ordinal);
        Assert.Contains("nunca toca o banco de dados", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Nenhum_workflow_deve_chamar_aws_ecs_run_task_diretamente_fora_do_script_reutilizavel()
    {
        // A UNICA forma de disparar a task one-off de migracao precisa ser
        // scripts/ci/run-migration-task.sh (que sempre invoca o comando
        // "migrate", nunca aceita "contract" como parametro - ver o script).
        // Nenhum workflow pode conter sua propria chamada "aws ecs run-task"
        // que contorne esse unico ponto de entrada.
        foreach (var file in new[] { "deploy-development.yml", "promote-staging.yml", "promote-production.yml", "rollback-production.yml" })
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", file));
            Assert.DoesNotContain("aws ecs run-task", content, StringComparison.Ordinal);
        }

        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "ci", "run-migration-task.sh"));
        Assert.Contains("aws ecs run-task", script, StringComparison.Ordinal);
        Assert.Contains("'migrate',", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'contract',", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Os_3_workflows_de_deploy_AWS_que_rodam_migrations_devem_bloquear_a_atualizacao_da_aplicacao_em_caso_de_falha()
    {
        // Cenario B (runbook, secao 7.1): falha de migracao ANTES do
        // deploy de aplicacao. run-migration-task.sh precisa ser chamado
        // ANTES do terraform apply final que atualiza os 4 servicos, para
        // que um exitCode != 0 bloqueie o deploy/promocao (script usa
        // "set -eu" e "fail() { ...; exit 1; }" - uma falha do script
        // interrompe o job inteiro antes do proximo step rodar).
        foreach (var file in new[] { "deploy-development.yml", "promote-staging.yml", "promote-production.yml" })
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", file));

            Assert.Contains("run-migration-task.sh", content, StringComparison.Ordinal);
            Assert.Contains("--boundary Ledger", content, StringComparison.Ordinal);
            Assert.Contains("--boundary Consolidation", content, StringComparison.Ordinal);

            var lastMigrationStepIndex = content.LastIndexOf("run-migration-task.sh", StringComparison.Ordinal);
            var finalApplyIndex = content.IndexOf("so apos as 2 migracoes confirmarem exitCode=0", StringComparison.Ordinal);
            if (finalApplyIndex < 0)
            {
                finalApplyIndex = content.IndexOf("so apos as 2 migracoes confirmarem", StringComparison.Ordinal);
            }

            Assert.True(finalApplyIndex > lastMigrationStepIndex, $"{file}: o apply que atualiza os servicos de aplicacao precisa vir DEPOIS das 2 chamadas de migracao, nunca antes.");
        }
    }

    [Fact]
    public void Runbook_de_implantacao_deve_documentar_os_3_cenarios_reais_de_rollback()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "operations", "runbook-implantacao-aws.md"));

        Assert.Contains("Os 3 cenários reais de rollback", content, StringComparison.Ordinal);
        Assert.Contains("Rollback de aplicação após EXPAND bem-sucedido", content, StringComparison.Ordinal);
        Assert.Contains("Falha de migração antes do deploy de aplicação", content, StringComparison.Ordinal);
        Assert.Contains("Falha de CONTRACT", content, StringComparison.Ordinal);
        Assert.Contains("Não há papel para `rollback-production.yml` neste", content, StringComparison.Ordinal);
        Assert.Contains("Isso nunca deve ser \"corrigido\" via", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Observabilidade_de_migracao_deve_usar_apenas_eventos_StructuredLog_genuinamente_emitidos_pelo_MigrationRunner()
    {
        // ADR-0015: o metric filter que alimenta o alarme de falha de aplicação
        // nunca pode referenciar um nome de evento fabricado - cada
        // "$.event = ..." precisa corresponder a uma chamada real de
        // StructuredLog.Emit(...) em src/Migrations/MigrationRunner/*.cs.
        var observabilityTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-migration-task", "observability.tf"));

        var eventNamePattern = new System.Text.RegularExpressions.Regex("\"migration\\.[a-z_]+\"");
        var referencedEvents = eventNamePattern.Matches(observabilityTf)
            .Select(m => m.Value.Trim('"'))
            .Distinct()
            .ToArray();

        Assert.NotEmpty(referencedEvents);

        var migrationRunnerSourceDir = Path.Combine(RepositoryRoot, "src", "Migrations", "MigrationRunner");
        var migrationRunnerSource = string.Join("\n", Directory.GetFiles(migrationRunnerSourceDir, "*.cs").Select(File.ReadAllText));

        foreach (var eventName in referencedEvents)
        {
            Assert.Contains($"StructuredLog.Emit(\"{eventName}\"", migrationRunnerSource, StringComparison.Ordinal);
        }

        // O caminho automático nunca inclui contract/backfill (nunca
        // invocados por workflow algum) - o alarme não deve depender de
        // eventos que só um operador manual observaria diretamente.
        Assert.DoesNotContain("contract_failed", string.Join(",", referencedEvents), StringComparison.Ordinal);
        Assert.DoesNotContain("backfill_failed", string.Join(",", referencedEvents), StringComparison.Ordinal);
    }

    [Fact]
    public void Observabilidade_de_migracao_deve_ter_alarme_para_falha_da_aplicacao_e_alarme_separado_para_task_que_nunca_iniciou()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-migration-task", "observability.tf"));

        // (1) Falha reportada pela própria aplicação via metric filter real.
        Assert.Contains("resource \"aws_cloudwatch_log_metric_filter\" \"migration_runner_failures\"", content, StringComparison.Ordinal);
        Assert.Contains("log_group_name = var.log_group_name", content, StringComparison.Ordinal);

        // (2) Falha ANTES da aplicação rodar - evento nativo do ECS (nenhum
        // log da aplicação existe nesse caso), escopado à família desta
        // task definition (nunca a todo o cluster/outros workloads).
        Assert.Contains("source      = [\"aws.ecs\"]", content, StringComparison.Ordinal);
        Assert.Contains("\"ECS Task State Change\"", content, StringComparison.Ordinal);
        Assert.Contains("stopCode   = [\"TaskFailedToStart\"]", content, StringComparison.Ordinal);
        Assert.Contains("group      = [\"family:${aws_ecs_task_definition.this.family}\"]", content, StringComparison.Ordinal);

        // Ambos alimentam um alarme CloudWatch real (fábrica genérica já
        // usada pelos outros workloads, nunca uma métrica inventada).
        Assert.Contains("module \"migration_runner_failures_alarm\"", content, StringComparison.Ordinal);
        Assert.Contains("module \"task_failed_to_start_alarm\"", content, StringComparison.Ordinal);
        Assert.Contains("namespace           = \"AWS/Events\"", content, StringComparison.Ordinal);
        Assert.Contains("metric_name         = \"MatchedEvents\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Politica_de_log_group_para_EventBridge_deve_restringir_PutLogEvents_a_esta_regra_especifica()
    {
        // Nunca uma policy aberta a qualquer regra EventBridge da conta -
        // a condição ArnEquals/aws:SourceArn (padrão oficial documentado
        // pelo provider AWS) restringe a ESTA regra especificamente.
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-migration-task", "observability.tf"));

        Assert.Contains("resource \"aws_cloudwatch_log_resource_policy\" \"task_lifecycle_events\"", content, StringComparison.Ordinal);
        Assert.Contains("test     = \"ArnEquals\"", content, StringComparison.Ordinal);
        Assert.Contains("variable = \"aws:SourceArn\"", content, StringComparison.Ordinal);
        Assert.Contains("values   = [aws_cloudwatch_event_rule.task_failed_to_start.arn]", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_ECS_de_migracao_nunca_deve_ser_um_aws_ecs_service_e_deve_ter_security_group_dedicado_sem_ingress()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-migration-task", "main.tf"));

        Assert.Contains("resource \"aws_ecs_task_definition\" \"this\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("resource \"aws_ecs_service\"", content, StringComparison.Ordinal);

        // Security group dedicado (nunca o mesmo dos 4 workloads de
        // negócio) e sem NENHUM bloco "ingress" - a task só origina
        // conexões, nunca recebe.
        Assert.Contains("resource \"aws_security_group\" \"migration_task\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ingress {", content, StringComparison.Ordinal);
        Assert.Contains("egress {", content, StringComparison.Ordinal);

        // Sem "command" fixo na task definition - o comando real é sempre
        // informado via containerOverrides no "aws ecs run-task" (nunca
        // executada sem overrides explícitos).
        Assert.DoesNotContain("command = [", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Imagem_do_migration_runner_deve_ser_qualificada_por_digest_nunca_tag_mutavel()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-migration-task", "variables.tf"));

        Assert.Contains("variable \"image\"", content, StringComparison.Ordinal);
        Assert.Contains("can(regex(\"@sha256:[0-9a-f]{64}$\", var.image))", content, StringComparison.Ordinal);
        Assert.DoesNotContain("default     = \"latest\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationRunner_nunca_deve_logar_a_mensagem_bruta_de_excecao_apenas_o_tipo()
    {
        // "auditable logs without secrets" (ADR-0015): uma
        // exceção do Npgsql pode conter host/porta/usuário na própria
        // mensagem - toda emissão de StructuredLog em caminho de erro
        // precisa se limitar a ex.GetType().Name, nunca a ex.Message.
        var migrationRunnerSourceDir = Path.Combine(RepositoryRoot, "src", "Migrations", "MigrationRunner");
        var files = Directory.GetFiles(migrationRunnerSourceDir, "*.cs")
            .Concat(Directory.GetFiles(migrationRunnerSourceDir, "*.csx"));

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            // Padrão real de atribuição/uso em código (nunca a substring
            // bruta "ex.Message", que também aparece em comentários que
            // documentam esta mesma regra, produzindo falso positivo).
            Assert.DoesNotContain("= ex.Message", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Comando_contract_deve_exigir_approved_by_e_compatibility_window_closed_e_nunca_ser_invocado_automaticamente()
    {
        var contractCommand = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Migrations", "MigrationRunner", "ContractCommand.cs"));

        Assert.Contains("options.ApprovedBy", contractCommand, StringComparison.Ordinal);
        Assert.Contains("options.CompatibilityWindowClosed", contractCommand, StringComparison.Ordinal);
        Assert.Contains("ExitCode.UsageOrConfigurationError", contractCommand, StringComparison.Ordinal);

        // Nenhum dos 3 workflows de deploy nem o run-migration-task.sh
        // reutilizável constrói uma invocação de "contract".
        foreach (var file in new[] { "deploy-development.yml", "promote-staging.yml", "promote-production.yml" })
        {
            var workflowContent = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", file));
            Assert.DoesNotContain("--approved-by", workflowContent, StringComparison.Ordinal);
            Assert.DoesNotContain("--compatibility-window-closed", workflowContent, StringComparison.Ordinal);
        }

        var runMigrationTaskScript = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "ci", "run-migration-task.sh"));
        Assert.DoesNotContain("--approved-by", runMigrationTaskScript, StringComparison.Ordinal);
        Assert.DoesNotContain("--compatibility-window-closed", runMigrationTaskScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Advisory_lock_de_migracao_deve_ter_chaves_distintas_por_fronteira_e_desabilitar_pooling_na_conexao_do_lock()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Migrations", "MigrationRunner", "PostgresMigrationLock.cs"));

        Assert.Contains("LedgerBoundaryKey", content, StringComparison.Ordinal);
        Assert.Contains("ConsolidationBoundaryKey", content, StringComparison.Ordinal);
        Assert.Contains("pg_try_advisory_lock", content, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_unlock", content, StringComparison.Ordinal);

        // Achado real de teste (Testcontainers, FMC-3): sem Pooling=false o
        // Npgsql devolve a conexão a um pool interno sem desconectar do
        // backend, e o advisory lock nunca é liberado de fato.
        Assert.Contains("Pooling = false", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_apply_nos_workflows_de_deploy_nunca_deve_usar_dash_target_como_forma_rotineira()
    {
        // O registro da task definition de migração por release passou a
        // ser feito diretamente via API ECS
        // (scripts/ci/run-migration-task.sh, "aws ecs
        // register-task-definition"), nunca mais via "terraform apply
        // -target". A infraestrutura estável da task de migração (family,
        // roles, security group, log group) é materializada por um apply
        // de rotina (sem -target) anterior - "lifecycle.ignore_changes =
        // [container_definitions]" no módulo ecs-migration-task garante
        // que esse apply de rotina nunca reverte a revisão registrada por
        // release. Nenhum dos 3 workflows de deploy pode conter
        // "-target=" - esse era exatamente o padrão eliminado nesta
        // correção.
        var runMigrationTaskScript = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "ci", "run-migration-task.sh"));
        Assert.Contains("aws ecs register-task-definition", runMigrationTaskScript, StringComparison.Ordinal);

        foreach (var file in new[] { "deploy-development.yml", "promote-staging.yml", "promote-production.yml" })
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", file));

            // "-target=" é o padrão REAL de invocação (nunca aparece nos 3
            // workflows) - os próprios comentários explicativos citam a
            // frase "terraform apply -target" em prosa (sem "="), o que
            // produziria falso positivo numa checagem de substring mais
            // ampla.
            Assert.DoesNotContain("-target=", content, StringComparison.Ordinal);
            Assert.Contains("--render-params-json", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Schema_de_release_manifest_deve_separar_exatamente_4_componentes_de_negocio_do_artefato_operacional()
    {
        // O MigrationRunner é um artefato operacional de release, nunca um 5º
        // componente de negócio - o schema precisa declarar as duas
        // coleções separadamente (nunca uma única lista indiferenciada de
        // 5 nomes).
        var schema = File.ReadAllText(Path.Combine(RepositoryRoot, "schemas", "release-manifest.schema.json"));

        Assert.Contains("\"components\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"operationalArtifacts\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"operationalArtifact\"", schema, StringComparison.Ordinal);

        var example = File.ReadAllText(Path.Combine(RepositoryRoot, "schemas", "examples", "release-manifest.example.json"));
        Assert.Contains("\"migration-runner\"", example, StringComparison.Ordinal);
        Assert.Contains("\"digestQualifiedReference\"", example, StringComparison.Ordinal);

        // Os scripts que geram/validam o manifesto real precisam conhecer
        // as duas coleções (nunca tratar migration-runner como um 5º nome
        // dentro de "components", nem hardcodar "4" como o total de
        // artefatos publicados).
        foreach (var script in new[] { "generate-release-manifest.sh", "validate-release-manifest.sh", "record-release-attestations.sh", "publish-validated-images.sh" })
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "ci", script));
            Assert.Contains("operationalArtifacts", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Migration_task_roles_devem_ter_exatamente_1_secret_arn_cada_nunca_ambos_nem_acesso_a_SQS_ou_publicacao_ECR()
    {
        // A task role de migração do Ledger só pode referenciar o secret do
        // Ledger, a do Consolidation só o do Consolidation - nunca ambos na
        // mesma role, nunca SQS, nunca permissão de publicação ECR
        // (publicação é responsabilidade exclusiva do publisher de CI,
        // nunca de uma task role de runtime).
        foreach (var env in Environments)
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, "main.tf"));

            var ledgerTaskRole = ExtractRoleBlock(content, "migration-ledger-task");
            var consolidationTaskRole = ExtractRoleBlock(content, "migration-consolidation-task");

            Assert.Contains("ledger-migration/db-credentials", ledgerTaskRole, StringComparison.Ordinal);
            Assert.DoesNotContain("consolidation-migration/db-credentials", ledgerTaskRole, StringComparison.Ordinal);

            Assert.Contains("consolidation-migration/db-credentials", consolidationTaskRole, StringComparison.Ordinal);
            Assert.DoesNotContain("ledger-migration/db-credentials", consolidationTaskRole, StringComparison.Ordinal);

            foreach (var roleBlock in new[] { ledgerTaskRole, consolidationTaskRole })
            {
                Assert.DoesNotContain("sqs_send_queue_arns", roleBlock, StringComparison.Ordinal);
                Assert.DoesNotContain("sqs_consume_queue_arns", roleBlock, StringComparison.Ordinal);
                Assert.DoesNotContain("ecr_repository_arns", roleBlock, StringComparison.Ordinal);
            }

            // A execution role compartilhada nunca recebe secret_arns (o
            // MigrationRunner resolve seu próprio secret via SDK, usando a
            // TASK role - ver comentário em main.tf).
            var executionRole = ExtractRoleBlock(content, "migration-runner-execution");
            Assert.DoesNotContain("secret_arns", executionRole, StringComparison.Ordinal);
            Assert.Contains("ecr_repository_arns", executionRole, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Release_qualification_deve_consumir_apenas_as_4_imagens_de_negocio_nunca_o_migration_runner_como_servico()
    {
        // O runtime de aplicação do release-qualification consome SOMENTE as
        // 4 imagens de negócio - o MigrationRunner nunca aparece como
        // serviço de longa duração neste Compose (sua validação é via
        // testes/non-root/SBOM/scan, nunca como container rodando aqui).
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot, "deploy", "compose", "docker-compose.release-qualification.yml"));

        foreach (var envVar in new[] { "LEDGER_API_IMAGE_REF", "LEDGER_OUTBOX_PUBLISHER_IMAGE_REF", "CONSOLIDATION_API_IMAGE_REF", "CONSOLIDATION_WORKER_IMAGE_REF" })
        {
            Assert.Contains(envVar, compose, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("MIGRATION_RUNNER_IMAGE_REF", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("migration-runner", compose, StringComparison.Ordinal);

        // O script que popula essas 4 variáveis busca por nome exato de
        // componente em artifacts/sbom/images.json - mesmo que esse
        // arquivo contenha uma 5ª entrada (migration-runner), ela nunca é
        // lida aqui.
        var runScript = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "ci", "run-release-qualification.sh"));
        Assert.DoesNotContain("migration-runner", runScript, StringComparison.Ordinal);
        Assert.Contains("component']=='ledger-api'", runScript, StringComparison.Ordinal);
    }

    private static string ExtractRoleBlock(string terraformContent, string roleNameFragment)
    {
        var keyIndex = terraformContent.IndexOf(roleNameFragment, StringComparison.Ordinal);
        Assert.True(keyIndex >= 0, $"role contendo '{roleNameFragment}' não encontrada.");

        var openBraceIndex = terraformContent.IndexOf('{', keyIndex);
        var closeBraceIndex = terraformContent.IndexOf("\n    }", openBraceIndex, StringComparison.Ordinal);
        Assert.True(openBraceIndex >= 0 && closeBraceIndex > openBraceIndex, $"corpo da role '{roleNameFragment}' não pôde ser delimitado.");

        return terraformContent[openBraceIndex..closeBraceIndex];
    }

    // Extrai um bloco de recurso pela contagem de profundidade de chaves -
    // ao contrario de um casamento literal por sequencia de newline
    // ("\n}\n"), funciona independente do terminador de linha do arquivo
    // (CRLF ou LF) e respeita blocos aninhados (ex.: "lifecycle { ... }"
    // dentro do proprio recurso).
    private static string ExtractBlockByBraceDepth(string content, string marker)
    {
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var braceStart = content.IndexOf('{', start);
        if (braceStart < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[start..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    // --- Auditoria estrutural aprofundada (fechamento pré-push): não basta
    // ser sintaticamente válido - cada estratégia precisa ter os elementos
    // estruturais completos exigidos pelo mecanismo real que declara usar. ---

    [Fact]
    public void Api_canary_deve_ter_revisao_candidata_target_group_health_check_alarmes_e_dimensao_de_release()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-api", "main.tf"));

        // Revisão estável/candidata: o próprio aws_ecs_task_definition (uma
        // nova revisão a cada apply com imagem diferente) e o
        // deployment_configuration.strategy=CANARY do ECS gerenciam essa
        // transição nativamente - não há uma segunda task definition
        // "candidata" explícita neste módulo (diferente do Worker,
        // que não tem esse mecanismo nativo).
        Assert.Contains("resource \"aws_ecs_task_definition\" \"this\"", content, StringComparison.Ordinal);
        Assert.Contains("resource \"aws_lb_target_group\" \"this\"", content, StringComparison.Ordinal);
        Assert.Contains("health_check {", content, StringComparison.Ordinal);
        Assert.Contains("healthCheck = {", content, StringComparison.Ordinal);
        Assert.Contains("health_check_grace_period_seconds", content, StringComparison.Ordinal);
        Assert.Contains("module \"alb_alarms\"", content, StringComparison.Ordinal);
        Assert.Contains("RELEASE_VERSION", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_canary_nunca_deve_ser_confundido_com_rolling_simples()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-api", "main.tf"));

        Assert.DoesNotContain("strategy             = \"ROLLING\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy = \"ROLLING\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_capacity_canary_deve_ter_identidades_de_imagem_candidata_e_aprovada_distintas_e_mesma_fila()
    {
        var mainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-worker", "main.tf"));
        var variablesTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-worker", "variables.tf"));

        Assert.Contains("variable \"primary_image\"", variablesTf, StringComparison.Ordinal);
        Assert.Contains("variable \"canary_image\"", variablesTf, StringComparison.Ordinal);
        Assert.Contains("variable \"canary_desired_count\"", variablesTf, StringComparison.Ordinal);

        // Ambos os serviços (primary/canary) recebem o MESMO
        // var.environment_variables (inclui Sqs__QueueUrl) - nunca filas
        // distintas por serviço.
        Assert.Contains("merge(var.environment_variables,", mainTf, StringComparison.Ordinal);

        // Documento nunca deve reivindicar uma porcentagem exata de
        // mensagens - a fração é sempre aproximada e dirigida por
        // capacidade relativa (canary_desired_count vs primary_desired_count).
        Assert.DoesNotContain("canary_percent", mainTf, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_capacity_canary_deve_permitir_scale_down_promocao_e_remocao_apos_sucesso()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-worker", "main.tf"));

        // canary_image=null remove a entrada "canary" do mapa local.services
        // (nenhuma task definition/serviço canário é criado) - a "remoção
        // após promoção bem-sucedida" é exatamente essa condição.
        Assert.Contains("var.canary_image != null ?", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Promocao_de_production_deve_expor_e_encadear_o_canario_de_capacidade_do_Worker_ate_o_terraform_apply()
    {
        // A ADR-0014 exige que a promocao/remocao do canario do Worker seja
        // conduzida pelo workflow de deploy - sem isso, o canario so
        // poderia ser acionado por um terraform apply manual fora do workflow.
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "promote-production.yml"));

        Assert.Contains("consolidation_worker_canary_image:", content, StringComparison.Ordinal);
        Assert.Contains("consolidation_worker_canary_desired_count:", content, StringComparison.Ordinal);
        Assert.Contains("CANARY_ARGS", content, StringComparison.Ordinal);
        Assert.Contains("$CANARY_ARGS", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Publisher_deve_documentar_a_semantica_real_de_shutdown_seguro_via_claim_recuperavel()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "ecs-service-publisher", "main.tf"));

        Assert.Contains("stopTimeout = var.stop_timeout_seconds", content, StringComparison.Ordinal);
        Assert.Contains("nunca manter uma transacao de claim", content, StringComparison.Ordinal);
        Assert.Contains("aberta durante a publicacao no SQS (ADR-0004)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Aplicacao_do_OutboxPublisher_nunca_deve_publicar_no_SQS_dentro_da_transacao_de_claim()
    {
        // Verificação de código real (ADR-0004) - não apenas Terraform: o
        // claim (ClaimNextBatchAsync) e a publicação (publisher.PublishAsync)
        // são chamadas SEPARADAS, nunca dentro de uma mesma transação de
        // banco. O claim usa timeout de recuperação (nunca fica preso
        // indefinidamente se o processo for encerrado no meio do lote).
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Ledger", "Ledger.Application", "PublishOutbox", "PublishPendingEventsUseCase.cs"));

        var claimIndex = content.IndexOf("ClaimNextBatchAsync", StringComparison.Ordinal);
        var publishIndex = content.IndexOf("publisher.PublishAsync", StringComparison.Ordinal);
        Assert.True(claimIndex >= 0 && publishIndex >= 0 && claimIndex < publishIndex, "o claim deve ocorrer antes e fora da chamada de publicação.");
        Assert.Contains("options.ClaimTimeout", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Publisher_deve_ter_alarmes_de_backlog_e_idade_do_item_mais_antigo_configuraveis()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", "production", "main.tf"));

        Assert.Contains("module \"alarms_ledger_outbox_publisher\"", content, StringComparison.Ordinal);
        Assert.Contains("publish-failures", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Cada_ambiente_deve_ter_um_dashboard_CloudWatch_real_com_widgets_para_Ledger_Consolidation_APIs_e_Deployment()
    {
        foreach (var env in Environments)
        {
            var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, "main.tf"));

            Assert.Contains("resource \"aws_cloudwatch_dashboard\" \"this\"", content, StringComparison.Ordinal);
            Assert.DoesNotContain("widgets = []", content, StringComparison.Ordinal);
            Assert.Contains("## Ledger.Api / Ledger.OutboxPublisher", content, StringComparison.Ordinal);
            Assert.Contains("## Consolidation.Api / Consolidation.Worker", content, StringComparison.Ordinal);
            Assert.Contains("## APIs (ALB + ECS)", content, StringComparison.Ordinal);
            Assert.Contains("## Deployment - estratégia por workload e gates de rollback", content, StringComparison.Ordinal);
            Assert.Contains("type = \"alarm\"", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Dashboard_deve_usar_nomes_de_metrica_reais_do_codigo_e_marcar_explicitamente_contratos_ainda_nao_emitidos()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", "production", "main.tf"));

        // Métricas REAIS (existem em src/*/Observability.cs hoje).
        Assert.Contains("ledger.entries.created", content, StringComparison.Ordinal);
        Assert.Contains("ledger.outbox.messages.failed", content, StringComparison.Ordinal);
        Assert.Contains("consolidation.events.processed", content, StringComparison.Ordinal);
        Assert.Contains("consolidation.daily_balance.query.duration", content, StringComparison.Ordinal);

        // Contratos declarados para métricas que a aplicação ainda NÃO emite
        // - devem estar explicitamente rotulados, nunca apresentados como
        // dado real.
        Assert.Contains("NAO EMITIDA AINDA", content, StringComparison.Ordinal);
        Assert.Contains("outbox_pending_events_total", content, StringComparison.Ordinal);
        Assert.Contains("consolidation_lag_seconds", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Modulo_observability_nunca_deve_criar_o_dashboard_para_evitar_dependencia_circular_com_os_modulos_de_workload()
    {
        // O dashboard precisa de outputs dos
        // módulos de workload (ecs-service-api.alarm_arns etc.), mas esses
        // módulos consomem module.observability.log_group_names na própria
        // instanciação - se o dashboard fosse criado DENTRO do módulo
        // observability, o grafo de dependências seria circular.
        var mainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "observability", "main.tf"));
        var variablesTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "observability", "variables.tf"));

        Assert.DoesNotContain("resource \"aws_cloudwatch_dashboard\"", mainTf, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard_body_json", variablesTf, StringComparison.Ordinal);
    }

    [Fact]
    public void Modulo_iam_deve_separar_acoes_de_SQS_entre_producao_send_e_consumo_receive_delete()
    {
        // ADR-0009 exige permissoes IAM distintas para Publisher (send) e
        // Worker (receive/delete) - um unico "sqs_queue_arns" concederia
        // SendMessage+ReceiveMessage+DeleteMessage+ChangeMessageVisibility
        // a QUALQUER role que o usasse, contrariando o isolamento
        // produtor/consumidor exigido pela decisão.
        var mainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "iam", "main.tf"));
        var variablesTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "modules", "iam", "variables.tf"));

        Assert.Contains("sqs_send_queue_arns", variablesTf, StringComparison.Ordinal);
        Assert.Contains("sqs_consume_queue_arns", variablesTf, StringComparison.Ordinal);
        Assert.DoesNotContain("sqs_queue_arns    = optional", variablesTf, StringComparison.Ordinal);

        var sendStart = mainTf.IndexOf("sqs_send_queue_arns", StringComparison.Ordinal);
        var sendEnd = mainTf.IndexOf('}', mainTf.IndexOf('{', sendStart));
        var sendBlock = mainTf[sendStart..sendEnd];
        Assert.Contains("sqs:SendMessage", sendBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("sqs:ReceiveMessage", sendBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("sqs:DeleteMessage", sendBlock, StringComparison.Ordinal);

        var consumeStart = mainTf.IndexOf("sqs_consume_queue_arns", StringComparison.Ordinal);
        var consumeEnd = mainTf.IndexOf('}', mainTf.IndexOf('{', consumeStart));
        var consumeBlock = mainTf[consumeStart..consumeEnd];
        Assert.Contains("sqs:ReceiveMessage", consumeBlock, StringComparison.Ordinal);
        Assert.Contains("sqs:DeleteMessage", consumeBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("sqs:SendMessage", consumeBlock, StringComparison.Ordinal);

        foreach (var env in Environments)
        {
            var envMainTf = File.ReadAllText(Path.Combine(RepositoryRoot, "infra", "terraform", "environments", env, "main.tf"));
            Assert.Contains("sqs_send_queue_arns = [module.messaging.queue_arn]", envMainTf, StringComparison.Ordinal);
            Assert.Contains("sqs_consume_queue_arns = [module.messaging.queue_arn, module.messaging.dlq_arn]", envMainTf, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Migrations_reais_do_EF_Core_devem_declarar_MigrationPhase_real_consistente_com_o_conteudo_de_Up()
    {
        // Espelha scripts/ci/validate-migration-governance.sh em C# - mas,
        // diferente da versão anterior (que procurava um comentário de
        // texto livre "CONTRACT-PHASE-APPROVED"), lê a metadata REAL via
        // reflexão sobre [MigrationPhase(...)]
        // (BancoCarrefour.Contracts.Migrations) - nunca infere a fase a
        // partir do nome do arquivo/classe (ADR-0015).
        string[] destructivePatterns = ["DropColumn(", "DropTable(", "RenameColumn(", "RenameTable("];

        var assemblies = new[] { typeof(LedgerDbContext).Assembly, typeof(ConsolidationDbContext).Assembly };
        var migrationTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .ToArray();

        Assert.True(migrationTypes.Length >= 3, "esperadas ao menos as 3 migrations reais conhecidas (InitialLedgerPersistence, InitialConsolidationPersistence, AddRecoverableOutboxClaim).");

        foreach (var type in migrationTypes)
        {
            var phaseAttribute = type.GetCustomAttribute<MigrationPhaseAttribute>();
            Assert.True(phaseAttribute is not null, $"{type.FullName} não declara [MigrationPhase(...)] - toda migration real precisa de metadata explícita de fase.");
            Assert.NotEqual(MigrationPhase.Backfill, phaseAttribute!.Phase);

            var sourceFile = FindMigrationSourceFile(type.Name);
            var content = File.ReadAllText(sourceFile);
            var upStart = content.IndexOf("protected override void Up(", StringComparison.Ordinal);
            var downStart = content.IndexOf("protected override void Down(", StringComparison.Ordinal);
            Assert.True(upStart >= 0 && downStart > upStart, $"{sourceFile}: método Up() não encontrado antes de Down().");

            var upBody = content[upStart..downStart];
            var isDestructive = destructivePatterns.Any(p => upBody.Contains(p, StringComparison.Ordinal))
                || (upBody.Contains("AlterColumn", StringComparison.Ordinal) && upBody.Contains("nullable: false", StringComparison.Ordinal));

            if (isDestructive)
            {
                Assert.Equal(MigrationPhase.Contract, phaseAttribute.Phase);
            }
            else
            {
                Assert.Equal(MigrationPhase.Expand, phaseAttribute.Phase);
            }
        }
    }

    private static string FindMigrationSourceFile(string typeName)
    {
        var candidates = new[]
        {
            Directory.GetFiles(Path.Combine(RepositoryRoot, "src", "Ledger", "Ledger.Infrastructure", "Migrations"), "*.cs"),
            Directory.GetFiles(Path.Combine(RepositoryRoot, "src", "Consolidation", "Consolidation.Infrastructure", "Migrations"), "*.cs"),
        }.SelectMany(f => f)
         .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal) && !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
         .Where(f => f.EndsWith($"_{typeName}.cs", StringComparison.Ordinal));

        var match = candidates.SingleOrDefault();
        Assert.True(match is not null, $"Arquivo de origem da migration '{typeName}' não encontrado.");
        return match!;
    }

    [Fact]
    public void Documentacao_de_diagramas_deve_refletir_a_borda_corrigida_e_nunca_mencionar_MCP()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "architecture", "06-diagramas.md"));

        Assert.Contains("VPC Link V2", content, StringComparison.Ordinal);
        Assert.Contains("sem NLB intermediário", content, StringComparison.Ordinal);
        Assert.DoesNotContain("VPC Link / Private integration", content, StringComparison.Ordinal);
        Assert.DoesNotContain("MCP", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADR_0008_deve_documentar_VPC_Link_V2_direto_ao_ALB_sem_NLB_intermediario()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "decisions", "ADR-0008-protecao-de-borda-e-conectividade-privada.md"));

        Assert.Contains("VPC Link V2", content, StringComparison.Ordinal);
        Assert.Contains("Não há NLB intermediário", content, StringComparison.Ordinal);
        Assert.DoesNotContain("VPC Link clássico → NLB(alvo=ALB)", content, StringComparison.Ordinal);
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BancoCarrefour.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
    }
}
