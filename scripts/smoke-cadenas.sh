#!/usr/bin/env bash
# Prueba de humo de las cadenas de clientes (Lote 245, Lote B del plan de rutas, cadenas y precios) contra un API levantado: crear, editar y desactivar cadenas, asignar y
# quitar clientes (una sola cadena por cliente), cambio de cadena desde la ficha, alta masiva por el inicio del nombre (simulación con huella, exclusiones, huella vencida),
# columna y filtros «Cadena» en clientes y órdenes de venta, análisis «Por cadena» con conciliación, permisos, módulo apagado y aislamiento entre compañías. Cada mensaje de
# error se compara con el texto exacto.
# NUNCA contra Brevo ni correo real: se NIEGA a correr si el canal EMAIL está configurado y no es local (buzón). No verifica correos. Solo contra un API en Development, nunca contra
# TEST ni producción: crea datos con el sufijo $CTS (cadenas, usuario, clientes) y no los borra.
# Requiere: curl, jq. Uso: scripts/smoke-cadenas.sh [base_url]   (API de pruebas: scripts/api-para-pruebas.ps1, con Auth__Onboarding__Enabled=false)
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
expect() { local code; code=$(echo "$2" | tail -n1); local body; body=$(echo "$2" | sed '$d'); [[ "$code" == "$1" ]] || fail "esperado HTTP $1, recibido $code: $body"; echo "$body"; }
# msg: el cuerpo de error trae exactamente ese texto (en title o en errors)
msg() { echo "$1" | jq -e --arg m "$2" 'tostring | contains($m)' >/dev/null || fail "mensaje esperado «$2»: $1"; }

CH_=/api/v1/client-chains
SA=/api/v1/sales-analysis
CTS=$(date +%s)
MES=$(date +%Y-%m); MES_ANT=$(date -d "$(date +%Y-%m-01) -1 month" +%Y-%m)

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-cadenas\"}")" | jq -r .tokens.accessToken); ok "token"
TID=$(expect 200 "$(req GET /api/v1/me)" | jq -r .tenantId)

step "guarda de correo: nunca contra Brevo"
CH=$(expect 200 "$(req GET /api/v1/notifications/channels)")
EMAIL_CONF=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isConfigured // false')
EMAIL_LOCAL=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isLocal // false')
if [[ "$EMAIL_CONF" == "true" && "$EMAIL_LOCAL" != "true" ]]; then fail "el API tiene correo real (Brevo): esta prueba no corre así; levante el API sin llave (scripts/api-para-pruebas.ps1)"; fi
ok "sin correo real"

# SALES_ORDERS viene apagado en una base recién creada y solo el administrador de la plataforma lo enciende (con reautenticación). Encenderlo otra vez no hace daño.
PEMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"; PPASS="${TEIKEM_PLATFORM_PASSWORD:-$PASS}"
platform_token() { # tenantId -> token de la plataforma reautenticado en esa compañía
  local t; t=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\",\"tenantId\":$1,\"deviceInfo\":\"smoke-cadenas\"}")" | jq -r .tokens.accessToken)
  expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PPASS\"}" "$t")" | jq -r .accessToken; }
# enable_sales TOKEN_PLATAFORMA: enciende SALES_ORDERS y, si el API dice que depende de otro módulo («'X' depende de 'Y'»), enciende ese antes
enable_sales() {
  local ptok=$1 mod=SALES_ORDERS r c dep
  for _ in 1 2 3 4 5 6; do
    r=$(req PUT /api/v1/modules/$mod '{"isEnabled":true}' "$ptok"); c=$(echo "$r" | tail -n1)
    if [[ "$c" == "200" ]]; then [[ "$mod" == "SALES_ORDERS" ]] && return 0; mod=SALES_ORDERS; continue; fi
    dep=$(echo "$r" | sed '$d' | jq -r '.title' | sed -n "s/.*depende de '\([A-Z_]*\)'.*/\1/p"); [[ -n "$dep" ]] || fail "no se pudo encender $mod: $r"
    mod=$dep
  done
  fail "SALES_ORDERS no quedó encendido: $r"; }
step "módulo SALES_ORDERS encendido"
PTOK=$(platform_token "$TID"); enable_sales "$PTOK"; ok "SALES_ORDERS encendido"

