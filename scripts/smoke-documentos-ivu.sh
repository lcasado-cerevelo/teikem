#!/usr/bin/env bash
# Prueba de humo de los documentos del cliente con vigencia, la exención de IVU atada a su documento, la exención de producto por temporada y los avisos de
# vencimiento (Lote 248, Lote D del plan de rutas, cadenas, precios e IVU) contra un API levantado: subir documentos con número y vigencia (y la regresión de la app
# del vendedor), captura única certificado + exención con Idempotency-Key, cálculo con la exención parcial, guardas del documento que respalda una exención (quitar,
# permiso de impuestos, rowVersion, uno por exención), exención vencida en el cálculo y en el aviso al vender, período por temporada, ajustes de vencimiento de la
# compañía, el trabajo DOCUMENT_EXPIRY_SCAN (no repite), los eventos de aviso, la lista «Documentos por vencer», los avisos al cliente (solo correo y el CHECK de los
# de almacén), aislamiento entre compañías y el módulo TAX apagado. Cada mensaje de error se compara con el texto exacto.
# NUNCA contra Brevo ni correo real: se NIEGA a correr si el canal EMAIL está configurado y no es local (buzón). No verifica correos. Solo contra un API en Development,
# nunca contra TEST ni producción: crea datos con el sufijo $CTS (tasa, clientes, productos, documentos, período, usuario) y no los borra.
# Requiere: curl, jq. Uso: scripts/smoke-documentos-ivu.sh [base_url]   (API de pruebas: scripts/api-para-pruebas.ps1, con Auth__Onboarding__Enabled=false)
set -euo pipefail
BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-teikem+admin@cerevelo.com}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
TMPD=$(mktemp -d); trap 'rm -rf "$TMPD"' EXIT
step() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m   ok\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m   FAIL\033[0m %s\n' "$*" >&2; exit 1; }
# req MÉTODO RUTA [CUERPO] [TOKEN]: el token de la cuarta posición manda sobre $TOKEN (la respuesta termina con una línea con el código HTTP).
req()  { local m=$1 p=$2 body=${3:-} tk=${4:-${TOKEN:-}}; local args=(-sS -X "$m" "$BASE$p" -H 'Accept: application/json' -H 'Content-Type: application/json' -H 'X-Lang: es')
  [[ -n "$tk" ]] && args+=(-H "Authorization: Bearer $tk")
  if [[ -n "$body" ]]; then local f; f=$(mktemp -p "$TMPD"); printf '%s' "$body" > "$f"; args+=(--data-binary "@$f"); fi
  curl "${args[@]}" -w '\n%{http_code}'; }
# form RUTA [TOKEN] [IDEMPOTENCY_KEY] -- CAMPOS…: POST multipart (cada campo «-F nombre=valor»; el archivo «-F file=@ruta;type=…»).
form() { local p=$1 tk=${2:-${TOKEN:-}} key=${3:-}; shift 3; [[ "${1:-}" == "--" ]] && shift
  local args=(-sS -X POST "$BASE$p" -H 'Accept: application/json' -H 'X-Lang: es' -H "Authorization: Bearer $tk")
  [[ -n "$key" ]] && args+=(-H "Idempotency-Key: $key")
  curl "${args[@]}" "$@" -w '\n%{http_code}'; }
# multipart ARCHIVO_SALIDA PDF campo=valor…: arma UNA vez el cuerpo multipart con un límite fijo. Repetirlo con la misma Idempotency-Key manda los mismos bytes
# (curl -F cambia el límite en cada llamada y el API lo vería como «otro contenido»).
multipart() { local out=$1 pdf=$2 b=humo248limite; shift 2; : > "$out"
  for kv in "$@"; do printf -- '--%s\r\nContent-Disposition: form-data; name="%s"\r\n\r\n%s\r\n' "$b" "${kv%%=*}" "${kv#*=}" >> "$out"; done
  printf -- '--%s\r\nContent-Disposition: form-data; name="file"; filename="cert.pdf"\r\nContent-Type: application/pdf\r\n\r\n' "$b" >> "$out"; cat "$pdf" >> "$out"
  printf -- '\r\n--%s--\r\n' "$b" >> "$out"; }
# form_raw RUTA CUERPO IDEMPOTENCY_KEY: POST del cuerpo armado por multipart
form_raw() { curl -sS -X POST "$BASE$1" -H 'Accept: application/json' -H 'X-Lang: es' -H "Authorization: Bearer $TOKEN" -H "Idempotency-Key: $3" \
  -H 'Content-Type: multipart/form-data; boundary=humo248limite' --data-binary "@$2" -w '\n%{http_code}'; }
expect() { local code; code=$(echo "$2" | tail -n1); local body; body=$(echo "$2" | sed '$d'); [[ "$code" == "$1" ]] || fail "esperado HTTP $1, recibido $code: $body"; echo "$body"; }
# msg: el cuerpo de error trae exactamente ese texto (en title o en errors)
msg() { echo "$1" | jq -e --arg m "$2" 'tostring | contains($m)' >/dev/null || fail "mensaje esperado «$2»: $1"; }
dplus() { date -d "$(date +%F) $1 day" +%F; }

CTS=$(date +%s)
HOY=$(date +%F)
PDF="$TMPD/cert.pdf"; printf '%%PDF-1.4\n%% humo documentos IVU\n%%%%EOF\n' > "$PDF"

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-documentos-ivu\"}")" | jq -r .tokens.accessToken); ok "token"
TID=$(expect 200 "$(req GET /api/v1/me)" | jq -r .tenantId)

