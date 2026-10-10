<#
  Construye el APK de producción (firmado con la llave propia) de la app del almacén, listo para instalar en los aparatos.
    - Variante 'production': solo HTTPS (sin tráfico sin cifrar) y nombre "Teikem Almacén".
    - La dirección del servidor viene escrita de fábrica (se puede cambiar al registrar el aparato).
    - Firma con %USERPROFILE%\.teikem\teikem-release.jks. Si no existe, la crea (usted escribe la contraseña).
      ¡GUARDE COPIA de ese archivo y de su contraseña! Sin ellos no se pueden publicar actualizaciones de la misma app.
  Al terminar, devuelve la carpeta android\ a la variante de desarrollo (para seguir usando el emulador con HTTP).
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\construir-apk.ps1"
  ACTUALIZAR SIN DESINSTALAR: este mismo script sirve. Cada compilación sube sola el «código de versión» (se recuerda en
  %USERPROFILE%\.teikem\apk-versioncode.txt; el primero que sale es el 2, porque los aparatos instalados traen el 1) y firma con la MISMA llave, que
  es lo que Android exige para instalar encima y conservar la configuración del aparato (servidor, registro, PIN y datos). Instale el APK nuevo
  con  scripts\actualizar-aparatos.ps1  (aparatos conectados por USB) o copiándolo al aparato y abriéndolo. NUNCA desinstale antes.
  Opciones: -ApiUrl <https://...>  -Salida <carpeta>  -SinRestaurar (deja android\ en producción)  -CodigoVersion <n> (fuerza el código)
#>
param(
    [string]$ApiUrl = 'https://teikem.advancelogisticspr.com',
    [string]$Salida = 'F:\Download\TeikemApp',
    [switch]$SinRestaurar,
    [int]$CodigoVersion = 0
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

# --- Código de versión: siempre mayor que el anterior (si no, Android no deja instalar encima) ---
$archivoCodigo = Join-Path $dirLlave 'apk-versioncode.txt'
if ($CodigoVersion -le 0) {
    $anterior = 1   # los aparatos ya instalados traen el código 1
    if (Test-Path $archivoCodigo) { $leido = 0; if ([int]::TryParse((Get-Content $archivoCodigo -Raw).Trim(), [ref]$leido) -and $leido -ge 1) { $anterior = $leido } }
    $CodigoVersion = $anterior + 1
}
$env:TEIKEM_VERSION_CODE = "$CodigoVersion"
# Sello de esta compilación (fecha y commit): Inicio de la app lo muestra al pie para saber qué APK tiene cada aparato
$commit = (& git -C $repo rev-parse --short HEAD 2>$null)
$env:TEIKEM_BUILD_STAMP = "$(Get-Date -Format 'yyyy-MM-dd HH:mm') $commit"
Write-Host "Código de versión de este APK: $CodigoVersion (tiene que ser mayor que el de los aparatos; no lo baje)" -ForegroundColor Cyan

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
# Esta computadora (Intel i9-14900K) ha reportado errores de hardware del procesador (WHEA «Processor Core»: paridad interna y TLB) y con
# la carga completa de la compilación se le cierran Java (Gradle) y clang (código 0xC0000005) al azar, aunque el código esté bien.
# Para no cargarla de más: sin tareas en paralelo, pocos hilos, solo las arquitecturas de los aparatos (el Zebra es ARM; x86 es de
# emuladores y duplica la compilación nativa) y hasta 3 intentos (Gradle retoma lo ya compilado). Si Java se cae, guarda su hs_err*.log
# en la carpeta de salida (antes se perdía al restaurar android\).
$gradleArgs = @('assembleRelease', '--no-parallel', '-Dorg.gradle.workers.max=4', '-PreactNativeArchitectures=armeabi-v7a,arm64-v8a')
Push-Location android
$ok = $false
for ($intento = 1; $intento -le 3 -and -not $ok; $intento++) {
    if ($intento -gt 1) {
        Write-Host ''
        Write-Host "Reintento $intento de 3 (Gradle sigue desde lo ya compilado)..." -ForegroundColor Yellow
        & .\gradlew.bat --stop | Out-Null
    }
    & .\gradlew.bat @gradleArgs
    $ok = ($LASTEXITCODE -eq 0)
    if (-not $ok) {
        $caidas = Get-ChildItem . -Filter 'hs_err_pid*.log' -ErrorAction SilentlyContinue
        if ($caidas) {
            New-Item -ItemType Directory -Force $Salida | Out-Null
            $caidas | ForEach-Object { Copy-Item $_.FullName (Join-Path $Salida $_.Name) -Force; Write-Host "Registro de la caída de Java guardado en $(Join-Path $Salida $_.Name)" -ForegroundColor Yellow }
        }
    }
}
Pop-Location

$apkOrigen = Join-Path $app 'android\app\build\outputs\apk\release\app-release.apk'
if ($ok -and (Test-Path $apkOrigen)) {
    $version = (Get-Content (Join-Path $app 'package.json') -Raw | ConvertFrom-Json).version
    New-Item -ItemType Directory -Force $Salida | Out-Null
    $apk = Join-Path $Salida ("TeikemAlmacen-$version-c$CodigoVersion.apk")
    Set-Content -Path $archivoCodigo -Value "$CodigoVersion" -Encoding ASCII   # solo si compiló: el próximo APK sube de aquí
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
    Remove-Item Env:TEIKEM_VERSION_CODE -ErrorAction SilentlyContinue
    Remove-Item Env:TEIKEM_BUILD_STAMP -ErrorAction SilentlyContinue
    Remove-Item Env:EXPO_PUBLIC_API_URL -ErrorAction SilentlyContinue
    npx expo prebuild --platform android --clean
}
Remove-Item Env:TEIKEM_RELEASE_KEYSTORE_PASSWORD, Env:TEIKEM_RELEASE_KEY_PASSWORD -ErrorAction SilentlyContinue
if (-not $ok) { exit 1 }
Write-Host ''
Write-Host 'Para ACTUALIZAR los aparatos (sin desinstalar, conservan su configuración): scripts\actualizar-aparatos.ps1  o copie el APK al aparato y ábralo (diga «Actualizar»/«Instalar»).'
