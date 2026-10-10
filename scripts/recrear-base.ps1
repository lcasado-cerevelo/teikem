<#
  *** PROHIBIDO FUERA DE DESARROLLO: borra la base. Producción desde 2026-10-09; allá solo db-update con Diseño/logistica-db-update.sql. ***
  Recrea DESDE CERO la base local de Teikem y vuelve a migrar las dos compañías:
    - Advance Depot:     QuickBooks (CSV) + inventario del WMS MSWM de PRODUCCIÓN (variable ConnectionStrings__LegacyMswm).
    - Advance Solutions: QuickBooks (CSV).
  Siempre recrea: borra TODO lo que haya en la base local (también la demo Advance Logistics, aparatos registrados y PIN).
  Del MSWM de producción solo se LEE.

  Uso (desde cualquier carpeta):
    powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\recrear-base.ps1"
  Opciones:
    -Carpeta <ruta>   carpeta con los 6 CSV de QuickBooks (por defecto F:\Download\TeikemMigracion)
    -SinConfirmar     no pregunta antes de borrar
  Detalle: docs/migracion/recrear-base.md
#>
param(
    [string]$Carpeta = 'F:\Download\TeikemMigracion',
    [switch]$SinConfirmar
)

# === PROHIBIDO FUERA DE DESARROLLO (producción desde 2026-10-09) ===
# Este script BORRA la base. Solo se corre en la máquina de desarrollo contra una base LOCAL. Jamás en producción, staging ni un servidor remoto:
# allá los cambios de base van en Diseño/logistica-db-update.sql y se aplican con db-update (instalador).
if ($env:ASPNETCORE_ENVIRONMENT -and $env:ASPNETCORE_ENVIRONMENT -ne 'Development') {
    Write-Host 'PROHIBIDO: este script solo corre en desarrollo (ASPNETCORE_ENVIRONMENT debe ser Development). No se borró nada.' -ForegroundColor Red
    exit 2
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$api = 'src/Teikem.Api'
$inicio = Get-Date

function Fallar([string]$msg) { Write-Host ''; Write-Host $msg -ForegroundColor Red; exit 1 }
function Paso([string]$msg) { Write-Host ''; Write-Host "== $msg" -ForegroundColor Cyan }

# --- 1. Conexión al MSWM de producción (la variable de usuario la crea scripts\crear-variable-mswm.ps1) ---
Paso 'Revisando la conexión al MSWM de producción'
if (-not $env:ConnectionStrings__LegacyMswm) {
    $env:ConnectionStrings__LegacyMswm = [Environment]::GetEnvironmentVariable('ConnectionStrings__LegacyMswm', 'User')
}
if (-not $env:ConnectionStrings__LegacyMswm) {
    Fallar 'Falta la variable ConnectionStrings__LegacyMswm. Créala con scripts\crear-variable-mswm.ps1 y abre otra PowerShell.'
}
try {
    $cn = New-Object System.Data.SqlClient.SqlConnection $env:ConnectionStrings__LegacyMswm
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = "SELECT (SELECT COUNT(*) FROM dbo.Item), (SELECT COUNT(*) FROM dbo.Inventory WHERE WarehouseId = 'Main' AND OnHandQuantity <> 0)"
    $r = $cmd.ExecuteReader(); [void]$r.Read()
    Write-Host ("MSWM ({0} / {1}): {2:N0} artículos; {3:N0} existencias en Main." -f $cn.DataSource, $cn.Database, $r.GetValue(0), $r.GetValue(1)) -ForegroundColor Green
    $cn.Close()
} catch {
    Fallar "No se pudo conectar al MSWM: $($_.Exception.Message)`nNo se borró nada."
}

# --- 2. Archivos de QuickBooks ---
Paso "Revisando los archivos de QuickBooks en $Carpeta"
$archivos = 'Depot Products.csv', 'Depot Customers.csv', 'Depot Vendor.csv',
            'Solutions Items.csv', 'Solutions Customers.csv', 'Solutions Vendors.csv'
$faltan = $archivos | Where-Object { -not (Test-Path (Join-Path $Carpeta $_)) }
if ($faltan) { Fallar ("Faltan en ${Carpeta}:`n  " + ($faltan -join "`n  ") + "`nNo se borró nada.") }
foreach ($a in $archivos) {
    $f = Get-Item (Join-Path $Carpeta $a)
    Write-Host ("  {0,-26} {1,10:N0} bytes  {2:yyyy-MM-dd HH:mm}" -f $a, $f.Length, $f.LastWriteTime)
}

# Los JSON del repo apuntan a F:/Download/TeikemMigracion; si la carpeta es otra, se usan copias temporales con la ruta cambiada.
$carpetaJson = ($Carpeta.TrimEnd('\', '/')) -replace '\\', '/'
$configs = @('docs/migracion/import.depot.json', 'docs/migracion/import.solutions.json')
if ($carpetaJson -ne 'F:/Download/TeikemMigracion') {
    $tmp = Join-Path $env:TEMP 'teikem-recrear'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $configs = $configs | ForEach-Object {
        $destino = Join-Path $tmp (Split-Path -Leaf $_)
        $texto = [IO.File]::ReadAllText((Join-Path $repo $_)).Replace('F:/Download/TeikemMigracion', $carpetaJson)
        [IO.File]::WriteAllText($destino, $texto, (New-Object Text.UTF8Encoding $false))
        $destino
    }
}

# --- 3. El API no puede estar corriendo (la base se borra con ROLLBACK IMMEDIATE) ---
$escuchando = Get-NetTCPConnection -LocalPort 5000, 5001 -State Listen -ErrorAction SilentlyContinue
if ($escuchando) { Fallar 'El API está corriendo (puerto 5000/5001). Detenlo y vuelve a correr este script. No se borró nada.' }

# --- 4. Confirmación ---
if (-not $SinConfirmar) {
    Write-Host ''
    Write-Host 'Esto BORRA la base local Teikem completa (demo, Depot, Solutions, aparatos, PIN) y la vuelve a crear.' -ForegroundColor Yellow
    if ((Read-Host 'Escribe RECREAR para continuar') -cne 'RECREAR') { Fallar 'Cancelado. No se borró nada.' }
}

$log = Join-Path $Carpeta ('recrear-' + $inicio.ToString('yyyyMMdd-HHmm') + '.log')
function Correr([string]$titulo, [string[]]$argumentos) {
    Paso $titulo
    "`r`n== $titulo  ($(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))" | Out-File -FilePath $log -Append -Encoding utf8
    & dotnet run --project $api --no-build -- @argumentos 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log -Append | Out-Host
    return $LASTEXITCODE
}

# --- 5. Compilar, recrear e importar ---
Paso 'Compilando el API'
& dotnet build $api -nologo -v q
if ($LASTEXITCODE -ne 0) { Fallar 'No compiló. No se borró nada.' }

if ((Correr 'Recreando la base (db-reset)' @('db-reset', '--yes')) -ne 0) { Fallar "db-reset falló; revisa $log" }
$depot = Correr 'Migrando Advance Depot (QuickBooks + MSWM de producción)' @('import-legacy', $configs[0])
$solutions = Correr 'Migrando Advance Solutions (QuickBooks)' @('import-legacy', $configs[1])

# --- 6. Resumen ---
Paso 'Resumen'
$informes = Get-ChildItem $Carpeta -Filter 'reporte-*.md' | Where-Object { $_.LastWriteTime -ge $inicio } | Sort-Object Name
foreach ($i in $informes) { Write-Host "  Informe: $($i.FullName)" }
Write-Host "  Bitácora: $log"
$ok = ($depot -eq 0) -and ($solutions -eq 0)
if ($ok) {
    Write-Host ''
    Write-Host 'Listo: base recreada, Depot y Solutions migradas sin rechazos graves.' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host ("Terminó con rechazos graves (Depot: {0}, Solutions: {1}; 0 = bien). Revisa los informes." -f $depot, $solutions) -ForegroundColor Red
}
Write-Host 'Recuerda: arranca el API y, para la app, registra otra vez el aparato (Sistema > Aparatos) y asigna los PIN (Sistema > Usuarios).'
if (-not $ok) { exit 1 }
