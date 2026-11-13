# Guardia de recursos para producir el video: nada pesado (API, Vite, Playwright/Chromium, ffmpeg, dotnet, SQL) corre si la máquina no tiene holgura, y lo que corre va con
# prioridad baja, pocos núcleos y un perro guardián que lo MATA (solo a él y a sus hijos, nunca a las demás aplicaciones de Luis) si la memoria o el procesador se acercan al límite.
# Regla R8 de docs/video/REGLAS.md. Motivo (2026-11-12): la máquina se cayó mientras Luis trabajaba y corría una preparación pesada.
#
#   -Estado                         una medición: RAM libre, procesador, SQL Server y veredicto (OK / PRECAUCION / ALTO). Código de salida 0 / 1 / 2.
#   -Vigilar -Segundos 60           imprime una muestra cada 5 s durante ese tiempo.
#   -Esperar [-MaxEsperaSeg 600]    espera a que haya holgura (OK); código 3 si no llega.
#   -Archivo <ruta.ps1> [-ArgsArchivo "-Recrear -Base X"]   igual que -Ejecutar pero corre un script (evita los problemas de comillas al pasar comandos largos).
#   -Ejecutar "<comando>"           espera holgura, corre el comando (en un powershell hijo) con prioridad BelowNormal y afinidad limitada, y lo vigila:
#                                   si la RAM libre baja del piso, el procesador pasa del piso durante -CpuSostenidoSeg, o el hijo (con sus hijos) pasa del tope de memoria,
#                                   lo mata (taskkill /T) y sale con código 4 diciendo por qué. Si no, sale con el código del comando.
#
# Umbrales por omisión (pensados para 64 GB y 32 hilos, ajustables): RAM libre mínima para ARRANCAR 16 GB; piso (matar) 8 GB; procesador para arrancar 60 %, piso 92 % sostenido 15 s;
# núcleos lógicos que se le dejan al hijo: 8 (de 32); tope de memoria del hijo y sus hijos: 8 GB.
#   Ejemplos:
#     powershell -File guardia-recursos.ps1 -Estado
#     powershell -File guardia-recursos.ps1 -Ejecutar "cd web-app; npx playwright test --config e2e/video.config.ts --workers=1"
param(
    [switch]$Estado,
    [switch]$Vigilar,
    [switch]$Esperar,
    [string]$Ejecutar = '',
    [string]$Archivo = '',
    [string]$ArgsArchivo = '',
    [int]$Segundos = 60,
    [int]$MaxEsperaSeg = 600,
    [double]$RamLibreMinGB = 16,
    [double]$RamPisoGB = 8,
    [int]$CpuMaxPct = 60,
    [int]$CpuPisoPct = 92,
    [int]$CpuSostenidoSeg = 15,
    [int]$NucleosMax = 8,
    [double]$TopeProcesoGB = 8,
    [string]$Registro = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

function Escribir([string]$texto) {
    $linea = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $texto
    Write-Host $linea
    if ($Registro) { Add-Content -Path $Registro -Value $linea -Encoding UTF8 }
}

function Medir {
    $os = Get-CimInstance Win32_OperatingSystem
    $cpu = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime
    $sql = (Get-Process -Name sqlservr -ErrorAction SilentlyContinue | Measure-Object WorkingSet64 -Sum).Sum
    [pscustomobject]@{
        TotalGB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
        LibreGB = [math]::Round($os.FreePhysicalMemory / 1MB, 1)
        CpuPct  = [int]$cpu
        SqlGB   = [math]::Round($(if ($sql) { $sql } else { 0 }) / 1GB, 1)
    }
}
function Veredicto($m) {
    if ($m.LibreGB -lt $RamPisoGB -or $m.CpuPct -ge $CpuPisoPct) { return 'ALTO' }
    if ($m.LibreGB -lt $RamLibreMinGB -or $m.CpuPct -gt $CpuMaxPct) { return 'PRECAUCION' }
    return 'OK'
}
function Linea($m) { "RAM libre {0} de {1} GB | procesador {2} % | SQL Server {3} GB | {4}" -f $m.LibreGB, $m.TotalGB, $m.CpuPct, $m.SqlGB, (Veredicto $m) }

function Descendientes([int]$raiz) {
    $todos = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, WorkingSetSize
    $ids = New-Object System.Collections.Generic.List[int]
    $ids.Add($raiz)
    for ($i = 0; $i -lt $ids.Count; $i++) { foreach ($h in ($todos | Where-Object { $_.ParentProcessId -eq $ids[$i] })) { if (-not $ids.Contains([int]$h.ProcessId)) { $ids.Add([int]$h.ProcessId) } } }
    $suma = ($todos | Where-Object { $ids -contains [int]$_.ProcessId } | Measure-Object WorkingSetSize -Sum).Sum
    return [pscustomobject]@{ Ids = $ids; GB = [math]::Round(($(if ($suma) { $suma } else { 0 })) / 1GB, 2) }
}

function EsperarHolgura([int]$max) {
    $inicio = Get-Date
    while ($true) {
        $m = Medir
        if ((Veredicto $m) -eq 'OK') { return $true }
        if (((Get-Date) - $inicio).TotalSeconds -ge $max) { Escribir ("Sin holgura tras {0} s: {1}" -f $max, (Linea $m)); return $false }
        Escribir ("Esperando holgura... " + (Linea $m))
        Start-Sleep -Seconds 5
    }
}

if ($Estado) {
    $m = Medir
    Escribir (Linea $m)
    switch (Veredicto $m) { 'OK' { exit 0 } 'PRECAUCION' { exit 1 } default { exit 2 } }
}
if ($Vigilar) {
    $fin = (Get-Date).AddSeconds($Segundos)
    while ((Get-Date) -lt $fin) { Escribir (Linea (Medir)); Start-Sleep -Seconds 5 }
    exit 0
}
if ($Esperar) { if (EsperarHolgura $MaxEsperaSeg) { Escribir 'Hay holgura.'; exit 0 } else { exit 3 } }

if ($Ejecutar -or $Archivo) {
    if (-not (EsperarHolgura $MaxEsperaSeg)) { exit 3 }
    $logicos = [Environment]::ProcessorCount
    $n = [math]::Max(1, [math]::Min($NucleosMax, [int][math]::Floor($logicos / 4)))
    # los últimos núcleos lógicos (los de un solo hilo suelen ser los menos usados por el escritorio); máscara de afinidad de $n bits
    $mascara = [int64]0
    for ($i = 0; $i -lt $n; $i++) { $mascara = $mascara -bor ([int64]1 -shl ($logicos - 1 - $i)) }
    Escribir ("Arranca: {0}" -f $(if ($Archivo) { $Archivo } else { $Ejecutar }))
    Escribir ("Límites: prioridad BelowNormal, {0} de {1} núcleos lógicos, piso de RAM libre {2} GB, procesador {3} % sostenido {4} s, tope del proceso {5} GB." -f $n, $logicos, $RamPisoGB, $CpuPisoPct, $CpuSostenidoSeg, $TopeProcesoGB)
    $argumentos = if ($Archivo) { @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Archivo) + @($ArgsArchivo -split '\s+' | Where-Object { $_ }) } else { @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $Ejecutar) }
    $hijo = Start-Process -FilePath 'powershell.exe' -ArgumentList $argumentos -PassThru -NoNewWindow
    $null = $hijo.Handle   # sin esto, ExitCode queda vacío en Windows PowerShell 5.1
    try { $hijo.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal; $hijo.ProcessorAffinity = [IntPtr]$mascara }
    catch { Escribir ("No pude limitar al hijo: " + $_.Exception.Message) }
    $altoDesde = $null
    $causa = $null
    while (-not $hijo.HasExited) {
        Start-Sleep -Seconds 2
        $m = Medir
        $arbol = Descendientes $hijo.Id
        if ($m.LibreGB -lt $RamPisoGB) { $causa = "RAM libre {0} GB por debajo del piso de {1} GB" -f $m.LibreGB, $RamPisoGB }
        elseif ($arbol.GB -gt $TopeProcesoGB) { $causa = "el comando y sus hijos usan {0} GB (tope {1} GB)" -f $arbol.GB, $TopeProcesoGB }
        elseif ($m.CpuPct -ge $CpuPisoPct) {
            if (-not $altoDesde) { $altoDesde = Get-Date }
            elseif (((Get-Date) - $altoDesde).TotalSeconds -ge $CpuSostenidoSeg) { $causa = "procesador al {0} % por más de {1} s" -f $m.CpuPct, $CpuSostenidoSeg }
        } else { $altoDesde = $null }
        if ($causa) {
            Escribir ("DETENIDO por la guardia: $causa. Se mata solo el comando y sus procesos hijos ({0})." -f ($arbol.Ids -join ', '))
            & taskkill.exe /PID $hijo.Id /T /F | Out-Null
            exit 4
        }
    }
    $codigo = $hijo.ExitCode
    Escribir ("Terminó con código {0}. {1}" -f $codigo, (Linea (Medir)))
    exit $codigo
}
Escribir 'Indique una acción: -Estado, -Vigilar, -Esperar, -Ejecutar "<comando>" o -Archivo <ruta.ps1>.'
exit 64
