<#
  Instalador de Teikem (API + web) en Windows Server con IIS. Se corre en el SERVIDOR, como Administrador, desde la carpeta donde
  descomprimió el paquete (la que trae esta carpeta "app"):
      powershell -ExecutionPolicy Bypass -File .\instalar.ps1
  Sirve para instalar y para actualizar (vuelva a correrlo con el paquete nuevo; conserva la configuración si usted quiere).
  - Crea (o actualiza) el sitio "Teikem" en IIS con su propio grupo de aplicaciones; el API y la web son el mismo sitio.
  - Instala el .NET 8 Hosting Bundle si falta.
  - Las llaves de Brevo se toman de las variables de entorno del sistema (Brevo__ApiKey, Brevo__FromEmail) si existen.
  - HTTPS: NO instala ni emite certificados. El sitio nuevo queda con su nombre de dominio en el puerto 80 y toma el
    certificado del proceso de Let's Encrypt que ya tiene el servidor. Cuando el sitio ya tenga el enlace HTTPS, vuelva a correr
    este script para activar la redirección de http a https.
  Opciones: -Sitio <nombre> (por defecto Teikem)  -Carpeta <ruta> (por defecto C:\inetpub\teikem)
#>
param(
    [string]$Sitio = 'Teikem',
    [string]$Carpeta = 'C:\inetpub\teikem'
)

$ErrorActionPreference = 'Stop'
function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Ok([string]$m) { Write-Host $m -ForegroundColor Green }
function Aviso([string]$m) { Write-Host $m -ForegroundColor Yellow }
function Fallar([string]$m) { Write-Host ''; Write-Host $m -ForegroundColor Red; exit 1 }
function Preguntar([string]$texto, [string]$defecto = '') {
    if ($defecto) { $r = Read-Host "$texto [$defecto]"; if ([string]::IsNullOrWhiteSpace($r)) { return $defecto } else { return $r.Trim() } }
    return (Read-Host $texto).Trim()
}
# icacls devuelve error (código distinto de 0) si una cuenta no existe; en PowerShell 5.1 la salida de error de un programa nativo
# abortaría el script, así que se ejecuta aparte y se mira solo el código de salida.
function Permisos([string[]]$argumentos) {
    $anterior = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & icacls @argumentos 2>&1 | Out-Null
    $ErrorActionPreference = $anterior
    return ($LASTEXITCODE -eq 0)
}
function Texto-Seguro($seguro) { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro)) }

$origen = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path (Join-Path $origen 'Teikem.Api.dll'))) { Fallar "No encuentro la carpeta 'app' del paquete junto a este script ($origen)." }
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Fallar 'Corra este script como Administrador (clic derecho en PowerShell > Ejecutar como administrador).' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# --------------------------------------------------------------------------------------------------------- IIS y .NET
Paso 'Revisando IIS y el .NET 8 Hosting Bundle'
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (-not (Get-Module WebAdministration)) {
    Fallar 'IIS no está instalado. Instálelo con:  Install-WindowsFeature Web-Server,Web-Mgmt-Console  y vuelva a correr este script.'
}
function Tiene-Hosting { return ((Test-Path 'C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\8.*') -and [bool](Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)) }
if (-not (Tiene-Hosting)) {
    Aviso 'Falta el .NET 8 Hosting Bundle: lo descargo e instalo (Microsoft).'
    $exe = Join-Path $env:TEMP 'dotnet-hosting-8-win.exe'
    Invoke-WebRequest 'https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe' -OutFile $exe -UseBasicParsing
    $p = Start-Process $exe -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) { Fallar "El instalador del Hosting Bundle terminó con código $($p.ExitCode)." }
    net stop was /y | Out-Null; net start w3svc | Out-Null
    Import-Module WebAdministration -Force
    if (-not (Tiene-Hosting)) { Fallar 'El Hosting Bundle no quedó registrado en IIS; reinicie el servidor y vuelva a correr este script.' }
}
Ok '.NET 8 Hosting Bundle presente.'

# --------------------------------------------------------------------------------------------------------- configuración
$cfgArchivo = Join-Path $Carpeta 'appsettings.Production.local.json'
$escribirCfg = $true
if (Test-Path $cfgArchivo) {
    Aviso "Ya existe una configuración ($cfgArchivo)."
    if ((Read-Host '¿Conservarla? (S/n)') -notmatch '^[nN]') { $escribirCfg = $false }
}

