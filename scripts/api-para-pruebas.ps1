# Levanta el API para correr los recorridos de Playwright SIN correo real: la llave de Brevo queda vacía en este proceso, así que el API solo escribe
# «Sin Brevo configurado» y no manda nada (los recorridos crean usuarios e invitan a cientos de direcciones de prueba; con Brevo real una sola corrida se come
# el tope diario de la cuenta, 300 correos en el plan gratis).
# Usa una base APARTE (por omisión Teikem_e2e) con la misma cadena de conexión de appsettings.Development.local.json: nunca la base de desarrollo del día a día.
# Los recorridos del portal piden además los enlaces de invitación y de computadora en la respuesta (no necesitan el correo) y sin la pantalla de bienvenida del administrador.
#   Uso:   powershell -ExecutionPolicy Bypass -File scripts\api-para-pruebas.ps1 [-Base Teikem_e2e] [-Puerto 5000]
#   Luego: cd web-app; npx playwright test        (Ctrl+C aquí apaga el API)
param(
    [string]$Base = 'Teikem_e2e',
    [int]$Puerto = 5000,
    [string]$FilesRoot = ''      # carpeta de archivos (fotos, firmas) distinta de la de desarrollo; p. ej. la copia de archivos del video (ver docs/video/REGLAS.md, R2)
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

Write-Host "API de pruebas SIN correo real: base $Base, http://localhost:$Puerto (Ctrl+C para apagarlo)."
Write-Host "Si la base no existe o está vieja:  dotnet run --project src\Teikem.Api -- db-init   (con ConnectionStrings__Teikem apuntando a $Base)"
dotnet run --project (Join-Path $raiz 'src\Teikem.Api') --no-launch-profile --urls "http://localhost:$Puerto"
