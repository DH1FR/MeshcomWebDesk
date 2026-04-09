<#
.SYNOPSIS
    Erstellt ein selbstsigniertes HTTPS-Zertifikat fuer den LAN-Zugriff auf MeshCom WebDesk.

.DESCRIPTION
    - Generiert ein RSA-2048-Zertifikat mit der angegebenen LAN-IP als SAN
    - Exportiert als PFX fuer Kestrel (MeshcomWebDesk\certs\meshcom-lan.pfx)
    - Exportiert als CRT fuer mobile Geraete (Android / iOS)
    - Traegt das Zertifikat im Windows-Vertrauensspeicher ein (CurrentUser\Root)

.PARAMETER LanIp
    IP-Adresse des PCs im LAN (z.B. 192.168.1.100).
    Kann weggelassen werden – das Skript erkennt die IP dann automatisch.

.PARAMETER CertPassword
    Passwort fuer die PFX-Datei (Standard: meshcom2025).
    Muss mit dem Wert in appsettings.LanHttps.json uebereinstimmen.

.EXAMPLE
    # IP automatisch ermitteln
    .\create-lan-cert.ps1

    # IP manuell angeben
    .\create-lan-cert.ps1 -LanIp 192.168.1.100
#>
param(
    [string]$LanIp       = "",
    [string]$CertPassword = "meshcom2025"
)

# Auto-detect LAN IP if not provided
if ([string]::IsNullOrWhiteSpace($LanIp)) {
    $LanIp = (Get-NetIPAddress -AddressFamily IPv4 |
              Where-Object { $_.InterfaceAlias -notmatch "Loopback|vEthernet|WSL|Hyper" -and $_.PrefixOrigin -eq "Dhcp" } |
              Select-Object -First 1).IPAddress
    if (-not $LanIp) {
        Write-Error "LAN-IP konnte nicht automatisch ermittelt werden. Bitte mit -LanIp angeben."
        exit 1
    }
    Write-Host "IP automatisch erkannt: $LanIp"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$certsDir    = Join-Path $projectRoot "MeshcomWebDesk\certs"
$pfxPath     = Join-Path $certsDir "meshcom-lan.pfx"
$crtPath     = Join-Path $certsDir "meshcom-lan.crt"

New-Item -ItemType Directory -Force -Path $certsDir | Out-Null

Write-Host ""
Write-Host "Erzeuge selbstsigniertes Zertifikat..."
Write-Host "  LAN-IP    : $LanIp"
Write-Host "  PFX       : $pfxPath"
Write-Host "  Gueltig   : $((Get-Date).ToString('dd.MM.yyyy')) bis $((Get-Date).AddYears(5).ToString('dd.MM.yyyy'))"
Write-Host ""

$cert = New-SelfSignedCertificate `
    -Subject         "CN=MeshCom WebDesk" `
    -DnsName         "localhost", "meshcom.local" `
    -IPAddress       $LanIp, "127.0.0.1" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter        (Get-Date).AddYears(5) `
    -KeyUsage        DigitalSignature, KeyEncipherment `
    -TextExtension   @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -KeyAlgorithm    RSA `
    -KeyLength       2048

# Export PFX (Kestrel)
$pwd = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwd | Out-Null

# Export CRT (mobile devices)
Export-Certificate -Cert $cert -FilePath $crtPath -Type CERT | Out-Null

# Trust on this Windows machine (CurrentUser\Root)
$rootStore = [System.Security.Cryptography.X509Certificates.X509Store]::new("Root", "CurrentUser")
$rootStore.Open("ReadWrite")
$rootStore.Add($cert)
$rootStore.Close()

Write-Host "ZERTIFIKAT ERSTELLT"
Write-Host "==================="
Write-Host ""
Write-Host "  Fingerprint : $($cert.Thumbprint)"
Write-Host "  PFX-Datei   : $pfxPath"
Write-Host "  CRT-Datei   : $crtPath"
Write-Host ""
Write-Host "Windows: Zertifikat automatisch im Vertrauensspeicher eingetragen."
Write-Host ""
Write-Host "Android / iPad / iPhone:"
Write-Host "  1. $crtPath auf das Geraet kopieren (z.B. per E-Mail oder USB)"
Write-Host "  2. Einstellungen -> Sicherheit -> Zertifikat installieren"
Write-Host "     (iOS: Einstellungen -> Allgemein -> VPN & Geraeteverwaltung)"
Write-Host "  3. Zertifikat als 'Vertrauenswuerdige Stammzertifizierungsstelle' eintragen"
Write-Host ""
Write-Host "App starten:"
Write-Host "  cd MeshcomWebDesk"
Write-Host "  dotnet run --launch-profile lan-https"
Write-Host ""
Write-Host "  HTTP  -> http://${LanIp}:5162  (weiterhin aktiv)"
Write-Host "  HTTPS -> https://${LanIp}:5163  (neu)"
