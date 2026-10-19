<#
  Crea el paquete para Windows Server + IIS (en SU computadora): compila la web y el API y deja un .zip con todo y el instalador.
  Salida: F:\Download\TeikemDeploy\teikem-windows-AAAAMMDD-HHMM.zip
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\deploy\windows\crear-paquete-windows.ps1"
  Detalle: deploy/windows/README.md
  AMBIENTE DE TEST (2026-10-19):  -Test  arma el paquete del ambiente de test (teikem-test-AAAAMMDD-HHMM.zip): lo mismo que el normal (API + web +
    scripts de la base + instalar.ps1) MÁS el portal del cliente compilado para la web (app-cliente, apuntando a https://test.advancelogisticspr.com,
    con la cinta TEST) en «portal\», la página de descargas de las apps de test en «descargas\» con los APK más recientes de
    F:\Download\TeikemApp (TeikemTest-*.apk y TeikemClientesTest-*.apk, si los hay), letsencrypt.ps1 e instalar-test.ps1 (lo que se corre en el servidor).
    -DominioTest / -DominioPortalTest cambian los nombres; -CarpetaApk, de dónde toma los APK.
#>
param(
    [string]$Salida = 'F:\Download\TeikemDeploy',
    [switch]$Test,
    [string]$DominioTest = 'test.advancelogisticspr.com',
    [string]$DominioPortalTest = 'test-portal.advancelogisticspr.com',
    [string]$CarpetaApk = 'F:\Download\TeikemApp'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Fallar([string]$m) { Write-Host ''; Write-Host $m -ForegroundColor Red; exit 1 }

$sucio = git -C $repo status --porcelain -- src web-app deploy Diseño $(if ($Test) { 'app-cliente' })
if ($sucio) {
    Write-Host 'Hay cambios sin commit en lo que va al paquete (el paquete se compila de lo que hay en su carpeta):' -ForegroundColor Yellow
    $sucio | ForEach-Object { Write-Host "  $_" }
    if ((Read-Host '¿Continuar? (s/N)') -ne 's') { exit 1 }
}

Paso 'Compilando la web'
Push-Location (Join-Path $repo 'web-app')
if (-not (Test-Path 'node_modules')) { npm ci; if ($LASTEXITCODE -ne 0) { Fallar 'npm ci falló.' } }
npm run build
if ($LASTEXITCODE -ne 0) { Fallar 'La compilación de la web falló.' }
Pop-Location

$stage = Join-Path $env:TEMP 'teikem-paquete-windows'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'app') | Out-Null

Paso 'Publicando el API'
dotnet publish (Join-Path $repo 'src\Teikem.Api\Teikem.Api.csproj') -c Release -o (Join-Path $stage 'app') -p:UseAppHost=false --nologo -v q
if ($LASTEXITCODE -ne 0) { Fallar 'dotnet publish falló.' }

Paso 'Juntando la web con el API (wwwroot)'
Copy-Item (Join-Path $repo 'web-app\dist') (Join-Path $stage 'app\wwwroot') -Recurse
# por seguridad: nada de configuraciones locales ni de desarrollo en el paquete
Get-ChildItem (Join-Path $stage 'app') -Filter 'appsettings.*.json' | Remove-Item -Force
# Scripts de la base (los usa «db-update» del instalador para dejar la base al día; el mismo set de scripts de todo el proyecto)
New-Item -ItemType Directory -Force (Join-Path $stage 'db') | Out-Null
Copy-Item (Join-Path $repo 'Diseño\logistica-db-estructura.sql') (Join-Path $stage 'db')
Copy-Item (Join-Path $repo 'Diseño\logistica-db-seed.sql') (Join-Path $stage 'db')
# cambios posteriores a la producción: UN solo archivo idempotente (estructura y datos); db-update lo aplica después de estructura y seed
Copy-Item (Join-Path $repo 'Diseño\logistica-db-update.sql') (Join-Path $stage 'db')
Copy-Item (Join-Path $PSScriptRoot 'instalar.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'letsencrypt.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'README.md') $stage

