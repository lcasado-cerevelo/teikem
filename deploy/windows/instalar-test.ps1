<#
  Instalador del AMBIENTE DE TEST de Teikem (2026-10-19): todo lo que hay en master, en el mismo servidor Windows donde ya viven producción y
  staging, sin tocarlos. Se corre en el SERVIDOR, como Administrador, desde la carpeta del paquete descomprimido (teikem-test-*.zip):
      powershell -ExecutionPolicy Bypass -File .\instalar-test.ps1
  La primera vez pregunta solo los datos de la base de datos (servidor, usuario y contraseña; la base se llama Teikem_Test) y el correo para
  Let's Encrypt. Para actualizar: el mismo comando con el paquete nuevo (conserve la configuración).
  Deja:
    1) https://test.advancelogisticspr.com          sitio IIS «TeikemTest» en C:\inetpub\teikem-test: API + web (cinta roja TEST en todas las
                                                    pantallas y etiqueta al pie, igual que staging), base Teikem_Test al día (db-update), y
                                                    https://test.advancelogisticspr.com/descargas/  la página para bajar los APK de test.
    2) https://test-portal.advancelogisticspr.com   sitio IIS «TeikemTestPortal» en C:\inetpub\teikem-test-portal: el portal del cliente (web
                                                    estática compilada apuntando al API de test, con la misma cinta TEST).
    3) Certificados de Let's Encrypt de los dos nombres con win-acme (se descarga si falta) y su tarea programada de renovación automática;
       redirección de http a https.
  Requisitos: el DNS de test.advancelogisticspr.com y test-portal.advancelogisticspr.com apunta a este servidor y el puerto 80 llega desde Internet.
    4) Correo: TODO lo que salga de test (clientes, consignatarios, staff) va al buzón teikem@cerevelo.com (con dirección «más» por destinatario:
       teikem+cliente_suempresa.com@cerevelo.com, y «[TEST → quien@era.com]» en el asunto); nadie real recibe nada. Excepción: los correos
       a @cerevelo.com llegan a cada quien (códigos de MFA y primer ingreso del equipo). Cámbielo con -CorreoRedirigir y -CorreoRedirigirExcepto.
  Opciones: -SinBase, -RespaldoHecho (igual que instalar.ps1), -CorreoLetsEncrypt <correo>, -SinPortal (no instala el portal).
#>
param(
    [string]$Dominio = 'test.advancelogisticspr.com',
    [string]$DominioPortal = 'test-portal.advancelogisticspr.com',
    [string]$Sitio = 'TeikemTest',
    [string]$Carpeta = 'C:\inetpub\teikem-test',
    [string]$SitioPortal = 'TeikemTestPortal',
    [string]$CarpetaPortal = 'C:\inetpub\teikem-test-portal',
    [string]$DbNombre = 'Teikem_Test',
    [string]$CorreoLetsEncrypt = '',
    [string]$CorreoRedirigir = 'teikem@cerevelo.com',
    [string[]]$CorreoRedirigirExcepto = @('cerevelo.com'),
    [switch]$SinBase,
    [switch]$RespaldoHecho,
    [switch]$SinPortal
)

$ErrorActionPreference = 'Stop'
function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Ok([string]$m) { Write-Host $m -ForegroundColor Green }
function Aviso([string]$m) { Write-Host $m -ForegroundColor Yellow }
function Fallar([string]$m) { Write-Host ''; Write-Host $m -ForegroundColor Red; exit 1 }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Fallar 'Corra este script como Administrador (clic derecho en PowerShell > Ejecutar como administrador).' }
if ($Sitio -eq 'Teikem' -or $SitioPortal -eq 'Teikem' -or $Carpeta -eq 'C:\inetpub\teikem') { Fallar 'Este instalador es solo para TEST: no puede usar el sitio ni la carpeta de producción.' }
foreach ($f in 'instalar.ps1', 'letsencrypt.ps1') { if (-not (Test-Path (Join-Path $PSScriptRoot $f))) { Fallar "Falta $f junto a este script (viene en el paquete)." } }
. (Join-Path $PSScriptRoot 'letsencrypt.ps1')

# El correo de Let's Encrypt se recuerda en la carpeta del sitio para no pedirlo en cada actualización.
$recuerdo = Join-Path $Carpeta 'letsencrypt-correo.txt'
if (-not $CorreoLetsEncrypt -and (Test-Path $recuerdo)) { $CorreoLetsEncrypt = (Get-Content $recuerdo -Raw).Trim() }
if (-not $CorreoLetsEncrypt) { $CorreoLetsEncrypt = (Read-Host "Correo para la cuenta de Let's Encrypt (avisos si no pudiera renovar)").Trim() }
if (-not $CorreoLetsEncrypt) { Fallar "Hace falta un correo para Let's Encrypt." }

