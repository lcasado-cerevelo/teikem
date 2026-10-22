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
  - BASE DE DATOS: antes de copiar los archivos nuevos, lleva la base a la versión del paquete (db-update: agrega las tablas, columnas,
    índices y datos de referencia que falten; nunca borra datos). Primero simula y muestra qué cambiaría; pide confirmar que ya hay un
    respaldo de la base. Si la base falla, el sitio sigue con la versión anterior (no se copia nada).
  STAGING en el mismo servidor: corra el instalador con otro nombre de sitio y otra carpeta (cada uno con su grupo de aplicaciones, su configuración,
  su base de datos y su dominio), p. ej.:  .\instalar.ps1 -Sitio TeikemStaging -Carpeta C:\inetpub\teikem-staging
  Opciones: -Sitio <nombre> (por defecto Teikem)  -Carpeta <ruta> (por defecto C:\inetpub\teikem)
            -SinBase (no toca la base de datos)  -RespaldoHecho (no pregunta por el respaldo; para correrlo sin teclado)
  AMBIENTE DE TEST (2026-10-19; lo usa instalar-test.ps1, que es lo que se corre para test):
            -Dominio <host> y -DbNombre <base>: valores por defecto de las preguntas de la configuración (para no teclearlos).
            -Etiqueta <TEXTO>: etiqueta del ambiente que la web muestra en su cinta roja (Hosting:EnvironmentLabel). Sin ella: nada en el sitio
                «Teikem» (producción) y STAGING en cualquier otro.
            -LetsEncrypt -CorreoLetsEncrypt <correo>: pide el certificado de Let's Encrypt con win-acme (lo descarga si falta), deja el enlace https y
                la tarea de renovación automática, y activa la redirección a https en la misma corrida (ver letsencrypt.ps1).
            -OrigenesExtra <url,...>: orígenes adicionales permitidos (CORS), p. ej. el portal del cliente en otro dominio.
            -ConDescargas: copia la carpeta «descargas» del paquete (página de descarga de las apps) a wwwroot\descargas; los APK ya subidos
                (wwwroot\descargas\apk) se conservan entre actualizaciones y los del paquete, si trae, los reemplazan.
            -CorreoRedirigir <buzón>: TODO el correo que salga de este sitio va a ese buzón (Email:RedirectTo) con el destinatario original en el
                asunto; nadie real lo recibe. -CorreoRedirigirExcepto <dominio,...>: dominios que sí reciben de verdad (los códigos del equipo).
                Solo para ambientes de pruebas; en producción no se usa.
            -UrlPortal <url>: dirección del portal del cliente de este ambiente (Portal:WebBaseUrl): a ella apunta el enlace del correo de la invitación al
                portal (la del usuario del cliente y la de su consignatario). Sin ella esas invitaciones no salen por correo. Se escribe cada vez que se pasa.
            Además, si falta, se escribe Auth:PasswordReset:WebBaseUrl = https://<dominio> (la web de este sitio): sin ella no sale el correo de «Olvidé mi contraseña».
#>
param(
    [string]$Sitio = 'Teikem',
    [string]$Carpeta = 'C:\inetpub\teikem',
    [switch]$SinBase,
    [switch]$RespaldoHecho,
    [string]$Dominio = '',
    [string]$DbNombre = '',
    [string]$Etiqueta = '',
    [switch]$LetsEncrypt,
    [string]$CorreoLetsEncrypt = '',
    [string[]]$OrigenesExtra = @(),
    [switch]$ConDescargas,
    [string]$CorreoRedirigir = '',
    [string[]]$CorreoRedirigirExcepto = @(),
    [string]$UrlPortal = ''
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
# Quita el aviso de mantenimiento (si lo pusimos) y termina sin haber cambiado nada del sitio.
function Cancelar([string]$m) { Remove-Item (Join-Path $Carpeta 'app_offline.htm') -ErrorAction SilentlyContinue; Fallar $m }
function Texto-Seguro($seguro) { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro)) }

