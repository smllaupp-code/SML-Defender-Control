<#
.SYNOPSIS
  Assina o SML Defender Control com um certificado de assinatura de codigo.

.DESCRIPTION
  Na primeira execucao, cria um certificado SELF-SIGNED "SML" (CodeSigning) no
  repositorio do usuario (CurrentUser\My), com 5 anos de validade. Assina o .exe com
  Authenticode + carimbo de tempo (timestamp) e confia no certificado NESTA maquina
  (Trusted Root + Trusted Publishers) para que o UAC mostre o publicador VERIFICADO "SML".

  IMPORTANTE (honestidade tecnica):
  - Um certificado self-signed so e confiavel nas maquinas onde ele foi instalado como
    confiavel. Em OUTROS computadores o app ainda aparece como "editor desconhecido".
  - Para confianca universal (remover o aviso em qualquer PC e ganhar reputacao no
    SmartScreen), use um certificado de uma Autoridade Certificadora (OV ou EV) e troque
    o bloco de assinatura para usar esse certificado (mesmo Set-AuthenticodeSignature).

.NOTES
  Requer execucao como usuario atual (nao precisa de admin para CurrentUser; precisa de
  admin se voce quiser instalar em LocalMachine - aqui usamos CurrentUser).
#>

[CmdletBinding()]
param(
    [string]$Subject     = "CN=SML",
    [string]$FriendlyName = "SML Defender Control - Code Signing",
    [string]$ExePath,
    [string]$TimeStampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = 'Stop'

function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

# Raiz do script, robusta a diferentes formas de invocacao ($PSScriptRoot pode vir vazio).
$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $ExePath) { $ExePath = Join-Path $root 'src\PainelSeguranca\bin\Release\SMLDefenderControl.exe' }

if (-not (Test-Path $ExePath)) {
    throw "Executavel nao encontrado: $ExePath`nCompile em Release antes de assinar."
}

# 1) Encontra (ou cria) o certificado de assinatura de codigo "SML".
Write-Step "Procurando certificado de assinatura de codigo '$Subject'..."
# Casa pelo OID de Code Signing (1.3.6.1.5.5.7.3.3) - universal, independe do idioma do
# Windows (o FriendlyName e localizado, ex.: "Assinatura de Codigo" em PT-BR).
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3') } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if (-not $cert) {
    Write-Step "Nenhum encontrado. Criando certificado self-signed (valido por 5 anos)..."
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -FriendlyName $FriendlyName `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyExportPolicy Exportable `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "    Criado. Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host "    Reaproveitando. Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
}

# 2) Confia no certificado NESTA maquina (Trusted Root + Trusted Publishers do usuario).
#    Assim a cadeia fica valida e o UAC mostra o publicador verificado aqui.
Write-Step "Instalando o certificado como CONFIAVEL nesta maquina (CurrentUser)..."
$publicCer = Join-Path $root 'signing\SML-CodeSigning.cer'
New-Item -ItemType Directory -Force -Path (Split-Path $publicCer) | Out-Null
Export-Certificate -Cert $cert -FilePath $publicCer -Force | Out-Null

foreach ($store in @('Root','TrustedPublisher')) {
    $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($store,'CurrentUser')
    $s.Open('ReadWrite')
    # Importa apenas a parte publica (sem chave privada).
    $pub = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($publicCer)
    if (-not ($s.Certificates | Where-Object Thumbprint -eq $cert.Thumbprint)) {
        $s.Add($pub)
        Write-Host "    Adicionado em CurrentUser\$store" -ForegroundColor Green
    } else {
        Write-Host "    Ja presente em CurrentUser\$store" -ForegroundColor DarkGray
    }
    $s.Close()
}

# 3) Assina o .exe (Authenticode SHA-256) com carimbo de tempo.
Write-Step "Assinando: $ExePath"
$sig = Set-AuthenticodeSignature -FilePath $ExePath -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer $TimeStampUrl

Write-Host ""
$statusColor = 'Yellow'; if ($sig.Status -eq 'Valid') { $statusColor = 'Green' }
Write-Host "Status da assinatura: $($sig.Status)" -ForegroundColor $statusColor
Write-Host "Signatario:           $($sig.SignerCertificate.Subject)"
if ($sig.TimeStamperCertificate) { Write-Host "Carimbo de tempo:     $($sig.TimeStamperCertificate.Subject)" }
Write-Host ""
Write-Host "Pronto. Neste PC o UAC mostrara o publicador verificado 'SML'." -ForegroundColor Cyan
Write-Host "Em outros PCs, use um certificado de uma CA (OV/EV) para remover o 'editor desconhecido'." -ForegroundColor DarkGray
