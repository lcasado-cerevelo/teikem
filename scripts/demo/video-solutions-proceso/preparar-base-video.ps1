# Prepara la base de grabación del video de Advance Solutions (docs/video/solutions-proceso/PLAN.md): una COPIA de la base de desarrollo, nunca la base de uso diario.
# Hace, en este orden (todo idempotente salvo -Recrear, que reemplaza la copia):
#   1. (solo con -Recrear) respaldo solo-copia de la base de origen y restauración como Teikem_video (SQL Server local; modelo de recuperación simple).
#   2. Scripts de scripts/solutions-pedido-vendedores aplicados con @Aplicar = 1: 01 (marcas), 03 (roles), 02 (kit OFERTA1), 08 (envío y QuickBooks), 10 (ya enviados a QuickBooks).
#      El 09 NO se corre (crea reglas de aviso con correo).
#   3. 90-proveedores-billing.sql (proveedores de las compras de QuickBooks, módulo BILLING, oferta de prueba apagada) y 91-almacen-recibo-directo.sql.
#   4. db-update de la copia (tablas y permisos de los lotes recientes, p. ej. el Lote 241 de Análisis de ventas) con el API SIN correo.
# Guardas: la base de destino debe llamarse Teikem_video*; los scripts SQL abortan si DB_NAME() no es Teikem_video*. Nunca toca TEST, producción ni la base de origen (solo la lee en el respaldo).
#   Uso (SIEMPRE con la guardia de recursos, R8):
#     powershell -File scripts\demo\video-solutions-proceso\guardia-recursos.ps1 -Archivo scripts\demo\video-solutions-proceso\preparar-base-video.ps1 -ArgsArchivo "-Recrear"
#   Parámetros: [-Recrear] [-Base Teikem_video] [-Origen Teikem] [-SinDbUpdate]
#   Luego: copiar el almacén de archivos (fotos) a F:\Download\TeikemVideo\base\files (ver LEEME.md) y, con los datos listos, crear la instantánea (instantanea-base-video.ps1 -Crear).
param(
    [string]$Base = 'Teikem_video',
    [string]$Origen = 'Teikem',
    [string]$Servidor = 'localhost,1433',
    [switch]$Recrear,
    [switch]$SinDbUpdate
)
$ErrorActionPreference = 'Stop'
if ($Base -notlike 'Teikem_video*') { throw "La base de video debe llamarse Teikem_video*; recibí '$Base'." }
if ($Base -eq $Origen) { throw 'La base de video no puede ser la de origen.' }
$raiz = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$ventas = Join-Path $raiz 'scripts\solutions-pedido-vendedores'
$tmp = Join-Path $env:TEMP "preparar-base-video-$([guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Force $tmp | Out-Null

function Sql([string]$db, [string]$q) {
    $out = & sqlcmd -S $Servidor -d $db -E -C -I -b -h -1 -W -Q $q
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd falló: $q`n$out" }
    return $out
}
function SqlArchivo([string]$db, [string]$dir, [string]$archivo) {
    # sqlcmd en esta máquina solo abre archivos con ruta relativa desde su carpeta
    Push-Location $dir
    try {
        $out = & sqlcmd -S $Servidor -d $db -E -C -I -b -f 65001 -i $archivo
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falló con $archivo`n$out" }
        $out | Where-Object { $_ -match '^\s*(APLICADO|ABORTADO|SIMULACI)|Proveedores creados|BILLING encendido|Ofertas de prueba|Almac.n actualizado' } | ForEach-Object { Write-Host ("    " + $_.Substring(0, [Math]::Min(150, $_.Length))) }
    } finally { Pop-Location }
}