$origen = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path (Join-Path $origen 'Teikem.Api.dll'))) { Fallar "No encuentro la carpeta 'app' del paquete junto a este script ($origen)." }
if ($LetsEncrypt) {
    $le = Join-Path $PSScriptRoot 'letsencrypt.ps1'
    if (-not (Test-Path $le)) { Fallar "Falta letsencrypt.ps1 junto a este script (viene en el paquete)." }
    . $le
}
$dominioDefecto = if ($Dominio) { $Dominio } else { 'teikem.advancelogisticspr.com' }
$dbNombreDefecto = if ($DbNombre) { $DbNombre } else { 'Teikem' }
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
    $dominio = Preguntar 'Dominio de la web' $dominioDefecto
    Write-Host ''
    Write-Host 'Base de datos de Teikem (la que ya subió; autenticación de SQL Server):'
    $dbServidor = Preguntar '  Servidor y puerto (ej. localhost,1433 o 10.0.0.5,1433)'
    $dbNombre = Preguntar '  Nombre de la base' $dbNombreDefecto
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
        Cors = @{ Origins = @(@("https://$dominio", "http://$dominio") + $OrigenesExtra | Select-Object -Unique) }
        Seed = @{ Demo = @{ Enabled = $false } }
        Auth = @{ Onboarding = @{ ReturnEmailCodeInResponse = $false } }
        Hosting = @{ RedirectToHttps = $false; Dominio = $dominio }
    }
    if ($brevo) { $cfg['Brevo'] = $brevo }
} else {
    $cfgActual = [IO.File]::ReadAllText($cfgArchivo, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $dominio = $cfgActual.Hosting.Dominio
    if (-not $dominio) { $dominio = Preguntar 'Dominio de la web' $dominioDefecto }
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
# --------------------------------------------------------------------------------------------------------- base de datos
# Se corre con el API NUEVO (el del paquete, sin copiarlo todavía) y la cadena de conexión de la configuración. Si algo falla, el
# sitio sigue con los archivos anteriores. db-update va en una transacción: un fallo no deja la base a medias.
function Correr-DbUpdate([string[]]$extra) {
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
    if (-not $dotnet) { Cancelar 'No encuentro dotnet.exe (el .NET 8 Hosting Bundle).' }
    if ($escribirCfg) { $cadena = $cfg.ConnectionStrings.Teikem; $llave = $cfg.Jwt.SigningKey }
    else { $c = [IO.File]::ReadAllText($cfgArchivo, [Text.Encoding]::UTF8) | ConvertFrom-Json; $cadena = $c.ConnectionStrings.Teikem; $llave = $c.Jwt.SigningKey }
    $nombres = 'ASPNETCORE_ENVIRONMENT', 'ConnectionStrings__Teikem', 'Jwt__SigningKey', 'Database__RepoRoot', 'Database__DesignFolder', 'Logging__LogLevel__Default', 'Logging__LogLevel__Teikem'
    $antes = @{}; foreach ($n in $nombres) { $antes[$n] = [Environment]::GetEnvironmentVariable($n, 'Process') }
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Production'; $env:ConnectionStrings__Teikem = $cadena; $env:Jwt__SigningKey = $llave
        $env:Database__RepoRoot = $PSScriptRoot; $env:Database__DesignFolder = 'db'
        $env:Logging__LogLevel__Default = 'Warning'; $env:Logging__LogLevel__Teikem = 'Warning'
        Push-Location $origen
        & $dotnet (Join-Path $origen 'Teikem.Api.dll') db-update @extra | Out-Host   # Out-Host: que la salida no se mezcle con el código que devuelve la función
        return $LASTEXITCODE
    } finally {
        Pop-Location
        foreach ($n in $nombres) { [Environment]::SetEnvironmentVariable($n, $antes[$n], 'Process') }
    }
}
Paso 'Base de datos'
if ($SinBase) {
    Aviso 'Se omite la base de datos (-SinBase). Si el paquete nuevo necesita tablas o columnas que la base no tiene, el API fallará.'
} elseif (-not (Test-Path (Join-Path $PSScriptRoot 'db\logistica-db-estructura.sql'))) {
    Cancelar "Falta la carpeta 'db' del paquete (los scripts de la base) junto a este script."
} else {
    Write-Host 'Primero se SIMULA (no cambia nada): esto es lo que le falta a la base para estar al día.'
    Write-Host ''
    $codigo = Correr-DbUpdate @('--dry-run')
    if ($codigo -eq 1) { Cancelar 'No se pudo revisar la base (mensaje arriba). No se cambió nada del sitio ni de la base.' }
    if ($codigo -eq 2) { Aviso 'Hay diferencias marcadas «REVISAR A MANO»: no se arreglan solas; el resto sí se aplica.' }
    Write-Host ''
    if (-not $RespaldoHecho) {
        Aviso 'Antes de seguir haga un respaldo de la base. En SQL Server, por ejemplo:'
        Write-Host "    BACKUP DATABASE [<nombre de la base>] TO DISK = N'C:\Respaldos\Teikem-antes.bak' WITH COMPRESSION"
        if ((Read-Host '¿Ya hizo el respaldo y quiere aplicar estos cambios a la base? (s/N)') -notmatch '^[sS]') {
            Cancelar 'Cancelado. No se cambió nada del sitio ni de la base.'
        }
    }
    $codigo = Correr-DbUpdate @()
    if ($codigo -eq 1) { Cancelar 'La actualización de la base falló (mensaje arriba). La base quedó como estaba y el sitio sigue con la versión anterior.' }
    if ($codigo -eq 2) { Aviso 'La base se actualizó, con diferencias para revisar a mano (arriba). Se sigue con el sitio.' } else { Ok 'Base de datos al día.' }
}

Paso 'Copiando los archivos'
# wwwroot\descargas (página de descarga de las apps y sus APK) no es parte del paquete «app»: se conserva al reemplazar el sitio y se actualiza aparte.
$descargasSitio = Join-Path $Carpeta 'wwwroot\descargas'
robocopy $origen $Carpeta /MIR /XF appsettings.Production.local.json app_offline.htm /XD logs $descargasSitio /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fallar "robocopy falló (código $LASTEXITCODE)." }
$descargasPaquete = Join-Path $PSScriptRoot 'descargas'
if ($ConDescargas -and (Test-Path (Join-Path $descargasPaquete 'index.html'))) {
    # /E (no /MIR): los APK que ya estaban en el sitio se quedan; los que trae el paquete se copian encima (mismo nombre).
    New-Item -ItemType Directory -Force (Join-Path $descargasSitio 'apk') | Out-Null
    robocopy $descargasPaquete $descargasSitio /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fallar "robocopy (descargas) falló (código $LASTEXITCODE)." }
    $apks = @(Get-ChildItem (Join-Path $descargasSitio 'apk') -Filter '*.apk' -ErrorAction SilentlyContinue)
    Ok "Página de descargas en https://$dominio/descargas/ ($($apks.Count) APK)."
}