step "guarda de correo: nunca contra Brevo"
CHN=$(expect 200 "$(req GET /api/v1/notifications/channels)")
EMAIL_CONF=$(echo "$CHN" | jq -r '[.[] | select(.channel=="EMAIL")][0].isConfigured // false')
EMAIL_LOCAL=$(echo "$CHN" | jq -r '[.[] | select(.channel=="EMAIL")][0].isLocal // false')
if [[ "$EMAIL_CONF" == "true" && "$EMAIL_LOCAL" != "true" ]]; then fail "el API tiene correo real (Brevo): esta prueba no corre así; levante el API sin llave (scripts/api-para-pruebas.ps1)"; fi
ok "sin correo real"

# TAX y SALES_ORDERS los enciende solo el administrador de la plataforma (con reautenticación). Encenderlos otra vez no hace daño.
PEMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"; PPASS="${TEIKEM_PLATFORM_PASSWORD:-$PASS}"
platform_token() { # tenantId -> token de la plataforma reautenticado en esa compañía
  local t; t=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\",\"tenantId\":$1,\"deviceInfo\":\"smoke-documentos-ivu\"}")" | jq -r .tokens.accessToken)
  expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PPASS\"}" "$t")" | jq -r .accessToken; }
# set_module TOKEN_PLATAFORMA MÓDULO true|false: lo cambia y, si el API dice que depende de otro («'X' depende de 'Y'»), enciende ese antes
set_module() {
  local ptok=$1 target=$2 on=$3 mod=$2 r c dep
  for _ in 1 2 3 4 5 6; do
    r=$(req PUT /api/v1/modules/$mod "{\"isEnabled\":$on}" "$ptok"); c=$(echo "$r" | tail -n1)
    if [[ "$c" == "200" ]]; then [[ "$mod" == "$target" ]] && return 0; mod=$target; continue; fi
    dep=$(echo "$r" | sed '$d' | jq -r '.title' | sed -n "s/.*depende de '\([A-Z_]*\)'.*/\1/p"); [[ -n "$dep" && "$on" == "true" ]] || fail "no se pudo cambiar $mod: $r"
    mod=$dep
  done
  fail "$target no quedó como se pidió: $r"; }
