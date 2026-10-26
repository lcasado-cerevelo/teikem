<#
  PUBLICADOR DE TEST (2026-10-26): deja listo, en su computadora, todo lo que se sube al ambiente de TEST (test.advancelogisticspr.com):
    1) revisa que esté en master y sin cambios sin guardar en lo que va al paquete;
    2) mira, para cada app móvil (almacén, clientes y la móvil del personal), si cambió su código desde el último APK de test que compiló y le ofrece
       compilar el APK de las que cambiaron (scripts\construir-apk.ps1 -Test; cada una le pide la contraseña de la llave de firma y tarda varios minutos);
    3) arma el paquete (deploy\windows\crear-paquete-windows.ps1 -Test): API + web del personal + portal del cliente para la web + página de descargas de las apps
       con los APK de test más recientes + scripts de la base + instalador;
    4) le dice lo que le toca a usted a mano (la base de datos, copiar el .zip, correr el instalador, etc.).
  No toca el servidor ni la base de datos: usted los hace.
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\publicar-test.ps1"
  Opciones:  -SinApk (no pregunta por APK: arma el paquete con los que ya hay)   -Forzar almacen,cliente,movil (ofrece compilar esas aunque no hayan cambiado)
             -SoloApk movil (solo considera el APK de esas apps: -SoloApk movil publica la app móvil nueva y deja los APK del almacén y de clientes como están;
             el API, la web del personal y el portal del cliente siempre van en el paquete)
             -CarpetaApk <carpeta> (dónde están los APK; por omisión F:\Download\TeikemApp)
