<#
  PÁGINA DE DESCARGAS DE LAS APPS (deploy\descargas-apk): arma, lista para subir, la carpeta con la página, los APK de producción y de staging, y el versions.json
  con la versión de TODAS las apps (producción, staging y test), a partir de los APK que hay en F:\Download\TeikemApp (el más reciente de cada una).
    · Producción y staging se COPIAN a apk\ con el nombre que espera la página (teikem-almacen.apk, teikem-staging.apk, teikem-portal.apk, teikem-movil.apk).
    · Los de TEST también se copian a apk (la página los baja de su propia carpeta; el servidor de test tiene además los suyos en /descargas/).
    · La app que no tenga APK todavía queda sin clave en versions.json: su tarjeta dice «Todavía no se publicó» y el botón se apaga.
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\versiones-descargas.ps1"
  Opciones:  -CarpetaApk <carpeta de los APK>   -Destino <carpeta que se sube al servidor>   -SinCopiar (solo escribe versions.json)
#>
param(
    [string]$CarpetaApk = 'F:\Download\TeikemApp',
    [string]$Destino = 'F:\Download\TeikemDescargas',
    [switch]$SinCopiar
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$pagina = Join-Path $repo 'deploy\descargas-apk'
if (-not (Test-Path $CarpetaApk)) { Write-Host "No encuentro la carpeta de los APK: $CarpetaApk" -ForegroundColor Red; exit 1 }

# clave de versions.json, patrón del APK (el guion después del nombre evita confundir TeikemClientes- con TeikemClientesTest-), y a dónde se copia (vacío = no se copia)
$apps = @(
    @{ clave = 'ultima';        nombre = 'Almacén (producción)';        patron = 'TeikemAlmacen-*.apk';       copia = 'teikem-almacen.apk' },
    @{ clave = 'staging';       nombre = 'Almacén STAGING';             patron = 'TeikemStaging-*.apk';       copia = 'teikem-staging.apk' },
    @{ clave = 'portal';        nombre = 'Portal del cliente (producción)'; patron = 'TeikemClientes-*.apk';  copia = 'teikem-portal.apk' },
    @{ clave = 'movil';         nombre = 'Advance App (producción)';    patron = 'TeikemMovil-*.apk';         copia = 'teikem-movil.apk' },
    @{ clave = 'test';          nombre = 'Almacén TEST';                patron = 'TeikemTest-*.apk';          copia = 'teikem-almacen-test.apk' },
    @{ clave = 'clientes-test'; nombre = 'Portal TEST';                 patron = 'TeikemClientesTest-*.apk';  copia = 'teikem-clientes-test.apk' },
    @{ clave = 'movil-test';    nombre = 'Advance App TEST';            patron = 'TeikemMovilTest-*.apk';     copia = 'teikem-movil-test.apk' }
)

if (-not $SinCopiar) {
    New-Item -ItemType Directory -Force (Join-Path $Destino 'apk') | Out-Null
    foreach ($f in 'index.html', 'logo.png', 'favicon.ico') { Copy-Item (Join-Path $pagina $f) $Destino -Force }
}

# Se parte de lo ya publicado (versions.json del destino, o el de la página si es la primera vez): una app sin APK en esta carpeta conserva la versión que ya tenía
$previo = Join-Path $Destino 'versions.json'
if (-not (Test-Path $previo)) { $previo = Join-Path $pagina 'versions.json' }
$versiones = [ordered]@{}
if (Test-Path $previo) { (Get-Content $previo -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $versiones[$_.Name] = [string]$_.Value } }
foreach ($a in $apps) {
    $apk = Get-ChildItem $CarpetaApk -Filter $a.patron -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $apk) { Write-Host ("  {0,-34} sin APK ({1})" -f $a.nombre, $a.patron) -ForegroundColor Yellow; continue }
    # TeikemMovilTest-1.3.0-c2.apk -> "1.3.0 (c2)"; TeikemAlmacen-1.0.0.apk (el primero, sin código) -> "1.0.0"
    $v = if ($apk.BaseName -match '-(\d+\.\d+\.\d+)-c(\d+)$') { "$($Matches[1]) (c$($Matches[2]))" } elseif ($apk.BaseName -match '-(\d+\.\d+\.\d+)$') { $Matches[1] } else { $apk.BaseName }
    $versiones[$a.clave] = $v
    $destinoApk = ''
    if ($a.copia -and -not $SinCopiar) { Copy-Item $apk.FullName (Join-Path $Destino ('apk\' + $a.copia)) -Force; $destinoApk = " -> apk\$($a.copia)" }
    Write-Host ("  {0,-34} {1}  ({2}){3}" -f $a.nombre, $v, $apk.Name, $destinoApk) -ForegroundColor Green
}

$json = if ($versiones.Count -gt 0) { $versiones | ConvertTo-Json } else { '{}' }
Write-Host ''
if ($SinCopiar) {
    Write-Host 'versions.json que saldría (con -SinCopiar no se escribe nada):' -ForegroundColor Cyan
    Write-Host $json
} else {
    $salida = Join-Path $Destino 'versions.json'
    [IO.File]::WriteAllText($salida, $json, (New-Object Text.UTF8Encoding $false))
    Write-Host "versions.json escrito en $salida" -ForegroundColor Cyan
    Write-Host "Suba el contenido completo de $Destino al servidor de la página de descargas (index.html, logo, favicon, versions.json y apk\)." -ForegroundColor Cyan
}