if ($escribirCfg) {
    $json = $cfg | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($cfgArchivo, $json, (New-Object Text.UTF8Encoding $false))
}
# Llaves de Data Protection (cifran el secreto del MFA y las invitaciones). Sin una carpeta fija se pierden cada vez que IIS reinicia el
# sitio y el MFA deja de poder verificarse ("Error interno"). Se protegen con DPAPI de la máquina; solo este sitio y los administradores las leen.
# Una carpeta por sitio (2026-10-12): el sitio «Teikem» (producción) conserva la de siempre; otro sitio (p. ej. «TeikemStaging») usa la suya, para no compartir llaves.
$llaves = if ($Sitio -eq 'Teikem') { 'C:\ProgramData\Teikem\keys' } else { "C:\ProgramData\Teikem\keys-$Sitio" }
New-Item -ItemType Directory -Force $llaves | Out-Null
if (-not (Permisos @($llaves, '/inheritance:r', '/grant:r', '*S-1-5-32-544:(OI)(CI)F', '/grant:r', '*S-1-5-18:(OI)(CI)F', '/grant:r', "IIS AppPool\${Sitio}:(OI)(CI)M"))) {
    Fallar "No pude dar permiso sobre la carpeta de llaves $llaves al grupo de aplicaciones '$Sitio'."
}
$jc = [IO.File]::ReadAllText($cfgArchivo, [Text.Encoding]::UTF8) | ConvertFrom-Json
$cambiado = $false
if (-not $jc.DataProtection) {
    $jc | Add-Member -NotePropertyName DataProtection -NotePropertyValue ([pscustomobject]@{ KeysPath = $llaves })
    $cambiado = $true
}
# Cinta de ambiente (2026-10-12): un sitio que no es «Teikem» (producción) se identifica como STAGING en la web (cinta roja en todas las pantallas;
# la lee de /health). Se puede cambiar el texto editando Hosting:EnvironmentLabel en appsettings.Production.local.json o con -Etiqueta (2026-10-19: TEST).
$etiquetaAmbiente = if ($Etiqueta) { $Etiqueta.Trim().ToUpperInvariant() } elseif ($Sitio -ne 'Teikem') { 'STAGING' } else { '' }
if ($etiquetaAmbiente) {
    if (-not $jc.Hosting) { $jc | Add-Member -NotePropertyName Hosting -NotePropertyValue ([pscustomobject]@{}) }
    if ($jc.Hosting.EnvironmentLabel -ne $etiquetaAmbiente) { $jc.Hosting | Add-Member -NotePropertyName EnvironmentLabel -NotePropertyValue $etiquetaAmbiente -Force; $cambiado = $true }
}
# Redirección de todo el correo (2026-10-19, ambientes de pruebas): Email:RedirectTo / ExceptDomains. Se escribe (o reemplaza) cada vez que se pasa la opción.
if ($CorreoRedirigir) {
    if ($Sitio -eq 'Teikem') { Fallar 'La redirección de correo (-CorreoRedirigir) no se usa en el sitio de producción.' }
    $redir = [pscustomobject]@{ RedirectTo = $CorreoRedirigir.Trim(); PlusAddressing = $true; ExceptDomains = @($CorreoRedirigirExcepto | Where-Object { $_ } | ForEach-Object { $_.Trim().TrimStart('@') }) }
    $jc | Add-Member -NotePropertyName Email -NotePropertyValue $redir -Force
    $cambiado = $true
    Ok "Correo: todo lo que salga de este sitio va a $($redir.RedirectTo)$(if ($redir.ExceptDomains.Count) { " (salvo @$($redir.ExceptDomains -join ', @'))" })."
}
# Direcciones de los enlaces de los correos (2026-10-22). Auth:PasswordReset:WebBaseUrl = la web de este sitio (sin ella NO sale el correo de «Olvidé mi contraseña»):
# solo se escribe si falta, para no pisar lo que ya se ajustó a mano. Portal:WebBaseUrl = el portal del cliente (invitaciones): se escribe cada vez que se pasa -UrlPortal.
if (-not $jc.Auth) { $jc | Add-Member -NotePropertyName Auth -NotePropertyValue ([pscustomobject]@{}) }
if (-not $jc.Auth.PasswordReset) { $jc.Auth | Add-Member -NotePropertyName PasswordReset -NotePropertyValue ([pscustomobject]@{}) }
if (-not $jc.Auth.PasswordReset.WebBaseUrl) {
    $jc.Auth.PasswordReset | Add-Member -NotePropertyName WebBaseUrl -NotePropertyValue "https://$dominio" -Force
    $cambiado = $true
    Ok "Enlaces de «Olvidé mi contraseña»: https://$dominio"
}
if ($UrlPortal) {
    $urlPortal = $UrlPortal.Trim().TrimEnd('/')
    if (-not $jc.Portal) { $jc | Add-Member -NotePropertyName Portal -NotePropertyValue ([pscustomobject]@{}) }
    if ($jc.Portal.WebBaseUrl -ne $urlPortal) { $jc.Portal | Add-Member -NotePropertyName WebBaseUrl -NotePropertyValue $urlPortal -Force; $cambiado = $true }
    Ok "Invitaciones al portal: los enlaces del correo van a $urlPortal"
}
# Orígenes extra de CORS (p. ej. el portal del cliente en otro dominio): se agregan a los que ya hay, sin repetir.
if ($OrigenesExtra.Count -gt 0) {
    if (-not $jc.Cors) { $jc | Add-Member -NotePropertyName Cors -NotePropertyValue ([pscustomobject]@{ Origins = @() }) }
    $actuales = @($jc.Cors.Origins)
    $nuevos = @($OrigenesExtra | Where-Object { $_ -and ($actuales -notcontains $_) })
    if ($nuevos.Count -gt 0) { $jc.Cors.Origins = @($actuales + $nuevos); $cambiado = $true }
}
if ($cambiado) { [IO.File]::WriteAllText($cfgArchivo, ($jc | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false)) }