step "usuario de ventas sin sales.chain (solo ve)"
RN="VentasCad $CTS"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"$RN\",\"permissions\":[\"sales.view\",\"sales.create\"]}")" >/dev/null
U2EMAIL="vendcad$CTS@teikem.local"
expect 200 "$(req POST /api/v1/users "{\"email\":\"$U2EMAIL\",\"fullName\":\"Vendedor Cadenas $CTS\",\"password\":\"$PASS\",\"roles\":[\"$RN\"]}")" >/dev/null
T2=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$U2EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-cadenas\"}")" | jq -r .tokens.accessToken); ok "usuario $U2EMAIL"

step "clientes de la prueba"
mkclient() { expect 200 "$(req POST /api/v1/clients "{\"name\":\"$1\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -c '{id,publicId}'; }
C1=$(mkclient "Wlg$CTS Centro"); C2=$(mkclient "Wlg$CTS Norte"); C3=$(mkclient "Wlg${CTS}boro"); C4=$(mkclient "Otro$CTS Sur")
P1=$(echo "$C1" | jq -r .publicId); P2=$(echo "$C2" | jq -r .publicId); P3=$(echo "$C3" | jq -r .publicId); P4=$(echo "$C4" | jq -r .publicId); ok "4 clientes"

step "crear, validar y editar cadenas"
msg "$(expect 400 "$(req POST $CH_ '{"name":"  "}')")" "Escriba el nombre de la cadena."
msg "$(expect 400 "$(req POST $CH_ "{\"name\":\"$(printf 'x%.0s' $(seq 1 201))\"}")")" "El nombre no puede pasar de"
A=$(expect 200 "$(req POST $CH_ "{\"name\":\"Walgreens $CTS\",\"notes\":\"Cadena de prueba\"}")")
AID=$(echo "$A" | jq -r .publicId)
echo "$A" | jq -e '.isActive==true and .clientCount==0 and (.rowVersion|length)>0' >/dev/null || fail "ficha nueva: $A"
msg "$(expect 409 "$(req POST $CH_ "{\"name\":\"WALGREENS $CTS\"}")")" "Ya hay una cadena activa con ese nombre."
B=$(expect 200 "$(req POST $CH_ "{\"name\":\"Econo $CTS\"}")"); BID=$(echo "$B" | jq -r .publicId)
msg "$(expect 409 "$(req PATCH $CH_/$BID "{\"name\":\"walgreens $CTS\"}")")" "Ya hay una cadena activa con ese nombre."
RV=$(echo "$A" | jq -r .rowVersion)
U=$(expect 200 "$(req PATCH $CH_/$AID "{\"notes\":\"Notas nuevas\",\"rowVersion\":\"$RV\"}")"); echo "$U" | jq -e '.notes=="Notas nuevas"' >/dev/null || fail "editar notas: $U"
expect 409 "$(req PATCH $CH_/$AID "{\"notes\":\"Otra\",\"rowVersion\":\"$RV\"}")" >/dev/null
expect 200 "$(req PATCH $CH_/$AID '{"clearNotes":true}')" | jq -e '.notes==null' >/dev/null || fail "clearNotes"
expect 404 "$(req GET $CH_/00000000-0000-0000-0000-000000000001)" >/dev/null; ok "crear, nombre único y rowVersion"

step "asignar clientes: una sola cadena por cliente"
R=$(expect 200 "$(req POST $CH_/$AID/clients "{\"clientPublicIds\":[\"$P1\",\"$P2\"]}")")
echo "$R" | jq -e '.assigned==2 and .moved==0' >/dev/null || fail "asignar: $R"
R=$(expect 200 "$(req POST $CH_/$AID/clients "{\"clientPublicIds\":[\"$P1\"]}")"); echo "$R" | jq -e '.alreadyInChain==1 and .assigned==0' >/dev/null || fail "idempotente: $R"
R=$(expect 200 "$(req POST $CH_/$BID/clients "{\"clientPublicIds\":[\"$P1\"]}")")
echo "$R" | jq -e --arg a "$AID" '.assigned==0 and (.skippedOtherChain|length)==1 and .skippedOtherChain[0].chainPublicId==$a' >/dev/null || fail "debía saltar el cliente de otra cadena: $R"
R=$(expect 200 "$(req POST $CH_/$BID/clients "{\"clientPublicIds\":[\"$P1\"],\"moveFromOtherChains\":true}")"); echo "$R" | jq -e '.moved==1' >/dev/null || fail "mover: $R"
msg "$(expect 400 "$(req POST $CH_/$AID/clients '{"clientPublicIds":[]}')")" "Indique los clientes (clientPublicIds)."
msg "$(expect 400 "$(req POST $CH_/$AID/clients "$(jq -cn '{clientPublicIds:[range(0;2001)|"00000000-0000-0000-0000-\(.|tostring|("000000000000"+.)[-12:])"]}')")")" "No se pueden mandar más de 2000 clientes en una sola llamada."
expect 404 "$(req POST $CH_/$AID/clients '{"clientPublicIds":["00000000-0000-0000-0000-000000000001"]}')" >/dev/null
R=$(expect 200 "$(req POST $CH_/$AID/clients/remove "{\"clientPublicIds\":[\"$P2\",\"$P4\"]}")"); echo "$R" | jq -e '.removed==1 and .notInChain==1' >/dev/null || fail "quitar: $R"
expect 200 "$(req POST $CH_/$AID/clients "{\"clientPublicIds\":[\"$P2\"]}")" >/dev/null; ok "asignar, saltar, mover y quitar"

step "cambio de cadena desde la ficha del cliente"
M=$(expect 200 "$(req PUT $CH_/by-client/$P4 "{\"chainPublicId\":\"$AID\"}")"); echo "$M" | jq -e --arg a "$AID" '.chainPublicId==$a and .chainIsActive==true' >/dev/null || fail "by-client: $M"
M=$(expect 200 "$(req PUT $CH_/by-client/$P4 "{\"chainPublicId\":\"$BID\"}")"); echo "$M" | jq -e --arg b "$BID" '.chainPublicId==$b' >/dev/null || fail "mover desde la ficha: $M"
expect 200 "$(req GET /api/v1/clients/$P4)" | jq -e --arg b "$BID" '.chainPublicId==$b' >/dev/null || fail "la ficha del cliente no trae la cadena"
M=$(expect 200 "$(req PUT $CH_/by-client/$P4 '{"chainPublicId":null}')"); echo "$M" | jq -e '.chainPublicId==null' >/dev/null || fail "quitar desde la ficha: $M"
expect 404 "$(req PUT $CH_/by-client/$P4 '{"chainPublicId":"00000000-0000-0000-0000-000000000001"}')" >/dev/null; ok "PUT by-client"

step "desactivar y reactivar"
expect 200 "$(req POST $CH_/$AID/clients "{\"clientPublicIds\":[\"$P3\"]}")" >/dev/null
msg "$(expect 409 "$(req POST $CH_/$AID/deactivate)")" "La cadena tiene 2 clientes activos: quítelos de la cadena antes de desactivarla."
msg "$(expect 409 "$(req POST $CH_/$BID/deactivate)")" "La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla."
expect 200 "$(req POST $CH_/$AID/clients/remove "{\"clientPublicIds\":[\"$P3\"]}")" >/dev/null
expect 200 "$(req POST $CH_/$BID/clients/remove "{\"clientPublicIds\":[\"$P1\"]}")" >/dev/null
expect 200 "$(req POST $CH_/$BID/deactivate)" | jq -e '.isActive==false' >/dev/null || fail "desactivar Econo"
msg "$(expect 400 "$(req POST $CH_/$BID/clients "{\"clientPublicIds\":[\"$P1\"]}")")" "La cadena está desactivada: actívela o escoja otra."
expect 200 "$(req GET $CH_)" | jq -e --arg b "$BID" '[.[] | select(.publicId==$b)] | length==0' >/dev/null || fail "la inactiva no debía salir en la lista"
expect 200 "$(req GET "$CH_?includeInactive=true")" | jq -e --arg b "$BID" '[.[] | select(.publicId==$b)] | length==1' >/dev/null || fail "includeInactive"
expect 200 "$(req POST $CH_/$BID/reactivate)" | jq -e '.isActive==true' >/dev/null || fail "reactivar"; ok "guardas de desactivar y reactivar"

step "alta masiva por el inicio del nombre (simulación con huella)"
expect 200 "$(req POST $CH_/$BID/clients/remove "{\"clientPublicIds\":[\"$P1\"]}")" >/dev/null
msg "$(expect 400 "$(req POST $CH_/$BID/bulk-assign/preview '{"prefix":"Wl"}')")" "Escriba al menos 3 letras del inicio del nombre."
msg "$(expect 400 "$(req POST $CH_/$BID/bulk-assign/apply "{\"prefix\":\"Wlg$CTS\"}")")" "Falta la huella de la simulación (fingerprint): simule antes de aplicar."
PV=$(expect 200 "$(req POST $CH_/$BID/bulk-assign/preview "{\"prefix\":\"Wlg$CTS\"}")"); FP=$(echo "$PV" | jq -r .fingerprint)
echo "$PV" | jq -e '.matched==2 and .toAssign==1 and .skippedOtherChain==1 and .tooMany==false and ([.rows[].name | contains("boro")] | any | not)' >/dev/null || fail "simulación (palabra completa, sin «boro»): $PV"
expect 200 "$(req GET $CH_/$BID)" | jq -e '.clientCount==0' >/dev/null || fail "la simulación escribió"
PV2=$(expect 200 "$(req POST $CH_/$BID/bulk-assign/preview "{\"prefix\":\"Wlg$CTS\",\"wholeWord\":false,\"moveFromOtherChains\":true}")")
echo "$PV2" | jq -e '.matched==3 and .toMove==1 and .toAssign==2' >/dev/null || fail "simulación con mover y sin palabra completa: $PV2"
msg "$(expect 400 "$(req POST $CH_/$BID/bulk-assign/apply "{\"prefix\":\"Wlg$CTS\",\"fingerprint\":\"$FP\",\"excludeClientPublicIds\":[\"$P4\"]}")")" "Un cliente excluido no está entre los que encontró la simulación."
# huella vencida: cambia un cliente que coincide entre simular y aplicar
expect 200 "$(req POST $CH_/$BID/clients "{\"clientPublicIds\":[\"$P1\"]}")" >/dev/null
msg "$(expect 409 "$(req POST $CH_/$BID/bulk-assign/apply "{\"prefix\":\"Wlg$CTS\",\"fingerprint\":\"$FP\"}")")" "Los clientes que coinciden cambiaron desde la simulación: vuelva a simular antes de aplicar."
expect 200 "$(req POST $CH_/$BID/clients/remove "{\"clientPublicIds\":[\"$P1\"]}")" >/dev/null
# aplicar con la huella nueva, moviendo y excluyendo un cliente (la exclusión no cambia la huella)
PV3=$(expect 200 "$(req POST $CH_/$BID/bulk-assign/preview "{\"prefix\":\"Wlg$CTS\",\"moveFromOtherChains\":true}")"); FP3=$(echo "$PV3" | jq -r .fingerprint)
AP=$(expect 200 "$(req POST $CH_/$BID/bulk-assign/apply "{\"prefix\":\"Wlg$CTS\",\"moveFromOtherChains\":true,\"fingerprint\":\"$FP3\",\"excludeClientPublicIds\":[\"$P2\"]}")")
echo "$AP" | jq -e '.assigned==1 and .excluded==1' >/dev/null || fail "aplicar con exclusión: $AP"
# la misma huella ya no vale (el cliente cambió de cadena); con una simulación nueva, aplicar otra vez no cambia nada
msg "$(expect 409 "$(req POST $CH_/$BID/bulk-assign/apply "{\"prefix\":\"Wlg$CTS\",\"moveFromOtherChains\":true,\"fingerprint\":\"$FP3\",\"excludeClientPublicIds\":[\"$P2\"]}")")" "vuelva a simular antes de aplicar"
FP4=$(expect 200 "$(req POST $CH_/$BID/bulk-assign/preview "{\"prefix\":\"Wlg$CTS\",\"moveFromOtherChains\":true}")" | jq -r .fingerprint)
AP=$(expect 200 "$(req POST $CH_/$BID/bulk-assign/apply "{\"prefix\":\"Wlg$CTS\",\"moveFromOtherChains\":true,\"fingerprint\":\"$FP4\",\"excludeClientPublicIds\":[\"$P2\"]}")")
echo "$AP" | jq -e '.assigned==0 and .moved==0 and .alreadyInChain==1' >/dev/null || fail "aplicar de nuevo es idempotente: $AP"
expect 200 "$(req GET "$CH_/$BID/clients?take=50")" | jq -e --arg p "$P1" '.total==1 and .items[0].clientPublicId==$p' >/dev/null || fail "clientes de Econo tras el alta masiva"
expect 200 "$(req GET "$CH_/$BID/clients?all=true")" | jq -e '.items|length==1' >/dev/null || fail "all=true"; ok "simulación, huella vencida, exclusión e idempotencia"

step "columna y filtros «Cadena» en clientes y órdenes de venta"
expect 200 "$(req GET "/api/v1/clients?chainPublicId=$BID")" | jq -e --arg p "$P1" '[.[] | select(.publicId==$p and .chainName=="Econo '"$CTS"'")] | length==1 and length==1' >/dev/null || fail "GET /clients?chainPublicId"
expect 200 "$(req GET "/api/v1/clients?noChain=true")" | jq -e --arg p "$P4" '[.[] | select(.publicId==$p)] | length==1' >/dev/null || fail "GET /clients?noChain"
msg "$(expect 400 "$(req GET "/api/v1/clients?chainPublicId=$BID&noChain=true")")" "Escoja una cadena o «Sin cadena», no las dos."
expect 200 "$(req GET "/api/v1/clients?chainPublicId=00000000-0000-0000-0000-000000000001")" | jq -e 'length==0' >/dev/null || fail "cadena inexistente: lista vacía"
expect 200 "$(req GET "/api/v1/sales-orders?chainPublicId=$BID&take=20")" >/dev/null; ok "filtros de clientes y de órdenes"

step "análisis «Por cadena» (conciliación)"
BC=$(expect 200 "$(req GET "$SA/by-chain?month=$MES&compareMonth=$MES_ANT")")
echo "$BC" | jq -e --arg a "$AID" --arg b "$BID" '.reconciliation.difference==0 and ([.rows[] | select(.chainPublicId==$a or .chainPublicId==$b)] | length==2) and .noChain != null' >/dev/null || fail "by-chain: $(echo "$BC" | jq -c '{reconciliation,r:[.rows[]|{chainName,sales}]}')"
expect 200 "$(req GET "$SA/by-chain?month=$MES&chainPublicId=$BID")" | jq -e '.noChain==null and ([.rows[]]|length==1)' >/dev/null || fail "by-chain de una sola cadena"
expect 200 "$(req GET "$SA/by-chain/series?chainPublicId=$BID")" | jq -e '(.months|length)==12' >/dev/null || fail "series 12 meses"
expect 400 "$(req GET "$SA/by-chain/series")" >/dev/null
expect 404 "$(req GET "$SA/by-chain?month=$MES&chainPublicId=00000000-0000-0000-0000-000000000001")" >/dev/null
expect 200 "$(req GET "$SA/by-route?month=$MES")" >/dev/null; ok "by-chain, series y by-route sigue vigente"

step "seguridad: sin sales.chain"
expect 200 "$(req GET $CH_ '' "$T2")" >/dev/null
expect 403 "$(req POST $CH_ "{\"name\":\"Nope $CTS\"}" "$T2")" >/dev/null
expect 403 "$(req PUT $CH_/by-client/$P4 '{"chainPublicId":null}' "$T2")" >/dev/null
expect 403 "$(req POST $CH_/$BID/bulk-assign/preview "{\"prefix\":\"Wlg$CTS\"}" "$T2")" >/dev/null
expect 403 "$(req GET "$SA/by-chain?month=$MES" '' "$T2")" >/dev/null; ok "403 al administrar y al analizar sin permiso; lista visible con sales.view"

step "otra compañía: módulo apagado y aislamiento"
BADM="admincad$CTS@smoke.local"; BPASS="Smoke_Admin_2026!"
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Cadenas $CTS\",\"phoneNumber\":\"787-555-0100\",\"address\":{\"line1\":\"Calle Smoke 1\",\"city\":\"San Juan\"},\"modules\":[\"CATALOG\"],\"adminEmail\":\"$BADM\",\"adminFullName\":\"Admin Cadenas\",\"adminPassword\":\"$BPASS\",\"mfaRequired\":false}" "$PTOK")" >/dev/null
TB=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$BADM\",\"password\":\"$BPASS\"}")" | jq -r .tokens.accessToken)
expect 403 "$(req GET $CH_ '' "$TB")" >/dev/null; ok "módulo SALES_ORDERS apagado: 403"
TBID=$(expect 200 "$(req GET /api/v1/me '' "$TB")" | jq -r .tenantId)
enable_sales "$(platform_token "$TBID")"
expect 200 "$(req GET $CH_ '' "$TB")" | jq -e --arg a "$AID" '[.[] | select(.publicId==$a)] | length==0' >/dev/null || fail "la otra compañía ve cadenas ajenas"
expect 404 "$(req GET $CH_/$AID '' "$TB")" >/dev/null
expect 404 "$(req PATCH $CH_/$AID '{"name":"Hack"}' "$TB")" >/dev/null
expect 404 "$(req PUT $CH_/by-client/$P1 '{"chainPublicId":null}' "$TB")" >/dev/null
OB=$(expect 200 "$(req POST $CH_ "{\"name\":\"Walgreens $CTS\"}" "$TB")" | jq -r .publicId)
expect 404 "$(req POST $CH_/$OB/clients "{\"clientPublicIds\":[\"$P1\"]}" "$TB")" >/dev/null
expect 404 "$(req GET "$SA/by-chain?month=$MES&chainPublicId=$AID" '' "$TB")" >/dev/null
ok "404 en cadenas y clientes ajenos; el mismo nombre en otra compañía es válido"

printf '\n\033[1;32mHumo de cadenas de clientes: todo en orden\033[0m\n'
