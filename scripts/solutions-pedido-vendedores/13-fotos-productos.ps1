# 13-fotos-productos.ps1 — Advance Solutions: sube la foto de cada producto por el API (POST /api/v1/products/{publicId}/image), según 13-fotos-productos.csv.
#
# Las fotos son las de Luis (padimont, prodigy, medpride, global y american red cross) ya emparejadas con su SKU por Claude, mirando cada foto
# (varias traen el SKU impreso en el empaque). Están renombradas por SKU en F:\Download\FotosSolutions\ (la «/» del SKU pasa a «_»).
# No toca la base directamente: usa el API como cualquier usuario con permiso «inventory.manage», así que queda en la bitácora y en el almacén de archivos.
#
#   Uso:  powershell -ExecutionPolicy Bypass -File 13-fotos-productos.ps1 -ApiUrl https://test.advancelogisticspr.com -Email usuario@dominio
#         (pide la contraseña y, si la cuenta tiene MFA, el código; sin -Aplicar solo SIMULA: dice qué subiría y no sube nada)
#         ... -Aplicar              sube de verdad
#         ... -IncluirDudosos       también las filas con Seguro = no del CSV (guantes de otras tallas, monitor «meter only»)
#         ... -SoloSinFoto          salta los productos que ya tienen foto (por omisión REEMPLAZA la que tengan)
#         ... -TenantId 4           si el usuario tiene varias compañías y la que abre no es Advance Solutions
# Para el API local de desarrollo: -ApiUrl https://localhost:5001 (o el puerto de Visual Studio).
param(
    [Parameter(Mandatory = $true)][string]$ApiUrl,
    [Parameter(Mandatory = $true)][string]$Email,
    [string]$Carpeta = 'F:\Download\FotosSolutions',
    [int]$TenantId = 0,
    [switch]$Aplicar,
    [switch]$IncluirDudosos,
    [switch]$SoloSinFoto
)
$ErrorActionPreference = 'Stop'
$api = $ApiUrl.TrimEnd('/')
$csv = Join-Path $PSScriptRoot '13-fotos-productos.csv'
if (-not (Test-Path $csv)) { throw "No encuentro $csv" }
if (-not (Test-Path $Carpeta)) { throw "No encuentro la carpeta de fotos $Carpeta" }

function Call($method, $path, $token, $body) {
    $h = @{}
    if ($token) { $h['Authorization'] = "Bearer $token" }
    if ($null -ne $body) { return Invoke-RestMethod -Method $method -Uri "$api$path" -Headers $h -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 6) }
    return Invoke-RestMethod -Method $method -Uri "$api$path" -Headers $h
}

# --- sesión (la contraseña y el código MFA se piden aquí; no se guardan en ningún archivo)
$sec = Read-Host -AsSecureString 'Contraseña'
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
$login = @{ email = $Email; password = $plain }
if ($TenantId -gt 0) { $login.tenantId = $TenantId }
$r = Call 'POST' '/api/v1/auth/login' $null $login
if ($r.status -eq 'mfa_required') {
    $code = Read-Host 'Código MFA (6 dígitos)'
    $r = Call 'POST' '/api/v1/auth/mfa/verify' $r.mfaChallengeToken @{ code = $code }
}
if ($r.status -eq 'tenant_selection') { throw 'El usuario tiene varias compañías: vuelva a correr con -TenantId <id de Advance Solutions>.' }
$token = $r.tokens.accessToken
if (-not $token) { throw "No se pudo entrar (estado: $($r.status))." }
$me = Call 'GET' '/api/v1/me' $token $null
Write-Host ("Sesión: {0} · compañía «{1}» · {2}" -f $me.email, $me.tenantName, $(if ($Aplicar) { 'APLICA' } else { 'SIMULACIÓN (agregue -Aplicar para subir)' }))
if ($me.tenantName -notmatch 'Solutions') { Write-Warning "La compañía activa es «$($me.tenantName)», no Advance Solutions. Ctrl+C para cancelar, o corra con -TenantId." }

$subidas = 0; $saltadas = 0; $faltan = @()
foreach ($row in (Import-Csv $csv -Encoding UTF8)) {
    if ($row.Seguro -ne 'si' -and -not $IncluirDudosos) { Write-Host ("  - {0}: dudosa ({1}), se salta" -f $row.Sku, $row.Nota); $saltadas++; continue }
    $file = Join-Path $Carpeta $row.Archivo
    if (-not (Test-Path $file)) { Write-Warning "Falta el archivo $file"; $faltan += $row.Sku; continue }
    $page = Call 'GET' ("/api/v1/products?search={0}&take=20" -f [uri]::EscapeDataString($row.Sku)) $token $null
    $match = @($page.items | Where-Object { $_.sku -eq $row.Sku })
    if ($match.Count -ne 1) { Write-Warning ("{0}: {1} producto(s) con ese SKU exacto; se salta" -f $row.Sku, $match.Count); $faltan += $row.Sku; continue }
    $pid_ = $match[0].publicId
    if ($SoloSinFoto) {
        $img = Call 'GET' "/api/v1/products/$pid_/image" $token $null
        if ($img.imagePublicId) { Write-Host ("  = {0}: ya tiene foto, se deja" -f $row.Sku); $saltadas++; continue }
    }
    if (-not $Aplicar) { Write-Host ("  · {0}: subiría {1}" -f $row.Sku, $row.Archivo); $subidas++; continue }
    # curl.exe (viene con Windows 10/11) arma el multipart; -k solo si el certificado de desarrollo no es de confianza
    $out = & curl.exe -sS -X POST "$api/api/v1/products/$pid_/image" -H "Authorization: Bearer $token" -F "file=@$file;type=image/png" -w "`n%{http_code}"
    $codeHttp = ($out | Select-Object -Last 1)
    if ($codeHttp -ne '200') { Write-Warning ("{0}: el API respondió {1}: {2}" -f $row.Sku, $codeHttp, ($out | Select-Object -First 1)); $faltan += $row.Sku; continue }
    Write-Host ("  ✓ {0}: foto subida" -f $row.Sku); $subidas++
}
Write-Host ''
Write-Host ("{0}: {1} · saltadas: {2} · con problema: {3}" -f $(if ($Aplicar) { 'SUBIDAS' } else { 'SE SUBIRÍAN' }), $subidas, $saltadas, $faltan.Count)
if ($faltan.Count -gt 0) { Write-Host ("Revisar: {0}" -f ($faltan -join ', ')) }

