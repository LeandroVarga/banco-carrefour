#!/usr/bin/env pwsh
# Wrapper Windows do bootstrap local de seguranca.
# Nao duplica regras de geracao: invoca o MESMO container/imagem que o
# wrapper .sh, executando a mesma logica em bootstrap-local-security-impl.sh.
# Diferenca exclusiva deste wrapper: aplica ACL restrita ao usuario atual nos
# artefatos gerados, ja que chmod dentro do container nao reflete em ACL NTFS
# quando o volume e um bind mount para um host Windows.
#
# Uso: pwsh scripts/security/bootstrap-local-security.ps1 [-Force]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$BootstrapImage = "alpine@sha256:1e42bbe2508154c9126d48c2b8a75420c3544343bf86fd041fb7527e017a4b4a"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..\..")

$implArgs = ""
if ($Force) { $implArgs = "--force" }

docker run --rm `
    -v "${RepoRoot}:/workspace" `
    -w /workspace `
    $BootstrapImage `
    sh -c "apk add --no-cache openssl >/dev/null && sh /workspace/scripts/security/bootstrap-local-security-impl.sh $implArgs"

if ($LASTEXITCODE -ne 0) {
    throw "bootstrap-local-security: o container de bootstrap falhou (exit code $LASTEXITCODE)."
}

function Protect-FileAcl {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    try {
        $acl = Get-Acl $Path
        $acl.SetAccessRuleProtection($true, $false)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
            "FullControl",
            "Allow")
        $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
        $acl.AddAccessRule($rule)
        Set-Acl -Path $Path -AclObject $acl
    }
    catch {
        throw "bootstrap-local-security: nao foi possivel restringir a ACL de '$Path' ($($_.Exception.Message)) — abortando em vez de deixar o arquivo com permissao ampla."
    }
}

$envFile = Join-Path $RepoRoot ".env"
$certsDir = Join-Path $RepoRoot ".local\security\certs"
$secretsEnvFile = Join-Path $RepoRoot ".local\security\.env.security"

Protect-FileAcl -Path $envFile
Protect-FileAcl -Path $secretsEnvFile
if (Test-Path $certsDir) {
    # ca.key nunca e montada em nenhum container - pode ficar restrita ao
    # usuario atual. edge.key precisa ser lida pelo processo do edge-proxy
    # (containers com UID diferente do host) e por isso fica com permissao
    # 644 aplicada pelo proprio bootstrap containerizado - a fronteira de
    # protecao real e o diretorio .local/security/ (gitignored), nao o bit
    # de permissao individual desse arquivo.
    Protect-FileAcl -Path (Join-Path $certsDir "ca.key")
}

Write-Host "bootstrap-local-security: concluido. ACL restrita ao usuario atual aplicada aos artefatos locais."