#>
param(
    [switch]$SinApk,
    [ValidateSet('almacen', 'cliente', 'movil')][string[]]$Forzar = @(),
    [ValidateSet('almacen', 'cliente', 'movil')][string[]]$SoloApk = @('almacen', 'cliente', 'movil'),
    [string]$CarpetaApk = 'F:\Download\TeikemApp'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
function Paso([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Fallar([string]$m) { Write-Host ''; Write-Host $m -ForegroundColor Red; exit 1 }

Paso '1. Revisando el repositorio'
$rama = (git -C $repo branch --show-current)
$commit = (git -C $repo rev-parse --short HEAD)
Write-Host "Rama: $rama   Commit: $commit"
if ($rama -ne 'master') { Write-Host "Aviso: no está en master (está en $rama). TEST lleva lo que hay en master." -ForegroundColor Yellow; if ((Read-Host '¿Continuar? (s/N)') -ne 's') { exit 1 } }
$atras = git -C $repo rev-list --count 'HEAD..@{u}' 2>$null
if ($atras -and [int]$atras -gt 0) { Write-Host "Aviso: hay $atras commit(s) en el remoto que no tiene aquí (haga git pull)." -ForegroundColor Yellow; if ((Read-Host '¿Continuar? (s/N)') -ne 's') { exit 1 } }

# Cada app móvil: el APK de test que se busca, cómo se construye y qué carpetas cuentan como «cambió su código»
$apps = @(
    @{ clave = 'almacen'; nombre = 'Almacén TEST';        patron = 'TeikemTest-*.apk';        extra = '';              rutas = @('app-almacen/src', 'app-almacen/app.config.ts', 'app-almacen/package.json', 'app-almacen/assets', 'app-almacen/plugins', 'app-almacen/modules') },
    @{ clave = 'cliente'; nombre = 'Clientes TEST';       patron = 'TeikemClientesTest-*.apk'; extra = '-App cliente';  rutas = @('app-cliente/src', 'app-cliente/app.config.ts', 'app-cliente/package.json', 'app-cliente/assets', 'app-cliente/plugins', 'shared') },
    @{ clave = 'movil';   nombre = 'Advance App TEST (móvil del personal)'; patron = 'TeikemMovilTest-*.apk'; extra = '-App movil'; rutas = @('app-movil/src', 'app-movil/app.config.ts', 'app-movil/package.json', 'app-movil/assets', 'app-movil/plugins', 'shared') }
)

$apps = @($apps | Where-Object { $SoloApk -contains $_.clave })   # -SoloApk: solo esas apps se consideran para compilar su APK

if (-not $SinApk) {
    Paso '2. ¿Qué apps móviles cambiaron desde su último APK de test?'
    foreach ($a in $apps) {
        $ultimo = Get-ChildItem $CarpetaApk -Filter $a.patron -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
        $cambio = $false
        $motivo = ''
        if (-not $ultimo) { $cambio = $true; $motivo = 'todavía no hay APK de test de esta app' }
        else {
            $desde = $ultimo.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
            # archivos tocados por commits posteriores al APK; no cuentan los tipos generados del API, las pruebas ni los documentos
            $archivos = git -C $repo log "--since=$desde" --name-only --pretty=format: -- @($a.rutas) | Where-Object { $_ -and $_ -notmatch 'schema\.d\.ts$|\.test\.|\.spec\.|\.md$|visual' } | Sort-Object -Unique
            if ($archivos) { $cambio = $true; $motivo = "cambió su código desde el APK ($($ultimo.Name), $desde): $(@($archivos).Count) archivo(s)" }
            else { $motivo = "sin cambios de código desde $($ultimo.Name) ($desde)" }
        }
        $forzada = $Forzar -contains $a.clave
        Write-Host ("  {0}: {1}{2}" -f $a.nombre, $motivo, $(if ($forzada -and -not $cambio) { ' (forzada)' } else { '' })) -ForegroundColor $(if ($cambio -or $forzada) { 'Yellow' } else { 'Green' })
        if ($cambio -or $forzada) {
            if ((Read-Host "  ¿Compilar el APK de $($a.nombre) ahora? Pide la contraseña de la llave de firma y tarda varios minutos (s/N)") -eq 's') {
                $argumentos = @('-ExecutionPolicy', 'Bypass', '-File', (Join-Path $repo 'scripts\construir-apk.ps1'), '-Test', '-Salida', $CarpetaApk)
                if ($a.extra) { $argumentos += $a.extra.Split(' ') }
                & powershell @argumentos
                if ($LASTEXITCODE -ne 0) { Fallar "La compilación del APK de $($a.nombre) falló; revise los mensajes de arriba." }
            } else { Write-Host '  (se arma el paquete con el APK que ya hay, o sin él si no hay)' -ForegroundColor DarkGray }
        }
    }
} else { Paso '2. APK: omitido (-SinApk)' }

Paso '3. Armando el paquete de TEST'
& powershell -ExecutionPolicy Bypass -File (Join-Path $repo 'deploy\windows\crear-paquete-windows.ps1') -Test -CarpetaApk $CarpetaApk
if ($LASTEXITCODE -ne 0) { Fallar 'El paquete no se pudo armar; revise los mensajes de arriba.' }

Paso '4. Lo que le toca a usted'
Write-Host '  [ ] La base de datos de TEST (Teikem_Test): la lleva al día el instalador con db-update; usted decide el respaldo y si restaura datos (restaurar-staging.bat).'
Write-Host '  [ ] Copiar el .zip nuevo (F:\Download\TeikemDeploy\teikem-test-*.zip) al servidor, descomprimirlo y correr  instalar-test.ps1  como Administrador.'
Write-Host '  [ ] Correo en TEST: Email:RedirectTo (todo va a teikem@cerevelo.com) y la llave de Brevo del ambiente; si la cuenta de Brevo llegó al tope diario, los correos no salen.'
Write-Host '  [ ] Portal del cliente: Auth:PasswordReset:WebBaseUrl y el origen https://test-portal.advancelogisticspr.com en Cors:Origins (lo pone el instalador; revíselo si cambió algo).'
Write-Host '  [ ] Módulos por compañía en TEST (Sistema > Módulos): CLIENT_PORTAL (portal), INTEGRATIONS (integraciones) y los que use cada compañía.'
Write-Host '  [ ] Avisos emergentes de la app móvil de TEST: google-services.json solo trae las apps de producción; el paquete com.advancelogisticspr.app.test hay que darlo de alta en Firebase para que reciba avisos (sin eso la app funciona, sin avisos emergentes).'
Write-Host '  [ ] Los dispositivos con la app de TEST ya instalada se actualizan encima (no se desinstala); los nuevos bajan la app de https://test.advancelogisticspr.com/descargas/'
Write-Host ''
Write-Host 'Listo. Si algo falló arriba, no suba nada hasta resolverlo.' -ForegroundColor Green