Write-Host ''
Write-Host 'AMBIENTE DE TEST' -ForegroundColor Red
Write-Host "  API + web:  https://$Dominio   (sitio $Sitio, $Carpeta, base $DbNombre)"
if (-not $SinPortal) { Write-Host "  Portal:     https://$DominioPortal   (sitio $SitioPortal, $CarpetaPortal)" }
Write-Host "  Descargas:  https://$Dominio/descargas/"
Write-Host "  Correo:     todo a $CorreoRedirigir$(if ($CorreoRedirigirExcepto.Count) { " (salvo @$($CorreoRedirigirExcepto -join ', @'))" }); nadie real recibe"

# --------------------------------------------------------------------------------------------------------- 1. API + web + base
Paso "1/3 API, web y base de datos ($Sitio)"
$args_ = @('-Sitio', $Sitio, '-Carpeta', $Carpeta, '-Dominio', $Dominio, '-DbNombre', $DbNombre, '-Etiqueta', 'TEST',
    '-LetsEncrypt', '-CorreoLetsEncrypt', $CorreoLetsEncrypt, '-ConDescargas')
if ($CorreoRedirigir) { $args_ += @('-CorreoRedirigir', $CorreoRedirigir); if ($CorreoRedirigirExcepto.Count) { $args_ += @('-CorreoRedirigirExcepto', ($CorreoRedirigirExcepto -join ',')) } }
if (-not $SinPortal) { $args_ += @('-OrigenesExtra', "https://$DominioPortal") }
if ($SinBase) { $args_ += '-SinBase' }
if ($RespaldoHecho) { $args_ += '-RespaldoHecho' }
& (Join-Path $PSScriptRoot 'instalar.ps1') @args_
if ($LASTEXITCODE -ne 0) { Fallar "La instalación del API y la web falló (arriba). El portal no se tocó." }
New-Item -ItemType Directory -Force $Carpeta | Out-Null
Set-Content $recuerdo $CorreoLetsEncrypt -Encoding ASCII

if ($SinPortal) { Write-Host ''; Ok "Listo (sin portal). Abra: https://$Dominio"; exit 0 }

# --------------------------------------------------------------------------------------------------------- 2. Portal del cliente
Paso "2/3 Portal del cliente ($SitioPortal)"
Import-Module WebAdministration
$origenPortal = Join-Path $PSScriptRoot 'portal'
if (-not (Test-Path (Join-Path $origenPortal 'index.html'))) { Fallar "El paquete no trae la carpeta 'portal' (la web del portal del cliente). Cree el paquete con crear-paquete-windows.ps1 -Test." }

