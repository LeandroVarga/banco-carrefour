#!/bin/sh
# Registra uma NOVA revisao da task definition de migracao (family ja
# materializada de forma estavel pelo Terraform - modulo
# infra/terraform/modules/ecs-migration-task, "aws_ecs_task_definition.this"
# com "lifecycle.ignore_changes = [container_definitions]") diretamente via
# API ECS, substituindo apenas a imagem pelo digest da release, e SO ENTAO
# executa a task one-off de migracao (comando "migrate", nunca "contract")
# para UMA fronteira (Ledger ou Consolidation), aguarda a conclusao e
# verifica o exit code real do container.
#
# Este script existe para eliminar o uso de "terraform apply -target" nos
# deploys de rotina: o Terraform so materializa infraestrutura ESTAVEL
# (cluster, subnets, security group, IAM roles, log group, a EXISTENCIA da
# family) - a imagem de CADA release e registrada aqui, como uma nova
# revisao da mesma family, via "aws ecs register-task-definition". Os
# demais parametros (family, roles, variaveis de ambiente, log group, cpu,
# memoria, storage efemero, usuario nao-root) vem do JSON de
# "terraform output -json migration_task_render_params_<boundary>" - os
# MESMOS valores que o Terraform usou para a revisao de bootstrap, nunca
# divergentes, garantindo que a revisao registrada aqui seja estruturalmente
# identica ao que a infraestrutura estavel espera, exceto pela imagem.
#
# Chamado pelos 3 workflows de deploy AWS (deploy-development.yml,
# promote-staging.yml, promote-production.yml) ANTES de qualquer
# atualizacao dos 4 servicos de aplicacao - uma falha aqui interrompe o
# deploy imediatamente (nunca atualiza os servicos de aplicacao com uma
# migration pendente/falha).
#
# Nunca invoca o comando "contract" (procedimento separado e protegido,
# fora do escopo de um deploy automatico).
#
# Uso:
#   sh scripts/ci/run-migration-task.sh \
#     --cluster <nome-ou-arn-do-cluster> \
#     --render-params-json <json-de-'terraform output -json migration_task_render_params_<boundary>'> \
#     --image <referencia-com-digest-do-migration-runner> \
#     --boundary Ledger|Consolidation \
#     --subnets subnet-a,subnet-b \
#     --security-group sg-xxxx \
#     --secret-name banco-carrefour/<env>/<boundary>-migration/db-credentials \
#     --release-id <release-id> \
#     --source-commit <sha> \
#     --lock-timeout-seconds 300 \
#     --evidence-file <caminho-opcional-para-registrar-evidencia-json>
set -eu

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

fail() { echo "run-migration-task: FALHA: $1" >&2; exit 1; }
log() { printf '%s\n' "$1"; }

CLUSTER=""
RENDER_PARAMS_JSON=""
IMAGE=""
BOUNDARY=""
SUBNETS=""
SECURITY_GROUP=""
SECRET_NAME=""
RELEASE_ID="unknown"
SOURCE_COMMIT="unknown"
LOCK_TIMEOUT_SECONDS=300
EVIDENCE_FILE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --cluster) CLUSTER="$2"; shift 2 ;;
    --render-params-json) RENDER_PARAMS_JSON="$2"; shift 2 ;;
    --image) IMAGE="$2"; shift 2 ;;
    --boundary) BOUNDARY="$2"; shift 2 ;;
    --subnets) SUBNETS="$2"; shift 2 ;;
    --security-group) SECURITY_GROUP="$2"; shift 2 ;;
    --secret-name) SECRET_NAME="$2"; shift 2 ;;
    --release-id) RELEASE_ID="$2"; shift 2 ;;
    --source-commit) SOURCE_COMMIT="$2"; shift 2 ;;
    --lock-timeout-seconds) LOCK_TIMEOUT_SECONDS="$2"; shift 2 ;;
    --evidence-file) EVIDENCE_FILE="$2"; shift 2 ;;
    *) fail "argumento desconhecido: $1" ;;
  esac
done

[ -n "$CLUSTER" ] || fail "--cluster e obrigatorio."
[ -n "$RENDER_PARAMS_JSON" ] || fail "--render-params-json e obrigatorio."
[ -n "$IMAGE" ] || fail "--image e obrigatorio."
[ -n "$SUBNETS" ] || fail "--subnets e obrigatorio."
[ -n "$SECURITY_GROUP" ] || fail "--security-group e obrigatorio."
[ -n "$SECRET_NAME" ] || fail "--secret-name e obrigatorio."

