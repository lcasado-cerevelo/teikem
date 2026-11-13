# Instantánea de la base de grabación del video (SQL Server Developer/Enterprise): fija el punto de partida y permite volver a él en menos de 30 segundos entre tomas.
#   -Crear   crea la instantánea Teikem_video_inicio (reemplaza una anterior).
#   -Volver  restaura la base a ese punto (el API de grabación debe estar APAGADO: se cierran las conexiones abiertas). La instantánea se conserva.
#   -Quitar  borra la instantánea.
# Ojo: la instantánea NO revierte los archivos de fotos y firmas (viven en Files__Root, fuera de la base): después de -Volver, restaurar también esa carpeta (ver LEEME.md).
# Guarda: solo bases Teikem_video*.
param(
    [string]$Base = 'Teikem_video',
    [string]$Servidor = 'localhost,1433',
    [switch]$Crear,
    [switch]$Volver,
    [switch]$Quitar
)
$ErrorActionPreference = 'Stop'
if ($Base -notlike 'Teikem_video*') { throw "Solo bases Teikem_video*; recibí '$Base'." }
if (@($Crear, $Volver, $Quitar | Where-Object { $_ }).Count -ne 1) { throw 'Indique exactamente una acción: -Crear, -Volver o -Quitar.' }
$snap = "${Base}_inicio"
function Sql([string]$q) {
    $out = & sqlcmd -S $Servidor -d master -E -C -I -b -h -1 -W -Q $q
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd falló: $q`n$out" }
    return $out
}
if ($Crear -or $Quitar) { Sql "IF DB_ID(N'$snap') IS NOT NULL DROP DATABASE [$snap]" | Out-Null }
if ($Crear) {
    $datos = Sql "SET NOCOUNT ON; SELECT CAST(name AS nvarchar(200)) COLLATE DATABASE_DEFAULT + N'|' + CAST(physical_name AS nvarchar(500)) COLLATE DATABASE_DEFAULT FROM sys.master_files WHERE database_id = DB_ID(N'$Base') AND type_desc = 'ROWS'"
    $partes = $datos | ForEach-Object { $n, $f = $_ -split '\|', 2; "(NAME = N'$($n.Trim())', FILENAME = N'$([IO.Path]::ChangeExtension($f.Trim(), $null).TrimEnd('.'))_inicio.ss')" }
    Sql "CREATE DATABASE [$snap] ON $($partes -join ', ') AS SNAPSHOT OF [$Base]" | Out-Null
    Write-Host "Instantánea [$snap] creada."
}
if ($Volver) {
    if ((Sql "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$snap'" | Select-Object -First 1).Trim() -ne '1') { throw "No existe la instantánea [$snap]: cree una con -Crear." }
    Sql "ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE" | Out-Null
    try { Sql "RESTORE DATABASE [$Base] FROM DATABASE_SNAPSHOT = N'$snap'" | Out-Null }
    finally { Sql "ALTER DATABASE [$Base] SET MULTI_USER" | Out-Null }
    Write-Host "Base [$Base] devuelta al punto de partida."
}
if ($Quitar) { Write-Host "Instantánea [$snap] borrada." }