if ($Test) {
    # ----- Portal del cliente (app-cliente) compilado para la web, apuntando al API de test y con la variante 'test' (cinta TEST) -----
    Paso "Compilando el portal del cliente para la web (https://$DominioPortalTest -> API https://$DominioTest)"
    Push-Location (Join-Path $repo 'app-cliente')
    if (-not (Test-Path 'node_modules')) { npm ci --legacy-peer-deps; if ($LASTEXITCODE -ne 0) { Fallar 'npm ci (app-cliente) falló.' } }
    $env:APP_VARIANT = 'test'
    $env:EXPO_PUBLIC_API_URL = "https://$DominioTest"
    $env:TEIKEM_API_URL = "https://$DominioTest"
    $env:TEIKEM_BUILD_STAMP = "$(Get-Date -Format 'yyyy-MM-dd HH:mm') $(git -C $repo rev-parse --short HEAD)"
    $env:CI = '1'
    if (Test-Path 'dist') { Remove-Item 'dist' -Recurse -Force }
    npx expo export --platform web --output-dir dist
    $codigo = $LASTEXITCODE
    Remove-Item Env:APP_VARIANT, Env:EXPO_PUBLIC_API_URL, Env:TEIKEM_API_URL, Env:TEIKEM_BUILD_STAMP -ErrorAction SilentlyContinue
    Pop-Location
    if ($codigo -ne 0 -or -not (Test-Path (Join-Path $repo 'app-cliente\dist\index.html'))) { Fallar 'La compilación del portal del cliente (expo export) falló.' }
    Copy-Item (Join-Path $repo 'app-cliente\dist') (Join-Path $stage 'portal') -Recurse

    # ----- Página de descargas de las apps de test y los APK más recientes -----
    Paso 'Página de descargas de las apps de test'
    $desc = Join-Path $stage 'descargas'
    New-Item -ItemType Directory -Force (Join-Path $desc 'apk') | Out-Null
    Copy-Item (Join-Path $repo 'deploy\descargas-test\index.html') $desc
    Copy-Item (Join-Path $repo 'deploy\descargas-apk\logo.png') $desc
    Copy-Item (Join-Path $repo 'deploy\descargas-apk\favicon.ico') $desc
    $versiones = [ordered]@{}
    foreach ($par in @(@{ clave = 'almacen'; patron = 'TeikemTest-*.apk'; destino = 'teikem-almacen-test.apk' }, @{ clave = 'clientes'; patron = 'TeikemClientesTest-*.apk'; destino = 'teikem-clientes-test.apk' })) {
        $apk = Get-ChildItem $CarpetaApk -Filter $par.patron -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
        if ($apk) {
            Copy-Item $apk.FullName (Join-Path $desc ('apk\' + $par.destino))
            # TeikemTest-1.2.0-c7.apk -> "1.2.0 (c7)"
            $v = if ($apk.BaseName -match '-(\d+\.\d+\.\d+)-c(\d+)$') { "$($Matches[1]) (c$($Matches[2]))" } else { $apk.BaseName }
            $versiones[$par.clave] = $v
            Write-Host "  $($par.clave): $($apk.Name) -> apk\$($par.destino) ($v)"
        } else {
            Write-Host "  $($par.clave): no hay $($par.patron) en $CarpetaApk (la tarjeta dirá que aún no se publicó; constrúyalo con scripts\construir-apk.ps1$(if ($par.clave -eq 'clientes') { ' -App cliente' }) -Test)" -ForegroundColor Yellow
        }
    }
    [IO.File]::WriteAllText((Join-Path $desc 'versions.json'), ($versiones | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    if ($versiones.Count -eq 0) { [IO.File]::WriteAllText((Join-Path $desc 'versions.json'), '{}', (New-Object Text.UTF8Encoding $false)) }

    Copy-Item (Join-Path $PSScriptRoot 'instalar-test.ps1') $stage
}

New-Item -ItemType Directory -Force $Salida | Out-Null
$zip = Join-Path $Salida (($(if ($Test) { 'teikem-test-' } else { 'teikem-windows-' })) + (Get-Date -Format 'yyyyMMdd-HHmm') + '.zip')
Paso 'Comprimiendo'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ''
Write-Host "Paquete listo: $zip ($mb MB)" -ForegroundColor Green
Write-Host ''
Write-Host 'Siguientes pasos:' -ForegroundColor Cyan
Write-Host '  1) Copie el .zip al servidor (Escritorio remoto: pegar archivo, o  scp / carpeta compartida).'
Write-Host '  2) En el servidor: descomprímalo (Expand-Archive) y, en una PowerShell como Administrador, dentro de esa carpeta:'
if ($Test) {
    Write-Host '       powershell -ExecutionPolicy Bypass -File .\instalar-test.ps1'
    Write-Host "     (deja https://$DominioTest, https://$DominioPortalTest y https://$DominioTest/descargas/ con certificados de Let's Encrypt que se renuevan solos)"
} else {
    Write-Host '       powershell -ExecutionPolicy Bypass -File .\instalar.ps1'
}
Write-Host '     (para actualizar una versión: lo mismo con el .zip nuevo; también actualiza la base: simula, pide el respaldo y aplica)'