case "$BOUNDARY" in
  Ledger|Consolidation) ;;
  *) fail "--boundary deve ser 'Ledger' ou 'Consolidation' - recebido: '${BOUNDARY}'." ;;
esac

# A imagem precisa ser qualificada por digest (nunca uma tag mutavel como
# "latest") - a mesma garantia ja exigida do manifesto de release
# (schemas/release-manifest.schema.json, "digestQualifiedReference").
case "$IMAGE" in
  *@sha256:*) ;;
  *) fail "--image deve ser uma referencia qualificada por digest ('repo@sha256:...') - recebida: '${IMAGE}'." ;;
esac

log "=== Migracao (${BOUNDARY}) - registrando nova revisao da task definition (release=${RELEASE_ID}, commit=${SOURCE_COMMIT}) ==="

TASK_DEF_JSON=$(RENDER_PARAMS_JSON="$RENDER_PARAMS_JSON" IMAGE="$IMAGE" BOUNDARY="$BOUNDARY" python3 -c "
import json, os

# JSON e imagem passados via variavel de ambiente (nunca interpolados no
# texto do script Python) para nao quebrar com aspas simples/caracteres
# especiais que possam aparecer em valores de environment_variables/secrets.
params = json.loads(os.environ['RENDER_PARAMS_JSON'])
image = os.environ['IMAGE']
boundary_lower = os.environ['BOUNDARY'].lower()

container = {
    'name': 'migration-runner',
    'image': image,
    'essential': True,
    'user': params['nonRootUser'],
    'readonlyRootFilesystem': True,
    'linuxParameters': {'capabilities': {'drop': ['ALL']}},
    'environment': [
        {'name': k, 'value': v} for k, v in params['environmentVariables'].items()
    ],
    'secrets': [
        {'name': k, 'valueFrom': v} for k, v in params['secrets'].items()
    ],
    'logConfiguration': {
        'logDriver': 'awslogs',
        'options': {
            'awslogs-group': params['logGroupName'],
            'awslogs-region': params['awsRegion'],
            'awslogs-stream-prefix': f'migration-{boundary_lower}',
        },
    },
}

task_def = {
    'family': params['family'],
    'requiresCompatibilities': ['FARGATE'],
    'networkMode': 'awsvpc',
    'cpu': str(params['cpu']),
    'memory': str(params['memory']),
    'executionRoleArn': params['taskExecutionRoleArn'],
    'taskRoleArn': params['taskRoleArn'],
    'runtimePlatform': {'operatingSystemFamily': 'LINUX', 'cpuArchitecture': 'X86_64'},
    'ephemeralStorage': {'sizeInGiB': params['ephemeralStorageGib']},
    'containerDefinitions': [container],
}

print(json.dumps(task_def))
")

REGISTER_OUTPUT=$(aws ecs register-task-definition --cli-input-json "$TASK_DEF_JSON" --output json)
TASK_DEF_ARN=$(printf '%s' "$REGISTER_OUTPUT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['taskDefinition']['taskDefinitionArn'])")
TASK_DEF_REVISION=$(printf '%s' "$REGISTER_OUTPUT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['taskDefinition']['revision'])")

log "Revisao registrada (${BOUNDARY}): ${TASK_DEF_ARN} - verificando o digest registrado..."

# Verifica que o ECS registrou EXATAMENTE a imagem pedida (protege contra
# erro de construcao do JSON acima ou qualquer reescrita inesperada da API)
# - "verifica o digest da imagem registrada".
DESCRIBE_TASKDEF_OUTPUT=$(aws ecs describe-task-definition --task-definition "$TASK_DEF_ARN" --output json)
REGISTERED_IMAGE=$(printf '%s' "$DESCRIBE_TASKDEF_OUTPUT" | python3 -c "
import json, sys
d = json.load(sys.stdin)
container = next(c for c in d['taskDefinition']['containerDefinitions'] if c['name'] == 'migration-runner')
print(container['image'])
")

if [ "$REGISTERED_IMAGE" != "$IMAGE" ]; then
  fail "a imagem registrada (${REGISTERED_IMAGE}) diverge da imagem pedida (${IMAGE}) - revisao ${TASK_DEF_ARN} nao sera executada."
fi

log "Digest confirmado (${BOUNDARY}): ${REGISTERED_IMAGE} - iniciando task one-off..."

OVERRIDES=$(python3 -c "
import json
print(json.dumps({
    'containerOverrides': [{
        'name': 'migration-runner',
        'command': [
            'migrate',
            '--boundary', '${BOUNDARY}',
            '--secret-name', '${SECRET_NAME}',
            '--release-id', '${RELEASE_ID}',
            '--source-commit', '${SOURCE_COMMIT}',
            '--lock-timeout-seconds', '${LOCK_TIMEOUT_SECONDS}',
        ],
    }],
}))
")

RUN_TASK_OUTPUT=$(aws ecs run-task \
  --cluster "$CLUSTER" \
  --task-definition "$TASK_DEF_ARN" \
  --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[${SUBNETS}],securityGroups=[${SECURITY_GROUP}],assignPublicIp=DISABLED}" \
  --overrides "$OVERRIDES" \
  --count 1 \
  --output json)

TASK_ARN=$(printf '%s' "$RUN_TASK_OUTPUT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['tasks'][0]['taskArn'])")
FAILURES=$(printf '%s' "$RUN_TASK_OUTPUT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(len(d.get('failures', [])))")

if [ "$FAILURES" != "0" ]; then
  echo "$RUN_TASK_OUTPUT" | python3 -m json.tool >&2
  fail "'aws ecs run-task' reportou falha(s) ao iniciar a task (${BOUNDARY}) - ver detalhes acima."
fi

log "Task iniciada (${BOUNDARY}): ${TASK_ARN} - aguardando conclusao..."

aws ecs wait tasks-stopped --cluster "$CLUSTER" --tasks "$TASK_ARN"

DESCRIBE_OUTPUT=$(aws ecs describe-tasks --cluster "$CLUSTER" --tasks "$TASK_ARN" --output json)
EXIT_CODE=$(printf '%s' "$DESCRIBE_OUTPUT" | python3 -c "
import json, sys
d = json.load(sys.stdin)
container = next(c for c in d['tasks'][0]['containers'] if c['name'] == 'migration-runner')
print(container.get('exitCode', 'null'))
")
STOPPED_REASON=$(printf '%s' "$DESCRIBE_OUTPUT" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['tasks'][0].get('stoppedReason', ''))")

if [ -n "$EVIDENCE_FILE" ]; then
  EVIDENCE_FILE="$EVIDENCE_FILE" TASK_DEF_ARN="$TASK_DEF_ARN" TASK_DEF_REVISION="$TASK_DEF_REVISION" \
    TASK_ARN="$TASK_ARN" IMAGE="$IMAGE" BOUNDARY="$BOUNDARY" RELEASE_ID="$RELEASE_ID" \
    SOURCE_COMMIT="$SOURCE_COMMIT" EXIT_CODE="$EXIT_CODE" python3 -c "
import json, os

evidence = {
    'boundary': os.environ['BOUNDARY'],
    'releaseId': os.environ['RELEASE_ID'],
    'sourceCommit': os.environ['SOURCE_COMMIT'],
    'taskDefinitionArn': os.environ['TASK_DEF_ARN'],
    'image': os.environ['IMAGE'],
    'taskArn': os.environ['TASK_ARN'],
    'exitCode': os.environ['EXIT_CODE'],
}
with open(os.environ['EVIDENCE_FILE'], 'w', encoding='utf-8') as f:
    json.dump(evidence, f, indent=2, ensure_ascii=False)
"
fi

if [ "$EXIT_CODE" != "0" ]; then
  echo "run-migration-task: exitCode=${EXIT_CODE} stoppedReason=${STOPPED_REASON}" >&2
  echo "$DESCRIBE_OUTPUT" | python3 -m json.tool >&2
  fail "task de migracao (${BOUNDARY}) terminou com exitCode=${EXIT_CODE} - deploy da aplicacao BLOQUEADO. Nunca sera tentado rollback destrutivo de banco automaticamente - ver docs/operations/runbook-implantacao-aws.md."
fi

log "=== Migracao (${BOUNDARY}) concluida com sucesso (exitCode=0) - revisao: ${TASK_DEF_ARN} (rev ${TASK_DEF_REVISION}), task: ${TASK_ARN} ==="
