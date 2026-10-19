# Certificado de Let's Encrypt para un sitio de IIS, con win-acme (wacs.exe), que se renueva solo (2026-10-19).
# Lo cargan instalar.ps1 (con -LetsEncrypt) e instalar-test.ps1 con  . .\letsencrypt.ps1  y llaman a Asegurar-CertificadoLetsEncrypt.
#
# Cómo funciona: win-acme es el cliente ACME más usado en Windows/IIS. La primera vez se descarga de GitHub a C:\Tools\win-acme (si el servidor ya
# lo tiene en otra carpeta, se usa ese). Para cada sitio crea una «renovación»: pide el certificado validando por http (el sitio debe responder en
# el puerto 80 con su nombre de dominio, que es como lo deja el instalador), lo instala en el almacén de certificados de la máquina, agrega el enlace
# https al sitio en IIS y deja una TAREA PROGRAMADA (win-acme renew) que lo renueva sola cuando faltan ~30 días para vencer. Si el servidor ya tiene
# win-acme para producción y staging, la misma tarea se encarga de todos los sitios.

function Buscar-Wacs {
    $candidatos = @('C:\Tools\win-acme\wacs.exe', 'C:\Program Files\win-acme\wacs.exe', 'C:\win-acme\wacs.exe', 'C:\inetpub\win-acme\wacs.exe')
    $enPath = Get-Command wacs.exe -ErrorAction SilentlyContinue
    if ($enPath) { $candidatos = @($enPath.Source) + $candidatos }
    foreach ($c in $candidatos) { if (Test-Path $c) { return $c } }
    # cualquier carpeta de primer nivel en C:\ o C:\Tools que se llame win-acme*
    foreach ($raiz in 'C:\', 'C:\Tools', 'C:\Program Files') {
        $hallado = Get-ChildItem $raiz -Directory -Filter 'win-acme*' -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'wacs.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($hallado) { return $hallado }
    }
    return $null
}

function Descargar-Wacs {
    $destino = 'C:\Tools\win-acme'
    Write-Host "Descargando win-acme (cliente de Let's Encrypt) a $destino..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $release = Invoke-RestMethod 'https://api.github.com/repos/win-acme/win-acme/releases/latest' -Headers @{ 'User-Agent' = 'teikem-instalador' } -UseBasicParsing
    $asset = $release.assets | Where-Object { $_.name -match '^win-acme\.v.*\.x64\.pluggable\.zip$' } | Select-Object -First 1
    if (-not $asset) { $asset = $release.assets | Where-Object { $_.name -match '^win-acme\.v.*\.x64\.trimmed\.zip$' } | Select-Object -First 1 }
    if (-not $asset) { throw 'No encontré el .zip de win-acme (x64) en la última versión publicada en GitHub.' }
    $zip = Join-Path $env:TEMP $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    New-Item -ItemType Directory -Force $destino | Out-Null
    Expand-Archive $zip $destino -Force
    Remove-Item $zip -ErrorAction SilentlyContinue
    $exe = Join-Path $destino 'wacs.exe'
    if (-not (Test-Path $exe)) { throw "win-acme se descargó pero no encuentro $exe." }
    Write-Host "win-acme $($release.tag_name) listo en $destino" -ForegroundColor Green
    return $exe
}

<#
  Pide (o reutiliza) el certificado de Let's Encrypt del sitio $Sitio para el nombre $Dominio y deja el enlace https en IIS y la tarea de renovación.
  Devuelve $true si el sitio quedó con enlace https. No lanza excepciones: si algo falla, lo dice y devuelve $false (el sitio sigue en http).
  $Correo: a dónde avisa Let's Encrypt si algún día no pudiera renovar (obligatorio para la cuenta ACME).
#>
function Asegurar-CertificadoLetsEncrypt([string]$Sitio, [string]$Dominio, [string]$Correo) {
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    $yaTiene = Get-WebBinding -Name $Sitio -Protocol https -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -like "*:443:$Dominio" }
    if ($yaTiene) { Write-Host "El sitio '$Sitio' ya tiene su enlace https para $Dominio (lo renueva la tarea de win-acme)." -ForegroundColor Green; return $true }
    if (-not $Correo) { Write-Host "Falta el correo para la cuenta de Let's Encrypt; el sitio queda en http." -ForegroundColor Yellow; return $false }
    try {
        $wacs = Buscar-Wacs
        if (-not $wacs) { $wacs = Descargar-Wacs }
        $sitioId = (Get-Website -Name $Sitio).Id
        Write-Host "Pidiendo el certificado de $Dominio a Let's Encrypt (win-acme: $wacs)..."
        # --source iis --siteid/--host: el enlace http del sitio que se valida; --installation iis: agrega el enlace https al mismo sitio;
        # --accepttos/--emailaddress: cuenta ACME sin preguntas; --setuptaskscheduler: deja la tarea programada de renovación si no existe.
        $args_ = @('--source', 'iis', '--siteid', "$sitioId", '--host', $Dominio, '--installation', 'iis', '--accepttos', '--emailaddress', $Correo, '--setuptaskscheduler', '--closeonfinish')
        $p = Start-Process -FilePath $wacs -ArgumentList $args_ -Wait -PassThru -NoNewWindow -WorkingDirectory (Split-Path $wacs)
        if ($p.ExitCode -ne 0) { Write-Host "win-acme terminó con código $($p.ExitCode). Revise su registro en $(Split-Path $wacs)\ o en C:\ProgramData\win-acme\." -ForegroundColor Yellow }
        $tiene = [bool](Get-WebBinding -Name $Sitio -Protocol https -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -like "*:443:$Dominio" })
        if ($tiene) { Write-Host "Certificado instalado: https://$Dominio (se renueva solo con la tarea programada de win-acme)." -ForegroundColor Green }
        else {
            Write-Host "No quedó el enlace https de $Dominio. Causas habituales: el DNS de $Dominio aún no apunta a este servidor, el puerto 80 no llega desde Internet," -ForegroundColor Yellow
            Write-Host "o Let's Encrypt limitó los intentos. Corrija y vuelva a correr el instalador (conserve la configuración)." -ForegroundColor Yellow
        }
        return $tiene
    } catch {
        Write-Host "No pude pedir el certificado: $($_.Exception.Message)" -ForegroundColor Yellow
        return $false
    }
}