step "módulos TAX y SALES_ORDERS encendidos"
PTOK=$(platform_token "$TID"); set_module "$PTOK" TAX true; set_module "$PTOK" SALES_ORDERS true; ok "TAX y SALES_ORDERS"

step "preparar: tasa IVU por omisión, cliente y productos"
RATE="IVU$CTS"
expect 200 "$(req POST /api/v1/tax/rates "{\"code\":\"$RATE\",\"name\":\"IVU humo $CTS\",\"effectiveFrom\":\"$(dplus -30)\",\"components\":[{\"code\":\"STATE\",\"name\":\"Estatal\",\"percent\":10.5},{\"code\":\"CITY\",\"name\":\"Municipal\",\"percent\":1}]}")" >/dev/null
expect 200 "$(req PATCH /api/v1/tax/settings "{\"defaultRateCode\":\"$RATE\",\"roundingMode\":\"LINE\",\"pricesIncludeTax\":false}")" | jq -e --arg r "$RATE" '.defaultRateCode==$r' >/dev/null || fail "tasa por omisión"
CL=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Doc IVU $CTS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")")
CID=$(echo "$CL" | jq -r .id); CP=$(echo "$CL" | jq -r .publicId)
mkprod() { expect 200 "$(req POST /api/v1/products "{\"sku\":\"$1$CTS\",\"name\":\"$2 $CTS\",\"purchaseCost\":2,\"salePrice\":100}")" | jq -c '{id:.product.id,publicId:.product.publicId}'; }
PR1=$(mkprod DI1 "Libreta"); PR2=$(mkprod DI2 "Mochila")
P1=$(echo "$PR1" | jq -r .id); P1P=$(echo "$PR1" | jq -r .publicId); P2=$(echo "$PR2" | jq -r .id)
calc() { req POST /api/v1/tax/calculate "{\"clientId\":$1,\"lines\":[{\"key\":\"L1\",\"productId\":$2,\"amount\":100}]}"; }
expect 200 "$(calc "$CID" "$P1")" | jq -e '.taxAmount==11.5' >/dev/null || fail "sin exención el cliente paga 11.5"; ok "tasa $RATE, cliente y 2 productos"
ADMIN=$(expect 200 "$(req GET /api/v1/me)" | jq -r .userId)
SA=/api/v1/sales-analysis
ROUTE=$(expect 200 "$(req POST $SA/routes "{\"name\":\"Doc$CTS\"}")" | jq -r .id)
expect 200 "$(req POST $SA/routes/$ROUTE/people "{\"userId\":$ADMIN,\"role\":\"PRIMARY\"}")" >/dev/null
expect 200 "$(req PUT $SA/clients/$CID/sales "{\"routeId\":$ROUTE,\"preferredUserId\":$ADMIN}")" >/dev/null; ok "ruta Doc$CTS: el administrador es titular y vendedor preferido del cliente"

D=/api/v1/clients/$CP/documents
step "documento simple con número y vigencia"
DOC1=$(expect 200 "$(form $D "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F documentType=OTHER -F documentNumber=X-1 -F "validTo=$(dplus 10)")")
DOC1P=$(echo "$DOC1" | jq -r .publicId)
echo "$DOC1" | jq -e '.documentNumber=="X-1" and .daysToExpiry>=9 and .daysToExpiry<=11 and .expiryState=="EXPIRING" and (.rowVersion|length)>0' >/dev/null || fail "documento OTHER: $DOC1"
# regresión de la app del vendedor: solo el tipo y el archivo (RESELLER_CERTIFICATE es de impuestos pero no lleva vigencia)
expect 200 "$(form $D "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F documentType=RESELLER_CERTIFICATE)" | jq -e '.expiryState=="NO_EXPIRY" and .isTax==true' >/dev/null || fail "RESELLER sin fechas"
msg "$(expect 400 "$(form $D "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F documentType=IVU_EXEMPTION_CITY)")" "Este tipo de documento lleva vigencia: escriba la fecha «hasta»."
msg "$(expect 400 "$(form $D "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F documentType=NO_EXISTE)")" "El tipo de documento no existe o está apagado en Catálogos de valores."
msg "$(expect 400 "$(form $D "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F documentType=OTHER -F "validFrom=$(dplus 5)" -F "validTo=$(dplus 1)")")" "La fecha «hasta» no puede ser anterior a la fecha «desde»."
ok "OTHER por vencer, RESELLER sin fechas y las validaciones"

step "la exención necesita su documento"
msg "$(expect 400 "$(req POST /api/v1/tax/exemptions "{\"clientId\":$CID,\"exemptionType\":\"Reventa\",\"certificateNumber\":\"R-$CTS\",\"validFrom\":\"$HOY\",\"validTo\":\"$(dplus 30)\"}")")" "La exención necesita el documento que la respalda."
ok "400 sin documento"

E=/api/v1/clients/$CP/tax-exemptions
step "captura única: certificado + exención en un paso"
msg "$(expect 400 "$(form $E "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F "clientDocumentPublicId=$DOC1P" -F documentType=IVU_EXEMPTION_CITY)")" "Mande el archivo del certificado o escoja un documento ya subido, no las dos cosas."
KEY="doc-ivu-$CTS"
BODY="$TMPD/captura.bin"; multipart "$BODY" "$PDF" documentType=IVU_EXEMPTION_CITY "documentNumber=MUN-$CTS" "validFrom=$HOY" "validTo=$(dplus 20)" scope=COMPONENTS componentCodes=CITY
CAP=$(expect 200 "$(form_raw $E "$BODY" "$KEY")")
EXID=$(echo "$CAP" | jq -r .exemption.id); CDOC=$(echo "$CAP" | jq -r .documentPublicId)
echo "$CAP" | jq -e --arg n "MUN-$CTS" '.exemption.certificateNumber==$n and .exemption.scope=="COMPONENTS" and .exemption.componentCodes==["CITY"] and .exemption.missingDocument==false' >/dev/null || fail "captura: $CAP"
CAP2=$(expect 200 "$(form_raw $E "$BODY" "$KEY")")
[[ "$(echo "$CAP2" | jq -r .exemption.id)" == "$EXID" ]] || fail "la misma Idempotency-Key debía devolver la misma exención: $CAP2"
expect 200 "$(req GET $D)" | jq -e --arg n "MUN-$CTS" '[.[] | select(.documentNumber==$n)] | length==1 and .[0].backsExemption==true' >/dev/null || fail "un solo documento nuevo, que respalda la exención"
expect 200 "$(req GET $E)" | jq -e --arg d "$CDOC" '[.[] | select(.documentPublicId==$d)][0].status=="VALID"' >/dev/null || fail "la exención vigente en la ficha"
ok "captura única, misma clave = misma exención"

step "cálculo con la exención municipal"
R=$(expect 200 "$(calc "$CID" "$P1")")
echo "$R" | jq -e '.taxAmount==10.5 and .lines[0].reason=="EXEMPT_CLIENT_PARTIAL"' >/dev/null || fail "debía cobrar solo el estatal: $R"; ok "10.50 con EXEMPT_CLIENT_PARTIAL"

step "guardas del documento que respalda la exención"
msg "$(expect 409 "$(req DELETE $D/$CDOC)")" "Este documento respalda una exención de IVU vigente: dé de baja la exención en Impuestos antes de quitarlo."
msg "$(expect 409 "$(req POST /api/v1/tax/exemptions "{\"clientId\":$CID,\"exemptionType\":\"Otra\",\"validFrom\":\"$HOY\",\"clientDocumentPublicId\":\"$CDOC\"}")")" "Ese documento ya respalda otra exención activa."
RN="ClientesSinIvu $CTS"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"$RN\",\"permissions\":[\"clients.read\",\"clients.update\"]}")" >/dev/null
U2EMAIL="docivu$CTS@teikem.local"
expect 200 "$(req POST /api/v1/users "{\"email\":\"$U2EMAIL\",\"fullName\":\"Clientes sin IVU $CTS\",\"password\":\"$PASS\",\"roles\":[\"$RN\"]}")" >/dev/null
T2=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$U2EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-documentos-ivu\"}")" | jq -r .tokens.accessToken)
msg "$(expect 403 "$(req PATCH $D/$CDOC "{\"validTo\":\"$(dplus 60)\"}" "$T2")")" "Este documento respalda una exención de IVU: cambiar su número o su vigencia exige el permiso de impuestos (tax.manage)."
ok "quitar 409, segunda exención 409, sin tax.manage 403"

