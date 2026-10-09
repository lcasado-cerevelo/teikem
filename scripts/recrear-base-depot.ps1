<#
  Recrea la base de Teikem SOLO con Advance Depot (sin Solutions, sin la demo Advance Logistics) para el deploy:
    - Advance Depot: QuickBooks (3 CSV de Depot) + inventario del WMS MSWM de PRODUCCIÓN (variable ConnectionStrings__LegacyMswm;
      hoy 172.31.40.124\sqlexpress, almacén 'Main'). Del MSWM solo se LEE (ApplicationIntent=ReadOnly).
  Siempre recrea: borra TODO lo que haya en la base destino y la vuelve a crear con estructura + seed + Depot.
  Sin la demo: el seed de la demo (Seed:Demo:Enabled) se apaga solo para este proceso; la base queda con UNA sola compañía.

  Uso (desde cualquier carpeta):
    powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\recrear-base-depot.ps1"
  Opciones:
    -Carpeta <ruta>      carpeta con los 3 CSV de Depot (por defecto F:\Download\TeikemMigracion)
    -Conexion <cadena>   cadena de conexión de la base DESTINO (Teikem). Sin ella se usa la configuración de siempre del API (secretos de usuario, variable ConnectionStrings__Teikem o appsettings).
                         Para el servidor de producción:
                         "Server=IP,1433;Database=Teikem;User Id=USUARIO;Password=CLAVE;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True"
    -PermitirRemoto      obligatorio si la base destino NO es del equipo local (el API se niega a borrar bases remotas sin esto)
    -SinConfirmar        no pregunta antes de borrar (en una base remota igual hay que escribir el nombre de la base si NO se usa este switch)
  Contraseña del administrador de Depot (teikem+admin@cerevelo.com): el script la pide (no se escribe en ningún archivo). Si la deja
  vacía se genera una temporal que no se muestra y hay que usar "¿Olvidó su contraseña?" (necesita el correo configurado).
  Para no atenderlo: defina antes la variable TEIKEM_IMPORT_ADMIN_PASSWORD.
  Detalle: docs/migracion/recrear-base.md
#>
param(
    [string]$Carpeta = 'F:\Download\TeikemMigracion',
    [string]$Conexion,
    [switch]$PermitirRemoto,
    [switch]$SinConfirmar
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$api = 'src/Teikem.Api'
$inicio = Get-Date

function Fallar([string]$msg) { Write-Host ''; Write-Host $msg -ForegroundColor Red; exit 1 }
function Paso([string]$msg) { Write-Host ''; Write-Host "== $msg" -ForegroundColor Cyan }

# --- 1. Conexión al MSWM de producción (la variable de usuario la crea scripts\crear-variable-mswm.ps1) ---
Paso 'Revisando la conexión al MSWM de producción (solo lectura)'
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
    Write-Host ("MSWM de origen: servidor {0}, base {1}" -f $cn.DataSource, $cn.Database) -ForegroundColor Green
    Write-Host ("  {0:N0} artículos; {1:N0} existencias en el almacén Main." -f $r.GetValue(0), $r.GetValue(1)) -ForegroundColor Green
    $cn.Close()
} catch {
    Fallar "No se pudo conectar al MSWM: $($_.Exception.Message)`nNo se borró nada."
}

