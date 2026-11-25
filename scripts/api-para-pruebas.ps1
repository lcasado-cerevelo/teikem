# Levanta el API para correr los recorridos de Playwright SIN correo real: la llave de Brevo queda vacía en este proceso, así que el API solo escribe
# «Sin Brevo configurado» y no manda nada (los recorridos crean usuarios e invitan a cientos de direcciones de prueba; con Brevo real una sola corrida se come
# el tope diario de la cuenta, 300 correos en el plan gratis).
# Usa una base APARTE (por omisión Teikem_e2e) con la misma cadena de conexión de appsettings.Development.local.json: nunca la base de desarrollo del día a día.
# Los recorridos del portal piden además los enlaces de invitación y de computadora en la respuesta (no necesitan el correo) y sin la pantalla de bienvenida del administrador.
# -Mailpit (Lote 243): manda el correo a un buzón LOCAL (Mailpit: SMTP en localhost:1025, página en http://localhost:8025) para verlo completo sin Brevo.
#   Solo funciona en Development (el API elige el buzón local antes que Brevo); la llave de Brevo sigue vacía igual. Mailpit se levanta aparte, p. ej.:
#   docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit
#   Uso:   powershell -ExecutionPolicy Bypass -File scripts\api-para-pruebas.ps1 [-Base Teikem_e2e] [-Puerto 5000] [-Mailpit]
#   Luego: cd web-app; npx playwright test        (Ctrl+C aquí apaga el API)
param(
    [string]$Base = 'Teikem_e2e',
    [int]$Puerto = 5000,
    [string]$FilesRoot = '',     # carpeta de archivos (fotos, firmas) distinta de la de desarrollo; p. ej. la copia de archivos del video (ver docs/video/REGLAS.md, R2)
    [switch]$Mailpit             # Lote 243: correo al buzón local (Mailpit) en vez de a ningún lado; nunca Brevo
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$local = Join-Path $raiz 'src\Teikem.Api\appsettings.Development.local.json'
if (-not (Test-Path $local)) { throw "No encuentro $local (ahí vive la cadena de conexión de esta máquina)." }

$enUso = Get-NetTCPConnection -State Listen -LocalPort $Puerto -ErrorAction SilentlyContinue
if ($enUso) { throw "El puerto $Puerto ya está ocupado (¿el API de Visual Studio?). Ciérrelo o use -Puerto con otro número (y ponga API_URL=http://localhost:<puerto> al correr Playwright)." }

$json = Get-Content $local -Raw -Encoding UTF8 | ConvertFrom-Json
$cadena = $json.ConnectionStrings.Teikem
if (-not $cadena) { throw 'appsettings.Development.local.json no trae ConnectionStrings:Teikem.' }
$cadena = [regex]::Replace($cadena, '(?i)(Database|Initial Catalog)=[^;]*', "`$1=$Base")

$env:ConnectionStrings__Teikem = $cadena
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Brevo__ApiKey = ''                                  # sin correo real: lo importante de este script
$env:Auth__Onboarding__Enabled = 'false'
$env:Portal__ReturnInviteTokenInResponse = 'true'
$env:Auth__TrustedBrowsers__ReturnLinkInResponse = 'true'
if ($FilesRoot) { $env:Files__Root = $FilesRoot }
if ($Mailpit) {
    $env:Email__Smtp__Host = 'localhost'                 # buzón local: el API lo usa en vez de Brevo (solo en Development)
    $env:Email__Smtp__Port = '1025'
    $env:Notifications__WebBaseUrl = 'http://localhost:5173'   # el botón «Abrir la orden en Teikem» del correo del personal
} else {
    Remove-Item Env:Email__Smtp__Host -ErrorAction SilentlyContinue   # sin el interruptor, igual que siempre: sin correo
    Remove-Item Env:Email__Smtp__Port -ErrorAction SilentlyContinue
    Remove-Item Env:Notifications__WebBaseUrl -ErrorAction SilentlyContinue
}

Write-Host "API de pruebas SIN correo real: base $Base, http://localhost:$Puerto (Ctrl+C para apagarlo)."
if ($Mailpit) { Write-Host "Buzón local: http://localhost:8025" }
Write-Host "Si la base no existe o está vieja:  dotnet run --project src\Teikem.Api -- db-init   (con ConnectionStrings__Teikem apuntando a $Base)"
dotnet run --project (Join-Path $raiz 'src\Teikem.Api') --no-launch-profile --urls "http://localhost:$Puerto"