step "exención vencida: cálculo y aviso al vender"
RV=$(expect 200 "$(req GET $D)" | jq -r --arg d "$CDOC" '.[] | select(.publicId==$d) | .rowVersion')
expect 200 "$(req PATCH $D/$CDOC "{\"validFrom\":\"$(dplus -40)\",\"validTo\":\"$(dplus -10)\",\"rowVersion\":\"$RV\"}")" | jq -e '.expiryState=="EXPIRED"' >/dev/null || fail "vencer el documento"
expect 409 "$(req PATCH $D/$CDOC "{\"notes\":\"vieja\",\"rowVersion\":\"$RV\"}")" >/dev/null
R=$(expect 200 "$(calc "$CID" "$P1")")
echo "$R" | jq -e '.taxAmount==11.5 and (.warnings|length)>=1' >/dev/null || fail "vencida debía cobrar 11.5 con aviso: $R"
N=$(expect 200 "$(req GET "/api/v1/sales-orders/tax-notice?client=$CP")")
echo "$N" | jq -e '.level=="EXPIRED" and (.message|startswith("El certificado de exención de IVU de este cliente venció el")) and (.message|endswith(": se cobrará el IVU."))' >/dev/null || fail "aviso al vender: $N"
msg "$(expect 400 "$(req GET /api/v1/sales-orders/tax-notice)")" "Indique el cliente."
ok "11.50 con aviso, rowVersion viejo 409, tax-notice EXPIRED"

