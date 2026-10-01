<#
  Construye el APK de producción (firmado con la llave propia) de la app del almacén, listo para instalar en los aparatos.
    - Variante 'production': solo HTTPS (sin tráfico sin cifrar) y nombre "Teikem Almacén".
    - La dirección del servidor viene escrita de fábrica (se puede cambiar al registrar el aparato).
    - Firma con %USERPROFILE%\.teikem\teikem-release.jks. Si no existe, la crea (usted escribe la contraseña).
      ¡GUARDE COPIA de ese archivo y de su contraseña! Sin ellos no se pueden publicar actualizaciones de la misma app.
  Al terminar, devuelve la carpeta android\ a la variante de desarrollo (para seguir usando el emulador con HTTP).
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\construir-apk.ps1"
  Opciones: -ApiUrl <https://...>  -Salida <carpeta>  -SinRestaurar (deja android\ en producción)
#>
param(
    [string]$ApiUrl = 'https://teikem.advancelogisticspr.com',
    [string]$Salida = 'F:\Download\TeikemApp',
    [switch]$SinRestaurar
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$app = Join-Path $repo 'app-almacen'
function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Fallar([string]$m) { Write-Host ''; Write-Host $m -ForegroundColor Red; exit 1 }

if ($ApiUrl -notmatch '^https://') { Fallar 'La dirección del servidor debe empezar con https:// (la app de producción no admite HTTP sin cifrar).' }

# --- Herramientas de Android ---
Paso 'Revisando Android SDK y Java'
if (-not $env:ANDROID_HOME) { $env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
if (-not (Test-Path $env:ANDROID_HOME)) { Fallar "No encuentro el Android SDK en $env:ANDROID_HOME. Instálelo con Android Studio." }
$jbr = 'C:\Program Files\Android\Android Studio\jbr'
if (-not $env:JAVA_HOME -and (Test-Path $jbr)) { $env:JAVA_HOME = $jbr }
if (-not $env:JAVA_HOME) { Fallar 'Falta JAVA_HOME (use el JDK de Android Studio: C:\Program Files\Android\Android Studio\jbr).' }
$keytool = Join-Path $env:JAVA_HOME 'bin\keytool.exe'
if (-not (Test-Path $keytool)) { Fallar "No encuentro keytool en $keytool." }
Write-Host "ANDROID_HOME = $env:ANDROID_HOME"; Write-Host "JAVA_HOME    = $env:JAVA_HOME"

# --- Llave de firma ---
$dirLlave = Join-Path $env:USERPROFILE '.teikem'
$llave = Join-Path $dirLlave 'teikem-release.jks'
$alias = 'teikem'
if (-not (Test-Path $llave)) {
    Paso 'Creando la llave de firma (una sola vez)'
    New-Item -ItemType Directory -Force $dirLlave | Out-Null
    Write-Host 'Va a pedirle una contraseña (escríbala dos veces; si pregunta por la contraseña de la llave, pulse Enter para usar la misma).' -ForegroundColor Yellow
    Write-Host 'Guarde esa contraseña y una copia del archivo:' -ForegroundColor Yellow
    Write-Host "  $llave" -ForegroundColor Yellow
    & $keytool -genkeypair -v -keystore $llave -alias $alias -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Teikem Almacen, O=Advance Logistics, C=PR'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $llave)) { Fallar 'No se creó la llave.' }
}
$seguro = Read-Host 'Contraseña de la llave de firma' -AsSecureString
$clave = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro))
$env:TEIKEM_RELEASE_KEYSTORE = $llave
$env:TEIKEM_RELEASE_KEYSTORE_PASSWORD = $clave
$env:TEIKEM_RELEASE_KEY_ALIAS = $alias
$env:TEIKEM_RELEASE_KEY_PASSWORD = $clave

# --- Compilar ---
Set-Location $app
if (-not (Test-Path 'node_modules')) { Paso 'Instalando dependencias'; npm ci --legacy-peer-deps; if ($LASTEXITCODE -ne 0) { Fallar 'npm ci falló.' } }
$env:APP_VARIANT = 'production'
$env:EXPO_PUBLIC_API_URL = $ApiUrl
$env:CI = '1'
Paso 'Generando el proyecto Android (variante production)'
npx expo prebuild --platform android --clean
if ($LASTEXITCODE -ne 0) { Fallar 'expo prebuild falló.' }

Paso 'Compilando el APK (la primera vez tarda varios minutos)'
Push-Location android
& .\gradlew.bat assembleRelease
$ok = ($LASTEXITCODE -eq 0)
Pop-Location

$apkOrigen = Join-Path $app 'android\app\build\outputs\apk\release\app-release.apk'
if ($ok -and (Test-Path $apkOrigen)) {
    $version = (Get-Content (Join-Path $app 'package.json') -Raw | ConvertFrom-Json).version
    New-Item -ItemType Directory -Force $Salida | Out-Null
    $apk = Join-Path $Salida ("TeikemAlmacen-$version.apk")
    Copy-Item $apkOrigen $apk -Force
    $firmador = Get-ChildItem (Join-Path $env:ANDROID_HOME 'build-tools') -Recurse -Filter apksigner.bat -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1
    if ($firmador) {
        Paso 'Verificando la firma'
        & $firmador.FullName verify --print-certs $apk
    }
    Write-Host ''
    Write-Host "APK listo: $apk" -ForegroundColor Green
    Write-Host "Servidor de fábrica: $ApiUrl" -ForegroundColor Green
} else {
    Write-Host 'La compilación falló; revise los mensajes de Gradle arriba.' -ForegroundColor Red
}

# --- Volver a la variante de desarrollo ---
if (-not $SinRestaurar) {
    Paso 'Devolviendo android\ a la variante de desarrollo (emulador con HTTP)'
    Remove-Item Env:APP_VARIANT -ErrorAction SilentlyContinue
    Remove-Item Env:EXPO_PUBLIC_API_URL -ErrorAction SilentlyContinue
    npx expo prebuild --platform android --clean
}
Remove-Item Env:TEIKEM_RELEASE_KEYSTORE_PASSWORD, Env:TEIKEM_RELEASE_KEY_PASSWORD -ErrorAction SilentlyContinue
if (-not $ok) { exit 1 }
Write-Host ''
Write-Host 'Para instalar en un aparato: copie el APK al teléfono (o  adb install -r archivo.apk ) y ábralo.'
