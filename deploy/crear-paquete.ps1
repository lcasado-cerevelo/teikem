<#
  Crea el paquete para subir al servidor: solo lo necesario para construir el API y la web (sin node_modules, pruebas ni
  documentos), de lo que ya está en git (HEAD). Salida: F:\Download\TeikemDeploy\teikem-deploy-AAAAMMDD-HHMM.tar.gz
  Uso:   powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\deploy\crear-paquete.ps1"
  Detalle: deploy/README.md
#>
param([string]$Salida = 'F:\Download\TeikemDeploy')

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$sucio = git status --porcelain -- src web-app deploy Directory.Build.props Directory.Packages.props global.json .dockerignore
if ($sucio) {
    Write-Host 'Hay cambios sin commit en lo que va al paquete (el paquete usa lo ya guardado en git):' -ForegroundColor Yellow
    $sucio | ForEach-Object { Write-Host "  $_" }
    if ((Read-Host '¿Continuar de todos modos? (s/N)') -ne 's') { exit 1 }
}

New-Item -ItemType Directory -Force $Salida | Out-Null
$archivo = Join-Path $Salida ('teikem-deploy-' + (Get-Date -Format 'yyyyMMdd-HHmm') + '.tar.gz')
git archive --format=tar.gz -o $archivo HEAD src web-app deploy Directory.Build.props Directory.Packages.props global.json .dockerignore
if ($LASTEXITCODE -ne 0) { throw 'git archive falló.' }

$mb = [math]::Round((Get-Item $archivo).Length / 1MB, 1)
Write-Host ''
Write-Host "Paquete listo: $archivo ($mb MB)" -ForegroundColor Green
Write-Host ''
Write-Host 'Siguientes pasos (cambie la llave y la dirección del servidor):' -ForegroundColor Cyan
Write-Host "  1) Subirlo:     scp -i `"C:\ruta\llave.pem`" `"$archivo`" ubuntu@IP-DEL-SERVIDOR:~/"
Write-Host '  2) Entrar:      ssh -i "C:\ruta\llave.pem" ubuntu@IP-DEL-SERVIDOR'
Write-Host '  3) Instalar:    mkdir -p ~/teikem && tar xzf ~/teikem-deploy-*.tar.gz -C ~/teikem && cd ~/teikem/deploy && bash install.sh'
Write-Host '  (para actualizar una versión: lo mismo, pero el último comando es  bash update.sh)'