# Módulo URL Rewrite: el portal es una aplicación de una sola página (todas las rutas sirven index.html). Si falta, se instala (Microsoft).
function Tiene-Rewrite { return [bool](Get-WebGlobalModule -Name RewriteModule -ErrorAction SilentlyContinue) }
if (-not (Tiene-Rewrite)) {
    Aviso 'Falta el módulo URL Rewrite de IIS: lo descargo e instalo (Microsoft).'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $msi = Join-Path $env:TEMP 'rewrite_amd64_en-US.msi'
        Invoke-WebRequest 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' -OutFile $msi -UseBasicParsing
        $p = Start-Process msiexec.exe -ArgumentList '/i', "`"$msi`"", '/quiet', '/norestart' -Wait -PassThru
        if ($p.ExitCode -notin 0, 3010) { throw "msiexec terminó con código $($p.ExitCode)" }
        Import-Module WebAdministration -Force
    } catch { Aviso "No pude instalar URL Rewrite ($($_.Exception.Message)); el portal usará la página de error 404 para abrir index.html." }
}
$conRewrite = Tiene-Rewrite

if (-not (Test-Path "IIS:\AppPools\$SitioPortal")) { New-WebAppPool -Name $SitioPortal | Out-Null }
Set-ItemProperty "IIS:\AppPools\$SitioPortal" managedRuntimeVersion ''
New-Item -ItemType Directory -Force $CarpetaPortal | Out-Null
robocopy $origenPortal $CarpetaPortal /MIR /XF web.config /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fallar "robocopy (portal) falló (código $LASTEXITCODE)." }

# web.config del portal: SPA (toda ruta que no sea un archivo → index.html), index.html sin caché (los archivos de _expo llevan hash), tipos MIME que
# IIS no trae, y redirección http → https cuando hay URL Rewrite (y ya hay certificado).
$tieneHttpsPortal = [bool](Get-WebBinding -Name $SitioPortal -Protocol https -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -like "*:443:$DominioPortal" })
function Escribir-WebConfigPortal([bool]$redirigir) {
    $reglas = ''
    if ($conRewrite) {
        $redir = if ($redirigir) { @"
        <rule name="http a https" stopProcessing="true">
          <match url="(.*)" />
          <conditions><add input="{HTTPS}" pattern="off" ignoreCase="true" /><add input="{REQUEST_URI}" pattern="^/\.well-known/acme-challenge/" negate="true" /></conditions>
          <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
        </rule>
"@ } else { '' }
        $reglas = @"
    <rewrite>
      <rules>
$redir        <rule name="Aplicación de una sola página" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
            <add input="{REQUEST_URI}" pattern="^/\.well-known/" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
"@
    } else {
        $reglas = @"
    <httpErrors errorMode="Custom" existingResponse="Replace">
      <remove statusCode="404" subStatusCode="-1" />
      <error statusCode="404" path="/index.html" responseMode="ExecuteURL" />
    </httpErrors>
"@
    }
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <defaultDocument><files><clear /><add value="index.html" /></files></defaultDocument>
    <staticContent>
      <remove fileExtension=".json" /><mimeMap fileExtension=".json" mimeType="application/json" />
      <remove fileExtension=".webmanifest" /><mimeMap fileExtension=".webmanifest" mimeType="application/manifest+json" />
      <remove fileExtension=".woff2" /><mimeMap fileExtension=".woff2" mimeType="font/woff2" />
      <remove fileExtension=".wasm" /><mimeMap fileExtension=".wasm" mimeType="application/wasm" />
      <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="365.00:00:00" />
    </staticContent>
    <httpProtocol><customHeaders><add name="X-Content-Type-Options" value="nosniff" /></customHeaders></httpProtocol>
$reglas  </system.webServer>
  <location path="index.html">
    <system.webServer><staticContent><clientCache cacheControlMode="DisableCache" /></staticContent></system.webServer>
  </location>
</configuration>
"@
    [IO.File]::WriteAllText((Join-Path $CarpetaPortal 'web.config'), $xml, (New-Object Text.UTF8Encoding $false))
}
Escribir-WebConfigPortal $tieneHttpsPortal

& icacls $CarpetaPortal /grant "IIS AppPool\${SitioPortal}:(OI)(CI)RX" /T /Q 2>&1 | Out-Null
if (-not (Get-Website -Name $SitioPortal -ErrorAction SilentlyContinue)) {
    New-Website -Name $SitioPortal -PhysicalPath $CarpetaPortal -ApplicationPool $SitioPortal -HostHeader $DominioPortal -Port 80 | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SitioPortal" physicalPath $CarpetaPortal
    Set-ItemProperty "IIS:\Sites\$SitioPortal" applicationPool $SitioPortal
}
if (-not (Get-WebBinding -Name $SitioPortal -Protocol http | Where-Object { $_.bindingInformation -like "*:80:$DominioPortal" })) {
    New-WebBinding -Name $SitioPortal -Protocol http -Port 80 -HostHeader $DominioPortal | Out-Null
}
if ((Get-WebAppPoolState $SitioPortal).Value -ne 'Started') { Start-WebAppPool $SitioPortal }
if ((Get-WebsiteState $SitioPortal).Value -ne 'Started') { Start-Website $SitioPortal }
Ok "Portal copiado y sitio '$SitioPortal' arriba (http://$DominioPortal)."

# --------------------------------------------------------------------------------------------------------- 3. Certificado del portal
Paso "3/3 Certificado de $DominioPortal"
$tieneHttpsPortal = Asegurar-CertificadoLetsEncrypt -Sitio $SitioPortal -Dominio $DominioPortal -Correo $CorreoLetsEncrypt
if ($tieneHttpsPortal) {
    Escribir-WebConfigPortal $true
    if ($conRewrite) { Ok 'Redirección de http a https del portal activada.' } else { Aviso 'Sin URL Rewrite no hay redirección automática de http a https en el portal (https sí funciona).' }
}

Write-Host ''
Ok 'Listo. AMBIENTE DE TEST:'
Write-Host "  Web y API:  $(if ($tieneHttpsPortal) { 'https' } else { 'http' })://$Dominio"
Write-Host "  Portal:     $(if ($tieneHttpsPortal) { 'https' } else { 'http' })://$DominioPortal"
Write-Host "  Descargas de las apps (APK de test): https://$Dominio/descargas/"
Write-Host 'Los APK de test se construyen en su computadora con  scripts\construir-apk.ps1 -Test  y  scripts\construir-apk.ps1 -App cliente -Test ; el paquete (crear-paquete-windows.ps1 -Test) los recoge de F:\Download\TeikemApp.'
