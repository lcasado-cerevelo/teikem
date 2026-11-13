# Estados nombrados de la base de grabación: cada escena parte del estado en que dejó la anterior.
#   -Guardar E02   respalda Teikem_video como estado «E02» (BACKUP COPY_ONLY al respaldo por omisión de SQL Server).
#   -Cargar  E02   restaura ese estado (el API de grabación debe estar APAGADO; se quita la instantánea que estorba).
# Solo bases Teikem_video*.
param([string]$Base = 'Teikem_video', [string]$Servidor = 'localhost,1433', [string]$Guardar, [string]$Cargar)
$ErrorActionPreference = 'Stop'
if ($Base -notlike 'Teikem_video*') { throw "Solo bases Teikem_video*; recibí '$Base'." }
function Sql([string]$q) { $o = & sqlcmd -S $Servidor -d master -E -C -I -b -h -1 -W -Q $q; if ($LASTEXITCODE -ne 0) { throw "sqlcmd falló: $q`n$o" }; $o }
$ruta = (Sql "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(300))" | Select-Object -First 1).Trim()
$name = if ($Guardar) { $Guardar } else { $Cargar }
if (-not $name) { throw 'Indique -Guardar <nombre> o -Cargar <nombre>.' }
$bak = Join-Path $ruta "${Base}_estado_$name.bak"
if ($Guardar) {
    Sql "BACKUP DATABASE [$Base] TO DISK = N'$bak' WITH COPY_ONLY, COMPRESSION, INIT, NAME = N'estado $name'" | Out-Null
    Write-Host "Estado [$name] guardado."
} else {
    Sql "IF DB_ID(N'${Base}_inicio') IS NOT NULL DROP DATABASE [${Base}_inicio]" | Out-Null
    Sql "ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE" | Out-Null
    try { Sql "RESTORE DATABASE [$Base] FROM DISK = N'$bak' WITH REPLACE" | Out-Null } finally { Sql "ALTER DATABASE [$Base] SET MULTI_USER" | Out-Null }
    Write-Host "Estado [$name] cargado."
}
