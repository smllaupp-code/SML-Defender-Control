<#
.SYNOPSIS
  Compila o SML Defender Control em Release, gera o instalador (Inno Setup) e assina o setup.exe.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Definition }

# 1) Compila o app em Release.
Write-Step "Compilando o app (Release)..."
$msbuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $vsw = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vsw) { $msbuild = & $vsw -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1 }
}
& $msbuild (Join-Path $root 'PainelSeguranca.sln') /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar o app." }

# 2) Assina o .exe do app (reaproveita/cria o certificado).
Write-Step "Assinando o app..."
& (Join-Path $root 'sign.ps1')

# 3) Gera o instalador com o Inno Setup.
Write-Step "Gerando o instalador (Inno Setup)..."
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup) nao encontrado. Instale o Inno Setup 6." }
& $iscc (Join-Path $root 'installer\installer.iss')
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o instalador." }

# 4) Assina o instalador gerado com o mesmo certificado do app.
Write-Step "Assinando o instalador..."
$setup = Get-ChildItem (Join-Path $root 'dist') -Filter 'SMLDefenderControl-Setup-*.exe' |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw "Instalador nao encontrado em dist\." }

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=SML' -and ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3') } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
$sig = Set-AuthenticodeSignature -FilePath $setup.FullName -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'

Write-Host ""
Write-Host "Instalador: $($setup.FullName)" -ForegroundColor Green
Write-Host "Tamanho:    $([math]::Round($setup.Length/1KB)) KB"
Write-Host "Assinatura: $($sig.Status)" -ForegroundColor (@{$true='Green';$false='Yellow'}[$sig.Status -eq 'Valid'])