# --- 2. Base DESTINO: cuál es y si es local ---
# Sin -Conexion el API usa SU configuración de siempre (secretos de usuario "teikem-api", variable ConnectionStrings__Teikem o appsettings), igual
# que recrear-base.ps1: el script NO la reemplaza, solo la lee para mostrarte a dónde va a borrar. Con -Conexion sí se usa esa cadena.
Paso 'Revisando la base destino'
$origenCadena = 'parámetro -Conexion'
$cadena = $Conexion
if (-not $cadena) {
    foreach ($ambito in 'Process', 'User', 'Machine') {
        $v = [Environment]::GetEnvironmentVariable('ConnectionStrings__Teikem', $ambito)
        if ($v) { $cadena = $v; $origenCadena = "variable de entorno ConnectionStrings__Teikem ($ambito)"; break }
    }
}
if (-not $cadena) {
    try {
        $secretos = & dotnet user-secrets list --project $api 2>$null
        foreach ($linea in $secretos) { if ($linea -match '^ConnectionStrings:Teikem\s*=\s*(.+)$') { $cadena = $Matches[1].Trim(); $origenCadena = 'secretos de usuario (teikem-api)' } }
    } catch { }
}
if (-not $cadena) {
    $origenCadena = 'appsettings'
    foreach ($archivo in 'appsettings.json', 'appsettings.Development.json') {
        $ruta = Join-Path $repo "$api\$archivo"
        if (Test-Path $ruta) {
            $json = [IO.File]::ReadAllText($ruta, [Text.Encoding]::UTF8) -replace '(?m)^\s*//.*$', ''
            try { $c = (ConvertFrom-Json $json).ConnectionStrings.Teikem } catch { $c = $null }
            if ($c) { $cadena = $c }
        }
    }
}
if (-not $cadena) { Fallar 'No encontré la cadena de conexión de la base destino. Use -Conexion "Server=...;Database=Teikem;...".' }
try { $csb = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $cadena } catch { Fallar "La cadena de conexión de la base destino no es válida: $($_.Exception.Message)" }
$servidor = $csb.DataSource; $base = $csb.InitialCatalog
if ([string]::IsNullOrWhiteSpace($base)) { Fallar 'La cadena de conexión no indica la base de datos (Database=...).' }
if ($base.Trim().StartsWith('MSWM', 'OrdinalIgnoreCase')) { Fallar "La base destino '$base' es del WMS heredado (MSWM*): jamás se borra desde aquí." }
$host_ = $servidor.Trim(); if ($host_ -like 'tcp:*') { $host_ = $host_.Substring(4) }
if ($host_ -like '(localdb)*') { $local = $true } else {
    $corte = $host_.IndexOfAny(@(',', '\')); if ($corte -ge 0) { $host_ = $host_.Substring(0, $corte) }
    $local = @('localhost', '127.0.0.1', '(local)', '.', '::1', '[::1]') -contains $host_.Trim().ToLowerInvariant()
}
Write-Host ("Destino (según {0}): servidor {1}, base {2}  ->  {3}" -f $origenCadena, $servidor, $base, $(if ($local) { 'LOCAL' } else { 'REMOTO' })) -ForegroundColor Yellow
if (-not $local -and -not $PermitirRemoto) {
    Fallar "La base destino NO es del equipo local. Si de verdad quiere borrarla y recrearla (deploy), repita con -PermitirRemoto. No se borró nada."
}
# Probar el acceso ANTES de borrar nada (un login fallido a mitad del db-reset deja la base a medias)
try {
    $prueba = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $cadena
    $prueba.InitialCatalog = 'master'; $prueba['Connect Timeout'] = 15
    $cnPrueba = New-Object System.Data.SqlClient.SqlConnection $prueba.ConnectionString
    $cnPrueba.Open(); $cnPrueba.Close()
    Write-Host ("Acceso al servidor destino: OK (usuario {0})." -f $(if ($csb.IntegratedSecurity) { 'de Windows' } else { $csb.UserID })) -ForegroundColor Green
} catch {
    Fallar ("No pude entrar al servidor destino con esa cadena (según $origenCadena): $($_.Exception.Message)" + [Environment]::NewLine + "No se borró nada." + [Environment]::NewLine +
            "Si es la base local, la cadena buena está en los secretos de usuario: revise 'dotnet user-secrets list --project $api'. Para otra, use -Conexion.")
}
if ($Conexion) { $env:ConnectionStrings__Teikem = $Conexion }   # solo con -Conexion se reemplaza la configuración del API
# Solo Depot: sin la demo Advance Logistics (ni su almacén, usuarios de prueba y administrador de plataforma de la demo)
$env:Seed__Demo__Enabled = 'false'

# --- 3. Archivos de QuickBooks (solo Depot) ---
Paso "Revisando los archivos de QuickBooks de Depot en $Carpeta"
$archivos = 'Depot Products.csv', 'Depot Customers.csv', 'Depot Vendor.csv'
$faltan = $archivos | Where-Object { -not (Test-Path (Join-Path $Carpeta $_)) }
if ($faltan) { Fallar ("Faltan en ${Carpeta}:`n  " + ($faltan -join "`n  ") + "`nNo se borró nada.") }
foreach ($a in $archivos) {
    $f = Get-Item (Join-Path $Carpeta $a)
    Write-Host ("  {0,-26} {1,10:N0} bytes  {2:yyyy-MM-dd HH:mm}" -f $a, $f.Length, $f.LastWriteTime)
}

# El JSON del repo apunta a F:/Download/TeikemMigracion; si la carpeta es otra, se usa una copia temporal con la ruta cambiada.
$carpetaJson = ($Carpeta.TrimEnd('\', '/')) -replace '\\', '/'
$config = 'docs/migracion/import.depot.json'
$tmp = $null
if ($carpetaJson -ne 'F:/Download/TeikemMigracion') {
    $tmp = Join-Path $env:TEMP 'teikem-recrear'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $config = Join-Path $tmp 'import.depot.json'
    $texto = [IO.File]::ReadAllText((Join-Path $repo 'docs/migracion/import.depot.json')).Replace('F:/Download/TeikemMigracion', $carpetaJson)
    [IO.File]::WriteAllText($config, $texto, (New-Object Text.UTF8Encoding $false))
}

# --- 4. El API no puede estar corriendo (la base se borra con ROLLBACK IMMEDIATE) ---
$escuchando = Get-NetTCPConnection -LocalPort 5000, 5001 -State Listen -ErrorAction SilentlyContinue
if ($escuchando) { Fallar 'El API está corriendo en este equipo (puerto 5000/5001). Detenlo y vuelve a correr este script. No se borró nada.' }

# --- 5. Contraseña del administrador de Depot (no se guarda en ningún archivo) ---
if (-not $env:TEIKEM_IMPORT_ADMIN_PASSWORD) {
    Write-Host ''
    Write-Host 'Administrador de Depot: teikem+admin@cerevelo.com' -ForegroundColor Cyan
    $claveSegura = Read-Host 'Contraseña inicial (Enter = que se genere una temporal que no se muestra)' -AsSecureString
    $clave = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($claveSegura))
    if ($clave) { $env:TEIKEM_IMPORT_ADMIN_PASSWORD = $clave }
    else { Write-Host 'Sin contraseña: entrará con "¿Olvidó su contraseña?" (requiere el correo configurado en el servidor).' -ForegroundColor Yellow }
}

# --- 6. Confirmación ---
if (-not $SinConfirmar) {
    Write-Host ''
    Write-Host ("Esto BORRA la base '{0}' en '{1}' completa y la vuelve a crear SOLO con Advance Depot." -f $base, $servidor) -ForegroundColor Yellow
    if ($local) {
        if ((Read-Host 'Escribe RECREAR para continuar') -cne 'RECREAR') { Fallar 'Cancelado. No se borró nada.' }
    } else {
        if ((Read-Host "Base REMOTA. Escribe el nombre de la base ($base) para continuar") -cne $base) { Fallar 'Cancelado. No se borró nada.' }
    }
}

$log = Join-Path $Carpeta ('recrear-depot-' + $inicio.ToString('yyyyMMdd-HHmm') + '.log')
function Correr([string]$titulo, [string[]]$argumentos) {
    Paso $titulo
    "`r`n== $titulo  ($(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))" | Out-File -FilePath $log -Append -Encoding utf8
    & dotnet run --project $api --no-build -- @argumentos 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log -Append | Out-Host
    return $LASTEXITCODE
}

# --- 7. Compilar, recrear e importar ---
Paso 'Compilando el API'
& dotnet build $api -nologo -v q
if ($LASTEXITCODE -ne 0) { Fallar 'No compiló. No se borró nada.' }

$resetArgs = @('db-reset', '--yes'); if (-not $local) { $resetArgs += '--allow-remote' }
if ((Correr 'Recreando la base (db-reset, sin demo)' $resetArgs) -ne 0) { Fallar "db-reset falló; revisa $log" }
$depot = Correr 'Migrando Advance Depot (QuickBooks + MSWM de producción)' @('import-legacy', $config)
Remove-Item Env:\TEIKEM_IMPORT_ADMIN_PASSWORD -ErrorAction SilentlyContinue
if ($tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }

# --- 8. Verificación de lo que quedó en la base ---
Paso 'Verificando la base'
$verificado = $true
try {
    $cn = New-Object System.Data.SqlClient.SqlConnection $cadena
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = @"
SELECT (SELECT COUNT(*) FROM dbo.Tenant),
       (SELECT STRING_AGG(Name, ', ') FROM dbo.Tenant),
       (SELECT COUNT(*) FROM dbo.Product),
       (SELECT COUNT(*) FROM dbo.WarehouseBin),
       (SELECT COUNT(*) FROM dbo.StockBalance WHERE QtyOnHand <> 0),
       (SELECT COUNT(*) FROM dbo.Client),
       (SELECT COUNT(*) FROM dbo.Supplier)
"@
    $r = $cmd.ExecuteReader(); [void]$r.Read()
    Write-Host ("  Compañías: {0} ({1})" -f $r.GetValue(0), $r.GetValue(1))
    Write-Host ("  Productos: {0:N0} · Posiciones: {1:N0} · Saldos con existencia: {2:N0} · Clientes: {3:N0} · Proveedores: {4:N0}" -f $r.GetValue(2), $r.GetValue(3), $r.GetValue(4), $r.GetValue(5), $r.GetValue(6))
    if ($r.GetValue(0) -ne 1) { Write-Host '  ATENCIÓN: se esperaba UNA sola compañía (Advance Depot).' -ForegroundColor Red; $verificado = $false }
    $cn.Close()
} catch {
    Write-Host "  No pude verificar: $($_.Exception.Message)" -ForegroundColor Yellow
}

# --- 9. Resumen ---
Paso 'Resumen'
$informes = Get-ChildItem $Carpeta -Filter 'reporte-depot*' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $inicio } | Sort-Object Name
foreach ($i in $informes) { Write-Host "  Informe: $($i.FullName)" }
Write-Host "  Bitácora: $log"
$ok = ($depot -eq 0) -and $verificado
if ($ok) {
    Write-Host ''
    Write-Host 'Listo: base recreada solo con Advance Depot, sin rechazos graves.' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host ("Terminó con problemas (Depot: {0}; 0 = bien). Revisa el informe y la bitácora." -f $depot) -ForegroundColor Red
}
Write-Host 'Siguiente: arranca el API; entra como teikem+admin@cerevelo.com (primer ingreso: correo, contraseña propia y MFA); para la app registra el aparato (Sistema > Aparatos) y asigna los PIN (Sistema > Usuarios).'
if (-not $ok) { exit 1 }
