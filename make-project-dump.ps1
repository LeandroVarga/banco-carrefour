<# make-project-dump.ps1
   Gera 1 TXT na raiz com: cabeçalho, tree (ou fallback), lista de arquivos
   e conteúdo completo (sem limite). Binários são pulados; use -IncludeBinary
   para gravar base64.
#>

param(
  [string]$Output = "project-dump.txt",
  [switch]$IncludeBinary
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# ------------------ CONFIG ------------------
$ExcludeDirs = @(
  '.git','node_modules','target','build','out','dist','logs','bin','obj',
  '.idea','.vscode','.gradle','.venv','venv','coverage','.m2','.terraform',
  '.DS_Store'
)

$BinaryExt = @(
  '.png','.jpg','.jpeg','.gif','.bmp','.svg','.ico',
  '.pdf','.zip','.7z','.gz','.tar','.tgz','.rar',
  '.jar','.war','.ear','.class',
  '.exe','.dll','.so','.dylib',
  '.psd','.ai','.mp3','.wav','.mp4','.mov','.avi','.mkv','.iso',
  '.sqlite','.db','.parquet'
)

# ------------------ ROOT (FIX) ------------------
# Usa $PSScriptRoot quando disponível; senão, PSCommandPath; senão, CWD.
$Root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Root)) {
  if ($PSCommandPath) {
    $Root = Split-Path -Path $PSCommandPath -Parent
  } else {
    $Root = (Get-Location).Path
  }
}
Set-Location -LiteralPath $Root

# ------------------ OUTPUT ------------------
$OutFile = Join-Path $Root $Output
if (Test-Path -LiteralPath $OutFile) {
  Copy-Item -LiteralPath $OutFile -Destination "$OutFile.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak" -Force
}
Set-Content -LiteralPath $OutFile -Value "" -Encoding UTF8

function Write-Line([string]$s = "") { Add-Content -LiteralPath $OutFile -Value $s }
function Write-Section([string]$title) {
  Write-Line ""
  Write-Line ("".PadLeft(80,'='))
  Write-Line ("=  $title")
  Write-Line ("".PadLeft(80,'='))
}

# ------------------ FILTERS ------------------
$dirsPattern  = ($ExcludeDirs | ForEach-Object { [regex]::Escape($_) }) -join '|'
$excludeRegex = [regex]::new("[\\/](?:$dirsPattern)[\\/]", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

function ShouldSkipPath([string]$fullPath) { return $excludeRegex.IsMatch($fullPath) }
function IsBinaryByExt([string]$path) {
  $ext = [System.IO.Path]::GetExtension($path)
  if ([string]::IsNullOrEmpty($ext)) { return $false }
  return ($BinaryExt -contains $ext.ToLowerInvariant())
}

# ------------------ HEADER ------------------
Write-Section "PROJECT DUMP — HEADER"
Write-Line ("Timestamp       : {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))
Write-Line ("Root            : {0}" -f $Root)
Write-Line ("PowerShell      : {0}" -f $PSVersionTable.PSVersion)
Write-Line ("OS              : {0}" -f [System.Environment]::OSVersion.VersionString)
Write-Line ("IncludeBinary   : {0}" -f ($IncludeBinary.IsPresent))

try {
  $null = Get-Command git -ErrorAction Stop
  $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
  $commit = (git rev-parse --short HEAD 2>$null)
  $remote = (git remote -v 2>$null | Select-Object -First 1)
  if ($branch) { Write-Line ("Git Branch      : {0}" -f $branch.Trim()) }
  if ($commit) { Write-Line ("Git Commit      : {0}" -f $commit.Trim()) }
  if ($remote) { Write-Line ("Git Remote      : {0}" -f $remote.Trim()) }
} catch { Write-Line "Git             : not available" }

# ------------------ TREE ------------------
Write-Section "TREE (ASCII)"
$treeOk = $false
try {
  if ($env:ComSpec) {
    $tree = & $env:ComSpec /c "tree /F /A" 2>$null
    if ($LASTEXITCODE -eq 0 -and $tree) {
      $tree | Add-Content -LiteralPath $OutFile -Encoding UTF8
      $treeOk = $true
    }
  }
} catch { }
if (-not $treeOk) {
  Write-Line "[fallback] 'tree' indisponível; listando caminhos relativos:"
  Get-ChildItem -Recurse -Force | ForEach-Object {
    Write-Line ($_.FullName.Substring($Root.Length).TrimStart('\','/'))
  }
}

# ------------------ FILE LIST ------------------
Write-Section "FILE LIST (filtered)"
$allFiles = Get-ChildItem -Recurse -Force -File | Where-Object { -not (ShouldSkipPath $_.FullName) }
$relFiles = $allFiles | ForEach-Object {
  $_ | Add-Member -NotePropertyName RelPath -NotePropertyValue ($_.FullName.Substring($Root.Length).TrimStart('\','/')) -PassThru
}
$relFiles | Sort-Object RelPath | ForEach-Object { Write-Line $_.RelPath }
Write-Line ""
Write-Line ("Total files (after filters): {0}" -f $relFiles.Count)

# ------------------ CONTENTS ------------------
Write-Section "FILE CONTENTS"
foreach ($f in ($relFiles | Sort-Object RelPath)) {
  $full = $f.FullName
  $rel  = $f.RelPath

  Write-Line ("-----8<----- FILE: {0}" -f $rel)
  Write-Line ("Size: {0} bytes" -f $f.Length)
  try { Write-Line ("SHA256: {0}" -f (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash) }
  catch { Write-Line ("SHA256: <unavailable> ({0})" -f $_.Exception.Message) }

  $isBin = IsBinaryByExt $full
  if ($isBin -and -not $IncludeBinary) {
    Write-Line "[content skipped] Reason: binary extension"
  } else {
    try {
      if ($isBin -and $IncludeBinary) {
        Write-Line "<<<BEGIN BINARY BASE64"
        [byte[]]$bytes = [System.IO.File]::ReadAllBytes($full)
        [Convert]::ToBase64String($bytes) -split "(.{1,120})" | Where-Object { $_ } | ForEach-Object { Write-Line $_ }
        Write-Line "END BINARY BASE64"
      } else {
        Write-Line "<<<BEGIN CONTENT"
        $content = Get-Content -LiteralPath $full -Raw -ErrorAction Stop
        $content = $content -replace "`r`n","`n"
        Write-Line $content
        Write-Line "END CONTENT"
      }
    } catch {
      Write-Line ("[content skipped] Could not read file: {0}" -f $_.Exception.Message)
    }
  }
  Write-Line ("-----8<----- END FILE: {0}" -f $rel)
  Write-Line ""
}

# ------------------ SUMMARY ------------------
Write-Section "SUMMARY"
Write-Line ("Files listed : {0}" -f $relFiles.Count)
Write-Line ("Output file  : {0}" -f $OutFile)
Write-Line "Done."
