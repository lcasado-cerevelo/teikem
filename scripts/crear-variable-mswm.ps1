# Crea la variable de entorno de usuario ConnectionStrings__LegacyMswm para que Teikem lea el MSWM de producción.
# La contraseña se escribe aquí, en tu consola; no queda en ningún archivo del repositorio.
# Uso:  powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\crear-variable-mswm.ps1"
# Detalle: docs/migracion/recrear-base.md

$server = Read-Host 'Servidor de MSWM (ej. 192.168.1.20,1433 o SERVIDOR\INSTANCIA)'
$db = Read-Host 'Base de datos [MSWM]'
if ([string]::IsNullOrWhiteSpace($db)) { $db = 'MSWM' }

$modo = Read-Host 'Autenticación: 1 = Windows (tu usuario de Windows), 2 = usuario de SQL Server [2]'
if ($modo -eq '1') {
    $auth = 'Integrated Security=True'
} else {
    $user = Read-Host 'Usuario de SQL Server'
    $secure = Read-Host 'Contraseña' -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    $auth = "User Id=$user;Password=$plain"
}

$cs = "Server=$server;Database=$db;$auth;ApplicationIntent=ReadOnly;TrustServerCertificate=True;Encrypt=True;Connect Timeout=15"

# Prueba la conexión antes de guardarla.
try {
    $cn = New-Object System.Data.SqlClient.SqlConnection $cs
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = "SELECT (SELECT COUNT(*) FROM dbo.Item), (SELECT COUNT(*) FROM dbo.Inventory WHERE WarehouseId = 'Main' AND OnHandQuantity <> 0)"
    $r = $cmd.ExecuteReader(); [void]$r.Read()
    Write-Host ("Conectó. Artículos: {0:N0}; existencias en Main: {1:N0}" -f $r.GetValue(0), $r.GetValue(1)) -ForegroundColor Green
    $cn.Close()
} catch {
    Write-Host "No conectó: $($_.Exception.Message)" -ForegroundColor Red
    if ((Read-Host '¿Guardar la variable de todos modos? (s/N)') -ne 's') { exit 1 }
}

[Environment]::SetEnvironmentVariable('ConnectionStrings__LegacyMswm', $cs, 'User')
Write-Host 'Listo: variable de usuario ConnectionStrings__LegacyMswm guardada. Cierra y abre de nuevo las terminales / Visual Studio.' -ForegroundColor Green
Write-Host 'Para quitarla:  [Environment]::SetEnvironmentVariable(''ConnectionStrings__LegacyMswm'', $null, ''User'')'