# Registro de lo que escribe el sitio (errores internos): se activa en web.config (que cada actualización reemplaza) y su carpeta logs\.
$wc = Join-Path $Carpeta 'web.config'
if (Test-Path $wc) {
    $txt = [IO.File]::ReadAllText($wc, [Text.Encoding]::UTF8)
    if ($txt -match 'stdoutLogEnabled="false"') {
        [IO.File]::WriteAllText($wc, ($txt -replace 'stdoutLogEnabled="false"', 'stdoutLogEnabled="true"'), (New-Object Text.UTF8Encoding $true))
    }
}
$logs = Join-Path $Carpeta 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
Permisos @($logs, '/grant', "IIS AppPool\${Sitio}:(OI)(CI)M") | Out-Null

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
# 2026-10-12: los filtros con selección múltiple («Todos» sobre una búsqueda) mandan muchos ids en la URL; IIS acepta por defecto solo 2048 caracteres de
# cadena de consulta. Se sube a 32 KB (y la URL completa a 64 KB) solo para este sitio.
try {
    Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/$Sitio" -Filter 'system.webServer/security/requestFiltering/requestLimits' -Name maxQueryString -Value 32768
    Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST/$Sitio" -Filter 'system.webServer/security/requestFiltering/requestLimits' -Name maxUrl -Value 65536
} catch { Write-Host "Aviso: no pude subir el límite de la cadena de consulta de IIS: $($_.Exception.Message)" }
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
    Aviso "El API no respondió. Mire también $Carpeta\logs\ (últimas líneas del registro del sitio) y los eventos de IIS:"
    Get-EventLog -LogName Application -Newest 8 -ErrorAction SilentlyContinue | Where-Object { $_.Source -like '*AspNetCore*' -or $_.Source -like '*IIS*' -or $_.Source -like '.NET*' } | ForEach-Object { Write-Host "  $($_.TimeGenerated) $($_.Message.Split("`n")[0])" }
    Fallar 'Revise la cadena de conexión a la base (appsettings.Production.local.json) y que este servidor llegue a ella.'
}

# --------------------------------------------------------------------------------------------------------- HTTPS (Let's Encrypt)
Paso 'HTTPS'
$tieneHttps = [bool](Get-WebBinding -Name $Sitio -Protocol https -ErrorAction SilentlyContinue)
if ($LetsEncrypt -and -not $tieneHttps) {
    # 2026-10-19: con -LetsEncrypt el instalador pide el certificado él mismo (win-acme) y deja la renovación automática; ver letsencrypt.ps1.
    if (-not $CorreoLetsEncrypt) { $CorreoLetsEncrypt = Preguntar "Correo para la cuenta de Let's Encrypt (avisos si no pudiera renovar)" }
    $tieneHttps = Asegurar-CertificadoLetsEncrypt -Sitio $Sitio -Dominio $dominio -Correo $CorreoLetsEncrypt
}
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
    $j = [IO.File]::ReadAllText($archivo, [Text.Encoding]::UTF8) | ConvertFrom-Json
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
