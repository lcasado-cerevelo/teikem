# Crea las variables de entorno de usuario con las que Teikem lee las bases de Colavora Courier (solo lectura):
#   ConnectionStrings__ColavoraCourier       -> base colavora_courier       (envíos, consignatarios, clientes, eventos, POD)
#   ConnectionStrings__ColavoraCourierLogin  -> base colavora_courier_login (usuarios, compañías)
# La contraseña se escribe en tu consola o se toma del appsettings.json del API de Colavora; NUNCA queda en el repositorio
# ni en la salida de este script.
#
# Uso interactivo:
#   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\crear-variable-colavora.ps1"
# Sin preguntas, tomando usuario y contraseña del appsettings.json del API de Colavora (ColavoraConnection):
#   powershell -ExecutionPolicy Bypass -File ".\scripts\crear-variable-colavora.ps1" -DesdeColavoraApi
#
# Detalle: docs/migracion/colavora/README.md
[CmdletBinding()]
param(
    # Toma User ID y Password de ConnectionStrings:ColavoraConnection del appsettings.json del API de Colavora, sin preguntar.
    [switch] $DesdeColavoraApi,
    [string] $Servidor = '172.31.53.133',
    [string] $BaseCourier = 'colavora_courier',
    [string] $BaseLogin = 'colavora_courier_login',
    [string] $AppSettingsColavora = 'F:\Visual Studio 2022\Projects\Colavora_Courier\Colavora New Project\ColavoraAPI\appsettings.json'
)

$ErrorActionPreference = 'Stop'

function Leer-ParteCadena([string] $cadena, [string[]] $claves) {
    foreach ($parte in ($cadena -split ';')) {
        $kv = $parte -split '=', 2
        if ($kv.Count -eq 2 -and ($claves -contains $kv[0].Trim())) { return $kv[1].Trim() }
    }
    return $null
}

if ($DesdeColavoraApi) {
    if (-not (Test-Path $AppSettingsColavora)) { throw "No existe $AppSettingsColavora" }
    $json = Get-Content $AppSettingsColavora -Raw | ConvertFrom-Json
    $origen = $json.ConnectionStrings.ColavoraConnection
    if ([string]::IsNullOrWhiteSpace($origen)) { throw 'El appsettings.json de Colavora no trae ConnectionStrings:ColavoraConnection.' }
    $servidorOrigen = Leer-ParteCadena $origen @('Data Source', 'Server')
    if ($servidorOrigen) { $Servidor = $servidorOrigen }
    $user = Leer-ParteCadena $origen @('User ID', 'User Id', 'Uid')
    $plain = Leer-ParteCadena $origen @('Password', 'Pwd')
    if (-not $user -or -not $plain) { throw 'La cadena del API de Colavora no trae usuario y contraseña de SQL Server.' }
    $auth = "User Id=$user;Password=$plain"
    Write-Host "Credenciales tomadas del API de Colavora (usuario $user, servidor $Servidor)."
} else {
    $s = Read-Host "Servidor de Colavora [$Servidor]"
    if (-not [string]::IsNullOrWhiteSpace($s)) { $Servidor = $s }
    $modo = Read-Host 'Autenticación: 1 = Windows (tu usuario de Windows), 2 = usuario de SQL Server [2]'
    if ($modo -eq '1') {
        $auth = 'Integrated Security=True'
    } else {
        $user = Read-Host 'Usuario de SQL Server [advancelogistics]'
        if ([string]::IsNullOrWhiteSpace($user)) { $user = 'advancelogistics' }
        $secure = Read-Host 'Contraseña' -AsSecureString
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        $auth = "User Id=$user;Password=$plain"
    }
}

$comun = "$auth;ApplicationIntent=ReadOnly;TrustServerCertificate=True;Encrypt=True;Connect Timeout=15"
$csCourier = "Server=$Servidor;Database=$BaseCourier;$comun"
$csLogin = "Server=$Servidor;Database=$BaseLogin;$comun"

function Probar([string] $nombre, [string] $cs, [string] $sql) {
    try {
        $cn = New-Object System.Data.SqlClient.SqlConnection $cs
        $cn.Open()
        $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql
        $n = $cmd.ExecuteScalar()
        $cn.Close()
        Write-Host ("Conectó a {0}: {1:N0} filas." -f $nombre, $n) -ForegroundColor Green
        return $true
    } catch {
        Write-Host "No conectó a ${nombre}: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

$ok1 = Probar $BaseCourier $csCourier 'SELECT COUNT(*) FROM dbo.shipments'
$ok2 = Probar $BaseLogin $csLogin 'SELECT COUNT(*) FROM dbo.users'
if (-not ($ok1 -and $ok2) -and -not $DesdeColavoraApi) {
    if ((Read-Host '¿Guardar las variables de todos modos? (s/N)') -ne 's') { exit 1 }
}

[Environment]::SetEnvironmentVariable('ConnectionStrings__ColavoraCourier', $csCourier, 'User')
[Environment]::SetEnvironmentVariable('ConnectionStrings__ColavoraCourierLogin', $csLogin, 'User')
Write-Host 'Listo: variables de usuario ConnectionStrings__ColavoraCourier y ConnectionStrings__ColavoraCourierLogin guardadas.' -ForegroundColor Green
Write-Host 'Las terminales y Visual Studio abiertos antes no las ven: ábrelos de nuevo.'
Write-Host "Para quitarlas:  [Environment]::SetEnvironmentVariable('ConnectionStrings__ColavoraCourier', `$null, 'User'); [Environment]::SetEnvironmentVariable('ConnectionStrings__ColavoraCourierLogin', `$null, 'User')"
if (-not ($ok1 -and $ok2)) { exit 1 }