if ($escribirCfg) {
    Paso 'Configuración'
    $dominio = Preguntar 'Dominio de la web' 'teikem.advancelogisticspr.com'
    Write-Host ''
    Write-Host 'Base de datos de Teikem (la que ya subió; autenticación de SQL Server):'
    $dbServidor = Preguntar '  Servidor y puerto (ej. localhost,1433 o 10.0.0.5,1433)'
    $dbNombre = Preguntar '  Nombre de la base' 'Teikem'
    $dbUsuario = Preguntar '  Usuario'
    $dbClave = Texto-Seguro (Read-Host '  Contraseña' -AsSecureString)
    if (-not $dbServidor -or -not $dbUsuario -or -not $dbClave) { Fallar 'Servidor, usuario y contraseña de la base son obligatorios.' }

    $brevoKey = [Environment]::GetEnvironmentVariable('Brevo__ApiKey', 'Machine')
    $brevoDe = [Environment]::GetEnvironmentVariable('Brevo__FromEmail', 'Machine')
    $brevo = $null
    if ($brevoKey -and $brevoDe) {
        Write-Host ''
        Ok 'Brevo: se usan las variables del sistema Brevo__ApiKey y Brevo__FromEmail (no se piden ni se copian).'
    } else {
        Write-Host ''
        Aviso 'No encontré Brevo__ApiKey / Brevo__FromEmail entre las variables del sistema.'
        $k = Preguntar '  Brevo API key (Enter para dejarlo vacío)'
        $d = Preguntar '  Correo remitente'
        if ($k -and $d) { $brevo = @{ ApiKey = $k; FromEmail = $d; FromName = 'Teikem' } }
        else { Aviso 'Sin Brevo no se puede enviar el código del primer ingreso.' }
    }

    $bytes = New-Object byte[] 48
    $rng = New-Object Security.Cryptography.RNGCryptoServiceProvider; $rng.GetBytes($bytes); $rng.Dispose()
    $jwt = [Convert]::ToBase64String($bytes)

    $cfg = [ordered]@{
        ConnectionStrings = @{ Teikem = "Server=$dbServidor;Database=$dbNombre;User Id=$dbUsuario;Password=$dbClave;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True" }
        Jwt = @{ SigningKey = $jwt }
        Cors = @{ Origins = @("https://$dominio", "http://$dominio") }
        Seed = @{ Demo = @{ Enabled = $false } }
        Auth = @{ Onboarding = @{ ReturnEmailCodeInResponse = $false } }
        Hosting = @{ RedirectToHttps = $false; Dominio = $dominio }
    }
    if ($brevo) { $cfg['Brevo'] = $brevo }
} else {
    $cfgActual = Get-Content $cfgArchivo -Raw | ConvertFrom-Json
    $dominio = $cfgActual.Hosting.Dominio
    if (-not $dominio) { $dominio = Preguntar 'Dominio de la web' 'teikem.advancelogisticspr.com' }
}

# --------------------------------------------------------------------------------------------------------- grupo de aplicaciones
Paso 'Grupo de aplicaciones de IIS'
if (-not (Test-Path "IIS:\AppPools\$Sitio")) { New-WebAppPool -Name $Sitio | Out-Null }
Set-ItemProperty "IIS:\AppPools\$Sitio" managedRuntimeVersion ''
Set-ItemProperty "IIS:\AppPools\$Sitio" startMode AlwaysRunning                       # el API tiene procesos en segundo plano
Set-ItemProperty "IIS:\AppPools\$Sitio" processModel.idleTimeout ([TimeSpan]::Zero)   # que IIS no lo duerma por inactividad
Ok "Grupo de aplicaciones '$Sitio' listo."

# --------------------------------------------------------------------------------------------------------- copiar archivos
Paso "Instalando en $Carpeta"
New-Item -ItemType Directory -Force $Carpeta | Out-Null
$hayPool = Test-Path "IIS:\AppPools\$Sitio"
if ($hayPool) {
    # app_offline.htm hace que IIS suelte los archivos mientras se copian
    Set-Content (Join-Path $Carpeta 'app_offline.htm') '<html><body><h2>Teikem se está actualizando. Vuelva en un minuto.</h2></body></html>' -Encoding UTF8
    Start-Sleep -Seconds 3
}
robocopy $origen $Carpeta /MIR /XF appsettings.Production.local.json app_offline.htm /XD logs /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fallar "robocopy falló (código $LASTEXITCODE)." }

if ($escribirCfg) {
    $json = $cfg | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($cfgArchivo, $json, (New-Object Text.UTF8Encoding $false))
}
# Solo Administradores, SYSTEM y el grupo de aplicaciones leen la configuración (trae la contraseña de la base).
if (-not (Permisos @($cfgArchivo, '/inheritance:r', '/grant:r', '*S-1-5-32-544:F', '/grant:r', '*S-1-5-18:F', '/grant:r', "IIS AppPool\${Sitio}:R"))) {
    Fallar "No pude restringir los permisos de $cfgArchivo (trae la contraseña de la base). Revise que exista el grupo de aplicaciones '$Sitio'."
}

# --------------------------------------------------------------------------------------------------------- IIS
Paso 'Configurando IIS'
if (-not (Permisos @($Carpeta, '/grant', "IIS AppPool\${Sitio}:(OI)(CI)RX", '/T', '/Q'))) {
    Fallar "No pude dar permiso de lectura a 'IIS AppPool\$Sitio' sobre $Carpeta."
}