P=/api/v1/tax/exemption-periods
step "exención de producto por temporada"
PER=$(expect 200 "$(req POST $P "{\"name\":\"Escolar $CTS\",\"reason\":\"Orden administrativa de humo\",\"validFrom\":\"$HOY\",\"validTo\":\"$(dplus 2)\",\"scope\":\"SELECTED\",\"productPublicIds\":[\"$P1P\"]}")")
PERID=$(echo "$PER" | jq -r .publicId); echo "$PER" | jq -e '.state=="ACTIVE"' >/dev/null || fail "período vigente: $PER"
R=$(expect 200 "$(req POST /api/v1/tax/calculate "{\"lines\":[{\"key\":\"A\",\"productId\":$P1,\"amount\":100},{\"key\":\"B\",\"productId\":$P2,\"amount\":100}]}")")
echo "$R" | jq -e --arg n "Escolar $CTS" '(.lines[] | select(.key=="A") | .taxAmount==0 and .reason=="EXEMPT_PRODUCT_PERIOD" and .exemptionPeriod==$n) and (.lines[] | select(.key=="B") | .taxAmount==11.5)' >/dev/null || fail "período en el cálculo: $R"
msg "$(expect 409 "$(req PATCH $P/$PERID "{\"productPublicIds\":[\"$P1P\",\"$(echo "$PR2" | jq -r .publicId)\"]}")")" "El período ya empezó: solo se le puede cambiar la fecha final o darlo de baja."
msg "$(expect 400 "$(req POST $P "{\"name\":\"Sin motivo $CTS\",\"validFrom\":\"$HOY\",\"validTo\":\"$(dplus 2)\",\"scope\":\"ALL\"}")")" "Escriba el motivo de la exención (por ejemplo, la orden administrativa que la decreta)."
ok "P1 exento por temporada, P2 gravado, empezado no cambia la selección"

step "ajustes de vencimiento de la compañía"
S=$(expect 200 "$(req PUT /api/v1/tenant/settings '{"docExpiryThresholdDays":[5,30,15,0,-1,-7],"docExpiryDailyAfterExpiry":false,"docExpiryNotifyClient":false}')")
echo "$S" | jq -e '.docExpiryThresholdDays==[30,15,5,0,-1,-7] and .docExpiryDailyAfterExpiry==false and .docExpiryNotifyClient==false' >/dev/null || fail "umbrales normalizados: $S"
msg "$(expect 400 "$(req PUT /api/v1/tenant/settings '{"docExpiryThresholdDays":[500]}')")" "Cada umbral es un número entero de días entre -365 y 365 (positivo = antes de vencer, 0 = el día, negativo = después)."
msg "$(expect 400 "$(req PUT /api/v1/tenant/settings '{"docExpiryThresholdDays":[5,5]}')")" "Hay un umbral repetido."
ok "30,15,5,0,-1,-7"

# run_job CLAVE: pide la corrida y espera (hasta 150 s) a que termine una corrida nueva; imprime su mensaje
run_job() {
  local key=$1 last id st
  last=$(expect 200 "$(req GET "/api/v1/jobs/$key/runs")" | jq -r '.[0].id // 0')
  expect 200 "$(req POST "/api/v1/jobs/$key/run")" >/dev/null
  for _ in $(seq 1 75); do
    sleep 2
    R=$(expect 200 "$(req GET "/api/v1/jobs/$key/runs")")
    id=$(echo "$R" | jq -r '.[0].id // 0'); st=$(echo "$R" | jq -r '.[0].finishedUtc // empty')
    if [[ "$id" != "$last" && -n "$st" ]]; then echo "$R" | jq -r '.[0] | "\(.status)|\(.message // "")|\(.error // "")"'; return 0; fi
  done
  fail "el trabajo $key no terminó a tiempo"; }

step "trabajo DOCUMENT_EXPIRY_SCAN: avisa y no repite"
OUT=$(run_job DOCUMENT_EXPIRY_SCAN); echo "   $OUT"
[[ "$OUT" == OK\|* ]] || fail "la corrida falló: $OUT"
NEW=$(echo "$OUT" | sed -n 's/^[A-Z]*|\([0-9]*\) aviso(s) nuevo(s).*/\1/p'); [[ -n "$NEW" && "$NEW" -ge 2 ]] || fail "debía avisar al menos el OTHER (T15) y el certificado vencido (T-7): $OUT"
OUT2=$(run_job DOCUMENT_EXPIRY_SCAN); echo "   $OUT2"
echo "$OUT2" | grep -Eq '\|0 aviso\(s\) nuevo\(s\)|\|Nada que avisar' || fail "la segunda corrida no debía repetir avisos: $OUT2"

step "reglas de aviso (después de anotar los umbrales ya pasados)"
EV=$(expect 200 "$(req GET /api/v1/notifications/events)")
for code in SALES_CLIENT_DOCUMENT_EXPIRING SALES_CLIENT_DOCUMENT_EXPIRED; do
  echo "$EV" | jq -e --arg c "$code" 'tostring | contains($c)' >/dev/null || fail "falta el evento $code"; done
echo "$EV" | jq -e 'tostring | contains("CLIENT_SELLERS") and contains("\"CLIENT\"")' >/dev/null || fail "faltan los destinatarios CLIENT_SELLERS y CLIENT"
msg "$(expect 400 "$(req POST /api/v1/notifications/rules "{\"eventCode\":\"SALES_CLIENT_DOCUMENT_EXPIRING\",\"name\":\"Mala $CTS\",\"recipients\":[{\"kind\":\"CONTEXT\",\"value\":\"FOO\"}]}")")" "Este evento no tiene el destinatario 'FOO'."
# SIN un USER fijo: el administrador recibe solo por ser vendedor preferido del cliente
for code in SALES_CLIENT_DOCUMENT_EXPIRING SALES_CLIENT_DOCUMENT_EXPIRED; do
  expect 200 "$(req POST /api/v1/notifications/rules "{\"eventCode\":\"$code\",\"name\":\"Humo docs $code $CTS\",\"isActive\":true,\"channels\":[{\"channel\":\"APP\",\"isEnabled\":true}],\"recipients\":[{\"kind\":\"CONTEXT\",\"value\":\"CLIENT_SELLERS\"},{\"kind\":\"CONTEXT\",\"value\":\"CLIENT\"}]}")" >/dev/null; done
OUT2B=$(run_job DOCUMENT_EXPIRY_SCAN); echo "   $OUT2B"
echo "$OUT2B" | grep -Eq '\|0 aviso\(s\) nuevo\(s\)|\|Nada que avisar' || fail "crear la regla no debía disparar los avisos ya anotados: $OUT2B"
ok "regla con CLIENT_SELLERS y CLIENT; los umbrales ya anotados no se disparan de golpe"

# otro TIPO (un OTHER a 5 días quedaría «renovado» por el OTHER a 10 días y no avisaría)
expect 200 "$(form $D "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F documentType=MERCHANT_REGISTRATION -F documentNumber=M-2 -F "validTo=$(dplus 5)")" >/dev/null
OUT3=$(run_job DOCUMENT_EXPIRY_SCAN); echo "   $OUT3"
echo "$OUT3" | grep -q '|1 aviso(s) nuevo(s): 1 por vencer' || fail "el documento nuevo a 5 días debía dar 1 aviso T5: $OUT3"
ok "avisa, no repite y avisa lo nuevo"
for _ in $(seq 1 15); do
  IN=$(expect 200 "$(req GET "/api/v1/inbox?take=50")")
  echo "$IN" | jq -e '[.items[] | select(.eventCode=="SALES_CLIENT_DOCUMENT_EXPIRING")] | length>=1' >/dev/null && break; sleep 2; done
echo "$IN" | jq -e '[.items[] | select(.eventCode=="SALES_CLIENT_DOCUMENT_EXPIRING")] | length>=1' >/dev/null || fail "la campanita del administrador no trae el aviso en 30 s: $IN"
LG=$(expect 200 "$(req GET "/api/v1/notifications/log?eventCode=SALES_CLIENT_DOCUMENT_EXPIRING&take=200")")
echo "$LG" | jq -e --arg r "Humo docs SALES_CLIENT_DOCUMENT_EXPIRING $CTS" '[.items[] | select(.ruleName==$r and .status=="SENT")] | length>=1' >/dev/null || fail "el registro no muestra la entrega SENT al vendedor del cliente: $LG"
echo "$LG" | jq -e 'tostring | contains("La compañía no tiene encendido «Avisar al cliente» (Ajustes → Avisos de vencimiento).")' >/dev/null || fail "el registro no explica por qué se saltó el aviso al cliente: $LG"
ok "campanita del vendedor preferido (CLIENT_SELLERS) y CLIENT saltado con su motivo"

step "vista previa del aviso con un documento real (no se envía nada)"
PV=$(expect 200 "$(req POST /api/v1/notifications/preview "$(jq -cn --arg r "$DOC1P" '{eventCode:"SALES_CLIENT_DOCUMENT_EXPIRING",channel:"EMAIL",audience:"CUSTOMER",reference:$r}')")")
echo "$PV" | jq -e --arg n "Doc IVU $CTS" '.usedSample==false and (.subject|contains($n))' >/dev/null || fail "vista previa: $PV"; ok "usedSample=false con el nombre del cliente en el asunto"

step "evento del precio especial por vencer"
expect 200 "$(req GET /api/v1/notifications/events)" | jq -e 'tostring | contains("SALES_SPECIAL_PRICE_EXPIRING")' >/dev/null || fail "falta el evento SALES_SPECIAL_PRICE_EXPIRING"; ok "SALES_SPECIAL_PRICE_EXPIRING"

step "lista «Documentos por vencer»"
L=$(expect 200 "$(req GET "/api/v1/client-documents/expiring?withinDays=30&includeExpired=true&client=$CP")")
echo "$L" | jq -e '(.items|length)>=2 and ([.items[].validTo] == ([.items[].validTo]|sort)) and all(.items[]; .daysToExpiry != null and (.expiryState|length)>0)' >/dev/null || fail "lista: $L"
echo "$L" | jq -e '[.items[] | select(.documentNumber=="X-1")] | length==1' >/dev/null || fail "el OTHER X-1 debía salir: $L"
ok "ordenada por «hasta», con días y estado"

N=/api/v1/clients/$CP/notifications/settings
step "avisos al cliente: documentos solo por correo y el CHECK de los de almacén"
msg "$(expect 400 "$(req PUT $N '{"settings":[{"kind":"DOCUMENT_EXPIRING","emailEnabled":true,"appEnabled":true,"smsEnabled":false}]}')")" "El aviso de documentos por vencer solo sale por correo."
expect 200 "$(req PUT $N '{"settings":[{"kind":"WH_RECEIVED","emailEnabled":true,"appEnabled":false,"smsEnabled":false},{"kind":"DOCUMENT_EXPIRING","emailEnabled":true,"appEnabled":false,"smsEnabled":false}]}')" >/dev/null
ok "DOCUMENT_EXPIRING por app 400; WH_RECEIVED y DOCUMENT_EXPIRING por correo 200"
run_job CLIENT_NOTIFICATION_DIGEST >/dev/null
CLOG=$(expect 200 "$(req GET /api/v1/clients/$CP/notifications/log)")
echo "$CLOG" | jq -e 'tostring | contains("\"SENT\"") | not' >/dev/null || fail "no debía salir ningún correo real: $CLOG"; ok "resumen del cliente corrido: ninguna entrega SENT"

step "otra compañía: aislamiento"
BADM="admindocivu$CTS@smoke.local"; BPASS="Smoke_Admin_2026!"
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Doc IVU $CTS\",\"phoneNumber\":\"787-555-0100\",\"address\":{\"line1\":\"Calle Smoke 1\",\"city\":\"San Juan\"},\"modules\":[\"CATALOG\"],\"adminEmail\":\"$BADM\",\"adminFullName\":\"Admin Doc IVU\",\"adminPassword\":\"$BPASS\",\"mfaRequired\":false}" "$PTOK")" >/dev/null
TB=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$BADM\",\"password\":\"$BPASS\"}")" | jq -r .tokens.accessToken)
TBID=$(expect 200 "$(req GET /api/v1/me '' "$TB")" | jq -r .tenantId)
set_module "$(platform_token "$TBID")" TAX true
expect 404 "$(req GET $D '' "$TB")" >/dev/null
expect 404 "$(req GET $E '' "$TB")" >/dev/null
expect 404 "$(req PATCH $P/$PERID '{"validTo":"2099-01-01"}' "$TB")" >/dev/null
ok "documentos, exenciones y período ajenos: 404"

step "módulo TAX apagado"
set_module "$PTOK" TAX false
expect 403 "$(form $E "$TOKEN" "" -- -F "file=@$PDF;type=application/pdf" -F documentType=IVU_EXEMPTION_TOTAL -F "documentNumber=T-$CTS" -F "validTo=$(dplus 20)")" >/dev/null
expect 200 "$(req GET "/api/v1/sales-orders/tax-notice?client=$CP")" | jq -e '.level=="NONE"' >/dev/null || fail "sin TAX el aviso al vender debía ser NONE"
set_module "$PTOK" TAX true
ok "captura 403 y aviso NONE con TAX apagado; TAX encendido otra vez"

printf '\n\033[1;32mHumo de documentos con vigencia, exención de IVU y avisos de vencimiento: todo en orden\033[0m\n'
