<#
  Crea el paquete para Windows Server + IIS (en SU computadora): compila la web y el API y deja un .zip con todo y el instalador.
  Salida: F:\Download\TeikemDeploy\teikem-windows-AAAAMMDD-HHMM.zip
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\deploy\windows\crear-paquete-windows.ps1"
  Detalle: deploy/windows/README.md
#>
param([string]$Salida = 'F:\Download\TeikemDeploy')

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Fallar([string]$m) { Write-Host ''; Write-Host $m -ForegroundColor Red; exit 1 }

$sucio = git -C $repo status --porcelain -- src web-app deploy Diseño
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
Copy-Item (Join-Path $PSScriptRoot 'instalar.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'README.md') $stage

New-Item -ItemType Directory -Force $Salida | Out-Null
$zip = Join-Path $Salida ('teikem-windows-' + (Get-Date -Format 'yyyyMMdd-HHmm') + '.zip')
Paso 'Comprimiendo'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ''
Write-Host "Paquete listo: $zip ($mb MB)" -ForegroundColor Green
Write-Host ''
Write-Host 'Siguientes pasos:' -ForegroundColor Cyan
Write-Host '  1) Copie el .zip al servidor (Escritorio remoto: pegar archivo, o  scp / carpeta compartida).'
Write-Host '  2) En el servidor: descomprímalo (Expand-Archive) y, en una PowerShell como Administrador, dentro de esa carpeta:'
Write-Host '       powershell -ExecutionPolicy Bypass -File .\instalar.ps1'
Write-Host '     (para actualizar una versión: lo mismo con el .zip nuevo; también actualiza la base: simula, pide el respaldo y aplica)'