if ($Recrear) {
    Write-Host "1. Respaldo solo-copia de [$Origen] y restauración como [$Base]..."
    $rutaBackup = (Sql 'master' "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(300))" | Select-Object -First 1).Trim()
    $rutaDatos = (Sql 'master' "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(300))" | Select-Object -First 1).Trim()
    $archivos = Sql 'master' "SET NOCOUNT ON; SELECT CAST(name AS nvarchar(200)) COLLATE DATABASE_DEFAULT + N'|' + CAST(type_desc AS nvarchar(60)) COLLATE DATABASE_DEFAULT FROM sys.master_files WHERE database_id = DB_ID(N'$Origen')"
    $datosLogico = (($archivos | Where-Object { $_ -match '\|ROWS' } | Select-Object -First 1) -split '\|')[0].Trim()
    $logLogico = (($archivos | Where-Object { $_ -match '\|LOG' } | Select-Object -First 1) -split '\|')[0].Trim()
    if (-not $datosLogico -or -not $logLogico) { throw "No encuentro los archivos de [$Origen] (¿existe la base?)." }
    $bak = Join-Path $rutaBackup "${Base}_base.bak"
    Sql 'master' "BACKUP DATABASE [$Origen] TO DISK = N'$bak' WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT, NAME = N'${Base}_base (copia para el video)'" | Out-Null
    # una instantánea impide borrar la base: se quita primero
    Sql 'master' "IF DB_ID(N'${Base}_inicio') IS NOT NULL DROP DATABASE [${Base}_inicio]" | Out-Null
    Sql 'master' "IF DB_ID(N'$Base') IS NOT NULL BEGIN ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Base]; END" | Out-Null
    Sql 'master' "RESTORE DATABASE [$Base] FROM DISK = N'$bak' WITH MOVE N'$datosLogico' TO N'${rutaDatos}$Base.mdf', MOVE N'$logLogico' TO N'${rutaDatos}${Base}_log.ldf'" | Out-Null
    Sql 'master' "ALTER DATABASE [$Base] SET RECOVERY SIMPLE WITH NO_WAIT" | Out-Null
    Sql $Base "DBCC SHRINKFILE (N'$logLogico', 64) WITH NO_INFOMSGS" | Out-Null
    Write-Host '   Copia restaurada.'
} else {
    if (-not (Sql 'master' "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$Base'" | Select-Object -First 1).Trim() -eq '1') { throw "No existe [$Base]: corra con -Recrear." }
    Write-Host "1. Se usa la copia existente [$Base] (sin -Recrear)."
}

Write-Host '2. Scripts de Advance Solutions con @Aplicar = 1 (01, 03, 02, 08, 10)...'
foreach ($n in '01-marcas', '03-roles', '02-kit-oferta1', '08-ajustes-envio-quickbooks', '10-quickbooks-ya-mandados') {
    $origenSql = Join-Path $ventas "$n.sql"
    if (-not (Test-Path $origenSql)) { throw "No encuentro $origenSql" }
    $destino = Join-Path $tmp "$n.apply.sql"
    $texto = [IO.File]::ReadAllText($origenSql, [Text.Encoding]::UTF8)
    $nuevo = [regex]::Replace($texto, '(?m)^DECLARE @Aplicar\s+BIT = 0;', 'DECLARE @Aplicar  BIT = 1;')
    if ($nuevo -eq $texto) { throw "$n no trae la línea DECLARE @Aplicar BIT = 0;" }
    [IO.File]::WriteAllText($destino, $nuevo, (New-Object Text.UTF8Encoding($true)))
    Write-Host "  - $n"
    SqlArchivo $Base $tmp "$n.apply.sql"
}

Write-Host '3. Proveedores, BILLING, oferta de prueba y almacén en recibo directo...'
SqlArchivo $Base $PSScriptRoot '90-proveedores-billing.sql'
SqlArchivo $Base $PSScriptRoot '91-almacen-recibo-directo.sql'

if (-not $SinDbUpdate) {
    Write-Host '4. db-update de la copia (API SIN correo)...'
    $local = Join-Path $raiz 'src\Teikem.Api\appsettings.Development.local.json'
    if (-not (Test-Path $local)) { throw "No encuentro $local (ahí vive la cadena de conexión de esta máquina)." }
    $json = Get-Content $local -Raw -Encoding UTF8 | ConvertFrom-Json
    $cadena = [regex]::Replace($json.ConnectionStrings.Teikem, '(?i)(Database|Initial Catalog)=[^;]*', "`$1=$Base")
    $env:ConnectionStrings__Teikem = $cadena
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Brevo__ApiKey = ''
    $env:Auth__Onboarding__Enabled = 'false'
    Push-Location $raiz
    try { dotnet run -c Release --project src\Teikem.Api --no-launch-profile -p:UseSharedCompilation=false -p:RunAnalyzers=false -- db-update } finally { Pop-Location }
} else { Write-Host '4. db-update omitido (-SinDbUpdate).' }

Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
Write-Host "Listo: [$Base] preparada. Siguiente: usuarios y clientes de demostración por el API, y la instantánea (instantanea-base-video.ps1 -Crear)."