if (-not (Get-Website -Name $Sitio -ErrorAction SilentlyContinue)) {
    New-Website -Name $Sitio -PhysicalPath $Carpeta -ApplicationPool $Sitio -HostHeader $dominio -Port 80 | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$Sitio" physicalPath $Carpeta
    Set-ItemProperty "IIS:\Sites\$Sitio" applicationPool $Sitio
}
if (-not (Get-WebBinding -Name $Sitio -Protocol http | Where-Object { $_.bindingInformation -like "*:80:$dominio" })) {
    New-WebBinding -Name $Sitio -Protocol http -Port 80 -HostHeader $dominio | Out-Null
}
Set-ItemProperty "IIS:\Sites\$Sitio" applicationDefaults.preloadEnabled $true
$sitioId = (Get-Website -Name $Sitio).Id

if (-not (Get-NetFirewallRule -DisplayName 'Teikem HTTP/HTTPS' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Teikem HTTP/HTTPS' -Direction Inbound -Protocol TCP -LocalPort 80, 443 -Action Allow | Out-Null
}

Remove-Item (Join-Path $Carpeta 'app_offline.htm') -ErrorAction SilentlyContinue
if ((Get-WebAppPoolState $Sitio).Value -ne 'Started') { Start-WebAppPool $Sitio }
if ((Get-WebsiteState $Sitio).Value -ne 'Started') { Start-Website $Sitio }
Restart-WebAppPool $Sitio

# --------------------------------------------------------------------------------------------------------- comprobar el API
function Probar-Salud([string]$host_) {
    try {
        $r = [Net.HttpWebRequest]::Create('http://127.0.0.1/health'); $r.Host = $host_; $r.Timeout = 20000; $r.AllowAutoRedirect = $false
        $resp = $r.GetResponse(); $txt = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd(); $resp.Close()
        return ($txt -match '"status":"ok"')
    } catch [Net.WebException] {
        # con la redirección a https activa (actualización) responde 308: el sitio está vivo
        return ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 308)
    } catch { return $false }
}
Paso 'Comprobando el API'
$vivo = $false
for ($i = 0; $i -lt 20 -and -not $vivo; $i++) { $vivo = Probar-Salud $dominio; if (-not $vivo) { Start-Sleep -Seconds 3 } }
if ($vivo) { Ok 'El API responde.' } else {
    Aviso 'El API no respondió. Últimos eventos del módulo de IIS:'
    Get-EventLog -LogName Application -Newest 8 -ErrorAction SilentlyContinue | Where-Object { $_.Source -like '*AspNetCore*' -or $_.Source -like '*IIS*' -or $_.Source -like '.NET*' } | ForEach-Object { Write-Host "  $($_.TimeGenerated) $($_.Message.Split("`n")[0])" }
    Fallar 'Revise la cadena de conexión a la base (appsettings.Production.local.json) y que este servidor llegue a ella.'
}

# --------------------------------------------------------------------------------------------------------- HTTPS (Let's Encrypt)
Paso 'HTTPS'
$tieneHttps = [bool](Get-WebBinding -Name $Sitio -Protocol https -ErrorAction SilentlyContinue)
if ($tieneHttps) {
    Ok "El sitio ya tiene su enlace HTTPS (el certificado lo renueva el proceso de Let's Encrypt del servidor)."
} else {
    Aviso 'El sitio todavía no tiene enlace HTTPS: este instalador no emite certificados.'
    Write-Host "  - Su proceso de Let's Encrypt del servidor debe agregar el sitio '$Sitio' (host $dominio, id de sitio $sitioId)."
    Write-Host "  - Requisitos habituales: el DNS de $dominio apunta a este servidor y el puerto 80 está abierto para la validación."
    Write-Host '  - Cuando el sitio ya tenga el certificado, vuelva a correr este script (conserve la configuración) para activar la redirección a https.'
}

if ($tieneHttps) {
    # Con el certificado puesto: http -> https (el desafío de Let's Encrypt queda exento en el API).
    $archivo = Join-Path $Carpeta 'appsettings.Production.local.json'
    $j = Get-Content $archivo -Raw | ConvertFrom-Json
    if (-not $j.Hosting) { $j | Add-Member -NotePropertyName Hosting -NotePropertyValue ([pscustomobject]@{}) }
    if ($null -eq $j.Hosting.RedirectToHttps) { $j.Hosting | Add-Member -NotePropertyName RedirectToHttps -NotePropertyValue $true } else { $j.Hosting.RedirectToHttps = $true }
    [IO.File]::WriteAllText($archivo, ($j | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
    Restart-WebAppPool $Sitio
    Ok 'Redirección de http a https activada.'
}

Write-Host ''
Ok 'Listo.'
if ($tieneHttps) { Write-Host "Abra:  https://$dominio" } else { Write-Host "Abra:  http://$dominio  (HTTPS cuando emita el certificado)" }
Write-Host 'Los primeros usuarios que entren pasan por el primer ingreso (correo, contraseña propia y MFA).'
