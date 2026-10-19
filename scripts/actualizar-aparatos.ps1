<#
  Actualiza la app del almacén en los aparatos conectados por USB, INSTALANDO ENCIMA (adb install -r): no desinstala nada, así que cada aparato
  conserva su configuración (servidor, registro, PIN y datos sin enviar). Nunca desinstala.
  Requisitos: depuración USB activada en los aparatos y el APK nuevo hecho con scripts\construir-apk.ps1 (misma llave de firma y código de versión mayor).
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\actualizar-aparatos.ps1"
  Opciones: -Apk <archivo.apk>  (sin él se usa el más reciente de F:\Download\TeikemApp)
            -Staging  (usa el TeikemStaging-*.apk más reciente, el de la app de pruebas; sin él, solo los TeikemAlmacen-*.apk de producción)
            -Test     (usa el TeikemTest-*.apk más reciente, el de la app del ambiente de TEST; 2026-10-19)
            -App cliente  (la app del portal del cliente: TeikemClientes-*.apk, con -Staging o -Test sus variantes)
#>
param(
    [string]$Apk,
    [string]$Carpeta = 'F:\Download\TeikemApp',
    [switch]$Staging,
    [switch]$Test,
    [ValidateSet('almacen', 'cliente')][string]$App = 'almacen'
)
if ($Staging -and $Test) { Write-Host 'Use -Staging o -Test, no los dos.' -ForegroundColor Red; exit 1 }
$prefijo = if ($App -eq 'cliente') { $(if ($Staging) { 'TeikemClientesStaging' } elseif ($Test) { 'TeikemClientesTest' } else { 'TeikemClientes' }) }
           else { $(if ($Staging) { 'TeikemStaging' } elseif ($Test) { 'TeikemTest' } else { 'TeikemAlmacen' }) }
$opciones = $(if ($App -eq 'cliente') { ' -App cliente' }) + $(if ($Staging) { ' -Staging' } elseif ($Test) { ' -Test' })

$ErrorActionPreference = 'Continue'
function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Fallar([string]$m) { Write-Host ''; Write-Host $m -ForegroundColor Red; exit 1 }

# --- APK ---
if (-not $Apk) {
    $ultimo = Get-ChildItem $Carpeta -Filter "$prefijo-*.apk" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $ultimo) { Fallar "No encuentro ningún $prefijo-*.apk en $Carpeta. Primero corra scripts\construir-apk.ps1$opciones (o use -Apk <archivo>)." }
    $Apk = $ultimo.FullName
}
if (-not (Test-Path $Apk)) { Fallar "No existe el archivo: $Apk" }
Write-Host "APK: $Apk"

# --- adb ---
$sdk = $env:ANDROID_HOME
if (-not $sdk) { $sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$adb = Join-Path $sdk 'platform-tools\adb.exe'
if (-not (Test-Path $adb)) { Fallar "No encuentro adb en $adb. Instale 'Android SDK Platform-Tools' con Android Studio." }

# --- aparatos conectados ---
Paso 'Aparatos conectados'
& $adb start-server | Out-Null
$lineas = & $adb devices
$seriales = @()
foreach ($l in $lineas) {
    $partes = ($l -split '\s+')
    if ($partes.Count -ge 2 -and $partes[1] -eq 'device') { $seriales += $partes[0] }
    elseif ($partes.Count -ge 2 -and $partes[1] -eq 'unauthorized') { Write-Host "  $($partes[0]): sin autorizar (acepte la pregunta «¿Permitir depuración USB?» en el aparato)" -ForegroundColor Yellow }
}
if ($seriales.Count -eq 0) { Fallar 'No hay aparatos listos. Conéctelos por USB, active la depuración USB y acepte la autorización en la pantalla.' }
foreach ($s in $seriales) { Write-Host "  $s" }

# --- instalar encima ---
$bien = 0; $mal = 0
foreach ($s in $seriales) {
    Paso "Actualizando $s (instalar encima, sin desinstalar)"
    $salida = & $adb -s $s install -r $Apk 2>&1 | Out-String
    Write-Host $salida.Trim()
    if ($salida -match 'Success') { $bien++; Write-Host "OK: $s actualizado; conserva su configuración." -ForegroundColor Green }
    else {
        $mal++
        if ($salida -match 'VERSION_DOWNGRADE') { Write-Host 'El aparato ya tiene una versión IGUAL o más nueva. Vuelva a correr construir-apk.ps1 (sube solo el código de versión) y repita. NO desinstale.' -ForegroundColor Red }
        elseif ($salida -match 'UPDATE_INCOMPATIBLE|signatures do not match') { Write-Host 'La firma no coincide: este APK se firmó con otra llave que el instalado. NO desinstale (se perdería la configuración): use la llave original (%USERPROFILE%\.teikem\teikem-release.jks).' -ForegroundColor Red }
        else { Write-Host 'No se pudo instalar (mensaje arriba). No se desinstaló nada.' -ForegroundColor Red }
    }
}
Write-Host ''
Write-Host "Resultado: $bien actualizado(s), $mal con problema." -ForegroundColor $(if ($mal -eq 0) { 'Green' } else { 'Yellow' })
if ($mal -gt 0) { exit 1 }
