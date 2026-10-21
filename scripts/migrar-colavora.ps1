# Migración de Colavora Courier → Teikem (2026-10-18). Corre el verbo import-colavora del API para una compañía.
# NO corre nada contra producción por sí solo: usa la cadena ConnectionStrings:Teikem del appsettings / variables de entorno
# de la máquina donde se ejecuta (local por omisión). Las bases de Colavora se leen (solo lectura) con las variables que crea
# scripts\crear-variable-colavora.ps1.
#
# Uso:
#   .scriptsmigrar-colavora.ps1 -Compania advance -DryRun              # simulación de Advance Logistics
#   .scriptsmigrar-colavora.ps1 -Compania delapaz                      # carga real de De La Paz Transport
#   .scriptsmigrar-colavora.ps1 -Compania advance -Solo catalogos,usuarios,zonas,clientes,consignatarios,contratos
#   .scriptsmigrar-colavora.ps1 -Compania advance -Solo ordenes -Desde 1200000 -Hasta 1276964
#   .scriptsmigrar-colavora.ps1 -Compania advance -Meses 3            # pruebas: solo las órdenes de los últimos 3 meses
#   .scriptsmigrar-colavora.ps1 -Compania delapaz -DesdeFecha 2026-01-01
#   .scriptsmigrar-colavora.ps1 -Compania advance -DiasAbiertas 30    # lo abierto de más de 30 días se migra como Entregada
# Detalle: docs/migracion/colavora/README.md
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('advance', 'delapaz')] [string] $Compania,
    [switch] $DryRun,
    [string[]] $Solo,
    [int] $Desde,
    [int] $Hasta,
    # Para pruebas: solo las órdenes creadas en los últimos N meses, o desde una fecha (aaaa-mm-dd). Los maestros van completos.
    [int] $Meses,
    [string] $DesdeFecha,
    # Días que se dejan abiertas las órdenes: las abiertas (de cualquier estatus) con más días se migran como Entregada. Sin esto, solo las Pending de más de 5 días.
    [int] $DiasAbiertas,
    [ValidateSet('Debug', 'Release')] [string] $Configuracion = 'Release'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$config = Join-Path $repo "docs\migracion\colavora\import.$Compania.json"
if (-not (Test-Path $config)) { throw "No existe $config" }

# Las variables de usuario (creadas por crear-variable-colavora.ps1) pueden no estar en este proceso si la terminal es vieja.
foreach ($v in 'ConnectionStrings__ColavoraCourier', 'ConnectionStrings__ColavoraCourierLogin') {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($v))) {
        $u = [Environment]::GetEnvironmentVariable($v, 'User')
        if ([string]::IsNullOrWhiteSpace($u)) { throw "Falta la variable $v. Corre scripts\crear-variable-colavora.ps1." }
        [Environment]::SetEnvironmentVariable($v, $u, 'Process')
    }
}

# Sin perfil de lanzamiento el API arranca como Producción (exige llave JWT propia): el verbo corre como Development salvo que se diga otra cosa.
if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) { $env:ASPNETCORE_ENVIRONMENT = 'Development' }

$args = @('run', '--project', (Join-Path $repo 'src\Teikem.Api'), '-c', $Configuracion, '--no-launch-profile', '--', 'import-colavora', $config)
if ($DryRun) { $args += '--dry-run' }
if ($Solo) { $args += @('--solo', ($Solo -join ',')) }
if ($PSBoundParameters.ContainsKey('Desde')) { $args += @('--desde', $Desde) }
if ($PSBoundParameters.ContainsKey('Hasta')) { $args += @('--hasta', $Hasta) }
if ($PSBoundParameters.ContainsKey('Meses')) { $args += @('--meses', $Meses) }
if ($DesdeFecha) { $args += @('--desde-fecha', $DesdeFecha) }
if ($PSBoundParameters.ContainsKey('DiasAbiertas')) { $args += @('--dias-abiertas', $DiasAbiertas) }

Write-Host "dotnet $($args -join ' ')" -ForegroundColor Cyan
$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet @args
$code = $LASTEXITCODE
Write-Host ("Terminó en {0} con código {1} (0 = sin rechazos graves, 1 = rechazos graves, 2 = sintaxis)." -f $sw.Elapsed, $code) -ForegroundColor $(if ($code -eq 0) { 'Green' } else { 'Yellow' })
exit $code
