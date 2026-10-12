<# : cmd
@echo off
setlocal
chcp 65001 >nul
rem  Restaura la base de PRODUCCION (Teikem) sobre la de STAGING (Teikem_Staging). Un solo archivo: copielo a cualquier parte DEL SERVIDOR,
rem  haga doble clic (o corralo) y siga las preguntas. Detalle y advertencias: dentro del bloque de PowerShell, mas abajo.
set "TEIKEM_BAT=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText($env:TEIKEM_BAT,[Text.Encoding]::UTF8); Invoke-Expression $s"
echo.
pause
exit /b
#>
<#
  restaurar-staging.bat (2026-10-12) — copia la base de producción (Teikem) sobre la de staging (Teikem_Staging).
  Qué hace, en orden:
    1. Respalda Teikem con COPY_ONLY (no afecta a producción ni a su cadena de respaldos) en la carpeta de respaldos de SQL Server.
    2. Detiene el grupo de aplicaciones de IIS de staging (TeikemStaging) para que nadie use la base mientras se reemplaza.
    3. Restaura ese respaldo SOBRE Teikem_Staging (WITH REPLACE: lo que tenía staging SE PIERDE) y deja de dueño al mismo dueño que Teikem.
    4. Copia las llaves de Data Protection de producción a las de staging: sin ellas los usuarios restaurados no pueden validar su MFA.
    5. Borra el respaldo (trae datos de producción), vuelve a encender el grupo de aplicaciones de staging.
  Seguridad: nunca escribe en Teikem (solo lo lee); el destino debe llevar «staging» en el nombre; pide escribir SI antes de reemplazar.
  Hay que correrlo EN EL SERVIDOR de SQL Server, como Administrador (IIS y las llaves están en esa máquina). Con autenticación de Windows
  su usuario necesita permiso de respaldar Teikem y restaurar sobre Teikem_Staging; si no, use un usuario SQL (el script lo pregunta).
  AVISOS tras restaurar:
    - Staging queda con los datos REALES de producción (usuarios, clientes, correos). Si staging tiene las llaves de Brevo, puede enviar
      correos reales. Para que no envíe, quite Brevo de appsettings.Production.local.json de staging.
    - Las sesiones abiertas de producción no sirven en staging (otra llave de sesiones): todos vuelven a entrar.
    - Si el paquete instalado en staging es más nuevo que producción, corra después instalar.ps1 de staging para llevar la base a su versión.
  Para correrlo sin preguntas (tareas programadas): defina la variable de entorno TEIKEM_SIN_PREGUNTAR=1 y TEIKEM_SQL_SERVIDOR (opcional).
#>

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# ----------------------------------------------------------------------------------------------------------------- ajustes
$Servidor = 'localhost\sqlexpress'
$Origen = 'Teikem'
$Destino = 'Teikem_Staging'
$PoolIIS = 'TeikemStaging'
$LlavesProd = 'C:\ProgramData\Teikem\keys'
$LlavesStaging = 'C:\ProgramData\Teikem\keys-TeikemStaging'
if ($env:TEIKEM_SQL_SERVIDOR) { $Servidor = $env:TEIKEM_SQL_SERVIDOR }
$SinPreguntar = ($env:TEIKEM_SIN_PREGUNTAR -eq '1')

$Usuario = ''
$Clave = ''

function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Ok([string]$m) { Write-Host $m -ForegroundColor Green }
function Aviso([string]$m) { Write-Host $m -ForegroundColor Yellow }
function Corchetes([string]$n) { '[' + $n.Replace(']', ']]') + ']' }
function Literal([string]$s) { "N'" + $s.Replace("'", "''") + "'" }
function Texto-Seguro($seguro) { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro)) }

function Conexion {
    $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $b['Data Source'] = $Servidor
    $b['Initial Catalog'] = 'master'
    $b['TrustServerCertificate'] = $true
    $b['Connect Timeout'] = 15
    $b['Application Name'] = 'restaurar-staging'
    if ($Usuario) { $b['User ID'] = $Usuario; $b['Password'] = $Clave } else { $b['Integrated Security'] = $true }
    return (New-Object System.Data.SqlClient.SqlConnection($b.ConnectionString))
}
# Ejecuta una instrucción y devuelve la tabla de resultados (vacía si no devuelve filas). espera = segundos (0 = sin límite).
function Consulta([string]$sql, [int]$espera = 0) {
    $c = Conexion
    $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = $espera
        $t = New-Object System.Data.DataTable
        $t.Load($cmd.ExecuteReader())
        return , $t
    } finally { $c.Dispose() }
}
function Escalar([string]$sql) { $v = (Consulta $sql).Rows[0][0]; if ($v -is [DBNull]) { return $null } else { return $v } }

$poolParado = $false
$destinoEnUnSoloUsuario = $false
try {
    Write-Host ''
    Write-Host 'Restaurar la base de PRODUCCION sobre la de STAGING' -ForegroundColor Cyan
    Write-Host "  de: $Origen   a: $Destino" -ForegroundColor Cyan
    Write-Host ''
    if (-not $SinPreguntar) {
        $r = Read-Host "Servidor SQL [$Servidor]"; if ($r.Trim()) { $Servidor = $r.Trim() }
        $u = Read-Host 'Usuario SQL (Enter = usar mi usuario de Windows)'
        if ($u.Trim()) { $Usuario = $u.Trim(); $Clave = Texto-Seguro (Read-Host 'Contraseña' -AsSecureString) }
    }

    # --------------------------------------------------------------------------------------------------------- revisiones
    Paso 'Revisando'
    if ($Origen -ieq $Destino) { throw 'El origen y el destino son la misma base.' }
    if ($Destino -notmatch '(?i)staging|prueba|test') { throw "Por seguridad, la base destino debe llevar 'staging' en el nombre ($Destino)." }
    $mb = Escalar "SELECT CAST(SUM(size) * 8.0 / 1024 AS INT) FROM sys.master_files WHERE database_id = DB_ID($(Literal $Origen))"
    if ($null -eq $mb) { throw "No veo la base '$Origen' en $Servidor (¿nombre o servidor equivocado, o su usuario no tiene acceso?)." }
    Ok "Conectado a $Servidor. $Origen pesa unos $mb MB."
    $destinoExiste = ($null -ne (Escalar "SELECT DB_ID($(Literal $Destino))"))
    if ($destinoExiste) { Aviso "$Destino ya existe: se REEMPLAZA (lo que tiene hoy se pierde)." } else { Write-Host "$Destino no existe: se crea." }

    $carpeta = $null
    try { $carpeta = Escalar "DECLARE @d NVARCHAR(512); EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory', @d OUTPUT; SELECT @d" } catch { }
    if (-not $carpeta) { $carpeta = (Read-Host 'No pude saber la carpeta de respaldos de SQL Server. Escriba una carpeta en este equipo que SQL Server pueda escribir').Trim() }
    if (-not $carpeta -or -not (Test-Path $carpeta)) { throw "La carpeta de respaldos no existe en este equipo: '$carpeta'. Corra este archivo en el servidor de SQL Server." }
    $bak = Join-Path $carpeta "$Origen-para-$Destino.bak"

    if (-not $SinPreguntar) {
        Write-Host ''
        Aviso "Se va a REEMPLAZAR '$Destino' con una copia de '$Origen' (sobre $Servidor)."
        if ((Read-Host 'Escriba SI para continuar') -ine 'si') { Write-Host 'Cancelado. No se cambió nada.'; return }
    }

    # --------------------------------------------------------------------------------------------------------- 1. respaldo
    Paso "1/5 Respaldando $Origen (COPY_ONLY: no afecta a producción)"
    [void](Consulta "BACKUP DATABASE $(Corchetes $Origen) TO DISK = $(Literal $bak) WITH COPY_ONLY, INIT, STATS = 10")
    Ok "Respaldo listo: $bak"

    # Archivos del respaldo -> destino (los de Teikem_Staging si ya existe con la misma forma; si no, junto a los de Teikem)
    $lista = @((Consulta "RESTORE FILELISTONLY FROM DISK = $(Literal $bak)").Rows)
    $datos = @($lista | Where-Object { $_.Type -eq 'D' })
    $logs = @($lista | Where-Object { $_.Type -eq 'L' })
    $exD = @(); $exL = @()
    if ($destinoExiste) {
        $f = @((Consulta "SELECT type, physical_name FROM sys.master_files WHERE database_id = DB_ID($(Literal $Destino)) ORDER BY file_id").Rows)
        $exD = @($f | Where-Object { $_.type -eq 0 } | ForEach-Object { [string]$_.physical_name })
        $exL = @($f | Where-Object { $_.type -eq 1 } | ForEach-Object { [string]$_.physical_name })
    }
    $mover = @()
    for ($i = 0; $i -lt $datos.Count; $i++) {
        $logico = [string]$datos[$i].LogicalName
        if ($exD.Count -eq $datos.Count) { $ruta = $exD[$i] }
        else { $ext = if ($i -eq 0) { '.mdf' } else { "_$i.ndf" }; $ruta = Join-Path (Split-Path ([string]$datos[$i].PhysicalName)) ($Destino + $ext) }
        $mover += "MOVE $(Literal $logico) TO $(Literal $ruta)"
    }
    for ($i = 0; $i -lt $logs.Count; $i++) {
        $logico = [string]$logs[$i].LogicalName
        if ($exL.Count -eq $logs.Count) { $ruta = $exL[$i] }
        else { $ext = if ($i -eq 0) { '_log.ldf' } else { "_log$i.ldf" }; $ruta = Join-Path (Split-Path ([string]$logs[$i].PhysicalName)) ($Destino + $ext) }
        $mover += "MOVE $(Literal $logico) TO $(Literal $ruta)"
    }
    $duenoOrigen = Escalar "SELECT SUSER_SNAME(owner_sid) FROM sys.databases WHERE name = $(Literal $Origen)"

    # --------------------------------------------------------------------------------------------------------- 2. detener staging
    Paso "2/5 Deteniendo el sitio de staging en IIS ($PoolIIS)"
    try {
        Import-Module WebAdministration -ErrorAction Stop
        if ((Test-Path "IIS:\AppPools\$PoolIIS") -and ((Get-WebAppPoolState $PoolIIS).Value -eq 'Started')) { Stop-WebAppPool $PoolIIS; $poolParado = $true; Ok 'Detenido.' }
        else { Write-Host 'No estaba encendido (o no existe); sigo.' }
    } catch { Aviso "No pude detener el grupo de aplicaciones ($($_.Exception.Message)); sigo (se cierran las conexiones a la fuerza)." }

    # --------------------------------------------------------------------------------------------------------- 3. restaurar
    Paso "3/5 Restaurando sobre $Destino"
    if ($destinoExiste) {
        [void](Consulta "ALTER DATABASE $(Corchetes $Destino) SET SINGLE_USER WITH ROLLBACK IMMEDIATE")
        $destinoEnUnSoloUsuario = $true
    }
    [void](Consulta "RESTORE DATABASE $(Corchetes $Destino) FROM DISK = $(Literal $bak) WITH REPLACE, $($mover -join ', '), STATS = 10")
    [void](Consulta "ALTER DATABASE $(Corchetes $Destino) SET MULTI_USER")
    $destinoEnUnSoloUsuario = $false
    if ($duenoOrigen) {
        try { [void](Consulta "ALTER AUTHORIZATION ON DATABASE::$(Corchetes $Destino) TO $(Corchetes $duenoOrigen)") }
        catch { Aviso "No pude dejar a '$duenoOrigen' como dueño de $Destino ($($_.Exception.Message)). Hágalo a mano: ALTER AUTHORIZATION ON DATABASE::$Destino TO $duenoOrigen;" }
    }
    $tablas = Escalar "SELECT COUNT(*) FROM $(Corchetes $Destino).sys.tables"
    Ok "Restaurada: $Destino tiene $tablas tablas."

    # --------------------------------------------------------------------------------------------------------- 4. llaves
    Paso '4/5 Llaves de Data Protection (para el MFA de los usuarios restaurados)'
    if ((Test-Path $LlavesProd) -and (Test-Path $LlavesStaging)) {
        $copiar = $true
        if (-not $SinPreguntar) { $copiar = ((Read-Host 'Copiar las llaves de producción a staging (necesario para que el MFA funcione) (S/n)') -notmatch '^[nN]') }
        if ($copiar) { Copy-Item (Join-Path $LlavesProd '*') $LlavesStaging -Force; Ok 'Llaves copiadas.' } else { Aviso 'No se copiaron: los usuarios con MFA no podrán validarlo en staging.' }
    } else { Aviso "No encontré las carpetas de llaves ($LlavesProd / $LlavesStaging); sin ellas el MFA de los usuarios restaurados puede fallar." }

    # --------------------------------------------------------------------------------------------------------- 5. limpiar
    Paso '5/5 Limpiando'
    Remove-Item $bak -Force -ErrorAction SilentlyContinue
    if (Test-Path $bak) { Aviso "No pude borrar el respaldo ($bak); bórrelo a mano (trae datos de producción)." } else { Ok 'Respaldo borrado.' }

    Write-Host ''
    Ok "Listo: $Destino ahora es una copia de $Origen."
    Aviso 'Recuerde: staging tiene datos reales; si tiene Brevo configurado puede mandar correos reales. Si el paquete de staging es más nuevo que producción, corra instalar.ps1 de staging.'
} catch {
    Write-Host ''
    Write-Host "FALLÓ: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Producción ($Origen) no se modificó." -ForegroundColor Red
} finally {
    if ($destinoEnUnSoloUsuario) { try { [void](Consulta "ALTER DATABASE $(Corchetes $Destino) SET MULTI_USER") } catch { } }
    if ($poolParado) { try { Start-WebAppPool $PoolIIS; Write-Host "Sitio de staging ($PoolIIS) encendido de nuevo." -ForegroundColor Green } catch { Write-Host "No pude encender $PoolIIS; enciéndalo en IIS." -ForegroundColor Yellow } }
}
