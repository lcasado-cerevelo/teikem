#!/usr/bin/env bash
# Prueba de humo de los Lotes 1 a 8A contra un API levantado (default http://localhost:5000).
# Requiere: curl, jq. Uso: scripts/smoke.sh [base_url]
# Opcional: SMOKE_SQL="sqlcmd … -d <bd> -b -h -1 -Q" habilita los pasos que insertan datos por SQL (pings del monitor, Lote 5;
# descuadre real Kárdex ↔ saldo, Lote 14).
set -euo pipefail
BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-teikem+admin@cerevelo.com}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
DISPATCH_EMAIL="${TEIKEM_DISPATCH_EMAIL:-teikem+dispatch@cerevelo.com}"

# Carpeta propia para los cuerpos de req() (ver más abajo): aislada del resto de /tmp, que en una máquina con muchas
# corridas acumuladas puede tener decenas de miles de archivos de otras herramientas y volverse lenta/inestable para
# crear+leer un archivo nuevo a esa frecuencia (curl (26) intermitente). Se borra sola al terminar el script.
REQ_TMPDIR=$(mktemp -d)
trap 'rm -rf "$REQ_TMPDIR"' EXIT

step() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m   ok\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m   FAIL\033[0m %s\n' "$*" >&2; exit 1; }
req()  { # method path [json] [token]
  local m=$1 p=$2 body=${3:-} tok=${4:-${TOKEN:-}}
  local args=(-sS -X "$m" "$BASE$p" -H 'Accept: application/json' -H 'Content-Type: application/json' -H 'X-Lang: es')
  [[ -n "$tok" ]] && args+=(-H "Authorization: Bearer $tok")
  # El cuerpo se manda por archivo (no --data inline): en Git Bash/Windows, curl reinterpreta un argumento con acentos
  # u otro carácter no-ASCII (p. ej. "Exprés") a través del codepage ANSI del proceso y lo manda corrompido — por
  # archivo, curl lee los bytes tal cual. mktemp por llamada (en $REQ_TMPDIR, no en /tmp) porque req() también se usa
  # en paralelo (fondo).
  local bodyfile=''
  if [[ -n "$body" ]]; then
    bodyfile=$(mktemp -p "$REQ_TMPDIR")
    printf '%s' "$body" > "$bodyfile"
    args+=(--data-binary "@$bodyfile")
  fi
  curl "${args[@]}" -w '\n%{http_code}'
  # con if (no `[[ ]] && rm`): sin cuerpo, req() debe terminar en 0 para que `VAR=$(req GET …)` no corte el script (set -e)
  if [[ -n "$bodyfile" ]]; then rm -f "$bodyfile"; fi
}
expect() { # expected_code response
  local code; code=$(echo "$2" | tail -n1); local body; body=$(echo "$2" | sed '$d')
  [[ "$code" == "$1" ]] || fail "esperado HTTP $1, recibido $code: $body"
  echo "$body"
}
# UUID sintáctico para "id ajeno que no existe": ni uuidgen ni /proc/sys/kernel/random/uuid existen en Git Bash/Windows
# (solo en Linux); /dev/urandom sí, en ambos.
randuuid() { od -An -tx1 -N16 /dev/urandom | tr -d ' \n' | sed -E 's/(.{8})(.{4})(.{4})(.{4})(.{12})/\1-\2-\3-\4-\5/'; }

step "health"; expect 200 "$(req GET /health)" >/dev/null; ok "/health"
step "openapi"; expect 200 "$(req GET /swagger/v1/swagger.json)" | jq -e '.paths | length > 200' >/dev/null || fail "el documento OpenAPI no se genera (rutas en conflicto)"; ok "/swagger/v1/swagger.json"

step "login admin"
R=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke\"}")")
[[ $(echo "$R" | jq -r .status) == "ok" ]] || fail "login status: $R"
TOKEN=$(echo "$R" | jq -r .tokens.accessToken); REFRESH=$(echo "$R" | jq -r .tokens.refreshToken); ok "tokens emitidos"

step "/me"
ME=$(expect 200 "$(req GET /api/v1/me)")
echo "$ME" | jq -e '.permissions | index("admin.tenant")' >/dev/null || fail "admin sin admin.tenant"
echo "$ME" | jq -e '.enabledModules | index("COD")' >/dev/null || fail "COD no encendido"
echo "$ME" | jq -e '.enabledModules | index("CROSSDOCK") | not' >/dev/null || fail "CROSSDOCK debería estar apagado"
ok "permisos y módulos del tenant demo"

step "catálogos (A)"
expect 200 "$(req GET /api/v1/catalogs/domains)" | jq -e 'length > 90' >/dev/null || fail "dominios"
expect 200 "$(req GET /api/v1/catalogs/ServiceType)" | jq -e '.[] | select(.code=="EXPRESS")' >/dev/null || fail "ServiceType"
expect 200 "$(req PUT /api/v1/catalogs/ServiceType/EXPRESS/override '{"labels":{"es":"Exprés 24h"}}')" | jq -e '.label=="Exprés 24h" and .isOverridden' >/dev/null || fail "override"
expect 204 "$(req DELETE /api/v1/catalogs/ServiceType/EXPRESS/override)" >/dev/null
LIST=$(expect 200 "$(req POST /api/v1/catalogs/lists '{"name":"Zona de entrega '"$(date +%s)"'","values":[{"code":"NORTE","labels":{"es":"Norte","en":"North"}},{"code":"SUR","labels":{"es":"Sur","en":"South"}}]}')")
LISTKEY=$(echo "$LIST" | jq -r .domainKey); ok "override y lista propia $LISTKEY"

step "estatus (B)"
expect 200 "$(req GET /api/v1/status/OrderStatus)" | jq -e 'map(select(.stageKind=="PIPELINE")) | length >= 5' >/dev/null || fail "pipeline"
expect 200 "$(req PUT /api/v1/status/OrderStatus/INBOUND/override '{"isEnabled":false}')" | jq -e '.isEnabled==false' >/dev/null || fail "deshabilitar INBOUND"
expect 422 "$(req PUT /api/v1/status/OrderStatus/DRAFT/override '{"isEnabled":false}')" >/dev/null; ok "validador rechaza pipeline sin inicial"
expect 200 "$(req PUT /api/v1/status/OrderStatus/INBOUND/override '{"isEnabled":true}')" >/dev/null
expect 200 "$(req PUT '/api/v1/status/capabilities/TRANSPORT_ORDER?statusDomain=OrderStatus' '[{"statusCode":"IN_TRANSIT","capability":"EDIT_CARGO","isAllowed":false}]')" | jq -e 'length>=1' >/dev/null || fail "capabilities"
expect 200 "$(req PUT '/api/v1/status/lateral-entries/TRANSPORT_ORDER?statusDomain=OrderStatus' '[{"lateralStatusCode":"CANCELLED","fromStatusCode":"PICKUP","isAllowed":true}]')" >/dev/null; ok "capacidades y entrada lateral (Caguas cancela desde PICKUP)"

step "módulos (0B) — exige AAL2"
expect 403 "$(req PUT /api/v1/modules/CROSSDOCK '{"isEnabled":true}')" | jq -e '.code=="aal2_required"' >/dev/null || fail "aal2"
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken); ok "reauth"
expect 200 "$(req PUT /api/v1/modules/CROSSDOCK '{"isEnabled":true}')" | jq -e '.[] | select(.key=="CROSSDOCK") | .isEnabled' >/dev/null || fail "encender CROSSDOCK"
expect 200 "$(req PUT /api/v1/modules/RENTAL_EQUIPMENT '{"isEnabled":false}')" >/dev/null      # apaga también RENTAL_BILLING en cascada
expect 409 "$(req PUT /api/v1/modules/RENTAL_BILLING '{"isEnabled":true}')" >/dev/null           # depende de RENTAL_EQUIPMENT
expect 409 "$(req PUT /api/v1/modules/LTL_GROUND '{"isEnabled":false}')" >/dev/null              # núcleo
ok "dependencias, cascada y núcleo"
expect 200 "$(req PUT /api/v1/modules/RENTAL_EQUIPMENT '{"isEnabled":true}')" >/dev/null
expect 200 "$(req PUT /api/v1/modules/CROSSDOCK '{"isEnabled":false}')" >/dev/null

step "tenant settings"
expect 200 "$(req PUT /api/v1/tenant/settings '{"defaultServiceType":"STANDARD","defaultPackageType":"BOX","maxStopsPerRouteDefault":30}')" | jq -e '.defaultPackageType=="BOX"' >/dev/null || fail "settings"
expect 200 "$(req POST /api/v1/tenant/holidays '{"date":"2026-12-25","name":"Navidad","isRecurring":true}')" >/dev/null
expect 200 "$(req GET '/api/v1/tenant/work-days?n=5')" | jq -e 'length==5' >/dev/null || fail "work-days"; ok "defaults, feriado y días hábiles"

step "campos personalizados (F)"
ME_ID=$(echo "$ME" | jq -r .userId)
DEF=$(req POST /api/v1/custom-fields/definitions/USER "{\"fieldKey\":\"cost_center\",\"labels\":{\"es\":\"Centro de costo\",\"en\":\"Cost center\"},\"dataType\":\"TEXT\",\"isRequired\":false,\"isUnique\":true,\"showInList\":true,\"validationJson\":\"{\\\"regex\\\":\\\"^CC-\\\\\\\\d{3}$\\\"}\"}")
CODE=$(echo "$DEF" | tail -n1); [[ "$CODE" == "200" || "$CODE" == "409" ]] || fail "definición: $DEF"
DEF2=$(req POST /api/v1/custom-fields/definitions/USER "{\"fieldKey\":\"zona\",\"labels\":{\"es\":\"Zona\",\"en\":\"Zone\"},\"dataType\":\"SELECT\",\"isRequired\":false,\"isUnique\":false,\"showInList\":true,\"refEntity\":\"$LISTKEY\"}")
CODE=$(echo "$DEF2" | tail -n1); [[ "$CODE" == "200" || "$CODE" == "409" ]] || fail "definición lista: $DEF2"
expect 400 "$(req PUT "/api/v1/custom-fields/values/USER/$ME_ID" '{"values":{"cost_center":"CC-1"}}')" | jq -e '.errors.cost_center' >/dev/null || fail "validación regex"
expect 200 "$(req PUT "/api/v1/custom-fields/values/USER/$ME_ID" '{"values":{"cost_center":"CC-100","zona":"NORTE"}}')" | jq -e '.[] | select(.fieldKey=="zona") | .displayValue=="Norte"' >/dev/null || fail "valores"
ok "definición TEXT+regex, SELECT sobre lista propia, valores tipados"

step "análisis (G/H/I) + Pulso"
expect 200 "$(req GET /api/v1/analytics/data-sources)" | jq -e 'map(.key) | index("AUDIT_LOG")' >/dev/null || fail "data-sources"
expect 200 "$(req GET /api/v1/analytics/pulse)" | jq -e '.indicators | length >= 1' >/dev/null || fail "pulse"
PREV=$(expect 200 "$(req POST '/api/v1/analytics/reports/AUDIT_LOG/preview?dateRangeMode=ALL' '{"name":"x","columns":["CreatedAtUtc","Action","EntityType","User.FullName"],"secondary":["User"],"filterJson":"{\"field\":\"ActionCode\",\"op\":\"in\",\"value\":[\"CREATE\",\"UPDATE\"]}"}')")
echo "$PREV" | jq -e '.total >= 1 and (.columns | map(.key) | index("User.FullName"))' >/dev/null || fail "preview combinada: $PREV"
IND=$(expect 200 "$(req POST /api/v1/analytics/indicators '{"name":"Mis cambios '"$(date +%s)"'","dataSource":"AUDIT_LOG","aggregateFn":"COUNT","dateRangeMode":"LAST7","showInPulse":true,"visibility":"PRIVATE"}')")
IND_ID=$(echo "$IND" | jq -r .id)
expect 200 "$(req GET "/api/v1/analytics/indicators/$IND_ID/value")" | jq -e '.value >= 1' >/dev/null || fail "valor indicador"
expect 200 "$(req PUT "/api/v1/analytics/indicators/$IND_ID/my-date-range" '{"dateRangeMode":"LAST30"}')" | jq -e '.effectiveDateRangeMode=="LAST30" and .dateRangeMode=="LAST7"' >/dev/null || fail "preferencia por usuario"
CH=$(expect 200 "$(req POST /api/v1/analytics/charts '{"name":"Cambios por entidad '"$(date +%s)"'","dataSource":"AUDIT_LOG","groupByField":"EntityType","aggregateFn":"COUNT","chartType":"DONUT","dateRangeMode":"ALL"}')")
expect 200 "$(req GET "/api/v1/analytics/charts/$(echo "$CH" | jq -r .id)/data")" | jq -e '.points | length >= 1' >/dev/null || fail "chart data"
ok "fuentes, preview con join, indicador propio + preferencia, gráfico"

step "auditoría (E)"
ACT=$(expect 200 "$(req GET '/api/v1/audit/activity?kind=all&take=20')")
echo "$ACT" | jq -e '.total >= 5' >/dev/null || fail "actividad vacía"
echo "$ACT" | jq -e '.items | map(select(.kind=="change")) | length >= 1' >/dev/null || fail "sin cambios auditados"
echo "$ACT" | jq -e '.items | map(select(.kind=="security")) | length >= 1' >/dev/null || fail "sin eventos de seguridad"
# grep -c lee todo el CSV (grep -q cerraría la tubería en la primera coincidencia y pipefail lo marcaría como error con CSV grandes)
expect 200 "$(req GET '/api/v1/audit/activity/export.csv')" | grep -c 'Cuando,Tipo,Usuario' >/dev/null || fail "csv"; ok "bitácora unificada + CSV"

step "RBAC: despachador sin admin.users → 403 + PERMISSION_DENIED"
R2=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$DISPATCH_EMAIL\",\"password\":\"$PASS\"}")")
T2=$(echo "$R2" | jq -r .tokens.accessToken)
expect 403 "$(req GET /api/v1/users '' "$T2")" >/dev/null
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=5')" | jq -e '.total >= 1' >/dev/null || fail "PERMISSION_DENIED no registrado"
ok "permiso denegado registrado"

# ============================================================================================================
# Lote 2 — Clientes y contratos. Re-ejecutable: TS como sufijo de nombres/correos; TODAY (UTC) para las vigencias.
# ============================================================================================================
TS=$(date +%s); TODAY=$(date -u +%F)
anon() { TOKEN= req "$@"; }   # petición sin Authorization (accept-invite y logins)
PLATFORM_EMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"

step "clientes (Lote 2): alta compuesta, código autogenerado, perfil y numeración"
C1=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Farmacia Las Marías $TS\",\"paymentTerm\":\"NET30\",\"currency\":\"USD\",\"contract\":{\"startDate\":\"2026-01-01\",\"serviceLevels\":[{\"serviceType\":\"STANDARD\",\"maxTransitHours\":48}]}}")")
CLIENT_PID=$(echo "$C1" | jq -r .publicId); CLIENT_ID=$(echo "$C1" | jq -r .id); CONTRACT_PID=$(echo "$C1" | jq -r .currentContract.publicId); CODE=$(echo "$C1" | jq -r .code)
echo "$C1" | jq -e '(.code | startswith("FARMACIA-LAS-MAR")) and (.code | length) <= 20 and .status=="ACTIVE" and .currentContract.status=="DRAFT" and .currentContract.billingModel.billPerService==true and .currentContract.billingModel.billExtraPiece==false' >/dev/null || fail "alta compuesta: $C1"
expect 200 "$(req GET /api/v1/clients)" | jq -e --arg p "$CLIENT_PID" '.[] | select(.publicId==$p) | .billingSummary=="Por servicio"' >/dev/null || fail "lista de clientes sin billingSummary"
expect 200 "$(req GET '/api/v1/clients/number-format/preview?pattern=AX-%23%23%23%23%23&seq=1')" | jq -e '.value=="AX-00001"' >/dev/null || fail "preview de numeración"
expect 200 "$(req GET "/api/v1/clients?search=$TS")" | jq -e --arg p "$CLIENT_PID" '(map(select(.publicId==$p)) | length)==1' >/dev/null || fail "buscador libre de clientes"
expect 400 "$(req POST /api/v1/clients "{\"name\":\"Crédito malo $TS\",\"creditLimit\":-1}")" | jq -e '.errors.creditLimit' >/dev/null || fail "límite de crédito negativo (alta)"
# SLA del alta compuesta: mismas reglas que PUT /service-levels (tipo repetido, tipo desconocido) y fechas (fin < inicio)
expect 400 "$(req POST /api/v1/clients "{\"name\":\"SLA malo $TS\",\"contract\":{\"startDate\":\"2026-01-01\",\"serviceLevels\":[{\"serviceType\":\"STANDARD\",\"maxTransitHours\":48},{\"serviceType\":\"standard\",\"maxTransitHours\":24}]}}")" | jq -e '.errors["contract.serviceLevels[1].serviceType"]' >/dev/null || fail "SLA repetido en el alta"
expect 400 "$(req POST /api/v1/clients "{\"name\":\"SLA malo $TS\",\"contract\":{\"startDate\":\"2026-01-01\",\"serviceLevels\":[{\"serviceType\":\"NO_EXISTE\"}]}}")" | jq -e '.errors["contract.serviceLevels[0].serviceType"]' >/dev/null || fail "SLA con tipo desconocido"
expect 400 "$(req POST /api/v1/clients "{\"name\":\"Fechas malas $TS\",\"contract\":{\"startDate\":\"2026-01-01\",\"endDate\":\"2025-12-31\"}}")" | jq -e '.errors["contract.endDate"]' >/dev/null || fail "fecha fin < inicio en el alta"
expect 409 "$(req POST /api/v1/clients "{\"code\":\"$CODE\",\"name\":\"Otro con el mismo código\"}")" >/dev/null
# Código autogenerado: sufijo -2/-3 con nombre corto (aserción exacta) y base recortada a 20 con nombre largo (tres altas, ninguna 409)
ACME=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Acme $TS\"}")" | jq -r .code)
expect 200 "$(req POST /api/v1/clients "{\"name\":\"Acme $TS\"}")" | jq -e --arg c "$ACME-2" '.code==$c' >/dev/null || fail "sufijo -2"
expect 200 "$(req POST /api/v1/clients "{\"name\":\"Acme $TS\"}")" | jq -e --arg c "$ACME-3" '.code==$c' >/dev/null || fail "sufijo -3"
L1=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Distribuidora Nacional del Caribe $TS\"}")" | jq -r .code)
L2=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Distribuidora Nacional del Caribe $TS\"}")" | jq -r .code)
L3=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Distribuidora Nacional del Caribe $TS\"}")" | jq -r .code)
[[ ${#L1} -le 20 && ${#L2} -le 20 && ${#L3} -le 20 && "$L1" != "$L2" && "$L2" != "$L3" && "$L1" != "$L3" ]] || fail "códigos con base recortada: $L1 $L2 $L3"
# Perfil (un "name" enviado se ignora: R1, la identidad no se edita), concurrencia optimista y numeración por cliente
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/profile" '{"name":"Otro","legalName":"Farmacia Las Marías, Inc.","creditLimit":5000}')" | jq -e --arg n "Farmacia Las Marías $TS" '.name==$n and .legalName=="Farmacia Las Marías, Inc." and .creditLimit==5000' >/dev/null || fail "perfil (R1: name enviado debe ignorarse)"
expect 409 "$(req PATCH "/api/v1/clients/$CLIENT_PID/profile" '{"legalName":"x","rowVersion":"AAAAAAAAAAA="}')" >/dev/null
expect 400 "$(req PATCH "/api/v1/clients/$CLIENT_PID/profile" '{"creditLimit":-1}')" | jq -e '.errors.creditLimit' >/dev/null || fail "límite de crédito negativo (perfil)"
expect 400 "$(req PATCH "/api/v1/clients/$CLIENT_PID/number-settings" '{"orderNumberFormat":"AX"}')" | jq -e '.errors.orderNumberFormat' >/dev/null || fail "patrón inválido"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/number-settings" '{"clientAssignsInvoiceNumber":true,"orderNumberFormat":"AX-#####","invoiceNumberFormat":"FAC-####"}')" | jq -e '.numberSettings.clientAssignsInvoiceNumber==true and .numberSettings.orderNumberPreview=="AX-00001" and .numberSettings.invoiceNumberPreview=="FAC-0001" and .numberSettings.packageNumberPreview=="PQT-00001" and .numberSettings.clientAssignsOrderNumber==false' >/dev/null || fail "numeración por cliente"
# Patrón vacío = volver al patrón por defecto; los campos omitidos no cambian; las dos preguntas son independientes (solo factura encendida)
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/number-settings" '{"invoiceNumberFormat":""}')" | jq -e '.numberSettings.clientAssignsOrderNumber==false and .numberSettings.clientAssignsInvoiceNumber==true and .numberSettings.invoiceNumberFormat==null and .numberSettings.invoiceNumberPreview=="FAC-00001" and .numberSettings.orderNumberPreview=="AX-00001"' >/dev/null || fail "patrón vacío = por defecto / flags independientes"
ok "alta compuesta (cliente + contrato DRAFT por servicio), códigos, buscador, perfil (R1, crédito negativo), rowVersion y numeración (patrón vacío, flags independientes)"

step "contactos del cliente (Lote 2): resolvers CLIENT/CLIENT_CONTACT y contacto principal"
expect 200 "$(req POST "/api/v1/contacts/CLIENT/$CLIENT_ID" '{"contactType":"PHONE","value":"787-555-0100","isPrimary":true}')" >/dev/null
expect 404 "$(req POST /api/v1/contacts/CLIENT/999999 '{"contactType":"PHONE","value":"787-555-0100","isPrimary":true}')" >/dev/null
CONTACT_ID=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/contacts" "{\"fullName\":\"Ana Pérez\",\"role\":\"Compras\",\"isPrimary\":true,\"contactPoints\":[{\"contactType\":\"EMAIL\",\"value\":\"ana$TS@lasmarias.pr\",\"isPrimary\":true}]}")" | jq -r .id)
CONTACT2_ID=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/contacts" '{"fullName":"Luis Ortiz","role":"Recibo","isPrimary":true}')" | jq -r .id)
CTS=$(expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/contacts")")
echo "$CTS" | jq -e --argjson a "$CONTACT_ID" '(map(select(.isPrimary)) | length)==1 and ((.[] | select(.id==$a) | .contactPoints | length)==1)' >/dev/null || fail "un solo principal / contactPoints: $CTS"
expect 404 "$(req PUT /api/v1/custom-fields/values/CLIENT/999999 '{"values":{}}')" >/dev/null
# Edición: cambiar el principal por PATCH (sin 409: el servicio limpia al anterior) e inactivar (un inactivo nunca es el principal)
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/contacts/$CONTACT_ID" '{"isPrimary":true}')" | jq -e '.isPrimary==true' >/dev/null || fail "cambio de principal por PATCH"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/contacts")" | jq -e --argjson a "$CONTACT_ID" --argjson b "$CONTACT2_ID" '(map(select(.isPrimary)) | length)==1 and (.[] | select(.id==$a) | .isPrimary) and ((.[] | select(.id==$b) | .isPrimary) | not)' >/dev/null || fail "principal tras PATCH"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/contacts/$CONTACT_ID" '{"isActive":false}')" | jq -e '.isPrimary==false and .isActive==false' >/dev/null || fail "inactivar al principal"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/contacts")" | jq -e --argjson a "$CONTACT_ID" '(map(select(.isPrimary)) | length)==0 and (map(select(.id==$a)) | length)==0' >/dev/null || fail "inactivo oculto y sin principal"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/contacts?includeInactive=true")" | jq -e --argjson a "$CONTACT_ID" '(map(select(.id==$a)) | length)==1' >/dev/null || fail "includeInactive"
expect 404 "$(req PATCH "/api/v1/clients/$CLIENT_PID/contacts/999999" '{"role":"x"}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/contacts/$CONTACT2_ID" '{"isPrimary":true}')" | jq -e '.isPrimary==true' >/dev/null || fail "vuelve a haber principal"
ok "contactos: resolvers, un solo principal, edición e inactivación"

step "consignatarios y direcciones del cliente (Lote 2)"
SHARED=$(expect 200 "$(req POST /api/v1/locations "{\"name\":\"Almacén compartido $TS\",\"locationType\":\"BOTH\",\"line1\":\"Calle 1\",\"city\":\"Bayamón\",\"postalCode\":\"00959\",\"country\":\"PR\"}")")
echo "$SHARED" | jq -e '.isShared==true and .allowDupInvoice==false' >/dev/null || fail "compartida / allowDupInvoice por defecto false: $SHARED"; SHARED_PID=$(echo "$SHARED" | jq -r .publicId)
[[ -n "$SHARED_PID" ]] || fail "localización compartida"
LOC=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_PID\",\"name\":\"Sucursal Caguas\",\"locationType\":\"DELIVERY\",\"line1\":\"Ave. Luis Muñoz Marín\",\"city\":\"Caguas\",\"postalCode\":\"00725\",\"country\":\"PR\",\"allowDupInvoice\":true}")")
echo "$LOC" | jq -e '.allowDupInvoice==true and .isShared==false' >/dev/null || fail "consignatario: $LOC"; LOC_PID=$(echo "$LOC" | jq -r .publicId)
CORP_PID=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_PID\",\"name\":\"Oficina central\",\"locationType\":\"CORPORATE\",\"line1\":\"Calle Sol 1\",\"city\":\"San Juan\",\"postalCode\":\"00901\",\"country\":\"PR\"}")" | jq -r .publicId)
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e '.physicalAddress.city=="San Juan" and .postalAddress==null and .pickupAddress.isDefaultFromCorporate==true' >/dev/null || fail "dirección física / recogido en la corporativa"
expect 409 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_PID\",\"name\":\"Otra central\",\"locationType\":\"CORPORATE\",\"line1\":\"x\",\"city\":\"x\",\"country\":\"PR\"}")" >/dev/null
expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_PID\",\"name\":\"Apartado postal\",\"locationType\":\"BILLING\",\"line1\":\"PO Box 1\",\"city\":\"San Juan\",\"country\":\"PR\"}")" >/dev/null
expect 409 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_PID\",\"name\":\"Otro apartado\",\"locationType\":\"BILLING\",\"line1\":\"PO Box 2\",\"city\":\"San Juan\",\"country\":\"PR\"}")" >/dev/null
expect 400 "$(req POST /api/v1/locations '{"name":"Corporativa sin dueño","locationType":"CORPORATE","line1":"x","city":"x","country":"PR"}')" | jq -e '.errors.clientPublicId' >/dev/null || fail "CORPORATE no puede ser compartida"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e '.postalAddress.name=="Apartado postal"' >/dev/null || fail "dirección postal"
WH_PID=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_PID\",\"name\":\"Mi almacén 1\",\"locationType\":\"PICKUP\",\"line1\":\"Carr. 2 km 10\",\"city\":\"Bayamón\",\"country\":\"PR\"}")" | jq -r .publicId)
expect 400 "$(req PATCH "/api/v1/clients/$CLIENT_PID/profile" "{\"defaultPickupLocationPublicId\":\"$LOC_PID\"}")" | jq -e '.errors.defaultPickupLocationPublicId' >/dev/null || fail "un DELIVERY no es almacén"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/profile" "{\"defaultPickupLocationPublicId\":\"$WH_PID\"}")" | jq -e '.pickupAddress.name=="Mi almacén 1" and .pickupAddress.isDefaultFromCorporate==false and (.pickupLocations | length)==1' >/dev/null || fail "almacén por defecto"
# Edición (dueño, tipo, ventana horaria, minutos, concurrencia) y su efecto sobre el recogido por defecto
expect 200 "$(req PATCH "/api/v1/locations/$WH_PID" '{"makeShared":true}')" | jq -e '.isShared==true' >/dev/null || fail "makeShared"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e '.pickupAddress.isDefaultFromCorporate==true' >/dev/null || fail "recogido vuelve a la corporativa al cambiar de dueño"
expect 200 "$(req PATCH "/api/v1/locations/$WH_PID" "{\"clientPublicId\":\"$CLIENT_PID\"}")" | jq -e '.isShared==false' >/dev/null || fail "devolver dueño"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/profile" "{\"defaultPickupLocationPublicId\":\"$WH_PID\"}")" >/dev/null
expect 200 "$(req PATCH "/api/v1/locations/$WH_PID" '{"locationType":"DELIVERY"}')" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e '.pickupAddress.isDefaultFromCorporate==true and (.pickupLocations | length)==0' >/dev/null || fail "recogido vuelve a la corporativa al cambiar de tipo"
expect 200 "$(req PATCH "/api/v1/locations/$WH_PID" '{"locationType":"PICKUP"}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/profile" "{\"defaultPickupLocationPublicId\":\"$WH_PID\"}")" >/dev/null
expect 400 "$(req PATCH "/api/v1/locations/$WH_PID" "{\"makeShared\":true,\"clientPublicId\":\"$CLIENT_PID\"}")" | jq -e '.errors.clientPublicId' >/dev/null || fail "dueño y compartida a la vez"
expect 400 "$(req PATCH "/api/v1/locations/$CORP_PID" '{"makeShared":true}')" >/dev/null
expect 409 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"locationType":"CORPORATE"}')" >/dev/null
expect 400 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"defaultWindowStart":"10:00:00"}')" | jq -e '.errors.defaultWindowStart' >/dev/null || fail "ventana sin fin"
expect 400 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"defaultWindowStart":"10:00:00","defaultWindowEnd":"09:00:00"}')" | jq -e '.errors.defaultWindowEnd' >/dev/null || fail "ventana invertida"
expect 400 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"defaultServiceMinutes":-1}')" | jq -e '.errors.defaultServiceMinutes' >/dev/null || fail "minutos negativos"
expect 200 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"defaultWindowStart":"08:00:00","defaultWindowEnd":"12:00:00"}')" | jq -e '(.defaultWindowStart | startswith("08:00")) and (.defaultWindowEnd | startswith("12:00"))' >/dev/null || fail "ventana horaria"
expect 200 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"clearWindow":true}')" | jq -e '.defaultWindowStart==null and .defaultWindowEnd==null' >/dev/null || fail "clearWindow"
expect 200 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"allowDupInvoice":false}')" | jq -e '.allowDupInvoice==false' >/dev/null || fail "allowDupInvoice=false por PATCH"
expect 200 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"allowDupInvoice":true}')" | jq -e '.allowDupInvoice==true' >/dev/null || fail "allowDupInvoice=true por PATCH"
expect 409 "$(req PATCH "/api/v1/locations/$LOC_PID" '{"name":"x","rowVersion":"AAAAAAAAAAA="}')" >/dev/null
# Baja lógica (nunca DELETE): el almacén por defecto desactivado devuelve el recogido a la corporativa; la lista oculta inactivas
expect 204 "$(req POST "/api/v1/locations/$WH_PID/deactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e '.pickupAddress.isDefaultFromCorporate==true' >/dev/null || fail "recogido vuelve a la corporativa al desactivar"
expect 204 "$(req POST "/api/v1/locations/$WH_PID/reactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_PID")" | jq -e --arg a "$LOC_PID" --arg b "$SHARED_PID" '(map(select(.publicId==$a)) | length)==1 and (map(select(.publicId==$b)) | length)==1' >/dev/null || fail "propias + compartidas"
expect 204 "$(req POST "/api/v1/locations/$LOC_PID/deactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_PID")" | jq -e --arg a "$LOC_PID" '(map(select(.publicId==$a)) | length)==0' >/dev/null || fail "inactiva oculta"
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_PID&includeInactive=true")" | jq -e --arg a "$LOC_PID" '(map(select(.publicId==$a)) | length)==1' >/dev/null || fail "includeInactive"
expect 204 "$(req POST "/api/v1/locations/$LOC_PID/reactivate")" >/dev/null
# Lecturas: ficha por PublicId, filtro por tipo (desconocido → 400) y buscador libre
expect 200 "$(req GET "/api/v1/locations/$LOC_PID")" | jq -e --arg p "$LOC_PID" '.publicId==$p and .locationType=="DELIVERY"' >/dev/null || fail "ficha de localización"
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_PID&locationType=DELIVERY")" | jq -e --arg a "$LOC_PID" '(map(select(.publicId==$a)) | length)==1 and all(.locationType=="DELIVERY")' >/dev/null || fail "filtro locationType"
expect 400 "$(req GET "/api/v1/locations?clientId=$CLIENT_PID&locationType=NOPE")" | jq -e '.errors.locationType' >/dev/null || fail "locationType desconocido"
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_PID&search=Caguas")" | jq -e --arg a "$LOC_PID" '(map(select(.publicId==$a)) | length)==1' >/dev/null || fail "search de localizaciones"
# Reactivar una CORPORATE cuando el cliente ya tiene otra activa → 409 (decisión 4: una activa por cliente)
expect 204 "$(req POST "/api/v1/locations/$CORP_PID/deactivate")" >/dev/null
expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_PID\",\"name\":\"Oficina central 2\",\"locationType\":\"CORPORATE\",\"line1\":\"Calle Sol 2\",\"city\":\"San Juan\",\"country\":\"PR\"}")" >/dev/null
expect 409 "$(req POST "/api/v1/locations/$CORP_PID/reactivate")" | jq -e '.title | contains("dirección corporativa activa")' >/dev/null || fail "reactivar CORPORATE con otra activa"
ok "compartidas/propias, CORPORATE/BILLING únicas (alta y reactivación), almacén por defecto, edición de dueño/tipo/ventana, allowDupInvoice (default y PATCH), baja lógica, ficha y filtros"

step "contrato (Lote 2): modelo de facturación, despacho, COD, SLA y fechas"
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID")" | jq -e --arg n "$CODE-C1" '(.serviceLevels | length)==1 and .canEdit==true and .contractNumber==$n' >/dev/null || fail "ficha del contrato"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billExtraPiece":true,"billDispatchFee":true,"billCodFee":true,"billSpecialServices":true}')" | jq -e '.billingModel.summary=="Por servicio, Pieza extra, Despacho, COD, Especiales"' >/dev/null || fail "billing-model"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/dispatch-fee" '{"amount":3}')" | jq -e '.dispatchFee==3' >/dev/null || fail "dispatch-fee"
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/cod-fee" '{"type":"PERCENT","value":150}')" | jq -e '.errors.value' >/dev/null || fail "COD 150%"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/cod-fee" '{"type":"FIXED","value":2}')" | jq -e '.codFee.type=="FIXED" and .codFee.value==2' >/dev/null || fail "cod-fee FIXED"
expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":1}],\"codAmount\":200}")" | jq -e '.codFee==2' >/dev/null || fail "cotización COD FIXED (\$2.00 fijos, bitácora L1113)"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/cod-fee" '{"type":"PERCENT","value":2.5}')" | jq -e '.codFee.type=="PERCENT" and .codFee.value==2.5' >/dev/null || fail "cod-fee"
expect 200 "$(req PUT "/api/v1/contracts/$CONTRACT_PID/service-levels" '[{"serviceType":"STANDARD","maxTransitHours":24},{"serviceType":"EXPRESS","maxTransitHours":8}]')" | jq -e '(.serviceLevels | length)==2' >/dev/null || fail "service-levels"
expect 400 "$(req PUT "/api/v1/contracts/$CONTRACT_PID/service-levels" '[{"serviceType":"STANDARD","maxTransitHours":24},{"serviceType":"standard","maxTransitHours":8}]')" >/dev/null
# SLA por reemplazo: el tipo ausente se da de baja (IsActive=0) y al volver a enviarlo se reactiva la misma fila (no choca con UX_ContractServiceLevel)
EXPRESS_ID=$(expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID")" | jq -r '.serviceLevels[] | select(.serviceType=="EXPRESS") | .id')
expect 200 "$(req PUT "/api/v1/contracts/$CONTRACT_PID/service-levels" '[{"serviceType":"STANDARD","maxTransitHours":24}]')" | jq -e '(.serviceLevels | length)==1 and ([.serviceLevels[] | select(.serviceType=="EXPRESS")] | length)==0' >/dev/null || fail "SLA ausente se da de baja"
expect 200 "$(req PUT "/api/v1/contracts/$CONTRACT_PID/service-levels" '[{"serviceType":"STANDARD","maxTransitHours":24},{"serviceType":"EXPRESS","maxTransitHours":8}]')" | jq -e --argjson e "$EXPRESS_ID" '(.serviceLevels | length)==2 and ([.serviceLevels[] | select(.serviceType=="EXPRESS")][0].id==$e)' >/dev/null || fail "SLA reactivado reutiliza la fila"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billDispatchFee":false}')" | jq -e '.dispatchFee==3 and .billingModel.billDispatchFee==false' >/dev/null || fail "R7b: el dato no se pierde"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billDispatchFee":true}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billCodFee":false}')" | jq -e '.codFee.type=="PERCENT" and .codFee.value==2.5 and .billingModel.billCodFee==false' >/dev/null || fail "R7b: el cargo COD no se pierde"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billCodFee":true}')" >/dev/null
# 'Cliente desde' editable inline; fin >= inicio en alta y edición
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"startDate":"2026-02-01"}')" | jq -e '.startDate=="2026-02-01"' >/dev/null || fail "startDate editable"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"startDate":"2026-01-01"}')" >/dev/null
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"endDate":"2025-12-31"}')" | jq -e '.errors.endDate' >/dev/null || fail "fin < inicio"
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"endDate":"2026-01-31","clearEndDate":true}')" >/dev/null
expect 400 "$(req POST /api/v1/contracts "{\"clientPublicId\":\"$CLIENT_PID\",\"title\":\"Mal\",\"startDate\":\"2026-01-01\",\"endDate\":\"2025-12-31\"}")" | jq -e '.errors.endDate' >/dev/null || fail "alta con fin < inicio"
# R13 (auto-renovación informativa) y R43 (disparador BY_PICKUP/BY_DELIVERY/MIXED): se persisten y se leen; código desconocido → 404; notas vacías = null
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"autoRenew":true,"billingTrigger":"BY_DELIVERY","currency":"USD","title":"Contrato marco","notes":"smoke"}')" | jq -e '.autoRenew==true and .billingTrigger=="BY_DELIVERY" and .currency=="USD" and .title=="Contrato marco" and .notes=="smoke"' >/dev/null || fail "R13/R43: autoRenew, billingTrigger, currency, title, notes"
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID")" | jq -e '.autoRenew==true and .billingTrigger=="BY_DELIVERY"' >/dev/null || fail "persistencia autoRenew/billingTrigger"
expect 404 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"billingTrigger":"NOPE"}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"notes":""}')" | jq -e '.notes==null' >/dev/null || fail "notes vacío borra"
expect 200 "$(req GET "/api/v1/contracts?clientId=$CLIENT_PID")" | jq -e --arg p "$CONTRACT_PID" '(map(select(.publicId==$p)) | length)==1 and all(.autoRenew!=null)' >/dev/null || fail "lista de contratos por cliente"
ok "5 checkboxes, despacho, COD (FIXED/PERCENT, cotizado), SLA por reemplazo (baja del ausente y reactivación), R7b (despacho y COD), fechas, R13/R43 y lista por cliente"

step "tarifas por servicio (Lote 2): historial efectivo-fechado"
RC_ID=$(expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":6.5,"effectiveFrom":"2026-01-01"}')" | jq -r .id)
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":9}')" >/dev/null
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID" '{"rate":8,"effectiveFrom":"2025-01-01"}')" | jq -e '.errors.effectiveFrom' >/dev/null || fail "nueva versión anterior al inicio"
YESTERDAY=$(date -u -d yesterday +%F)
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID" "{\"rate\":8,\"effectiveFrom\":\"$YESTERDAY\"}")" | jq -e '.errors.effectiveFrom' >/dev/null || fail "nueva versión fechada en el pasado (principio #8)"
expect 400 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID/close" "{\"effectiveTo\":\"$YESTERDAY\"}")" | jq -e '.errors.effectiveTo' >/dev/null || fail "cierre fechado en el pasado (principio #8)"
RC2=$(expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID" "{\"rate\":7,\"effectiveFrom\":\"$TODAY\"}")")
RC_ID2=$(echo "$RC2" | jq -r .id); [[ "$RC_ID2" != "$RC_ID" ]] || fail "la nueva versión debe ser una fila nueva"
echo "$RC2" | jq -e '.rate==7 and .isCurrent==true' >/dev/null || fail "nueva versión: $RC2"
expect 409 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID" '{"rate":8}')" >/dev/null   # la cerrada no se edita
expect 400 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID2/tiers" '{"fromUnit":2,"rate":1}')" >/dev/null   # tramos sobre PER_SERVICE
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components?includeHistory=true")" | jq -e --arg t "$TODAY" --argjson a "$RC_ID" '(.perService | length)==2 and (.perService[] | select(.id==$a) | .effectiveTo==$t and .isCurrent==false) and (.perService[] | select(.rate==7) | .isCurrent==true)' >/dev/null || fail "historial por servicio"
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components?asOf=2026-06-01")" | jq -e '(.perService | length)==1 and .perService[0].rate==6.5 and .perService[0].isCurrent==true' >/dev/null || fail "asOf pasado"
ok "alta, 409 por fila vigente, nueva versión (cerrar + abrir), fechas pasadas rechazadas, historial y asOf"

step "pieza extra (Lote 2): tramos, traslape, edición cerrar+abrir, componente apagado"
EP_ID=$(expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"EXTRA_PIECE","serviceType":"STANDARD","packageType":"BOX"}')" | jq -r .id)
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID" '{"rate":1}')" >/dev/null   # PATCH de tarifa sobre EXTRA_PIECE
T1_ID=$(expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers" '{"fromUnit":2,"toUnit":5,"rate":1.0}')" | jq -r .id)
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers" '{"fromUnit":6,"rate":0.75}')" >/dev/null
expect 400 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers" '{"fromUnit":4,"toUnit":7,"rate":0.5}')" | jq -e '.errors.fromUnit' >/dev/null || fail "traslape"
expect 400 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers" '{"fromUnit":9,"toUnit":8,"rate":1}')" | jq -e '.errors.fromUnit' >/dev/null || fail "rango inválido"
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers/$T1_ID" '{"toUnit":6}')" | jq -e '.errors.fromUnit' >/dev/null || fail "editar Hasta con traslape"
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers/$T1_ID" '{"fromUnit":9,"toUnit":8}')" >/dev/null
T1B=$(expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers/$T1_ID" "{\"toUnit\":5,\"effectiveFrom\":\"$TODAY\"}")")
T1_ID2=$(echo "$T1B" | jq -r .id); [[ "$T1_ID2" != "$T1_ID" ]] || fail "editar un tramo abre una fila nueva"
echo "$T1B" | jq -e --arg t "$TODAY" '.effectiveFrom==$t and .fromUnit==2 and .toUnit==5 and .rate==1' >/dev/null || fail "tramo nuevo: $T1B"
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components?includeHistory=true")" | jq -e --arg t "$TODAY" --argjson a "$T1_ID" --argjson b "$T1_ID2" '(.extraPiece[0].tiers | length)==3 and (.extraPiece[0].tiers[] | select(.id==$a) | .effectiveTo==$t and .isCurrent==false) and (.extraPiece[0].tiers[] | select(.id==$b) | .isCurrent==true)' >/dev/null || fail "historial de tramos"
expect 409 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers/$T1_ID" '{"rate":2}')" >/dev/null   # el cerrado no se edita
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers/$T1_ID2" '{"clearToUnit":true}')" >/dev/null   # 2+ traslapa con 6+
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billExtraPiece":false}')" >/dev/null
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers" '{"fromUnit":20,"rate":0.5}')" >/dev/null
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components")" | jq -e '(.extraPiece | length)==1 and (.extraPiece[0].tiers | length)==2' >/dev/null || fail "R7b: pieza extra y tramos visibles con componente apagado"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billExtraPiece":true}')" >/dev/null
ok "tramos 2–5 y 6+, traslape y rango inválido (alta y edición), cerrar+abrir, componente apagado (409 y R7b)"

step "cotización del contrato (Lote 2): bitácora 7 + 6.25 + 3 + 5 + 5 = 26.25"
# Segundo par servicio+paquete en el mismo contrato (documento L217: una fila por combinación; el 409 por fila vigente es solo por el mismo par)
RC_ENV=$(expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"ENVELOPE","rate":5}')" | jq -r .id)
Q=$(expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":8},{\"serviceType\":\"STANDARD\",\"packageType\":\"ENVELOPE\",\"pieces\":1}],\"codAmount\":200}")")
echo "$Q" | jq -e --argjson a "$RC_ID2" --argjson b "$EP_ID" --argjson c "$RC_ENV" '.lines[0].baseRate==7 and .lines[0].baseSource=="CONTRACT" and .lines[0].extraPieces==6.25 and .lines[1].baseRate==5 and .lines[1].baseSource=="CONTRACT" and .lines[1].extraPieces==0 and .dispatchFee==3 and .codFee==5 and .total==26.25 and (.rateComponentIds | index($a) != null) and (.rateComponentIds | index($b) != null) and (.rateComponentIds | index($c) != null)' >/dev/null || fail "cotización: $Q"
expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":8}],\"codAmount\":200,\"asOf\":\"2026-06-01\"}")" | jq -e '.lines[0].baseRate==6.5 and .lines[0].extraSource=="NONE"' >/dev/null || fail "cotización asOf"
# R7b con 'Por servicio' y COD apagados: las tarifas siguen visibles, no se cobran (base cae a genérica/NONE, COD 0), el alta responde 409
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billPerService":false,"billCodFee":false}')" >/dev/null
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components")" | jq -e '(.perService | length)==2 and ([.perService[] | select(.packageType=="BOX")][0].rate==7)' >/dev/null || fail "R7b: tarifas por servicio visibles con componente apagado"
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"EXPRESS","packageType":"BOX","rate":9}')" >/dev/null   # componente apagado
expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":8}],\"codAmount\":200}")" | jq -e '(.lines[0].baseSource=="GENERIC" or .lines[0].baseSource=="NONE") and .codFee==0 and .dispatchFee==3 and .lines[0].extraPieces==6.25' >/dev/null || fail "R7b: cotización con Por servicio y COD apagados"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billPerService":true,"billCodFee":true}')" >/dev/null
expect 400 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":0}]}")" >/dev/null
expect 400 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":1}],\"codAmount\":-1}")" | jq -e '.errors.codAmount' >/dev/null || fail "COD negativo"
expect 400 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":1},{\"serviceType\":\"standard\",\"packageType\":\"box\",\"pieces\":1}]}")" | jq -e '.errors["lines[1]"]' >/dev/null || fail "líneas repetidas"
ok "multi-línea con dos bases de contrato, asOf, R7b (Por servicio y COD apagados), piezas 0, COD negativo y par repetido"

step "cierre conservando historial (Lote 2): tramos, componente, tarifa por servicio"
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers/$T1_ID2/close" '{}')" | jq -e --arg t "$TODAY" '.effectiveTo==$t and .isCurrent==false' >/dev/null || fail "cerrar tramo"
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers/$T1_ID2/close" '{}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers" '{"fromUnit":2,"toUnit":5,"rate":1.1}')" >/dev/null   # el cerrado ya no cuenta para el traslape
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/close" '{}')" | jq -e --arg t "$TODAY" '.effectiveTo==$t and ([.tiers[] | select(.effectiveTo==null)] | length)==0' >/dev/null || fail "cerrar componente en cascada"
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/close" '{}')" >/dev/null
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$EP_ID/tiers" '{"fromUnit":2,"toUnit":5,"rate":1}')" >/dev/null   # componente cerrado
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components")" | jq -e '(.extraPiece | length)==0' >/dev/null || fail "componente cerrado oculto"
expect 400 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID2/close" '{"effectiveTo":"2025-01-01"}')" | jq -e '.errors.effectiveTo' >/dev/null || fail "cierre anterior al inicio"
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components/$RC_ID2/close" '{}')" | jq -e '.isCurrent==false' >/dev/null || fail "cerrar tarifa por servicio"
# Guarda por intervalo: la fila original (2026-01-01 → hoy) sigue vigente en junio, así que un alta con vigencia pasada choca (409)
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":9,"effectiveFrom":"2026-06-01"}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":7}')" >/dev/null   # misma combinación tras cerrar
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components?includeHistory=true")" | jq -e '(.perService | length)==4 and ([.perService[] | select(.effectiveTo != null)] | length)==2' >/dev/null || fail "historial completo"
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_PID/rate-components")" | jq -e '(.perService | length)==2' >/dev/null || fail "solo las vigentes (BOX y ENVELOPE)"
ok "quitar = cerrar (tramo, componente en cascada, tarifa), 409 por intervalo, misma combinación tras cerrar"

step "servicios especiales (Lote 2): tipos compartidos del tenant, tarifa efectivo-fechada"
SS=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"newTypeName\":\"Vagón del muelle $TS\",\"rate\":150}")")
SS_ID=$(echo "$SS" | jq -r .id); TYPE_ID=$(echo "$SS" | jq -r .typeId)
expect 200 "$(req GET /api/v1/special-service-types)" | jq -e --argjson t "$TYPE_ID" '.[] | select(.id==$t) | .clientsUsing==1' >/dev/null || fail "tipo del tenant"
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"newTypeName\":\"vagon del muelle $TS\",\"rate\":1}")" >/dev/null   # mismo tipo normalizado
expect 400 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"typeId\":$TYPE_ID,\"newTypeName\":\"x\",\"rate\":1}")" | jq -e '.errors.typeId' >/dev/null || fail "typeId y newTypeName a la vez"
expect 400 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" '{"rate":1}')" | jq -e '.errors.typeId' >/dev/null || fail "sin tipo"
expect 400 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"newTypeName\":\"Negativo $TS\",\"rate\":-1}")" | jq -e '.errors.rate' >/dev/null || fail "tarifa negativa"
C2=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Beauty Code $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")")
CLIENT2_PID=$(echo "$C2" | jq -r .publicId); CONTRACT_C2_PID=$(echo "$C2" | jq -r .currentContract.publicId); CLIENT2_ID=$(echo "$C2" | jq -r .id)
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_C2_PID/billing-model" '{"billSpecialServices":true}')" >/dev/null
SS_C2=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT2_PID/special-services" "{\"typeId\":$TYPE_ID,\"rate\":120}")")
echo "$SS_C2" | jq -e --argjson t "$TYPE_ID" '.typeId==$t' >/dev/null || fail "tipo compartido"; SS_C2_ID=$(echo "$SS_C2" | jq -r .id)
expect 200 "$(req GET /api/v1/special-service-types)" | jq -e --argjson t "$TYPE_ID" '.[] | select(.id==$t) | .clientsUsing==2' >/dev/null || fail "clientsUsing==2"
SS2=$(expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_PID/special-services/$SS_ID" '{"rate":160}')")
SS_ID2=$(echo "$SS2" | jq -r .id); [[ "$SS_ID2" != "$SS_ID" ]] || fail "nueva versión de tarifa especial"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/special-services?includeHistory=true")" | jq -e '(.items | length)==2' >/dev/null || fail "historial especiales"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billSpecialServices":false}')" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/special-services")" | jq -e '.componentEnabled==false and (.items | length)>=1' >/dev/null || fail "R7b: filas visibles con componente apagado"
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"newTypeName\":\"Otro $TS\",\"rate\":1}")" >/dev/null
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/billing-model" '{"billSpecialServices":true}')" >/dev/null
# Cierre conservando historial y misma combinación tras cerrar
expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services/$SS_ID2/close" '{}')" | jq -e --arg t "$TODAY" '.effectiveTo==$t and .isCurrent==false' >/dev/null || fail "cerrar especial"
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services/$SS_ID2/close" '{}')" >/dev/null
SS_ID3=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"typeId\":$TYPE_ID,\"rate\":170}")" | jq -r .id)
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/special-services?includeHistory=true")" | jq -e '(.items | length)==3' >/dev/null || fail "historial 3 filas"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/special-services")" | jq -e '(.items | length)==1' >/dev/null || fail "una vigente"
expect 200 "$(req GET /api/v1/special-service-types)" | jq -e --argjson t "$TYPE_ID" '.[] | select(.id==$t) | .clientsUsing==2' >/dev/null || fail "clientsUsing sigue en 2"
# Baja lógica del tipo (documento L214): 409 mientras haya tarifas abiertas; sale del selector; no se puede usar; reactivable
expect 409 "$(req POST "/api/v1/special-service-types/$TYPE_ID/deactivate")" | jq -e '.title | contains("2 cliente(s)")' >/dev/null || fail "tipo con tarifas abiertas"
expect 400 "$(req PATCH "/api/v1/clients/$CLIENT_PID/special-services/$SS_ID3" '{"rate":1,"effectiveFrom":"2025-01-01"}')" | jq -e '.errors.effectiveFrom' >/dev/null || fail "versión especial anterior al inicio"
expect 400 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services/$SS_ID3/close" '{"effectiveTo":"2025-01-01"}')" | jq -e '.errors.effectiveTo' >/dev/null || fail "cierre especial anterior al inicio"
expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services/$SS_ID3/close" '{}')" >/dev/null
expect 200 "$(req POST "/api/v1/clients/$CLIENT2_PID/special-services/$SS_C2_ID/close" '{}')" >/dev/null
expect 200 "$(req POST "/api/v1/special-service-types/$TYPE_ID/deactivate")" | jq -e '.isActive==false and .clientsUsing==0' >/dev/null || fail "desactivar tipo"
expect 409 "$(req POST "/api/v1/special-service-types/$TYPE_ID/deactivate")" >/dev/null   # ya inactivo
expect 200 "$(req GET /api/v1/special-service-types)" | jq -e --argjson t "$TYPE_ID" '(map(select(.id==$t)) | length)==0' >/dev/null || fail "tipo inactivo oculto"
expect 200 "$(req GET '/api/v1/special-service-types?includeInactive=true')" | jq -e --argjson t "$TYPE_ID" '(map(select(.id==$t)) | length)==1' >/dev/null || fail "tipo inactivo con includeInactive"
expect 400 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"typeId\":$TYPE_ID,\"rate\":1}")" | jq -e '.errors.typeId' >/dev/null || fail "tipo inactivo no se usa"
expect 404 "$(req POST /api/v1/special-service-types/999999/reactivate)" >/dev/null
expect 200 "$(req POST "/api/v1/special-service-types/$TYPE_ID/reactivate")" | jq -e '.isActive==true' >/dev/null || fail "reactivar tipo"
expect 409 "$(req POST "/api/v1/special-service-types/$TYPE_ID/reactivate")" >/dev/null   # ya activo
expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/special-services" "{\"typeId\":$TYPE_ID,\"rate\":170}")" >/dev/null   # vuelve a usarse
ok "tipo nuevo/reutilizado, validaciones (incluidas fechas anteriores al inicio), tipo compartido entre clientes, nueva versión, R7b, cierre, baja/reactivación del tipo"

step "estatus (Lote 2): cliente, contrato con efecto, contrato vigente y baja lógica"
expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/status" '{"toCode":"SUSPENDED","comment":"smoke"}')" | jq -e '.status=="SUSPENDED"' >/dev/null || fail "suspender cliente"
expect 422 "$(req POST "/api/v1/contracts/$CONTRACT_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null     # ContractStatusEffect: cliente suspendido
expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/status" '{"toCode":"ACTIVE"}')" | jq -e '.status=="ACTIVE"' >/dev/null || fail "regreso desde lateral"
expect 200 "$(req GET "/api/v1/status/history/CLIENT/$CLIENT_ID")" | jq -e 'length >= 3' >/dev/null || fail "historial de estatus del cliente"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"endDate":"2026-01-31"}')" | jq -e '.endDate=="2026-01-31"' >/dev/null || fail "fecha fin pasada"
expect 400 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID" '{"startDate":"2027-01-01"}')" | jq -e '.errors.startDate' >/dev/null || fail "inicio posterior al fin"
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/status" '{"toCode":"ACTIVE"}')" | jq -e '.status=="ACTIVE"' >/dev/null || fail "activar (la fecha fin pasada no bloquea)"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e --arg c "$CONTRACT_PID" '.currentContract.publicId==$c' >/dev/null || fail "sigue vigente con EndDate pasada"
expect 422 "$(req POST "/api/v1/contracts/$CONTRACT_PID/status" '{"toCode":"DRAFT"}')" >/dev/null
C2X=$(expect 200 "$(req POST /api/v1/contracts "{\"clientPublicId\":\"$CLIENT_PID\",\"title\":\"Extra\",\"startDate\":\"2026-01-01\"}")")
CONTRACT2_PID=$(echo "$C2X" | jq -r .publicId); echo "$C2X" | jq -e '.contractNumber | endswith("-C2")' >/dev/null || fail "numeración -C2"
expect 200 "$(req GET "/api/v1/contracts?clientId=$CLIENT_PID")" | jq -e --arg a "$CONTRACT_PID" --arg b "$CONTRACT2_PID" '(map(select(.publicId==$a)) | length)==1 and (map(select(.publicId==$b)) | length)==1' >/dev/null || fail "lista incluye C1 y C2"
expect 409 "$(req POST /api/v1/contracts "{\"clientPublicId\":\"$CLIENT_PID\",\"contractNumber\":\"$CODE-C1\",\"title\":\"Duplicado\",\"startDate\":\"2026-01-01\"}")" | jq -e '.title | contains("Ya existe un contrato con el número")' >/dev/null || fail "número explícito duplicado"
expect 422 "$(req POST "/api/v1/contracts/$CONTRACT2_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null   # un solo contrato ACTIVE por cliente
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT2_PID/status" '{"toCode":"CANCELLED"}')" >/dev/null
expect 422 "$(req PATCH "/api/v1/contracts/$CONTRACT2_PID/dispatch-fee" '{"amount":1}')" >/dev/null   # EDIT_CONTRACT bloqueado en terminal
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT2_PID")" | jq -e '.canEdit==false' >/dev/null || fail "canEdit en terminal"
# Reemplazo del contrato vigente: ACTIVE → EXPIRED (siguiente por SortOrder), EDIT_CONTRACT=0 en EXPIRED, el vigente se suelta, se activa el siguiente, ACTIVE → CANCELLED
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_PID/status" '{"toCode":"EXPIRED","comment":"smoke"}')" | jq -e '.status=="EXPIRED" and .canEdit==false' >/dev/null || fail "ACTIVE → EXPIRED"
expect 422 "$(req PATCH "/api/v1/contracts/$CONTRACT_PID/dispatch-fee" '{"amount":1}')" >/dev/null   # EDIT_CONTRACT bloqueado en EXPIRED (seed 3B)
expect 422 "$(req POST "/api/v1/contracts/$CONTRACT_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null   # terminal: no se sale
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e '.currentContract==null' >/dev/null || fail "EXPIRED nunca es vigente"
C4=$(expect 200 "$(req POST /api/v1/contracts "{\"clientPublicId\":\"$CLIENT_PID\",\"title\":\"Reemplazo\",\"startDate\":\"2026-01-01\"}")")
CONTRACT4_PID=$(echo "$C4" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT4_PID/status" '{"toCode":"ACTIVE"}')" | jq -e '.status=="ACTIVE"' >/dev/null || fail "activar el reemplazo tras expirar el anterior"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e --arg c "$CONTRACT4_PID" '.currentContract.publicId==$c' >/dev/null || fail "el reemplazo es el vigente"
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT4_PID/status" '{"toCode":"CANCELLED"}')" | jq -e '.status=="CANCELLED" and .canEdit==false' >/dev/null || fail "ACTIVE → CANCELLED"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID")" | jq -e '.currentContract==null' >/dev/null || fail "sin vigente tras cancelar"
# Sin contrato vigente: desempate DRAFT más reciente por StartDate; nunca EXPIRED/CANCELLED
C3=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Sin contrato $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")")
CLIENT3_PID=$(echo "$C3" | jq -r .publicId); C3_PID=$(echo "$C3" | jq -r .currentContract.publicId)
C3B_PID=$(expect 200 "$(req POST /api/v1/contracts "{\"clientPublicId\":\"$CLIENT3_PID\",\"title\":\"Nuevo\",\"startDate\":\"2026-03-01\"}")" | jq -r .publicId)
expect 200 "$(req GET "/api/v1/clients/$CLIENT3_PID")" | jq -e --arg c "$C3B_PID" '.currentContract.publicId==$c' >/dev/null || fail "DRAFT más reciente"
expect 200 "$(req POST "/api/v1/contracts/$C3_PID/status" '{"toCode":"CANCELLED"}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$C3B_PID/status" '{"toCode":"CANCELLED"}')" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT3_PID")" | jq -e '.currentContract==null and .billingSummary==""' >/dev/null || fail "sin contrato vigente"
expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT3_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":3}],\"codAmount\":100}")" | jq -e '.contractPublicId==null and (.lines[0].baseSource=="GENERIC" or .lines[0].baseSource=="NONE") and .lines[0].extraPieces==0 and .dispatchFee==0 and .codFee==0' >/dev/null || fail "cotización sin contrato"
expect 409 "$(req POST "/api/v1/clients/$CLIENT3_PID/special-services" "{\"newTypeName\":\"X $TS\",\"rate\":1}")" | jq -e '.title | contains("no tiene un contrato vigente")' >/dev/null || fail "especial sin contrato"
# Baja lógica del cliente (nunca DELETE): desaparece de la lista, sigue accesible, reactivable
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/deactivate")" >/dev/null
expect 200 "$(req GET /api/v1/clients)" | jq -e --arg p "$CLIENT2_PID" '(map(select(.publicId==$p)) | length)==0' >/dev/null || fail "inactivo oculto"
expect 200 "$(req GET '/api/v1/clients?includeInactive=true')" | jq -e --arg p "$CLIENT2_PID" '.[] | select(.publicId==$p) | .isActive==false' >/dev/null || fail "includeInactive"
expect 200 "$(req GET "/api/v1/clients/$CLIENT2_PID")" | jq -e '.isActive==false' >/dev/null || fail "ficha accesible"
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/deactivate")" >/dev/null   # idempotente
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/reactivate")" >/dev/null
expect 200 "$(req GET /api/v1/clients)" | jq -e --arg p "$CLIENT2_PID" '.[] | select(.publicId==$p) | .isActive==true' >/dev/null || fail "reactivado"
ok "lateral reversible, 422 del efecto (suspendido / segundo ACTIVE), fecha fin no vence, número explícito duplicado, reemplazo ACTIVE → EXPIRED/CANCELLED y activación del siguiente, EDIT_CONTRACT en terminal, sin vigente, baja lógica"

step "usuarios de portal (Lote 2): invitar, aceptar (anónimo), suspender, reactivar, dar de baja"
INV=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/invite" "{\"email\":\"portal$TS@lasmarias.pr\",\"fullName\":\"Luis Cliente\",\"role\":\"CLIENT_ADMIN\"}")")
# R41: expiresAtUtc anuncia la ventana configurada (Portal:InviteHours = 48 h); la caducidad real la aplica el DataProtectorTokenProvider (no comprobable aquí)
echo "$INV" | jq -e '.user.status=="INVITED" and (.inviteToken | length) > 10 and (((.expiresAtUtc | sub("\\.[0-9]+";"") | fromdateiso8601) - now) | . > 47*3600 and . < 49*3600)' >/dev/null || fail "invitación: $INV"
expect 404 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/invite" "{\"email\":\"rolmalo$TS@lasmarias.pr\",\"role\":\"NO_EXISTE\"}")" >/dev/null   # rol de portal desconocido
PU_ID=$(echo "$INV" | jq -r .user.id); TOKEN_INV=$(echo "$INV" | jq -r .inviteToken)
DUP_MSG="Ese correo no está disponible para el portal de esta compañía."
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/invite" "{\"email\":\"$EMAIL\",\"role\":\"CLIENT_ADMIN\"}")" | jq -e --arg m "$DUP_MSG" '.title==$m' >/dev/null || fail "correo interno: mismo 409 neutro"
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/invite" "{\"email\":\"portal$TS@lasmarias.pr\",\"role\":\"CLIENT_ADMIN\"}")" | jq -e --arg m "$DUP_MSG" '.title==$m' >/dev/null || fail "invitar dos veces el mismo correo de portal: mismo 409 neutro"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=ROLE_CHANGE&take=10')" | jq -e '[.items[] | select((.detailJson // "") | contains("portal_invite_conflict"))] | length >= 1' >/dev/null || fail "SecurityEvent portal_invite_conflict"
# Sentido contrario de la unicidad: un correo de portal no se da de alta como usuario interno (ni se adjunta a la compañía)
expect 409 "$(req POST /api/v1/users "{\"email\":\"portal$TS@lasmarias.pr\",\"fullName\":\"x\"}")" | jq -e '.title | contains("usuario de portal")' >/dev/null || fail "POST /users con correo de portal"
TOKEN_INV2=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/resend-invite")" | jq -r .inviteToken)
[[ -n "$TOKEN_INV2" && "$TOKEN_INV2" != "$TOKEN_INV" ]] || fail "reenvío sin token nuevo"
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal$TS@lasmarias.pr\",\"token\":\"$TOKEN_INV\",\"password\":\"una-contrasena-larga-1\"}")" >/dev/null   # el enlace anterior deja de valer al reenviar
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal$TS@lasmarias.pr\",\"token\":\"basura\",\"password\":\"una-contrasena-larga-1\"}")" | jq -e '.title=="Invitación inválida o vencida."' >/dev/null || fail "token basura"
# Sin enumeración (documento L402): correo inexistente y correo interno reciben exactamente la misma respuesta
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"nadie$TS@lasmarias.pr\",\"token\":\"basura\",\"password\":\"una-contrasena-larga-1\"}")" | jq -e '.title=="Invitación inválida o vencida."' >/dev/null || fail "correo inexistente: misma respuesta (sin enumeración)"
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"$EMAIL\",\"token\":\"basura\",\"password\":\"una-contrasena-larga-1\"}")" | jq -e '.title=="Invitación inválida o vencida."' >/dev/null || fail "correo interno: misma respuesta (sin enumeración)"
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal$TS@lasmarias.pr\",\"token\":\"$TOKEN_INV2\",\"password\":\"corta\"}")" | jq -e '.title=="Invitación inválida o vencida."' >/dev/null || fail "contraseña corta (misma respuesta)"
expect 204 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal$TS@lasmarias.pr\",\"token\":\"$TOKEN_INV2\",\"password\":\"una-contrasena-larga-1\"}")" >/dev/null
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal$TS@lasmarias.pr\",\"token\":\"$TOKEN_INV2\",\"password\":\"una-contrasena-larga-1\"}")" >/dev/null   # un solo uso
# Documento L411: los eventos de acceso del portal entran al SecurityEvent — fallo sin userId, éxito atribuido al invitado
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PASSWORD_CHANGE&take=10')" | jq -e '([.items[] | select(((.detailJson // "") | contains("portal_invite_accept_failed")) and .userId == null)] | length >= 1) and ([.items[] | select(((.detailJson // "") | contains("portal_invite_accepted")) and .userId != null)] | length >= 1)' >/dev/null || fail "SecurityEvent PASSWORD_CHANGE portal_invite_accept_failed (sin userId) / portal_invite_accepted (atribuido)"
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$PU_ID" '.[] | select(.id==$i) | .status=="ACTIVE"' >/dev/null || fail "ACTIVE tras aceptar"
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/resend-invite")" >/dev/null   # ya no está INVITED
expect 401 "$(anon POST /api/v1/auth/login "{\"email\":\"portal$TS@lasmarias.pr\",\"password\":\"una-contrasena-larga-1\"}")" >/dev/null   # el portal no entra por la app interna
expect 200 "$(req GET /api/v1/users)" | jq -e --arg e "portal$TS@lasmarias.pr" '(map(select(.email==$e)) | length)==0' >/dev/null || fail "R39: no aparece en /users"
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/suspend")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$PU_ID" '.[] | select(.id==$i) | .status=="SUSPENDED"' >/dev/null || fail "SUSPENDED"
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/suspend")" >/dev/null
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/reactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$PU_ID" '.[] | select(.id==$i) | .status=="ACTIVE"' >/dev/null || fail "reactivado"
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/suspend")" >/dev/null
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/remove")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$PU_ID" '.[] | select(.id==$i) | .status=="DISABLED"' >/dev/null || fail "DISABLED"
expect 409 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU_ID/reactivate")" >/dev/null   # terminal
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=ROLE_CHANGE&take=10')" | jq -e '[.items[] | select((.detailJson // "") | contains("portal_invite"))] | length >= 1' >/dev/null || fail "SecurityEvent portal_invite"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=5')" | jq -e '([.items[] | select((.detailJson // "") | contains("portal_user_suspended"))] | length >= 1) and ([.items[] | select((.detailJson // "") | contains("portal_user_disabled"))] | length >= 1)' >/dev/null || fail "SecurityEvent TOKEN_REVOKED portal_user_suspended/portal_user_disabled"
ok "invitación por token de un solo uso (el reenvío mata el anterior) con expiración anunciada, rol desconocido 404, 409 neutro (interno, repetido, /users), misma respuesta 400 exista o no la cuenta / sea interna, R39, SUSPENDED reversible, DISABLED terminal, eventos ROLE_CHANGE/PASSWORD_CHANGE/TOKEN_REVOKED"

step "R42 (Lote 2): el admin de plataforma queda atribuido dentro del tenant"
ME_TID=$(echo "$ME" | jq -r .tenantId)
RS=$(expect 200 "$(anon POST /api/v1/auth/login "{\"email\":\"$PLATFORM_EMAIL\",\"password\":\"$PASS\",\"tenantId\":$ME_TID,\"deviceInfo\":\"smoke\"}")")
[[ $(echo "$RS" | jq -r .status) == "ok" ]] || fail "login soporte: $RS"
T_SOP=$(echo "$RS" | jq -r .tokens.accessToken)
SOP_ID=$(expect 200 "$(req GET /api/v1/me '' "$T_SOP")" | jq -r .userId)
INV2=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/invite" "{\"email\":\"portal2$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}" "$T_SOP")")
PU2_ID=$(echo "$INV2" | jq -r .user.id); TOKEN_INV_P2=$(echo "$INV2" | jq -r .inviteToken)
expect 200 "$(req GET "/api/v1/audit/security-events?eventType=ROLE_CHANGE&userId=$SOP_ID&take=5")" | jq -e '.total >= 1 and ([.items[] | select((.detailJson // "") | contains("portal_invite"))] | length >= 1)' >/dev/null || fail "evento atribuido a soporte"
expect 200 "$(req GET "/api/v1/audit/changes?entityType=PORTAL_USER&userId=$SOP_ID&take=5")" | jq -e '.total >= 1' >/dev/null || fail "AuditLog atribuido a soporte"
ok "invitación hecha por soporte@ visible y atribuida en la bitácora del tenant"

step "aislamiento entre tenants (Lote 2)"
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}" "$T_SOP")"); T_SOP=$(echo "$RE" | jq -r .accessToken)
# mfaRequired:false — el resto del smoke no enrola TOTP; sin esto, Tenant.MfaRequired por default (Lote F8a) bloquearía
# el login normal de este tenant nuevo con "mfa_required".
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Smoke $TS\",\"modules\":[\"CATALOG\",\"CLIENT_PORTAL\"],\"adminEmail\":\"admin$TS@smoke.local\",\"adminFullName\":\"Admin Smoke\",\"adminPassword\":\"Smoke_Admin_2026!\",\"mfaRequired\":false}" "$T_SOP")" >/dev/null
R3=$(expect 200 "$(anon POST /api/v1/auth/login "{\"email\":\"admin$TS@smoke.local\",\"password\":\"Smoke_Admin_2026!\"}")"); T3=$(echo "$R3" | jq -r .tokens.accessToken)
expect 200 "$(req GET /api/v1/me '' "$T3")" | jq -e '.permissions | index("clients.read")' >/dev/null || fail "plantilla clonada sin clients.read"
expect 404 "$(req GET "/api/v1/clients/$CLIENT_PID" '' "$T3")" >/dev/null
expect 404 "$(req POST "/api/v1/contracts/$CONTRACT_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":1}' "$T3")" >/dev/null
expect 404 "$(req GET "/api/v1/clients/$CLIENT_PID/special-services" '' "$T3")" >/dev/null
expect 200 "$(req GET /api/v1/locations '' "$T3")" | jq -e 'length==0' >/dev/null || fail "locations de otro tenant visibles"
expect 200 "$(req GET "/api/v1/contacts/CLIENT/$CLIENT_ID" '' "$T3")" | jq -e 'length==0' >/dev/null || fail "contactos de otro tenant visibles"
# Correo de portal de otro tenant: mismo 409 neutro al invitar (sin oráculo de causa) y 409 al darlo de alta como interno (no se adjunta la cuenta ajena)
C3T=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Cliente Smoke $TS\",\"paymentTerm\":\"NET30\",\"currency\":\"USD\",\"contract\":{\"startDate\":\"2026-01-01\",\"serviceLevels\":[{\"serviceType\":\"STANDARD\",\"maxTransitHours\":48}]}}" "$T3")")
CLIENT_T3_PID=$(echo "$C3T" | jq -r .publicId); CONTRACT_T3_PID=$(echo "$C3T" | jq -r .currentContract.publicId)
# BOLA por id hijo: padre de T3 en la ruta + id de hija del tenant demo → 404 (las hijas no llevan TenantId; solo las protege el vínculo ClientId/ContractId)
expect 404 "$(req PATCH "/api/v1/clients/$CLIENT_T3_PID/contacts/$CONTACT2_ID" '{"role":"x"}' "$T3")" >/dev/null
expect 404 "$(req POST "/api/v1/clients/$CLIENT_T3_PID/portal-users/$PU_ID/suspend" '' "$T3")" >/dev/null
expect 404 "$(req PATCH "/api/v1/contracts/$CONTRACT_T3_PID/rate-components/$RC_ID2" '{"rate":1}' "$T3")" >/dev/null
expect 404 "$(req POST "/api/v1/contracts/$CONTRACT_T3_PID/rate-components/$EP_ID/tiers" '{"fromUnit":2,"rate":1}' "$T3")" >/dev/null
expect 409 "$(req POST "/api/v1/clients/$CLIENT_T3_PID/portal-users/invite" "{\"email\":\"portal$TS@lasmarias.pr\",\"role\":\"CLIENT_ADMIN\"}" "$T3")" | jq -e --arg m "$DUP_MSG" '.title==$m' >/dev/null || fail "invitar correo de portal ajeno: mismo 409 neutro"
expect 409 "$(req POST /api/v1/users "{\"email\":\"portal$TS@lasmarias.pr\",\"fullName\":\"x\"}" "$T3")" | jq -e '.title | contains("usuario de portal")' >/dev/null || fail "POST /users de otro tenant con correo de portal"
expect 200 "$(req GET /api/v1/users '' "$T3")" | jq -e --arg e "portal$TS@lasmarias.pr" '(map(select(.email==$e)) | length)==0' >/dev/null || fail "la cuenta de portal no se adjuntó al otro tenant"
ok "otro tenant: 404 por PublicId, 404 por id hijo ajeno bajo padre propio, listas vacías, permisos nuevos en el clon, unicidad de correo bidireccional sin oráculo"

step "fuentes de datos y contenido de sistema (Lote 2)"
expect 200 "$(req GET /api/v1/analytics/data-sources)" | jq -e '(map(.key) | index("CLIENT") != null) and (map(.key) | index("CONTRACT") != null) and (map(.key) | index("LOCATION") != null)' >/dev/null || fail "data-sources"
PV=$(expect 200 "$(req POST '/api/v1/analytics/reports/CONTRACT/preview?dateRangeMode=ALL' '{"name":"x","columns":["ContractNumber","Status","Client.Name"],"secondary":["Client"]}')")
echo "$PV" | jq -e '.total >= 2 and (.columns | map(.key) | index("Client.Name") != null)' >/dev/null || fail "preview CONTRACT + Client: $PV"
PV=$(expect 200 "$(req POST '/api/v1/analytics/reports/CLIENT/preview?dateRangeMode=ALL' '{"name":"x","columns":["Code","BillingSummary"]}')")
echo "$PV" | jq -e '[.rows[] | select((.BillingSummary // "") | contains("Por servicio"))] | length >= 1' >/dev/null || fail "preview CLIENT: $PV"
expect 200 "$(req GET /api/v1/analytics/pulse)" | jq -e '[.indicators[] | select(.name=="Clientes activos" and .value >= 2)] | length == 1' >/dev/null || fail "indicador Clientes activos"
# Vista y gráfico de sistema sembrados por SystemAnalyticsSeeder (los nombres de campo deben coincidir con los IDataSource)
RPT_ID=$(expect 200 "$(req GET /api/v1/analytics/reports)" | jq -r '[.[] | select(.name=="Clientes" and .isSystem==true)][0].id')
[[ -n "$RPT_ID" && "$RPT_ID" != "null" ]] || fail "vista de sistema Clientes no sembrada"
expect 200 "$(req POST "/api/v1/analytics/reports/$RPT_ID/run" '{}')" | jq -e '.total >= 2 and (.columns | map(.key) | index("BillingSummary") != null)' >/dev/null || fail "run de la vista Clientes"
CH_ID=$(expect 200 "$(req GET /api/v1/analytics/charts)" | jq -r '[.[] | select(.name=="Contratos por estatus" and .isSystem==true)][0].id')
[[ -n "$CH_ID" && "$CH_ID" != "null" ]] || fail "gráfico de sistema Contratos por estatus no sembrado"
expect 200 "$(req GET "/api/v1/analytics/charts/$CH_ID/data")" | jq -e '.points | length >= 1' >/dev/null || fail "datos del gráfico Contratos por estatus"
PV=$(expect 200 "$(req POST '/api/v1/analytics/reports/LOCATION/preview?dateRangeMode=ALL' '{"name":"x","columns":["Name","IsShared","Client.Name"],"secondary":["Client"]}')")
echo "$PV" | jq -e '.total >= 2 and ([.rows[] | select(.IsShared==true)] | length >= 1) and ([.rows[] | select(.IsShared==false and (."Client.Name" // "") != "")] | length >= 1)' >/dev/null || fail "preview LOCATION + Client: $PV"
ok "CLIENT/CONTRACT/LOCATION, vista combinada, BillingSummary, vista/indicador/gráfico de sistema, LOCATION con cliente"

step "RBAC y módulo CLIENT_PORTAL (Lote 2)"
expect 200 "$(req GET /api/v1/clients '' "$T2")" >/dev/null
expect 403 "$(req POST /api/v1/clients '{"name":"x"}' "$T2")" >/dev/null
expect 403 "$(req POST "/api/v1/clients/$CLIENT2_PID/deactivate" '' "$T2")" >/dev/null
expect 403 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users" '' "$T2")" >/dev/null
# Defensa en profundidad en las rutas polimórficas: escribir campos personalizados de CLIENT/CONTRACT/LOCATION exige clients/contracts/locations.update
expect 403 "$(req PUT "/api/v1/custom-fields/values/CLIENT/$CLIENT_ID" '{"values":{}}' "$T2")" | jq -e '.title=="Falta el permiso '"'"'clients.update'"'"'."' >/dev/null || fail "custom-fields CLIENT sin clients.update"
expect 403 "$(req PUT "/api/v1/custom-fields/values/LOCATION/$(expect 200 "$(req GET "/api/v1/locations/$LOC_PID")" | jq -r .id)" '{"values":{}}' "$T2")" >/dev/null
expect 200 "$(req GET "/api/v1/contacts/CLIENT/$CLIENT_ID" '' "$T2")" >/dev/null   # el despachador sí tiene clients.read
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=5')" | jq -e '.total >= 1' >/dev/null || fail "PERMISSION_DENIED"
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken)
# Un usuario con solo orders.view no lee contactos, historial ni campos personalizados de clientes (permiso de lectura de la entidad dueña)
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Solo órdenes $TS\",\"permissions\":[\"orders.view\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"soloordenes$TS@teikem.local\",\"fullName\":\"Solo Órdenes\",\"password\":\"$PASS\",\"roles\":[\"Solo órdenes $TS\"]}")" >/dev/null
T4=$(expect 200 "$(anon POST /api/v1/auth/login "{\"email\":\"soloordenes$TS@teikem.local\",\"password\":\"$PASS\"}")" | jq -r .tokens.accessToken)
expect 403 "$(req GET "/api/v1/contacts/CLIENT/$CLIENT_ID" '' "$T4")" | jq -e '.title=="Falta el permiso '"'"'clients.read'"'"'."' >/dev/null || fail "contactos de CLIENT sin clients.read"
expect 403 "$(req GET "/api/v1/contacts/CLIENT_CONTACT/$CONTACT2_ID" '' "$T4")" >/dev/null
expect 403 "$(req GET "/api/v1/status/history/CLIENT/$CLIENT_ID" '' "$T4")" >/dev/null
expect 403 "$(req GET "/api/v1/status/history/PORTAL_USER/$PU_ID" '' "$T4")" >/dev/null
expect 403 "$(req GET "/api/v1/custom-fields/values/CLIENT/$CLIENT_ID" '' "$T4")" >/dev/null
expect 200 "$(req PUT /api/v1/modules/CLIENT_PORTAL '{"isEnabled":false}')" >/dev/null
expect 403 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e '.code=="module_disabled"' >/dev/null || fail "módulo apagado"
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal2$TS@lasmarias.pr\",\"token\":\"$TOKEN_INV_P2\",\"password\":\"otra-contrasena-larga-2\"}")" >/dev/null   # módulo apagado
expect 200 "$(req PUT /api/v1/modules/CLIENT_PORTAL '{"isEnabled":true}')" >/dev/null
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU2_ID/remove")" >/dev/null   # baja de una invitación nunca aceptada
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$PU2_ID" '.[] | select(.id==$i) | .status=="DISABLED"' >/dev/null || fail "portal2 DISABLED"
expect 400 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal2$TS@lasmarias.pr\",\"token\":\"$TOKEN_INV_P2\",\"password\":\"otra-contrasena-larga-2\"}")" >/dev/null   # cuenta inactiva
# Reinvitación tras baja: mismo correo, misma fila (renace null → INVITED con historial) y la cuenta vuelve a servir
RI=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/invite" "{\"email\":\"portal2$TS@lasmarias.pr\",\"fullName\":\"Portal Dos\",\"role\":\"CLIENT_OPERATOR\"}")")
echo "$RI" | jq -e --argjson i "$PU2_ID" '.user.id==$i and .user.status=="INVITED" and .user.role=="CLIENT_OPERATOR" and (.inviteToken | length) > 10' >/dev/null || fail "reinvitación: $RI"
expect 204 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"portal2$TS@lasmarias.pr\",\"token\":\"$(echo "$RI" | jq -r .inviteToken)\",\"password\":\"otra-contrasena-larga-2\"}")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$PU2_ID" '.[] | select(.id==$i) | .status=="ACTIVE"' >/dev/null || fail "reinvitado ACTIVE"
expect 200 "$(req GET "/api/v1/status/history/PORTAL_USER/$PU2_ID")" | jq -e 'length >= 3' >/dev/null || fail "historial de la reinvitación"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=ROLE_CHANGE&take=10')" | jq -e '[.items[] | select((.detailJson // "") | contains("portal_reinvite"))] | length >= 1' >/dev/null || fail "SecurityEvent portal_reinvite"
ok "despachador: lectura sí, escritura 403 (incluidos campos personalizados); solo orders.view: contactos/historial/valores 403; módulo apagado 403/400; baja desde INVITED; reinvitación tras baja"

step "auditoría del Lote 2"
for ET in CLIENT CLIENT_CONTACT CONTRACT LOCATION RATE_COMPONENT SPECIAL_SERVICE PORTAL_USER; do
  expect 200 "$(req GET "/api/v1/audit/changes?entityType=$ET&take=1")" | jq -e '.total >= 1' >/dev/null || fail "sin bitácora de $ET"
done
expect 200 "$(req GET '/api/v1/audit/changes?entityType=RATE_COMPONENT&take=5')" | jq -e '.total >= 3 and ([.items[] | select((.changesJson // "") | ascii_downcase | contains("token"))] | length)==0' >/dev/null || fail "RATE_COMPONENT auditado sin secretos"
expect 200 "$(req GET '/api/v1/audit/activity?kind=changes&take=100')" | jq -e '.total >= 1 and ([.items[] | select(.kind=="change")] | length >= 1)' >/dev/null || fail "actividad de cambios"
ok "Lote 2: clientes, consignatarios, contratos, tarifas, especiales, portal, aislamiento y auditoría"

# ============================================================================================================
# Lote 3 — Órdenes de transporte. Re-ejecutable: TS en nombres y patrón de numeración (contadores por cliente nuevos en
# cada corrida). Reutiliza del Lote 2: CLIENT_PID/CLIENT2_PID/CONTRACT_C2_PID, SHARED_PID, LOC_PID, SS_ID3, PU2_ID.
# ============================================================================================================
login() { expect 200 "$(anon POST /api/v1/auth/login "{\"email\":\"$1\",\"password\":\"$2\"}")" | jq -r .tokens.accessToken; }
ordnum() { printf 'T%s-%05d' "$TS" "$1"; }   # número de orden automático del cliente de órdenes (patrón T$TS-#####)
seqof() { local n=${1##*-}; echo $((10#$n)); }  # consecutivo de un número automático
T2=$(login "$DISPATCH_EMAIL" "$PASS"); T3=$(login "admin$TS@smoke.local" "Smoke_Admin_2026!"); T4=$(login "soloordenes$TS@teikem.local" "$PASS")
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken)

step "órdenes de transporte (Lote 3): prerrequisitos (cliente con contrato y tarifas, servicio especial, consignatarios)"
expect 200 "$(req PUT '/api/v1/status/lateral-entries/TRANSPORT_ORDER?statusDomain=OrderStatus' '[{"lateralStatusCode":"CANCELLED","fromStatusCode":"PICKUP","isAllowed":true},{"lateralStatusCode":"CANCELLED","fromStatusCode":"DRAFT","isAllowed":true},{"lateralStatusCode":"CANCELLED","fromStatusCode":"CONFIRMED","isAllowed":true}]')" >/dev/null
CO=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Órdenes $TS\",\"paymentTerm\":\"NET30\",\"currency\":\"USD\",\"contract\":{\"startDate\":\"2026-01-01\"}}")")
CLIENT_O_PID=$(echo "$CO" | jq -r .publicId); CONTRACT_O_PID=$(echo "$CO" | jq -r .currentContract.publicId)
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_O_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_O_PID/billing-model" '{"billExtraPiece":true,"billDispatchFee":true,"billCodFee":true,"billSpecialServices":true}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_O_PID/dispatch-fee" '{"amount":3}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_O_PID/cod-fee" '{"type":"PERCENT","value":2.5}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_O_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":7}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_O_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"ENVELOPE","rate":5}')" >/dev/null
EPO=$(expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_O_PID/rate-components" '{"kind":"EXTRA_PIECE","serviceType":"STANDARD","packageType":"BOX"}')" | jq -r .id)
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_O_PID/rate-components/$EPO/tiers" '{"fromUnit":2,"toUnit":5,"rate":1.0}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_O_PID/rate-components/$EPO/tiers" '{"fromUnit":6,"rate":0.75}')" >/dev/null
SS_O_ID=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/special-services" "{\"newTypeName\":\"Vagón $TS\",\"rate\":150}")" | jq -r .id)
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/number-settings" "{\"clientAssignsInvoiceNumber\":true,\"orderNumberFormat\":\"T$TS-#####\"}")" >/dev/null
expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_O_PID\",\"name\":\"Central $TS\",\"locationType\":\"CORPORATE\",\"line1\":\"Calle Sol 1\",\"city\":\"Ponce\",\"postalCode\":\"00716\",\"country\":\"PR\",\"deliveryNotes\":\"Portón azul\",\"defaultWindowStart\":\"08:00:00\",\"defaultWindowEnd\":\"12:00:00\"}")" >/dev/null
LOC_A_PID=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_O_PID\",\"name\":\"Consignatario A $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 5 #12\",\"city\":\"Ponce\",\"postalCode\":\"00716\",\"country\":\"PR\",\"allowDupInvoice\":true}")" | jq -r .publicId)
LOC_B_PID=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_O_PID\",\"name\":\"Consignatario B $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 7\",\"city\":\"Juana Díaz\",\"country\":\"PR\",\"allowDupInvoice\":false}")" | jq -r .publicId)
COB=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Órdenes B $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")"); CLIENT_OB_PID=$(echo "$COB" | jq -r .publicId)
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_OB_PID/number-settings" '{"clientAssignsOrderNumber":true}')" >/dev/null
LOC_BB_PID=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_OB_PID\",\"name\":\"Consignatario BB $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 9\",\"city\":\"Arecibo\",\"country\":\"PR\"}")" | jq -r .publicId)
LOC_COUNT_0=$(expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_O_PID")" | jq 'length')
# Cuerpo base de una orden del cliente de órdenes hacia el consignatario A (jq -c añade/pisa campos con --argjson extra)
ob() { local x=${1:-}; [[ -n "$x" ]] || x='{}'
  jq -cn --arg c "$CLIENT_O_PID" --arg l "$LOC_A_PID" --argjson x "$x" '{clientPublicId:$c,consigneeLocationPublicId:$l,packages:[{pieces:1}]} + $x'; }
ok "clientes, contrato ACTIVE con tarifas (BOX 7, ENVELOPE 5, tramos 2–5/6+, despacho 3, COD 2.5 %), servicio especial y consignatarios"

step "órdenes (Lote 3): entrada rápida con defaults del tenant, numeración y snapshot de paradas"
O1=$(expect 200 "$(req POST /api/v1/orders "$(ob)")")
O1_PID=$(echo "$O1" | jq -r .publicId); O1_ID=$(echo "$O1" | jq -r .id); O1_NUM=$(echo "$O1" | jq -r .orderNumber); O1_PB=$(echo "$O1" | jq -r .packBatchNumber)
echo "$O1" | jq -e --arg n "$(ordnum 1)" --arg c "Consignatario A $TS" --arg p "Central $TS" '.status=="DRAFT" and .isInitialStatus and .serviceType=="STANDARD" and .packages[0].packageType=="BOX" and .orderNumber==$n and (.packBatchNumber | test("^EMP-[0-9]{5,}$")) and (.clientInvoiceNumber | test("^FAC-[0-9]{5}$")) and (.packages[0].packageNumber | test("^PQT-")) and .delivery.name==$c and .delivery.line1=="Calle 5 #12" and .delivery.city=="Ponce" and .delivery.windowStartUtc==null and .pickup.name==$p and .pickup.notes=="Portón azul" and .codStatus==null and .capabilities.canEditCargo and .capabilities.canDelete and .capabilities.canConfirm and (.capabilities.canReprice | not)' >/dev/null || fail "entrada rápida: $O1"
O2=$(expect 200 "$(req POST /api/v1/orders "$(ob '{"requestedDate":"2026-10-05T00:00:00"}')")")
echo "$O2" | jq -e --arg n "$(ordnum 2)" --arg f "$(echo "$O1" | jq -r .clientInvoiceNumber)" '.orderNumber==$n and .clientInvoiceNumber!=$f and (.pickup.windowStartUtc | startswith("2026-10-05T08:00:00")) and (.pickup.windowEndUtc | startswith("2026-10-05T12:00:00"))' >/dev/null || fail "segunda orden / ventana copiada: $O2"
expect 200 "$(req GET "/api/v1/orders/$O1_PID")" | jq -e --arg n "$O1_NUM" --arg b "$O1_PB" '.orderNumber==$n and .packBatchNumber==$b' >/dev/null || fail "ficha"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$O1_ID")" | jq -e 'length==1 and .[0].toCode=="DRAFT" and .[0].fromCode==null' >/dev/null || fail "historial de nacimiento"
# Snapshot (L236/L254): editar el consignatario después no altera la orden; se restaura porque LOC_A se reutiliza abajo
expect 200 "$(req PATCH "/api/v1/locations/$LOC_A_PID" '{"line1":"Otra 99","city":"Yauco"}')" >/dev/null
expect 200 "$(req GET "/api/v1/orders/$O1_PID")" | jq -e '.delivery.line1=="Calle 5 #12" and .delivery.city=="Ponce"' >/dev/null || fail "snapshot: la orden no debe cambiar al editar el consignatario"
expect 200 "$(req PATCH "/api/v1/locations/$LOC_A_PID" '{"line1":"Calle 5 #12","city":"Ponce"}')" >/dev/null
ok "DRAFT con STANDARD/BOX por defecto, T$TS-00001/00002, EMP-/FAC-/PQT- generados, recogido en la corporativa con notas y ventana, snapshot de paradas aislado del directorio"

step "órdenes (Lote 3): concurrencia del contador (8 altas simultáneas)"
TMPD=$(mktemp -d)
for i in 1 2 3 4 5 6 7 8; do req POST /api/v1/orders "$(ob)" > "$TMPD/$i" & done
wait
for i in 1 2 3 4 5 6 7 8; do [[ $(tail -n1 "$TMPD/$i") == "200" ]] || fail "alta simultánea $i: $(cat "$TMPD/$i")"; done
ALL=$(for i in 1 2 3 4 5 6 7 8; do sed '$d' "$TMPD/$i"; echo; done | jq -s '.'); rm -rf "$TMPD"
echo "$ALL" | jq -e '([.[].orderNumber] | unique | length)==8 and ([.[].packBatchNumber] | unique | length)==8 and ([.[].clientInvoiceNumber] | unique | length)==8 and ([.[].orderNumber | split("-") | last | tonumber] | (max - min)==7)' >/dev/null || fail "números repetidos o con huecos: $(echo "$ALL" | jq -c '[.[].orderNumber]')"
ok "ocho creaciones simultáneas, ocho números distintos y consecutivos (sin 409/500)"

step "órdenes (Lote 3): quién asigna cada número, unicidad por cliente y colisión automático/tecleado"
expect 400 "$(req POST /api/v1/orders "$(ob "{\"orderNumber\":\"MANUAL-$TS\"}")")" | jq -e '.errors.orderNumber' >/dev/null || fail "número tecleado cuando lo asigna Teikem"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/number-settings" '{"clientAssignsOrderNumber":true}')" >/dev/null
O_MAN=$(expect 200 "$(req POST /api/v1/orders "$(ob "{\"orderNumber\":\"MANUAL-$TS\"}")")"); O_MAN_PID=$(echo "$O_MAN" | jq -r .publicId)
echo "$O_MAN" | jq -e --arg n "MANUAL-$TS" '.orderNumber==$n' >/dev/null || fail "número tecleado"
expect 409 "$(req POST /api/v1/orders "$(ob "{\"orderNumber\":\"MANUAL-$TS\"}")")" | jq -e '.title=="Ya existe una orden con ese número para este cliente."' >/dev/null || fail "número repetido en el mismo cliente"
expect 200 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT_OB_PID\",\"consigneeLocationPublicId\":\"$LOC_BB_PID\",\"orderNumber\":\"MANUAL-$TS\",\"packages\":[{\"pieces\":1}]}")" >/dev/null
expect 200 "$(req GET "/api/v1/orders/lookup?code=MANUAL-$TS")" | jq -e '.matchedBy=="ORDER_NUMBER" and (.matches | length)==2' >/dev/null || fail "mismo número en dos clientes"
# Patrón de 40 caracteres: el consecutivo 10 lo desborda (Resolve no trunca) → 409 con mensaje de negocio y rollback de los consecutivos
LONGPAT="$(printf 'X%.0s' $(seq 1 39))#"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_OB_PID/number-settings" "{\"packageNumberFormat\":\"$LONGPAT\"}")" >/dev/null
PK12=$(jq -cn --arg c "$CLIENT_OB_PID" --arg l "$LOC_BB_PID" '{clientPublicId:$c,consigneeLocationPublicId:$l,packages:[range(12) | {pieces:1}]}')
expect 409 "$(req POST /api/v1/orders "$PK12")" | jq -e '.title=="El número de paquete generado con el patrón del cliente excede 40 caracteres; acorte el patrón en la ficha del cliente."' >/dev/null || fail "número de paquete generado demasiado largo"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_OB_PID/number-settings" '{"packageNumberFormat":""}')" >/dev/null
expect 200 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT_OB_PID\",\"consigneeLocationPublicId\":\"$LOC_BB_PID\",\"packages\":[{\"pieces\":1}]}")" | jq -e '.packages[0].packageNumber=="PQT-00002"' >/dev/null || fail "el 409 por número largo no debía gastar consecutivos de paquete"
expect 400 "$(req POST /api/v1/orders "$(ob '{"codType":"CASH"}')")" | jq -e '.title | contains("se registra al entregar")' >/dev/null || fail "codType en la captura"
expect 400 "$(req POST /api/v1/orders "$(ob '{"packBatchNumber":"EMP-1"}')")" | jq -e '.title | contains("siempre lo genera Teikem")' >/dev/null || fail "packBatchNumber en la captura"
expect 200 "$(req POST /api/v1/orders "$(ob "{\"clientInvoiceNumber\":\"INV-$TS-1\"}")")" | jq -e --arg f "INV-$TS-1" '.clientInvoiceNumber==$f' >/dev/null || fail "factura tecleada"
LASTN=$(seqof "$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .orderNumber)")
expect 200 "$(req POST /api/v1/orders "$(ob "{\"orderNumber\":\"$(ordnum $((LASTN + 1)))\"}")")" >/dev/null   # alguien teclea el siguiente automático
expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -e --arg n "$(ordnum $((LASTN + 2)))" '.orderNumber==$n' >/dev/null || fail "el contador debía saltar el número chocado"
ok "400 si lo asigna Teikem, 409 por cliente, mismo número en otro cliente, codType/packBatchNumber 400, colisión con salto"

step "órdenes (Lote 3): validaciones de captura y estado del cliente"
expect 400 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT_O_PID\",\"packages\":[{\"pieces\":1}]}")" | jq -e '.errors.consignee' >/dev/null || fail "sin consignatario"
expect 400 "$(req POST /api/v1/orders "$(ob '{"newConsignee":{"name":"X","line1":"Y","city":"Ponce"}}')")" | jq -e '.errors.consignee' >/dev/null || fail "consignatario doble"
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":[]}')")" | jq -e '.errors.packages' >/dev/null || fail "sin paquetes"
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":[{"pieces":0}]}')")" | jq -e '.errors["packages[0].pieces"]' >/dev/null || fail "piezas 0"
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":[{"packageType":"NOPE","pieces":1}]}')")" | jq -e '.errors["packages[0].packageType"]' >/dev/null || fail "tipo de paquete desconocido"
expect 400 "$(req POST /api/v1/orders "$(ob '{"serviceType":"NOPE"}')")" | jq -e '.errors.serviceType' >/dev/null || fail "tipo de servicio desconocido"
expect 400 "$(req POST /api/v1/orders "$(ob '{"codAmount":-1}')")" | jq -e '.errors.codAmount' >/dev/null || fail "COD negativo"
# Topes de las columnas: 400 por campo, nunca 500
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":[{"pieces":1,"weightKg":1000000000000}]}')")" | jq -e '.errors["packages[0].weightKg"]' >/dev/null || fail "peso fuera de rango"
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":[{"pieces":1,"volumeM3":123456789.5}]}')")" | jq -e '.errors["packages[0].volumeM3"]' >/dev/null || fail "volumen fuera de rango"
expect 400 "$(req POST /api/v1/orders "$(ob '{"codAmount":100000000000000000}')")" | jq -e '.errors.codAmount' >/dev/null || fail "COD fuera de rango"
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":[{"pieces":2000000000},{"pieces":2000000000}]}')")" | jq -e '.errors.packages' >/dev/null || fail "total de piezas fuera de rango"
# Valor de catálogo deshabilitado por el tenant (L54/L1158): deja de aceptarse al capturar
expect 200 "$(req PUT /api/v1/catalogs/PackageType/PALLET/override '{"isEnabled":false}')" >/dev/null
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":[{"packageType":"PALLET","pieces":1}]}')")" | jq -e '.errors["packages[0].packageType"]' >/dev/null || fail "tipo de paquete deshabilitado por el tenant"
expect 204 "$(req DELETE /api/v1/catalogs/PackageType/PALLET/override)" >/dev/null
expect 404 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":\"$LOC_PID\"}")")" | jq -e '.title | contains("Consignatario")' >/dev/null || fail "consignatario de otro cliente"
expect 204 "$(req POST "/api/v1/locations/$LOC_B_PID/deactivate")" >/dev/null
expect 400 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":\"$LOC_B_PID\"}")")" | jq -e '.title | contains("inactivo")' >/dev/null || fail "consignatario inactivo"
expect 204 "$(req POST "/api/v1/locations/$LOC_B_PID/reactivate")" >/dev/null
# Recogido explícito (DECISIÓN 13): ajeno 404, inactivo 400, precedencia del almacén por defecto, orden sin recogido y alta de PICKUP por PATCH
expect 404 "$(req POST /api/v1/orders "$(ob "{\"pickupLocationPublicId\":\"$LOC_PID\"}")")" | jq -e '.title | contains("recogido")' >/dev/null || fail "recogido de otro cliente"
expect 204 "$(req POST "/api/v1/locations/$LOC_B_PID/deactivate")" >/dev/null
expect 400 "$(req POST /api/v1/orders "$(ob "{\"pickupLocationPublicId\":\"$LOC_B_PID\"}")")" | jq -e '.errors.pickupLocationPublicId' >/dev/null || fail "recogido inactivo"
expect 204 "$(req POST "/api/v1/locations/$LOC_B_PID/reactivate")" >/dev/null
WH_O=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_O_PID\",\"name\":\"Almacén O $TS\",\"locationType\":\"PICKUP\",\"line1\":\"Carr. 1\",\"city\":\"Ponce\",\"country\":\"PR\"}")" | jq -r .publicId)
LOC_COUNT_0=$((LOC_COUNT_0 + 1))   # el almacén cuenta en el directorio del cliente (paso de consignatario al vuelo)
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" "{\"defaultPickupLocationPublicId\":\"$WH_O\"}")" >/dev/null
expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -e --arg w "Almacén O $TS" '.pickup.name==$w' >/dev/null || fail "precedencia del almacén por defecto sobre la corporativa"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"clearDefaultPickup":true}')" >/dev/null
expect 200 "$(req POST /api/v1/orders "$(ob "{\"pickupLocationPublicId\":\"$WH_O\"}")")" | jq -e --arg w "Almacén O $TS" '.pickup.name==$w' >/dev/null || fail "recogido explícito"
ONP=$(expect 200 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT_OB_PID\",\"consigneeLocationPublicId\":\"$LOC_BB_PID\",\"orderNumber\":\"NOPK-$TS\",\"packages\":[{\"pieces\":1}]}")")
echo "$ONP" | jq -e '.pickup==null' >/dev/null || fail "orden sin recogido: $ONP"
WH_OB=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_OB_PID\",\"name\":\"Almacén OB $TS\",\"locationType\":\"PICKUP\",\"line1\":\"Carr. 2\",\"city\":\"Arecibo\",\"country\":\"PR\"}")" | jq -r .publicId)
expect 200 "$(req PATCH "/api/v1/orders/$(echo "$ONP" | jq -r .publicId)" "{\"pickupLocationPublicId\":\"$WH_OB\"}")" | jq -e --arg w "Almacén OB $TS" '.pickup.name==$w' >/dev/null || fail "alta de la parada PICKUP por PATCH"
# País ISO alfa-2 (snapshot CHAR(2) de la parada): 400 en la captura, en el directorio y en el catálogo global, nunca 500
CCMSG="El código de país debe ser ISO 3166-1 alfa-2 (2 letras)."
expect 400 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":null,\"newConsignee\":{\"name\":\"Mex $TS\",\"line1\":\"Calle 1\",\"city\":\"Ponce\",\"country\":\"MEX\"}}")")" | jq -e --arg m "$CCMSG" '.errors["newConsignee.country"][0]==$m' >/dev/null || fail "país de 3 letras en la captura"
expect 400 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_O_PID\",\"name\":\"Mex $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 1\",\"city\":\"Ponce\",\"country\":\"MEX\"}")" | jq -e --arg m "$CCMSG" '.errors.country[0]==$m' >/dev/null || fail "país de 3 letras en el directorio"
expect 400 "$(req POST /api/v1/catalogs/Country '{"code":"MEX","labels":{"es":"México","en":"Mexico"}}' "$T_SOP")" | jq -e --arg m "$CCMSG" '.errors.code[0]==$m' >/dev/null || fail "país de 3 letras en el catálogo global"
expect 400 "$(req POST /api/v1/orders "{\"consigneeLocationPublicId\":\"$LOC_A_PID\",\"packages\":[{\"pieces\":1}]}")" | jq -e '.errors.clientPublicId' >/dev/null || fail "sin cliente"
OSUS=$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/status" '{"toCode":"SUSPENDED","comment":"smoke órdenes"}')" >/dev/null
expect 422 "$(req POST /api/v1/orders "$(ob)")" | jq -e '.title | contains("suspendido")' >/dev/null || fail "cliente suspendido"
expect 422 "$(req POST "/api/v1/orders/$OSUS/confirm" '{}')" | jq -e '.title=="El cliente está suspendido; no se pueden crear ni confirmar órdenes."' >/dev/null || fail "confirmar con cliente suspendido"
expect 200 "$(req GET "/api/v1/orders/$OSUS")" | jq -e '.status=="DRAFT" and .quotedAmount==null' >/dev/null || fail "la confirmación con cliente suspendido se revirtió"
expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null
ok "consignatario obligatorio/único/ajeno (404)/inactivo, recogido ajeno/inactivo/por defecto/ausente/por PATCH, país ISO-2, paquetes, servicio, COD, topes de columnas, catálogo deshabilitado, cliente obligatorio y suspendido (422: ni se crea ni se confirma)"

step "órdenes (Lote 3): multi-paquete, consignatario coincidente y consignatario al vuelo"
MP=$(expect 200 "$(req POST /api/v1/orders "$(ob '{"packages":[{"packageType":"BOX","pieces":2},{"packageType":"ENVELOPE","pieces":1,"description":"Documentos"}]}')")")
echo "$MP" | jq -e '.totalPieces==3 and (.packages | length)==2 and .packagesSummary=="Caja ×2 + Sobre ×1"' >/dev/null || fail "multi-paquete: $MP"
expect 200 "$(req GET "/api/v1/orders?clientId=$CLIENT_O_PID&orderNumber=$(echo "$MP" | jq -r .orderNumber)")" | jq -e '.total==1 and (.items[0].packages | length)==2 and (.items[0].packBatchNumber | length) > 0' >/dev/null || fail "listado multi-paquete"
expect 200 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":null,\"newConsignee\":{\"name\":\"  consignatario a $TS \",\"line1\":\"calle 5 #12\",\"city\":\"Ponce\"}}")")" | jq -e --arg l "$LOC_A_PID" '.delivery.locationPublicId==$l' >/dev/null || fail "coincidencia normalizada"
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_O_PID")" | jq -e --argjson n "$LOC_COUNT_0" 'length==$n' >/dev/null || fail "la coincidencia no debe crear consignatarios"
NEWC=$(expect 200 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":null,\"newConsignee\":{\"name\":\"Nuevo $TS\",\"line1\":\"Calle 9\",\"city\":\"Ponce\",\"postalCode\":\"00716\",\"deliveryNotes\":\"Timbre roto\"}}")")")
echo "$NEWC" | jq -e --arg n "Nuevo $TS" '.delivery.name==$n and .delivery.notes=="Timbre roto"' >/dev/null || fail "consignatario al vuelo: $NEWC"
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_O_PID")" | jq -e --argjson n "$LOC_COUNT_0" --arg c "Nuevo $TS" 'length==($n + 1) and (map(select(.name==$c and .locationType=="DELIVERY" and .isShared==false)) | length)==1' >/dev/null || fail "consignatario al vuelo asignado al cliente"
ok "Caja ×2 + Sobre ×1, coincidencia nombre + línea 1 sin duplicar el directorio, alta al vuelo DELIVERY del cliente"

step "órdenes (Lote 3): factura repetida por consignatario (R36) al crear y al cambiar de consignatario"
D1=$(expect 200 "$(req POST /api/v1/orders "$(ob "{\"clientInvoiceNumber\":\"DUP-$TS\"}")")"); D1_NUM=$(echo "$D1" | jq -r .orderNumber); D1_PID=$(echo "$D1" | jq -r .publicId)
expect 409 "$(req POST /api/v1/orders "$(ob "{\"clientInvoiceNumber\":\"DUP-$TS\"}")")" | jq -e --arg n "$D1_NUM" --arg p "$D1_PID" '.code=="duplicate_invoice_confirmable" and .errors.existingOrderNumber[0]==$n and .errors.existingOrderPublicId[0]==$p and (.title | contains($n))' >/dev/null || fail "R36 confirmable"
expect 200 "$(req POST /api/v1/orders "$(ob "{\"clientInvoiceNumber\":\"DUP-$TS\",\"confirmDuplicateInvoice\":true}")")" | jq -e --arg n "$(ordnum $(( $(seqof "$D1_NUM") + 1 )))" '.orderNumber==$n' >/dev/null || fail "crear de todos modos (y el 409 no gastó consecutivo)"
expect 200 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":\"$LOC_B_PID\",\"clientInvoiceNumber\":\"DUPB-$TS\"}")")" >/dev/null
expect 409 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":\"$LOC_B_PID\",\"clientInvoiceNumber\":\"DUPB-$TS\",\"confirmDuplicateInvoice\":true}")")" | jq -e '.code=="duplicate_invoice" and (.errors.existingOrderNumber | length)==1' >/dev/null || fail "R36 bloqueada"
expect 409 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":null,\"newConsignee\":{\"name\":\"Consignatario A $TS\",\"line1\":\"Calle 5 #12\",\"city\":\"Ponce\"},\"clientInvoiceNumber\":\"DUP-$TS\"}")")" | jq -e '.code=="duplicate_invoice_confirmable"' >/dev/null || fail "R36 con consignatario coincidente"
expect 200 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":\"$SHARED_PID\",\"clientInvoiceNumber\":\"DUP-$TS\"}")")" >/dev/null   # otro consignatario: sin aviso
# PATCH que cambia el consignatario reevalúa R36 (la orden editada no cuenta como duplicado de sí misma)
MV=$(expect 200 "$(req POST /api/v1/orders "$(ob "{\"clientInvoiceNumber\":\"DUPB-$TS\"}")")" | jq -r .publicId)
expect 409 "$(req PATCH "/api/v1/orders/$MV" "{\"consigneeLocationPublicId\":\"$LOC_B_PID\"}")" | jq -e '.code=="duplicate_invoice"' >/dev/null || fail "PATCH hacia consignatario sin duplicados"
MV2=$(expect 200 "$(req POST /api/v1/orders "$(ob "{\"consigneeLocationPublicId\":\"$LOC_B_PID\",\"clientInvoiceNumber\":\"DUP-$TS\"}")")" | jq -r .publicId)
expect 409 "$(req PATCH "/api/v1/orders/$MV2" "{\"consigneeLocationPublicId\":\"$LOC_A_PID\"}")" | jq -e '.code=="duplicate_invoice_confirmable"' >/dev/null || fail "PATCH hacia consignatario con duplicado confirmable"
expect 200 "$(req PATCH "/api/v1/orders/$MV2" "{\"consigneeLocationPublicId\":\"$LOC_A_PID\",\"confirmDuplicateInvoice\":true}")" | jq -e --arg c "Consignatario A $TS" '.delivery.name==$c' >/dev/null || fail "PATCH confirmado"
# PATCH de una orden con factura GENERADA hacia un consignatario sin duplicados: R36 no aplica (L1124: solo números tecleados),
# aunque el cliente teclee sus facturas y otra orden de otro cliente tenga el mismo FAC- automático en ese consignatario.
SHR=$(expect 200 "$(req POST /api/v1/locations "{\"name\":\"Compartido R36 $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle R36\",\"city\":\"Ponce\",\"country\":\"PR\"}")" | jq -r .publicId)
CX=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"R36 X $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId)
CY=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"R36 Y $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId)
for C in "$CX" "$CY"; do expect 200 "$(req PATCH "/api/v1/clients/$C/number-settings" '{"clientAssignsInvoiceNumber":true}')" >/dev/null; done
LY=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CY\",\"name\":\"Propio Y $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle Y\",\"city\":\"Ponce\",\"country\":\"PR\"}")" | jq -r .publicId)
FX=$(expect 200 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CX\",\"consigneeLocationPublicId\":\"$SHR\",\"packages\":[{\"pieces\":1}]}")" | jq -r .clientInvoiceNumber)
OY=$(expect 200 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CY\",\"consigneeLocationPublicId\":\"$LY\",\"packages\":[{\"pieces\":1}]}")")
echo "$OY" | jq -e --arg f "$FX" '.clientInvoiceNumber==$f' >/dev/null || fail "prerrequisito: mismo FAC- automático en dos clientes ($FX): $OY"
expect 200 "$(req PATCH "/api/v1/orders/$(echo "$OY" | jq -r .publicId)" "{\"consigneeLocationPublicId\":\"$SHR\"}")" | jq -e --arg n "Compartido R36 $TS" '.delivery.name==$n' >/dev/null || fail "PATCH con factura generada no debe dar 409 R36"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/number-settings" '{"clientAssignsInvoiceNumber":false}')" >/dev/null
expect 400 "$(req POST /api/v1/orders "$(ob "{\"clientInvoiceNumber\":\"DUP-$TS\"}")")" | jq -e '.errors.clientInvoiceNumber' >/dev/null || fail "factura tecleada cuando la asigna Teikem"
# Factura tecleada y después el cliente pasa a 'Teikem asigna': el PATCH sigue revisando R36 (la orden recuerda que se tecleó)
expect 409 "$(req PATCH "/api/v1/orders/$MV" "{\"consigneeLocationPublicId\":\"$LOC_B_PID\"}")" | jq -e '.code=="duplicate_invoice"' >/dev/null || fail "PATCH de factura tecleada con el ajuste cambiado"
expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -e '.clientInvoiceNumber | test("^FAC-")' >/dev/null || fail "factura automática"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/number-settings" '{"clientAssignsInvoiceNumber":true}')" >/dev/null
ok "409 duplicate_invoice_confirmable/duplicate_invoice con errors estructurados, crear de todos modos, coincidencia, otro consignatario, PATCH de consignatario (solo facturas tecleadas), 400 cuando la asigna Teikem"

step "órdenes (Lote 3): vista previa y confirmar = cotizar y congelar"
Q=$(expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_O_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":8},{\"serviceType\":\"STANDARD\",\"packageType\":\"ENVELOPE\",\"pieces\":1}],\"codAmount\":200}")")
QT=$(echo "$Q" | jq -r .total); echo "$Q" | jq -e '.total==26.25' >/dev/null || fail "cotización del contrato (7 + 6.25 + 3 + 5 + 5): $Q"
OQ=$(expect 200 "$(req POST /api/v1/orders "$(ob '{"packages":[{"packageType":"BOX","pieces":8},{"packageType":"ENVELOPE","pieces":1}],"codAmount":200}')")")
OQ_PID=$(echo "$OQ" | jq -r .publicId); OQ_ID=$(echo "$OQ" | jq -r .id); echo "$OQ" | jq -e '.codStatus=="PENDING"' >/dev/null || fail "COD PENDING"
expect 200 "$(req GET "/api/v1/orders/$OQ_PID/quote")" | jq -e --argjson t "$QT" '.total==$t and (.lines | length)==2 and .credit.exceeds==false and .credit.creditLimit==null and .credit.orderTotal==$t' >/dev/null || fail "vista previa"
expect 200 "$(req GET "/api/v1/orders/$OQ_PID")" | jq -e '.quotedAmount==null' >/dev/null || fail "la vista previa no persiste"
expect 200 "$(req POST "/api/v1/orders/$OQ_PID/confirm" '{}')" | jq -e --argjson t "$QT" --arg c "$CONTRACT_O_PID" '.status=="CONFIRMED" and .quotedAmount==$t and .quotedAtUtc!=null and .confirmedAtUtc!=null and .contractPublicId==$c and .capabilities.canReprice and (.capabilities.canDelete | not) and (.capabilities.canConfirm | not) and (.capabilities.canEditCargo | not)' >/dev/null || fail "confirmar"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$OQ_ID")" | jq -e 'length==2 and (map(select(.toCode=="PENDING")) | length)==0' >/dev/null || fail "historial de la orden sin COD mezclado"
expect 200 "$(req GET "/api/v1/status/history/ORDER_COD/$OQ_ID")" | jq -e 'length==1 and .[0].toCode=="PENDING"' >/dev/null || fail "historial COD"
expect 422 "$(req POST "/api/v1/orders/$OQ_PID/confirm" '{}')" >/dev/null
expect 409 "$(req POST "/api/v1/orders/$O1_PID/confirm" '{"rowVersion":"AAAAAAAAAAA="}')" >/dev/null
expect 200 "$(req POST /api/v1/orders "$(ob '{"confirmNow":true}')")" | jq -e '.status=="CONFIRMED" and .quotedAmount > 0' >/dev/null || fail "confirmNow"
ok "GET /quote sin persistir, confirmar congela monto/fecha/contrato, COD bajo ORDER_COD, 422 al reconfirmar, 409 por rowVersion, confirmNow"

step "órdenes (Lote 3): crédito (aviso + autorización con permiso) y tarifa faltante"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":10}')" >/dev/null
OC1=$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .publicId)
expect 200 "$(req GET "/api/v1/orders/$OC1/quote")" | jq -e --argjson t "$QT" '.credit.exceeds==true and .credit.pendingBalance >= $t' >/dev/null || fail "vista previa con crédito excedido"
expect 422 "$(req POST "/api/v1/orders/$OC1/confirm" '{}')" | jq -e '.code=="credit_exceeded" and (.title | contains("límite de crédito")) and (.errors.available | length)==1' >/dev/null || fail "crédito excedido"
expect 200 "$(req GET "/api/v1/orders/$OC1")" | jq -e '.status=="DRAFT" and .quotedAmount==null' >/dev/null || fail "la confirmación fallida se revirtió"
N0=$(expect 200 "$(req GET "/api/v1/orders?clientId=$CLIENT_O_PID&take=1")" | jq .total)
expect 422 "$(req POST /api/v1/orders "$(ob '{"confirmNow":true}')")" | jq -e '.code=="credit_exceeded"' >/dev/null || fail "confirmNow con crédito excedido"
expect 200 "$(req GET "/api/v1/orders?clientId=$CLIENT_O_PID&take=1")" | jq -e --argjson n "$N0" '.total==$n' >/dev/null || fail "confirmNow fallido también revierte la creación"
# Una orden cancelada libera crédito: límite exacto = pendiente + esta orden
expect 200 "$(req POST "/api/v1/orders/$OQ_PID/cancel" '{"comment":"Cliente desistió"}')" | jq -e '.status=="CANCELLED"' >/dev/null || fail "cancelar"
PQ=$(expect 200 "$(req GET "/api/v1/orders/$OC1/quote")")
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" "{\"creditLimit\":$(echo "$PQ" | jq '.credit.pendingBalance + .credit.orderTotal')}")" >/dev/null
expect 200 "$(req POST "/api/v1/orders/$OC1/confirm" '{}')" | jq -e '.status=="CONFIRMED"' >/dev/null || fail "límite justo (la cancelada ya no cuenta)"
# Ajuste C: 422 credit_exceeded → overrideCredit exige orders.credit_override (despacho 403; admin confirma con bitácora)
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":10}')" >/dev/null
OC2=$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .publicId); OC2_ID=$(expect 200 "$(req GET "/api/v1/orders/$OC2")" | jq -r .id)
expect 422 "$(req POST "/api/v1/orders/$OC2/confirm" '{}')" | jq -e '.code=="credit_exceeded" and .errors.limit[0]=="10.00" and (.errors.exposure|length)==1 and (.errors.newAmount|length)==1 and (.errors.available|length)==1' >/dev/null || fail "credit_exceeded con limit/exposure/newAmount/available"
pdtotal() { expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=1')" | jq .total; }
PD0=$(pdtotal)
expect 403 "$(req POST "/api/v1/orders/$OC2/confirm" '{"overrideCredit":true}' "$T2")" | jq -e '.title=="Falta el permiso '"'"'orders.credit_override'"'"'."' >/dev/null || fail "403 override sin permiso"
[[ $(pdtotal) -gt $PD0 ]] || fail "PERMISSION_DENIED del override sin permiso"
expect 200 "$(req POST "/api/v1/orders/$OC2/confirm" '{"overrideCredit":true}')" | jq -e '.status=="CONFIRMED" and .quotedAmount > 0' >/dev/null || fail "override con permiso"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$OC2_ID")" | jq -e 'map(select((.comment // "") | startswith("Crédito excedido autorizado por"))) | length==1' >/dev/null || fail "comentario del override"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=ROLE_CHANGE&take=10')" | jq -e '[.items[] | select((.detailJson // "") | contains("credit_override"))] | length >= 1' >/dev/null || fail "SecurityEvent credit_override"
# Facturación (plantilla Billing: orders.view + orders.credit_override, sin orders.edit) autoriza desde POST /confirm; sin
# overrideCredit, o si el crédito no se excede, confirmar sigue exigiendo orders.edit
expect 200 "$(req POST /api/v1/users "{\"email\":\"facturacion$TS@teikem.local\",\"fullName\":\"Facturación $TS\",\"password\":\"$PASS\",\"roles\":[\"Billing\"]}")" >/dev/null
TBILL=$(login "facturacion$TS@teikem.local" "$PASS")
OC3=$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .publicId); OC3_ID=$(expect 200 "$(req GET "/api/v1/orders/$OC3")" | jq -r .id)
expect 403 "$(req POST "/api/v1/orders/$OC3/confirm" '{}' "$TBILL")" | jq -e '.title=="Falta el permiso '"'"'orders.edit'"'"'."' >/dev/null || fail "Facturación confirma sin overrideCredit"
expect 200 "$(req POST "/api/v1/orders/$OC3/confirm" '{"overrideCredit":true}' "$TBILL")" | jq -e '.status=="CONFIRMED"' >/dev/null || fail "Facturación autoriza el crédito excedido"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$OC3_ID")" | jq -e --arg n "Facturación $TS" 'any(.[]; (.comment // "") | startswith("Crédito excedido autorizado por " + $n))' >/dev/null || fail "comentario del override de Facturación"
PD0=$(pdtotal)
expect 403 "$(req POST /api/v1/orders "$(ob '{"confirmNow":true,"overrideCredit":true}')" "$T2")" | jq -e '.title=="Falta el permiso '"'"'orders.credit_override'"'"'."' >/dev/null || fail "403 confirmNow + override sin permiso"
[[ $(pdtotal) -gt $PD0 ]] || fail "PERMISSION_DENIED de confirmNow + override sin permiso"
expect 400 "$(req POST /api/v1/orders "$(ob '{"overrideCredit":true}')")" | jq -e '.errors.overrideCredit' >/dev/null || fail "overrideCredit sin confirmNow"
expect 200 "$(req POST /api/v1/orders "$(ob '{"confirmNow":true,"overrideCredit":true}')")" | jq -e '.status=="CONFIRMED"' >/dev/null || fail "confirmNow + overrideCredit"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":100000}')" >/dev/null
OC4=$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .publicId)
expect 403 "$(req POST "/api/v1/orders/$OC4/confirm" '{"overrideCredit":true}' "$TBILL")" | jq -e '.title=="Falta el permiso '"'"'orders.edit'"'"'."' >/dev/null || fail "el permiso de autorización no confirma órdenes dentro del crédito"
OX=$(expect 200 "$(req POST /api/v1/orders "$(ob '{"serviceType":"EXPRESS"}')")" | jq -r .publicId)
expect 422 "$(req GET "/api/v1/orders/$OX/quote")" | jq -e '.title | contains("No hay tarifa vigente")' >/dev/null || fail "vista previa sin tarifa"
expect 422 "$(req POST "/api/v1/orders/$OX/confirm" '{}')" | jq -e '.title | contains("No hay tarifa vigente")' >/dev/null || fail "confirmar sin tarifa"
ok "422 credit_exceeded revierte estatus y cotización (también confirmNow), la cancelada libera crédito, override 403/200 con historial y SecurityEvent, Facturación autoriza desde /confirm (sin orders.edit solo con crédito excedido), 422 sin tarifa"

step "órdenes (Lote 3): edición (solo DRAFT por defecto, campos fijos, re-cotización con REPRICE)"
expect 200 "$(req PATCH "/api/v1/orders/$O1_PID" "{\"consigneeLocationPublicId\":\"$LOC_B_PID\",\"packages\":[{\"packageType\":\"BOX\",\"pieces\":2,\"packageNumber\":\" PK-$TS \"},{\"packageType\":\"BOX\",\"pieces\":1}],\"notes\":\"Frágil\"}")" | jq -e --arg c "Consignatario B $TS" --arg pk "PK-$TS" '.delivery.name==$c and .totalPieces==3 and .notes=="Frágil" and ([.packages[].packageNumber] | index($pk) != null) and ([.packages[].packageNumber | select(test("^PQT-"))] | length)==1' >/dev/null || fail "PATCH (número de paquete tecleado y generado)"
expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" "{\"packages\":[{\"pieces\":1,\"packageNumber\":\"$(printf 'P%.0s' $(seq 1 41))\"}]}")" | jq -e '.errors["packages[0].packageNumber"]' >/dev/null || fail "número de paquete de 41 caracteres"
# Mismas validaciones que al crear (bitácora L1064)
expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" '{"packages":[]}')" | jq -e '.errors.packages | tostring | contains("Indique al menos una línea de paquete.")' >/dev/null || fail "PATCH sin paquetes"
expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" '{"packages":[{"packageType":"NOPE","pieces":1}]}')" | jq -e '.errors["packages[0].packageType"]' >/dev/null || fail "PATCH tipo de paquete desconocido"
expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" '{"packages":[{"pieces":0}]}')" | jq -e '.errors["packages[0].pieces"]' >/dev/null || fail "PATCH piezas 0"
expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" '{"serviceType":"NOPE"}')" | jq -e '.errors.serviceType' >/dev/null || fail "PATCH tipo de servicio desconocido"
# Consignatario escrito libre en el PATCH: coincide (nombre + línea 1) con el creado al vuelo arriba → se reutiliza; luego vuelve a B
expect 200 "$(req PATCH "/api/v1/orders/$O1_PID" "{\"newConsignee\":{\"name\":\" nuevo $TS\",\"line1\":\"calle 9\",\"city\":\"Ponce\"}}")" | jq -e --arg n "Nuevo $TS" '.delivery.name==$n and .delivery.notes=="Timbre roto"' >/dev/null || fail "PATCH con consignatario coincidente"
expect 200 "$(req PATCH "/api/v1/orders/$O1_PID" "{\"consigneeLocationPublicId\":\"$LOC_B_PID\"}")" >/dev/null
for F in '{"orderNumber":"X"}' "{\"clientPublicId\":\"$CLIENT_O_PID\"}" '{"clientInvoiceNumber":"X"}' '{"packBatchNumber":"X"}' '{"codType":"CASH"}'; do
  expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" "$F")" >/dev/null
done
expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" '{"orderNumber":"X"}')" | jq -e '.errors.orderNumber' >/dev/null || fail "orderNumber fijo"
expect 409 "$(req PATCH "/api/v1/orders/$O1_PID" '{"notes":"x","rowVersion":"AAAAAAAAAAA="}')" >/dev/null
# Referencias externas (L249, DECISIÓN 20): reemplazo completo en PATCH y tipo validado contra OrderRefType
expect 200 "$(req PATCH "/api/v1/orders/$O1_PID" '{"references":[{"refType":"CLIENT_PO","value":"PO-1"},{"refType":"carrier","value":"GUIA-9","source":"UPS"}]}')" | jq -e '(.references|length)==2 and .references[0].refType=="CLIENT_PO" and .references[0].value=="PO-1" and .references[1].refType=="CARRIER" and .references[1].source=="UPS"' >/dev/null || fail "referencias en PATCH"
expect 200 "$(req PATCH "/api/v1/orders/$O1_PID" '{"references":[{"refType":"ECOMMERCE","value":"SHOP-7"}]}')" | jq -e '(.references|length)==1 and .references[0].refType=="ECOMMERCE"' >/dev/null || fail "reemplazo completo de referencias"
expect 400 "$(req PATCH "/api/v1/orders/$O1_PID" '{"references":[{"refType":"NOPE","value":"X"}]}')" | jq -e '.errors["references[0].refType"]' >/dev/null || fail "tipo de referencia desconocido"
expect 422 "$(req PATCH "/api/v1/orders/$OC1" '{"packages":[{"packageType":"BOX","pieces":2}]}')" >/dev/null   # EDIT_CARGO apagado fuera de DRAFT
expect 200 "$(req PUT '/api/v1/status/capabilities/TRANSPORT_ORDER?statusDomain=OrderStatus' '[{"statusCode":"CONFIRMED","capability":"EDIT_CARGO","isAllowed":true}]')" >/dev/null
Q2=$(expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_O_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":2}]}")" | jq .total)
expect 200 "$(req PATCH "/api/v1/orders/$OC1" '{"packages":[{"packageType":"BOX","pieces":2}]}')" | jq -e --argjson t "$Q2" '.quotedAmount==$t' >/dev/null || fail "re-cotización al editar"
expect 200 "$(req PATCH "/api/v1/orders/$OC1" '{"codAmount":50}')" | jq -e '.codStatus=="PENDING" and .codAmount==50' >/dev/null || fail "COD por PATCH"
expect 200 "$(req PATCH "/api/v1/orders/$OC1" '{"clearCod":true}')" | jq -e '.codAmount==null and .codStatus==null' >/dev/null || fail "clearCod"
expect 200 "$(req PUT '/api/v1/status/capabilities/TRANSPORT_ORDER?statusDomain=OrderStatus' '[{"statusCode":"CONFIRMED","capability":"EDIT_CARGO","isAllowed":false}]')" >/dev/null
ok "PATCH de consignatario/paquetes (número tecleado y generado)/notas/referencias, mismas validaciones que al crear, campos fijos 400, rowVersion 409, EDIT_CARGO 422 fuera de DRAFT y re-cotización al relajarlo"

step "órdenes (Lote 3): reprecio y estatus laterales"
QA1=$(expect 200 "$(req GET "/api/v1/orders/$OC1")" | jq -r .quotedAtUtc)
expect 200 "$(req POST "/api/v1/orders/$OC1/reprice")" | jq -e --arg a "$QA1" '.quotedAtUtc != $a' >/dev/null || fail "reprecio"
expect 422 "$(req POST "/api/v1/orders/$O1_PID/reprice")" >/dev/null   # REPRICE apagado en DRAFT
expect 200 "$(req POST "/api/v1/orders/$OC1/status" '{"toCode":"ON_HOLD"}')" | jq -e '.status=="ON_HOLD"' >/dev/null || fail "lateral"
expect 422 "$(req POST "/api/v1/orders/$OC1/status" '{"toCode":"PICKUP"}')" | jq -e '.title | contains("módulo correspondiente")' >/dev/null || fail "rebote por lateral no avanza el pipeline"
expect 200 "$(req POST "/api/v1/orders/$OC1/status" '{"toCode":"CONFIRMED"}')" | jq -e '.status=="CONFIRMED"' >/dev/null || fail "regreso al último pipeline"
expect 422 "$(req POST "/api/v1/orders/$OC1/status" '{"toCode":"PLANNED"}')" | jq -e '.title | contains("módulo correspondiente")' >/dev/null || fail "avance reservado"
expect 422 "$(req POST "/api/v1/orders/$OC1/status" '{"toCode":"CANCELLED"}')" | jq -e '.title | contains("cancelación")' >/dev/null || fail "cancelar por /status"
expect 422 "$(req POST "/api/v1/orders/$O1_PID/status" '{"toCode":"CONFIRMED"}')" | jq -e '.title | contains("confirmación")' >/dev/null || fail "confirmar por /status"
expect 200 "$(req POST "/api/v1/orders/$O1_PID/status" '{"toCode":"ON_HOLD"}')" >/dev/null
expect 422 "$(req POST "/api/v1/orders/$O1_PID/status" '{"toCode":"CONFIRMED"}')" | jq -e '.title | contains("confirmación")' >/dev/null || fail "DRAFT → ON_HOLD → CONFIRMED no confirma sin cotizar"
expect 200 "$(req POST "/api/v1/orders/$O1_PID/status" '{"toCode":"DRAFT"}')" | jq -e '.status=="DRAFT" and .quotedAmount==null' >/dev/null || fail "regreso a DRAFT"
expect 404 "$(req POST "/api/v1/orders/$OC1/status" '{"toCode":"NOPE"}')" >/dev/null
LONGC=$(printf 'x%.0s' $(seq 1 600))
expect 400 "$(req POST "/api/v1/orders/$OC1/status" "{\"toCode\":\"ON_HOLD\",\"comment\":\"$LONGC\"}")" | jq -e '.errors.comment[0]=="El comentario admite como máximo 500 caracteres."' >/dev/null || fail "comentario de 600 caracteres en /status"
expect 400 "$(req POST "/api/v1/orders/$O1_PID/cancel" "{\"comment\":\"$LONGC\"}")" | jq -e '.errors.comment' >/dev/null || fail "comentario de 600 caracteres en /cancel"
expect 200 "$(req GET "/api/v1/orders/$O1_PID")" | jq -e '.status=="DRAFT"' >/dev/null || fail "el 400 por comentario no cambia el estatus"
expect 200 "$(req PUT /api/v1/status/OrderStatus/CONFIRMED/override '{"isEnabled":false}')" >/dev/null
OPK=$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/orders/$OPK/confirm" '{}')" | jq -e '.status=="PICKUP" and .quotedAmount > 0' >/dev/null || fail "confirmar con CONFIRMED deshabilitado"
expect 200 "$(req PUT /api/v1/status/OrderStatus/CONFIRMED/override '{"isEnabled":true}')" >/dev/null
ok "reprecio solo en CONFIRMED, laterales y regreso solo al último pipeline (rebote por lateral 422), avances reservados, comentario > 500 → 400, CONFIRMED deshabilitado → PICKUP"

step "órdenes (Lote 3): cancelar y eliminar"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$OQ_ID")" | jq -e '.[-1].comment=="Cliente desistió" and .[-1].toCode=="CANCELLED"' >/dev/null || fail "bitácora de cancelación"
expect 200 "$(req GET "/api/v1/orders/$OQ_PID")" | jq -e '.capabilities.canCancel | not' >/dev/null || fail "canCancel en terminal"
expect 422 "$(req POST "/api/v1/orders/$OQ_PID/cancel" '{}')" >/dev/null
expect 422 "$(req DELETE "/api/v1/orders/$OQ_PID")" | jq -e '.title | contains("estatus inicial")' >/dev/null || fail "eliminar cancelada"
expect 422 "$(req DELETE "/api/v1/orders/$OC1")" >/dev/null
expect 204 "$(req DELETE "/api/v1/orders/$O_MAN_PID")" >/dev/null
expect 200 "$(req GET "/api/v1/orders/$O_MAN_PID")" | jq -e '.isActive==false and (.capabilities.canDelete | not)' >/dev/null || fail "ficha de la eliminada (baja lógica)"
expect 200 "$(req GET "/api/v1/orders?includeInactive=true&orderNumber=MANUAL-$TS&clientId=$CLIENT_O_PID")" | jq -e '.total==1 and .items[0].isActive==false and .items[0].status=="DRAFT"' >/dev/null || fail "eliminada visible con includeInactive"
# Eliminada (L253): no se confirma, cancela, reprecia, edita, cambia de estatus ni se vuelve a eliminar (404 'Orden')
expect 404 "$(req POST "/api/v1/orders/$O_MAN_PID/confirm" '{}')" >/dev/null
expect 404 "$(req POST "/api/v1/orders/$O_MAN_PID/cancel" '{}')" >/dev/null
expect 404 "$(req POST "/api/v1/orders/$O_MAN_PID/status" '{"toCode":"ON_HOLD"}')" >/dev/null
expect 404 "$(req POST "/api/v1/orders/$O_MAN_PID/reprice")" >/dev/null
expect 404 "$(req PATCH "/api/v1/orders/$O_MAN_PID" '{"notes":"x"}')" >/dev/null
expect 404 "$(req DELETE "/api/v1/orders/$O_MAN_PID")" >/dev/null
expect 200 "$(req GET "/api/v1/orders/$O_MAN_PID")" | jq -e '.status=="DRAFT" and .quotedAmount==null and .isActive==false' >/dev/null || fail "la eliminada sigue en DRAFT sin cotizar"
expect 200 "$(req POST /api/v1/orders "$(ob "{\"orderNumber\":\"MANUAL-$TS\"}")")" >/dev/null   # número liberado por el índice filtrado
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Solo crear $TS\",\"permissions\":[\"orders.view\",\"orders.create\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"solocrear$TS@teikem.local\",\"fullName\":\"Solo Crear\",\"password\":\"$PASS\",\"roles\":[\"Solo crear $TS\"]}")" >/dev/null
T6=$(login "solocrear$TS@teikem.local" "$PASS")
OSC=$(expect 200 "$(req POST /api/v1/orders "$(ob)" "$T6")" | jq -r .publicId)
expect 200 "$(req POST /api/v1/orders "$(ob '{"confirmNow":true}')" "$T6")" | jq -e '.status=="CONFIRMED" and .quotedAmount > 0' >/dev/null || fail "confirmNow con solo orders.create (DECISIÓN 26)"
expect 403 "$(req POST "/api/v1/orders/$OSC/cancel" '{}' "$T6")" >/dev/null
expect 403 "$(req DELETE "/api/v1/orders/$OSC" '' "$T6")" >/dev/null
ok "cancelación con bitácora y terminal, eliminar solo en la etapa inicial (baja lógica que libera el número; la eliminada responde 404 a toda acción), 403 sin orders.cancel, confirmNow con solo orders.create"

step "órdenes (Lote 3): buscar o escanear (exacto) y listado paginado"
expect 200 "$(req GET "/api/v1/orders/lookup?code=$O1_NUM")" | jq -e '.matchedBy=="ORDER_NUMBER" and (.matches | length)==1' >/dev/null || fail "lookup por orden"
expect 200 "$(req GET "/api/v1/orders/lookup?code=$O1_PB")" | jq -e '.matchedBy=="PACK_BATCH" and (.matches | length)==1' >/dev/null || fail "lookup por empaque"
expect 200 "$(req GET "/api/v1/orders/lookup?code=DUP-$TS")" | jq -e '.matchedBy=="INVOICE" and (.matches | length) >= 3' >/dev/null || fail "lookup por factura"
expect 200 "$(req GET "/api/v1/orders/lookup?code=DUP")" | jq -e '(.matches | length)==0 and .matchedBy==null' >/dev/null || fail "lookup parcial no cuenta"
expect 200 "$(req GET "/api/v1/orders/lookup?code=$(echo "$O1_NUM" | tr 'A-Z' 'a-z')")" | jq -e '(.matches | length)==1' >/dev/null || fail "lookup sin distinguir mayúsculas"
expect 400 "$(req GET "/api/v1/orders/lookup?code=")" | jq -e '.errors.code' >/dev/null || fail "lookup vacío"
expect 200 "$(req GET "/api/v1/orders?invoice=UP-$TS")" | jq -e '.total >= 3 and all(.items[]; .clientInvoiceNumber | contains("UP-"))' >/dev/null || fail "filtro factura parcial"
expect 200 "$(req GET "/api/v1/orders?packBatch=EMP-")" | jq -e '.total >= 10' >/dev/null || fail "filtro empaque"
expect 200 "$(req GET "/api/v1/orders?consignee=ignatario%20B%20$TS")" | jq -e --arg c "Consignatario B $TS" '.total >= 1 and all(.items[]; .consigneeName | contains($c))' >/dev/null || fail "filtro consignatario parcial"
expect 200 "$(req GET "/api/v1/orders?search=UP-$TS")" | jq -e '.total >= 3' >/dev/null || fail "search por factura"
expect 200 "$(req GET "/api/v1/orders?search=${O1_PB:1}")" | jq -e --arg pb "$O1_PB" 'any(.items[]; .packBatchNumber==$pb)' >/dev/null || fail "search por empaque"
expect 200 "$(req GET "/api/v1/orders?search=ANUAL-$TS")" | jq -e '.total >= 1' >/dev/null || fail "search por número de orden"
expect 200 "$(req GET "/api/v1/orders?status=CONFIRMED&clientId=$CLIENT_O_PID")" | jq -e '.total >= 1 and all(.items[]; .status=="CONFIRMED")' >/dev/null || fail "filtro estatus"
expect 400 "$(req GET "/api/v1/orders?status=NOPE")" | jq -e '.errors.status' >/dev/null || fail "estatus desconocido"
expect 200 "$(req GET "/api/v1/orders?clientId=$CLIENT_O_PID&take=2")" | jq -e '(.items | length)==2 and .total >= 12' >/dev/null || fail "paginación"
expect 200 "$(req GET "/api/v1/orders?take=1000")" | jq -e '.total as $t | (.items | length) == ([$t, 500] | min)' >/dev/null || fail "take acotado a 500 (sin cortar por debajo)"
expect 200 "$(req GET "/api/v1/orders?take=0")" | jq -e '.total as $t | (.items | length) == ([$t, 100] | min)' >/dev/null || fail "take=0 usa el default 100"
expect 200 "$(req GET "/api/v1/orders?clientId=$CLIENT_O_PID&from=$TODAY&to=$(date -u -d tomorrow +%F)")" | jq -e '.total >= 1 and all(.items[]; (.packBatchNumber | length) > 0 and (.clientInvoiceNumber | length) > 0)' >/dev/null || fail "rango de fechas / columnas siempre pobladas"
ok "lookup exacto por orden/empaque/factura (CI, sin parciales), filtros y buscador parciales (factura, consignatario, empaque, número de orden), estatus validado, paginación y rango"

step "órdenes (Lote 3): entrega especial"
SP=$(expect 200 "$(req POST /api/v1/orders "$(ob "{\"packages\":null,\"isSpecialDelivery\":true,\"specialServiceId\":$SS_O_ID}")")")
SP_PID=$(echo "$SP" | jq -r .publicId)
echo "$SP" | jq -e --arg n "Vagón $TS" '.isSpecialDelivery and .specialServiceName==$n and .packages[0].description==$n and .packages[0].packageType==null and .packagesSummary==$n and (.clientInvoiceNumber | length) > 0 and (.packBatchNumber | length) > 0' >/dev/null || fail "entrega especial: $SP"
expect 200 "$(req GET "/api/v1/orders/$SP_PID/quote")" | jq -e '.total==150 and .isSpecialDelivery' >/dev/null || fail "cotización especial"
expect 200 "$(req POST "/api/v1/orders/$SP_PID/confirm" '{}')" | jq -e '.quotedAmount==150' >/dev/null || fail "confirmar especial"
expect 400 "$(req POST /api/v1/orders "$(ob "{\"isSpecialDelivery\":true,\"specialServiceId\":$SS_O_ID}")")" | jq -e '.errors.packages[0] | contains("entrega especial")' >/dev/null || fail "especial con paquetes"
expect 400 "$(req POST /api/v1/orders "$(ob "{\"packages\":null,\"isSpecialDelivery\":true,\"specialServiceId\":$SS_O_ID,\"codAmount\":50}")")" >/dev/null
expect 400 "$(req POST /api/v1/orders "$(ob '{"packages":null,"isSpecialDelivery":true}')")" | jq -e '.errors.specialServiceId' >/dev/null || fail "especial sin servicio"
expect 400 "$(req POST /api/v1/orders "$(ob "{\"packages\":null,\"isSpecialDelivery\":true,\"specialServiceId\":$SS_ID3}")")" | jq -e '.title | contains("no está vigente para este cliente")' >/dev/null || fail "servicio especial de otro cliente"
# Orden especial DRAFT con el servicio aún vigente: sirve para probar el componente apagado y el servicio cerrado después
SP2_PID=$(expect 200 "$(req POST /api/v1/orders "$(ob "{\"packages\":null,\"isSpecialDelivery\":true,\"specialServiceId\":$SS_O_ID}")")" | jq -r .publicId)
expect 400 "$(req PATCH "/api/v1/orders/$SP2_PID" '{"packages":[{"pieces":1}]}')" | jq -e '.errors.packages | tostring | contains("entrega especial")' >/dev/null || fail "PATCH especial con paquetes"
expect 400 "$(req PATCH "/api/v1/orders/$SP2_PID" '{"codAmount":50}')" | jq -e '.errors.codAmount' >/dev/null || fail "PATCH especial con COD"
# Componente 5 'Servicios especiales' apagado en el contrato (L221/L223/L229): ni se captura ni se cotiza; 409
SSOFF="El componente 'Servicios especiales' está apagado en el contrato vigente del cliente; enciéndalo en el modelo de facturación o capture una orden normal."
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_O_PID/billing-model" '{"billSpecialServices":false}')" >/dev/null
expect 409 "$(req POST /api/v1/orders "$(ob "{\"packages\":null,\"isSpecialDelivery\":true,\"specialServiceId\":$SS_O_ID}")")" | jq -e --arg m "$SSOFF" '.title==$m' >/dev/null || fail "especial con el componente apagado"
expect 409 "$(req GET "/api/v1/orders/$SP2_PID/quote")" | jq -e --arg m "$SSOFF" '.title==$m' >/dev/null || fail "quote especial con el componente apagado"
expect 409 "$(req POST "/api/v1/orders/$SP2_PID/confirm" '{}')" | jq -e --arg m "$SSOFF" '.title==$m' >/dev/null || fail "confirmar especial con el componente apagado"
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_O_PID/billing-model" '{"billSpecialServices":true}')" >/dev/null
expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/special-services/$SS_O_ID/close" '{}')" >/dev/null
expect 400 "$(req POST /api/v1/orders "$(ob "{\"packages\":null,\"isSpecialDelivery\":true,\"specialServiceId\":$SS_O_ID}")")" >/dev/null
# Servicio cerrado con la orden ya capturada (DECISIÓN 9/28): 409 igual en la vista previa y en la confirmación
SSC_MSG="El servicio especial ya no está vigente; elija otro antes de confirmar."
expect 409 "$(req GET "/api/v1/orders/$SP2_PID/quote")" | jq -e --arg m "$SSC_MSG" '.title==$m' >/dev/null || fail "quote especial con servicio cerrado"
expect 409 "$(req POST "/api/v1/orders/$SP2_PID/confirm" '{}')" | jq -e --arg m "$SSC_MSG" '.title==$m' >/dev/null || fail "confirmar especial con servicio cerrado"
expect 200 "$(req GET "/api/v1/orders/$SP2_PID")" | jq -e '.status=="DRAFT" and .quotedAmount==null' >/dev/null || fail "la orden especial sigue en DRAFT sin cotizar"
ok "entrega especial con el servicio del cliente (una línea, sin paquetes ni COD), cotiza 150, vigencia por cliente, 409 con el componente apagado y 409 en quote/confirm si el servicio se cerró"

step "órdenes (Lote 3): aislamiento entre tenants"
expect 404 "$(req GET "/api/v1/orders/$O1_PID" '' "$T3")" >/dev/null
expect 404 "$(req GET "/api/v1/orders/$O1_PID/quote" '' "$T3")" >/dev/null
expect 404 "$(req POST /api/v1/orders "$(ob)" "$T3")" | jq -e '.title | contains("Cliente")' >/dev/null || fail "cliente ajeno"
expect 200 "$(req GET /api/v1/orders '' "$T3")" | jq -e '.total==0' >/dev/null || fail "órdenes de otro tenant visibles"
expect 404 "$(req POST "/api/v1/contacts/TRANSPORT_ORDER/$O1_ID" '{"contactType":"PHONE","value":"787-555-0100"}' "$T3")" >/dev/null
expect 404 "$(req PUT "/api/v1/custom-fields/values/TRANSPORT_ORDER/$O1_ID" '{"values":{}}' "$T3")" >/dev/null
expect 200 "$(req GET "/api/v1/orders/lookup?code=$O1_NUM" '' "$T3")" | jq -e '(.matches | length)==0' >/dev/null || fail "lookup de otro tenant"
expect 404 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT_T3_PID\",\"consigneeLocationPublicId\":\"$LOC_A_PID\",\"serviceType\":\"STANDARD\",\"packages\":[{\"packageType\":\"BOX\",\"pieces\":1}]}" "$T3")" | jq -e '.title | contains("Consignatario")' >/dev/null || fail "consignatario ajeno"
# T3 no tiene tipo de servicio ni de paquete por defecto (L245): la captura los exige explícitos
expect 400 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT_T3_PID\",\"consigneeLocationPublicId\":\"$LOC_A_PID\",\"packages\":[{\"pieces\":1}]}" "$T3")" | jq -e '.errors.serviceType' >/dev/null || fail "tipo de servicio obligatorio sin default del tenant"
expect 400 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT_T3_PID\",\"serviceType\":\"STANDARD\",\"newConsignee\":{\"name\":\"Sin default $TS\",\"line1\":\"Calle 1\",\"city\":\"Ponce\"},\"packages\":[{\"pieces\":1}]}" "$T3")" | jq -e '.errors["packages[0].packageType"]' >/dev/null || fail "tipo de paquete obligatorio sin default del tenant"
ok "otro tenant: 404 por PublicId, 404 por consignatario ajeno, listas y lookup vacíos, sin oráculo; sin defaults del tenant, servicio y paquete obligatorios"

step "órdenes (Lote 3): RBAC (orders.*) y rutas polimórficas de TRANSPORT_ORDER"
expect 200 "$(req GET /api/v1/orders '' "$T4")" >/dev/null
expect 200 "$(req GET "/api/v1/orders/$O1_PID" '' "$T4")" >/dev/null
expect 200 "$(req GET "/api/v1/orders/$O1_PID/quote" '' "$T4")" >/dev/null
expect 403 "$(req POST /api/v1/orders "$(ob)" "$T4")" >/dev/null
expect 403 "$(req PATCH "/api/v1/orders/$O1_PID" '{"notes":"x"}' "$T4")" >/dev/null
expect 403 "$(req PUT "/api/v1/custom-fields/values/TRANSPORT_ORDER/$O1_ID" '{"values":{}}' "$T4")" | jq -e '.title=="Falta el permiso '"'"'orders.edit'"'"'."' >/dev/null || fail "campos personalizados de la orden sin orders.edit"
expect 403 "$(req POST "/api/v1/contacts/TRANSPORT_ORDER/$O1_ID" '{"contactType":"PHONE","value":"787-555-0100"}' "$T4")" >/dev/null
expect 200 "$(req GET "/api/v1/contacts/TRANSPORT_ORDER/$O1_ID" '' "$T4")" >/dev/null
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$O1_ID" '' "$T4")" >/dev/null
expect 200 "$(req GET "/api/v1/status/history/ORDER_COD/$OQ_ID" '' "$T4")" >/dev/null
OCP_ID=$(expect 200 "$(req POST "/api/v1/contacts/TRANSPORT_ORDER/$O1_ID" '{"contactType":"PHONE","value":"787-555-0100"}')" | jq -r .id)
expect 200 "$(req GET "/api/v1/contacts/TRANSPORT_ORDER/$O1_ID")" | jq -e 'length >= 1' >/dev/null || fail "contacto de la orden"
# contacts.manage + orders.view sin orders.edit: agregar, editar o desactivar contactos de la orden → 403 orders.edit
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Contactos $TS\",\"permissions\":[\"contacts.manage\",\"orders.view\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"contactos$TS@teikem.local\",\"fullName\":\"Contactos\",\"password\":\"$PASS\",\"roles\":[\"Contactos $TS\"]}")" >/dev/null
T7=$(login "contactos$TS@teikem.local" "$PASS")
OEDIT="Falta el permiso 'orders.edit'."
expect 403 "$(req POST "/api/v1/contacts/TRANSPORT_ORDER/$O1_ID" '{"contactType":"PHONE","value":"787-555-0100"}' "$T7")" | jq -e --arg m "$OEDIT" '.title==$m' >/dev/null || fail "contacto de la orden sin orders.edit"
expect 403 "$(req POST "/api/v1/contacts/TRANSPORT_ORDER/999999" '{"contactType":"PHONE","value":"787-555-0100"}' "$T7")" | jq -e --arg m "$OEDIT" '.title==$m' >/dev/null || fail "sin oráculo 200/404 para quien no tiene orders.edit"
expect 403 "$(req PUT "/api/v1/contacts/$OCP_ID" '{"contactType":"PHONE","value":"787-555-0101"}' "$T7")" | jq -e --arg m "$OEDIT" '.title==$m' >/dev/null || fail "editar contacto de la orden sin orders.edit"
expect 403 "$(req DELETE "/api/v1/contacts/$OCP_ID" '' "$T7")" | jq -e --arg m "$OEDIT" '.title==$m' >/dev/null || fail "desactivar contacto de la orden sin orders.edit"
expect 200 "$(req GET "/api/v1/contacts/TRANSPORT_ORDER/$O1_ID" '' "$T7")" | jq -e --argjson i "$OCP_ID" 'any(.[]; .id==$i and .value=="787-555-0100")' >/dev/null || fail "el contacto de la orden no debía cambiar"
# Dueños polimórficos del lote con resolver: un id inexistente de parada/COD/lote/plantilla → 404, nunca 200
for E in ORDER_STOP ORDER_COD IMPORT_BATCH IMPORT_TEMPLATE; do
  expect 404 "$(req PUT "/api/v1/custom-fields/values/$E/999999" '{"values":{}}')" >/dev/null
done
# Sin orders.view no se lee el historial de ORDER_COD / ORDER_STOP / IMPORT_BATCH (permiso de la entidad dueña)
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Sin órdenes $TS\",\"permissions\":[\"clients.read\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"sinordenes$TS@teikem.local\",\"fullName\":\"Sin Órdenes\",\"password\":\"$PASS\",\"roles\":[\"Sin órdenes $TS\"]}")" >/dev/null
T5=$(login "sinordenes$TS@teikem.local" "$PASS")
expect 403 "$(req GET "/api/v1/status/history/ORDER_COD/$OQ_ID" '' "$T5")" | jq -e '.title=="Falta el permiso '"'"'orders.view'"'"'."' >/dev/null || fail "historial ORDER_COD sin orders.view"
expect 403 "$(req GET "/api/v1/status/history/ORDER_STOP/1" '' "$T5")" >/dev/null
expect 403 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$O1_ID" '' "$T5")" >/dev/null
OT2=$(expect 200 "$(req POST /api/v1/orders "$(ob)" "$T2")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/orders/$OT2/cancel" '{}' "$T2")" >/dev/null
expect 204 "$(req DELETE "/api/v1/orders/$(expect 200 "$(req POST /api/v1/orders "$(ob)" "$T2")" | jq -r .publicId)" '' "$T2")" >/dev/null
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=5')" | jq -e '.total >= 1' >/dev/null || fail "PERMISSION_DENIED"
ok "solo orders.view: lectura sí, escritura 403 (incluidos contactos y campos de la orden); contacts.manage sin orders.edit 403 en contactos de la orden; ORDER_STOP/ORDER_COD/IMPORT_* inexistentes 404; sin orders.view: historial COD/paradas 403; despachador crea, cancela y elimina"

step "órdenes (Lote 3): fuentes de datos, contenido de sistema y auditoría"
expect 200 "$(req GET /api/v1/analytics/data-sources)" | jq -e '.[] | select(.key=="TRANSPORT_ORDER") | (.relations | map(.key) | index("Client") != null and index("Consignee") != null)' >/dev/null || fail "data-source TRANSPORT_ORDER"
PV=$(expect 200 "$(req POST '/api/v1/analytics/reports/TRANSPORT_ORDER/preview?dateRangeMode=ALL' '{"name":"x","columns":["PackBatchNumber","OrderNumber","Status","Client.Name","Consignee.Name"],"secondary":["Client","Consignee"]}')")
echo "$PV" | jq -e --arg c "Órdenes $TS" '.total >= 10 and ([.rows[] | select(."Client.Name"==$c)] | length) >= 1' >/dev/null || fail "preview TRANSPORT_ORDER: $(echo "$PV" | head -c 300)"
expect 200 "$(req GET /api/v1/analytics/data-sources)" | jq -e '.[] | select(.key=="TRANSPORT_ORDER") | (.fields | map(.key) | index("PackageType") != null and index("PackageTypeCode") != null)' >/dev/null || fail "campo PackageType (tipo de paquete principal) en TRANSPORT_ORDER"
codp() { expect 200 "$(req GET /api/v1/analytics/pulse)" | jq '[.indicators[] | select(.name=="COD por cobrar")][0].value // 0'; }
COD0=$(codp)
OCOD=$(expect 200 "$(req POST /api/v1/orders "$(ob '{"codAmount":200}')")" | jq -r .publicId)
COD1=$(codp)
jq -en --argjson a "$COD0" --argjson b "$COD1" '$b - $a == 200' >/dev/null || fail "COD por cobrar suma la orden viva con COD ($COD0 → $COD1)"
expect 200 "$(req POST "/api/v1/orders/$OCOD/cancel" '{"comment":"COD cancelado"}')" >/dev/null
jq -en --argjson a "$COD0" --argjson b "$(codp)" '$b == $a' >/dev/null || fail "COD por cobrar no debe sumar órdenes canceladas"
PU=$(expect 200 "$(req GET /api/v1/analytics/pulse)")
echo "$PU" | jq -e '([.indicators[] | select(.name=="Órdenes en curso" and .value >= 1)] | length)==1 and ([.indicators[] | select(.name=="COD por cobrar")] | length)==1' >/dev/null || fail "indicadores de órdenes en Pulso: $(echo "$PU" | jq -c '[.indicators[] | {name,value}]')"
RPT_O=$(expect 200 "$(req GET /api/v1/analytics/reports)" | jq -r '[.[] | select(.name=="Órdenes" and .isSystem==true)][0].id')
[[ -n "$RPT_O" && "$RPT_O" != "null" ]] || fail "vista de sistema Órdenes no sembrada"
expect 200 "$(req POST "/api/v1/analytics/reports/$RPT_O/run" '{}')" | jq -e '.total >= 10 and (.columns | map(.key) | index("PackBatchNumber") != null)' >/dev/null || fail "run de la vista Órdenes"
CH_O=$(expect 200 "$(req GET /api/v1/analytics/charts)" | jq -r '[.[] | select(.name=="Órdenes por estatus" and .isSystem==true)][0].id')
[[ -n "$CH_O" && "$CH_O" != "null" ]] || fail "gráfico de sistema Órdenes por estatus no sembrado"
expect 200 "$(req GET "/api/v1/analytics/charts/$CH_O/data")" | jq -e '.points | length >= 2' >/dev/null || fail "datos del gráfico Órdenes por estatus"
AU=$(expect 200 "$(req GET '/api/v1/audit/changes?entityType=TRANSPORT_ORDER&take=50')")
echo "$AU" | jq -e '.total >= 5 and ([.items[] | select((.changesJson // "") | ascii_downcase | contains("rowversion"))] | length)==0' >/dev/null || fail "auditoría TRANSPORT_ORDER"
expect 200 "$(req GET '/api/v1/audit/activity?kind=changes&take=100')" | jq -e '[.items[] | select(.kind=="change" and ((.type // "") | contains("Orden")))] | length >= 1' >/dev/null || fail "actividad con TRANSPORT_ORDER"
ok "TRANSPORT_ORDER con Client/Consignee, Pulso (en curso, COD por cobrar sin canceladas), campo PackageType, vista y gráfico de sistema, AuditLog sin rowVersion"

step "cliente dado de baja (Lote 3, ajuste A): solo se consulta su historial"
BAJA="El cliente está dado de baja; solo se consulta su historial."
# Antes de la baja: un componente EXTRA_PIECE (para intentar un tramo), un usuario de portal y una orden del cliente
expect 200 "$(req PATCH "/api/v1/contracts/$CONTRACT_C2_PID/billing-model" '{"billExtraPiece":true}')" >/dev/null
EP_C2=$(expect 200 "$(req POST "/api/v1/contracts/$CONTRACT_C2_PID/rate-components" '{"kind":"EXTRA_PIECE","serviceType":"STANDARD","packageType":"BOX"}')" | jq -r .id)
PUB_ID=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/invite" "{\"email\":\"bajaok$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}")" | jq -r .user.id)
OB2=$(expect 200 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT2_PID\",\"consigneeLocationPublicId\":\"$SHARED_PID\",\"packages\":[{\"pieces\":1}]}")" | jq -r .publicId)
REVB=$(expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq .total)
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/deactivate")" >/dev/null
expect 409 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/0/resend-invite")" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "reenviar invitación con cliente de baja"
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_C2_PID/rate-components/$EP_C2/tiers" '{"fromUnit":2,"rate":1}')" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "tramo con cliente de baja"
expect 409 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/invite" "{\"email\":\"baja$TS@lasmarias.pr\",\"role\":\"CLIENT_ADMIN\"}")" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "invitar con cliente de baja"
expect 409 "$(req POST /api/v1/contracts "{\"clientPublicId\":\"$CLIENT2_PID\",\"title\":\"Nuevo\",\"startDate\":\"2026-01-01\"}")" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "contrato con cliente de baja"
expect 409 "$(req POST "/api/v1/contracts/$CONTRACT_C2_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":1}')" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "tarifa con cliente de baja"
expect 409 "$(req POST "/api/v1/clients/$CLIENT2_PID/special-services" "{\"newTypeName\":\"Baja $TS\",\"rate\":1}")" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "servicio especial con cliente de baja"
expect 409 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT2_PID\",\"consigneeLocationPublicId\":\"$SHARED_PID\",\"packages\":[{\"pieces\":1}]}")" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "orden con cliente de baja"
expect 200 "$(req GET "/api/v1/clients/$CLIENT2_PID")" >/dev/null
expect 200 "$(req GET "/api/v1/contracts?clientId=$CLIENT2_PID")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT2_PID/special-services?includeHistory=true")" >/dev/null
# Lo que sigue permitido: historial, tarifas del contrato, usuarios de portal (la baja no toca las cuentas), órdenes y cancelarlas
expect 200 "$(req GET "/api/v1/status/history/CLIENT/$CLIENT2_ID")" | jq -e 'length >= 1' >/dev/null || fail "historial del cliente de baja"
expect 200 "$(req GET "/api/v1/contracts/$CONTRACT_C2_PID")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT2_PID/portal-users")" | jq -e --argjson i "$PUB_ID" 'any(.[]; .id==$i)' >/dev/null || fail "usuarios de portal del cliente de baja"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq -e --argjson n "$REVB" '.total==$n' >/dev/null || fail "la baja del cliente no debe revocar cuentas de portal"
expect 200 "$(req GET "/api/v1/orders?clientId=$CLIENT2_PID")" | jq -e '.total >= 1' >/dev/null || fail "órdenes del cliente de baja"
expect 200 "$(req POST "/api/v1/orders/$OB2/cancel" '{}')" | jq -e '.status=="CANCELLED"' >/dev/null || fail "cancelar una orden del cliente de baja"
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/reactivate")" >/dev/null
expect 200 "$(req POST /api/v1/orders "{\"clientPublicId\":\"$CLIENT2_PID\",\"consigneeLocationPublicId\":\"$SHARED_PID\",\"packages\":[{\"pieces\":1}]}")" >/dev/null
ok "409 exacto en invitar, reenviar invitación, contrato, tarifa, tramo, servicio especial y orden; ficha, historial, contrato, portal (sin revocar cuentas) y órdenes 200; cancelar una orden 200; reactivar devuelve lo nuevo"

step "portal multi-cliente (Lote 3, ajuste B): una cuenta, una fila por cliente"
# Correo de más de 150 caracteres (PortalUser.Email NVARCHAR(150)): 400 antes de crear la cuenta, nunca 500
LONGMAIL="$(printf 'a%.0s' $(seq 1 150))@x.pr"
expect 400 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/invite" "{\"email\":\"$LONGMAIL\",\"role\":\"CLIENT_ADMIN\"}")" | jq -e '.errors.email[0]=="Máximo 150 caracteres."' >/dev/null || fail "correo de portal de más de 150 caracteres"
MC=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/invite" "{\"email\":\"portal2$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}")")
echo "$MC" | jq -e '.user.status=="ACTIVE" and .inviteToken==null and .user.clientsCount==2' >/dev/null || fail "segundo cliente de la misma cuenta: $MC"
PU2C2_ID=$(echo "$MC" | jq -r .user.id)
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=ROLE_CHANGE&take=10')" | jq -e '[.items[] | select((.detailJson // "") | contains("portal_client_added"))] | length >= 1' >/dev/null || fail "SecurityEvent portal_client_added"
expect 409 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/invite" "{\"email\":\"portal2$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}")" >/dev/null   # mismo cliente ya activo
REV0=$(expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq .total)
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$PU2_ID/remove")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT2_PID/portal-users")" | jq -e --argjson i "$PU2C2_ID" '.[] | select(.id==$i) | .status=="ACTIVE"' >/dev/null || fail "la fila del otro cliente sigue ACTIVE"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq -e --argjson n "$REV0" '.total==$n' >/dev/null || fail "la cuenta no debía desactivarse mientras tenga otro cliente activo"
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/$PU2C2_ID/remove")" >/dev/null
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq -e --argjson n "$REV0" '.total > $n' >/dev/null || fail "sin clientes activos la cuenta se desactiva (TOKEN_REVOKED)"
# Reinvitar tras quitar el último cliente: la cuenta está desactivada → INVITED con enlace (conserva su contraseña)
RI3=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/invite" "{\"email\":\"portal2$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}")")
echo "$RI3" | jq -e --argjson i "$PU2C2_ID" '.user.id==$i and .user.status=="INVITED" and (.inviteToken|length)>10 and .user.hasPassword==true' >/dev/null || fail "reinvitar tras quitar el último cliente: $RI3"
# Cuenta nueva SIN contraseña invitada desde dos clientes: ambas filas INVITED con enlace; aceptar activa las dos
N1=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/invite" "{\"email\":\"nuevo$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}")")
echo "$N1" | jq -e '.user.status=="INVITED" and (.inviteToken|length)>10 and .user.hasPassword==false' >/dev/null || fail "cuenta nueva en el cliente 1: $N1"
N2=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/invite" "{\"email\":\"nuevo$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}")")
echo "$N2" | jq -e '.user.status=="INVITED" and (.inviteToken|length)>10 and .user.hasPassword==false and .user.clientsCount==2' >/dev/null || fail "cuenta sin contraseña en el cliente 2: $N2"
N1_ID=$(echo "$N1" | jq -r .user.id); N2_ID=$(echo "$N2" | jq -r .user.id)
expect 204 "$(anon POST /api/v1/portal-users/accept-invite "{\"email\":\"nuevo$TS@lasmarias.pr\",\"token\":\"$(echo "$N2" | jq -r .inviteToken)\",\"password\":\"nueva-contrasena-larga-3\"}")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$N1_ID" '.[] | select(.id==$i) | .status=="ACTIVE" and .hasPassword==true' >/dev/null || fail "accept-invite activa también la fila del cliente 1"
expect 200 "$(req GET "/api/v1/clients/$CLIENT2_PID/portal-users")" | jq -e --argjson i "$N2_ID" '.[] | select(.id==$i) | .status=="ACTIVE"' >/dev/null || fail "accept-invite activa la fila del cliente 2"
# Suspender una fila con otra ACTIVE no toca la cuenta; suspender la última sí; reactivar la reabre (se nota al agregar otro cliente)
REV1=$(expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq .total)
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$N1_ID/suspend")" >/dev/null
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq -e --argjson n "$REV1" '.total==$n' >/dev/null || fail "suspender con otro cliente activo no desactiva la cuenta"
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$N1_ID/reactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/clients/$CLIENT_PID/portal-users")" | jq -e --argjson i "$N1_ID" '.[] | select(.id==$i) | .status=="ACTIVE"' >/dev/null || fail "reactivar la fila"
expect 204 "$(req POST "/api/v1/clients/$CLIENT_PID/portal-users/$N1_ID/suspend")" >/dev/null
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/$N2_ID/suspend")" >/dev/null
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=1')" | jq -e --argjson n "$REV1" '.total > $n' >/dev/null || fail "suspender la última fila activa desactiva la cuenta"
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/portal-users/$N2_ID/reactivate")" >/dev/null
expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/portal-users/invite" "{\"email\":\"nuevo$TS@lasmarias.pr\",\"role\":\"CLIENT_READONLY\"}")" | jq -e '.user.status=="ACTIVE" and .inviteToken==null and .user.clientsCount==3' >/dev/null || fail "reactivar una fila reabre la cuenta (el tercer cliente entra ACTIVE sin enlace)"
ok "correo > 150 → 400, invitar desde otro cliente agrega el cliente (ACTIVE sin token), quitar en un cliente no toca la cuenta, quitar el último la desactiva, reinvitar da enlace, cuenta sin contraseña en dos clientes (un accept activa ambas), suspender/reactivar por fila"

step "importador de órdenes (Lote 3, ajuste D): plantilla, validar → confirmar"
TPL=$(expect 200 "$(req POST /api/v1/import-templates "{\"name\":\"Plantilla $TS\",\"columns\":[{\"position\":1,\"field\":\"consigneeName\"},{\"position\":2,\"field\":\"line1\"},{\"position\":3,\"field\":\"city\"},{\"position\":4,\"field\":\"postalCode\"},{\"position\":5,\"field\":\"packageType\"},{\"position\":6,\"field\":\"pieces\"},{\"position\":7,\"field\":\"description\"},{\"position\":8,\"field\":\"reference\"}],\"defaults\":{\"serviceType\":\"STANDARD\"}}")")
TPL_PID=$(echo "$TPL" | jq -r .publicId)
expect 400 "$(req POST /api/v1/import-templates "{\"name\":\"Repetida $TS\",\"columns\":[{\"position\":1,\"field\":\"consigneeName\"},{\"position\":1,\"field\":\"pieces\"}]}")" >/dev/null
expect 415 "$(curl -sS -X POST "$BASE/api/v1/orders/import/validate" -H "Authorization: Bearer $TOKEN" -w '\n%{http_code}')" >/dev/null   # sin Content-Type: 415, no 500
# Un número automático ya tecleado por alguien: la fila importada salta el valor chocado (reintento dentro de la transacción de la fila)
LASTI=$(seqof "$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .orderNumber)")
expect 200 "$(req POST /api/v1/orders "$(ob "{\"orderNumber\":\"$(ordnum $((LASTI + 1)))\"}")")" >/dev/null
CSV=$(printf 'consignatario,linea1,pueblo,zip,tipo,piezas,desc,ref\nImportado %s,Calle 10,Ponce,00716,BOX,2,Cajas,R-1\nImportado %s,Calle 10,Ponce,00716,ENVELOPE,1,"Sobre, urgente",R-2\nImportado %s,Calle 10,Ponce,00716,NOPE,1,Malo,R-3\n' "$TS" "$TS" "$TS")
VB=$(jq -cn --arg t "$TPL_PID" --arg c "$CLIENT_O_PID" --arg s "$CSV" '{templatePublicId:$t,clientPublicId:$c,content:$s,fileName:"ordenes.csv"}')
# Ajuste A en la importación: lote validado con el cliente activo y confirmado tras la baja → 409; validar tras la baja → 409
CSVA=$(printf 'c,l,p,z,t,n,d,r\nBaja imp %s,Calle 14,Ponce,00716,BOX,1,A,B-1\n' "$TS")
VBA=$(jq -cn --arg t "$TPL_PID" --arg c "$CLIENT2_PID" --arg s "$CSVA" '{templatePublicId:$t,clientPublicId:$c,content:$s}')
BK=$(expect 200 "$(req POST /api/v1/orders/import/validate "$VBA")" | jq -r .batchPublicId)
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/deactivate")" >/dev/null
expect 409 "$(req POST "/api/v1/orders/import/$BK/confirm" '{}')" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "confirmar importación con cliente de baja"
expect 409 "$(req POST /api/v1/orders/import/validate "$VBA")" | jq -e --arg m "$BAJA" '.title==$m' >/dev/null || fail "validar importación con cliente de baja"
expect 200 "$(req GET "/api/v1/orders?search=Baja%20imp%20$TS")" | jq -e '.total==0' >/dev/null || fail "la importación de un cliente de baja no crea órdenes"
expect 204 "$(req POST "/api/v1/clients/$CLIENT2_PID/reactivate")" >/dev/null
# Cliente SUSPENDED (DECISIÓN 16): la importación tampoco valida
expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/status" '{"toCode":"SUSPENDED","comment":"smoke importador"}')" >/dev/null
expect 422 "$(req POST /api/v1/orders/import/validate "$VB")" | jq -e '.title=="El cliente está suspendido; no se pueden crear ni confirmar órdenes."' >/dev/null || fail "validar importación con cliente suspendido"
expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null
V=$(expect 200 "$(req POST /api/v1/orders/import/validate "$VB")")
echo "$V" | jq -e '.status=="VALIDATED" and .rowCount==3 and .validRows==2 and .rows[2].errors.packageType and .rows[0].consignee.action=="CREATE"' >/dev/null || fail "validar: $V"
B_PID=$(echo "$V" | jq -r .batchPublicId)
expect 200 "$(req GET "/api/v1/orders/import/$B_PID")" | jq -e '.validRows==2' >/dev/null || fail "GET del lote"
# Archivo por multipart (campo 'file'), límites de 5.000 filas (JSON) y 2 MB (multipart), delimitador ';' sin cabecera
FUP=$(mktemp); printf '%s' "$CSV" > "$FUP"
# Sin ";type=text/csv" en el @archivo: en curl 8.21 para Windows (mingw32) ese sufijo hace fallar la lectura del
# archivo (curl (26) Failed to open/read local data), aunque el archivo exista; el servidor no valida el Content-Type
# declarado de la parte (lee el contenido tal cual), así que el sufijo no hacía falta.
mpost() { curl -sS -X POST "$BASE/api/v1/orders/import/validate-file" -H "Authorization: Bearer $TOKEN" -F "templatePublicId=$1" -F "clientPublicId=$CLIENT_O_PID" -F "file=@$2" -w '\n%{http_code}'; }
expect 200 "$(mpost "$TPL_PID" "$FUP")" | jq -e '.rowCount==3 and .validRows==2' >/dev/null || fail "validar por multipart"
LIMMSG="El archivo supera el límite de 5.000 filas o 2 MB."
{ echo h; for i in $(seq 5001); do echo "a,b"; done; } > "$FUP"
expect 400 "$(req POST /api/v1/orders/import/validate "$(jq -cn --arg t "$TPL_PID" --arg c "$CLIENT_O_PID" --rawfile s "$FUP" '{templatePublicId:$t,clientPublicId:$c,content:$s}')")" | jq -e --arg m "$LIMMSG" '.errors.content[0]==$m' >/dev/null || fail "5.001 filas → 400"
head -c 2200000 /dev/zero | tr '\0' a > "$FUP"
expect 400 "$(mpost "$TPL_PID" "$FUP")" | jq -e --arg m "$LIMMSG" '.errors.file[0]==$m' >/dev/null || fail "archivo de más de 2 MB → 400"
TPLS_PID=$(expect 200 "$(req POST /api/v1/import-templates "{\"name\":\"Punto y coma $TS\",\"delimiter\":\";\",\"hasHeader\":false,\"columns\":[{\"position\":1,\"field\":\"consigneeName\"},{\"position\":2,\"field\":\"line1\"},{\"position\":3,\"field\":\"city\"},{\"position\":4,\"field\":\"packageType\"},{\"position\":5,\"field\":\"pieces\"}]}")" | jq -r .publicId)
printf 'PuntoComa %s;Calle 15, apto 2;Ponce;BOX;1\n' "$TS" > "$FUP"
expect 200 "$(mpost "$TPLS_PID" "$FUP")" | jq -e '.rowCount==1 and .validRows==1 and (.rows[0].values.line1 // "" | contains("apto 2"))' >/dev/null || fail "plantilla con ';' y sin cabecera"
rm -f "$FUP"
CF=$(expect 200 "$(req POST "/api/v1/orders/import/$B_PID/confirm" '{}')")
echo "$CF" | jq -e --arg a "$(ordnum $((LASTI + 2)))" --arg b "$(ordnum $((LASTI + 3)))" '.created==2 and .status=="CONFIRMED" and .rows[0].orderNumber==$a and .rows[1].orderNumber==$b' >/dev/null || fail "confirmar (con salto del número chocado): $CF"
expect 200 "$(req GET "/api/v1/orders?search=Importado%20$TS")" | jq -e '.total==2' >/dev/null || fail "órdenes importadas"
expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_O_PID&search=Importado%20$TS")" | jq -e 'length==1' >/dev/null || fail "un solo consignatario creado para las dos filas"
expect 409 "$(req POST "/api/v1/orders/import/$B_PID/confirm" '{}')" >/dev/null
expect 422 "$(req POST "/api/v1/orders/import/$B_PID/discard")" >/dev/null
BID=$(expect 200 "$(req GET "/api/v1/audit/changes?entityType=IMPORT_BATCH&take=1")" | jq -r '.items[0].entityId')
expect 403 "$(req GET "/api/v1/status/history/IMPORT_BATCH/$BID" '' "$T5")" >/dev/null
# Dos confirmaciones simultáneas del mismo lote: una crea las órdenes, la otra 409 sin crear nada (reserva atómica)
CSVP=$(printf 'c,l,p,z,t,n,d,r\nParalelo %s,Calle 11,Ponce,00716,BOX,1,A,P-1\nParalelo %s,Calle 11,Ponce,00716,BOX,1,B,P-2\n' "$TS" "$TS")
BP=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(jq -cn --arg t "$TPL_PID" --arg c "$CLIENT_O_PID" --arg s "$CSVP" '{templatePublicId:$t,clientPublicId:$c,content:$s}')")" | jq -r .batchPublicId)
TMPI=$(mktemp -d)
for i in 1 2; do req POST "/api/v1/orders/import/$BP/confirm" '{}' > "$TMPI/$i" & done
wait
CODES=$(for i in 1 2; do echo "$(tail -n1 "$TMPI/$i")"; done | sort | tr "\n" " "); rm -rf "$TMPI"
[[ "$CODES" == "200 409 " ]] || fail "confirmaciones simultáneas: $CODES"
expect 200 "$(req GET "/api/v1/orders?search=Paralelo%20$TS")" | jq -e '.total==2' >/dev/null || fail "confirmación doble creó órdenes repetidas"
# Descartar un lote VALIDATED: 200 DISCARDED con historial; confirmarlo 409; descartarlo de nuevo 422
vbody() { jq -cn --arg t "${2:-$TPL_PID}" --arg c "$CLIENT_O_PID" --arg s "$1" '{templatePublicId:$t,clientPublicId:$c,content:$s}'; }
csv2() { printf 'c,l,p,z,t,n,d,r\n%s %s,Calle 12,Ponce,00716,BOX,1,A,X-1\n%s %s,Calle 12,Ponce,00716,BOX,1,B,X-2\n' "$1" "$TS" "$1" "$TS"; }
BD=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$(csv2 Descartado)")")" | jq -r .batchPublicId)
expect 200 "$(req POST "/api/v1/orders/import/$BD/discard")" | jq -e '.status=="DISCARDED"' >/dev/null || fail "descartar lote validado"
BD_ID=$(expect 200 "$(req GET "/api/v1/audit/changes?entityType=IMPORT_BATCH&take=1")" | jq -r '.items[0].entityId')
expect 200 "$(req GET "/api/v1/status/history/IMPORT_BATCH/$BD_ID")" | jq -e 'any(.[]; .fromCode=="VALIDATED" and .toCode=="DISCARDED")' >/dev/null || fail "historial del descarte"
expect 409 "$(req POST "/api/v1/orders/import/$BD/confirm" '{}')" | jq -e '.title=="El lote fue descartado; valide el archivo de nuevo."' >/dev/null || fail "confirmar descartado"
expect 422 "$(req POST "/api/v1/orders/import/$BD/discard")" | jq -e '.title=="Solo se descarta un lote pendiente de confirmar."' >/dev/null || fail "descartar dos veces"
expect 200 "$(req GET "/api/v1/orders?search=Descartado%20$TS")" | jq -e '.total==0' >/dev/null || fail "un lote descartado no crea órdenes"
# Confirmar y descartar a la vez: gana uno y el estado queda coherente (CONFIRMED con sus órdenes o DISCARDED sin ninguna)
BR=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$(csv2 Carrera)")")" | jq -r .batchPublicId)
TMPR=$(mktemp -d)
req POST "/api/v1/orders/import/$BR/confirm" '{}' > "$TMPR/c" & req POST "/api/v1/orders/import/$BR/discard" > "$TMPR/d" &
wait
RC=$(tail -n1 "$TMPR/c"); RD=$(tail -n1 "$TMPR/d"); rm -rf "$TMPR"
BRS=$(expect 200 "$(req GET "/api/v1/orders/import/$BR")" | jq -r .status)
NR=$(expect 200 "$(req GET "/api/v1/orders?search=Carrera%20$TS")" | jq .total)
if [[ "$RC" == "200" && ( "$RD" == "409" || "$RD" == "422" ) ]]; then [[ "$BRS" == "CONFIRMED" && "$NR" == "2" ]] || fail "confirmación ganó pero el lote quedó $BRS con $NR órdenes"
elif [[ "$RD" == "200" && "$RC" == "409" ]]; then [[ "$BRS" == "DISCARDED" && "$NR" == "0" ]] || fail "descarte ganó pero el lote quedó $BRS con $NR órdenes"
else fail "confirmar/descartar a la vez: confirm $RC, discard $RD"; fi
# Validación fila a fila: consignatario por código (EXISTING), por coincidencia (EXISTING), código inexistente, R36 como aviso
# (contra órdenes y dentro del archivo), teléfono de contacto guardado como ContactPoint de la orden
expect 200 "$(req PATCH "/api/v1/locations/$LOC_A_PID" "{\"code\":\"LA-$TS\"}")" >/dev/null
TPL2_PID=$(expect 200 "$(req POST /api/v1/import-templates "{\"name\":\"Plantilla código $TS\",\"columns\":[{\"position\":1,\"field\":\"consigneeCode\"},{\"position\":2,\"field\":\"consigneeName\"},{\"position\":3,\"field\":\"line1\"},{\"position\":4,\"field\":\"city\"},{\"position\":5,\"field\":\"packageType\"},{\"position\":6,\"field\":\"pieces\"},{\"position\":7,\"field\":\"orderNumber\"},{\"position\":8,\"field\":\"clientInvoiceNumber\"},{\"position\":9,\"field\":\"contactPhone\"}]}")" | jq -r .publicId)
CSVE=$(printf 'cod,nombre,l1,pueblo,tipo,n,orden,factura,tel\nLA-%s,,,,BOX,1,,DUP-%s,787-555-0199\n,Consignatario A %s,Calle 5 #12,Ponce,BOX,1,,,\nNOEXISTE-%s,,,,BOX,1,,,\nLA-%s,,,,BOX,1,,DUP-%s,\n' "$TS" "$TS" "$TS" "$TS" "$TS" "$TS")
VE=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$CSVE" "$TPL2_PID")")")
echo "$VE" | jq -e --arg l "$LOC_A_PID" '.rowCount==4 and .validRows==3
  and .rows[0].consignee.action=="EXISTING" and .rows[0].consignee.locationPublicId==$l and (.rows[0].warnings|length)>=1
  and .rows[1].consignee.action=="EXISTING" and .rows[1].consignee.locationPublicId==$l
  and (.rows[2].errors.consigneeCode|length)>0
  and any(.rows[3].warnings[]; contains("se repite en la fila 1"))' >/dev/null || fail "validación por código/coincidencia/R36: $VE"
CE=$(expect 200 "$(req POST "/api/v1/orders/import/$(echo "$VE" | jq -r .batchPublicId)/confirm" '{"rows":[1],"confirmDuplicateInvoice":true}')")
echo "$CE" | jq -e '.created==1 and .skipped==3 and .rows[1].skipped==true' >/dev/null || fail "confirmar la fila 1: $CE"
CE_ID=$(expect 200 "$(req GET "/api/v1/orders/$(echo "$CE" | jq -r .rows[0].orderPublicId)")" | jq -r .id)
expect 200 "$(req GET "/api/v1/contacts/TRANSPORT_ORDER/$CE_ID")" | jq -e 'any(.[]; .value | test("555.?0199"))' >/dev/null || fail "teléfono de la fila como contacto de la orden"
expect 200 "$(req PATCH "/api/v1/locations/$LOC_B_PID" "{\"code\":\"LB-$TS\"}")" >/dev/null
CSVD=$(printf 'cod,nombre,l1,pueblo,tipo,n,orden,factura,tel\nLB-%s,,,,BOX,1,,DUPB-%s,\nLA-%s,,,,BOX,1,,DUP-%s,\n' "$TS" "$TS" "$TS" "$TS")
VD=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$CSVD" "$TPL2_PID")")")
echo "$VD" | jq -e '.validRows==1 and (.rows[0].errors.clientInvoiceNumber|length)>0 and (.rows[1].warnings|length)>=1' >/dev/null || fail "R36 bloqueada en la validación: $VD"
BDUP=$(echo "$VD" | jq -r .batchPublicId)
expect 400 "$(req POST "/api/v1/orders/import/$BDUP/confirm" '{"rows":[1],"confirmDuplicateInvoice":true}')" | jq -e '.errors["rows[1]"]' >/dev/null || fail "fila con R36 bloqueada elegible"
NDUP=$(expect 200 "$(req GET "/api/v1/orders?search=DUP-$TS")" | jq .total)
expect 200 "$(req POST "/api/v1/orders/import/$BDUP/confirm" '{"rows":[2]}')" | jq -e '.created==0 and .failed==1 and ((.rows[1].error // "")|length)>0' >/dev/null || fail "confirmar sin confirmDuplicateInvoice"
expect 200 "$(req GET "/api/v1/orders?search=DUP-$TS")" | jq -e --argjson n "$NDUP" '.total==$n' >/dev/null || fail "la fila con aviso R36 se creó sin confirmDuplicateInvoice"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/number-settings" '{"clientAssignsOrderNumber":false}')" >/dev/null
expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$(printf 'c,n,l,p,t,x,o,f,tel\nLA-%s,,,,BOX,1,TEC-%s,,\n' "$TS" "$TS")" "$TPL2_PID")")" | jq -e '.validRows==0 and (.rows[0].errors.orderNumber|length)>0' >/dev/null || fail "número de orden tecleado cuando lo asigna Teikem"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/number-settings" '{"clientAssignsOrderNumber":true}')" >/dev/null
# Subconjunto de filas: solo la 1 (las otras quedan omitidas); una fila con errores no se puede elegir (400)
CSVS=$(printf 'c,l,p,z,t,n,d,r\nSub %s,Calle 13,Ponce,00716,BOX,1,A,S-1\nSub %s,Calle 13,Ponce,00716,BOX,1,B,S-2\nSub %s,Calle 13,Ponce,00716,NOPE,1,C,S-3\n' "$TS" "$TS" "$TS")
BS=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$CSVS")")" | jq -r .batchPublicId)
expect 400 "$(req POST "/api/v1/orders/import/$BS/confirm" '{"rows":[3]}')" | jq -e '.errors["rows[3]"]' >/dev/null || fail "elegir una fila con errores"
expect 200 "$(req POST "/api/v1/orders/import/$BS/confirm" '{"rows":[1]}')" | jq -e '.created==1 and .skipped==2 and .rows[1].skipped==true and .rows[0].orderPublicId!=null' >/dev/null || fail "confirmar un subconjunto"
expect 200 "$(req GET "/api/v1/orders?search=Sub%20$TS")" | jq -e '.total==1' >/dev/null || fail "solo la fila elegida se creó"
# Crédito en la importación (ajuste C): overrideCredit sin confirmNow 400, sin permiso 403 (el lote sigue VALIDATED),
# confirmNow con crédito excedido: cada fila falla sola (su transacción se revierte) y el lote se cierra con failed
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":10}')" >/dev/null
BC=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$(csv2 Crédito)")")" | jq -r .batchPublicId)
expect 400 "$(req POST "/api/v1/orders/import/$BC/confirm" '{"overrideCredit":true}')" | jq -e '.errors.overrideCredit' >/dev/null || fail "overrideCredit sin confirmNow en la importación"
PD0=$(pdtotal)
expect 403 "$(req POST "/api/v1/orders/import/$BC/confirm" '{"confirmNow":true,"overrideCredit":true}' "$T2")" >/dev/null
[[ $(pdtotal) -gt $PD0 ]] || fail "PERMISSION_DENIED del override en la importación"
expect 200 "$(req GET "/api/v1/orders/import/$BC")" | jq -e '.status=="VALIDATED"' >/dev/null || fail "el 403 no debe reservar el lote"
expect 200 "$(req POST "/api/v1/orders/import/$BC/confirm" '{"confirmNow":true}')" | jq -e '.created==0 and .failed==2 and all(.rows[]; (.error // "") | contains("límite de crédito"))' >/dev/null || fail "crédito excedido por fila en la importación"
expect 200 "$(req GET "/api/v1/orders?search=Cr%C3%A9dito%20$TS")" | jq -e '.total==0' >/dev/null || fail "las filas fallidas no dejan órdenes"
# Una fila falla y la otra no: límite = saldo en curso + una orden
ROWAMT=$(expect 200 "$(req POST /api/v1/billing/contract-rate-quote "{\"clientPublicId\":\"$CLIENT_O_PID\",\"lines\":[{\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"pieces\":1}]}")" | jq .total)
PEND=$(expect 200 "$(req GET "/api/v1/orders/$O1_PID/quote")" | jq .credit.pendingBalance)
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" "{\"creditLimit\":$(jq -n --argjson a "$PEND" --argjson b "$ROWAMT" '$a + $b')}")" >/dev/null
B1=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$(csv2 Mitad)")")" | jq -r .batchPublicId)
expect 200 "$(req POST "/api/v1/orders/import/$B1/confirm" '{"confirmNow":true}')" | jq -e '.created==1 and .failed==1 and .rows[0].orderStatus=="CONFIRMED" and (.rows[1].error | contains("límite de crédito"))' >/dev/null || fail "una fila falla sin tumbar la otra"
expect 200 "$(req GET "/api/v1/orders?search=Mitad%20$TS")" | jq -e '.total==1 and .items[0].status=="CONFIRMED"' >/dev/null || fail "solo la fila que cupo en el crédito existe"
# Override con permiso: todas las filas se confirman sobre el límite
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":10}')" >/dev/null
BO=$(expect 200 "$(req POST /api/v1/orders/import/validate "$(vbody "$(csv2 Autorizado)")")" | jq -r .batchPublicId)
expect 200 "$(req POST "/api/v1/orders/import/$BO/confirm" '{"confirmNow":true,"overrideCredit":true}')" | jq -e '.created==2 and .failed==0 and all(.rows[]; .orderStatus=="CONFIRMED")' >/dev/null || fail "override en la importación"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":100000}')" >/dev/null
# Aislamiento del importador (otro tenant → 404 sin oráculo)
expect 404 "$(req GET "/api/v1/orders/import/$B_PID" '' "$T3")" >/dev/null
expect 404 "$(req POST "/api/v1/orders/import/$B_PID/discard" '' "$T3")" >/dev/null
expect 404 "$(req GET "/api/v1/import-templates/$TPL_PID" '' "$T3")" >/dev/null
expect 404 "$(req PATCH "/api/v1/import-templates/$TPL_PID" '{"name":"x"}' "$T3")" >/dev/null
expect 200 "$(req GET /api/v1/import-templates '' "$T3")" | jq -e --arg p "$TPL_PID" '[.[] | select(.publicId==$p)] | length==0' >/dev/null || fail "plantilla de otro tenant visible"
expect 404 "$(req POST /api/v1/orders/import/validate "$(jq -cn --arg t "$TPL_PID" --arg c "$CLIENT_T3_PID" --arg s "$CSV" '{templatePublicId:$t,clientPublicId:$c,content:$s}')" "$T3")" >/dev/null
# RBAC del importador (T4 = solo orders.view)
expect 200 "$(req GET "/api/v1/orders/import/$B_PID" '' "$T4")" >/dev/null
expect 403 "$(req POST /api/v1/orders/import/validate "$VB" "$T4")" >/dev/null
expect 403 "$(req POST "/api/v1/orders/import/$B_PID/confirm" '{}' "$T4")" >/dev/null
expect 403 "$(req GET /api/v1/import-templates '' "$T4")" >/dev/null
expect 403 "$(req POST /api/v1/import-templates "{\"name\":\"T4 $TS\",\"columns\":[{\"position\":1,\"field\":\"consigneeName\"},{\"position\":2,\"field\":\"pieces\"}]}" "$T4")" >/dev/null
# Plantilla de otro cliente del mismo tenant → 404
TPL_C=$(expect 200 "$(req POST /api/v1/import-templates "{\"name\":\"Solo C1 $TS\",\"clientPublicId\":\"$CLIENT_PID\",\"columns\":[{\"position\":1,\"field\":\"consigneeName\"},{\"position\":2,\"field\":\"line1\"},{\"position\":3,\"field\":\"city\"},{\"position\":4,\"field\":\"postalCode\"},{\"position\":5,\"field\":\"packageType\"},{\"position\":6,\"field\":\"pieces\"}]}")" | jq -r .publicId)
expect 404 "$(req POST /api/v1/orders/import/validate "$(jq -cn --arg t "$TPL_C" --arg c "$CLIENT_O_PID" --arg s "$CSV" '{templatePublicId:$t,clientPublicId:$c,content:$s}')")" >/dev/null
# CRUD de plantillas: lista, ficha, nombre repetido 409, PATCH revalida columnas, baja/reactivación dobles 409, inactiva no valida
expect 200 "$(req GET /api/v1/import-templates)" | jq -e --arg p "$TPL_PID" 'any(.[]; .publicId==$p)' >/dev/null || fail "lista de plantillas"
expect 200 "$(req GET "/api/v1/import-templates/$TPL_PID")" | jq -e '.isActive==true' >/dev/null || fail "GET plantilla"
expect 409 "$(req POST /api/v1/import-templates "{\"name\":\"Plantilla $TS\",\"columns\":[{\"position\":1,\"field\":\"consigneeName\"},{\"position\":2,\"field\":\"pieces\"}]}")" >/dev/null
expect 400 "$(req PATCH "/api/v1/import-templates/$TPL_PID" '{"columns":[{"position":1,"field":"consigneeName"},{"position":1,"field":"pieces"}]}')" >/dev/null
expect 200 "$(req POST "/api/v1/import-templates/$TPL_PID/deactivate")" | jq -e '.isActive==false' >/dev/null || fail "desactivar plantilla"
expect 409 "$(req POST "/api/v1/import-templates/$TPL_PID/deactivate")" >/dev/null
expect 400 "$(req POST /api/v1/orders/import/validate "$VB")" | jq -e '.title | contains("inactiva")' >/dev/null || fail "validar con plantilla inactiva"
expect 200 "$(req POST "/api/v1/import-templates/$TPL_PID/reactivate")" | jq -e '.isActive==true' >/dev/null || fail "reactivar plantilla"
expect 409 "$(req POST "/api/v1/import-templates/$TPL_PID/reactivate")" >/dev/null
ok "plantilla por posición (repetida 400), 415 sin Content-Type, validar sin guardar órdenes, confirmar crea las válidas (salta el número tecleado), 409 al reconfirmar (también en paralelo), descartar (200/409/422 y en carrera con confirmar), consignatario por código/coincidencia, R36 como aviso (bloqueada no elegible, sin confirmDuplicateInvoice falla la fila), baja/suspensión del cliente (409/422), multipart, 5.000 filas/2 MB (400), delimitador y cabecera de la plantilla, teléfono como contacto, número tecleado indebido, subconjunto de filas, crédito por fila (400/403/422 por fila/override), aislamiento y RBAC del importador, CRUD de plantillas"

# ============================================================================================================
# Lote 4 — Flota, choferes y mantenimiento. Re-ejecutable: TS en códigos de vehículos, choferes y zonas; las fechas se
# calculan desde TODAY (UTC). Reutiliza del Lote 3: T2 (despachador), T3 (otro tenant, recién creado en cada corrida:
# ahí se prueban niveles de intento y fórmula sin arrastrar estado), T7 (contacts.manage sin fleet.*), CLIENT_O_PID +
# LOC_A_PID + ob(), el tipo 'Vagón $TS' (SS_O_ID quedó cerrado en el Lote 3: se crea un servicio nuevo de ese tipo) y pdtotal().
# ============================================================================================================
HASM='([.title] + [(.errors // {})[][]]) | index($m) != null'   # el mensaje llega como título o como error del campo
dplus() { date -u -d "$1 days" +%F; }                    # fecha relativa a hoy (UTC)
mago() { date -u -d "$1 minutes ago" +%FT%T; }          # instante UTC de hace N minutos
T2=$(login "$DISPATCH_EMAIL" "$PASS"); T3=$(login "admin$TS@smoke.local" "Smoke_Admin_2026!"); T7=$(login "contactos$TS@teikem.local" "$PASS")
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken)

step "flota (Lote 4): permisos, catálogos, pipelines y capacidades"
expect 200 "$(req GET /api/v1/me)" | jq -e '.permissions as $p | ["fleet.view","fleet.manage","fleet.maintenance","driverpay.view","driverpay.manage"] | all(.[]; . as $x | ($p | index($x)) != null)' >/dev/null || fail "admin sin permisos del Lote 4"
expect 200 "$(req GET /api/v1/me '' "$T2")" | jq -e '(.permissions | index("fleet.view")) != null and (.permissions | index("driverpay.view")) == null and (.permissions | index("fleet.manage")) == null' >/dev/null || fail "Despachador: fleet.view sí, driverpay.view/fleet.manage no"
TBILL=$(login "facturacion$TS@teikem.local" "$PASS")
expect 200 "$(req GET /api/v1/me '' "$TBILL")" | jq -e '(.permissions | index("driverpay.view")) != null and (.permissions | index("driverpay.manage")) == null and (.permissions | index("fleet.view")) == null' >/dev/null || fail "Facturación: driverpay.view sí, driverpay.manage/fleet.view no"
expect 200 "$(req POST /api/v1/users "{\"email\":\"lectura$TS@teikem.local\",\"fullName\":\"Solo lectura $TS\",\"password\":\"$PASS\",\"roles\":[\"ReadOnly\"]}")" >/dev/null
expect 200 "$(req GET /api/v1/me '' "$(login "lectura$TS@teikem.local" "$PASS")")" | jq -e '(.permissions | index("fleet.view")) != null and (.permissions | index("driverpay.view")) == null' >/dev/null || fail "Solo lectura con fleet.view"
for D in VehicleType Ownership FuelType VehicleDocType LicenseClass CertificationType MaintenanceTrigger MaintenanceType DriverPayoutFormula; do
  expect 200 "$(req GET "/api/v1/catalogs/$D")" | jq -e 'length >= 2' >/dev/null || fail "catálogo $D vacío"
done
for D in VehicleStatus DriverStatus WorkOrderStatus DriverTripStatus; do expect 200 "$(req GET "/api/v1/status/$D")" >/dev/null; done
expect 200 "$(req GET /api/v1/status/DriverTripStatus)" | jq -e '(.[] | select(.code=="OPEN") | .isInitial) and ([.[] | select(.code=="SETTLED" or .code=="CANCELLED") | .stageKind] == ["TERMINAL","TERMINAL"])' >/dev/null || fail "pipeline DriverTripStatus"
expect 200 "$(req GET /api/v1/status/capabilities/WORK_ORDER)" | jq -e '([.[] | select(.capability=="EDIT_WORK_ORDER" and .isAllowed==false) | .statusCode] | sort) == ["CANCELLED","CLOSED"]' >/dev/null || fail "EDIT_WORK_ORDER denegado en CLOSED y CANCELLED"
ok "permisos (admin, Despachador, Facturación, Solo lectura), 9 catálogos, 4 pipelines (DriverTripStatus OPEN→SETTLED|CANCELLED) y capacidad EDIT_WORK_ORDER"

step "vehículos (Lote 4): alta, código único, qbox, código fijo y precisión"
vb() { local x=${2:-}; [[ -n "$x" ]] || x='{}'
  jq -cn --arg c "$1" --argjson x "$x" '{code:$c,vehicleType:"VAN",ownership:"OWNED",fuelType:"DIESEL"} + $x'; }
VIN="1FTBW3XM5HKA$TS"
VH1=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "V$TS" "{\"vin\":\"$VIN\",\"plateNumber\":\"AB-$TS\",\"make\":\"Ford\",\"model\":\"Transit\",\"modelYear\":2024,\"currentOdometerKm\":1000,\"maxWeightKg\":1500,\"maxVolumeM3\":12.5,\"maxStops\":40}")")")
echo "$VH1" | jq -e --arg v "$VIN" '.statusCode=="ACTIVE" and .isActive and .vehicleTypeCode=="VAN" and .vin==$v and .currentOdometerKm==1000' >/dev/null || fail "alta de vehículo: $VH1"
VH1_PID=$(echo "$VH1" | jq -r .publicId); VH1_ID=$(echo "$VH1" | jq -r .id)
expect 409 "$(req POST /api/v1/vehicles "$(vb "v$TS")")" | jq -e '.title=="Ya existe un vehículo con ese código."' >/dev/null || fail "código de vehículo repetido (sin distinguir mayúsculas)"
expect 400 "$(req POST /api/v1/vehicles "$(vb "VX$TS" '{"vehicleType":"FOO"}')")" | jq -e '.errors.vehicleType' >/dev/null || fail "tipo de vehículo desconocido"
expect 400 "$(req POST /api/v1/vehicles "$(vb "VX$TS" '{"maxStops":0}')")" | jq -e '.errors.maxStops' >/dev/null || fail "tope de paradas 0"
expect 400 "$(req POST /api/v1/vehicles "$(vb "VX$TS" '{"maxWeightKg":1.23456}')")" | jq -e '.errors.maxWeightKg[0] | startswith("El valor admite como máximo 3 decimales")' >/dev/null || fail "precisión de maxWeightKg"
expect 200 "$(req PATCH "/api/v1/vehicles/$VH1_PID" "{\"plateNumber\":\"CD-$TS\"}")" | jq -e --arg p "CD-$TS" '.plateNumber==$p' >/dev/null || fail "PATCH placa"
expect 400 "$(req PATCH "/api/v1/vehicles/$VH1_PID" '{"code":"OTRO"}')" | jq -e --arg m "El código del vehículo se fija al crearlo; no se puede cambiar." "$HASM" >/dev/null || fail "código de vehículo fijo"
# Hallazgo de revisión: el almacén base también es fijo en el PATCH (clave en Extra)
expect 400 "$(req PATCH "/api/v1/vehicles/$VH1_PID" '{"homeWarehouseId":1}')" | jq -e --arg m "El código del vehículo se fija al crearlo; no se puede cambiar." "$HASM" >/dev/null || fail "almacén base del vehículo fijo"
# Hallazgo de revisión: concurrencia optimista (rowVersion obsoleto → 409)
expect 409 "$(req PATCH "/api/v1/vehicles/$VH1_PID" '{"make":"x","rowVersion":"AAAAAAAAAAA="}')" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "rowVersion obsoleto en el vehículo"
expect 200 "$(req GET "/api/v1/vehicles?search=$(echo "${VIN:6:10}" | tr 'A-Z' 'a-z')")" | jq -e --arg p "$VH1_PID" 'any(.[]; .publicId==$p)' >/dev/null || fail "qbox por parte del VIN en minúsculas"
expect 200 "$(req GET "/api/v1/vehicles?search=diesel")" | jq -e --arg p "$VH1_PID" 'any(.[]; .publicId==$p)' >/dev/null || fail "qbox 'diesel' encuentra 'Diésel'"
# Hallazgo de revisión: VIN repetido entre activos (alta) y qbox por placa y propiedad
expect 409 "$(req POST /api/v1/vehicles "$(vb "VD$TS" "{\"vin\":\"$VIN\"}")")" | jq -e '.title=="Ya existe un vehículo activo con ese VIN."' >/dev/null || fail "VIN repetido entre activos (alta)"
expect 200 "$(req GET "/api/v1/vehicles?search=cd-$TS")" | jq -e --arg p "$VH1_PID" 'any(.[]; .publicId==$p)' >/dev/null || fail "qbox por placa"
expect 200 "$(req GET "/api/v1/vehicles?search=propio")" | jq -e --arg p "$VH1_PID" 'any(.[]; .publicId==$p)' >/dev/null || fail "qbox por propiedad ('propio')"
expect 200 "$(req GET "/api/v1/vehicles?search=V$TS&vehicleType=TRUCK")" | jq -e --arg p "$VH1_PID" 'all(.[]; .publicId!=$p)' >/dev/null || fail "filtro vehicleType"
expect 400 "$(req GET "/api/v1/vehicles?status=NOPE")" >/dev/null
expect 200 "$(req GET "/api/v1/status/history/VEHICLE/$VH1_ID")" | jq -e 'any(.[]; .toCode=="ACTIVE")' >/dev/null || fail "historial del vehículo"
ok "alta con estatus inicial, 409 por código repetido y por VIN repetido entre activos, 400 por catálogo/tope/precisión, placa editable, código y almacén base fijos, qbox (VIN, placa, propiedad, 'diesel') y filtros, 409 por rowVersion obsoleto"

step "vehículos (Lote 4): estatus y baja lógica"
expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/status" '{"toCode":"MAINTENANCE","comment":"smoke"}')" | jq -e '.statusCode=="MAINTENANCE"' >/dev/null || fail "ACTIVE → MAINTENANCE"
expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/status" '{"toCode":"ACTIVE"}')" | jq -e '.statusCode=="ACTIVE"' >/dev/null || fail "MAINTENANCE → ACTIVE"
expect 204 "$(req POST "/api/v1/vehicles/$VH1_PID/deactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles?search=V$TS")" | jq -e --arg p "$VH1_PID" 'all(.[]; .publicId!=$p)' >/dev/null || fail "inactivo fuera de la lista"
expect 200 "$(req GET "/api/v1/vehicles?search=V$TS&includeInactive=true")" | jq -e --arg p "$VH1_PID" 'any(.[]; .publicId==$p and .isActive==false)' >/dev/null || fail "includeInactive"
expect 204 "$(req POST "/api/v1/vehicles/$VH1_PID/reactivate")" >/dev/null
VH2_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "V2$TS")")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/vehicles/$VH2_PID/status" '{"toCode":"INACTIVE","comment":"baja definitiva"}')" | jq -e '.statusCode=="INACTIVE" and .isActive==false and .isTerminal' >/dev/null || fail "baja definitiva del vehículo"
expect 409 "$(req POST "/api/v1/vehicles/$VH2_PID/reactivate")" | jq -e '.title=="El vehículo está dado de baja definitiva; no se puede reactivar."' >/dev/null || fail "reactivar un vehículo dado de baja"
VH3=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "V3$TS" '{"vehicleType":"TRUCK","currentOdometerKm":100}')")"); VH3_PID=$(echo "$VH3" | jq -r .publicId); VH3_ID=$(echo "$VH3" | jq -r .id)
# Hallazgo de revisión: qbox por tipo; VIN repetido entre activos también al editar y al reactivar
expect 200 "$(req GET "/api/v1/vehicles?search=camion")" | jq -e --arg p "$VH3_PID" 'any(.[]; .publicId==$p)' >/dev/null || fail "qbox por tipo ('camion' encuentra 'Camión')"
expect 409 "$(req PATCH "/api/v1/vehicles/$VH3_PID" "{\"vin\":\"$VIN\"}")" | jq -e '.title=="Ya existe un vehículo activo con ese VIN."' >/dev/null || fail "VIN repetido entre activos (edición)"
expect 204 "$(req POST "/api/v1/vehicles/$VH1_PID/deactivate")" >/dev/null
VW_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VW$TS" "{\"vin\":\"$VIN\"}")")" | jq -r .publicId)
expect 409 "$(req POST "/api/v1/vehicles/$VH1_PID/reactivate")" | jq -e '.title=="Ya existe un vehículo activo con ese VIN."' >/dev/null || fail "VIN repetido entre activos (reactivación)"
expect 200 "$(req POST "/api/v1/vehicles/$VW_PID/status" '{"toCode":"INACTIVE","comment":"VIN duplicado"}')" >/dev/null
expect 204 "$(req POST "/api/v1/vehicles/$VH1_PID/reactivate")" >/dev/null
ok "MAINTENANCE lateral ida y vuelta, checkbox Activo reversible, INACTIVE terminal (isActive=false, 409 al reactivar), qbox por tipo, VIN repetido 409 al editar y al reactivar"

step "zonas y choferes (Lote 4): código fijo, zona/área, tope de paradas y usuario"
ZN=$(expect 200 "$(req POST /api/v1/dispatch-zones "{\"code\":\"z$TS\",\"name\":\"Toa Baja · Bayamón\"}")")
ZN_ID=$(echo "$ZN" | jq -r .id); echo "$ZN" | jq -e --arg c "Z$TS" '.code==$c and .isActive' >/dev/null || fail "zona: $ZN"
expect 409 "$(req POST /api/v1/dispatch-zones "{\"code\":\"Z$TS\",\"name\":\"Otra\"}")" | jq -e '.title=="Ya existe una zona de despacho con ese código."' >/dev/null || fail "zona repetida"
expect 400 "$(req PATCH "/api/v1/dispatch-zones/$ZN_ID" '{"code":"OTRA"}')" >/dev/null
DR1=$(expect 200 "$(req POST /api/v1/drivers "{\"code\":\"D1$TS\",\"fullName\":\"Ana Rivera $TS\",\"dispatchZoneId\":$ZN_ID,\"hireDate\":\"2025-01-15\"}")")
DR1_PID=$(echo "$DR1" | jq -r .publicId); DR1_ID=$(echo "$DR1" | jq -r .id)
echo "$DR1" | jq -e --arg z "Z$TS" '.statusCode=="ACTIVE" and .isActive and .zoneCode==$z and .area=="Toa Baja · Bayamón" and .maxStopsPerRoute==null and .effectiveMaxStops==30 and .user==null' >/dev/null || fail "alta de chofer (zona/área, tope del tenant 30): $DR1"
expect 409 "$(req POST /api/v1/drivers "{\"code\":\"d1$TS\",\"fullName\":\"Otra\"}")" | jq -e '.title=="Ya existe un chofer con ese código."' >/dev/null || fail "código de chofer repetido"
expect 400 "$(req POST /api/v1/drivers "{\"code\":\"DX$TS\",\"fullName\":\" \"}")" | jq -e '.errors.fullName' >/dev/null || fail "nombre del chofer obligatorio"
expect 200 "$(req PATCH "/api/v1/drivers/$DR1_PID" '{"maxStopsPerRoute":18}')" | jq -e '.maxStopsPerRoute==18 and .effectiveMaxStops==18' >/dev/null || fail "tope propio"
expect 400 "$(req PATCH "/api/v1/drivers/$DR1_PID" '{"code":"OTRO"}')" | jq -e --arg m "El código del chofer se fija al crearlo; no se puede cambiar." "$HASM" >/dev/null || fail "código del chofer fijo"
expect 400 "$(req PATCH "/api/v1/drivers/$DR1_PID" '{"employeeCode":"X"}')" | jq -e --arg m "El código del chofer se fija al crearlo; no se puede cambiar." "$HASM" >/dev/null || fail "número de empleado del chofer fijo"
expect 409 "$(req PATCH "/api/v1/drivers/$DR1_PID" '{"fullName":"x","rowVersion":"AAAAAAAAAAA="}')" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "rowVersion obsoleto en el chofer"
expect 409 "$(req POST "/api/v1/dispatch-zones/$ZN_ID/deactivate")" | jq -e '.title=="La zona tiene choferes asignados; reasígnelos antes de inactivarla."' >/dev/null || fail "zona con choferes"
expect 200 "$(req GET "/api/v1/dispatch-zones")" | jq -e --argjson z "$ZN_ID" '.[] | select(.id==$z) | .driverCount==1' >/dev/null || fail "driverCount de la zona"
# Vínculo con usuario: admin.users + usuario INTERNAL con membresía ACTIVE, único por compañía
expect 200 "$(req POST /api/v1/users "{\"email\":\"chofer$TS@teikem.local\",\"fullName\":\"Chofer $TS\",\"password\":\"$PASS\",\"roles\":[\"Driver\"]}")" >/dev/null
UCH=$(expect 200 "$(req GET /api/v1/me '' "$(login "chofer$TS@teikem.local" "$PASS")")" | jq -r .userId)
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Flota $TS\",\"permissions\":[\"fleet.view\",\"fleet.manage\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"flota$TS@teikem.local\",\"fullName\":\"Flota $TS\",\"password\":\"$PASS\",\"roles\":[\"Flota $TS\"]}")" >/dev/null
T8=$(login "flota$TS@teikem.local" "$PASS")
expect 403 "$(req PATCH "/api/v1/drivers/$DR1_PID" "{\"userId\":$UCH}" "$T8")" | jq -e '.title=="Falta el permiso '"'"'admin.users'"'"'."' >/dev/null || fail "vincular usuario sin admin.users"
expect 200 "$(req PATCH "/api/v1/drivers/$DR1_PID" "{\"userId\":$UCH}")" | jq -e --arg e "chofer$TS@teikem.local" '.user.email==$e' >/dev/null || fail "vincular usuario interno"
expect 200 "$(req GET "/api/v1/drivers?search=D1$TS")" | jq -e '.[0].hasUser==true' >/dev/null || fail "hasUser en la lista"
PORTAL_UID=$(expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PASSWORD_CHANGE&take=50')" | jq -r '[.items[] | select(((.detailJson // "") | contains("portal_invite_accepted")) and .userId != null)][0].userId')
[[ "$PORTAL_UID" =~ ^[0-9]+$ ]] || fail "no se encontró un usuario de portal"
DR2=$(expect 200 "$(req POST /api/v1/drivers "{\"code\":\"D2$TS\",\"fullName\":\"Beto Colón $TS\"}")"); DR2_PID=$(echo "$DR2" | jq -r .publicId); DR2_ID=$(echo "$DR2" | jq -r .id)
expect 400 "$(req PATCH "/api/v1/drivers/$DR2_PID" "{\"userId\":$PORTAL_UID}")" | jq -e --arg m "Solo un usuario interno se puede vincular a un chofer." "$HASM" >/dev/null || fail "vincular usuario de portal"
expect 409 "$(req PATCH "/api/v1/drivers/$DR2_PID" "{\"userId\":$UCH}")" | jq -e '.title=="El usuario ya está vinculado a otro chofer."' >/dev/null || fail "usuario ya vinculado"
expect 404 "$(req PATCH "/api/v1/drivers/$DR2_PID" '{"userId":999999}')" | jq -e '.title=="Usuario no encontrado."' >/dev/null || fail "usuario inexistente"
# Hallazgo de revisión: desvincular también exige admin.users; una membresía no ACTIVE no se vincula (al final D1 vuelve a quedar vinculado)
expect 403 "$(req PATCH "/api/v1/drivers/$DR1_PID" '{"clearUser":true}' "$T8")" | jq -e '.title=="Falta el permiso '"'"'admin.users'"'"'."' >/dev/null || fail "desvincular usuario sin admin.users"
expect 200 "$(req PATCH "/api/v1/drivers/$DR1_PID" '{"clearUser":true}')" | jq -e '.user==null' >/dev/null || fail "desvincular usuario"
expect 200 "$(req PUT "/api/v1/users/$UCH/membership" '{"status":"SUSPENDED"}')" >/dev/null
expect 409 "$(req PATCH "/api/v1/drivers/$DR1_PID" "{\"userId\":$UCH}")" | jq -e '.title=="El usuario no tiene una membresía activa en esta compañía."' >/dev/null || fail "vincular usuario con membresía suspendida"
expect 200 "$(req PUT "/api/v1/users/$UCH/membership" '{"status":"ACTIVE"}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/drivers/$DR1_PID" "{\"userId\":$UCH}")" | jq -e --arg e "chofer$TS@teikem.local" '.user.email==$e' >/dev/null || fail "volver a vincular el usuario"
# Hallazgo de revisión: reasignar y quitar la zona, inactivar una zona sin choferes, zona inactiva (400), inexistente o ajena (404)
ZN2_ID=$(expect 200 "$(req POST /api/v1/dispatch-zones "{\"code\":\"y$TS\",\"name\":\"Cataño\"}")" | jq -r .id)
ZN3_ID=$(expect 200 "$(req POST /api/v1/dispatch-zones "{\"code\":\"x$TS\",\"name\":\"Guaynabo\"}")" | jq -r .id)
expect 200 "$(req PATCH "/api/v1/drivers/$DR2_PID" "{\"dispatchZoneId\":$ZN2_ID}")" | jq -e --arg z "Y$TS" '.zoneCode==$z and .area=="Cataño"' >/dev/null || fail "asignar zona"
expect 409 "$(req POST "/api/v1/dispatch-zones/$ZN2_ID/deactivate")" >/dev/null
expect 200 "$(req PATCH "/api/v1/drivers/$DR2_PID" "{\"dispatchZoneId\":$ZN3_ID}")" | jq -e --arg z "X$TS" '.zoneCode==$z and .area=="Guaynabo"' >/dev/null || fail "reasignar zona"
expect 200 "$(req POST "/api/v1/dispatch-zones/$ZN2_ID/deactivate")" | jq -e '.isActive==false and .driverCount==0' >/dev/null || fail "inactivar la zona que quedó sin choferes"
expect 200 "$(req PATCH "/api/v1/drivers/$DR2_PID" '{"clearZone":true}')" | jq -e '.zoneCode==null and .area==null' >/dev/null || fail "quitar zona"
expect 200 "$(req GET "/api/v1/dispatch-zones")" | jq -e --argjson z "$ZN3_ID" '.[] | select(.id==$z) | .driverCount==0' >/dev/null || fail "driverCount tras quitar la zona"
expect 400 "$(req POST /api/v1/drivers "{\"code\":\"DZ$TS\",\"fullName\":\"X\",\"dispatchZoneId\":$ZN2_ID}")" | jq -e --arg m "La zona de despacho está inactiva." "$HASM" >/dev/null || fail "zona inactiva al crear"
expect 400 "$(req PATCH "/api/v1/drivers/$DR2_PID" "{\"dispatchZoneId\":$ZN2_ID}")" | jq -e --arg m "La zona de despacho está inactiva." "$HASM" >/dev/null || fail "zona inactiva al editar"
expect 404 "$(req PATCH "/api/v1/drivers/$DR2_PID" '{"dispatchZoneId":999999}')" | jq -e '.title=="Zona de despacho no encontrada."' >/dev/null || fail "zona inexistente"
ZT3_ID=$(expect 200 "$(req POST /api/v1/dispatch-zones "{\"code\":\"t$TS\",\"name\":\"Otra compañía\"}" "$T3")" | jq -r .id)
expect 404 "$(req PATCH "/api/v1/drivers/$DR2_PID" "{\"dispatchZoneId\":$ZT3_ID}")" | jq -e '.title=="Zona de despacho no encontrada."' >/dev/null || fail "zona de otro tenant"
expect 200 "$(req POST "/api/v1/dispatch-zones/$ZN2_ID/reactivate")" | jq -e '.isActive==true' >/dev/null || fail "reactivar zona"
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/status" '{"toCode":"UNAVAILABLE","comment":"permiso"}')" | jq -e '.statusCode=="UNAVAILABLE"' >/dev/null || fail "UNAVAILABLE"
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/status" '{"toCode":"ACTIVE"}')" | jq -e '.statusCode=="ACTIVE"' >/dev/null || fail "regreso a ACTIVE"
expect 204 "$(req POST "/api/v1/drivers/$DR1_PID/deactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/drivers?search=D1$TS")" | jq -e 'length==0' >/dev/null || fail "chofer inactivo fuera de la lista"
expect 204 "$(req POST "/api/v1/drivers/$DR1_PID/reactivate")" >/dev/null
DR3=$(expect 200 "$(req POST /api/v1/drivers "{\"code\":\"D3$TS\",\"fullName\":\"Carla Díaz $TS\",\"dispatchZoneId\":$ZN_ID}")"); DR3_PID=$(echo "$DR3" | jq -r .publicId); DR3_ID=$(echo "$DR3" | jq -r .id)
DR3_NAME="Carla Díaz $TS"
# Hallazgo de revisión: sin licencias, licenseExpiry no se serializa (null), nunca 0001-01-01
expect 200 "$(req GET "/api/v1/drivers?search=D2$TS")" | jq -e 'length==1 and (.[0] | has("licenseExpiry") | not)' >/dev/null || fail "licenseExpiry de un chofer sin licencias"
ok "zona (código fijo, 409 repetida, 409 con choferes), chofer con zona/área y tope efectivo 30→18, código fijo (code y employeeCode), vínculo con usuario (403 sin admin.users al vincular y al desvincular, 400 portal, 409 repetido, 409 membresía no activa, 404), zona reasignada/quitada, zona inactiva 400, inexistente o ajena 404, inactivar/reactivar zona, UNAVAILABLE, Activo reversible, licenseExpiry vacío, 409 por rowVersion obsoleto"

step "documentos (Lote 4): licencias, certificaciones, documentos de vehículo y 'Documentos por vencer'"
expect 400 "$(req POST "/api/v1/vehicles/$VH1_PID/documents" "{\"docType\":\"INSURANCE\",\"issuedDate\":\"$(dplus 5)\",\"expiryDate\":\"$(dplus 1)\"}")" | jq -e '.errors.expiryDate[0]=="La fecha de vencimiento no puede ser anterior a la de emisión."' >/dev/null || fail "emisión posterior al vencimiento"
expect 400 "$(req POST "/api/v1/vehicles/$VH1_PID/documents" '{"docType":"FOO"}')" | jq -e '.errors.docType' >/dev/null || fail "tipo de documento desconocido"
DOC_INS=$(expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/documents" "{\"docType\":\"INSURANCE\",\"docNumber\":\"SEG-$TS\",\"expiryDate\":\"$(dplus 10)\"}")" | jq -r .id)
DOC_REG=$(expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/documents" "{\"docType\":\"REGISTRATION\",\"docNumber\":\"MAR-OLD-$TS\",\"issuedDate\":\"$(dplus -366)\",\"expiryDate\":\"$(dplus -1)\"}")")
echo "$DOC_REG" | jq -e '.expiryState=="EXPIRED" and .daysToExpiry==-1' >/dev/null || fail "documento vencido ayer: $DOC_REG"; DOC_REG=$(echo "$DOC_REG" | jq -r .id)
expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/documents" "{\"docType\":\"INSPECTION\",\"docNumber\":\"INS-$TS\",\"expiryDate\":\"$(dplus 90)\"}")" | jq -e '.expiryState=="OK"' >/dev/null || fail "inspección a 90 días"
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/licenses" "{\"licenseClass\":\"CDL_A\",\"licenseNumber\":\"L1-$TS\",\"expiryDate\":\"$(dplus 5)\"}")" | jq -e '.expiryState=="EXPIRING" and .daysToExpiry==5' >/dev/null || fail "licencia de D1"
expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/licenses" "{\"licenseClass\":\"CDL_A\",\"licenseNumber\":\"L3-$TS\",\"expiryDate\":\"$(dplus 365)\"}")" >/dev/null
LIC2=$(expect 200 "$(req POST "/api/v1/drivers/$DR2_PID/licenses" "{\"licenseClass\":\"CDL_A\",\"licenseNumber\":\"L2-$TS\",\"expiryDate\":\"$(dplus -30)\"}")" | jq -r .id)
expect 200 "$(req POST "/api/v1/drivers/$DR2_PID/certifications" "{\"certType\":\"HAZMAT\",\"certNumber\":\"HZ-$TS\",\"expiryDate\":\"$(dplus -10)\"}")" | jq -e '.expiryState=="EXPIRED"' >/dev/null || fail "certificación vencida"
expect 400 "$(req POST "/api/v1/drivers/$DR2_PID/licenses" '{"licenseClass":"CDL_A","licenseNumber":""}')" | jq -e '.errors.licenseNumber' >/dev/null || fail "número de licencia obligatorio"
expect 400 "$(req POST "/api/v1/drivers/$DR2_PID/licenses" '{"licenseClass":"FOO","licenseNumber":"X"}')" | jq -e '.errors.licenseClass' >/dev/null || fail "clase de licencia desconocida"
expect 200 "$(req GET "/api/v1/drivers?search=D1$TS")" | jq -e --arg d "$(dplus 5)" '.[0].licenseExpiry==$d' >/dev/null || fail "licenseExpiry en la lista"
MINE="[\"V$TS\",\"D1$TS\",\"D2$TS\",\"D3$TS\"]"
EXP=$(expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=30')")
echo "$EXP" | jq -e --argjson m "$MINE" --arg v "V$TS" --arg d1 "D1$TS" --arg d2 "D2$TS" '[.[] | select(.ownerCode as $o | $m | index($o))] as $x
  | ($x | length)==5
  and any($x[]; .ownerCode==$v and .documentTypeCode=="INSURANCE" and .expiryState=="EXPIRING" and .daysToExpiry==10)
  and any($x[]; .ownerCode==$v and .documentTypeCode=="REGISTRATION" and .expiryState=="EXPIRED")
  and all($x[]; .documentTypeCode!="INSPECTION")
  and any($x[]; .ownerCode==$d1 and .documentKind=="LICENSE" and .expiryState=="EXPIRING")
  and any($x[]; .ownerCode==$d2 and .documentKind=="LICENSE" and .expiryState=="EXPIRED")
  and any($x[]; .ownerCode==$d2 and .documentKind=="CERTIFICATION" and .documentTypeCode=="HAZMAT")' >/dev/null || fail "documentos por vencer: $(echo "$EXP" | jq -c --argjson m "$MINE" '[.[] | select(.ownerCode as $o | $m | index($o)) | {ownerCode,documentTypeCode,expiryState}]')"
echo "$EXP" | jq -e '[.[].expiryDate] as $d | $d == ($d | sort)' >/dev/null || fail "orden por vencimiento"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?docType=LICENSE')" | jq -e 'length >= 2 and all(.[]; .documentKind=="LICENSE")' >/dev/null || fail "filtro docType=LICENSE"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?entity=VEHICLE')" | jq -e 'length >= 2 and all(.[]; .ownerKind=="VEHICLE")' >/dev/null || fail "filtro entity=VEHICLE"
# Hallazgo de revisión: filtro por tipo de documento de vehículo, filtro mixto vehículo+chofer y entity=DRIVER
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?docType=INSURANCE')" | jq -e --arg n "SEG-$TS" 'any(.[]; .docNumber==$n) and all(.[]; .ownerKind=="VEHICLE" and .documentKind=="INSURANCE")' >/dev/null || fail "filtro docType=INSURANCE"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?docType=REGISTRATION&docType=LICENSE')" | jq -e --arg v "V$TS" --arg d1 "D1$TS" 'any(.[]; .ownerCode==$v and .documentKind=="REGISTRATION") and any(.[]; .ownerCode==$d1 and .documentKind=="LICENSE") and all(.[]; .documentKind=="REGISTRATION" or .documentKind=="LICENSE")' >/dev/null || fail "filtro docType mixto vehículo+chofer"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?entity=DRIVER')" | jq -e 'length >= 3 and all(.[]; .ownerKind=="DRIVER")' >/dev/null || fail "filtro entity=DRIVER"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?includeExpired=false')" | jq -e 'all(.[]; .expiryState!="EXPIRED")' >/dev/null || fail "includeExpired=false"
expect 400 "$(req GET '/api/v1/fleet/expiring-documents?docType=FOO')" >/dev/null
expect 400 "$(req GET '/api/v1/fleet/expiring-documents?entity=FOO')" >/dev/null
expect 400 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=400')" >/dev/null
# Hallazgo de revisión: el panel no trae documentos de dueños inactivos ni dados de baja
VE_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VE$TS")")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/vehicles/$VE_PID/documents" "{\"docType\":\"PERMIT\",\"docNumber\":\"PER-E-$TS\",\"expiryDate\":\"$(dplus 7)\"}")" >/dev/null
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=30')" | jq -e --arg v "VE$TS" 'any(.[]; .ownerCode==$v)' >/dev/null || fail "documento de un vehículo activo en el panel"
expect 204 "$(req POST "/api/v1/vehicles/$VE_PID/deactivate")" >/dev/null
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=30')" | jq -e --arg v "VE$TS" 'all(.[]; .ownerCode!=$v)' >/dev/null || fail "documento de un vehículo inactivo en el panel"
expect 204 "$(req POST "/api/v1/vehicles/$VE_PID/reactivate")" >/dev/null
expect 200 "$(req POST "/api/v1/vehicles/$VE_PID/status" '{"toCode":"INACTIVE","comment":"baja"}')" >/dev/null
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=30')" | jq -e --arg v "VE$TS" 'all(.[]; .ownerCode!=$v)' >/dev/null || fail "documento de un vehículo dado de baja en el panel"
ok "fechas validadas, estados EXPIRED/EXPIRING/OK, panel unido (vehículos, licencias, certificaciones) ordenado, filtros por tipo/entidad/vencidos, 400 por valores desconocidos y sin documentos de dueños inactivos o dados de baja (docType de vehículo como INSURANCE, mixto REGISTRATION+LICENSE, entity=VEHICLE y entity=DRIVER)"

step "disponibilidad para despacho (Lote 4)"
expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/status" '{"toCode":"UNAVAILABLE"}')" >/dev/null
AV=$(expect 200 "$(req GET /api/v1/fleet/availability)")
echo "$AV" | jq -e --arg p "$DR1_PID" '.drivers[] | select(.publicId==$p) | .available and any(.issues[]; .code=="DOC_EXPIRING" and (.blocking | not))' >/dev/null || fail "D1 disponible con aviso"
echo "$AV" | jq -e --arg p "$DR2_PID" '.drivers[] | select(.publicId==$p) | (.available | not) and any(.issues[]; .code=="NO_VALID_LICENSE" and .blocking) and any(.issues[]; .code=="CERT_EXPIRED" and (.blocking | not))' >/dev/null || fail "D2 sin licencia vigente"
echo "$AV" | jq -e --arg p "$DR3_PID" '.drivers[] | select(.publicId==$p) | (.available | not) and any(.issues[]; .code=="DRIVER_STATUS" and .blocking)' >/dev/null || fail "D3 UNAVAILABLE"
echo "$AV" | jq -e --arg p "$VH1_PID" '.vehicles[] | select(.publicId==$p) | (.available | not) and any(.issues[]; .code=="VEHICLE_DOC_EXPIRED" and .blocking)' >/dev/null || fail "V1 con documento vencido"
echo "$AV" | jq -e --arg p "$VH3_PID" '.vehicles[] | select(.publicId==$p) | .available and any(.issues[]; .code=="VEHICLE_NO_DOCUMENTS" and (.blocking | not))' >/dev/null || fail "vehículo sin documentos"
expect 200 "$(req GET '/api/v1/fleet/availability?onlyAvailable=true')" | jq -e --arg p "$DR2_PID" 'all(.drivers[]; .available) and all(.drivers[]; .publicId!=$p)' >/dev/null || fail "onlyAvailable"
# Hallazgo de revisión: el checkbox Activo saca de despacho al chofer y al vehículo (DRIVER_INACTIVE / VEHICLE_INACTIVE)
expect 204 "$(req POST "/api/v1/drivers/$DR1_PID/deactivate")" >/dev/null
expect 204 "$(req POST "/api/v1/vehicles/$VH3_PID/deactivate")" >/dev/null
AVI=$(expect 200 "$(req GET /api/v1/fleet/availability)")
echo "$AVI" | jq -e --arg p "$DR1_PID" '.drivers[] | select(.publicId==$p) | (.available | not) and any(.issues[]; .code=="DRIVER_INACTIVE" and .blocking)' >/dev/null || fail "chofer inactivo (checkbox) no se ofrece en despacho"
echo "$AVI" | jq -e --arg p "$VH3_PID" '.vehicles[] | select(.publicId==$p) | (.available | not) and any(.issues[]; .code=="VEHICLE_INACTIVE" and .blocking)' >/dev/null || fail "vehículo inactivo (checkbox) no se ofrece en despacho"
expect 200 "$(req GET '/api/v1/fleet/availability?onlyAvailable=true')" | jq -e --arg d "$DR1_PID" --arg v "$VH3_PID" 'all(.drivers[]; .publicId!=$d) and all(.vehicles[]; .publicId!=$v)' >/dev/null || fail "onlyAvailable con inactivos"
expect 204 "$(req POST "/api/v1/drivers/$DR1_PID/reactivate")" >/dev/null
expect 204 "$(req POST "/api/v1/vehicles/$VH3_PID/reactivate")" >/dev/null
# Hallazgo de revisión: la disponibilidad se evalúa en la fecha pedida (a hoy+10 la licencia de D1, que vence en 5 días, ya venció)
AVD=$(dplus 10)
expect 200 "$(req GET "/api/v1/fleet/availability?date=$AVD")" | jq -e --arg p "$DR1_PID" --arg d "$AVD" '.date==$d and (.drivers[] | select(.publicId==$p) | (.available | not) and any(.issues[]; .code=="NO_VALID_LICENSE" and .blocking))' >/dev/null || fail "disponibilidad evaluada en la fecha pedida (D1 con licencia vencida a hoy+10)"
expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null
ok "D1 disponible con aviso DOC_EXPIRING, D2 bloqueado (NO_VALID_LICENSE) con aviso CERT_EXPIRED, D3 UNAVAILABLE bloqueado, V1 bloqueado por documento vencido, vehículo sin documentos con aviso; chofer y vehículo inactivos (checkbox) bloqueados (DRIVER_INACTIVE / VEHICLE_INACTIVE) y fuera de onlyAvailable; con ?date=hoy+10 D1 queda bloqueado (NO_VALID_LICENSE)"

step "documento vigente por tipo (Lote 4): la renovación supera al vencido"
expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/documents" "{\"docType\":\"REGISTRATION\",\"docNumber\":\"MAR-NEW-$TS\",\"expiryDate\":\"$(dplus 365)\"}")" >/dev/null
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=30')" | jq -e --arg v "V$TS" '[.[] | select(.ownerCode==$v)] | length==1 and .[0].documentTypeCode=="INSURANCE"' >/dev/null || fail "el REGISTRATION superado sigue en el panel"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID")" | jq -e --argjson r "$DOC_REG" '.documents[] | select(.id==$r) | .isSuperseded and .expiryState=="EXPIRED"' >/dev/null || fail "isSuperseded en la ficha"
expect 200 "$(req GET /api/v1/fleet/availability)" | jq -e --arg p "$VH1_PID" '.vehicles[] | select(.publicId==$p) | .available and ([.issues[].code] == ["DOC_EXPIRING"])' >/dev/null || fail "V1 disponible tras renovar"
expect 200 "$(req GET "/api/v1/vehicles?search=V$TS")" | jq -e --arg p "$VH1_PID" --arg d "$(dplus 10)" '.[] | select(.publicId==$p) | .nextDocumentExpiry==$d' >/dev/null || fail "nextDocumentExpiry = el seguro"
# BOLA por id hijo en el mismo tenant: documento de V1 bajo la ruta de V3
expect 404 "$(req PATCH "/api/v1/vehicles/$VH3_PID/documents/$DOC_INS" '{"docNumber":"x"}')" | jq -e '.title=="Documento no encontrado."' >/dev/null || fail "documento de otro vehículo"
expect 404 "$(req PATCH "/api/v1/drivers/$DR1_PID/licenses/$LIC2" '{"licenseNumber":"x"}')" | jq -e '.title=="Licencia no encontrada."' >/dev/null || fail "licencia de otro chofer"
# Hallazgo de revisión: dispositivos del chofer (la app del Lote 7 los registra; aquí solo se listan y se desactivan)
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/devices?includeInactive=true")" | jq -e 'length==0' >/dev/null || fail "dispositivos del chofer"
expect 404 "$(req POST "/api/v1/drivers/$DR1_PID/devices/999999/deactivate")" | jq -e '.title=="Dispositivo no encontrado."' >/dev/null || fail "dispositivo inexistente"
# Hallazgo de revisión: editar y quitar documentos, licencias y certificaciones; el quitado deja de contar
DOCX=$(expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/documents" "{\"docType\":\"PERMIT\",\"docNumber\":\"PER-$TS\",\"issuedDate\":\"$(dplus -10)\",\"expiryDate\":\"$(dplus 20)\"}")" | jq -r .id)
expect 200 "$(req PATCH "/api/v1/vehicles/$VH1_PID/documents/$DOCX" "{\"docNumber\":\"EDIT-$TS\",\"clearIssuedDate\":true}")" | jq -e --arg n "EDIT-$TS" '.docNumber==$n and .issuedDate==null and .isActive' >/dev/null || fail "editar documento (clearIssuedDate)"
expect 400 "$(req PATCH "/api/v1/vehicles/$VH1_PID/documents/$DOCX" "{\"issuedDate\":\"$(dplus 30)\"}")" | jq -e '.errors.expiryDate[0]=="La fecha de vencimiento no puede ser anterior a la de emisión."' >/dev/null || fail "fechas validadas al editar"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=30')" | jq -e --arg k "VD-$DOCX" 'any(.[]; .rowKey==$k)' >/dev/null || fail "documento editado en el panel"
expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/documents/$DOCX/deactivate")" | jq -e '.isActive==false' >/dev/null || fail "quitar documento"
expect 200 "$(req POST "/api/v1/vehicles/$VH1_PID/documents/$DOCX/deactivate")" | jq -e '.isActive==false' >/dev/null || fail "quitar documento (idempotente)"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=30')" | jq -e --arg k "VD-$DOCX" 'all(.[]; .rowKey!=$k)' >/dev/null || fail "documento quitado sigue en el panel"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID")" | jq -e --argjson d "$DOCX" 'all(.documents[]; .id!=$d)' >/dev/null || fail "documento quitado sigue en la ficha"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID/documents?includeInactive=true")" | jq -e --argjson d "$DOCX" 'any(.[]; .id==$d and .isActive==false)' >/dev/null || fail "documento quitado con includeInactive"
LICX=$(expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/licenses" "{\"licenseClass\":\"CDL_B\",\"licenseNumber\":\"LX-$TS\",\"issuedDate\":\"$(dplus -10)\",\"expiryDate\":\"$(dplus 20)\"}")" | jq -r .id)
expect 200 "$(req PATCH "/api/v1/drivers/$DR3_PID/licenses/$LICX" "{\"licenseNumber\":\"LX2-$TS\",\"clearIssuedDate\":true}")" | jq -e --arg n "LX2-$TS" '.licenseNumber==$n and .issuedDate==null' >/dev/null || fail "editar licencia"
expect 400 "$(req PATCH "/api/v1/drivers/$DR3_PID/licenses/$LICX" "{\"issuedDate\":\"$(dplus 30)\"}")" | jq -e '.errors.expiryDate' >/dev/null || fail "fechas de licencia al editar"
expect 200 "$(req GET "/api/v1/drivers?search=D3$TS")" | jq -e --arg d "$(dplus 20)" '.[0].licenseExpiry==$d' >/dev/null || fail "licenseExpiry con la licencia nueva"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?docType=LICENSE')" | jq -e --arg k "DL-$LICX" 'any(.[]; .rowKey==$k)' >/dev/null || fail "licencia en el panel"
expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/licenses/$LICX/deactivate")" | jq -e '.isActive==false' >/dev/null || fail "quitar licencia"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?docType=LICENSE')" | jq -e --arg k "DL-$LICX" 'all(.[]; .rowKey!=$k)' >/dev/null || fail "licencia quitada sigue en el panel"
expect 200 "$(req GET "/api/v1/drivers?search=D3$TS")" | jq -e --arg d "$(dplus 365)" '.[0].licenseExpiry==$d' >/dev/null || fail "licencia quitada sigue contando en licenseExpiry"
CERTX=$(expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/certifications" "{\"certType\":\"FORKLIFT\",\"certNumber\":\"FK-$TS\",\"expiryDate\":\"$(dplus 15)\"}")" | jq -r .id)
expect 200 "$(req PATCH "/api/v1/drivers/$DR1_PID/certifications/$CERTX" "{\"certNumber\":\"FK2-$TS\"}")" | jq -e --arg n "FK2-$TS" '.certNumber==$n' >/dev/null || fail "editar certificación"
expect 404 "$(req PATCH "/api/v1/drivers/$DR2_PID/certifications/$CERTX" '{"certNumber":"x"}')" | jq -e '.title=="Certificación no encontrada."' >/dev/null || fail "certificación de otro chofer"
expect 404 "$(req POST "/api/v1/drivers/$DR2_PID/certifications/$CERTX/deactivate")" >/dev/null
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?docType=CERTIFICATION')" | jq -e --arg k "DC-$CERTX" 'any(.[]; .rowKey==$k)' >/dev/null || fail "certificación en el panel"
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/certifications/$CERTX/deactivate")" | jq -e '.isActive==false' >/dev/null || fail "quitar certificación"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?docType=CERTIFICATION')" | jq -e --arg k "DC-$CERTX" 'all(.[]; .rowKey!=$k)' >/dev/null || fail "certificación quitada sigue en el panel"
ok "REGISTRATION renovado: el viejo queda isSuperseded, sale del panel, no bloquea y no cuenta para el próximo vencimiento; hijos bajo otro padre → 404; editar (clear de fechas, 400 de fechas) y quitar documento/licencia/certificación: deja de contar en panel, ficha y licenseExpiry; dispositivos: lista vacía y 404 por id ajeno; certificación bajo otro chofer → 404"

step "contactos, campos personalizados e historial de flota (Lote 4)"
expect 200 "$(req POST "/api/v1/contacts/DRIVER/$DR1_ID" '{"contactType":"PHONE","value":"787-555-0142","isPrimary":true}')" >/dev/null
expect 200 "$(req GET "/api/v1/contacts/DRIVER/$DR1_ID")" | jq -e 'any(.[]; .value | test("555.?0142"))' >/dev/null || fail "contacto del chofer"
DEFV=$(req POST /api/v1/custom-fields/definitions/VEHICLE '{"fieldKey":"color","labels":{"es":"Color","en":"Color"},"dataType":"TEXT","isRequired":false,"isUnique":false,"showInList":true}')
CODE=$(echo "$DEFV" | tail -n1); [[ "$CODE" == "200" || "$CODE" == "409" ]] || fail "definición VEHICLE: $DEFV"
expect 200 "$(req PUT "/api/v1/custom-fields/values/VEHICLE/$VH1_ID" '{"values":{"color":"Blanco"}}')" >/dev/null
expect 403 "$(req POST "/api/v1/contacts/DRIVER/$DR1_ID" '{"contactType":"PHONE","value":"787-555-0143"}' "$T7")" | jq -e '.title=="Falta el permiso '"'"'fleet.manage'"'"'."' >/dev/null || fail "contacto del chofer sin fleet.manage"
for E in VEHICLE DRIVER DISPATCH_ZONE MAINTENANCE_SCHEDULE WORK_ORDER FUEL_LOG DRIVER_TRIP DRIVER_RATE FLEET_DOCUMENT PORTAL_USER; do
  expect 404 "$(req PUT "/api/v1/custom-fields/values/$E/999999" '{"values":{}}')" >/dev/null
done
expect 404 "$(req PUT "/api/v1/custom-fields/values/DRIVER_RATE/1" '{"values":{}}')" >/dev/null   # resolver cerrado: 404 aunque el id exista
ok "ContactPoint del chofer, campo personalizado de VEHICLE, 403 sin fleet.manage, 404 para ids inexistentes en todos los dueños del lote (DRIVER_RATE/FLEET_DOCUMENT cerrados) y en PORTAL_USER"

step "mantenimiento preventivo (Lote 4): Al día / Por vencer / Vencido / Sin historial"
SCH1=$(expect 200 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"Aceite 5000 $TS\",\"vehiclePublicId\":\"$VH1_PID\",\"trigger\":\"MILEAGE\",\"intervalKm\":5000,\"lastServiceKm\":0}")" | jq -r .id)
due() { expect 200 "$(req GET "/api/v1/maintenance-schedules/due?vehiclePublicId=$1")" | jq -c --argjson s "$2" '[.[] | select(.scheduleId==$s)][0]'; }
due "$VH1_PID" "$SCH1" | jq -e '.state=="OK" and .nextDueKm==5000 and .kmRemaining==4000' >/dev/null || fail "preventivo OK: $(due "$VH1_PID" "$SCH1")"
expect 200 "$(req PATCH "/api/v1/vehicles/$VH1_PID" '{"currentOdometerKm":4600}')" >/dev/null
due "$VH1_PID" "$SCH1" | jq -e '.state=="DUE_SOON"' >/dev/null || fail "preventivo DUE_SOON"
expect 200 "$(req PATCH "/api/v1/vehicles/$VH1_PID" '{"currentOdometerKm":5200}')" >/dev/null
due "$VH1_PID" "$SCH1" | jq -e '.state=="OVERDUE" and .kmRemaining==-200' >/dev/null || fail "preventivo OVERDUE"
expect 200 "$(req GET "/api/v1/maintenance-schedules/due?vehiclePublicId=$VH1_PID&status=OVERDUE")" | jq -e --argjson s "$SCH1" 'any(.[]; .scheduleId==$s) and all(.[]; .state=="OVERDUE")' >/dev/null || fail "filtro status del panel"
SCHT=$(expect 200 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"Inspección 30 días $TS\",\"vehiclePublicId\":\"$VH3_PID\",\"trigger\":\"TIME\",\"intervalDays\":30,\"lastServiceDate\":\"$(dplus -40)\"}")" | jq -r .id)
due "$VH3_PID" "$SCHT" | jq -e '.state=="OVERDUE" and .daysRemaining==-10' >/dev/null || fail "preventivo por tiempo OVERDUE"
expect 400 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"Ambos $TS\",\"vehiclePublicId\":\"$VH1_PID\",\"vehicleType\":\"VAN\",\"trigger\":\"MILEAGE\",\"intervalKm\":5000}")" | jq -e --arg m "Indique el vehículo o el tipo de vehículo del programa, no ambos." "$HASM" >/dev/null || fail "vehículo y tipo a la vez"
expect 400 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"Sin intervalo $TS\",\"vehiclePublicId\":\"$VH1_PID\",\"trigger\":\"MILEAGE\"}")" | jq -e '.errors.intervalKm' >/dev/null || fail "MILEAGE sin intervalo"
expect 400 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"X $TS\",\"vehiclePublicId\":\"$VH1_PID\",\"trigger\":\"FOO\",\"intervalKm\":1}")" >/dev/null
# Programa por tipo (hallazgo de revisión): la línea base de cada vehículo es su última OT CERRADA del programa
VA_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VA$TS" '{"currentOdometerKm":5000}')")" | jq -r .publicId)
VB_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VB$TS" '{"currentOdometerKm":3000}')")" | jq -r .publicId)
SCHV=$(expect 200 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"Vans 5000 $TS\",\"vehicleType\":\"VAN\",\"trigger\":\"MILEAGE\",\"intervalKm\":5000}")" | jq -r .id)
due "$VA_PID" "$SCHV" | jq -e '.state=="NO_BASELINE" and .lastServiceKm==null' >/dev/null || fail "programa por tipo sin OT → NO_BASELINE"
WOV=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VA_PID\",\"scheduleId\":$SCHV}")")
WOV_PID=$(echo "$WOV" | jq -r .publicId); WOV_NUM=$(echo "$WOV" | jq -r .number)
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOV_PID/status" '{"toCode":"CLOSED","odometerKm":5200}')" | jq -e --arg t "$TODAY" '.statusCode=="CLOSED" and .completedDate==$t and .odometerKm==5200' >/dev/null || fail "cerrar OT del programa por tipo"
due "$VA_PID" "$SCHV" | jq -e --arg n "$WOV_NUM" --arg t "$TODAY" '.lastServiceKm==5200 and .lastWorkOrderNumber==$n and .lastServiceDate==$t and .state=="OK" and .currentOdometerKm==5200' >/dev/null || fail "línea base por la OT cerrada: $(due "$VA_PID" "$SCHV")"
due "$VB_PID" "$SCHV" | jq -e '.lastServiceKm==null and .lastWorkOrderNumber==null and .state=="NO_BASELINE"' >/dev/null || fail "otra van del tipo sigue sin historial"
# Hallazgo de revisión: el programa por tipo solo se expande a vehículos activos no dados de baja (VH2, VW y VE son vans de baja)
expect 200 "$(req GET /api/v1/maintenance-schedules/due)" | jq -e --argjson s "$SCHV" --arg a "$VH2_PID" --arg b "$VW_PID" --arg c "$VE_PID" --arg vb "$VB_PID" 'any(.[]; .scheduleId==$s and .vehiclePublicId==$vb) and all(.[]; .scheduleId!=$s or (.vehiclePublicId!=$a and .vehiclePublicId!=$b and .vehiclePublicId!=$c))' >/dev/null || fail "el programa por tipo no debe expandirse a vans dadas de baja"
expect 204 "$(req POST "/api/v1/vehicles/$VB_PID/deactivate")" >/dev/null
expect 200 "$(req GET /api/v1/maintenance-schedules/due)" | jq -e --argjson s "$SCHV" --arg vb "$VB_PID" 'all(.[]; .scheduleId!=$s or .vehiclePublicId!=$vb)' >/dev/null || fail "un vehículo inactivo sale del panel preventivo"
expect 204 "$(req POST "/api/v1/vehicles/$VB_PID/reactivate")" >/dev/null
# Hallazgo de revisión: protección del odómetro (bajar un error tecleado, OT cerrada como piso, el cierre de una OT no baja)
expect 200 "$(req PATCH "/api/v1/vehicles/$VA_PID" '{"currentOdometerKm":5300}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/vehicles/$VA_PID" '{"currentOdometerKm":5250}')" | jq -e '.currentOdometerKm==5250' >/dev/null || fail "corrección manual hacia abajo por encima de la última lectura"
expect 400 "$(req PATCH "/api/v1/vehicles/$VA_PID" '{"currentOdometerKm":5100}')" | jq -e --arg m "El odómetro no puede ser menor que la última lectura registrada (5200 km el $TODAY)." "$HASM" >/dev/null || fail "la OT cerrada es piso de la corrección manual"
WOV2_PID=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VA_PID\",\"maintenanceType\":\"CORRECTIVE\"}")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOV2_PID/status" '{"toCode":"CLOSED","odometerKm":5000}')" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles/$VA_PID")" | jq -e '.currentOdometerKm==5250' >/dev/null || fail "cerrar una OT con lectura menor no baja el odómetro"
# Hallazgo de revisión: edición y checkbox Activo del programa (P4 c/d)
expect 400 "$(req PATCH "/api/v1/maintenance-schedules/$SCH1" '{"trigger":"TIME"}')" | jq -e '.errors.intervalDays' >/dev/null || fail "PATCH revalida el intervalo contra el disparador resultante"
expect 204 "$(req POST "/api/v1/maintenance-schedules/$SCH1/deactivate")" >/dev/null
due "$VH1_PID" "$SCH1" | jq -e '. == null' >/dev/null || fail "un programa inactivo no aparece en el panel"
expect 204 "$(req POST "/api/v1/maintenance-schedules/$SCH1/reactivate")" >/dev/null
due "$VH1_PID" "$SCH1" | jq -e '.state=="OVERDUE"' >/dev/null || fail "el programa reactivado vuelve al panel"
SCHX=$(expect 200 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"Cambio objetivo $TS\",\"vehiclePublicId\":\"$VH1_PID\",\"trigger\":\"MILEAGE\",\"intervalKm\":1000,\"lastServiceKm\":100}")" | jq -r .id)
expect 200 "$(req PATCH "/api/v1/maintenance-schedules/$SCHX" '{"vehicleType":"VAN"}')" | jq -e '.vehiclePublicId==null and .vehicleTypeCode=="VAN" and .lastServiceKm==null and .lastServiceDate==null' >/dev/null || fail "pasar a programa por tipo limpia el último servicio"
expect 400 "$(req PATCH "/api/v1/maintenance-schedules/$SCHX" '{"lastServiceKm":10}')" | jq -e --arg m "El último servicio solo se captura en programas de un vehículo; en los de tipo se toma de sus órdenes de trabajo cerradas." "$HASM" >/dev/null || fail "último servicio en programa por tipo"
expect 204 "$(req POST "/api/v1/maintenance-schedules/$SCHX/deactivate")" >/dev/null
expect 404 "$(req PATCH /api/v1/maintenance-schedules/999999 '{"name":"x"}')" | jq -e '.title=="Programa de mantenimiento no encontrado."' >/dev/null || fail "PATCH de programa inexistente"
expect 404 "$(req POST /api/v1/maintenance-schedules/999999/deactivate)" >/dev/null
ok "por vehículo: OK → DUE_SOON (≤10 %) → OVERDUE por odómetro; por tiempo OVERDUE; por tipo: NO_BASELINE hasta cerrar una OT (5200, número, fecha) sin afectar a otra van; 400 por vehículo+tipo, sin intervalo y disparador desconocido; PATCH revalida el intervalo contra el disparador (400), vehículo → tipo limpia el último servicio (400 si se captura en uno por tipo), inactivar/reactivar lo saca y lo regresa al panel, 404 por programa inexistente; el programa por tipo no se expande a vans dadas de baja ni a una inactiva (checkbox); odómetro: la corrección manual baja un error (5300 → 5250) pero no bajo la OT cerrada (5200, 400) y cerrar una OT con lectura menor (5000) no lo baja"

step "órdenes de trabajo (Lote 4): número, tareas, costos, cierre y efecto en el vehículo"
WO1=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH1_PID\",\"scheduleId\":$SCH1,\"vendor\":\"Taller $TS\"}")")
echo "$WO1" | jq -e '(.number | test("^OT-[0-9]{5,}$")) and .statusCode=="OPEN" and .maintenanceTypeCode=="PREVENTIVE" and .canEdit' >/dev/null || fail "alta de OT: $WO1"
WO1_PID=$(echo "$WO1" | jq -r .publicId); WO1_ID=$(echo "$WO1" | jq -r .id); WO1_NUM=$(echo "$WO1" | jq -r .number)
expect 400 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH1_PID\",\"scheduleId\":$SCHT}")" | jq -e --arg m "El programa de mantenimiento no aplica a este vehículo." "$HASM" >/dev/null || fail "programa de otro vehículo"
expect 400 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH1_PID\",\"scheduleId\":$SCH1,\"maintenanceType\":\"CORRECTIVE\"}")" >/dev/null
expect 400 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH1_PID\"}")" | jq -e --arg m "El tipo de mantenimiento es obligatorio." "$HASM" >/dev/null || fail "tipo obligatorio sin programa"
TK1=$(expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/tasks" '{"description":"Cambio de aceite","partCost":40,"laborCost":25}')" | jq -r '.tasks[0].id')
W=$(expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/tasks" '{"description":"Filtro de aire","partCost":15,"laborCost":10}')")
echo "$W" | jq -e '.laborCost==35 and .partsCost==55 and .totalCost==90 and .costsFromTasks' >/dev/null || fail "costos sumados de las tareas: $W"
TK2=$(echo "$W" | jq -r --argjson a "$TK1" '[.tasks[] | select(.id!=$a)][0].id')
expect 400 "$(req PATCH "/api/v1/maintenance-work-orders/$WO1_PID" '{"laborCost":10}')" | jq -e --arg m "La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas." "$HASM" >/dev/null || fail "costos del encabezado con tareas"
expect 400 "$(req PATCH "/api/v1/maintenance-work-orders/$WO1_PID" '{"number":"OT-1"}')" | jq -e --arg m "El número y el vehículo de la orden de trabajo se fijan al crearla." "$HASM" >/dev/null || fail "número de la OT fijo"
expect 400 "$(req PATCH "/api/v1/maintenance-work-orders/$WO1_PID" "{\"vehiclePublicId\":\"$VH3_PID\"}")" | jq -e --arg m "El número y el vehículo de la orden de trabajo se fijan al crearla." "$HASM" >/dev/null || fail "vehículo de la OT fijo"
expect 409 "$(req PATCH "/api/v1/maintenance-work-orders/$WO1_PID" '{"notes":"x","rowVersion":"AAAAAAAAAAA="}')" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "rowVersion obsoleto en el PATCH de la OT"
expect 409 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/status" '{"toCode":"IN_PROGRESS","rowVersion":"AAAAAAAAAAA="}')" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "rowVersion obsoleto en el estatus de la OT"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID")" | jq -e '.statusCode=="ACTIVE"' >/dev/null || fail "el 409 de la OT no debe dejar el vehículo en MAINTENANCE"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/status" '{"toCode":"IN_PROGRESS"}')" | jq -e '.statusCode=="IN_PROGRESS"' >/dev/null || fail "OT IN_PROGRESS"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID")" | jq -e '.statusCode=="MAINTENANCE"' >/dev/null || fail "el vehículo pasa a MAINTENANCE"
expect 200 "$(req GET /api/v1/fleet/availability)" | jq -e --arg p "$VH1_PID" --arg n "$WO1_NUM" '.vehicles[] | select(.publicId==$p) | (.available | not) and any(.issues[]; .code=="WORK_ORDER_IN_PROGRESS" and (.message | contains($n)))' >/dev/null || fail "WORK_ORDER_IN_PROGRESS en disponibilidad"
expect 200 "$(req PATCH "/api/v1/maintenance-work-orders/$WO1_PID/tasks/$TK1" '{"isCompleted":true}')" >/dev/null
expect 422 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/status" '{"toCode":"CLOSED","odometerKm":5200}')" | jq -e '.title | startswith("La orden tiene 1 tarea(s) sin completar")' >/dev/null || fail "cerrar con una tarea pendiente"
expect 200 "$(req PATCH "/api/v1/maintenance-work-orders/$WO1_PID/tasks/$TK2" '{"isCompleted":true}')" >/dev/null
expect 400 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/status" '{"toCode":"CLOSED"}')" | jq -e --arg m "Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje." "$HASM" >/dev/null || fail "cerrar sin odómetro"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/status" '{"toCode":"CLOSED","odometerKm":5200}')" | jq -e '.statusCode=="CLOSED" and .isTerminal and (.canEdit | not)' >/dev/null || fail "cerrar la OT"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID")" | jq -e '.statusCode=="ACTIVE" and .currentOdometerKm==5200' >/dev/null || fail "el vehículo vuelve a ACTIVE con odómetro 5200"
due "$VH1_PID" "$SCH1" | jq -e --arg n "$WO1_NUM" '.state=="OK" and .lastServiceKm==5200 and .lastWorkOrderNumber==$n' >/dev/null || fail "el programa queda al día"
expect 200 "$(req GET /api/v1/maintenance-schedules)" | jq -e --argjson s "$SCH1" --arg t "$TODAY" 'any(.[]; .id==$s and .lastServiceKm==5200 and .lastServiceDate==$t)' >/dev/null || fail "el cierre de la OT avanza el último servicio del programa por vehículo"
expect 422 "$(req PATCH "/api/v1/maintenance-work-orders/$WO1_PID" '{"notes":"x"}')" >/dev/null
expect 422 "$(req POST "/api/v1/maintenance-work-orders/$WO1_PID/tasks" '{"description":"tarde"}')" >/dev/null
expect 200 "$(req GET "/api/v1/status/history/WORK_ORDER/$WO1_ID")" | jq -e '(map(.toCode) | sort)==["CLOSED","IN_PROGRESS","OPEN"]' >/dev/null || fail "historial de la OT"
expect 409 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH2_PID\",\"maintenanceType\":\"CORRECTIVE\"}")" | jq -e '.title | startswith("El vehículo está inactivo")' >/dev/null || fail "OT sobre un vehículo dado de baja"
expect 200 "$(req GET "/api/v1/maintenance-work-orders?vehiclePublicId=$VH1_PID&status=CLOSED")" | jq -e --arg p "$WO1_PID" 'any(.[]; .publicId==$p and .totalCost==90)' >/dev/null || fail "lista de OT filtrada"
# Hallazgo de revisión: cancelar una OT en proceso devuelve el vehículo a ACTIVE
WOA=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\"}")"); WOA_PID=$(echo "$WOA" | jq -r .publicId); WOA_NUM=$(echo "$WOA" | jq -r .number)
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOA_PID/status" '{"toCode":"IN_PROGRESS"}')" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles/$VH3_PID")" | jq -e '.statusCode=="MAINTENANCE"' >/dev/null || fail "V3 en MAINTENANCE"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOA_PID/status" '{"toCode":"CANCELLED","comment":"no procede"}')" | jq -e '.statusCode=="CANCELLED"' >/dev/null || fail "cancelar OT en proceso"
expect 200 "$(req GET "/api/v1/vehicles/$VH3_PID")" | jq -e '.statusCode=="ACTIVE"' >/dev/null || fail "cancelar la OT devuelve el vehículo a ACTIVE"
expect 200 "$(req GET "/api/v1/status/history/VEHICLE/$VH3_ID")" | jq -e --arg c "$WOA_NUM cancelada" 'max_by(.id) | .toCode=="ACTIVE" and .comment==$c' >/dev/null || fail "historial '… cancelada' del vehículo"
# Hallazgo de revisión: con dos OT en proceso, cerrar una no devuelve el vehículo a ACTIVE
WOB_PID=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\"}")" | jq -r .publicId)
WOC=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\"}")"); WOC_PID=$(echo "$WOC" | jq -r .publicId); WOC_NUM=$(echo "$WOC" | jq -r .number)
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOB_PID/status" '{"toCode":"IN_PROGRESS"}')" >/dev/null
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOC_PID/status" '{"toCode":"IN_PROGRESS"}')" >/dev/null
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOB_PID/status" '{"toCode":"CLOSED"}')" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles/$VH3_PID")" | jq -e '.statusCode=="MAINTENANCE"' >/dev/null || fail "con otra OT en proceso el vehículo sigue en MAINTENANCE"
expect 200 "$(req GET /api/v1/fleet/availability)" | jq -e --arg p "$VH3_PID" --arg n "$WOC_NUM" '.vehicles[] | select(.publicId==$p) | any(.issues[]; .code=="WORK_ORDER_IN_PROGRESS" and (.message | contains($n)))' >/dev/null || fail "WORK_ORDER_IN_PROGRESS con el número de la otra OT"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOC_PID/status" '{"toCode":"CLOSED"}')" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles/$VH3_PID")" | jq -e '.statusCode=="ACTIVE"' >/dev/null || fail "al cerrar la última OT en proceso el vehículo vuelve a ACTIVE"
# Hallazgo de revisión: MAINTENANCE puesto a mano + OT en proceso → al cerrar la OT el vehículo vuelve a ACTIVE
VM_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VM$TS")")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/vehicles/$VM_PID/status" '{"toCode":"MAINTENANCE","comment":"a mano"}')" >/dev/null
WOM_PID=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VM_PID\",\"maintenanceType\":\"CORRECTIVE\"}")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOM_PID/status" '{"toCode":"IN_PROGRESS"}')" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles/$VM_PID")" | jq -e '.statusCode=="MAINTENANCE"' >/dev/null || fail "MAINTENANCE a mano se conserva al iniciar la OT"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOM_PID/status" '{"toCode":"CLOSED"}')" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles/$VM_PID")" | jq -e '.statusCode=="ACTIVE"' >/dev/null || fail "MAINTENANCE puesto a mano vuelve a ACTIVE al cerrar la OT"
# Hallazgo de revisión: vehículo dado de baja con OT en proceso → cerrar la OT no toca su estatus ni su odómetro
VX_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VX$TS" '{"currentOdometerKm":1000}')")" | jq -r .publicId)
WOX_PID=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VX_PID\",\"maintenanceType\":\"CORRECTIVE\"}")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOX_PID/status" '{"toCode":"IN_PROGRESS"}')" >/dev/null
expect 200 "$(req POST "/api/v1/vehicles/$VX_PID/status" '{"toCode":"INACTIVE","comment":"baja"}')" >/dev/null
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOX_PID/status" '{"toCode":"CLOSED","odometerKm":9000}')" >/dev/null
expect 200 "$(req GET "/api/v1/vehicles/$VX_PID")" | jq -e '.statusCode=="INACTIVE" and .currentOdometerKm==1000' >/dev/null || fail "vehículo terminal: cerrar la OT no cambia su estatus ni su odómetro"
# Hallazgo de revisión: quitar tareas (deja de contar para el cierre y recalcula costos), costos negativos, cierre con fecha futura
WOD_PID=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\"}")" | jq -r .publicId)
expect 400 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks" '{"description":"x","partCost":-1}')" | jq -e --arg m "Los costos no pueden ser negativos." "$HASM" >/dev/null || fail "costo negativo en tarea"
expect 400 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\",\"laborCost\":-5}")" | jq -e --arg m "Los costos no pueden ser negativos." "$HASM" >/dev/null || fail "costo negativo en el encabezado"
# Hallazgo de revisión: sin tareas los costos se capturan en el encabezado (TotalCost computado en SQL); precisión (18,4)
WOE=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\",\"laborCost\":100.5,\"partsCost\":49.5}")")
echo "$WOE" | jq -e '.laborCost==100.5 and .partsCost==49.5 and .totalCost==150 and (.costsFromTasks | not)' >/dev/null || fail "costos capturados en el encabezado: $WOE"
WOE_PID=$(echo "$WOE" | jq -r .publicId)
expect 200 "$(req PATCH "/api/v1/maintenance-work-orders/$WOE_PID" '{"partsCost":10}')" | jq -e '.laborCost==100.5 and .partsCost==10 and .totalCost==110.5' >/dev/null || fail "PATCH de costos del encabezado sin tareas"
expect 400 "$(req PATCH "/api/v1/maintenance-work-orders/$WOE_PID" '{"laborCost":1.23456}')" | jq -e '.errors.laborCost[0] | startswith("El valor admite como máximo 4 decimales")' >/dev/null || fail "precisión del costo de la OT"
expect 400 "$(req PATCH "/api/v1/maintenance-work-orders/$WOE_PID" '{"partsCost":100000000000000}')" | jq -e '.errors.partsCost' >/dev/null || fail "magnitud del costo de la OT"
expect 400 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks" '{"description":"x","laborCost":1.23456}')" | jq -e '.errors.laborCost[0] | startswith("El valor admite como máximo 4 decimales")' >/dev/null || fail "precisión del costo de la tarea"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOE_PID/status" '{"toCode":"CANCELLED","comment":"smoke costos"}')" >/dev/null
TD1=$(expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks" '{"description":"A","partCost":40,"laborCost":25,"isCompleted":true}')" | jq -r '.tasks[0].id')
TD2=$(expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks" '{"description":"B","partCost":15,"laborCost":10}')" | jq -r --argjson a "$TD1" '[.tasks[] | select(.id!=$a)][0].id')
expect 422 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/status" '{"toCode":"CLOSED"}')" >/dev/null
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks/$TD2/deactivate")" | jq -e '.laborCost==25 and .partsCost==40 and .totalCost==65 and (.tasks | length)==1' >/dev/null || fail "quitar tarea recalcula costos"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks/$TD2/deactivate")" >/dev/null   # idempotente
expect 404 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks/$TK1/deactivate")" | jq -e '.title=="Tarea no encontrada."' >/dev/null || fail "quitar la tarea de otra OT"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/tasks/$TD1/deactivate")" | jq -e '.laborCost==null and .partsCost==null and .totalCost==0 and (.costsFromTasks | not)' >/dev/null || fail "sin tareas el encabezado queda vacío"
expect 400 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/status" "{\"toCode\":\"CLOSED\",\"completedDate\":\"$(dplus 1)\"}")" | jq -e --arg m "La fecha de cierre no puede ser futura." "$HASM" >/dev/null || fail "cierre con fecha futura"
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOD_PID/status" '{"toCode":"CLOSED"}')" | jq -e '.statusCode=="CLOSED"' >/dev/null || fail "cerrar tras quitar la tarea pendiente"
# Hallazgo de revisión: to=9999-12-31 no desborda (antes 500)
expect 200 "$(req GET "/api/v1/maintenance-work-orders?vehiclePublicId=$VH3_PID&to=9999-12-31")" | jq -e --arg p "$WOD_PID" 'any(.[]; .publicId==$p)' >/dev/null || fail "filtro to=9999-12-31"
ok "OT-##### OPEN, programa ajeno 400, tareas suman costos (400 al editarlos), número y vehículo fijos (400), IN_PROGRESS → MAINTENANCE + WORK_ORDER_IN_PROGRESS, cierre 422 (tarea) / 400 (odómetro) / CLOSED → ACTIVE, odómetro y programa al día, 422 tras cerrar, historial, 409 en vehículo de baja; cancelar devuelve a ACTIVE; dos en proceso: sigue en MAINTENANCE hasta cerrar la última; quitar tareas (recalcula, deja cerrar, idempotente, 404 ajena), costos negativos y cierre futuro 400, to=9999-12-31 sin 500; 409 por rowVersion obsoleto (PATCH y estatus, sin tocar el vehículo); MAINTENANCE puesto a mano vuelve a ACTIVE al cerrar la OT; un vehículo dado de baja no cambia de estatus ni de odómetro al cerrar su OT; costos del encabezado sin tareas (150 → 110.5) y precisión 18,4 en OT y tarea"

step "órdenes de trabajo (Lote 4): 8 altas simultáneas con números consecutivos"
TMPW=$(mktemp -d)
for i in 1 2 3 4 5 6 7 8; do req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\",\"notes\":\"paralela $i\"}" > "$TMPW/$i" & done
wait
for i in 1 2 3 4 5 6 7 8; do [[ "$(tail -n1 "$TMPW/$i")" == "200" ]] || fail "OT simultánea $i: $(cat "$TMPW/$i")"; done
WNUMS=$(for i in 1 2 3 4 5 6 7 8; do sed '$d' "$TMPW/$i" | jq -r .number; done | sort)
WO8_PID=$(sed '$d' "$TMPW/1" | jq -r .publicId); rm -rf "$TMPW"
[[ $(echo "$WNUMS" | uniq | wc -l) -eq 8 ]] || fail "números de OT repetidos: $WNUMS"
FIRSTW=$(seqof "$(echo "$WNUMS" | head -n1)"); LASTW=$(seqof "$(echo "$WNUMS" | tail -n1)")
[[ $((LASTW - FIRSTW)) -eq 7 ]] || fail "números de OT no consecutivos: $WNUMS"
# Hallazgo de revisión: solo IN_PROGRESS inhabilita; V3 tiene ahora 8 OT en OPEN y sigue disponible
expect 200 "$(req GET /api/v1/fleet/availability)" | jq -e --arg p "$VH3_PID" '.vehicles[] | select(.publicId==$p) | .available and all(.issues[]; .code!="WORK_ORDER_IN_PROGRESS")' >/dev/null || fail "una OT en OPEN no inhabilita el vehículo"
ok "8 × 200, sin 409 ni 500, números distintos y consecutivos ($(echo "$WNUMS" | head -n1) … $(echo "$WNUMS" | tail -n1)); con 8 OT en OPEN el vehículo sigue disponible"

step "bitácora de combustible (Lote 4): km/L, costo/km y odómetro protegido"
fl() { jq -cn --arg v "$VH1_PID" --arg f "$1" --argjson o "$2" --argjson l "${3:-40}" --argjson c "${4:-60}" '{vehiclePublicId:$v,fillDateUtc:$f,odometerKm:$o,liters:$l,totalCost:$c,station:"Puma"}'; }
F1=$(expect 200 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 180)" 5200)")" | jq -r .id)
F2=$(expect 200 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 120)" 5600)")" | jq -r .id)
F3=$(expect 200 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 60)" 6000 40 64)")" | jq -r .id)
FP=$(expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID")")
echo "$FP" | jq -e --argjson a "$F1" --argjson b "$F2" --argjson c "$F3" '(.items[] | select(.id==$a) | .kmPerLiter==null) and (.items[] | select(.id==$b) | .distanceKm==400 and .kmPerLiter==10 and .costPerKm==0.15) and (.items[] | select(.id==$c) | .kmPerLiter==10 and .costPerKm==0.16) and .summary[0].kmPerLiter==10 and .total==3' >/dev/null || fail "eficiencia: $(echo "$FP" | jq -c '[.items[] | {id,odometerKm,distanceKm,kmPerLiter,costPerKm}], .summary')"
# Hallazgo de revisión: paginación, rango (fromUtc inclusivo, toUtc exclusivo) y eficiencia sobre la serie completa, no la página
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID&take=1")" | jq -e --argjson c "$F3" '.total==3 and (.items|length)==1 and .items[0].id==$c and .items[0].distanceKm==400 and .items[0].kmPerLiter==10 and .summary[0].kmPerLiter==10' >/dev/null || fail "eficiencia calculada sobre la página"
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID&skip=1&take=1")" | jq -e --argjson b "$F2" '(.items|length)==1 and .items[0].id==$b' >/dev/null || fail "skip"
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID&fromUtc=$(mago 90)")" | jq -e --argjson c "$F3" '.total==1 and .items[0].id==$c and .items[0].distanceKm==400 and .items[0].kmPerLiter==10' >/dev/null || fail "fromUtc recorta la serie de eficiencia"
F3D=$(echo "$FP" | jq -r --argjson c "$F3" '.items[] | select(.id==$c) | .fillDateUtc')
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID&toUtc=$F3D")" | jq -e '.total==2' >/dev/null || fail "toUtc debe ser exclusivo"
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID&fromUtc=$F3D")" | jq -e '.total==1' >/dev/null || fail "fromUtc debe ser inclusivo"
expect 400 "$(req GET "/api/v1/fuel-logs?take=0")" | jq -e --arg m "take debe estar entre 1 y 500." "$HASM" >/dev/null || fail "take=0"
expect 400 "$(req GET "/api/v1/fuel-logs?take=501")" | jq -e --arg m "take debe estar entre 1 y 500." "$HASM" >/dev/null || fail "take=501"
expect 400 "$(req GET "/api/v1/fuel-logs?skip=-1")" | jq -e --arg m "skip no puede ser negativo." "$HASM" >/dev/null || fail "skip negativo"
expect 400 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 90)" 5500)")" | jq -e '.title | contains("es menor que la de una carga anterior del mismo vehículo")' >/dev/null || fail "odómetro no monótono"
expect 400 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 30)" 6100 0)")" | jq -e '.errors.liters' >/dev/null || fail "0 litros"
expect 400 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 30)" 6100 1.23456)")" | jq -e '.errors.liters[0] | startswith("El valor admite como máximo 3 decimales")' >/dev/null || fail "precisión de litros"
expect 400 "$(req POST /api/v1/fuel-logs "$(fl "$(date -u -d '+2 hours' +%FT%T)" 6100)")" | jq -e --arg m "La fecha de la carga no puede ser futura." "$HASM" >/dev/null || fail "carga futura"
expect 409 "$(req POST /api/v1/fuel-logs "$(jq -cn --arg v "$VH2_PID" --arg f "$(mago 5)" '{vehiclePublicId:$v,fillDateUtc:$f,liters:10,totalCost:10}')")" >/dev/null   # vehículo de baja
expect 400 "$(req PATCH "/api/v1/fuel-logs/$F1" "{\"vehiclePublicId\":\"$VH3_PID\"}")" | jq -e --arg m "El vehículo de una carga no se cambia; desactívela y registre otra." "$HASM" >/dev/null || fail "cambiar el vehículo de una carga"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID")" | jq -e '.currentOdometerKm==6000' >/dev/null || fail "odómetro del vehículo = 6000"
expect 400 "$(req PATCH "/api/v1/vehicles/$VH1_PID" '{"currentOdometerKm":5000}')" | jq -e '.title | startswith("El odómetro no puede ser menor que la última lectura registrada (6")' >/dev/null || fail "corrección manual bajo la última lectura"
expect 200 "$(req POST "/api/v1/fuel-logs/$F3/deactivate")" >/dev/null
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID")" | jq -e '.total==2 and .summary[0].totalLiters==80 and .summary[0].distanceKm==400' >/dev/null || fail "la carga desactivada deja de contar"
expect 200 "$(req GET "/api/v1/vehicles/$VH1_PID")" | jq -e '.currentOdometerKm==6000' >/dev/null || fail "desactivar una carga no baja el odómetro"
# Hallazgo de revisión: carga con chofer (vínculo, filtro por chofer y clearDriver)
FD=$(expect 200 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 20)" 6100 | jq -c --arg d "$DR1_PID" '. + {driverPublicId:$d}')")")
echo "$FD" | jq -e --arg d "$DR1_PID" '.driverPublicId==$d and (.driverName | length > 0)' >/dev/null || fail "carga con chofer: $FD"
expect 200 "$(req GET "/api/v1/fuel-logs?driverPublicId=$DR1_PID")" | jq -e --argjson f "$(echo "$FD" | jq .id)" '.total==1 and .items[0].id==$f' >/dev/null || fail "filtro por chofer"
expect 200 "$(req PATCH "/api/v1/fuel-logs/$(echo "$FD" | jq -r .id)" '{"clearDriver":true}')" | jq -e '.driverPublicId==null' >/dev/null || fail "clearDriver"
ok "km/L 10 y costo/km 0.15/0.16 sobre la serie, 400 por odómetro no monótono / 0 L / precisión / fecha futura, 409 en vehículo de baja, odómetro sube a 6000 y la corrección manual no baja de la última lectura; desactivar deja de contar; carga con chofer, filtro por chofer y clearDriver; paginación (take/skip, 400 fuera de rango), fromUtc inclusivo y toUtc exclusivo, y km/L calculado sobre la serie completa aunque la página o el rango solo traigan la última carga"

step "bitácora de combustible (Lote 4): 8 cargas simultáneas del mismo vehículo (bloqueo de fila del odómetro)"
VF_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VF$TS" '{"currentOdometerKm":1000}')")" | jq -r .publicId)
TMPF=$(mktemp -d)
for i in 1 2 3 4 5 6 7 8; do req POST /api/v1/fuel-logs "$(jq -cn --arg v "$VF_PID" --arg f "$(mago $((100 - i)))" --argjson o $((2000 + 100 * i)) '{vehiclePublicId:$v,fillDateUtc:$f,odometerKm:$o,liters:40,totalCost:60}')" > "$TMPF/$i" & done
wait
for i in 1 2 3 4 5 6 7 8; do [[ "$(tail -n1 "$TMPF/$i")" == "200" ]] || fail "carga simultánea $i: $(cat "$TMPF/$i")"; done
rm -rf "$TMPF"
expect 200 "$(req GET "/api/v1/vehicles/$VF_PID")" | jq -e '.currentOdometerKm==2800' >/dev/null || fail "odómetro tras cargas simultáneas != 2800"
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VF_PID")" | jq -e '.total==8 and .summary[0].distanceKm==700' >/dev/null || fail "no quedaron 8 cargas en serie"
ok "8 × 200 sin 409 ni 500; odómetro = máximo (2800) y la serie completa (700 km)"

step "tarifas por entrega (Lote 4)"
DDR1=$(expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"BOX","rate":3.5}')" | jq -r .id)
expect 409 "$(req POST "/api/v1/drivers/$DR1_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"BOX","rate":3.75}')" | jq -e '.title | startswith("El chofer ya tiene una tarifa vigente para ") and endswith("edite esa tarifa o ciérrela antes de agregar otra.")' >/dev/null || fail "tarifa de entrega repetida"
expect 400 "$(req POST "/api/v1/drivers/$DR1_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"ENVELOPE","rate":1.23456}')" | jq -e '.errors.rate[0] | startswith("El valor admite como máximo 4 decimales")' >/dev/null || fail "precisión de la tarifa"
expect 400 "$(req POST "/api/v1/drivers/$DR1_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"ENVELOPE","rate":-1}')" >/dev/null
expect 400 "$(req POST "/api/v1/drivers/$DR1_PID/delivery-rates" '{"serviceType":"NOPE","packageType":"BOX","rate":1}')" | jq -e '.errors.serviceType' >/dev/null || fail "servicio desconocido"
DDR2=$(expect 200 "$(req PATCH "/api/v1/drivers/$DR1_PID/delivery-rates/$DDR1" '{"rate":4.00}')" | jq -r .id)
[[ "$DDR2" != "$DDR1" ]] || fail "editar debe abrir una fila nueva"
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/rates?includeHistory=true")" | jq -e '[.deliveryRates[] | select(.serviceTypeCode=="STANDARD" and .packageTypeCode=="BOX")] | length==2 and (map(select(.isCurrent)) | length==1 and .[0].rate==4)' >/dev/null || fail "historial de la tarifa"
expect 400 "$(req PATCH "/api/v1/drivers/$DR1_PID/delivery-rates/$DDR2" '{"rate":4.5,"packageType":"ENVELOPE"}')" | jq -e --arg m "El servicio y el paquete se fijan al crear la tarifa; quite la fila y cree una nueva." "$HASM" >/dev/null || fail "servicio/paquete fijos"
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/delivery-rates/$DDR2/close")" >/dev/null
expect 409 "$(req PATCH "/api/v1/drivers/$DR1_PID/delivery-rates/$DDR2" '{"rate":5}')" | jq -e '.title=="La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla."' >/dev/null || fail "editar una tarifa cerrada"
expect 404 "$(req PATCH "/api/v1/drivers/$DR2_PID/delivery-rates/$DDR2" '{"rate":5}')" | jq -e '.title=="Tarifa no encontrada."' >/dev/null || fail "tarifa de otro chofer"
ok "alta 3.50, 409 repetida, 400 precisión/negativa/servicio desconocido, editar = cerrar y abrir (historial 2), servicio/paquete fijos, cerrar, 409 sobre cerrada, 404 bajo otro chofer"

step "niveles de intento, fórmula y vista previa (Lote 4, en el tenant recién creado T3)"
DT3=$(expect 200 "$(req POST /api/v1/drivers "{\"code\":\"DT$TS\",\"fullName\":\"Chofer T3 $TS\"}" "$T3")"); DT3_PID=$(echo "$DT3" | jq -r .publicId)
expect 404 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 10)" 6200 | jq -c --arg d "$DT3_PID" '. + {driverPublicId:$d}')")" | jq -e '.title=="Chofer no encontrado."' >/dev/null || fail "carga con chofer de otro tenant"
expect 200 "$(req GET /api/v1/driver-pay-policy '' "$T3")" | jq -e '.attemptLevels==2 and .payoutFormulaCode=="DELIVERY_PLUS_ATTEMPTS" and .isDefault and (.formulas | length)==3' >/dev/null || fail "política por defecto"
expect 200 "$(req PUT "/api/v1/drivers/$DT3_PID/attempt-rates/1" '{"rate":1.00}' "$T3")" >/dev/null
expect 200 "$(req PUT "/api/v1/drivers/$DT3_PID/attempt-rates/2" '{"rate":1.50}' "$T3")" >/dev/null
expect 400 "$(req PUT "/api/v1/drivers/$DT3_PID/attempt-rates/3" '{"rate":2.00}' "$T3")" | jq -e --arg m "El intento 3 no existe; los niveles configurados van de 1 a 2." "$HASM" >/dev/null || fail "intento fuera de los niveles"
expect 200 "$(req POST /api/v1/driver-pay-policy/attempt-levels '' "$T3")" | jq -e '.attemptLevels==3' >/dev/null || fail "+ Agregar intento"
expect 200 "$(req GET "/api/v1/drivers/$DT3_PID/rates" '' "$T3")" | jq -e '.attemptLevels==3 and (.attemptRates | map(.attemptNumber))==[1,2,3] and (.attemptRates[] | select(.attemptNumber==3) | .rate==null and .id==null)' >/dev/null || fail "nivel 3 sin tarifa"
expect 200 "$(req PUT "/api/v1/drivers/$DT3_PID/attempt-rates/3" '{"rate":2.00}' "$T3")" >/dev/null
# Hallazgo de revisión: tarifas por intento efectivo-fechadas (editar = cerrar y abrir), fecha pasada, cierre y cierre a futuro
A1=$(expect 200 "$(req GET "/api/v1/drivers/$DT3_PID/rates" '' "$T3")" | jq '.attemptRates[] | select(.attemptNumber==1) | .id')
A1B=$(expect 200 "$(req PUT "/api/v1/drivers/$DT3_PID/attempt-rates/1" '{"rate":1.25}' "$T3")" | jq '.id')
[[ "$A1B" =~ ^[0-9]+$ && "$A1B" != "$A1" ]] || fail "editar la tarifa del intento debe abrir una fila nueva ($A1 → $A1B)"
expect 200 "$(req GET "/api/v1/drivers/$DT3_PID/rates?includeHistory=true" '' "$T3")" | jq -e '[.attemptRates[] | select(.attemptNumber==1)] | length==2 and (map(select(.isCurrent)) | length==1 and .[0].rate==1.25)' >/dev/null || fail "historial de la tarifa del intento 1"
expect 400 "$(req PUT "/api/v1/drivers/$DT3_PID/attempt-rates/1" "{\"rate\":1,\"effectiveFrom\":\"$YESTERDAY\"}" "$T3")" | jq -e '.errors.effectiveFrom' >/dev/null || fail "tarifa de intento en el pasado"
expect 200 "$(req POST "/api/v1/drivers/$DT3_PID/attempt-rates/1/close" "{\"effectiveTo\":\"$(dplus 5)\"}" "$T3")" | jq -e --arg d "$(dplus 5)" '.effectiveTo==$d' >/dev/null || fail "cerrar la tarifa del intento a futuro"
expect 409 "$(req PUT "/api/v1/drivers/$DT3_PID/attempt-rates/1" '{"rate":2}' "$T3")" | jq -e '.title | startswith("El intento 1 tiene una tarifa vigente hasta el")' >/dev/null || fail "tarifa nueva antes del cierre a futuro"
expect 404 "$(req POST "/api/v1/drivers/$DT3_PID/attempt-rates/1/close" '' "$T3")" | jq -e '.title=="Tarifa no encontrada."' >/dev/null || fail "cerrar un intento sin tarifa abierta"
PVB='{"attempts":[{"number":1,"delivered":false},{"number":2,"delivered":false},{"number":3,"delivered":false},{"number":4,"delivered":true}],"deliveryRate":4.00,"attemptRates":[{"number":1,"rate":1.00},{"number":2,"rate":1.50},{"number":3,"rate":2.00}]'
for P in "DELIVERY_PLUS_ATTEMPTS 10.5" "DELIVERY_INCLUDES_FIRST 9.5" "FAILED_REPLACES_DELIVERY 8.5"; do
  set -- $P
  expect 200 "$(req POST /api/v1/driver-pay-policy/preview "$PVB,\"formula\":\"$1\"}" "$T3")" | jq -e --argjson t "$2" --arg f "$1" '.total==$t and .formulaCode==$f' >/dev/null || fail "vista previa $1 = $2"
done
expect 200 "$(req POST /api/v1/driver-pay-policy/preview '{"attempts":[{"number":1,"delivered":true}],"attemptRates":[{"number":1,"rate":1.00}]}' "$T3")" | jq -e 'any(.lines[]; .amount==0 and .note=="sin tarifa configurada")' >/dev/null || fail "entrega sin tarifa → línea 0 con nota"
expect 400 "$(req PATCH /api/v1/driver-pay-policy '{"payoutFormula":"FOO"}' "$T3")" >/dev/null
expect 200 "$(req PATCH /api/v1/driver-pay-policy '{"payoutFormula":"DELIVERY_INCLUDES_FIRST"}' "$T3")" | jq -e '.payoutFormulaCode=="DELIVERY_INCLUDES_FIRST" and (.isDefault | not)' >/dev/null || fail "cambiar la fórmula"
expect 200 "$(req POST /api/v1/driver-pay-policy/preview "$PVB}" "$T3")" | jq -e '.total==9.5 and .formulaCode=="DELIVERY_INCLUDES_FIRST"' >/dev/null || fail "la vista previa usa la fórmula vigente"
# Hallazgo de revisión: tope de 20 niveles de intento
for _ in $(seq 4 20); do expect 200 "$(req POST /api/v1/driver-pay-policy/attempt-levels '' "$T3")" >/dev/null; done
expect 200 "$(req GET /api/v1/driver-pay-policy '' "$T3")" | jq -e '.attemptLevels==20' >/dev/null || fail "20 niveles"
expect 409 "$(req POST /api/v1/driver-pay-policy/attempt-levels '' "$T3")" | jq -e '.title=="Se alcanzó el máximo de 20 niveles de intento."' >/dev/null || fail "tope de 20 niveles"
ok "defaults (2 niveles, entrega + cada intento), 400 intento 3, + Agregar intento → 3 (sin tarifa hasta capturarla), tarifa de intento: editar = cerrar y abrir (historial 2), 400 en el pasado, cierre a futuro (409 al abrir antes, 404 sin abierta), vista previa 10.50/9.50/8.50, entrega sin tarifa en 0 con nota, fórmula desconocida 400 y fórmula vigente, tope 20 niveles → 409"

step "tarifas por viaje (Lote 4): el tipo sale del catálogo de servicios especiales"
VAGON=$(expect 200 "$(req GET /api/v1/driver-trip-types)" | jq -r --arg n "Vagón $TS" '[.[] | select(.name==$n)][0].id')
[[ "$VAGON" =~ ^[0-9]+$ ]] || fail "'Vagón $TS' no aparece en /driver-trip-types"
expect 200 "$(req GET /api/v1/driver-trip-types)" | jq -e 'length >= 1 and all(.[]; (has("clientsUsing") | not) and (has("id") and has("name")))' >/dev/null || fail "/driver-trip-types expone datos de contratos (clientsUsing)"
MONTA=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/special-services" "{\"newTypeName\":\"Montacargas $TS\",\"rate\":90}")" | jq -r .typeId)
TR1=$(expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trip-rates" "{\"specialServiceTypeId\":$VAGON,\"rate\":75}")" | jq -r .id)
expect 409 "$(req POST "/api/v1/drivers/$DR1_PID/trip-rates" "{\"specialServiceTypeId\":$VAGON,\"rate\":70}")" | jq -e --arg n "Vagón $TS" '.title | contains($n)' >/dev/null || fail "tarifa por viaje repetida"
expect 400 "$(req PATCH "/api/v1/drivers/$DR1_PID/trip-rates/$TR1" "{\"rate\":75,\"specialServiceTypeId\":$MONTA}")" | jq -e --arg m "El tipo de viaje se fija al crear la tarifa; quite la fila y agregue una con el tipo correcto." "$HASM" >/dev/null || fail "tipo de viaje fijo"
expect 400 "$(req POST "/api/v1/drivers/$DT3_PID/trip-rates" '{"specialServiceTypeId":1,"rate":10}' "$T3")" | jq -e --arg m "Sin servicios especiales: agréguelos en Clientes y contratos antes de configurar tarifas por viaje." "$HASM" >/dev/null || fail "tenant sin servicios especiales"
# Hallazgo de revisión: tipo de viaje inexistente (404) o inactivo (400), en tarifas por viaje y en viajes
expect 404 "$(req POST "/api/v1/drivers/$DR1_PID/trip-rates" '{"specialServiceTypeId":999999,"rate":10}')" | jq -e '.title=="Tipo de servicio especial no encontrado."' >/dev/null || fail "tipo de viaje inexistente (tarifa)"
expect 404 "$(req POST "/api/v1/drivers/$DR1_PID/trips" '{"specialServiceTypeId":999999}')" | jq -e '.title=="Tipo de servicio especial no encontrado."' >/dev/null || fail "tipo de viaje inexistente (viaje)"
SSX=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/special-services" "{\"newTypeName\":\"Inactivo $TS\",\"rate\":1}")")
SSX_TYPE=$(echo "$SSX" | jq -r .typeId)
expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/special-services/$(echo "$SSX" | jq -r .id)/close" '{}')" >/dev/null
expect 200 "$(req POST "/api/v1/special-service-types/$SSX_TYPE/deactivate")" >/dev/null
expect 400 "$(req POST "/api/v1/drivers/$DR1_PID/trip-rates" "{\"specialServiceTypeId\":$SSX_TYPE,\"rate\":10}")" | jq -e --arg m "El tipo de servicio especial está inactivo; reactívelo o elija otro." "$HASM" >/dev/null || fail "tipo de viaje inactivo (tarifa)"
expect 400 "$(req POST "/api/v1/drivers/$DR1_PID/trips" "{\"specialServiceTypeId\":$SSX_TYPE}")" | jq -e --arg m "El tipo de servicio especial está inactivo; reactívelo o elija otro." "$HASM" >/dev/null || fail "tipo de viaje inactivo (viaje)"
expect 409 "$(req POST "/api/v1/special-service-types/$VAGON/deactivate")" | jq -e '.title=="El tipo tiene tarifas por viaje vigentes en 1 chofer(es); ciérrelas antes de inactivarlo."' >/dev/null || fail "inactivar un tipo con tarifas de chofer"
ok "tipos de viaje = servicios especiales activos (solo id y nombre, sin clientsUsing), alta 75, 409 repetida, tipo fijo, 'Sin servicios especiales…' en T3, 409 al inactivar un tipo con tarifas vigentes, tipo inexistente 404 e inactivo 400 (en tarifas y en viajes)"

step "viajes del chofer (Lote 4): monto congelado"
TP1=$(expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trips" "{\"specialServiceTypeId\":$VAGON}")")
echo "$TP1" | jq -e --arg t "$TODAY" '.amount==75 and .statusCode=="OPEN" and (.rateMissing | not) and .tripDate==$t and .isActive' >/dev/null || fail "viaje a 75: $TP1"
TP1_PID=$(echo "$TP1" | jq -r .publicId); TP1_ID=$(echo "$TP1" | jq -r .id)
TR2=$(expect 200 "$(req PATCH "/api/v1/drivers/$DR1_PID/trip-rates/$TR1" '{"rate":80}')" | jq -e -r 'select(.rate==80) | .id') || fail "tarifa por viaje a 80"
[[ "$TR2" =~ ^[0-9]+$ && "$TR2" != "$TR1" ]] || fail "editar la tarifa por viaje debe abrir una fila nueva ($TR1 → $TR2)"
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/rates?includeHistory=true")" | jq -e --argjson a "$TR1" --argjson b "$TR2" --argjson v "$VAGON" --arg t "$TODAY" '[.tripRates[] | select(.specialServiceTypeId==$v)] | length==2 and any(.[]; .id==$a and .effectiveTo==$t and (.isCurrent | not)) and any(.[]; .id==$b and .rate==80 and .isCurrent)' >/dev/null || fail "historial de la tarifa por viaje"
expect 400 "$(req PATCH "/api/v1/drivers/$DR1_PID/trip-rates/$TR2" "{\"rate\":81,\"effectiveFrom\":\"$YESTERDAY\"}")" | jq -e '.errors.effectiveFrom' >/dev/null || fail "tarifa por viaje en el pasado"
expect 404 "$(req PATCH "/api/v1/drivers/$DR2_PID/trip-rates/$TR2" '{"rate":5}')" | jq -e '.title=="Tarifa no encontrada."' >/dev/null || fail "tarifa por viaje de otro chofer"
expect 404 "$(req POST "/api/v1/drivers/$DR2_PID/trip-rates/$TR2/close")" >/dev/null
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trips" "{\"specialServiceTypeId\":$VAGON}")" | jq -e '.amount==80' >/dev/null || fail "viaje nuevo a 80"
# Hallazgo de revisión: se congela la tarifa vigente en tripDate (ayer no había ninguna: la de 75 empezó hoy)
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trips" "{\"specialServiceTypeId\":$VAGON,\"tripDate\":\"$YESTERDAY\"}")" | jq -e --arg y "$YESTERDAY" '.tripDate==$y and .amount==0 and .rateMissing' >/dev/null || fail "viaje de ayer: se aplica la tarifa vigente en tripDate (ninguna)"
TP0=$(expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trips" "{\"specialServiceTypeId\":$MONTA}")")
echo "$TP0" | jq -e '.amount==0 and .rateMissing and .note=="sin tarifa configurada"' >/dev/null || fail "viaje sin tarifa: $TP0"; TP0_PID=$(echo "$TP0" | jq -r .publicId)
expect 400 "$(req POST "/api/v1/drivers/$DR1_PID/trips" "{\"specialServiceTypeId\":$VAGON,\"tripDate\":\"$(dplus 1)\"}")" | jq -e --arg m "La fecha del viaje no puede ser futura." "$HASM" >/dev/null || fail "viaje futuro"
expect 400 "$(req POST "/api/v1/drivers/$DR1_PID/trips" '{}')" | jq -e --arg m "El tipo de viaje es obligatorio." "$HASM" >/dev/null || fail "tipo de viaje obligatorio"
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trips/$TP0_PID/cancel" '{"comment":"error de captura"}')" | jq -e '.statusCode=="CANCELLED" and .isActive==false' >/dev/null || fail "cancelar viaje"
expect 422 "$(req POST "/api/v1/drivers/$DR1_PID/trips/$TP0_PID/cancel")" >/dev/null
expect 404 "$(req POST "/api/v1/drivers/$DR2_PID/trips/$TP1_PID/cancel")" | jq -e '.title=="Viaje no encontrado."' >/dev/null || fail "viaje de otro chofer"
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/trips?from=$TODAY&to=$TODAY")" | jq -e 'length==2 and ([.[].amount] | sort)==[75,80]' >/dev/null || fail "viajes vigentes"
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/trips?from=$TODAY&to=$TODAY&includeCancelled=true")" | jq -e 'length==3' >/dev/null || fail "viajes con cancelados"
# Hallazgo de revisión: un chofer inactivo (checkbox) sí admite tarifas y viajes; cerrar la tarifa por viaje (409 la segunda vez)
expect 204 "$(req POST "/api/v1/drivers/$DR2_PID/deactivate")" >/dev/null
expect 200 "$(req POST "/api/v1/drivers/$DR2_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"ENVELOPE","rate":1}')" >/dev/null
expect 200 "$(req POST "/api/v1/drivers/$DR2_PID/trips" "{\"specialServiceTypeId\":$VAGON}")" | jq -e '.statusCode=="OPEN"' >/dev/null || fail "viaje de un chofer inactivo"
expect 204 "$(req POST "/api/v1/drivers/$DR2_PID/reactivate")" >/dev/null
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trip-rates/$TR2/close")" | jq -e --arg t "$TODAY" '.effectiveTo==$t' >/dev/null || fail "cerrar la tarifa por viaje"
expect 409 "$(req POST "/api/v1/drivers/$DR1_PID/trip-rates/$TR2/close")" | jq -e '.title=="La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla."' >/dev/null || fail "cerrar dos veces la tarifa por viaje"
expect 200 "$(req POST "/api/v1/drivers/$DR1_PID/trip-rates" "{\"specialServiceTypeId\":$VAGON,\"rate\":80}")" >/dev/null   # se restituye para los pasos siguientes
ok "viaje congelado en 75 aunque la tarifa suba a 80, sin tarifa → 0 con rateMissing, 400 futura/sin tipo, cancelar (CANCELLED + isActive=false, 422 la segunda vez), 404 bajo otro chofer, vigentes 2 / con cancelados 3; tarifa por viaje: editar = cerrar y abrir (historial), 400 en el pasado, cerrar (409 la segunda vez); viaje de ayer sin tarifa (se aplica la vigente en tripDate); chofer inactivo admite tarifas y viajes; tarifa por viaje bajo otro chofer → 404"

step "entrega especial con chofer (Lote 4 sobre el Lote 3)"
SSN=$(expect 200 "$(req POST "/api/v1/clients/$CLIENT_O_PID/special-services" "{\"typeId\":$VAGON,\"rate\":150}")" | jq -r .id)
expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/trip-rates" "{\"specialServiceTypeId\":$VAGON,\"rate\":80}")" >/dev/null
spb() { local x=${1:-}; [[ -n "$x" ]] || x='{}'; ob "$(jq -cn --argjson s "$SSN" --argjson x "$x" '{packages:null,isSpecialDelivery:true,specialServiceId:$s} + $x')"; }
ntrips() { expect 200 "$(req GET "/api/v1/drivers/$1/trips?includeCancelled=true")" | jq length; }
ocount() { expect 200 "$(req GET "/api/v1/orders?clientId=$CLIENT_O_PID&take=1")" | jq .total; }
# Hallazgo de revisión: crédito excedido con chofer → 422 sin orden, sin viaje y sin hueco en la numeración; override 403/200
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":10}')" >/dev/null
LASTS=$(seqof "$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .orderNumber)")
N0=$(ocount); TR0=$(ntrips "$DR3_PID")
expect 422 "$(req POST /api/v1/orders "$(spb "{\"driverPublicId\":\"$DR3_PID\"}")")" | jq -e '.code=="credit_exceeded"' >/dev/null || fail "entrega especial con chofer y crédito excedido"
[[ $(ocount) -eq $N0 ]] || fail "el 422 de crédito dejó una orden"
[[ $(ntrips "$DR3_PID") -eq $TR0 ]] || fail "el 422 de crédito dejó un viaje"
PD0=$(pdtotal)
expect 403 "$(req POST /api/v1/orders "$(spb "{\"driverPublicId\":\"$DR3_PID\",\"overrideCredit\":true}")" "$T2")" | jq -e '.title=="Falta el permiso '"'"'orders.credit_override'"'"'."' >/dev/null || fail "override con chofer sin permiso"
[[ $(pdtotal) -gt $PD0 ]] || fail "PERMISSION_DENIED del override con chofer"
SPO=$(expect 200 "$(req POST /api/v1/orders "$(spb "{\"driverPublicId\":\"$DR3_PID\",\"overrideCredit\":true}")")")
echo "$SPO" | jq -e --arg n "$DR3_NAME" '.status=="IN_TRANSIT" and .assignedDriverName==$n and .quotedAmount==150' >/dev/null || fail "override con chofer: $SPO"
[[ $(seqof "$(echo "$SPO" | jq -r .orderNumber)") -eq $((LASTS + 1)) ]] || fail "el 422 de crédito dejó hueco en la numeración"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$(echo "$SPO" | jq -r .id)")" | jq -e 'map(select((.comment // "") | startswith("Crédito excedido autorizado por"))) | length==1' >/dev/null || fail "comentario del override con chofer"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=ROLE_CHANGE&take=10')" | jq -e '[.items[] | select((.detailJson // "") | contains("credit_override"))] | length >= 1' >/dev/null || fail "SecurityEvent credit_override"
expect 200 "$(req PATCH "/api/v1/clients/$CLIENT_O_PID/profile" '{"creditLimit":100000}')" >/dev/null
# Alta con chofer sin confirmNow: confirma, avanza etapa por etapa hasta IN_TRANSIT y congela el viaje del chofer
SP4=$(expect 200 "$(req POST /api/v1/orders "$(spb "{\"driverPublicId\":\"$DR3_PID\"}")")")
SP4_PID=$(echo "$SP4" | jq -r .publicId); SP4_ID=$(echo "$SP4" | jq -r .id)
echo "$SP4" | jq -e --arg n "$DR3_NAME" --arg c "D3$TS" --arg p "$DR3_PID" '.status=="IN_TRANSIT" and .assignedDriverName==$n and .assignedDriverCode==$c and .assignedDriverPublicId==$p and .quotedAmount==150' >/dev/null || fail "alta con chofer: $SP4"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$SP4_ID")" | jq -e 'sort_by(.id) | map(.toCode)==["DRAFT","CONFIRMED","PICKUP","INBOUND","PLANNED","IN_TRANSIT"]' >/dev/null || fail "historial DRAFT→…→IN_TRANSIT"
expect 200 "$(req GET "/api/v1/drivers/$DR3_PID/trips")" | jq -e --arg o "$SP4_PID" '[.[] | select(.orderPublicId==$o)] | length==1 and .[0].amount==80 and .[0].statusCode=="OPEN"' >/dev/null || fail "viaje de 80 ligado a la orden"
# Rechazos: chofer en orden normal, override sin confirmNow ni chofer, chofer no disponible (sin orden ni hueco)
expect 400 "$(req POST /api/v1/orders "$(ob "{\"driverPublicId\":\"$DR3_PID\"}")")" | jq -e --arg m "El chofer solo se asigna en una entrega especial." "$HASM" >/dev/null || fail "chofer en orden normal"
expect 400 "$(req POST /api/v1/orders "$(spb '{"overrideCredit":true}')")" | jq -e '.errors.overrideCredit' >/dev/null || fail "overrideCredit sin confirmNow ni chofer"
LASTS=$(seqof "$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .orderNumber)"); N0=$(ocount)
expect 409 "$(req POST /api/v1/orders "$(spb "{\"driverPublicId\":\"$DR2_PID\"}")")" | jq -e '.title | startswith("El chofer no está disponible para despacho: ")' >/dev/null || fail "chofer no disponible"
[[ $(ocount) -eq $N0 ]] || fail "el 409 de disponibilidad dejó una orden"
[[ $(seqof "$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .orderNumber)") -eq $((LASTS + 1)) ]] || fail "el 409 de disponibilidad dejó hueco en la numeración"
# Hallazgo de revisión: un chofer inactivo (checkbox) no se asigna a una entrega especial
expect 204 "$(req POST "/api/v1/drivers/$DR3_PID/deactivate")" >/dev/null
expect 409 "$(req POST /api/v1/orders "$(spb "{\"driverPublicId\":\"$DR3_PID\"}")")" | jq -e '.title=="El chofer no está disponible para despacho: El chofer está inactivo."' >/dev/null || fail "entrega especial con chofer inactivo"
expect 204 "$(req POST "/api/v1/drivers/$DR3_PID/reactivate")" >/dev/null
# Hallazgo de revisión: en POST /orders el único guardián de trips.dispatch es el servicio (PrepareAsync): 403 sin orden ni hueco
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Captura $TS\",\"permissions\":[\"orders.view\",\"orders.create\",\"clients.read\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"captura$TS@teikem.local\",\"fullName\":\"Captura\",\"password\":\"$PASS\",\"roles\":[\"Captura $TS\"]}")" >/dev/null
TCAP=$(login "captura$TS@teikem.local" "$PASS")
LASTS=$(seqof "$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .orderNumber)"); N0=$(ocount)
expect 403 "$(req POST /api/v1/orders "$(spb "{\"driverPublicId\":\"$DR1_PID\"}")" "$TCAP")" | jq -e '.title=="Falta el permiso '"'"'trips.dispatch'"'"'."' >/dev/null || fail "alta con chofer sin trips.dispatch"
[[ $(ocount) -eq $N0 ]] || fail "el 403 de trips.dispatch dejó una orden"
[[ $(seqof "$(expect 200 "$(req POST /api/v1/orders "$(ob)")" | jq -r .orderNumber)") -eq $((LASTS + 1)) ]] || fail "el 403 de trips.dispatch dejó hueco en la numeración"
# Hallazgo de revisión: rowVersion obsoleto en la asignación → 409 y la orden sigue con su chofer
expect 409 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DR1_PID\",\"rowVersion\":\"AAAAAAAAAAA=\"}")" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "rowVersion obsoleto en la asignación"
expect 200 "$(req GET "/api/v1/orders/$SP4_PID")" | jq -e --arg p "$DR3_PID" '.assignedDriverPublicId==$p' >/dev/null || fail "el 409 de rowVersion cambió el chofer"
# Reasignación
expect 200 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DR1_PID\"}")" | jq -e --arg p "$DR1_PID" '.assignedDriverPublicId==$p and .status=="IN_TRANSIT"' >/dev/null || fail "reasignar a D1"
expect 200 "$(req GET "/api/v1/drivers/$DR3_PID/trips?includeCancelled=true")" | jq -e --arg o "$SP4_PID" '[.[] | select(.orderPublicId==$o)] | length==1 and .[0].statusCode=="CANCELLED" and .[0].isActive==false' >/dev/null || fail "el viaje de D3 queda cancelado"
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/trips")" | jq -e --arg o "$SP4_PID" '[.[] | select(.orderPublicId==$o)] | length==1 and .[0].statusCode=="OPEN"' >/dev/null || fail "un único viaje vigente, de D1"
DR1_TRIP=$(expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/trips")" | jq -r --arg o "$SP4_PID" '[.[] | select(.orderPublicId==$o)][0].publicId')
expect 409 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DR1_PID\"}")" | jq -e '.title=="La orden ya está asignada a ese chofer."' >/dev/null || fail "mismo chofer"
expect 422 "$(req POST "/api/v1/orders/$O1_PID/driver" "{\"driverPublicId\":\"$DR1_PID\"}")" >/dev/null   # orden normal
expect 403 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DR3_PID\"}" "$T7")" >/dev/null   # sin trips.dispatch
expect 404 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DT3_PID\"}")" | jq -e '.title=="Chofer no encontrado."' >/dev/null || fail "chofer de otro tenant"
expect 404 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DT3_PID\"}" "$T3")" >/dev/null   # orden de otro tenant
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken)
expect 200 "$(req PUT /api/v1/modules/CATALOG '{"isEnabled":false}')" >/dev/null
expect 403 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DR3_PID\"}")" | jq -e '.code=="module_disabled"' >/dev/null || fail "asignar chofer con CATALOG apagado"
expect 403 "$(req GET /api/v1/vehicles)" | jq -e '.code=="module_disabled"' >/dev/null || fail "flota con CATALOG apagado"
for P in /api/v1/driver-pay-policy "/api/v1/drivers/$DR1_PID/trips" /api/v1/maintenance-schedules /api/v1/fuel-logs /api/v1/dispatch-zones; do
  expect 403 "$(req GET "$P")" | jq -e '.code=="module_disabled"' >/dev/null || fail "$P con CATALOG apagado"
done
expect 200 "$(req PUT /api/v1/modules/CATALOG '{"isEnabled":true}')" >/dev/null
# Hallazgo de revisión: en un lateral (o después de IN_TRANSIT) no se asigna ni reasigna; desde DRAFT se confirma y avanza
expect 200 "$(req POST "/api/v1/orders/$SP4_PID/status" '{"toCode":"ON_HOLD","comment":"smoke"}')" | jq -e '.status=="ON_HOLD"' >/dev/null || fail "entrega especial en ON_HOLD"
expect 422 "$(req POST "/api/v1/orders/$SP4_PID/driver" "{\"driverPublicId\":\"$DR3_PID\"}")" | jq -e '.title=="La entrega especial ya llegó a destino o terminó; no se puede asignar ni reasignar el chofer."' >/dev/null || fail "asignar chofer fuera del pipeline"
expect 200 "$(req POST "/api/v1/orders/$SP4_PID/status" '{"toCode":"IN_TRANSIT"}')" | jq -e '.status=="IN_TRANSIT"' >/dev/null || fail "regreso a IN_TRANSIT"
SP5=$(expect 200 "$(req POST /api/v1/orders "$(spb)")"); SP5_PID=$(echo "$SP5" | jq -r .publicId); SP5_ID=$(echo "$SP5" | jq -r .id)
echo "$SP5" | jq -e '.status=="DRAFT"' >/dev/null || fail "entrega especial sin chofer queda en DRAFT"
expect 403 "$(req POST "/api/v1/orders/$SP5_PID/driver" "{\"driverPublicId\":\"$DR1_PID\",\"overrideCredit\":true}" "$T2")" | jq -e '.title=="Falta el permiso '"'"'orders.credit_override'"'"'."' >/dev/null || fail "asignar con overrideCredit sin permiso"
expect 200 "$(req POST "/api/v1/orders/$SP5_PID/driver" "{\"driverPublicId\":\"$DR1_PID\"}")" | jq -e --arg p "$DR1_PID" '.status=="IN_TRANSIT" and .assignedDriverPublicId==$p' >/dev/null || fail "asignar chofer desde DRAFT"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$SP5_ID")" | jq -e 'sort_by(.id) | map(.toCode)==["DRAFT","CONFIRMED","PICKUP","INBOUND","PLANNED","IN_TRANSIT"]' >/dev/null || fail "historial DRAFT→…→IN_TRANSIT desde el endpoint de asignación"
# Cancelaciones: el viaje de una orden no se cancela directo; cancelar la orden cancela su viaje OPEN
expect 409 "$(req POST "/api/v1/drivers/$DR1_PID/trips/$DR1_TRIP/cancel")" | jq -e '.title=="El viaje nace de una entrega especial; cancele la orden o reasigne el chofer."' >/dev/null || fail "cancelar directo el viaje de una orden"
expect 200 "$(req PUT '/api/v1/status/lateral-entries/TRANSPORT_ORDER?statusDomain=OrderStatus' '[{"lateralStatusCode":"CANCELLED","fromStatusCode":"PICKUP","isAllowed":true},{"lateralStatusCode":"CANCELLED","fromStatusCode":"DRAFT","isAllowed":true},{"lateralStatusCode":"CANCELLED","fromStatusCode":"CONFIRMED","isAllowed":true},{"lateralStatusCode":"CANCELLED","fromStatusCode":"IN_TRANSIT","isAllowed":true}]')" >/dev/null
expect 200 "$(req POST "/api/v1/orders/$SP4_PID/cancel" '{"comment":"cliente canceló"}')" | jq -e '.status=="CANCELLED"' >/dev/null || fail "cancelar la entrega especial"
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/trips?includeCancelled=true")" | jq -e --arg o "$SP4_PID" '[.[] | select(.orderPublicId==$o)] | length==1 and .[0].statusCode=="CANCELLED" and .[0].isActive==false' >/dev/null || fail "cancelar la orden cancela su viaje"
expect 200 "$(req GET "/api/v1/orders/$SP4_PID")" | jq -e '.assignedDriverPublicId==null' >/dev/null || fail "la orden cancelada queda sin chofer vigente"
ok "crédito excedido 422 (sin orden, viaje ni hueco), override 403/200 con bitácora, alta con chofer hasta IN_TRANSIT (5 pasos) con viaje de 80, 400/409 sin orden ni hueco, 409 con chofer inactivo (checkbox), reasignación (cancela el viaje anterior, un solo vigente), 409/422/403/404, 422 en lateral, asignación desde DRAFT (override 403, confirma y avanza 5 pasos), CATALOG apagado 403, cancelar orden → cancela su viaje; alta con chofer sin trips.dispatch 403 (sin orden ni hueco); asignación con rowVersion obsoleto 409; CATALOG apagado 403 también en política de pago, viajes, programas, combustible y zonas"

step "eliminar chofer (Lote 4): baja definitiva sin borrar historial"
DDR3=$(expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"BOX","rate":5}')" | jq -r .id)
expect 200 "$(req POST "/api/v1/drivers/$DR3_PID/delivery-rates/$DDR3/close" "{\"effectiveTo\":\"$(dplus 10)\"}")" | jq -e --arg d "$(dplus 10)" '.effectiveTo==$d' >/dev/null || fail "cerrar a futuro"
# Hallazgo de revisión: D3 con usuario vinculado y tarifa por intento, para comprobar la desvinculación y el cierre de intentos
expect 200 "$(req POST /api/v1/users "{\"email\":\"chofer3$TS@teikem.local\",\"fullName\":\"Chofer 3 $TS\",\"password\":\"$PASS\",\"roles\":[\"Driver\"]}")" >/dev/null
UC3=$(expect 200 "$(req GET /api/v1/me '' "$(login "chofer3$TS@teikem.local" "$PASS")")" | jq -r .userId)
expect 200 "$(req PATCH "/api/v1/drivers/$DR3_PID" "{\"userId\":$UC3}")" | jq -e --arg e "chofer3$TS@teikem.local" '.user.email==$e' >/dev/null || fail "vincular usuario a D3"
expect 200 "$(req PUT "/api/v1/drivers/$DR3_PID/attempt-rates/1" '{"rate":1.25}')" >/dev/null
DR3_SPO_TRIP=$(expect 200 "$(req GET "/api/v1/drivers/$DR3_PID/trips")" | jq -r --arg o "$(echo "$SPO" | jq -r .publicId)" '[.[] | select(.orderPublicId==$o)][0].publicId')
[[ -n "$DR3_SPO_TRIP" && "$DR3_SPO_TRIP" != "null" ]] || fail "viaje vigente de D3 (orden SPO)"
expect 204 "$(req DELETE "/api/v1/drivers/$DR3_PID" '{"comment":"baja smoke"}')" >/dev/null
expect 200 "$(req GET "/api/v1/drivers/$DR3_PID")" | jq -e '.statusCode=="INACTIVE" and .isActive==false and .isTerminal and .zoneCode==null and .user==null' >/dev/null || fail "chofer eliminado"
expect 200 "$(req GET "/api/v1/drivers/$DR3_PID/rates")" | jq -e '(.deliveryRates | length)==0 and (.tripRates | length)==0 and all(.attemptRates[]; .id==null or (.isCurrent | not))' >/dev/null || fail "sin tarifas vigentes"
expect 200 "$(req GET "/api/v1/drivers/$DR3_PID/rates?asOf=$(dplus 1)")" | jq -e '(.deliveryRates | length)==0' >/dev/null || fail "la tarifa cerrada a futuro sigue vigente mañana"
expect 200 "$(req GET "/api/v1/drivers/$DR3_PID/rates?includeHistory=true")" | jq -e --arg t "$TODAY" --argjson d "$DDR3" '(.deliveryRates[] | select(.id==$d) | .effectiveTo==$t) and all(.tripRates[]; .effectiveTo==$t) and any(.attemptRates[]; .attemptNumber==1 and .id!=null and .effectiveTo==$t)' >/dev/null || fail "tarifas (entrega, viaje e intento) cerradas hoy"
expect 200 "$(req GET "/api/v1/drivers/$DR3_PID/trips?includeCancelled=true")" | jq -e 'length >= 2' >/dev/null || fail "los viajes del chofer eliminado siguen visibles"
expect 409 "$(req POST "/api/v1/drivers/$DR3_PID/reactivate")" | jq -e '.title=="El chofer fue eliminado; no se puede reactivar."' >/dev/null || fail "reactivar un chofer eliminado"
expect 409 "$(req POST "/api/v1/drivers/$DR3_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"ENVELOPE","rate":1}')" | jq -e '.title=="El chofer fue eliminado; sus tarifas y viajes solo se consultan."' >/dev/null || fail "tarifa a un chofer eliminado"
# Hallazgo de revisión: viajes de un chofer eliminado (alta y cancelación → 409), su cuenta queda libre y sus documentos salen del panel
NT3=$(ntrips "$DR3_PID")
expect 409 "$(req POST "/api/v1/drivers/$DR3_PID/trips" "{\"specialServiceTypeId\":$VAGON}")" | jq -e '.title=="El chofer fue eliminado; sus tarifas y viajes solo se consultan."' >/dev/null || fail "viaje a un chofer eliminado"
expect 409 "$(req POST "/api/v1/drivers/$DR3_PID/trips/$DR3_SPO_TRIP/cancel")" | jq -e '.title=="El chofer fue eliminado; sus tarifas y viajes solo se consultan."' >/dev/null || fail "cancelar viaje de un chofer eliminado"
[[ $(ntrips "$DR3_PID") -eq $NT3 ]] || fail "los 409 del chofer eliminado cambiaron sus viajes"
expect 200 "$(req PATCH "/api/v1/drivers/$DR2_PID" "{\"userId\":$UC3}")" | jq -e --arg e "chofer3$TS@teikem.local" '.user.email==$e' >/dev/null || fail "la cuenta del chofer eliminado queda libre"
expect 200 "$(req PATCH "/api/v1/drivers/$DR2_PID" '{"clearUser":true}')" | jq -e '.user==null' >/dev/null || fail "desvincular la cuenta de D2"
expect 200 "$(req GET '/api/v1/fleet/expiring-documents?withinDays=365')" | jq -e --arg d "D3$TS" 'all(.[]; .ownerCode!=$d)' >/dev/null || fail "documento de un chofer eliminado en el panel"
expect 200 "$(req GET "/api/v1/dispatch-zones")" | jq -e --argjson z "$ZN_ID" '.[] | select(.id==$z) | .driverCount==1' >/dev/null || fail "la zona pierde al chofer eliminado"
ok "DELETE → INACTIVE sin zona ni usuario (la cuenta queda libre), tarifas de entrega/viaje/intento abiertas y cerradas a futuro quedan cerradas hoy, viajes visibles, 409 al reactivar, al tarifar y al crear o cancelar viajes, sus documentos salen del panel"

step "RBAC y módulo (Lote 4)"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Solo flota $TS\",\"permissions\":[\"fleet.view\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"soloflota$TS@teikem.local\",\"fullName\":\"Solo flota\",\"password\":\"$PASS\",\"roles\":[\"Solo flota $TS\"]}")" >/dev/null
T9=$(login "soloflota$TS@teikem.local" "$PASS")
for P in /api/v1/vehicles /api/v1/drivers /api/v1/fleet/availability /api/v1/fleet/expiring-documents /api/v1/maintenance-work-orders /api/v1/fuel-logs /api/v1/maintenance-schedules/due; do
  expect 200 "$(req GET "$P" '' "$T9")" >/dev/null
done
PD0=$(pdtotal)
expect 403 "$(req POST /api/v1/vehicles "$(vb "VR$TS")" "$T9")" >/dev/null
[[ $(pdtotal) -gt $PD0 ]] || fail "PERMISSION_DENIED en flota"
expect 403 "$(req GET "/api/v1/drivers/$DR1_PID/rates" '' "$T9")" >/dev/null   # [RequirePermission(driverpay.view)]
expect 403 "$(req GET "/api/v1/status/history/DRIVER_TRIP/$TP1_ID" '' "$T9")" >/dev/null
expect 403 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VH3_PID\",\"maintenanceType\":\"CORRECTIVE\"}" "$T8")" >/dev/null
expect 403 "$(req POST /api/v1/fuel-logs "$(fl "$(mago 5)" 7000)" "$T8")" >/dev/null
expect 200 "$(req GET /api/v1/drivers '' "$T2")" >/dev/null
expect 403 "$(req POST "/api/v1/drivers/$DR1_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"ENVELOPE","rate":1}' "$TBILL")" >/dev/null
expect 200 "$(req GET "/api/v1/drivers/$DR1_PID/rates" '' "$TBILL")" >/dev/null
# Hallazgo de revisión: driverpay.view sin driverpay.manage no cambia la política ni los viajes; fleet.manage sin fleet.maintenance no crea programas
expect 403 "$(req PATCH /api/v1/driver-pay-policy '{"payoutFormula":"DELIVERY_PLUS_ATTEMPTS"}' "$TBILL")" >/dev/null
expect 403 "$(req POST /api/v1/driver-pay-policy/attempt-levels '' "$TBILL")" >/dev/null
expect 200 "$(req GET /api/v1/driver-pay-policy '' "$TBILL")" >/dev/null
expect 403 "$(req POST "/api/v1/drivers/$DR1_PID/trips" "{\"specialServiceTypeId\":$VAGON}" "$TBILL")" >/dev/null
expect 403 "$(req POST "/api/v1/drivers/$DR1_PID/trips/$TP1_PID/cancel" '' "$TBILL")" >/dev/null
expect 403 "$(req POST /api/v1/maintenance-schedules "{\"name\":\"X $TS\",\"vehiclePublicId\":\"$VH1_PID\",\"trigger\":\"MILEAGE\",\"intervalKm\":5000}" "$T8")" >/dev/null
ok "solo fleet.view: lectura 200, escritura 403 (PERMISSION_DENIED), tarifas e historial de viajes 403; fleet.manage sin fleet.maintenance: OT/combustible 403; Despachador lee choferes; Facturación lee tarifas pero no las cambia; Facturación (driverpay.view) no cambia la política ni crea o cancela viajes (403); fleet.manage sin fleet.maintenance tampoco crea programas"

step "aislamiento entre tenants y BOLA por id hijo (Lote 4)"
expect 404 "$(req GET "/api/v1/vehicles/$VH1_PID" '' "$T3")" >/dev/null
expect 404 "$(req GET "/api/v1/drivers/$DR1_PID" '' "$T3")" >/dev/null
expect 404 "$(req GET "/api/v1/drivers/$DR1_PID/rates" '' "$T3")" >/dev/null
expect 404 "$(req GET "/api/v1/maintenance-work-orders/$WO1_PID" '' "$T3")" >/dev/null
expect 200 "$(req GET /api/v1/vehicles '' "$T3")" | jq -e 'length==0' >/dev/null || fail "vehículos de otro tenant visibles"
expect 200 "$(req GET /api/v1/drivers '' "$T3")" | jq -e --arg p "$DT3_PID" 'all(.[]; .publicId==$p)' >/dev/null || fail "choferes de otro tenant visibles"
expect 404 "$(req POST "/api/v1/contacts/DRIVER/$DR1_ID" '{"contactType":"PHONE","value":"787-555-0100"}' "$T3")" >/dev/null
expect 200 "$(req GET /api/v1/fleet/expiring-documents '' "$T3")" | jq -e 'length==0' >/dev/null || fail "documentos de otro tenant visibles"
expect 200 "$(req GET /api/v1/fleet/availability '' "$T3")" | jq -e --arg p "$DT3_PID" '(.vehicles | length)==0 and all(.drivers[]; .publicId==$p)' >/dev/null || fail "disponibilidad con datos ajenos"
expect 200 "$(req GET "/api/v1/fuel-logs" '' "$T3")" | jq -e '.total==0' >/dev/null || fail "combustible de otro tenant visible"
# Hallazgo de revisión: recursos con id entero sin padre (carga, programa, zona) → su única barrera es el filtro de tenant
expect 404 "$(req PATCH "/api/v1/fuel-logs/$F1" '{"station":"x"}' "$T3")" | jq -e '.title=="Carga de combustible no encontrada."' >/dev/null || fail "PATCH de carga de otro tenant"
expect 404 "$(req POST "/api/v1/fuel-logs/$F1/deactivate" '' "$T3")" | jq -e '.title=="Carga de combustible no encontrada."' >/dev/null || fail "desactivar carga de otro tenant"
expect 404 "$(req PATCH "/api/v1/maintenance-schedules/$SCH1" '{"name":"x"}' "$T3")" | jq -e '.title=="Programa de mantenimiento no encontrado."' >/dev/null || fail "PATCH de programa de otro tenant"
expect 404 "$(req POST "/api/v1/maintenance-schedules/$SCH1/deactivate" '' "$T3")" >/dev/null
expect 404 "$(req PATCH "/api/v1/dispatch-zones/$ZN_ID" '{"name":"x"}' "$T3")" | jq -e '.title=="Zona de despacho no encontrada."' >/dev/null || fail "PATCH de zona de otro tenant"
expect 404 "$(req POST "/api/v1/dispatch-zones/$ZN_ID/deactivate" '' "$T3")" >/dev/null
expect 200 "$(req GET "/api/v1/fuel-logs?vehiclePublicId=$VH1_PID")" | jq -e --argjson f "$F1" 'any(.items[]; .id==$f and .isActive and .station=="Puma")' >/dev/null || fail "la carga del demo cambió desde otro tenant"
expect 200 "$(req GET /api/v1/maintenance-schedules)" | jq -e --argjson s "$SCH1" --arg n "Aceite 5000 $TS" 'any(.[]; .id==$s and .isActive and .name==$n)' >/dev/null || fail "el programa del demo cambió desde otro tenant"
expect 200 "$(req GET /api/v1/dispatch-zones)" | jq -e --argjson z "$ZN_ID" 'any(.[]; .id==$z and .isActive and .name=="Toa Baja · Bayamón")' >/dev/null || fail "la zona del demo cambió desde otro tenant"
# T3 crea sus propios hijos; el admin del demo los intenta alcanzar bajo SUS padres → 404
VT3_PID=$(expect 200 "$(req POST /api/v1/vehicles "$(vb "VT$TS")" "$T3")" | jq -r .publicId)
DOCT3=$(expect 200 "$(req POST "/api/v1/vehicles/$VT3_PID/documents" "{\"docType\":\"INSURANCE\",\"expiryDate\":\"$(dplus 100)\"}" "$T3")" | jq -r .id)
RT3=$(expect 200 "$(req POST "/api/v1/drivers/$DT3_PID/delivery-rates" '{"serviceType":"STANDARD","packageType":"BOX","rate":2}' "$T3")" | jq -r .id)
WT3_PID=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$VT3_PID\",\"maintenanceType\":\"CORRECTIVE\"}" "$T3")" | jq -r .publicId)
TKT3=$(expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WT3_PID/tasks" '{"description":"Frenos"}' "$T3")" | jq -r '.tasks[0].id')
expect 404 "$(req PATCH "/api/v1/vehicles/$VH1_PID/documents/$DOCT3" '{"docNumber":"x"}')" >/dev/null
expect 404 "$(req PATCH "/api/v1/drivers/$DR1_PID/delivery-rates/$RT3" '{"rate":9}')" >/dev/null
expect 404 "$(req PATCH "/api/v1/maintenance-work-orders/$WO8_PID/tasks/$TKT3" '{"description":"x"}')" | jq -e '.title=="Tarea no encontrada."' >/dev/null || fail "tarea de otro tenant"
expect 404 "$(req GET "/api/v1/vehicles/$VT3_PID")" >/dev/null
expect 200 "$(req GET "/api/v1/maintenance-work-orders/$WT3_PID" '' "$T3")" | jq -e '(.tasks[0].description)=="Frenos"' >/dev/null || fail "la tarea de T3 no debía cambiar"
ok "otro tenant: 404 por PublicId (vehículo, chofer, tarifas, OT, contactos), listas/panel/disponibilidad/combustible sin datos ajenos; carga, programa y zona del demo por id entero (PATCH y desactivar) → 404 sin cambios; ids hijos de T3 (documento, tarifa, tarea) bajo padres del demo → 404"

step "fuentes de datos, contenido de sistema y auditoría (Lote 4)"
DS=$(expect 200 "$(req GET /api/v1/analytics/data-sources)")
echo "$DS" | jq -e 'map(.key) as $k | (["VEHICLE","DRIVER","WORK_ORDER","FUEL_LOG","FLEET_DOCUMENT"] | all(.[]; . as $x | $k | index($x))) and ($k | index("DRIVER_TRIP") | not) and ($k | index("DRIVER_RATE") | not)' >/dev/null || fail "fuentes de datos de flota"
expect 200 "$(req POST '/api/v1/analytics/reports/FUEL_LOG/preview?dateRangeMode=ALL' '{"name":"x","columns":["VehicleCode","DriverName","OdometerKm","KmPerLiter","CostPerKm"]}')" | jq -e --arg v "V$TS" 'any(.rows[]; .VehicleCode==$v and .KmPerLiter==10)' >/dev/null || fail "preview FUEL_LOG con KmPerLiter"
expect 200 "$(req POST '/api/v1/analytics/reports/WORK_ORDER/preview?dateRangeMode=ALL' '{"name":"x","columns":["Number","Status","TotalCost","Vehicle.Code"],"secondary":["Vehicle"]}')" | jq -e --arg n "$WO1_NUM" 'any(.rows[]; .Number==$n and .TotalCost==90)' >/dev/null || fail "preview WORK_ORDER con Vehicle"
expect 200 "$(req POST '/api/v1/analytics/reports/DRIVER/preview?dateRangeMode=ALL' "$(jq -cn --arg c "D1$TS" '{name:"x",columns:["Code","ZoneCode","Area","EffectiveMaxStops","LicenseExpiry","HasUser"],filterJson:({field:"Code",op:"eq",value:$c} | tojson)}')")" | jq -e --arg c "D1$TS" --arg z "Z$TS" --arg d "$(dplus 5)" 'any(.rows[]; .Code==$c and .ZoneCode==$z and .Area=="Toa Baja · Bayamón" and .EffectiveMaxStops==18 and (.LicenseExpiry|tostring|startswith($d)) and .HasUser==true)' >/dev/null || fail "preview DRIVER (zona, tope efectivo, licencia vigente)"
PU=$(expect 200 "$(req GET /api/v1/analytics/pulse)")
echo "$PU" | jq -e '([.indicators[] | select(.name=="Documentos por vencer" and .value >= 3)] | length)==1 and ([.indicators[] | select(.name=="Órdenes de trabajo abiertas")] | length)==1' >/dev/null || fail "indicadores de flota en Pulso: $(echo "$PU" | jq -c '[.indicators[] | {name,value}]')"
RV=$(expect 200 "$(req GET /api/v1/analytics/reports)" | jq -r '[.[] | select(.name=="Vehículos" and .isSystem==true)][0].id')
expect 200 "$(req POST "/api/v1/analytics/reports/$RV/run" '{}')" | jq -e --arg v "V$TS" 'any(.rows[]; .Code==$v)' >/dev/null || fail "vista Vehículos"
RD=$(expect 200 "$(req GET /api/v1/analytics/reports)" | jq -r '[.[] | select(.name=="Documentos por vencer" and .isSystem==true)][0].id')
RDR=$(expect 200 "$(req POST "/api/v1/analytics/reports/$RD/run" '{}')")
echo "$RDR" | jq -e --arg s "SEG-$TS" --arg o "MAR-OLD-$TS" 'any(.rows[]; .DocNumber==$s) and all(.rows[]; .DocNumber!=$o)' >/dev/null || fail "vista Documentos por vencer (sin el superado)"
for ET in VEHICLE DRIVER DRIVER_RATE DRIVER_TRIP WORK_ORDER; do
  expect 200 "$(req GET "/api/v1/audit/changes?entityType=$ET&take=50")" | jq -e '.total >= 1 and ([.items[] | select((.changesJson // "") | ascii_downcase | (contains("rowversion") or contains("pushtoken")))] | length)==0' >/dev/null || fail "auditoría de $ET"
done
ok "VEHICLE/DRIVER/WORK_ORDER/FUEL_LOG/FLEET_DOCUMENT (sin DRIVER_TRIP/DRIVER_RATE), preview con km/L y join a Vehicle, Pulso, vistas de sistema (sin el documento superado) y AuditLog sin rowVersion ni pushToken; preview de DRIVER con zona, área, tope efectivo 18, licencia vigente y usuario"

# ============================================================================================================
# Lote 5 — Trips y rutas. Re-ejecutable: TS en códigos de zonas, choferes y vehículos y en los pueblos de los
# consignatarios (las zonas del bloque resuelven por MUNICIPIO 'Pueblo X $TS', único por corrida; los criterios por código
# postal se prueban y se quitan en el mismo paso). Las fechas salen de TODAY (UTC). Reutiliza: T2 (Despachador), T3 (otro
# tenant), T7 (contacts.manage sin trips.*), TBILL (Facturación) y 'lectura$TS' (Solo lectura). Todo lo que el bloque
# cambia de la configuración del tenant (capacidades ASSIGN_TRIP/EDIT_TRIP, etapa DISPATCHED, estatus del chofer, módulo
# CATALOG) se restaura en el mismo paso.
# ============================================================================================================
T2=$(login "$DISPATCH_EMAIL" "$PASS"); T3=$(login "admin$TS@smoke.local" "Smoke_Admin_2026!"); T7=$(login "contactos$TS@teikem.local" "$PASS")
TBILL=$(login "facturacion$TS@teikem.local" "$PASS"); TREAD=$(login "lectura$TS@teikem.local" "$PASS")
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken)
TOMORROW=$(dplus 1)
tpost() { req POST /api/v1/trips "$(jq -cn --arg d "$TODAY" --argjson x "${1:-{\}}" '{planDate:$d} + $x')" "${2:-$TOKEN}"; }   # alta de ruta (hoy + campos)
trip() { expect 200 "$(req GET "/api/v1/trips/$1" '' "${2:-$TOKEN}")"; }                                                           # ficha
rv() { trip "$1" | jq -r .rowVersion; }
stopof() { trip "$1" | jq -r --arg o "$2" '.stops[] | select(.orderPublicId==$o) | .id'; }                                        # RouteStopId de una orden en la ruta
ostatus() { expect 200 "$(req GET "/api/v1/orders/$1")" | jq -r .status; }
scan() { req POST /api/v1/scan/outbound "$(jq -cn --arg c "$1" --arg d "$TODAY" '{code:$c,planDate:$d}')" "${2:-$TOKEN}"; }
plan() { req POST /api/v1/trips/plan-day "$1" "${2:-$TOKEN}"; }

step "trips (Lote 5): permisos, catálogos, pipelines, capacidades y entradas laterales"
expect 200 "$(req GET /api/v1/me)" | jq -e '.permissions as $p | ["trips.view","trips.plan","trips.optimize","trips.dispatch","trips.scan"] | all(.[]; . as $x | ($p | index($x)) != null)' >/dev/null || fail "admin sin permisos del Lote 5"
expect 200 "$(req GET /api/v1/me '' "$T2")" | jq -e '.permissions as $p | ["trips.view","trips.plan","trips.optimize","trips.dispatch","trips.scan"] | all(.[]; . as $x | ($p | index($x)) != null)' >/dev/null || fail "Despachador sin trips.*"
expect 200 "$(req GET /api/v1/me '' "$TREAD")" | jq -e '(.permissions | index("trips.view")) != null and (.permissions | index("trips.plan")) == null and (.permissions | index("trips.scan")) == null' >/dev/null || fail "Solo lectura: trips.view sí, trips.plan/scan no"
expect 200 "$(req POST /api/v1/users "{\"email\":\"bodega$TS@teikem.local\",\"fullName\":\"Operador de almacén $TS\",\"password\":\"$PASS\",\"roles\":[\"WarehouseOperator\"]}")" >/dev/null
TWH=$(login "bodega$TS@teikem.local" "$PASS")
expect 200 "$(req GET /api/v1/me '' "$TWH")" | jq -e '(.permissions | index("trips.view")) != null and (.permissions | index("trips.scan")) != null and (.permissions | index("trips.plan")) == null' >/dev/null || fail "Operador de almacén: trips.view y trips.scan sin trips.plan"
for D in TripStatus RouteStatus RouteStopStatus OptimizationRunStatus; do expect 200 "$(req GET "/api/v1/status/$D")" | jq -e 'length >= 3' >/dev/null || fail "pipeline $D"; done
expect 200 "$(req GET /api/v1/catalogs/OptimizerEngine)" | jq -e 'any(.[]; .code=="HEURISTIC")' >/dev/null || fail "motor HEURISTIC sembrado"
expect 200 "$(req GET /api/v1/status/capabilities/TRIP)" | jq -e 'any(.[]; .capability=="EDIT_TRIP" and .statusCode=="DISPATCHED" and .isAllowed==false)' >/dev/null || fail "EDIT_TRIP negada en DISPATCHED"
expect 200 "$(req GET /api/v1/status/lateral-entries/TRIP)" | jq -e '([.[] | select(.lateralStatusCode=="CANCELLED" and .isAllowed) | .fromStatusCode] | sort) == ["DRAFT","PLANNED"]' >/dev/null || fail "laterales de TRIP (CANCELLED desde DRAFT y PLANNED)"
ok "permisos (admin, Despachador, Solo lectura, Operador de almacén), 4 pipelines, motor HEURISTIC, EDIT_TRIP negada desde DISPATCHED y 'Eliminar ruta' solo desde DRAFT/PLANNED"

step "zonas (Lote 5): miembros por código postal, rango y municipio, sin solapamiento, y resolución ZIP/pueblo → zona"
zone() { expect 200 "$(req POST /api/v1/dispatch-zones "{\"code\":\"$1\",\"name\":\"$2\"}")" | jq -r .id; }
member() { req POST "/api/v1/dispatch-zones/$1/members" "{\"matchType\":\"$2\",\"matchValue\":\"$3\"}" "${4:-$TOKEN}"; }
Z1=$(zone "Z1$TS" "Zona 1"); Z2=$(zone "Z2$TS" "Zona 2"); Z3=$(zone "Z3$TS" "Zona 3"); Z4=$(zone "Z4$TS" "Zona 4"); Z5=$(zone "Z5$TS" "Zona 5"); Z6=$(zone "Z6$TS" "Zona 6")
for i in 1 2 3 4 5 6; do eval "expect 200 \"\$(member \$Z$i MUNICIPALITY 'Pueblo $i $TS')\"" >/dev/null; done
ZB=$((10000 + (TS % 8000) * 10)); ZIP1=$(printf '%05d' "$ZB"); ZIP2=$(printf '%05d' $((ZB + 1))); ZR="$(printf '%05d' $((ZB + 2)))-$(printf '%05d' $((ZB + 9)))"
M1=$(expect 200 "$(member "$Z1" POSTAL_CODE "$ZIP1")" | jq -r --arg v "$ZIP1" '.members[] | select(.matchValue==$v) | .id')
expect 200 "$(member "$Z1" POSTAL_CODE "$ZIP2-1234")" | jq -e --arg v "$ZIP2" 'any(.members[]; .matchTypeCode=="POSTAL_CODE" and .matchValue==$v)' >/dev/null || fail "ZIP+4 se guarda normalizado a 5 dígitos"
M3=$(expect 200 "$(member "$Z2" POSTAL_RANGE "$ZR")" | jq -r '.members[] | select(.matchTypeCode=="POSTAL_RANGE") | .id')
expect 400 "$(member "$Z1" POSTAL_CODE "123")" | jq -e --arg m "El código postal debe tener 5 dígitos (ej. 00949)." "$HASM" >/dev/null || fail "código postal inválido"
expect 400 "$(member "$Z1" POLYGON "x")" | jq -e --arg m "Las zonas por polígono todavía no se soportan; use código postal, rango postal o municipio." "$HASM" >/dev/null || fail "POLYGON"
expect 400 "$(member "$Z1" FOO "x")" | jq -e --arg m "Criterio de zona desconocido: 'FOO'." "$HASM" >/dev/null || fail "criterio desconocido"
expect 409 "$(member "$Z2" POSTAL_CODE "$ZIP1")" | jq -e --arg m "El valor '$ZIP1' ya pertenece a la zona Z1$TS." '.title==$m' >/dev/null || fail "CP de otra zona activa"
expect 409 "$(member "$Z1" POSTAL_CODE "$ZIP1")" | jq -e '.title=="La zona ya tiene ese criterio."' >/dev/null || fail "criterio repetido en la zona"
expect 409 "$(member "$Z3" MUNICIPALITY "PUEBLO 1 $TS")" >/dev/null   # municipio de otra zona activa (sin distinguir mayúsculas)
res() { expect 200 "$(req GET "/api/v1/dispatch-zones/resolve?$1")"; }
res "postalCode=$ZIP1" | jq -e --arg z "Z1$TS" '.zoneCode==$z and .matchedBy=="POSTAL_CODE" and (.ambiguous|not)' >/dev/null || fail "resolver por CP"
res "postalCode=$ZIP2-9999" | jq -e --arg z "Z1$TS" '.zoneCode==$z' >/dev/null || fail "resolver ZIP+4"
res "postalCode=$(printf '%05d' $((ZB + 5)))" | jq -e --arg z "Z2$TS" '.zoneCode==$z and .matchedBy=="POSTAL_RANGE"' >/dev/null || fail "resolver por rango"
res "city=pueblo%203%20$TS" | jq -e --arg z "Z3$TS" '.zoneCode==$z and .matchedBy=="MUNICIPALITY"' >/dev/null || fail "resolver por municipio en minúsculas"
res "postalCode=$ZIP1&city=Pueblo%203%20$TS" | jq -e --arg z "Z1$TS" '.zoneCode==$z' >/dev/null || fail "precedencia CP > municipio"
res "postalCode=99999" | jq -e '.dispatchZoneId==null and .zoneCode==null' >/dev/null || fail "CP sin zona"
expect 400 "$(req GET /api/v1/dispatch-zones/resolve)" | jq -e '.title=="Indique el código postal o el pueblo."' >/dev/null || fail "resolver sin parámetros"
expect 200 "$(req GET "/api/v1/dispatch-zones/$Z1/members" '' "$T2")" | jq -e '(.members | length)==3' >/dev/null || fail "miembros de la zona"
expect 404 "$(req DELETE "/api/v1/dispatch-zones/$Z2/members/$M1")" | jq -e '.title=="Criterio de zona no encontrado."' >/dev/null || fail "miembro de otra zona"
# Los criterios por código postal se quitan (DELETE físico auditado): las corridas siguientes reutilizan esos ZIP.
for M in $(expect 200 "$(req GET "/api/v1/dispatch-zones/$Z1/members")" | jq -r '.members[] | select(.matchTypeCode=="POSTAL_CODE") | .id'); do
  expect 204 "$(req DELETE "/api/v1/dispatch-zones/$Z1/members/$M")" >/dev/null
done
expect 204 "$(req DELETE "/api/v1/dispatch-zones/$Z2/members/$M3")" >/dev/null
res "postalCode=$ZIP1" | jq -e '.zoneCode==null' >/dev/null || fail "CP quitado sigue resolviendo"
ok "Z1..Z6 por municipio; CP, ZIP+4 normalizado y rango; 400 (CP inválido, POLYGON, criterio desconocido); 409 (valor de otra zona activa, criterio repetido); resolución CP > rango > municipio sin mayúsculas; 99999 sin zona; DELETE de otra zona 404 y propio 204"

step "rutas (Lote 5): prerrequisitos (cliente, consignatarios por pueblo, choferes con licencia, vehículos y órdenes confirmadas)"
CR=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Rutas $TS\",\"paymentTerm\":\"NET30\",\"currency\":\"USD\",\"contract\":{\"startDate\":\"2026-01-01\"}}")")
CR_PID=$(echo "$CR" | jq -r .publicId); KR_PID=$(echo "$CR" | jq -r .currentContract.publicId)
expect 200 "$(req POST "/api/v1/contracts/$KR_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$KR_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":7}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/contracts/$KR_PID/billing-model" '{"billSpecialServices":true}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/clients/$CR_PID/number-settings" "{\"orderNumberFormat\":\"R$TS-#####\"}")" >/dev/null   # números únicos para el escaneo por número
SSR=$(expect 200 "$(req POST "/api/v1/clients/$CR_PID/special-services" "{\"newTypeName\":\"Grúa $TS\",\"rate\":90}")" | jq -r .id)
loc() { expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CR_PID\",\"name\":\"$1 $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 1\",\"city\":\"$2\",\"country\":\"PR\",\"allowDupInvoice\":true}")" | jq -r .publicId; }
for i in 1 2 3 4 5 6; do eval "L$i=\$(loc 'Consignatario $i' 'Pueblo $i $TS')"; done
LN=$(loc "Consignatario sin zona" "Sin Zona $TS")
# Orden confirmada hacia un consignatario (extra pisa campos); devuelve la ficha completa
mko() { local x=${2:-}; [[ -n "$x" ]] || x='{}'
  expect 200 "$(req POST /api/v1/orders "$(jq -cn --arg c "$CR_PID" --arg l "$1" --argjson x "$x" '{clientPublicId:$c,consigneeLocationPublicId:$l,packages:[{pieces:1,weightKg:10}],confirmNow:true} + $x')")"; }
pid() { echo "$1" | jq -r .publicId; }
OA1=$(mko "$L1"); OA2=$(mko "$L1"); OA3=$(mko "$L1"); OA4=$(mko "$L1"); OA5=$(mko "$L1")
OB1=$(mko "$L2"); OB2=$(mko "$L2"); OB3=$(mko "$L2"); OB4=$(mko "$L2")
OP1=$(mko "$L3"); OP2=$(mko "$L3"); OP3=$(mko "$L3"); ON1=$(mko "$LN")
OD=$(mko "$L1" '{"confirmNow":false}'); OS=$(mko "$L1" "{\"packages\":null,\"isSpecialDelivery\":true,\"specialServiceId\":$SSR}")
echo "$OA1" | jq -e '.status=="CONFIRMED"' >/dev/null || fail "orden confirmada: $OA1"
echo "$OD" | jq -e '.status=="DRAFT"' >/dev/null || fail "orden en DRAFT"
echo "$OS" | jq -e '.isSpecialDelivery and .status=="CONFIRMED"' >/dev/null || fail "entrega especial confirmada: $OS"
OC_PB=(); OC_PID=()
for i in 1 2 3 4 5 6 7 8; do X=$(mko "$L1"); OC_PB+=("$(echo "$X" | jq -r .packBatchNumber)"); OC_PID+=("$(pid "$X")"); done
drv() { expect 200 "$(req POST /api/v1/drivers "$1")" | jq -r .publicId; }
lic() { expect 200 "$(req POST "/api/v1/drivers/$1/licenses" "{\"licenseClass\":\"CDL_A\",\"licenseNumber\":\"L$RANDOM-$TS\",\"expiryDate\":\"$(dplus "$2")\"}")" >/dev/null; }
D1=$(drv "{\"code\":\"R1$TS\",\"fullName\":\"Rita Uno $TS\",\"dispatchZoneId\":$Z1}"); lic "$D1" 365
D2=$(drv "{\"code\":\"R2$TS\",\"fullName\":\"Rafa Dos $TS\"}"); lic "$D2" 365
D3=$(drv "{\"code\":\"R3$TS\",\"fullName\":\"Rosa Tres $TS\",\"dispatchZoneId\":$Z3}"); lic "$D3" 365
DX=$(drv "{\"code\":\"RX$TS\",\"fullName\":\"Raúl Vencido $TS\"}"); lic "$DX" -30
expect 200 "$(req PATCH "/api/v1/drivers/$D1" '{"maxStopsPerRoute":1}')" | jq -e '.effectiveMaxStops==1' >/dev/null || fail "máximo de paradas de R1"
V1=$(expect 200 "$(req POST /api/v1/vehicles "{\"code\":\"RV1$TS\",\"vehicleType\":\"VAN\",\"ownership\":\"OWNED\",\"fuelType\":\"DIESEL\",\"maxStops\":2}")" | jq -r .publicId)
V2=$(expect 200 "$(req POST /api/v1/vehicles "{\"code\":\"RV2$TS\",\"vehicleType\":\"VAN\",\"ownership\":\"OWNED\",\"fuelType\":\"DIESEL\"}")" | jq -r .publicId)
# Segundo cliente (consolidación multi-cliente, maestro L263) con el MISMO formato de número: la numeración es por cliente,
# así que su primera orden OX1 (consignatario en Pueblo 1 → Z1) repite el número de OA1 y el escaneo por ese número es ambiguo.
CR2=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Rutas B $TS\",\"paymentTerm\":\"NET30\",\"currency\":\"USD\",\"contract\":{\"startDate\":\"2026-01-01\"}}")")
CR2_PID=$(echo "$CR2" | jq -r .publicId); KR2_PID=$(echo "$CR2" | jq -r .currentContract.publicId)
expect 200 "$(req POST "/api/v1/contracts/$KR2_PID/status" '{"toCode":"ACTIVE"}')" >/dev/null
expect 200 "$(req POST "/api/v1/contracts/$KR2_PID/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":7}')" >/dev/null
expect 200 "$(req PATCH "/api/v1/clients/$CR2_PID/number-settings" "{\"orderNumberFormat\":\"R$TS-#####\"}")" >/dev/null
LX=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CR2_PID\",\"name\":\"Consignatario B $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 2\",\"city\":\"Pueblo 1 $TS\",\"country\":\"PR\",\"allowDupInvoice\":true}")" | jq -r .publicId)
OX1=$(mko "$LX" "{\"clientPublicId\":\"$CR2_PID\"}")
echo "$OX1" | jq -e --arg n "$(echo "$OA1" | jq -r .orderNumber)" '.status=="CONFIRMED" and .orderNumber==$n' >/dev/null || fail "la primera orden del segundo cliente repite el número de OA1: $(echo "$OX1" | jq -c '{orderNumber,status}')"
ok "cliente con contrato, 7 consignatarios (Pueblo 1..6 y sin zona), 21 órdenes confirmadas + una en DRAFT + una entrega especial, segundo cliente con OX1 (mismo número que OA1, Z1), choferes R1 (Z1, máximo 1), R2, R3 (Z3) con licencia y RX vencida, vehículos RV1 (máximo 2) y RV2"

step "alta de ruta (Lote 5): número AAAA-####, chofer por defecto de la zona, salida 12:00 UTC y validaciones"
TR1J=$(expect 200 "$(tpost "{\"dispatchZoneId\":$Z1,\"vehiclePublicId\":\"$V1\"}")")
echo "$TR1J" | jq -e --arg d "$TODAY" --arg c "R1$TS" '(.code | test("^[0-9]{4}-[0-9]{4,}$")) and .statusCode=="DRAFT" and .driverCode==$c and (.plannedStartUtc | startswith($d + "T12:00:00")) and .stopCount==0 and .isEditable and .canEditHeader' >/dev/null || fail "alta de ruta: $TR1J"
TR1=$(pid "$TR1J"); TR1_ID=$(echo "$TR1J" | jq -r .id); TR1_CODE=$(echo "$TR1J" | jq -r .code)
[[ "${TR1_CODE%%-*}" == "${TODAY%%-*}" ]] || fail "el número de ruta empieza con el año: $TR1_CODE"
expect 200 "$(req GET "/api/v1/status/history/TRIP/$TR1_ID")" | jq -e 'length==1 and .[0].toCode=="DRAFT"' >/dev/null || fail "historial de nacimiento de la ruta"
expect 400 "$(req POST /api/v1/trips "{\"planDate\":\"$(dplus -3)\"}")" | jq -e --arg m "La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días." "$HASM" >/dev/null || fail "fecha fuera de rango"
expect 400 "$(req POST /api/v1/trips '{}')" | jq -e --arg m "Indique la fecha de la ruta." "$HASM" >/dev/null || fail "fecha obligatoria"
Z0=$(zone "Z0$TS" "Zona inactiva"); expect 200 "$(req POST "/api/v1/dispatch-zones/$Z0/deactivate")" >/dev/null
expect 400 "$(tpost "{\"dispatchZoneId\":$Z0}")" | jq -e --arg m "La zona de despacho está inactiva." "$HASM" >/dev/null || fail "zona inactiva"
expect 409 "$(tpost "{\"driverPublicId\":\"$DX\"}")" | jq -e '.title | startswith("El chofer no está disponible para despacho")' >/dev/null || fail "chofer con licencia vencida"
expect 400 "$(req PATCH "/api/v1/trips/$TR1" '{"code":"2000-0001"}')" | jq -e --arg m "El número de la ruta se fija al crearlo; no se puede cambiar." "$HASM" >/dev/null || fail "número de ruta fijo"
expect 409 "$(req PATCH "/api/v1/trips/$TR1" '{"plannedStartUtc":null,"rowVersion":"AAAAAAAAAAA="}')" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "rowVersion obsoleto en la ruta"
TR2J=$(expect 200 "$(tpost "{\"dispatchZoneId\":$Z2,\"driverPublicId\":\"$D2\",\"vehiclePublicId\":\"$V2\"}")"); TR2=$(pid "$TR2J"); TR2_CODE=$(echo "$TR2J" | jq -r .code)
TR3J=$(expect 200 "$(tpost "{\"dispatchZoneId\":$Z1}")"); TR3=$(pid "$TR3J"); TR3_CODE=$(echo "$TR3J" | jq -r .code)
echo "$TR3J" | jq -e --arg c "R1$TS" '.driverCode==$c and .vehicleCode==null' >/dev/null || fail "TR3 con chofer por defecto y sin vehículo"
ok "$TR1_CODE en DRAFT con R1 por defecto y salida $TODAY 12:00Z, historial de nacimiento, 400 (fecha, zona inactiva, número fijo), 409 (chofer no disponible, rowVersion); $TR2_CODE (Z2, R2, RV2) y $TR3_CODE (Z1, sin vehículo)"

step "números de ruta concurrentes (Lote 5): 8 altas simultáneas"
TMPT=$(mktemp -d)
for i in 1 2 3 4 5 6 7 8; do tpost > "$TMPT/$i" & done
wait
CODES=()
for i in 1 2 3 4 5 6 7 8; do [[ $(tail -n1 "$TMPT/$i") == "200" ]] || fail "alta simultánea de ruta $i: $(cat "$TMPT/$i")"; CODES+=("$(sed '$d' "$TMPT/$i" | jq -r .code)"); done
[[ $(printf '%s\n' "${CODES[@]}" | sort -u | wc -l) -eq 8 ]] || fail "números de ruta repetidos: ${CODES[*]}"
SEQS=$(printf '%s\n' "${CODES[@]}" | sed 's/^[0-9]*-//' | sed 's/^0*//' | sort -n)
[[ $(( $(echo "$SEQS" | tail -1) - $(echo "$SEQS" | head -1) )) -eq 7 ]] || fail "números de ruta no consecutivos: ${CODES[*]}"
for i in 1 2 3 4 5 6 7 8; do expect 204 "$(req DELETE "/api/v1/trips/$(sed '$d' "$TMPT/$i" | jq -r .publicId)")" >/dev/null; done
ok "8 × 200 con números distintos y consecutivos ($(echo "$SEQS" | head -1)..$(echo "$SEQS" | tail -1)); eliminadas (204)"

step "sin asignar y agregar órdenes (Lote 5): consolidación atómica, elegibilidad y la orden no cambia de estatus"
num() { echo "$1" | jq -r .orderNumber; }; pb() { echo "$1" | jq -r .packBatchNumber; }   # el empaque es único en la compañía; el número, por cliente
UA=$(expect 200 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z1&take=500")")
echo "$UA" | jq -e --arg a "$(pid "$OA1")" --arg b "$(pid "$OA4")" --arg d "$(pid "$OD")" --arg s "$(pid "$OS")" --arg z "Z1$TS" \
  '(.items | map(.publicId)) as $ids | ($ids | index($a)) and ($ids | index($b)) and ($ids | index($d) | not) and ($ids | index($s) | not) and all(.items[]; .zoneCode==$z)' >/dev/null || fail "sin asignar de Z1: $(echo "$UA" | jq -c '.total')"
expect 200 "$(req GET "/api/v1/trips/unassigned-orders?noZone=true&search=$(pb "$ON1")")" | jq -e --arg n "$(pid "$ON1")" '.total==1 and .items[0].publicId==$n and .items[0].dispatchZoneId==null' >/dev/null || fail "sin asignar sin zona"
expect 200 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z1&search=$(pb "$OA2")")" | jq -e --arg n "$(pid "$OA2")" '.total==1 and .items[0].publicId==$n' >/dev/null || fail "búsqueda después del filtro de zona"
expect 200 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z2&search=$(pb "$OA2")")" | jq -e '.total==0' >/dev/null || fail "la búsqueda no salta el filtro de zona"
expect 400 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z1&noZone=true")" | jq -e --arg m "Use dispatchZoneId o noZone, no ambos." "$HASM" >/dev/null || fail "zona y sin zona a la vez"
addo() { req POST "/api/v1/trips/$1/orders" "$(jq -cn --args '{orderPublicIds:$ARGS.positional}' "${@:2}")"; }
D=$(expect 200 "$(addo "$TR1" "$(pid "$OA1")" "$(pid "$OA2")")")
echo "$D" | jq -e '([.stops[].sequence]==[1,2]) and .routeVersion==1 and .routeStatusCode=="DRAFT" and .stopCount==2 and all(.stops[]; .plannedArrivalUtc != null)' >/dev/null || fail "agregar órdenes: $D"
expect 422 "$(addo "$TR1" "$(pid "$OA3")" "$(pid "$OD")")" | jq -e --arg m "La orden está en Entrada; confírmela antes de asignarla a una ruta." '.title=="Hay órdenes que no se pueden asignar a la ruta." and ([.errors[][]] | index($m))' >/dev/null || fail "orden en DRAFT (atómico)"
expect 200 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z1&search=$(pb "$OA3")")" | jq -e '.total==1' >/dev/null || fail "el 422 atómico asignó OA3"
expect 422 "$(addo "$TR1" "$(pid "$OS")")" | jq -e --arg m "Las entregas especiales se asignan al chofer desde la orden; no pasan por Sala de despacho." '[.errors[][]] | index($m)' >/dev/null || fail "entrega especial"
expect 409 "$(addo "$TR1" "$(pid "$OA1")")" | jq -e '.title=="La orden ya está en esta ruta."' >/dev/null || fail "orden repetida en la ruta"
expect 409 "$(addo "$TR3" "$(pid "$OA1")")" | jq -e --arg m "La orden ya está asignada a la ruta $TR1_CODE." '.title==$m' >/dev/null || fail "orden en otra ruta"
expect 404 "$(addo "$TR1" "$(randuuid)")" | jq -e '.title=="Orden no encontrado."' >/dev/null || fail "orden inexistente"
expect 200 "$(req GET "/api/v1/orders/$(pid "$OA1")")" | jq -e --arg t "$TR1_CODE" --arg d "R1$TS" '.status=="CONFIRMED" and .assignedTripCode==$t and .assignedDriverCode==$d' >/dev/null || fail "ficha de la orden con ruta y chofer"
ok "sin asignar por zona (excluye DRAFT y entrega especial), sin zona y búsqueda después del filtro; $TR1_CODE con 2 paradas (v1 DRAFT, ETAs); 422 atómico, 409 misma ruta / otra ruta, 404; la orden sigue CONFIRMED con ruta y chofer en su ficha"

step "concurrencia de asignación (Lote 5): la misma orden a dos rutas a la vez"
addo "$TR1" "$(pid "$OA3")" > "$TMPT/a1" & addo "$TR3" "$(pid "$OA3")" > "$TMPT/a2" & wait
C1=$(tail -n1 "$TMPT/a1"); C2=$(tail -n1 "$TMPT/a2")
[[ "$C1$C2" == "200409" || "$C1$C2" == "409200" ]] || fail "carrera de asignación: $C1 / $C2: $(cat "$TMPT/a1" "$TMPT/a2")"
for F in a1 a2; do [[ $(tail -n1 "$TMPT/$F") == 409 ]] && { sed '$d' "$TMPT/$F" | jq -e '.title | contains("ya está asignada a")' >/dev/null || fail "mensaje del perdedor: $(cat "$TMPT/$F")"; }; done
IN1=$(trip "$TR1" | jq --arg o "$(pid "$OA3")" '[.stops[] | select(.orderPublicId==$o)] | length'); IN3=$(trip "$TR3" | jq --arg o "$(pid "$OA3")" '[.stops[] | select(.orderPublicId==$o)] | length')
[[ $((IN1 + IN3)) -eq 1 ]] || fail "OA3 en $IN1 + $IN3 rutas"
WIN=$([[ $IN1 -eq 1 ]] && echo "$TR1" || echo "$TR3")
ok "un 200 y un 409 ('ya está asignada a'); OA3 queda en una sola ruta"

step "quitar y liberar (Lote 5): resecuencia, la orden vuelve a 'sin asignar' con su estatus"
expect 200 "$(req DELETE "/api/v1/trips/$WIN/orders/$(pid "$OA3")")" >/dev/null
D=$(expect 200 "$(req DELETE "/api/v1/trips/$TR1/orders/$(pid "$OA1")")")
echo "$D" | jq -e '[.stops[].sequence]==[1] and .stopCount==1' >/dev/null || fail "resecuencia al quitar: $D"
expect 200 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z1&search=$(pb "$OA1")")" | jq -e '.total==1 and .items[0].statusCode=="CONFIRMED"' >/dev/null || fail "OA1 vuelve a sin asignar con su estatus"
expect 404 "$(req DELETE "/api/v1/trips/$TR1/orders/$(pid "$OB1")")" | jq -e '.title=="La orden no está en esta ruta."' >/dev/null || fail "quitar una orden que no está en la ruta"
expect 200 "$(req GET '/api/v1/audit/changes?entityType=TRIP&take=100')" | jq -e --argjson o "$(echo "$OA1" | jq .id)" 'any(.items[]; .action=="Borrar" and ((.changesJson // "{}") | fromjson | .TransportOrderId==$o))' >/dev/null || fail "AuditLog sin el DELETE del TripOrder"
RV0=$(rv "$TR1")
D=$(expect 200 "$(addo "$TR1" "$(pid "$OA1")" "$(pid "$OA3")")"); echo "$D" | jq -e '[.stops[].sequence]==[1,2,3]' >/dev/null || fail "volver a agregar"
# Agregar órdenes cambia el RowVersion del Trip: una secuencia o un PATCH con el rowVersion anterior responden 409.
[[ "$(echo "$D" | jq -r .rowVersion)" != "$RV0" ]] || fail "agregar órdenes no cambió el rowVersion de la ruta"
expect 409 "$(req PUT "/api/v1/trips/$TR1/route/sequence" "$(echo "$D" | jq -c --arg r "$RV0" '{routeStopIds:([.stops[].id] | reverse),rowVersion:$r}')")" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "secuencia con el rowVersion anterior a agregar una orden"
expect 409 "$(req PATCH "/api/v1/trips/$TR1" "$(jq -cn --arg r "$RV0" --arg s "${TODAY}T12:00:00Z" '{plannedStartUtc:$s,rowVersion:$r}')")" | jq -e '.title | startswith("El registro fue modificado")' >/dev/null || fail "PATCH con el rowVersion anterior a agregar una orden"
trip "$TR1" | jq -e '[.stops[].sequence]==[1,2,3]' >/dev/null || fail "el 409 cambió la ruta"
expect 200 "$(addo "$TR2" "$(pid "$OB1")")" >/dev/null
ok "quitar resecuencia 1..N, OA1 libre y CONFIRMED, 404 'La orden no está en esta ruta.', AuditLog con el DELETE; agregar cambia el rowVersion (secuencia y PATCH con el anterior → 409); $TR1_CODE queda con 3 paradas y $TR2_CODE con OB1"

step "pin manual y ETAs (Lote 5): precisión MANUAL, distancia recalculada y BOLA por id de parada"
loc5() { req PUT "/api/v1/trips/$1/stops/$2/location" "$3" "${4:-$TOKEN}"; }
S1=$(trip "$TR1" | jq -r '.stops[0].id'); S2=$(trip "$TR1" | jq -r '.stops[1].id'); S2TR2=$(trip "$TR2" | jq -r '.stops[0].id')
trip "$TR1" | jq -e --argjson s "$S1" '(.stops[] | select(.id==$s) | .isApproximate and .point==null) and any(.issues[]; .code=="APPROXIMATE_PINS" and (.blocking|not))' >/dev/null || fail "parada sin pin: isApproximate y APPROXIMATE_PINS antes del pin manual: $(trip "$TR1" | jq -c '{s: [.stops[] | {id,isApproximate,geocodeAccuracyCode}], i: [.issues[].code]}')"
expect 200 "$(loc5 "$TR1" "$S1" '{"lat":18.4655,"lng":-66.1057}')" | jq -e --argjson s "$S1" '.stops[] | select(.id==$s) | .geocodeAccuracyCode=="MANUAL" and (.isApproximate|not) and .point.lat==18.4655' >/dev/null || fail "pin de la parada 1"
D=$(expect 200 "$(loc5 "$TR1" "$S2" '{"lat":18.3985,"lng":-66.1553}')")
echo "$D" | jq -e --argjson s "$S2" '.stops[] | select(.id==$s) | .distanceFromPrevKm >= 11.6 and .distanceFromPrevKm <= 12.0 and .plannedArrivalUtc != null' >/dev/null || fail "distancia San Juan → Bayamón recalculada: $(echo "$D" | jq -c '[.stops[] | {id,distanceFromPrevKm,durationFromPrevMin}]')"
expect 400 "$(loc5 "$TR1" "$S1" '{"lat":91,"lng":0}')" | jq -e --arg m "La latitud debe estar entre -90 y 90." "$HASM" >/dev/null || fail "latitud fuera de rango"
expect 400 "$(loc5 "$TR1" "$S1" '{}')" | jq -e --arg m "Indique la latitud y la longitud." "$HASM" >/dev/null || fail "pin sin coordenadas"
expect 404 "$(loc5 "$TR1" "$S2TR2" '{"lat":18.4,"lng":-66.1}')" | jq -e '.title=="Parada no encontrada en esta ruta."' >/dev/null || fail "parada de otra ruta por la URL de esta"
expect 200 "$(req GET '/api/v1/audit/changes?entityType=TRANSPORT_ORDER&take=100')" | jq -e 'any(.items[]; (.changesJson // "") | contains("GeocodeAccuracy"))' >/dev/null || fail "AuditLog de TRANSPORT_ORDER sin el cambio de precisión"
ok "MANUAL, isApproximate false y punto; 11.6–12.0 km entre los pines; 400 (latitud, sin coordenadas); 404 con una parada de $TR2_CODE; AuditLog de la precisión"

step "optimizar, versiones y secuencia (Lote 5): tres fases, corridas OK/ERROR y reordenamiento sin re-optimizar"
runs() { expect 200 "$(req GET "/api/v1/trips/$1/optimization-runs")"; }
V1ROUTE=$(trip "$TR1" | jq -r .routeId)
OPT=$(expect 200 "$(req POST "/api/v1/trips/$TR1/optimize" '{}')")
echo "$OPT" | jq -e '.engineCode=="HEURISTIC" and .statusCode=="OK" and .routeVersion==2 and .unassignedCount==1 and .unassigned[0].reasonCode=="CAPACITY_STOPS" and .trip.statusCode=="PLANNED" and .trip.routeStatusCode=="OPTIMIZED" and .trip.stopCount==2' >/dev/null || fail "optimizar: $(echo "$OPT" | jq -c 'del(.trip)')"
UNA=$(echo "$OPT" | jq -r '.unassigned[0].orderPublicId')
trip "$TR1" | jq -e --arg o "$UNA" '.lastRunUnassigned[0].orderPublicId==$o and .lastRunUnassigned[0].reason=="Excede el máximo de paradas del vehículo."' >/dev/null || fail "ficha con el motivo de la parada que no cupo"
expect 200 "$(req GET "/api/v1/orders/$UNA")" | jq -e '.status=="CONFIRMED" and .assignedTripCode==null' >/dev/null || fail "la que no cupo vuelve a sin asignar"
expect 200 "$(req GET "/api/v1/status/history/ROUTE/$V1ROUTE")" | jq -e '.[-1].toCode=="ARCHIVED"' >/dev/null || fail "la versión 1 queda archivada"
if [[ -n "${SMOKE_SQL:-}" ]]; then   # la v1 archivada conserva sus 3 paradas: la que no cupo se libera de la v2, no de la v1
  N1=$($SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.RouteStop WHERE RouteId = $V1ROUTE;" | tr -d '[:space:]')
  [[ "$N1" == "3" ]] || fail "la versión 1 archivada debe conservar sus 3 paradas: $N1"
fi
runs "$TR1" | jq -e 'length==1 and .[0].statusCode=="OK" and .[0].routeVersion==2 and .[0].engineCode=="HEURISTIC"' >/dev/null || fail "corridas de optimización"
IDS=$(trip "$TR1" | jq -c '[.stops[].id] | reverse')
D=$(expect 200 "$(req PUT "/api/v1/trips/$TR1/route/sequence" "$(jq -cn --argjson i "$IDS" --arg r "$(rv "$TR1")" '{routeStopIds:$i,rowVersion:$r}')")")
echo "$D" | jq -e --argjson i "$IDS" '[.stops[].id]==$i and .routeVersion==2 and ([.stops[].plannedArrivalUtc] as $a | $a==($a|sort))' >/dev/null || fail "reordenar: $(echo "$D" | jq -c '[.stops[] | {id,plannedArrivalUtc}]')"
runs "$TR1" | jq -e 'length==1' >/dev/null || fail "reordenar no crea corrida"
SEQMSG="La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir."
expect 400 "$(req PUT "/api/v1/trips/$TR1/route/sequence" "$(jq -cn --argjson i "$IDS" --argjson o "$S2TR2" '{routeStopIds:[$i[0],$o]}')")" | jq -e --arg m "$SEQMSG" "$HASM" >/dev/null || fail "secuencia con una parada de otra ruta"
expect 400 "$(req PUT "/api/v1/trips/$TR1/route/sequence" "$(jq -cn --argjson i "$IDS" '{routeStopIds:[$i[0]]}')")" | jq -e --arg m "$SEQMSG" "$HASM" >/dev/null || fail "secuencia incompleta"
req POST "/api/v1/trips/$TR1/optimize" '{}' > "$TMPT/o1" & req POST "/api/v1/trips/$TR1/optimize" '{}' > "$TMPT/o2" & wait
N200=0; N409=0
for F in o1 o2; do
  case $(tail -n1 "$TMPT/$F") in
    200) N200=$((N200 + 1)) ;;
    409) sed '$d' "$TMPT/$F" | jq -e '.title=="La ruta cambió mientras se optimizaba; vuelva a optimizar."' >/dev/null || fail "409 inesperado: $(cat "$TMPT/$F")"; N409=$((N409 + 1)) ;;
    *) fail "optimización simultánea: $(cat "$TMPT/$F")" ;;
  esac
done
[[ $N200 -ge 1 ]] || fail "ninguna optimización simultánea ganó"
runs "$TR1" | jq -e --argjson ok $((1 + N200)) --argjson err "$N409" '([.[] | select(.statusCode=="OK")] | length)==$ok and ([.[] | select(.statusCode=="ERROR")] | length)==$err and all(.[]; .statusCode!="PENDING")' >/dev/null || fail "corridas tras la carrera: $(runs "$TR1" | jq -c '[.[] | {statusCode,routeVersion,errorMessage}]')"
trip "$TR1" | jq -e --argjson v $((2 + N200)) '.routeVersion==$v' >/dev/null || fail "versión vigente tras la carrera"
expect 422 "$(req POST "/api/v1/trips/$TR3/optimize" '{}')" | jq -e '.title=="La ruta no tiene paradas que optimizar."' >/dev/null || fail "optimizar sin paradas"
runs "$TR3" | jq -e 'length==0' >/dev/null || fail "optimizar sin paradas creó una corrida"
ok "v2 HEURISTIC con 1 parada liberada (CAPACITY_STOPS, motivo en la ficha), v1 ARCHIVED, PLANNED/OPTIMIZED; reordenar = misma versión, ETAs crecientes y sin corrida; 400 de secuencia; 2 optimizaciones simultáneas: $N200 × 200 y $N409 × 409 con corridas OK/ERROR iguales y ninguna PENDING; 422 sin paradas y sin corrida"

step "alerta de máximo de paradas (Lote 5)"
trip "$TR1" | jq -e '.overStopLimit and .effectiveMaxStops==1 and any(.issues[]; .code=="OVER_STOP_LIMIT" and .message=="La ruta tiene 2 paradas y el máximo del chofer es 1." and (.blocking|not))' >/dev/null || fail "aviso OVER_STOP_LIMIT: $(trip "$TR1" | jq -c '.issues')"
indval() { local id; id=$(expect 200 "$(req GET /api/v1/analytics/indicators)" | jq -r --arg n "$1" '[.[] | select(.name==$n)][0].id'); [[ "$id" =~ ^[0-9]+$ ]] || fail "indicador '$1' no sembrado"; expect 200 "$(req GET "/api/v1/analytics/indicators/$id/value")" | jq -r .value; }
[[ $(indval "Rutas sobre el máximo de paradas") -ge 1 ]] || fail "indicador 'Rutas sobre el máximo de paradas'"
# Respaldo del tenant (maestro L270): R2 no tiene override → el máximo efectivo es Tenant.MaxStopsPerRouteDefault (30).
trip "$TR2" | jq -e '.effectiveMaxStops==30 and (.overStopLimit|not)' >/dev/null || fail "máximo efectivo por defecto del tenant: $(trip "$TR2" | jq -c '{effectiveMaxStops,overStopLimit}')"
expect 200 "$(req GET "/api/v1/trips?date=$TODAY")" | jq -e --arg a "$TR1" --arg b "$TR2" 'any(.[]; .publicId==$a and .overStopLimit and .effectiveMaxStops==1) and any(.[]; .publicId==$b and .effectiveMaxStops==30 and (.overStopLimit|not))' >/dev/null || fail "alerta de máximo de paradas en el listado"
expect 200 "$(req GET "/api/v1/trips/dispatchable?date=$TODAY")" | jq -e --arg a "$TR1" 'any(.[]; .trip.publicId==$a and .trip.overStopLimit and any(.issues[]; .code=="OVER_STOP_LIMIT" and (.blocking|not)))' >/dev/null || fail "alerta de máximo de paradas en el selector de despacho"
ok "overStopLimit y OVER_STOP_LIMIT (aviso, no bloquea) en ficha, listado y selector; respaldo del tenant (30) sin override del chofer; indicador ≥ 1"

step "planificar el día (Lote 5): por zona, idempotente, con chofer estándar y rutas vacías para el escaneo"
PD=$(expect 200 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z3]}")")
echo "$PD" | jq -e '.tripsCreated==1 and .ordersAssigned==3 and .zones[0].tripCreated and .zones[0].ordersAssigned==3' >/dev/null || fail "planificar Z3: $PD"
TRP=$(echo "$PD" | jq -r '.zones[0].tripPublicId'); TRP_CODE=$(echo "$PD" | jq -r '.zones[0].tripCode')
trip "$TRP" | jq -e --arg c "R3$TS" --arg d "$TODAY" '.driverCode==$c and (.plannedStartUtc | startswith($d + "T12:00:00")) and .stopCount==3 and .vehicleCode==null and .statusCode=="DRAFT"' >/dev/null || fail "ruta de Z3 con chofer estándar y salida 12:00Z"
expect 200 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z3]}")" | jq -e '.tripsCreated==0 and .ordersAssigned==0' >/dev/null || fail "planificar dos veces no cambia nada"
OP4=$(mko "$L3")
expect 200 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z3]}")" | jq -e --arg c "$TRP_CODE" '.tripsCreated==0 and .ordersAssigned==1 and .zones[0].tripCode==$c and (.zones[0].tripCreated|not)' >/dev/null || fail "la orden nueva entra en la MISMA ruta"
expect 200 "$(req GET "/api/v1/orders/$(pid "$OP1")")" | jq -e '.status=="CONFIRMED"' >/dev/null || fail "planificar no cambia el estatus de la orden"
# Por fecha (maestro L262): una orden pedida para mañana no entra en la ruta de hoy; sí al planificar mañana.
OP5=$(mko "$L3" "{\"requestedDate\":\"${TOMORROW}T00:00:00\"}")
expect 200 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z3]}")" | jq -e '.tripsCreated==0 and .ordersAssigned==0' >/dev/null || fail "una orden pedida para mañana entró al planificar hoy"
trip "$TRP" | jq -e --arg o "$(pid "$OP5")" '.stopCount==4 and all(.stops[]; .orderPublicId!=$o)' >/dev/null || fail "OP5 quedó en la ruta de hoy"
PDM=$(expect 200 "$(plan "{\"planDate\":\"$TOMORROW\",\"dispatchZoneIds\":[$Z3]}")")
echo "$PDM" | jq -e '.tripsCreated==1 and .ordersAssigned==1' >/dev/null || fail "la orden pedida para mañana entra al planificar mañana: $PDM"
trip "$(echo "$PDM" | jq -r '.zones[0].tripPublicId')" | jq -e --arg d "$TOMORROW" --arg o "$(pid "$OP5")" '.planDate==$d and .stops[0].orderPublicId==$o' >/dev/null || fail "ruta de mañana con OP5"
expect 200 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z4]}")" | jq -e '.tripsCreated==0 and .ordersAssigned==0' >/dev/null || fail "zona sin órdenes no crea ruta"
PD4=$(expect 200 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z4],\"createEmptyTrips\":true}")")
echo "$PD4" | jq -e '.tripsCreated==1 and .zones[0].tripCreated' >/dev/null || fail "createEmptyTrips: $PD4"
TR4Z=$(echo "$PD4" | jq -r '.zones[0].tripPublicId'); trip "$TR4Z" | jq -e '.stopCount==0' >/dev/null || fail "ruta vacía de Z4"
B5="{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z5],\"createEmptyTrips\":true}"
plan "$B5" > "$TMPT/p1" & plan "$B5" > "$TMPT/p2" & wait
for F in p1 p2; do [[ $(tail -n1 "$TMPT/$F") == 200 ]] || fail "planificación simultánea: $(cat "$TMPT/$F")"; done
expect 200 "$(req GET "/api/v1/trips?dispatchZoneId=$Z5&date=$TODAY")" | jq -e 'length==1' >/dev/null || fail "dos planificaciones simultáneas dejaron más de una ruta en Z5"
expect 400 "$(plan "$(jq -cn --arg d "$TODAY" '{planDate:$d,dispatchZoneIds:[range(1;52)]}')")" | jq -e --arg m "Máximo 50 zonas por planificación." "$HASM" >/dev/null || fail "51 zonas"
ZT3=$(expect 200 "$(req POST /api/v1/dispatch-zones "{\"code\":\"W$TS\",\"name\":\"Zona T3\"}" "$T3")" | jq -r .id)
expect 404 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$ZT3]}")" | jq -e '.title=="Zona de despacho no encontrada."' >/dev/null || fail "zona de otro tenant"
expect 400 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z0]}")" | jq -e --arg m "La zona de despacho está inactiva." "$HASM" >/dev/null || fail "plan-day con zona inactiva"
# Sin lista = todas las zonas ACTIVAS del tenant. Se prueba en T3 (aislado de las órdenes del demo) con una zona inactiva.
ZT3I=$(expect 200 "$(req POST /api/v1/dispatch-zones "{\"code\":\"WI$TS\",\"name\":\"Zona T3 inactiva\"}" "$T3")" | jq -r .id)
expect 200 "$(req POST "/api/v1/dispatch-zones/$ZT3I/deactivate" '' "$T3")" >/dev/null
expect 200 "$(plan "{\"planDate\":\"$TODAY\"}" "$T3")" | jq -e --argjson a "$ZT3" --argjson i "$ZT3I" '(.zones | map(.dispatchZoneId)) as $z | ($z | index($a)) != null and ($z | index($i)) == null' >/dev/null || fail "plan-day sin lista: todas las zonas activas y ninguna inactiva"
expect 403 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z3]}" "$TREAD")" >/dev/null
ok "$TRP_CODE (Z3, R3, 12:00Z) con 3 órdenes; repetir = 0/0; OP4 entra en la misma ruta; OP5 (pedida para mañana) solo entra al planificar mañana; Z4 vacía solo con createEmptyTrips; 2 simultáneas en Z5 = 1 ruta; sin lista = zonas activas (sin la inactiva); 400 (51 zonas, zona inactiva), 404 (zona ajena), 403 (Solo lectura)"

step "escaneo Outbound (Lote 5): lookup número > empaque > factura, ruta abierta de la zona y palabra de voz"
expect 200 "$(scan "$(pb "$OA4")")" | jq -e --arg t "$TR1_CODE" '.outcome=="FOUND_ASSIGNED" and .voice=="found" and .matchedBy=="PACK_BATCH" and .tripCode==$t' >/dev/null || fail "empaque de OA4 → $TR1_CODE"
expect 200 "$(scan "$(pb "$OA4")")" | jq -e --arg t "$TR1_CODE" '.outcome=="ALREADY_ASSIGNED" and .voice=="dup" and .tripCode==$t' >/dev/null || fail "segundo escaneo = dup"
expect 200 "$(scan "NOEXISTE$TS")" | jq -e '.outcome=="NOT_FOUND" and .voice=="notfound"' >/dev/null || fail "código inexistente"
expect 200 "$(scan "$(pb "$OD")")" | jq -e '.outcome=="NOT_ELIGIBLE" and .voice=="notfound" and .message=="La orden está en Entrada; confírmela antes de asignarla a una ruta."' >/dev/null || fail "orden en DRAFT"
expect 200 "$(scan "$(pb "$OB2")")" | jq -e --arg t "$TR2_CODE" '.outcome=="FOUND_ASSIGNED" and .tripCode==$t' >/dev/null || fail "OB2 → $TR2_CODE"
expect 200 "$(scan "$(pb "$ON1")")" | jq -e '.outcome=="FOUND_UNASSIGNED" and .reasonCode=="NO_ZONE" and .voice=="found"' >/dev/null || fail "orden sin zona"
OB5=$(mko "$L2")
expect 200 "$(scan "$(num "$OB5")")" | jq -e --arg t "$TR2_CODE" '.outcome=="FOUND_ASSIGNED" and .matchedBy=="ORDER_NUMBER" and .tripCode==$t' >/dev/null || fail "por número de orden"
OZ4=$(mko "$L4")
expect 200 "$(scan "$(pb "$OZ4")" "$TWH")" | jq -e --arg t "$(trip "$TR4Z" | jq -r .code)" '.outcome=="FOUND_ASSIGNED" and .tripCode==$t' >/dev/null || fail "orden de Z4 → la ruta vacía de 'Planificar el día' (Operador de almacén)"
expect 400 "$(scan "")" | jq -e --arg m "Escanee o escriba un código." "$HASM" >/dev/null || fail "código vacío"
expect 403 "$(scan "$(pb "$OA5")" "$TREAD")" >/dev/null
for i in 0 1 2 3 4 5 6 7; do scan "${OC_PB[$i]}" > "$TMPT/s$i" & done
wait
for i in 0 1 2 3 4 5 6 7; do [[ $(tail -n1 "$TMPT/s$i") == 200 ]] && sed '$d' "$TMPT/s$i" | jq -e --arg t "$TR1_CODE" '.outcome=="FOUND_ASSIGNED" and .tripCode==$t' >/dev/null || fail "escaneo simultáneo $i: $(cat "$TMPT/s$i")"; done
trip "$TR1" | jq -e '[.stops[].sequence]==[range(1; (.stops|length)+1)] and .stopCount==11' >/dev/null || fail "secuencias tras 8 escaneos: $(trip "$TR1" | jq -c '[.stops[].sequence]')"
[[ $(ostatus "${OC_PID[0]}") == CONFIRMED ]] || fail "escanear no cambia el estatus de la orden"
ok "FOUND_ASSIGNED (empaque, número), ALREADY_ASSIGNED, NOT_FOUND, NOT_ELIGIBLE, FOUND_UNASSIGNED NO_ZONE, ruta vacía de Z4 desde el Operador de almacén, 400 vacío, 403 Solo lectura; 8 escaneos simultáneos → $TR1_CODE con secuencia 1..11 sin huecos"

step "consolidación multi-cliente (Lote 5): órdenes de dos clientes en la misma ruta física, aunque repitan el número"
D=$(expect 200 "$(addo "$TR1" "$(pid "$OX1")")")
echo "$D" | jq -e --arg a "Rutas $TS" --arg b "Rutas B $TS" --arg o "$(pid "$OX1")" '.stopCount==12 and ([.stops[].clientName] | unique)==([$a,$b] | sort) and (.stops[-1].orderPublicId==$o)' >/dev/null || fail "ruta con órdenes de dos clientes: $(echo "$D" | jq -c '[.stops[] | {orderNumber,clientName}]')"
expect 200 "$(scan "$(num "$OA1")")" | jq -e '.outcome=="NOT_FOUND" and .reasonCode=="MULTIPLE_MATCHES" and .voice=="notfound" and .message=="Hay varias órdenes con ese código; escanee el empaque."' >/dev/null || fail "escaneo por un número repetido entre clientes"
expect 200 "$(scan "$(pb "$OX1")")" | jq -e --arg t "$TR1_CODE" '.outcome=="ALREADY_ASSIGNED" and .tripCode==$t and .matchedBy=="PACK_BATCH"' >/dev/null || fail "el empaque distingue la orden del segundo cliente"
ok "$TR1_CODE con 12 paradas de 'Rutas $TS' y 'Rutas B $TS'; el número repetido entre clientes da NOT_FOUND MULTIPLE_MATCHES y el empaque distingue la orden (ALREADY_ASSIGNED)"

step "bloqueantes de despacho vistos desde fuera (Lote 5): chofer no disponible, ASSIGN_TRIP apagada y etapa DISPATCHED deshabilitada"
dispatchable() { expect 200 "$(req GET "/api/v1/trips/dispatchable?date=$TODAY" '' "${1:-$TOKEN}")"; }
dispatch() { req POST "/api/v1/trips/$1/dispatch" '{}' "${2:-$TOKEN}"; }
dispatchable | jq -e --arg t "$TR2" 'any(.[]; .trip.publicId==$t and .canDispatch)' >/dev/null || fail "$TR2_CODE despachable al inicio: $(dispatchable | jq -c --arg t "$TR2" '.[] | select(.trip.publicId==$t) | .issues')"
expect 200 "$(req POST "/api/v1/drivers/$D2/status" '{"toCode":"UNAVAILABLE","comment":"smoke Lote 5"}')" >/dev/null
SEL=$(dispatchable); RD=$(dispatch "$TR2")
expect 200 "$(req POST "/api/v1/drivers/$D2/status" '{"toCode":"ACTIVE"}')" >/dev/null
echo "$SEL" | jq -e --arg t "$TR2" 'any(.[]; .trip.publicId==$t and (.canDispatch|not) and any(.issues[]; .code=="DRIVER_UNAVAILABLE" and .blocking))' >/dev/null || fail "selector con DRIVER_UNAVAILABLE"
expect 422 "$RD" | jq -e '(.title | contains("no se puede despachar")) and (.title | contains("El chofer no está disponible para despacho:"))' >/dev/null || fail "despachar con el chofer no disponible"
# Vehículo con una orden de trabajo en proceso (maestro L286): 409 al asignarlo (alta y PATCH), bloqueante en el selector y
# 422 al despachar. La OT se cancela ANTES de verificar: TR2 se despacha más adelante con RV2.
WOT=$(expect 200 "$(req POST /api/v1/maintenance-work-orders "{\"vehiclePublicId\":\"$V2\",\"maintenanceType\":\"CORRECTIVE\"}")" | jq -r .publicId)
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOT/status" '{"toCode":"IN_PROGRESS"}')" >/dev/null
SELV=$(dispatchable); RDV=$(dispatch "$TR2"); RCV=$(tpost "{\"vehiclePublicId\":\"$V2\"}"); RPV=$(req PATCH "/api/v1/trips/$TR3" "{\"vehiclePublicId\":\"$V2\"}")
expect 200 "$(req POST "/api/v1/maintenance-work-orders/$WOT/status" '{"toCode":"CANCELLED","comment":"smoke Lote 5"}')" >/dev/null
echo "$SELV" | jq -e --arg t "$TR2" 'any(.[]; .trip.publicId==$t and (.canDispatch|not) and any(.issues[]; .code=="VEHICLE_UNAVAILABLE" and .blocking))' >/dev/null || fail "selector con VEHICLE_UNAVAILABLE: $(echo "$SELV" | jq -c --arg t "$TR2" '.[] | select(.trip.publicId==$t) | .issues')"
expect 422 "$RDV" | jq -e '(.title | contains("no se puede despachar")) and (.title | contains("El vehículo no está disponible para despacho:"))' >/dev/null || fail "despachar con el vehículo en taller"
expect 409 "$RCV" | jq -e '.title | startswith("El vehículo no está disponible para despacho")' >/dev/null || fail "alta de ruta con vehículo en taller"
expect 409 "$RPV" | jq -e '.title | startswith("El vehículo no está disponible para despacho")' >/dev/null || fail "cambiar a un vehículo en taller"
dispatchable | jq -e --arg t "$TR2" 'any(.[]; .trip.publicId==$t and .canDispatch)' >/dev/null || fail "$TR2_CODE vuelve a ser despachable al cancelar la OT"
CAPS='/api/v1/status/capabilities/TRANSPORT_ORDER?statusDomain=OrderStatus'
# Cada cambio de configuración se restaura ANTES de verificar la respuesta capturada.
expect 200 "$(req PUT "$CAPS" '[{"statusCode":"CONFIRMED","capability":"ASSIGN_TRIP","isAllowed":false}]')" >/dev/null
RD=$(dispatch "$TR2")
expect 200 "$(req PUT "$CAPS" '[{"statusCode":"CONFIRMED","capability":"ASSIGN_TRIP","isAllowed":true}]')" >/dev/null
expect 422 "$RD" | jq -e --arg m "Orden $(num "$OB2"): El estatus actual no permite la acción 'ASSIGN_TRIP'." '(.title | contains($m)) or ([(.errors // {})[][]] | any(contains($m)))' >/dev/null || fail "despachar con ASSIGN_TRIP apagada"
expect 200 "$(req PUT /api/v1/status/TripStatus/DISPATCHED/override '{"isEnabled":false}')" >/dev/null
RD=$(dispatch "$TR2")
expect 200 "$(req PUT /api/v1/status/TripStatus/DISPATCHED/override '{"isEnabled":true}')" >/dev/null
expect 422 "$RD" | jq -e '.title=="El pipeline de rutas de esta compañía no tiene habilitada la etapa DISPATCHED."' >/dev/null || fail "despachar sin la etapa DISPATCHED"
trip "$TR2" | jq -e '.statusCode=="DRAFT" and .isEditable' >/dev/null || fail "$TR2_CODE sigue abierta tras los 422"
ok "DRIVER_UNAVAILABLE en el selector y 422 al despachar; vehículo con OT en proceso: VEHICLE_UNAVAILABLE, 422 al despachar y 409 al asignarlo (alta y PATCH); 'Orden …: El estatus actual no permite la acción 'ASSIGN_TRIP'.'; 422 sin la etapa DISPATCHED; chofer, capacidad y etapa restaurados"

step "despacho y salida (Lote 5): congela la ruta, órdenes a PLANNED; la salida las lleva a IN_TRANSIT"
dispatchable | jq -e --arg t "$TR3" 'any(.[]; .trip.publicId==$t and (.canDispatch|not) and any(.issues[]; .code=="NO_VEHICLE"))' >/dev/null || fail "$TR3_CODE sin vehículo en el selector"
expect 422 "$(dispatch "$TR3")" >/dev/null
NTRIPS0=$(expect 200 "$(req GET "/api/v1/drivers/$D1/trips?includeCancelled=true")" | jq length)
OT1=$(trip "$TR1" | jq -r '.stops[0].orderPublicId'); OT1_ID=$(expect 200 "$(req GET "/api/v1/orders/$OT1")" | jq -r .id)
expect 200 "$(dispatch "$TR1")" | jq -e '.statusCode=="DISPATCHED" and .routeStatusCode=="ACTIVE" and (.isEditable|not) and (.canEditHeader|not)' >/dev/null || fail "despachar $TR1_CODE"
[[ $(ostatus "$OT1") == PLANNED ]] || fail "la orden despachada no quedó en PLANNED"
[[ $(ostatus "$(pid "$OX1")") == PLANNED ]] || fail "la orden del segundo cliente no quedó en PLANNED al despachar la ruta consolidada"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$OT1_ID")" | jq -e --arg c "Despachada en la ruta $TR1_CODE" '(map(.toCode) | index("PICKUP") and index("INBOUND") and index("PLANNED")) and any(.[]; .comment==$c) and all(.[]; .toCode!="IN_TRANSIT")' >/dev/null || fail "historial del despacho de la orden"
dispatchable | jq -e --arg t "$TR1" 'all(.[]; .trip.publicId!=$t)' >/dev/null || fail "el selector sigue mostrando $TR1_CODE"
FROZEN="La ruta $TR1_CODE ya fue despachada; no se puede editar ni eliminar."
expect 422 "$(req PATCH "/api/v1/trips/$TR1" "{\"plannedStartUtc\":\"${TODAY}T13:00:00Z\"}")" | jq -e --arg m "$FROZEN" '.title==$m' >/dev/null || fail "PATCH de una ruta despachada"
expect 422 "$(addo "$TR1" "$(pid "$OA5")")" | jq -e --arg m "$FROZEN" '.title==$m' >/dev/null || fail "agregar a una ruta despachada"
expect 422 "$(req POST "/api/v1/trips/$TR1/optimize" '{}')" | jq -e --arg m "$FROZEN" '.title==$m' >/dev/null || fail "optimizar una ruta despachada"
expect 422 "$(req PUT "/api/v1/trips/$TR1/route/sequence" "$(trip "$TR1" | jq -c '{routeStopIds:[.stops[].id]}')")" | jq -e --arg m "$FROZEN" '.title==$m' >/dev/null || fail "reordenar una ruta despachada"
expect 422 "$(loc5 "$TR1" "$S1" '{"lat":18.4,"lng":-66.1}')" | jq -e --arg m "$FROZEN" '.title==$m' >/dev/null || fail "pin en una ruta despachada"
expect 422 "$(req DELETE "/api/v1/trips/$TR1")" | jq -e --arg m "$FROZEN" '.title==$m' >/dev/null || fail "eliminar una ruta despachada"
LAT='/api/v1/status/lateral-entries/TRANSPORT_ORDER?statusDomain=OrderStatus'
expect 200 "$(req PUT "$LAT" '[{"lateralStatusCode":"CANCELLED","fromStatusCode":"PLANNED","isAllowed":true}]')" >/dev/null
RC1=$(req POST "/api/v1/orders/$OT1/cancel" '{}')
expect 200 "$(req PUT "$LAT" '[{"lateralStatusCode":"CANCELLED","fromStatusCode":"PLANNED","isAllowed":false}]')" >/dev/null
expect 422 "$RC1" | jq -e --arg m "La orden va en la ruta $TR1_CODE ya despachada; no se puede cancelar mientras la ruta esté en curso." '.title==$m' >/dev/null || fail "cancelar una orden de una ruta despachada"
expect 200 "$(req POST "/api/v1/trips/$TR1/start" '{}')" | jq -e '.statusCode=="IN_PROGRESS" and .actualStartUtc != null' >/dev/null || fail "salida de $TR1_CODE"
[[ $(ostatus "$OT1") == IN_TRANSIT ]] || fail "la salida no llevó la orden a IN_TRANSIT"
expect 200 "$(req GET "/api/v1/status/history/TRANSPORT_ORDER/$OT1_ID")" | jq -e --arg c "Salió en la ruta $TR1_CODE" 'any(.[]; .toCode=="IN_TRANSIT" and .comment==$c)' >/dev/null || fail "comentario de la salida"
expect 422 "$(req POST "/api/v1/trips/$TR1/start" '{}')" | jq -e --arg m "La ruta $TR1_CODE ya salió." '.title==$m' >/dev/null || fail "segunda salida"
expect 422 "$(req POST "/api/v1/trips/$TR3/start" '{}')" | jq -e --arg m "La ruta $TR3_CODE no está despachada; despáchela antes de registrar su salida." '.title==$m' >/dev/null || fail "salida de una ruta no despachada"
[[ $(expect 200 "$(req GET "/api/v1/drivers/$D1/trips?includeCancelled=true")" | jq length) -eq $NTRIPS0 ]] || fail "despachar creó un viaje del chofer (DriverTrip)"
FOREIGN=$(randuuid)
BR=$(expect 200 "$(req POST /api/v1/trips/dispatch "$(jq -cn --arg a "$TR2" --arg b "$TR3" --arg c "$FOREIGN" '{tripPublicIds:[$a,$b,$c]}')")")
echo "$BR" | jq -e --arg a "$TR2" --arg b "$TR3" --arg c "$FOREIGN" '.requested==3 and .dispatched==1 and ([.items[].tripPublicId]==[$a,$b,$c]) and .items[0].dispatched and (.items[1].dispatched|not) and any(.items[1].issues[]; .code=="NO_VEHICLE") and .items[2].error=="Ruta no encontrada."' >/dev/null || fail "despacho en lote: $BR"
ok "$TR1_CODE DISPATCHED/ACTIVE; orden PLANNED con PICKUP, INBOUND, PLANNED y 'Despachada en la ruta …'; contenido y cabecera congelados (422); cancelar una orden despachada 422; salida → IN_PROGRESS e IN_TRANSIT; 422 (ya salió / no despachada); sin DriverTrip; lote {$TR2_CODE, $TR3_CODE, ajeno} = 3/1"

step "cabecera con EDIT_TRIP (Lote 5): configurable por compañía; fecha, zona y contenido siguen congelados"
TCAPS='/api/v1/status/capabilities/TRIP?statusDomain=TripStatus'
FROZEN2="La ruta $TR2_CODE ya fue despachada; no se puede editar ni eliminar."
REQD="Una ruta despachada debe conservar chofer, vehículo y hora de salida; cámbielos en lugar de quitarlos."
expect 422 "$(req PATCH "/api/v1/trips/$TR2" "{\"driverPublicId\":\"$D1\"}")" | jq -e --arg m "$FROZEN2" '.title==$m' >/dev/null || fail "PATCH de una ruta despachada sin EDIT_TRIP"
expect 200 "$(req PUT "$TCAPS" '[{"statusCode":"DISPATCHED","capability":"EDIT_TRIP","isAllowed":true}]')" >/dev/null
RE1=$(req PATCH "/api/v1/trips/$TR2" "{\"driverPublicId\":\"$D1\"}"); RE2=$(req PATCH "/api/v1/trips/$TR2" "{\"planDate\":\"$TOMORROW\"}"); RE3=$(addo "$TR2" "$(pid "$OB3")")
RE4=$(req PATCH "/api/v1/trips/$TR2" '{"clearDriver":true}'); RE5=$(req PATCH "/api/v1/trips/$TR2" '{"clearVehicle":true}'); RE6=$(req PATCH "/api/v1/trips/$TR2" '{"clearPlannedStart":true}')
expect 200 "$(req PUT "$TCAPS" '[{"statusCode":"DISPATCHED","capability":"EDIT_TRIP","isAllowed":false}]')" >/dev/null
expect 200 "$RE1" | jq -e --arg c "R1$TS" '.driverCode==$c and .canEditHeader and .statusCode=="DISPATCHED"' >/dev/null || fail "cambiar el chofer de una ruta despachada con EDIT_TRIP"
expect 422 "$RE2" | jq -e '.title=="La fecha y la zona de una ruta despachada no se cambian."' >/dev/null || fail "fecha de una ruta despachada"
expect 422 "$RE3" | jq -e --arg m "$FROZEN2" '.title==$m' >/dev/null || fail "agregar a una ruta despachada con EDIT_TRIP"
for R in "$RE4" "$RE5" "$RE6"; do expect 422 "$R" | jq -e --arg m "$REQD" '.title==$m' >/dev/null || fail "quitar chofer, vehículo u hora de salida de una ruta despachada"; done
trip "$TR2" | jq -e --arg c "R1$TS" '.driverCode==$c and .vehicleCode!=null and .plannedStartUtc!=null' >/dev/null || fail "la ruta despachada perdió chofer, vehículo u hora de salida"
expect 422 "$(req PATCH "/api/v1/trips/$TR2" "{\"driverPublicId\":\"$D2\"}")" | jq -e --arg m "$FROZEN2" '.title==$m' >/dev/null || fail "PATCH tras restaurar EDIT_TRIP"
ok "sin EDIT_TRIP 422; con EDIT_TRIP el chofer cambia (200) pero la fecha no (422), el contenido tampoco (422) y chofer, vehículo y hora de salida no se quitan (422); capacidad restaurada"

step "reasignación en bloque (Lote 5): solo rutas abiertas de esas zonas y esa fecha; no toca la zona del chofer"
TRMJ=$(expect 200 "$(req POST /api/v1/trips "{\"planDate\":\"$TOMORROW\",\"dispatchZoneId\":$Z1}")"); TRM=$(pid "$TRMJ")
TR5=$(pid "$(expect 200 "$(tpost "{\"dispatchZoneId\":$Z2}")")")
TRC=$(pid "$(expect 200 "$(tpost "{\"dispatchZoneId\":$Z1}")")"); expect 204 "$(req DELETE "/api/v1/trips/$TRC")" >/dev/null   # eliminada (CANCELLED)
D2ZONE=$(expect 200 "$(req GET "/api/v1/drivers/$D2")" | jq -c .zoneCode)
reassign() { req POST /api/v1/trips/reassign-zone "$(jq -cn --arg d "$TODAY" --argjson z "$1" --arg p "$2" '{planDate:$d,dispatchZoneIds:$z,driverPublicId:$p}')" "${3:-$TOKEN}"; }
# EDIT_TRIP negada en DRAFT (TR3 y TR5 lo están): el PATCH y la reasignación la respetan; se restaura antes de verificar.
expect 200 "$(req PUT "$TCAPS" '[{"statusCode":"DRAFT","capability":"EDIT_TRIP","isAllowed":false}]')" >/dev/null
RAX=$(reassign "[$Z1,$Z2]" "$D2"); RPX=$(req PATCH "/api/v1/trips/$TR5" "{\"driverPublicId\":\"$D2\"}")
expect 200 "$(req PUT "$TCAPS" '[{"statusCode":"DRAFT","capability":"EDIT_TRIP","isAllowed":true}]')" >/dev/null
expect 200 "$RAX" | jq -e '.tripsUpdated==0 and (.trips | length)==0' >/dev/null || fail "la reasignación ignoró EDIT_TRIP negada: $RAX"
expect 422 "$RPX" >/dev/null
RA=$(expect 200 "$(reassign "[$Z1,$Z2]" "$D2")")
echo "$RA" | jq -e --arg c "R2$TS" --arg a "$TR3" --arg b "$TR5" '.tripsUpdated==2 and ([.trips[].publicId] | sort)==([$a,$b] | sort) and all(.trips[]; .driverCode==$c) and any(.issues[]; .code=="DRIVER_DOUBLE_BOOKED")' >/dev/null || fail "reasignación: $RA"
trip "$TR1" | jq -e --arg c "R1$TS" '.driverCode==$c' >/dev/null || fail "la ruta en curso no cambia de chofer"
trip "$TR2" | jq -e --arg c "R1$TS" '.driverCode==$c' >/dev/null || fail "la ruta despachada no cambia de chofer"
trip "$TRM" | jq -e --arg c "R1$TS" '.driverCode==$c' >/dev/null || fail "la ruta de mañana no cambia de chofer"
trip "$TRC" | jq -e --arg c "R1$TS" '.driverCode==$c and .statusCode=="CANCELLED"' >/dev/null || fail "la ruta eliminada no cambia de chofer"
[[ $(expect 200 "$(req GET "/api/v1/drivers/$D2")" | jq -c .zoneCode) == "$D2ZONE" ]] || fail "la reasignación tocó la zona del chofer"
expect 409 "$(reassign "[$Z1]" "$DX")" | jq -e '.title | startswith("El chofer no está disponible para despacho")' >/dev/null || fail "reasignar a un chofer no disponible"
expect 400 "$(reassign "[]" "$D2")" | jq -e --arg m "Indique al menos una zona." "$HASM" >/dev/null || fail "sin zonas"
expect 400 "$(reassign "$(jq -cn '[range(1;52)]')" "$D2")" | jq -e --arg m "Máximo 50 zonas por reasignación." "$HASM" >/dev/null || fail "51 zonas"
expect 404 "$(reassign "[$ZT3]" "$D2")" >/dev/null
expect 403 "$(reassign "[$Z1]" "$D2" "$TREAD")" >/dev/null
expect 200 "$(reassign "[$Z6]" "$D2")" | jq -e '.tripsUpdated==0' >/dev/null || fail "zona sin rutas abiertas"
ok "con EDIT_TRIP negada en DRAFT no se reasigna nada (200 con 0) y el PATCH da 422; 2 rutas abiertas (Z1/Z2 de hoy) pasan a R2 con aviso DRIVER_DOUBLE_BOOKED; en curso, despachada, eliminada y de mañana intactas; zona del chofer intacta; 409/400/400/404/403 y 200 con 0"

step "eliminar ruta y cancelar órdenes (Lote 5): libera sin retroceder estatus"
TR4J=$(expect 200 "$(tpost "{\"dispatchZoneId\":$Z2}")"); TR4=$(pid "$TR4J"); TR4_CODE=$(echo "$TR4J" | jq -r .code)
expect 200 "$(addo "$TR4" "$(pid "$OB3")" "$(pid "$OB4")")" >/dev/null
expect 200 "$(req POST "/api/v1/orders/$(pid "$OB3")/cancel" '{}')" | jq -e '.status=="CANCELLED"' >/dev/null || fail "cancelar una orden de una ruta abierta"
trip "$TR4" | jq -e '.stopCount==1' >/dev/null || fail "cancelar la orden no la liberó de la ruta"
expect 204 "$(req DELETE "/api/v1/trips/$TR4" '{"comment":"Ruta de prueba"}')" >/dev/null
trip "$TR4" | jq -e '.statusCode=="CANCELLED" and (.isActive|not)' >/dev/null || fail "ruta eliminada"
CLOSED="La ruta $TR4_CODE está cerrada; solo se consulta."
expect 422 "$(req PATCH "/api/v1/trips/$TR4" "{\"plannedStartUtc\":\"${TODAY}T13:00:00Z\"}")" | jq -e --arg m "$CLOSED" '.title==$m' >/dev/null || fail "PATCH de una ruta eliminada"
expect 422 "$(req DELETE "/api/v1/trips/$TR4")" | jq -e --arg m "$CLOSED" '.title==$m' >/dev/null || fail "eliminar dos veces una ruta"
expect 422 "$(addo "$TR4" "$(pid "$OB4")")" | jq -e --arg m "$CLOSED" '.title==$m' >/dev/null || fail "agregar a una ruta eliminada"
# Listado: las eliminadas se ocultan salvo includeCancelled o filtro de estatus; estatus desconocido y rango invertido → 400;
# la búsqueda corre después del filtro de zona.
expect 200 "$(req GET "/api/v1/trips?date=$TODAY")" | jq -e --arg a "$TR4" 'all(.[]; .publicId!=$a)' >/dev/null || fail "la ruta eliminada aparece sin includeCancelled"
expect 200 "$(req GET "/api/v1/trips?date=$TODAY&includeCancelled=true")" | jq -e --arg a "$TR4" 'any(.[]; .publicId==$a)' >/dev/null || fail "includeCancelled no trae la eliminada"
expect 200 "$(req GET "/api/v1/trips?date=$TODAY&status=CANCELLED")" | jq -e --arg a "$TR4" 'any(.[]; .publicId==$a) and all(.[]; .statusCode=="CANCELLED")' >/dev/null || fail "filtro de estatus"
expect 400 "$(req GET "/api/v1/trips?status=FOO")" | jq -e --arg m "Estatus de ruta desconocido: 'FOO'." "$HASM" >/dev/null || fail "estatus desconocido"
expect 400 "$(req GET "/api/v1/trips?from=$TOMORROW&to=$TODAY")" | jq -e --arg m "El rango de fechas es inválido." "$HASM" >/dev/null || fail "rango de fechas invertido"
expect 200 "$(req GET "/api/v1/trips?date=$TODAY&dispatchZoneId=$Z2&search=$TR1_CODE")" | jq -e 'length==0' >/dev/null || fail "la búsqueda salta el filtro de zona"
expect 200 "$(req GET "/api/v1/trips?date=$TODAY&dispatchZoneId=$Z1&search=$TR1_CODE")" | jq -e --arg a "$TR1" 'any(.[]; .publicId==$a)' >/dev/null || fail "búsqueda por código dentro de la zona"
expect 200 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z2&search=$(pb "$OB4")")" | jq -e '.total==1 and .items[0].statusCode=="CONFIRMED"' >/dev/null || fail "la orden de la ruta eliminada vuelve a sin asignar"
expect 204 "$(req DELETE "/api/v1/trips/$TR3")" >/dev/null
expect 200 "$(scan "$(pb "$OA5")")" | jq -e --arg m "No hay ruta abierta para la zona Z1$TS en la fecha $TODAY; queda sin asignar." '.outcome=="FOUND_UNASSIGNED" and .reasonCode=="NO_OPEN_ROUTE" and .message==$m' >/dev/null || fail "escaneo sin ruta abierta"
ok "cancelar libera (stopCount 1); DELETE → CANCELLED/isActive false y la orden vuelve CONFIRMED a sin asignar; ruta cerrada: PATCH, segundo DELETE y agregar → 422 'está cerrada'; listado oculta la eliminada salvo includeCancelled/estatus, 400 (estatus desconocido, rango), búsqueda después de la zona; sin ruta abierta en Z1 el escaneo responde NO_OPEN_ROUTE"

step "monitoreo (Lote 5): despachadas y en curso, totales sobre la fecha y búsqueda solo sobre la lista"
MON=$(expect 200 "$(req GET "/api/v1/trips/monitor?date=$TODAY")")
echo "$MON" | jq -e --arg a "$TR1" --arg b "$TR2" '(.trips | map(.publicId) | index($a) and index($b)) and (.trips[] | select(.publicId==$a) | .statusCode=="IN_PROGRESS" and .completedStops==0 and .nextEtaUtc != null and .lastPing==null) and .totals.trips >= 2 and all(.trips[]; .statusCode!="DRAFT" and .statusCode!="PLANNED")' >/dev/null || fail "monitor: $(echo "$MON" | jq -c '.totals')"
echo "$MON" | jq -e --arg a "$TR1" '(.trips[] | select(.publicId==$a) | .overStopLimit) and .totals.overStopLimitTrips >= 1' >/dev/null || fail "alerta de máximo de paradas en el monitor: $(echo "$MON" | jq -c '.totals')"
MONS=$(expect 200 "$(req GET "/api/v1/trips/monitor?date=$TODAY&search=$TR1_CODE")")
[[ $(echo "$MONS" | jq '.trips | length') -eq 1 && $(echo "$MONS" | jq -c .totals) == $(echo "$MON" | jq -c .totals) ]] || fail "la búsqueda cambió los totales del monitor"
# Pings (maestro L268): el API de pings es del Lote 7, así que se insertan por SQL, solo si SMOKE_SQL está definida (el CI la
# define con sqlcmd dentro del contenedor de SQL Server; en local es opcional). Primero el respaldo del chofer (TripId NULL):
# gana el último desde ActualStartUtc y los anteriores a la salida no cuentan; después un ping con TripId gana al respaldo.
PINGS="omitidos (sin SMOKE_SQL)"
if [[ -n "${SMOKE_SQL:-}" ]]; then
  ping_sql() { $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; INSERT dbo.DriverLocationPing (TenantId, DriverId, TripId, GeoPoint, CapturedAtUtc) SELECT TenantId, DriverId, $1, geography::Point($2, $3, 4326), $4 FROM dbo.Trip WHERE TripId = $TR1_ID;" >/dev/null; }
  mon1() { expect 200 "$(req GET "/api/v1/trips/monitor?date=$TODAY")"; }
  WP0=$(echo "$MON" | jq .totals.tripsWithoutPing)
  ping_sql NULL 18.10 -66.10 "DATEADD(minute, -5, ActualStartUtc)"
  ping_sql NULL 18.20 -66.20 "DATEADD(second, 1, ActualStartUtc)"
  ping_sql NULL 18.30 -66.30 "DATEADD(minute, -10, ActualStartUtc)"
  M1=$(mon1)
  echo "$M1" | jq -e --arg a "$TR1" --argjson w "$WP0" '(.trips[] | select(.publicId==$a) | .lastPing.linkedToTrip==false and .lastPing.point.lat==18.2 and .lastPing.point.lng==-66.2) and .totals.tripsWithoutPing==($w - 1)' >/dev/null || fail "ping de respaldo del chofer: $(echo "$M1" | jq -c --arg a "$TR1" '{t: .totals, p: (.trips[] | select(.publicId==$a) | .lastPing)}')"
  ping_sql "$TR1_ID" 18.45 -66.07 "ActualStartUtc"
  M2=$(mon1)
  echo "$M2" | jq -e --arg a "$TR1" --argjson w "$WP0" '(.trips[] | select(.publicId==$a) | .lastPing.linkedToTrip==true and .lastPing.point.lat==18.45) and .totals.tripsWithoutPing==($w - 1)' >/dev/null || fail "ping de la ruta: $(echo "$M2" | jq -c --arg a "$TR1" '(.trips[] | select(.publicId==$a) | .lastPing)')"
  trip "$TR1" | jq -e '.lastPing.linkedToTrip==true and .lastPing.point.lat==18.45' >/dev/null || fail "ficha con el último ping de la ruta"
  PINGS="respaldo del chofer desde la salida (linkedToTrip false) y ping de la ruta que gana (true); tripsWithoutPing −1"
fi
ok "$TR1_CODE (IN_PROGRESS, 0 completadas, próxima ETA, sin ping) y $TR2_CODE; buscar deja 1 ruta y los mismos totales; pings: $PINGS"

step "sin dinero (Lote 5): ningún JSON de rutas expone montos ni tarifas"
for P in "/api/v1/trips/$TR1" "/api/v1/trips?date=$TODAY" "/api/v1/trips/monitor?date=$TODAY" "/api/v1/trips/dispatchable?date=$TODAY" "/api/v1/trips/$TR1/optimization-runs"; do
  expect 200 "$(req GET "$P")" | jq -e '[paths | .[] | strings | ascii_downcase | select(contains("amount") or contains("rate"))] | length==0' >/dev/null || fail "claves de dinero en $P"
done
ok "ficha, listado, monitor, selector y corridas sin claves 'amount'/'rate'; despachar no creó DriverTrip (paso anterior)"

step "RBAC y módulo (Lote 5)"
PD0=$(pdtotal)
expect 200 "$(req GET "/api/v1/trips?date=$TODAY" '' "$TREAD")" >/dev/null
expect 403 "$(tpost '{}' "$TREAD")" >/dev/null
expect 403 "$(req POST "/api/v1/trips/$TRP/optimize" '{}' "$TREAD")" >/dev/null
expect 403 "$(dispatch "$TRP" "$TREAD")" >/dev/null
expect 403 "$(req POST "/api/v1/trips/$TR2/start" '{}' "$TREAD")" >/dev/null
expect 403 "$(loc5 "$TRP" "$(trip "$TRP" | jq -r '.stops[0].id')" '{"lat":18.4,"lng":-66.1}' "$TREAD")" >/dev/null
[[ $(pdtotal) -gt $PD0 ]] || fail "PERMISSION_DENIED en rutas"
expect 403 "$(req GET "/api/v1/trips?date=$TODAY" '' "$TBILL")" >/dev/null
expect 403 "$(req GET "/api/v1/trips?date=$TODAY" '' "$T7")" >/dev/null
expect 403 "$(tpost '{}' "$TWH")" >/dev/null
expect 200 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z1" '' "$TWH")" >/dev/null
TT2=$(pid "$(expect 200 "$(tpost '{}' "$T2")")"); expect 204 "$(req DELETE "/api/v1/trips/$TT2" '' "$T2")" >/dev/null
expect 200 "$(req PATCH "/api/v1/trips/$TRP" "{\"vehiclePublicId\":\"$V2\"}" "$T2")" >/dev/null
expect 200 "$(req POST "/api/v1/trips/$TRP/optimize" '{}' "$T2")" >/dev/null
expect 200 "$(dispatch "$TRP" "$T2")" | jq -e '.statusCode=="DISPATCHED"' >/dev/null || fail "el Despachador despacha"
expect 200 "$(req POST "/api/v1/trips/$TRP/start" '{}' "$T2")" | jq -e '.statusCode=="IN_PROGRESS"' >/dev/null || fail "el Despachador registra la salida"
# Con CATALOG apagado solo se capturan las respuestas; el módulo se vuelve a encender ANTES de verificarlas.
expect 200 "$(req PUT /api/v1/modules/CATALOG '{"isEnabled":false}')" >/dev/null
TMD=$(tpost "{\"driverPublicId\":\"$D2\"}"); TNC=$(tpost "{\"dispatchZoneId\":$Z1}")
# Las guardas de CATALOG van antes de bloquear o escribir: TRP (en curso) no cambia de estado.
TMDISP=$(dispatch "$TRP"); TMBAT=$(req POST /api/v1/trips/dispatch "{\"tripPublicIds\":[\"$TRP\"]}")
TMREA=$(reassign "[$Z1]" "$D2"); TMPD=$(req PATCH "/api/v1/trips/$TRP" "{\"driverPublicId\":\"$D2\"}"); TMPV=$(req PATCH "/api/v1/trips/$TRP" "{\"vehiclePublicId\":\"$V2\"}")
expect 200 "$(req PUT /api/v1/modules/CATALOG '{"isEnabled":true}')" >/dev/null
for R in "$TMD" "$TMDISP" "$TMBAT" "$TMREA" "$TMPD" "$TMPV"; do
  expect 403 "$R" | jq -e '.code=="module_disabled"' >/dev/null || fail "acción con chofer/vehículo y CATALOG apagado: $R"
done
TNC_PID=$(expect 200 "$TNC" | jq -r 'select(.driverCode==null) | .publicId'); [[ -n "$TNC_PID" ]] || fail "con CATALOG apagado no hay chofer por defecto"
# Ruta sin chofer: bloqueante NO_DRIVER en el selector y 422 al despachar.
dispatchable | jq -e --arg t "$TNC_PID" 'any(.[]; .trip.publicId==$t and (.canDispatch|not) and any(.issues[]; .code=="NO_DRIVER" and .blocking))' >/dev/null || fail "selector con NO_DRIVER"
expect 422 "$(dispatch "$TNC_PID")" | jq -e '(.title | contains("no se puede despachar")) and (.title | contains("La ruta no tiene chofer asignado"))' >/dev/null || fail "despachar una ruta sin chofer"
expect 204 "$(req DELETE "/api/v1/trips/$TNC_PID")" >/dev/null
ok "Solo lectura: lee (200) y todo lo demás 403 con PERMISSION_DENIED; Facturación y contactos 403; Operador de almacén sin alta (403) pero con 'sin asignar' (200); el Despachador crea, optimiza, despacha y registra la salida; CATALOG apagado: alta con chofer, despacho, despacho en lote, reasignación y PATCH de chofer/vehículo → 403 module_disabled, sin chofer por defecto (restaurado); ruta sin chofer: NO_DRIVER en el selector y 422 al despachar"

step "aislamiento y BOLA por id hijo (Lote 5): otro tenant no alcanza rutas, paradas, órdenes ni zonas"
NF='.title=="Ruta no encontrada."'
expect 404 "$(req GET "/api/v1/trips/$TR1" '' "$T3")" | jq -e "$NF" >/dev/null || fail "ficha de otro tenant"
expect 404 "$(req PATCH "/api/v1/trips/$TR1" '{"plannedStartUtc":null}' "$T3")" | jq -e "$NF" >/dev/null || fail "PATCH de otro tenant"
expect 404 "$(req DELETE "/api/v1/trips/$TR1" '' "$T3")" | jq -e "$NF" >/dev/null || fail "DELETE de otro tenant"
expect 404 "$(dispatch "$TR1" "$T3")" | jq -e "$NF" >/dev/null || fail "despachar de otro tenant"
expect 404 "$(req POST "/api/v1/trips/$TR1/start" '{}' "$T3")" | jq -e "$NF" >/dev/null || fail "salida de otro tenant"
expect 404 "$(req DELETE "/api/v1/trips/$TR2/orders/$(pid "$OB2")" '' "$T3")" >/dev/null
T3R=$(pid "$(expect 200 "$(tpost '{}' "$T3")")")
expect 404 "$(req POST "/api/v1/trips/$T3R/orders" "{\"orderPublicIds\":[\"$(pid "$OA3")\"]}" "$T3")" | jq -e '.title=="Orden no encontrado."' >/dev/null || fail "orden de otro tenant en una ruta propia"
expect 400 "$(req PUT "/api/v1/trips/$T3R/route/sequence" "$(trip "$TR1" | jq -c '{routeStopIds:[.stops[].id]}')" "$T3")" >/dev/null
expect 404 "$(loc5 "$T3R" "$S1" '{"lat":18.4,"lng":-66.1}' "$T3")" | jq -e '.title=="Parada no encontrada en esta ruta."' >/dev/null || fail "pin de una parada de otro tenant"
expect 404 "$(plan "{\"planDate\":\"$TODAY\",\"dispatchZoneIds\":[$Z1]}" "$T3")" >/dev/null
ZNF='.title=="Zona de despacho no encontrada."'
expect 404 "$(tpost "{\"dispatchZoneId\":$Z1}" "$T3")" | jq -e "$ZNF" >/dev/null || fail "alta de ruta con zona de otro tenant"
expect 404 "$(req PATCH "/api/v1/trips/$T3R" "{\"dispatchZoneId\":$Z1}" "$T3")" | jq -e "$ZNF" >/dev/null || fail "PATCH de ruta con zona de otro tenant"
expect 404 "$(req GET "/api/v1/trips/unassigned-orders?dispatchZoneId=$Z1" '' "$T3")" | jq -e "$ZNF" >/dev/null || fail "sin asignar con zona de otro tenant"
expect 404 "$(req GET "/api/v1/trips?driverPublicId=$D1" '' "$T3")" | jq -e '.title=="Chofer no encontrado."' >/dev/null || fail "listado filtrado por chofer de otro tenant"
expect 200 "$(scan "$(pb "$OA4")" "$T3")" | jq -e '.outcome=="NOT_FOUND"' >/dev/null || fail "escaneo de una orden de otro tenant"
expect 200 "$(req GET "/api/v1/trips/monitor?date=$TODAY" '' "$T3")" | jq -e --arg a "$TR1" 'all(.trips[]; .publicId!=$a)' >/dev/null || fail "monitor con rutas ajenas"
expect 200 "$(req GET "/api/v1/trips?date=$TODAY" '' "$T3")" | jq -e --arg a "$TR1" 'all(.[]; .publicId!=$a)' >/dev/null || fail "listado con rutas ajenas"
expect 404 "$(req GET "/api/v1/dispatch-zones/$Z1/members" '' "$T3")" >/dev/null
H=$(req GET "/api/v1/status/history/ROUTE/$V1ROUTE" '' "$T3"); [[ $(echo "$H" | tail -n1) == 404 || $(echo "$H" | sed '$d' | jq length) == 0 ]] || fail "historial de ROUTE de otro tenant: $H"
trip "$TR1" | jq -e '.statusCode=="IN_PROGRESS" and .isActive' >/dev/null || fail "la ruta del demo cambió desde otro tenant"
ok "T3: 404 'Ruta no encontrada.' (ficha, PATCH, DELETE, despacho, salida), 404 al quitar una orden ajena, 'Orden no encontrado.' en su ruta, 400 con paradas ajenas en la secuencia, 404 al fijar el pin de una parada ajena, 404 al planificar una zona ajena, 404 'Zona de despacho no encontrada.' (alta, PATCH y sin asignar con zona ajena), 404 'Chofer no encontrado.' (listado por chofer ajeno), NOT_FOUND al escanear, monitor/listado sin rutas ajenas, miembros de zona 404 e historial de ROUTE vacío"

step "contactos, campos personalizados e historial de rutas (Lote 5): resolvers de TRIP, ROUTE, ROUTE_STOP y OPTIMIZATION_RUN"
DEFT=$(req POST /api/v1/custom-fields/definitions/TRIP '{"fieldKey":"sello","labels":{"es":"Sello","en":"Seal"},"dataType":"TEXT","isRequired":false,"isUnique":false,"showInList":true}')
CODE=$(echo "$DEFT" | tail -n1); [[ "$CODE" == "200" || "$CODE" == "409" ]] || fail "definición TRIP: $DEFT"
expect 200 "$(req PUT "/api/v1/custom-fields/values/TRIP/$TR1_ID" "{\"values\":{\"sello\":\"S-$TS\"}}")" | jq -e --arg v "S-$TS" 'any(.[]; .fieldKey=="sello" and .value==$v)' >/dev/null || fail "campo personalizado de la ruta"
expect 403 "$(req PUT "/api/v1/custom-fields/values/TRIP/$TR1_ID" '{"values":{"sello":"X"}}' "$TREAD")" | jq -e '.title=="Falta el permiso '"'"'trips.plan'"'"'."' >/dev/null || fail "campo de la ruta sin trips.plan"
# Con trips.plan pero sin trips.view: 403 ANTES de guardar (la respuesta relee con el permiso de lectura) y el valor no cambia.
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Solo planificar $TS\",\"permissions\":[\"trips.plan\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"planifica$TS@teikem.local\",\"fullName\":\"Solo planificar $TS\",\"password\":\"$PASS\",\"roles\":[\"Solo planificar $TS\"]}")" >/dev/null
TPLAN=$(login "planifica$TS@teikem.local" "$PASS")
expect 403 "$(req PUT "/api/v1/custom-fields/values/TRIP/$TR1_ID" '{"values":{"sello":"CAMBIADO"}}' "$TPLAN")" | jq -e '.title=="Falta el permiso '"'"'trips.view'"'"'."' >/dev/null || fail "campo de la ruta con trips.plan sin trips.view"
expect 200 "$(req GET "/api/v1/custom-fields/values/TRIP/$TR1_ID")" | jq -e --arg v "S-$TS" 'any(.[]; .fieldKey=="sello" and .value==$v)' >/dev/null || fail "el 403 sin trips.view guardó el valor"
for E in TRIP ROUTE ROUTE_STOP OPTIMIZATION_RUN; do
  expect 404 "$(req PUT "/api/v1/custom-fields/values/$E/999999" '{"values":{}}')" >/dev/null
done
RUN1=$(runs "$TR1" | jq -r '.[0].id')
expect 404 "$(req PUT "/api/v1/custom-fields/values/OPTIMIZATION_RUN/$RUN1" '{"values":{}}')" >/dev/null   # resolver cerrado: 404 aunque la corrida exista
RS1=$(trip "$TR1" | jq -r '.stops[0].id')
expect 200 "$(req PUT "/api/v1/custom-fields/values/ROUTE_STOP/$RS1" '{"values":{}}')" >/dev/null
for P in "TRIP/$TR1_ID" "ROUTE/$V1ROUTE" "ROUTE_STOP/$RS1"; do
  expect 404 "$(req PUT "/api/v1/custom-fields/values/$P" '{"values":{}}' "$T3")" >/dev/null
done
expect 403 "$(req POST "/api/v1/contacts/TRIP/$TR1_ID" '{"contactType":"PHONE","value":"787-555-0150"}' "$T7")" >/dev/null
H=$(req GET "/api/v1/status/history/TRIP/$TR1_ID" '' "$T3"); [[ $(echo "$H" | tail -n1) == 404 || $(echo "$H" | sed '$d' | jq length) == 0 ]] || fail "historial de TRIP de otro tenant: $H"
ok "campo personalizado de TRIP; 403 sin trips.plan y 403 sin trips.view sin guardar; 404 para ids inexistentes (TRIP/ROUTE/ROUTE_STOP/OPTIMIZATION_RUN) y para una corrida existente (cerrado); 404 desde T3 en TRIP/ROUTE/ROUTE_STOP; contacto de la ruta sin trips.plan 403; historial de TRIP vacío desde T3"

step "fuentes de datos, contenido de sistema y auditoría (Lote 5)"
DS=$(expect 200 "$(req GET /api/v1/analytics/data-sources)")
echo "$DS" | jq -e '(.[] | select(.key=="TRIP") | .dateField=="PlanDate" and all(.fields[]; (.key | ascii_downcase | (contains("amount") or contains("rate"))) | not)) and (.[] | select(.key=="TRANSPORT_ORDER") | [.fields[].key] as $f | ["TripCode","HasAssignedDriver","IsException","DispatchZoneCode"] | all(.[]; . as $x | $f | index($x)))' >/dev/null || fail "fuentes TRIP y TRANSPORT_ORDER del Lote 5"
RR=$(expect 200 "$(req GET /api/v1/analytics/reports)" | jq -r '[.[] | select(.name=="Rutas" and .isSystem==true)][0].id')
expect 200 "$(req POST "/api/v1/analytics/reports/$RR/run" '{}')" | jq -e --arg c "$TR1_CODE" 'any(.rows[]; .Code==$c)' >/dev/null || fail "vista 'Rutas'"
# Por valor (definición V5): asignar una orden libre a una ruta con chofer baja 'sin chofer' en 1; pasar a ON_HOLD una orden
# libre sube 'en excepción' en 1. La asignación a una variable propaga el fallo de indval (set -e).
SIN0=$(indval "Órdenes sin chofer asignado"); [[ $SIN0 -ge 1 ]] || fail "indicador 'Órdenes sin chofer asignado' = $SIN0 (ON1 no tiene chofer)"
expect 200 "$(addo "$TR5" "$(pid "$OB4")")" | jq -e --arg c "R2$TS" '.driverCode==$c' >/dev/null || fail "OB4 a una ruta abierta con chofer"
SIN1=$(indval "Órdenes sin chofer asignado"); [[ $SIN1 -eq $((SIN0 - 1)) ]] || fail "'Órdenes sin chofer asignado' no bajó al asignar OB4 a una ruta con chofer: $SIN0 → $SIN1"
EXC0=$(indval "Órdenes en excepción")
expect 200 "$(req POST "/api/v1/orders/$(pid "$OA5")/status" '{"toCode":"ON_HOLD","comment":"smoke Lote 5"}')" | jq -e '.status=="ON_HOLD"' >/dev/null || fail "OA5 a ON_HOLD"
EXC1=$(indval "Órdenes en excepción"); [[ $EXC1 -eq $((EXC0 + 1)) ]] || fail "'Órdenes en excepción' no subió con OA5 en ON_HOLD: $EXC0 → $EXC1"
CHR=$(expect 200 "$(req GET /api/v1/analytics/charts)" | jq -r '[.[] | select(.name=="Rutas por estatus")][0].id')
expect 200 "$(req GET "/api/v1/analytics/charts/$CHR/data")" | jq -e '.points | length >= 1' >/dev/null || fail "gráfico 'Rutas por estatus'"
for ET in TRIP ROUTE DISPATCH_ZONE TRANSPORT_ORDER; do
  expect 200 "$(req GET "/api/v1/audit/changes?entityType=$ET&take=20")" | jq -e '.total >= 1 and ([.items[] | select((.changesJson // "") | ascii_downcase | contains("rowversion"))] | length)==0' >/dev/null || fail "auditoría de $ET"
done
ok "TRIP (PlanDate, sin montos) y TRANSPORT_ORDER (TripCode, HasAssignedDriver, IsException, DispatchZoneCode); vista 'Rutas', indicadores por valor (sin chofer −1 al asignar, excepción +1 con ON_HOLD) y gráfico 'Rutas por estatus'; AuditLog de TRIP, ROUTE, DISPATCH_ZONE y TRANSPORT_ORDER sin rowVersion"

# ============================================================================================================
# Lote 6 — Inventario y almacén (WMS, compras mínimas, cruce de muelle demo). Re-ejecutable: TS en códigos y SKU.
# El tenant demo ya trae ALM-01 (D49), así que siempre hay más de un almacén: toda operación indica warehousePublicId.
# Lo que SOLO el smoke prueba: las 17 sentencias de InventoryQueries (UPDLOCK/HOLDLOCK, upsert, rangos, series con OPENJSON,
# EnsureLot — lotes, series y baja de posición tienen su paso), la
# traducción 547 → 409 de CK_StockBalance_Qty, las carreras y la conciliación ledger ↔ saldo sin descuadres al final.
# ============================================================================================================
TOKEN=$(login "$EMAIL" "$PASS"); T2=$(login "$DISPATCH_EMAIL" "$PASS"); T3=$(login "admin$TS@smoke.local" "Smoke_Admin_2026!")
wpid() { echo "$1" | jq -r .warehouse.publicId; }
kardex() { expect 200 "$(req GET "/api/v1/inventory/transactions?$1")"; }
onhand() { expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$1&binIds=$2&productPublicIds=$3&includeZero=true")" | jq '([.items[].qtyOnHand] | add // 0) + 0'; }
reserved() { expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$1&binIds=$2&productPublicIds=$3&includeZero=true")" | jq '([.items[].qtyReserved] | add // 0) + 0'; }
adjust() { req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$1" --arg w "$2" --argjson b "$3" --argjson q "$4" --arg r "$5" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:$q,reason:$r,notes:"humo"}')" "${6:-$TOKEN}"; }
collect() { req POST /api/v1/pick-batches "$(jq -cn --arg w "$1" --arg p "$2" --argjson q "$3" '{warehousePublicId:$w,lines:[{productPublicId:$p,quantity:$q}]}')" "${4:-$TOKEN}"; }
blind() { req POST /api/v1/receipts "$(jq -cn --arg w "$1" --argjson s "$2" --arg p "$3" --argjson q "$4" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:$q}]}')" "${5:-$TOKEN}"; }
tasksof() { expect 200 "$(req GET "/api/v1/warehouse-tasks?warehousePublicId=$1&types=$2&take=200")"; }
reconcile() { expect 200 "$(req GET /api/v1/inventory/reconciliation)"; }
codes() { local f; for f in "$@"; do echo "$(tail -n1 "$f")"; done | sort | tr '\n' ' '; }   # códigos HTTP de respuestas en paralelo, ordenados
onhandall() { expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$1&productPublicIds=$2&includeZero=true")" | jq '([.items[].qtyOnHand] | add // 0) + 0'; }
collectj() { req POST /api/v1/pick-batches "$1" "${2:-$TOKEN}"; }   # cuerpo JSON completo (varias líneas, series)
denied() { jq -e --arg m "Falta el permiso '$1'." '.title==$m' >/dev/null; }   # 403 del servicio con el permiso exacto
pdcount() { expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=1')" | jq .total; }
# Usuarios del lote, antes de los pasos que prueban las negativas de permisos que exige SOLO el servicio:
# Operador de almacén (recolecta; no empaca, no ajusta), Solo lectura (inventory.view) y un rol con inventory.adjust +
# purchasing.view + warehouse.receive SIN purchasing.manage ni purchasing.receive.
expect 200 "$(req POST /api/v1/users "{\"email\":\"bodega6$TS@teikem.local\",\"fullName\":\"Operador 6 $TS\",\"password\":\"$PASS\",\"roles\":[\"WarehouseOperator\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"lectura6$TS@teikem.local\",\"fullName\":\"Lectura 6 $TS\",\"password\":\"$PASS\",\"roles\":[\"ReadOnly\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Ajustes sin compras $TS\",\"permissions\":[\"inventory.view\",\"inventory.adjust\",\"purchasing.view\",\"warehouse.receive\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"ajustes6$TS@teikem.local\",\"fullName\":\"Ajustes 6 $TS\",\"password\":\"$PASS\",\"roles\":[\"Ajustes sin compras $TS\"]}")" >/dev/null
TWH6=$(login "bodega6$TS@teikem.local" "$PASS"); TREAD6=$(login "lectura6$TS@teikem.local" "$PASS"); TADJ6=$(login "ajustes6$TS@teikem.local" "$PASS")

step "catálogos, permisos y almacén demo (Lote 6)"
ME6=$(expect 200 "$(req GET /api/v1/me)")
echo "$ME6" | jq -e '[.permissions[]] as $p | all(["inventory.view","inventory.manage","inventory.adjust","warehouse.manage"][]; . as $x | $p | index($x))' >/dev/null || fail "el admin sin los 4 permisos nuevos"
expect 200 "$(req GET /api/v1/status/SerialStatus)" | jq -e 'any(.[]; .code=="SCRAPPED" and .stageKind=="TERMINAL") and any(.[]; .code=="SHIPPED" and .stageKind=="LATERAL")' >/dev/null || fail "SerialStatus (D16)"
expect 200 "$(req GET /api/v1/catalogs/AdjustmentReason)" | jq -e 'length==10 and any(.[]; .code=="OPENING_BALANCE")' >/dev/null || fail "AdjustmentReason (10, con OPENING_BALANCE del Lote 10)"
DEMO=$(expect 200 "$(req GET /api/v1/warehouses)" | jq -r '[.[] | select(.code=="ALM-01" and .statusCode=="ACTIVE")][0].publicId')
[[ "$DEMO" =~ ^[0-9a-f-]{36}$ ]] || fail "almacén demo ALM-01 ACTIVE (D49)"
expect 200 "$(req GET "/api/v1/warehouses/$DEMO")" | jq -e '([.zones[].code] | sort)==["PCK","QUA","RSV","STG"] and (.zones[] | select(.code=="STG") | .zoneTypeCode=="STAGING") and ([.docks[] | select(.statusCode=="FREE") | .code] | sort)==["D1","D2"]' >/dev/null || fail "zonas y muelles de ALM-01"
expect 200 "$(req GET "/api/v1/warehouses/$DEMO/bins?search=STG-01")" | jq -e 'any(.items[]; .code=="STG-01")' >/dev/null || fail "posición STG-01 de ALM-01"
ok "4 permisos nuevos, SerialStatus con SHIPPED lateral y SCRAPPED terminal, 9 motivos de ajuste; ALM-01 ACTIVE con STG/PCK/RSV/QUA, STG-01 y muelles D1/D2 FREE"

step "almacenes y ubicaciones (Lote 6): alta, zonas, posiciones, muelles y PATCH persistido"
W6=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"W6$TS\",\"name\":\"Almacén smoke $TS\"}")"); W6P=$(wpid "$W6")
echo "$W6" | jq -e '.warehouse.statusCode=="ACTIVE"' >/dev/null || fail "alta de almacén: $W6"
expect 409 "$(req POST /api/v1/warehouses "{\"code\":\"W6$TS\",\"name\":\"Otro\"}")" >/dev/null
zone() { expect 200 "$(req POST "/api/v1/warehouses/$W6P/zones" "{\"code\":\"$1\",\"name\":\"Zona $1\",\"zoneType\":\"$2\"}")" | jq -r .id; }
bin() { expect 200 "$(req POST "/api/v1/warehouses/$W6P/bins" "{\"zoneId\":$1,\"code\":\"$2\"}")" | jq -r .id; }
ZSTG=$(zone STG STAGING); ZPCK=$(zone PCK PICKING); ZRSV=$(zone RSV RESERVE)
B_STG=$(bin "$ZSTG" STG-01); B_PCK=$(bin "$ZPCK" P-01); B_PCK2=$(bin "$ZPCK" P-02); B_RSV=$(bin "$ZRSV" R-01)
expect 409 "$(req POST "/api/v1/warehouses/$W6P/bins" "{\"zoneId\":$ZRSV,\"code\":\"P-01\"}")" >/dev/null   # único por almacén
DK=$(expect 200 "$(req POST "/api/v1/warehouses/$W6P/docks" '{"code":"D1","dockType":"BOTH"}')" | jq -r .id)
# PATCH persistido (WmsResolve con track:true): se relee con GET.
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P/zones/$ZRSV" '{"name":"Reserva alta"}')" >/dev/null
expect 200 "$(req GET "/api/v1/warehouses/$W6P/zones")" | jq -e --argjson z "$ZRSV" 'any(.[]; .id==$z and .name=="Reserva alta")' >/dev/null || fail "PATCH de zona no persistido"
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P/bins/$B_RSV" '{"aisle":"A9","maxWeightKg":500,"maxCapacityQty":40}')" >/dev/null
expect 200 "$(req GET "/api/v1/warehouses/$W6P/bins")" | jq -e --argjson b "$B_RSV" 'any(.items[]; .id==$b and .aisle=="A9" and .maxWeightKg==500 and .maxCapacityQty==40 and .occupancy=="EMPTY")' >/dev/null || fail "PATCH de posición no persistido"
# Lote 1 de cambios de Almacén: listado paginado ({ total, items }), búsqueda por pasillo, filtro de ocupación y cupo > 0.
expect 200 "$(req GET "/api/v1/warehouses/$W6P/bins?search=a9&take=1")" | jq -e --argjson b "$B_RSV" '.total==1 and .take==1 and .items[0].id==$b' >/dev/null || fail "búsqueda de posiciones por pasillo"
expect 200 "$(req GET "/api/v1/warehouses/$W6P/bins?occupancy=EMPTY&occupancy=FULL")" | jq -e '.total==4' >/dev/null || fail "filtro de ocupación de posiciones"
expect 400 "$(req GET "/api/v1/warehouses/$W6P/bins?occupancy=HALF")" | jq -e --arg m "Estado de ocupación desconocido: 'HALF'. Use EMPTY, PARTIAL, FULL o NO_CAPACITY." "$HASM" >/dev/null || fail "ocupación desconocida 400"
expect 400 "$(req PATCH "/api/v1/warehouses/$W6P/bins/$B_RSV" '{"maxCapacityQty":0}')" | jq -e --arg m "El cupo máximo de la posición debe ser mayor que cero." "$HASM" >/dev/null || fail "cupo 0 → 400"
expect 200 "$(req GET "/api/v1/warehouses/$W6P/zones")" | jq -e --argjson z "$ZRSV" 'any(.[]; .id==$z and .binCount==1 and .capacityQty==40 and .binsWithoutCapacity==0)' >/dev/null || fail "ocupación de la zona RSV"
# Lote 11: cupo en bloque (mismos filtros que el listado; { matched, changed }; sin filtros exige allBins; clear lo quita).
expect 200 "$(req GET "/api/v1/warehouses/$W6P/bins?zoneIds=$ZPCK&take=1")" | jq -e '.total==2' >/dev/null || fail "vista previa del cupo en bloque (total del listado)"
expect 200 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" "{\"zoneIds\":[$ZPCK],\"maxCapacityQty\":25}")" | jq -e '.matched==2 and .changed==2' >/dev/null || fail "cupo en bloque por zona"
expect 200 "$(req GET "/api/v1/warehouses/$W6P/bins?zoneIds=$ZPCK")" | jq -e '(.items | length)==2 and all(.items[]; .maxCapacityQty==25)' >/dev/null || fail "cupo en bloque persistido"
expect 200 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" "{\"zoneIds\":[$ZPCK],\"maxCapacityQty\":25}")" | jq -e '.matched==2 and .changed==0' >/dev/null || fail "cupo en bloque repetido sin cambios"
expect 200 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" '{"allBins":true,"onlyWithoutCapacity":true,"maxCapacityQty":90,"search":"NO-EXISTE"}')" | jq -e '.matched==0 and .changed==0' >/dev/null || fail "cupo en bloque sin coincidencias"
expect 400 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" '{"maxCapacityQty":25}')" | jq -e --arg m "Indique al menos un filtro de posiciones (zoneIds, aisle, rack, level, position, search o binIds) o allBins: true para aplicarlo a todo el almacén." "$HASM" >/dev/null || fail "cupo en bloque sin filtros → 400"
expect 400 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" "{\"zoneIds\":[$ZPCK]}")" | jq -e --arg m "Indique el cupo máximo (maxCapacityQty) o clear: true para quitarlo." "$HASM" >/dev/null || fail "cupo en bloque sin valor → 400"
expect 400 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" "{\"zoneIds\":[$ZPCK],\"maxCapacityQty\":5,\"clear\":true}")" | jq -e --arg m "Indique el cupo máximo o clear: true, no ambos." "$HASM" >/dev/null || fail "cupo en bloque con valor y clear → 400"
expect 400 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" "{\"zoneIds\":[$ZPCK],\"maxCapacityQty\":0}")" | jq -e --arg m "El cupo máximo de la posición debe ser mayor que cero." "$HASM" >/dev/null || fail "cupo en bloque 0 → 400"
expect 200 "$(req POST "/api/v1/warehouses/$W6P/bins/capacity" "{\"zoneIds\":[$ZPCK],\"clear\":true}")" | jq -e '.matched==2 and .changed==2' >/dev/null || fail "quitar el cupo en bloque"
expect 200 "$(req GET "/api/v1/warehouses/$W6P/bins?zoneIds=$ZPCK")" | jq -e 'all(.items[]; .maxCapacityQty==null)' >/dev/null || fail "cupo en bloque quitado"
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P/docks/$DK" '{"dockType":"INBOUND"}')" >/dev/null
expect 200 "$(req GET "/api/v1/warehouses/$W6P/docks")" | jq -e --argjson d "$DK" 'any(.[]; .id==$d and .dockTypeCode=="INBOUND")' >/dev/null || fail "PATCH de muelle no persistido"
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P/docks/$DK" '{"dockType":"BOTH"}')" >/dev/null
expect 400 "$(req POST /api/v1/inventory/adjustments '{"quantity":1,"reason":"FOUND"}')" >/dev/null
# Campos fijos en PATCH (llegan por [JsonExtensionData]) → 400 con el mensaje de la entidad.
expect 400 "$(req PATCH "/api/v1/warehouses/$W6P" '{"code":"OTRO"}')" | jq -e --arg m "El código del almacén no se puede cambiar." "$HASM" >/dev/null || fail "código del almacén fijo"
# Lote 1 de cambios de Almacén: el código de la zona se edita (único en el almacén → 409); su almacén no (400).
expect 409 "$(req PATCH "/api/v1/warehouses/$W6P/zones/$ZRSV" '{"code":"PCK"}')" | jq -e '.title=="Ya existe una zona con ese código en el almacén."' >/dev/null || fail "código de zona repetido 409"
expect 400 "$(req PATCH "/api/v1/warehouses/$W6P/zones/$ZRSV" '{"warehouseId":1}')" | jq -e --arg m "La zona no se puede mover a otro almacén." "$HASM" >/dev/null || fail "almacén de la zona fijo"
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P/zones/$ZRSV" '{"code":"rsv-2"}')" | jq -e '.code=="RSV-2"' >/dev/null || fail "código de zona editable"
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P/zones/$ZRSV" '{"code":"RSV"}')" | jq -e '.code=="RSV"' >/dev/null || fail "código de zona de vuelta a RSV"
expect 400 "$(req PATCH "/api/v1/warehouses/$W6P/bins/$B_RSV" "{\"zoneId\":$ZPCK}")" | jq -e --arg m "El código y la zona de la posición no se pueden cambiar." "$HASM" >/dev/null || fail "zona de la posición fija"
expect 400 "$(req PATCH "/api/v1/warehouses/$W6P/docks/$DK" '{"code":"D9"}')" | jq -e --arg m "El código del muelle no se puede cambiar." "$HASM" >/dev/null || fail "código del muelle fijo"
# Una zona con posiciones activas no se desactiva (409); el muelle cambia de estatus a mano con historial (maestro L317).
expect 409 "$(req POST "/api/v1/warehouses/$W6P/zones/$ZRSV/deactivate" '{}')" | jq -e '.title=="La zona tiene posiciones activas; desactívelas primero."' >/dev/null || fail "baja de zona con posiciones activas"
expect 200 "$(req POST "/api/v1/warehouses/$W6P/docks/$DK/status" '{"status":"MAINTENANCE","comment":"Rampa en reparación"}')" | jq -e '.statusCode=="MAINTENANCE"' >/dev/null || fail "muelle en MAINTENANCE"
expect 400 "$(req POST "/api/v1/warehouses/$W6P/docks/$DK/status" '{"status":"FOO"}')" >/dev/null
expect 200 "$(req POST "/api/v1/warehouses/$W6P/docks/$DK/status" '{"status":"FREE"}')" | jq -e '.statusCode=="FREE"' >/dev/null || fail "muelle de vuelta a FREE"
expect 200 "$(req GET "/api/v1/postal-localities?search=toa%20baja")" | jq -e 'any(.[]; .postalCode=="00949" and .municipality=="Toa Baja" and .countryCode=="PR") and any(.[]; .postalCode=="00952" and .city=="SABANA SECA")' >/dev/null || fail "localidad postal Toa Baja 00949"
expect 200 "$(req GET "/api/v1/postal-localities?search=mayaguez")" | jq -e 'length>0 and all(.[]; .municipality=="Mayagüez")' >/dev/null || fail "búsqueda de ciudad sin acentos"
ok "W6$TS con STG/PCK/RSV, posición repetida en el almacén 409, muelle D1; PATCH de zona, posición (con cupo) y muelle persistidos (releídos con GET); posiciones paginadas con búsqueda por pasillo y filtro de ocupación (desconocida 400, cupo 0 400); ocupación de zona; cupo en bloque por zona (vista previa con el total del listado, repetido sin cambios, sin filtros/sin valor/valor y clear/0 → 400, clear); código de zona editable (repetido 409) y su almacén fijo 400; PATCH de código de almacén, muelle y zona de la posición 400; zona con posiciones activas 409; muelle MAINTENANCE → FREE a mano (estatus desconocido 400); localidades postales por ciudad sin acentos"

step "productos, ajustes y 547 → 409 (Lote 6)"
prod() { expect 200 "$(req POST /api/v1/products "$1")" | jq -r .product.publicId; }
PN=$(prod "{\"sku\":\"PN$TS\",\"name\":\"Producto N $TS\",\"purchaseCost\":2.5,\"salePrice\":4}")
PC=$(prod "{\"sku\":\"PC$TS\",\"name\":\"Concurrencia $TS\",\"purchaseCost\":1}")
PD=$(prod "{\"sku\":\"PD$TS\",\"name\":\"Baja $TS\",\"purchaseCost\":1}")
PX=$(prod "{\"sku\":\"PX$TS\",\"name\":\"Cruce $TS\",\"purchaseCost\":1}")
PR=$(prod "{\"sku\":\"PR$TS\",\"name\":\"Reabasto $TS\",\"purchaseCost\":1,\"preferredWarehousePublicId\":\"$W6P\",\"preferredBinId\":$B_PCK2,\"minPickQty\":5,\"maxPickQty\":8}")
# D26 (maestro L294): sin warehousePublicId y con más de un almacén activo (ALM-01 y W6) → 400 con el campo exacto.
expect 400 "$(req POST /api/v1/receipts "$(jq -cn --arg p "$PN" '{type:"BLIND",lines:[{productPublicId:$p,receivedQty:1}]}')")" | jq -e '.errors.warehousePublicId[0]=="Indique el almacén: la compañía tiene más de uno."' >/dev/null || fail "selector de almacén obligatorio con más de uno (D26)"
expect 409 "$(req POST /api/v1/products "{\"sku\":\"PN$TS\",\"name\":\"Repetido\"}")" >/dev/null
expect 400 "$(req PATCH "/api/v1/products/$PN" '{"sku":"OTRO"}')" | jq -e --arg m "El SKU del producto no se puede cambiar." "$HASM" >/dev/null || fail "SKU fijo"
# Categorías jerárquicas (maestro L549/L553): sin ciclos (400), nombre único por nivel (409), baja con subcategoría activa 409.
C1=$(expect 200 "$(req POST /api/v1/product-categories "{\"name\":\"Cat $TS\"}")" | jq -r .id)
C2=$(expect 200 "$(req POST /api/v1/product-categories "{\"name\":\"Sub $TS\",\"parentId\":$C1}")" | jq -r .id)
expect 400 "$(req PATCH "/api/v1/product-categories/$C1" "{\"parentId\":$C2}")" | jq -e '.errors.parentId[0]=="Una categoría no puede ser su propia ascendente."' >/dev/null || fail "ciclo de categorías → 400"
expect 409 "$(req POST /api/v1/product-categories "{\"name\":\"Sub $TS\",\"parentId\":$C1}")" | jq -e '.title=="Ya existe una categoría con ese nombre en ese nivel."' >/dev/null || fail "categoría repetida en el nivel → 409"
expect 409 "$(req POST "/api/v1/product-categories/$C1/deactivate" '{}')" | jq -e '.title=="La categoría tiene subcategorías activas; desactívelas primero."' >/dev/null || fail "baja de categoría con subcategoría activa → 409"
expect 200 "$(req POST /api/v1/products "{\"sku\":\"PB$TS\",\"name\":\"Barras $TS\",\"barcode\":\"BC$TS\"}")" >/dev/null
expect 409 "$(req POST /api/v1/products "{\"sku\":\"PB2$TS\",\"name\":\"Barras 2 $TS\",\"barcode\":\"BC$TS\"}")" | jq -e '.title=="Ya existe un producto activo con ese código de barras."' >/dev/null || fail "código de barras repetido → 409"
expect 400 "$(req POST /api/v1/products "{\"sku\":\"PM$TS\",\"name\":\"Otro almacén $TS\",\"preferredWarehousePublicId\":\"$DEMO\",\"preferredBinId\":$B_PCK}")" | jq -e '.errors.preferredBinId[0]=="La posición preferida debe pertenecer al almacén preferido."' >/dev/null || fail "posición preferida de otro almacén → 400"
expect 400 "$(adjust "$PN" "$W6P" "$B_PCK" 3 "")" >/dev/null                                  # motivo obligatorio
expect 400 "$(adjust "$PN" "$W6P" "$B_PCK" 3 RECEIPT_VARIANCE)" >/dev/null                    # lo asigna el sistema
# Ajuste del 2026-09-30: la nota del ajuste manual es obligatoria también en el API (400 en errors.notes).
expect 400 "$(req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$PN" --arg w "$W6P" --argjson b "$B_PCK" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:1,reason:"FOUND",notes:"   "}')")" | jq -e '.errors.notes[0]=="Escriba una nota que explique el ajuste."' >/dev/null || fail "ajuste sin nota → 400 errors.notes"
expect 200 "$(adjust "$PN" "$W6P" "$B_PCK" 10 FOUND)" | jq -e '.transactions[0].quantity==10 and .transactions[0].typeCode=="ADJUSTMENT"' >/dev/null || fail "ajuste +10"
expect 200 "$(adjust "$PN" "$W6P" "$B_PCK" -2 DAMAGE)" | jq -e '.transactions[0].quantity==-2' >/dev/null || fail "ajuste −2 con signo"
expect 409 "$(adjust "$PN" "$W6P" "$B_PCK" -1000 LOSS)" | jq -e --arg m "Inventario insuficiente de PN$TS en P-01: disponible 8, solicitado 1000." '.code=="insufficient_stock" and (.title==$m or any((.errors // {})[][]; .==$m))' >/dev/null || fail "−1000 → 409 insufficient_stock"
[[ $(onhand "$W6P" "$B_PCK" "$PN") == 8 ]] || fail "saldo de PN tras el 409"
ok "recibo sin almacén con más de uno activo 400 (D26); SKU repetido 409; SKU fijo en PATCH 400; categorías: ciclo 400, nombre repetido en el nivel 409, baja con subcategoría 409; código de barras repetido 409; posición preferida de otro almacén 400; sin motivo / motivo del sistema 400; +10 FOUND, −2 DAMAGE con signo; −1000 → 409 insufficient_stock con el mensaje exacto y sin efecto"

step "concurrencia de inventario (Lote 6): 8 recolecciones sobre 5 unidades"
expect 200 "$(adjust "$PC" "$W6P" "$B_PCK" 5 FOUND)" >/dev/null
TMP6=$(mktemp -d)
for i in 1 2 3 4 5 6 7 8; do collect "$W6P" "$PC" 1 > "$TMP6/$i" & done
wait
OKS=0; CONF=0
for i in 1 2 3 4 5 6 7 8; do c=$(tail -n1 "$TMP6/$i"); if [[ $c == 200 ]]; then OKS=$((OKS+1)); elif [[ $c == 409 ]]; then CONF=$((CONF+1)); fi; done
[[ $OKS -eq 5 && $CONF -eq 3 ]] || fail "8 recolecciones sobre 5: $OKS × 200 y $CONF × 409 ($(for i in 1 2 3 4 5 6 7 8; do tail -n1 "$TMP6/$i"; done | tr '\n' ' '))"
NUMS=$(for i in 1 2 3 4 5 6 7 8; do if [[ $(tail -n1 "$TMP6/$i") == 200 ]]; then sed '$d' "$TMP6/$i" | jq -r .number; fi; done | sed -E 's/^EMP-0*//' | sort -n)
[[ $(( $(echo "$NUMS" | tail -1) - $(echo "$NUMS" | head -1) )) -eq 4 ]] || fail "EMP con huecos: $(echo $NUMS)"
rm -rf "$TMP6"
[[ $(onhand "$W6P" "$B_PCK" "$PC") == 0 ]] || fail "saldo de PC tras las recolecciones"
kardex "productPublicIds=$PC&types=ISSUE" | jq -e '.total==5 and all(.items[]; .quantity==-1 and .refEntityCode=="PICK_BATCH" and .refId!=null)' >/dev/null || fail "5 ISSUE con Ref PICK_BATCH en el INSERT"
# 4 recibos ciegos simultáneos → 4 REC distintos y consecutivos (NumberSequence bajo bloqueo).
TMP6=$(mktemp -d)
for i in 1 2 3 4; do blind "$W6P" "$B_STG" "$PC" 1 > "$TMP6/$i" & done; wait
[[ "$(codes "$TMP6"/1 "$TMP6"/2 "$TMP6"/3 "$TMP6"/4)" == "200 200 200 200 " ]] || fail "4 recibos en paralelo: $(codes "$TMP6"/1 "$TMP6"/2 "$TMP6"/3 "$TMP6"/4)"
RN=$(for i in 1 2 3 4; do sed '$d' "$TMP6/$i" | jq -r .header.number; done | sed -E 's/^REC-0*//' | sort -n | uniq)
[[ $(echo "$RN" | wc -l) -eq 4 && $(( $(echo "$RN" | tail -1) - $(echo "$RN" | head -1) )) -eq 3 ]] || fail "REC duplicados o con huecos: $(echo $RN)"
for i in 1 2 3 4; do expect 204 "$(req DELETE "/api/v1/receipts/$(sed '$d' "$TMP6/$i" | jq -r .header.publicId)")" >/dev/null; done
rm -rf "$TMP6"
# Desactivar un producto contra confirmar su recibo: Product → saldos bajo bloqueo; nunca queda inactivo con inventario.
PQ=$(prod "{\"sku\":\"PQ$TS\",\"name\":\"Carrera baja $TS\",\"purchaseCost\":1}")
RQ=$(expect 200 "$(blind "$W6P" "$B_STG" "$PQ" 2)" | jq -r .header.publicId)
TMP6=$(mktemp -d)
req POST "/api/v1/receipts/$RQ/confirm" '{}' > "$TMP6/1" & req POST "/api/v1/products/$PQ/deactivate" '{}' > "$TMP6/2" & wait
RACE=$(codes "$TMP6"/1 "$TMP6"/2); rm -rf "$TMP6"
ACT=$(expect 200 "$(req GET "/api/v1/products/$PQ")" | jq -r .product.isActive)
[[ "$ACT" == true || $(onhandall "$W6P" "$PQ") == 0 ]] || fail "producto inactivo con inventario ($RACE)"
ok "5 × 200 y 3 × 409, saldo 0, 5 ISSUE −1 con Ref PICK_BATCH y 5 EMP consecutivos (los fallidos no consumen número); 4 recibos en paralelo con REC consecutivos; baja de producto contra confirmación de su recibo ($RACE) sin producto inactivo con inventario"

step "recepción ciega, doble confirmación, putaway parcial/total y reabasto (Lote 6)"
R6=$(expect 200 "$(blind "$W6P" "$B_STG" "$PN" 3)"); R6P=$(echo "$R6" | jq -r .header.publicId); R6ID=$(echo "$R6" | jq -r .header.id)
TMP6=$(mktemp -d)
for i in 1 2; do req POST "/api/v1/receipts/$R6P/confirm" '{}' > "$TMP6/$i" & done; wait
CODES=$(codes "$TMP6"/1 "$TMP6"/2); rm -rf "$TMP6"
[[ "$CODES" == "200 422 " ]] || fail "doble confirmación del recibo: $CODES"
kardex "refEntity=RECEIPT&refId=$R6ID" | jq -e '.total==1 and .items[0].quantity==3' >/dev/null || fail "una sola tanda de RECEIPT"
PUT6=$(tasksof "$W6P" PUTAWAY | jq -r --argjson r "$R6ID" '[.items[] | select(.refEntityCode=="RECEIPT" and .refId==$r)][0].id')
[[ "$PUT6" =~ ^[0-9]+$ ]] || fail "PUTAWAY del recibo"
expect 200 "$(req GET "/api/v1/receipts/$R6P")" | jq -e '(.putawayTasks | length) > 0 and all(.putawayTasks[]; .toBinId != null)' >/dev/null || fail "la PUTAWAY nace sin posición sugerida (L301)"
# D41: la cola pide inventory.view; el permiso del handler (PUTAWAY → warehouse.receive) lo exige el servicio.
PD0=$(pdcount)
expect 403 "$(req POST "/api/v1/warehouse-tasks/$PUT6/start" '{}' "$TREAD6")" | denied warehouse.receive || fail "iniciar PUTAWAY sin warehouse.receive"
expect 403 "$(req POST "/api/v1/warehouse-tasks/$PUT6/complete" "{\"toBinId\":$B_RSV}" "$TREAD6")" | denied warehouse.receive || fail "completar PUTAWAY sin warehouse.receive"
[[ $(pdcount) -ge $((PD0+2)) ]] || fail "PERMISSION_DENIED del permiso del handler"
expect 200 "$(req GET "/api/v1/warehouse-tasks/$PUT6")" | jq -e '.statusCode=="PENDING"' >/dev/null || fail "la tarea cambió sin permiso"
expect 200 "$(req POST "/api/v1/warehouse-tasks/$PUT6/complete" "{\"toBinId\":$B_RSV,\"quantity\":1}")" | jq -e '.statusCode=="DONE" and .quantity==1' >/dev/null || fail "putaway parcial"
expect 200 "$(req GET "/api/v1/receipts/$R6P")" | jq -e '.header.statusCode=="RECEIVED"' >/dev/null || fail "el recibo pasó a PUTAWAY con una tarea abierta"
REM6=$(tasksof "$W6P" PUTAWAY | jq -r --argjson r "$R6ID" '[.items[] | select(.refEntityCode=="RECEIPT" and .refId==$r and .quantity==2)][0].id')
[[ "$REM6" =~ ^[0-9]+$ ]] || fail "remanente de la PUTAWAY (2)"
expect 404 "$(req POST "/api/v1/warehouse-tasks/$REM6/complete" "{\"toBinId\":999999}")" >/dev/null
TMP6=$(mktemp -d)
for i in 1 2; do req POST "/api/v1/warehouse-tasks/$REM6/complete" "{\"toBinId\":$B_RSV}" > "$TMP6/$i" & done; wait
CODES=$(codes "$TMP6"/1 "$TMP6"/2); rm -rf "$TMP6"
[[ "$CODES" == "200 422 " ]] || fail "doble completado de la misma tarea: $CODES"
expect 200 "$(req GET "/api/v1/receipts/$R6P")" | jq -e '.header.statusCode=="PUTAWAY"' >/dev/null || fail "el recibo no pasó a PUTAWAY con la última tarea"
kardex "refEntity=WAREHOUSE_TASK&productPublicIds=$PN" | jq -e '.total==2 and all(.items[]; .typeCode=="TRANSFER" and .quantity>0)' >/dev/null || fail "TRANSFER con Ref WAREHOUSE_TASK"
# Reabasto: PR con 2 en su posición de picking (mínimo 5, máximo 8) y 10 en reserva.
expect 200 "$(adjust "$PR" "$W6P" "$B_PCK2" 2 FOUND)" >/dev/null; expect 200 "$(adjust "$PR" "$W6P" "$B_RSV" 10 FOUND)" >/dev/null
RUN1=$(expect 200 "$(req POST /api/v1/warehouse-tasks/replenishment/run "{\"warehousePublicId\":\"$W6P\"}")")
echo "$RUN1" | jq -e '.tasksCreated==1 and .tasks[0].quantity==6' >/dev/null || fail "reabasto: $RUN1"
expect 200 "$(req POST /api/v1/warehouse-tasks/replenishment/run "{\"warehousePublicId\":\"$W6P\"}")" | jq -e '.tasksCreated==0 and .skippedWithOpenTask==1' >/dev/null || fail "reabasto idempotente"
expect 200 "$(req POST "/api/v1/warehouse-tasks/$(echo "$RUN1" | jq -r '.tasks[0].id')/complete" '{}')" >/dev/null
[[ $(onhand "$W6P" "$B_PCK2" "$PR") == 8 ]] || fail "reabasto completado: picking en 8"
# Putaway dirigido (maestro L301, D24): PR tiene posición preferida; su PUTAWAY la sugiere (PREFERRED con rotación) y se
# completa con '{}' hacia esa posición.
RP=$(expect 200 "$(blind "$W6P" "$B_STG" "$PR" 1)"); RPP=$(echo "$RP" | jq -r .header.publicId)
PUTP=$(expect 200 "$(req POST "/api/v1/receipts/$RPP/confirm" '{}')" | jq -r --argjson b "$B_PCK2" '[.putawayTasks[] | select(.toBinId==$b)][0].id')
[[ "$PUTP" =~ ^[0-9]+$ ]] || fail "la PUTAWAY de PR no sugiere su posición preferida"
expect 200 "$(req GET "/api/v1/warehouse-tasks/putaway-suggestions?taskId=$PUTP")" | jq -e --argjson b "$B_PCK2" '.[0].binId==$b and .[0].reasonCode=="PREFERRED" and (.[0].rotationClass=="FAST" or .[0].rotationClass=="SLOW")' >/dev/null || fail "sugerencia PREFERRED con rotationClass"
expect 200 "$(req POST "/api/v1/warehouse-tasks/$PUTP/complete" '{}')" | jq -e '.statusCode=="DONE"' >/dev/null || fail "completar la PUTAWAY sin destino explícito"
[[ $(onhand "$W6P" "$B_PCK2" "$PR") == 9 ]] || fail "el TRANSFER no llegó a la posición sugerida"
# Asignar y cancelar desde la cola (maestro L308): solo a miembros activos; cancelar la última PUTAWAY cierra el recibo.
RA=$(expect 200 "$(blind "$W6P" "$B_STG" "$PN" 1)"); RAP=$(echo "$RA" | jq -r .header.publicId); RAID=$(echo "$RA" | jq -r .header.id)
expect 200 "$(req POST "/api/v1/receipts/$RAP/confirm" '{}')" >/dev/null
PUTA=$(tasksof "$W6P" PUTAWAY | jq -r --argjson r "$RAID" '[.items[] | select(.refEntityCode=="RECEIPT" and .refId==$r)][0].id')
UID_T3=$(expect 200 "$(req GET /api/v1/me '' "$T3")" | jq -r .userId); UID_ME=$(expect 200 "$(req GET /api/v1/me)" | jq -r .userId)
expect 400 "$(req POST "/api/v1/warehouse-tasks/$PUTA/assign" "{\"userId\":$UID_T3}")" | jq -e '.errors.userId[0]=="El usuario no es miembro activo de la compañía."' >/dev/null || fail "asignar a un usuario de otro tenant"
expect 200 "$(req POST "/api/v1/warehouse-tasks/$PUTA/assign" "{\"userId\":$UID_ME}")" | jq -e --argjson u "$UID_ME" '.assignedToUserId==$u' >/dev/null || fail "asignar a un miembro activo"
expect 403 "$(req POST "/api/v1/warehouse-tasks/$PUTA/cancel" '{}' "$TREAD6")" >/dev/null   # cancelar exige warehouse.manage (desde 2026-10-01 el Operador lo tiene)
expect 200 "$(req POST "/api/v1/warehouse-tasks/$PUTA/cancel" '{"comment":"Se guarda en otra corrida"}')" | jq -e '.statusCode=="CANCELLED" and .completedAtUtc!=null' >/dev/null || fail "cancelar la PUTAWAY desde la cola"
expect 200 "$(req GET "/api/v1/receipts/$RAP")" | jq -e '.header.statusCode=="PUTAWAY"' >/dev/null || fail "cancelar la última PUTAWAY no cerró el recibo"
ok "doble confirmación: 200 + 422 y un solo RECEIPT; putaway 1 + remanente 2 (destino inexistente 404), el recibo pasa a PUTAWAY solo con la última; TRANSFER con Ref WAREHOUSE_TASK; doble completado 200 + 422; Solo lectura no inicia ni completa la PUTAWAY (403 warehouse.receive + PERMISSION_DENIED); reabasto 6 y segunda corrida skippedWithOpenTask 1; putaway dirigido: la PUTAWAY nace con destino, la de PR sugiere su preferida (PREFERRED + rotationClass) y se completa con '{}'; asignar a otro tenant 400 y a un miembro 200; cancelar la última PUTAWAY cierra el recibo"

step "recolección y empaque (Lote 6): doble empaque, bajas vigiladas y doble eliminación"
# Cliente y consignatario propios del paso (orden de empaque).
C6=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Empaque $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")"); C6P=$(pid "$C6")
L6=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$C6P\",\"name\":\"Farmacia $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 1\",\"city\":\"Ponce\",\"country\":\"PR\"}")" | jq -r .publicId)
packbody() { jq -cn --arg c "$C6P" --arg l "$L6" '{order:{clientPublicId:$c,consigneeLocationPublicId:$l,serviceType:"STANDARD",packages:[{pieces:1,packageType:"BOX"}]}}'; }
W7=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"W7$TS\",\"name\":\"Almacén baja $TS\"}")"); W7P=$(wpid "$W7")
Z7=$(expect 200 "$(req POST "/api/v1/warehouses/$W7P/zones" '{"code":"PCK","name":"Picking","zoneType":"PICKING"}')" | jq -r .id)
B7=$(expect 200 "$(req POST "/api/v1/warehouses/$W7P/bins" "{\"zoneId\":$Z7,\"code\":\"P-01\"}")" | jq -r .id)
expect 200 "$(adjust "$PD" "$W7P" "$B7" 2 FOUND)" >/dev/null
PB7=$(expect 200 "$(collect "$W7P" "$PD" 2)"); PB7P=$(pid "$PB7")
# Producto con una recolección que aún se puede eliminar: su reversa es una entrada → no se da de baja.
expect 409 "$(req POST "/api/v1/products/$PD/deactivate" '{}')" | jq -e --arg m "El producto PD$TS está en recibos abiertos, tareas pendientes, recolecciones o conteos abiertos; ciérrelos antes de desactivarlo." '.title==$m' >/dev/null || fail "baja de producto con recolección eliminable"
# D27: el Operador recolecta (warehouse.pick) pero no empaca: orders.create lo exige el servicio.
expect 403 "$(req POST "/api/v1/pick-batches/$PB7P/pack" "$(packbody)" "$TWH6")" | denied orders.create || fail "empacar sin orders.create"
expect 200 "$(req GET "/api/v1/pick-batches/$PB7P")" | jq -e '.statusCode=="COLLECTED" and .orderPublicId==null' >/dev/null || fail "el empaque sin permiso dejó efectos"
TMP6=$(mktemp -d)
for i in 1 2; do req POST "/api/v1/pick-batches/$PB7P/pack" "$(packbody)" > "$TMP6/$i" & done; wait
CODES=$(codes "$TMP6"/1 "$TMP6"/2)
[[ "$CODES" == "200 409 " || "$CODES" == "200 422 " ]] || fail "doble empaque: $CODES"
PACKED=$(for i in 1 2; do if [[ $(tail -n1 "$TMP6/$i") == 200 ]]; then sed '$d' "$TMP6/$i"; fi; done); rm -rf "$TMP6"
echo "$PACKED" | jq -e '.batch.statusCode=="PACKED" and .order.packBatchNumber==.batch.number' >/dev/null || fail "empaque: $PACKED"
# L790 / R40: sin factura en el cuerpo, la de la orden se autogenera y se guarda también en el lote.
OP7=$(echo "$PACKED" | jq -r .order.publicId)
echo "$PACKED" | jq -e '(.order.clientInvoiceNumber // "") != "" and .batch.clientInvoiceNumber==.order.clientInvoiceNumber' >/dev/null || fail "factura autogenerada no copiada al lote: $(echo "$PACKED" | jq -c '{o:.order.clientInvoiceNumber,b:.batch.clientInvoiceNumber}')"
expect 200 "$(req GET "/api/v1/pick-batches?invoiceNumber=$(echo "$PACKED" | jq -r .order.clientInvoiceNumber)")" | jq -e --arg p "$PB7P" 'any(.items[]; .publicId==$p)' >/dev/null || fail "filtro por factura de la recolección"
# Almacén vacío con una recolección PACKED eliminable: la baja (terminal) se bloquea.
expect 409 "$(req POST "/api/v1/warehouses/$W7P/deactivate" '{}')" | jq -e '.errors.pickBatches[0]=="Recolecciones sin empacar o con orden en etapa inicial: 1."' >/dev/null || fail "baja de almacén con recolección empacada eliminable"
expect 409 "$(req DELETE "/api/v1/orders/$(echo "$PACKED" | jq -r .order.publicId)")" >/dev/null   # D12: se borra desde Recolección
# Eliminar una recolección EMPACADA borra su orden: exige orders.cancel (el Operador no lo tiene).
expect 403 "$(req DELETE "/api/v1/pick-batches/$PB7P" '{}' "$TWH6")" | denied orders.cancel || fail "eliminar recolección empacada sin orders.cancel"
expect 200 "$(req GET "/api/v1/orders/$OP7")" | jq -e '.isActive==true' >/dev/null || fail "la eliminación sin permiso quitó la orden"
TMP6=$(mktemp -d)
for i in 1 2; do req DELETE "/api/v1/pick-batches/$PB7P" '{}' > "$TMP6/$i" & done; wait
CODES=$(codes "$TMP6"/1 "$TMP6"/2); rm -rf "$TMP6"
[[ "$CODES" == "204 404 " ]] || fail "doble eliminación: $CODES"
[[ $(onhand "$W7P" "$B7" "$PD") == 2 ]] || fail "la eliminación no restauró el inventario"
expect 200 "$(req GET "/api/v1/orders/$OP7")" | jq -e '.isActive==false' >/dev/null || fail "eliminar la recolección empacada no quitó la orden (L780)"
kardex "productPublicIds=$PD&types=ADJUSTMENT" | jq -e 'any(.items[]; .reasonCode=="PICK_BATCH_REVERSAL" and .quantity==2)' >/dev/null || fail "reversa PICK_BATCH_REVERSAL"
expect 200 "$(req GET "/api/v1/pick-batches?from=$TODAY&to=$TODAY&status=COLLECTED&productPublicIds=$PC&take=200")" | jq -e '.total==5' >/dev/null || fail "filtros de recolecciones (fecha, estatus, producto)"
expect 200 "$(req GET "/api/v1/pick-batches?includeDeleted=true&productPublicIds=$PD")" | jq -e '.total==1 and .items[0].statusCode=="CANCELLED"' >/dev/null || fail "includeDeleted"
PK=$(prod "{\"sku\":\"PK$TS\",\"name\":\"Orden avanzada $TS\",\"purchaseCost\":1}")
expect 200 "$(adjust "$PK" "$W7P" "$B7" 1 FOUND)" >/dev/null
PBK=$(expect 200 "$(collect "$W7P" "$PK" 1)"); PBKP=$(pid "$PBK"); PBKN=$(echo "$PBK" | jq -r .number)
OPK=$(expect 200 "$(req POST "/api/v1/pick-batches/$PBKP/pack" "$(packbody)")" | jq -r .order.publicId)
expect 200 "$(req POST "/api/v1/orders/$OPK/cancel" '{"comment":"El cliente desistió"}')" | jq -e '.status=="CANCELLED"' >/dev/null || fail "cancelar la orden del empaque"
expect 422 "$(req DELETE "/api/v1/pick-batches/$PBKP" '{}')" | jq -e --arg m "La orden de la recolección $PBKN ya avanzó a 'Cancelada'; la recolección ya no se puede eliminar." '.title==$m' >/dev/null || fail "eliminar recolección con la orden avanzada (L783)"
expect 200 "$(req GET "/api/v1/pick-batches/$PBKP")" | jq -e '.statusCode=="PACKED" and .isActive==true' >/dev/null || fail "el 422 cambió la recolección"
expect 200 "$(req GET "/api/v1/orders/$OPK")" | jq -e '.isActive==true' >/dev/null || fail "el 422 quitó la orden"
kardex "productPublicIds=$PK&types=ADJUSTMENT" | jq -e '[.items[] | select(.reasonCode=="PICK_BATCH_REVERSAL")] | length==0' >/dev/null || fail "el 422 revirtió inventario"
[[ $(onhand "$W7P" "$B7" "$PK") == 0 ]] || fail "cancelar la orden restauró inventario (D12)"
# Maestro L326 (D25): con inventario en mano no se da de baja; en cero sí, sale de los selectores (activeOnly) y sigue en
# saldos y Kárdex; un recibo con el producto inactivo → 422; reactivar lo devuelve.
expect 409 "$(req POST "/api/v1/products/$PD/deactivate" '{}')" | jq -e --arg m "El producto PD$TS tiene inventario en mano (2); no se puede desactivar." '.title==$m' >/dev/null || fail "baja de producto con inventario"
expect 200 "$(adjust "$PD" "$W7P" "$B7" -2 LOSS)" >/dev/null
expect 200 "$(req POST "/api/v1/products/$PD/deactivate" '{}')" | jq -e '.product.isActive==false' >/dev/null || fail "baja de producto a 0"
expect 200 "$(req GET "/api/v1/products?activeOnly=true&search=PD$TS")" | jq -e '.total==0' >/dev/null || fail "inactivo visible en activeOnly"
expect 200 "$(req GET "/api/v1/products?search=PD$TS")" | jq -e '.total==1' >/dev/null || fail "inactivo fuera de la lista completa"
expect 200 "$(req GET "/api/v1/inventory/balances?productPublicIds=$PD&includeZero=true")" | jq -e '(.items|length)>0' >/dev/null || fail "inactivo fuera de saldos"
kardex "productPublicIds=$PD" | jq -e '.total>0' >/dev/null || fail "inactivo fuera del Kárdex"
expect 422 "$(blind "$W6P" "$B_STG" "$PD" 1)" >/dev/null
expect 422 "$(collect "$W7P" "$PD" 1)" | jq -e --arg m "El producto PD$TS está inactivo; no se puede recolectar." '.title==$m' >/dev/null || fail "recolección con producto inactivo (L326)"
expect 200 "$(req POST "/api/v1/products/$PD/reactivate" '{}')" | jq -e '.product.isActive==true' >/dev/null || fail "reactivar producto"
# Transferencias (maestro L330, D40): a la misma posición 400; entre almacenes una sola fila TRANSFER (+) en SQL Server
# (CK_InvTxn_Direction y FKs compuestas de posición por almacén). La conciliación final ve el movimiento entre almacenes.
PT=$(prod "{\"sku\":\"PT$TS\",\"name\":\"Transferencia $TS\",\"purchaseCost\":1}")
expect 200 "$(adjust "$PT" "$W6P" "$B_RSV" 3 FOUND)" >/dev/null
xfer() { req POST /api/v1/inventory/transfers "$(jq -cn --arg p "$1" --arg fw "$2" --argjson fb "$3" --arg tw "$4" --argjson tb "$5" --argjson q "$6" '{productPublicId:$p,fromWarehousePublicId:$fw,fromBinId:$fb,toWarehousePublicId:$tw,toBinId:$tb,quantity:$q}')"; }
expect 400 "$(xfer "$PT" "$W6P" "$B_RSV" "$W6P" "$B_RSV" 1)" | jq -e --arg m "La posición de origen y la de destino son la misma." "$HASM" >/dev/null || fail "transferencia a la misma posición → 400"
expect 200 "$(xfer "$PT" "$W6P" "$B_RSV" "$W7P" "$B7" 2)" | jq -e '(.transactions|length)==1 and .transactions[0].typeCode=="TRANSFER" and .transactions[0].quantity==2' >/dev/null || fail "transferencia W6→W7"
kardex "productPublicIds=$PT&types=TRANSFER" | jq -e '.total==1 and .items[0].quantity>0' >/dev/null || fail "una sola fila TRANSFER entre almacenes (D40)"
[[ $(onhand "$W6P" "$B_RSV" "$PT") == 1 && $(onhand "$W7P" "$B7" "$PT") == 2 ]] || fail "saldos tras la transferencia W6→W7"
ok "producto con recolección eliminable 409; doble empaque: una orden (número = EMP); almacén vacío con recolección empacada eliminable 409 (errors.pickBatches); DELETE de la orden 409 (D12); el Operador no empaca (403 orders.create) ni elimina una empacada (403 orders.cancel); factura autogenerada copiada al lote; doble eliminación 204 + 404 con reversa exacta y la orden inactiva; con la orden cancelada la recolección empacada no se elimina (422 con su mensaje, sin reversa, lote y orden intactos); filtros de la lista; baja de producto con inventario 409, en cero 200 (fuera de activeOnly, visible en saldos y Kárdex, recibo y recolección 422) y reactivación; transferencia a la misma posición 400 y W6→W7 en una sola fila TRANSFER"

step "lotes, series, recolección de varias líneas y baja de posición (Lote 6): EnsureLot, series (OPENJSON) y rango por posición en SQL Server"
PL=$(prod "{\"sku\":\"PL$TS\",\"name\":\"Lote $TS\",\"trackingType\":\"LOT\",\"purchaseCost\":1}")
PS=$(prod "{\"sku\":\"PS$TS\",\"name\":\"Serie $TS\",\"trackingType\":\"SERIAL\",\"purchaseCost\":1}")
P3=$(prod "{\"sku\":\"P3$TS\",\"name\":\"De cliente $TS\",\"ownerClientPublicId\":\"$C6P\"}")
lotrcpt() { req POST /api/v1/receipts "$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PL" --argjson q "$1" --arg n "$2" --arg e "$3" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:$q,lot:{number:$n,manufactureDate:"2026-01-01",expiryDate:$e}}]}')"; }
serrcpt() { req POST /api/v1/receipts "$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PS" --argjson q "$1" --argjson n "$2" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:$q,serialNumbers:$n}]}')"; }
confirmr() { req POST "/api/v1/receipts/$(echo "$1" | jq -r .header.publicId)/confirm" '{}'; }
serialst() { expect 200 "$(req GET "/api/v1/products/$PS/serials?search=$1")" | jq -r --arg n "$1" '[.[] | select(.serialNumber==$n)][0].statusCode'; }
lotqty() { expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$W6P&productPublicIds=$PL&lotNumber=$1&includeZero=true")" | jq '([.items[].qtyOnHand] | add // 0) + 0'; }
# (17) EnsureLot: el lote L1 (vence tarde) se crea primero y L0 (vence antes) después; el mismo número con otras fechas → 409.
RL1=$(expect 200 "$(lotrcpt 2 "L1$TS" 2027-12-31)"); expect 200 "$(confirmr "$RL1")" >/dev/null
RL0=$(expect 200 "$(lotrcpt 2 "L0$TS" 2027-01-31)"); expect 200 "$(confirmr "$RL0")" >/dev/null
kardex "productPublicIds=$PL&types=RECEIPT" | jq -e --arg a "L1$TS" --arg b "L0$TS" '.total==2 and ([.items[].lotNumber] | sort)==([$a,$b] | sort)' >/dev/null || fail "RECEIPT con lote en el Kárdex"
expect 409 "$(lotrcpt 1 "L1$TS" 2028-06-30)" | jq -e --arg m "El lote L1$TS ya existe con otras fechas; corrija las fechas o use otro número de lote." '.title==$m' >/dev/null || fail "lote existente con otras fechas → 409"
RL1B=$(expect 200 "$(lotrcpt 1 "L1$TS" 2027-12-31)")
echo "$RL1B" | jq -e --arg n "L1$TS" --argjson id "$(echo "$RL1" | jq '.lines[0].lotId')" '.lines[0].lotNumber==$n and .lines[0].lotId==$id' >/dev/null || fail "mismo lote con las mismas fechas se reutiliza"
expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$RL1B" | jq -r .header.publicId)")" >/dev/null
# (16) Series con OPENJSON: S1-S3 entran; S2 otra vez → 409 (ya en inventario).
RS=$(expect 200 "$(serrcpt 3 "[\"S1$TS\",\"S2$TS\",\"S3$TS\"]")"); expect 200 "$(confirmr "$RS")" >/dev/null
[[ $(serialst "S2$TS") == AVAILABLE ]] || fail "serie recibida AVAILABLE"
RS2=$(serrcpt 1 "[\"S2$TS\"]")
if [[ $(echo "$RS2" | tail -n1) == 200 ]]; then
  expect 409 "$(confirmr "$(echo "$RS2" | sed '$d')")" | jq -e --arg s "S2$TS" '.title | contains($s)' >/dev/null || fail "serie en inventario recibida otra vez"
  expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$RS2" | sed '$d' | jq -r .header.publicId)")" >/dev/null
else
  expect 409 "$RS2" | jq -e --arg s "S2$TS" '.title | contains($s)' >/dev/null || fail "serie en inventario recibida otra vez"
fi
# Recolección de VARIAS líneas (L781/L783): PN 2 y PL 1 por FEFO → 2 ISSUE con Ref PICK_BATCH y el stock baja exacto.
PN0=$(onhandall "$W6P" "$PN")
PBM=$(expect 200 "$(collectj "$(jq -cn --arg w "$W6P" --arg a "$PN" --arg b "$PL" '{warehousePublicId:$w,lines:[{productPublicId:$a,quantity:2},{productPublicId:$b,quantity:1}]}')")")
echo "$PBM" | jq -e --arg l "L0$TS" '.statusCode=="COLLECTED" and ([.lines[] | select(.lotNumber!=null) | .lotNumber]==[$l])' >/dev/null || fail "FEFO: el lote que vence primero: $(echo "$PBM" | jq -c '[.lines[] | {sku,lotNumber,quantity}]')"
kardex "refEntity=PICK_BATCH&refId=$(echo "$PBM" | jq -r .id)" | jq -e '.total==2 and ([.items[].quantity] | sort)==[-2,-1] and all(.items[]; .typeCode=="ISSUE")' >/dev/null || fail "2 ISSUE con Ref PICK_BATCH"
[[ $(onhandall "$W6P" "$PN") == $(jq -n "$PN0 - 2") ]] || fail "PN no bajó exactamente 2"
[[ $(lotqty "L0$TS") == 1 && $(lotqty "L1$TS") == 2 ]] || fail "PL no bajó exactamente 1 del lote L0"
# D15: dos dueños en una recolección → 400.
expect 400 "$(collectj "$(jq -cn --arg w "$W6P" --arg a "$PN" --arg b "$P3" '{warehousePublicId:$w,lines:[{productPublicId:$a,quantity:1},{productPublicId:$b,quantity:1}]}')")" | jq -e '.errors.lines[0]=="Una recolección solo puede tener productos de un mismo dueño."' >/dev/null || fail "recolección con dos dueños"
# Serie escaneada: S2 → SHIPPED (S1 sigue AVAILABLE); eliminar la recolección → S2 AVAILABLE.
PBS=$(expect 200 "$(collectj "$(jq -cn --arg w "$W6P" --arg p "$PS" --arg s "S2$TS" '{warehousePublicId:$w,lines:[{productPublicId:$p,serialNumbers:[$s]}]}')")")
[[ $(serialst "S2$TS") == SHIPPED && $(serialst "S1$TS") == AVAILABLE ]] || fail "S2 SHIPPED y S1 AVAILABLE"
NS=$(echo "$PBS" | jq -r .number | sed -E 's/^EMP-0*//')
# 409 de varias líneas sin efecto parcial: ni el PN de la primera línea sale, ni se consume número EMP.
PN1=$(onhandall "$W6P" "$PN"); ISS1=$(kardex "productPublicIds=$PN&types=ISSUE" | jq .total)
expect 409 "$(collectj "$(jq -cn --arg w "$W6P" --arg a "$PN" --arg b "$PL" '{warehousePublicId:$w,lines:[{productPublicId:$a,quantity:1},{productPublicId:$b,quantity:9999}]}')")" | jq -e '.code=="insufficient_stock"' >/dev/null || fail "recolección de varias líneas insuficiente → 409"
[[ $(onhandall "$W6P" "$PN") == "$PN1" && $(kardex "productPublicIds=$PN&types=ISSUE" | jq .total) == "$ISS1" ]] || fail "efecto parcial tras el 409"
PBN=$(expect 200 "$(collect "$W6P" "$PN" 1)")
[[ $(echo "$PBN" | jq -r .number | sed -E 's/^EMP-0*//') -eq $((NS+1)) ]] || fail "el 409 consumió número EMP ($NS → $(echo "$PBN" | jq -r .number))"
expect 204 "$(req DELETE "/api/v1/pick-batches/$(echo "$PBN" | jq -r .publicId)" '{}')" >/dev/null
expect 204 "$(req DELETE "/api/v1/pick-batches/$(echo "$PBS" | jq -r .publicId)" '{}')" >/dev/null
[[ $(serialst "S2$TS") == AVAILABLE ]] || fail "S2 no volvió a AVAILABLE al eliminar la recolección"
expect 200 "$(req GET "/api/v1/inventory/serials/trace?productPublicId=$PS&serialNumber=S2$TS")" | jq -e '[.statusHistory[].toCode] as $h | ($h | index("SHIPPED")) != null and $h[-1]=="AVAILABLE" and (.movements | length)==3' >/dev/null || fail "rastro de S2"
# Baja de serie: ajuste −1 DAMAGE con S1 → SCRAPPED; volver a entrarla → 409.
serialadj() { req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$PS" --arg w "$W6P" --argjson b "$B_STG" --argjson q "$1" --arg r "$2" --arg s "$3" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:$q,reason:$r,serialNumbers:[$s],notes:"humo"}')"; }
expect 200 "$(serialadj -1 DAMAGE "S1$TS")" | jq -e '.transactions[0].quantity==-1' >/dev/null || fail "baja de serie"
[[ $(serialst "S1$TS") == SCRAPPED ]] || fail "S1 SCRAPPED"
expect 409 "$(serialadj 1 FOUND "S1$TS")" >/dev/null
# Genealogía de L0: entró 2, salió 1, queda 1.
L0ID=$(expect 200 "$(req GET "/api/v1/products/$PL/lots")" | jq -r --arg n "L0$TS" '[.[] | select(.lotNumber==$n)][0].id')
expect 200 "$(req GET "/api/v1/inventory/lots/$L0ID/genealogy")" | jq -e '.qtyIn==2 and .qtyOut==1 and .qtyOnHand==1' >/dev/null || fail "genealogía de L0"
# (5) Rango por posición: con inventario la posición no se desactiva; vacía sí (y se reactiva).
expect 409 "$(req POST "/api/v1/warehouses/$W6P/bins/$B_PCK/deactivate" '{}')" | jq -e '.title=="La posición P-01 tiene inventario; no se puede desactivar."' >/dev/null || fail "baja de posición con inventario"
B_EMPTY=$(bin "$ZPCK" P-09)
expect 200 "$(req POST "/api/v1/warehouses/$W6P/bins/$B_EMPTY/deactivate" '{}')" | jq -e '.isActive==false' >/dev/null || fail "baja de posición vacía"
expect 200 "$(req POST "/api/v1/warehouses/$W6P/bins/$B_EMPTY/reactivate" '{}')" | jq -e '.isActive==true' >/dev/null || fail "reactivar posición"
ok "EnsureLot (lote con otras fechas 409, mismas fechas reutiliza); series con OPENJSON (repetida 409); varias líneas FEFO con 2 ISSUE Ref PICK_BATCH y stock exacto; dos dueños 400; S2 SHIPPED → AVAILABLE al eliminar, con rastro; 409 de varias líneas sin efecto parcial ni hueco EMP; S1 SCRAPPED y reingreso 409; genealogía de L0; posición con inventario 409 y vacía 200"

step "aviso de llegada de cliente (Lote 6): dueño, un recibo activo por ASN y orden de los selectores de producto"
# Maestro L298/L462 y D6: el ASN de un cliente solo lleva productos de ese dueño; un ASN tiene un solo recibo activo.
asn() { req POST /api/v1/asns "$(jq -cn --arg w "$W6P" --arg c "$C6P" --arg p "$1" '{warehousePublicId:$w,clientPublicId:$c,reference:"ASN smoke",lines:[{productPublicId:$p,expectedQty:2}]}')"; }
expect 400 "$(asn "$PN")" | jq -e --arg m "El producto PN$TS no pertenece al cliente del aviso de llegada." '[(.errors // {})[][]] | index($m) != null' >/dev/null || fail "ASN de cliente con un producto de otro dueño"
ASN6=$(expect 200 "$(asn "$P3")"); ASN6ID=$(echo "$ASN6" | jq -r .id)
echo "$ASN6" | jq -e '.statusCode=="EXPECTED"' >/dev/null || fail "ASN de cliente EXPECTED: $ASN6"
asnrcpt() { req POST /api/v1/receipts "{\"asnId\":$ASN6ID,\"stagingBinId\":$B_STG}"; }
RA1=$(expect 200 "$(asnrcpt)"); RA1P=$(echo "$RA1" | jq -r .header.publicId)
echo "$RA1" | jq -e '.lines[0].expectedQty==2 and .lines[0].receivedQty==2' >/dev/null || fail "recibo contra ASN precargado (R8)"
expect 409 "$(asnrcpt)" | jq -e '.title=="El aviso de llegada ya tiene un recibo abierto o confirmado."' >/dev/null || fail "segundo recibo sobre el mismo ASN"
expect 204 "$(req DELETE "/api/v1/receipts/$RA1P")" >/dev/null
expect 200 "$(req GET "/api/v1/asns/$ASN6ID")" | jq -e '.statusCode=="EXPECTED"' >/dev/null || fail "el ASN de cliente no quedó EXPECTED al borrar su recibo"
RA2=$(expect 200 "$(asnrcpt)"); RA2P=$(echo "$RA2" | jq -r .header.publicId)
expect 200 "$(req POST "/api/v1/receipts/$RA2P/confirm" '{}')" | jq -e '.header.statusCode=="RECEIVED"' >/dev/null || fail "confirmar el recibo del ASN"
expect 200 "$(req GET "/api/v1/asns/$ASN6ID")" | jq -e --arg r "$RA2P" '.statusCode=="RECEIVED" and .receiptPublicId==$r' >/dev/null || fail "ASN RECEIVED con su recibo"
# Recibo congelado al confirmar (maestro L300): editar su línea o borrarlo → 422.
RA2L=$(echo "$RA2" | jq -r '.lines[0].id')
expect 422 "$(req PUT "/api/v1/receipts/$RA2P/lines/$RA2L" '{"receivedQty":1}')" >/dev/null
expect 422 "$(req DELETE "/api/v1/receipts/$RA2P")" >/dev/null
# Cancelar un aviso: con recibo abierto 409; sin él → CANCELLED; ya cancelado o recibido → 422.
expect 422 "$(req POST "/api/v1/asns/$ASN6ID/cancel" '{}')" | jq -e '.title=="El aviso de llegada no está pendiente de recibir."' >/dev/null || fail "cancelar un ASN recibido"
ASNC=$(expect 200 "$(asn "$P3")" | jq -r .id)
RC=$(expect 200 "$(req POST /api/v1/receipts "{\"asnId\":$ASNC,\"stagingBinId\":$B_STG}")" | jq -r .header.publicId)
expect 409 "$(req POST "/api/v1/asns/$ASNC/cancel" '{}')" | jq -e '.title=="El aviso de llegada tiene un recibo abierto; elimínelo antes de cancelar."' >/dev/null || fail "cancelar ASN con recibo abierto"
expect 204 "$(req DELETE "/api/v1/receipts/$RC")" >/dev/null
expect 200 "$(req POST "/api/v1/asns/$ASNC/cancel" '{}')" | jq -e '.statusCode=="CANCELLED"' >/dev/null || fail "cancelar ASN"
expect 422 "$(req POST "/api/v1/asns/$ASNC/cancel" '{}')" >/dev/null
# Maestro L1207: orden de los selectores = mercancía de clientes primero (por inventario en mano) y propios al final.
expect 200 "$(req GET "/api/v1/products?selectorOrder=true&activeOnly=true&search=$TS&take=200")" | jq -e '(.items | length) > 1 and .items[0].isOwn==false and ([.items[].isOwn] == ([.items[].isOwn] | sort))' >/dev/null || fail "orden de los selectores de producto"
expect 200 "$(req GET "/api/v1/products?activeOnly=true&search=$TS&take=200")" | jq -e '[.items[].sku] == ([.items[].sku] | sort)' >/dev/null || fail "la pantalla de productos ordena por SKU"
ok "ASN de cliente con producto ajeno 400; segundo recibo sobre el mismo ASN 409; borrar el recibo deja el ASN EXPECTED y el reintento entra; confirmado → ASN RECEIVED (y el recibo congelado: PUT de línea y DELETE 422); cancelar ASN con recibo abierto 409, sin él CANCELLED, ya cancelado o recibido 422; selectores con la mercancía de clientes primero y propios al final; la pantalla por SKU"

step "recibo con ciclo de estatus (Lote 13): Esperado → Recibiendo ↔ Discrepancia → Completado con diferencia → Acomodado"
# Ciego solo con encabezado: nace EXPECTED (antes daba 400 'Indique al menos una línea.'); guarda posición, transporte y referencia.
R13=$(expect 200 "$(req POST /api/v1/receipts "$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg r "BL-$TS" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,carrier:" DHL ",reference:$r}')")")
R13P=$(echo "$R13" | jq -r .header.publicId); R13ID=$(echo "$R13" | jq -r .header.id); R13N=$(echo "$R13" | jq -r .header.number)
echo "$R13" | jq -e --argjson s "$B_STG" --arg r "BL-$TS" '.header.statusCode=="EXPECTED" and .header.isOpen and .canDelete and (.lines|length)==0 and .header.defaultStagingBinId==$s and .header.carrier=="DHL" and .header.reference==$r' >/dev/null || fail "recibo ciego solo con encabezado: $(echo "$R13" | jq -c .header)"
# Línea con esperado 5 y recibido 3 → DISCREPANCY (toma la posición del encabezado).
R13=$(expect 200 "$(req POST "/api/v1/receipts/$R13P/lines" "$(jq -cn --arg p "$PN" '{productPublicId:$p,receivedQty:3,expectedQty:5}')")")
echo "$R13" | jq -e --argjson s "$B_STG" '.header.statusCode=="DISCREPANCY" and .lines[0].expectedQty==5 and .lines[0].varianceQty==-2 and .lines[0].stagingBinId==$s' >/dev/null || fail "línea con diferencia → DISCREPANCY: $(echo "$R13" | jq -c '[.header.statusCode, .lines]')"
R13L=$(echo "$R13" | jq -r '.lines[0].id')
# PUT igual → RECEIVING; distinto otra vez → DISCREPANCY.
expect 200 "$(req PUT "/api/v1/receipts/$R13P/lines/$R13L" '{"receivedQty":5}')" | jq -e '.header.statusCode=="RECEIVING" and .lines[0].varianceQty==0' >/dev/null || fail "PUT igual a lo esperado → RECEIVING"
expect 200 "$(req PUT "/api/v1/receipts/$R13P/lines/$R13L" '{"receivedQty":4}')" | jq -e '.header.statusCode=="DISCREPANCY"' >/dev/null || fail "PUT distinto → DISCREPANCY"
expect 400 "$(req PUT "/api/v1/receipts/$R13P/lines/$R13L" '{"expectedQty":-1}')" | jq -e --arg m "La cantidad esperada no puede ser negativa." "$HASM" >/dev/null || fail "esperado negativo → 400"
# Filtros de la lista: fase y diferencia (varias con O); valores desconocidos → 400.
expect 200 "$(req GET "/api/v1/receipts?phase=OPEN&variance=SHORT&warehousePublicId=$W6P&take=200")" | jq -e --argjson r "$R13ID" 'any(.items[]; .id==$r and .isOpen and .carrier=="DHL")' >/dev/null || fail "phase=OPEN&variance=SHORT no trae el recibo"
expect 200 "$(req GET "/api/v1/receipts?variance=OVER&variance=NONE&warehousePublicId=$W6P&take=200")" | jq -e --argjson r "$R13ID" 'all(.items[]; .id!=$r)' >/dev/null || fail "variance=OVER|NONE trae un faltante"
expect 400 "$(req GET '/api/v1/receipts?phase=LATE')" | jq -e --arg m "Fase desconocida: 'LATE'. Use OPEN, PENDING_PUTAWAY o DONE." "$HASM" >/dev/null || fail "fase desconocida → 400"
expect 400 "$(req GET '/api/v1/receipts?variance=X')" | jq -e --arg m "Diferencia desconocida: 'X'. Use SHORT, OVER o NONE." "$HASM" >/dev/null || fail "diferencia desconocida → 400"
# PATCH del encabezado: transporte, referencia ('' la borra), muelle y tipo BLIND → RETURN; rowVersion viejo 409; más de 80 → 400.
RV13=$(expect 200 "$(req GET "/api/v1/receipts/$R13P")" | jq -r .rowVersion)
expect 200 "$(req PATCH "/api/v1/receipts/$R13P" "$(jq -cn --arg v "$RV13" --argjson d "$DK" '{carrier:"UPS",reference:"",dockId:$d,type:"RETURN",rowVersion:$v}')")" | jq -e --argjson d "$DK" '.header.carrier=="UPS" and .header.reference==null and .header.dockId==$d and .header.typeCode=="RETURN" and .header.statusCode=="DISCREPANCY"' >/dev/null || fail "PATCH del encabezado"
expect 409 "$(req PATCH "/api/v1/receipts/$R13P" "$(jq -cn --arg v "$RV13" '{carrier:"FedEx",rowVersion:$v}')")" >/dev/null
expect 400 "$(req PATCH "/api/v1/receipts/$R13P" "$(jq -cn --arg c "$(printf 'c%.0s' {1..81})" '{carrier:$c}')")" | jq -e --arg m "El transporte admite como máximo 80 caracteres." "$HASM" >/dev/null || fail "transporte de 81 → 400"
expect 400 "$(req PATCH "/api/v1/receipts/$R13P" "{\"warehousePublicId\":\"$DEMO\"}")" | jq -e --arg m "El almacén solo se puede cambiar en un recibo sin aviso de llegada ni orden de compra y sin líneas." "$HASM" >/dev/null || fail "cambiar el almacén de un recibo con líneas → 400"
# Confirmar con diferencia → RECEIVED_VARIANCE; en ciegos y devoluciones entra lo recibido (4) sin ajuste (decisión 3).
C13=$(expect 200 "$(req POST "/api/v1/receipts/$R13P/confirm" '{}')")
echo "$C13" | jq -e '.header.statusCode=="RECEIVED_VARIANCE" and (.header.isOpen|not) and (.canDelete|not) and .header.pendingPutawayCount==1 and .lines[0].adjustmentTxnId==null' >/dev/null || fail "confirmar con diferencia: $(echo "$C13" | jq -c .header)"
kardex "refEntity=RECEIPT&refId=$R13ID" | jq -e '.total==1 and .items[0].quantity==4 and .items[0].typeCode=="RECEIPT"' >/dev/null || fail "recibo con diferencia sin documento: un RECEIPT por lo recibido"
expect 200 "$(req GET "/api/v1/receipts?phase=PENDING_PUTAWAY&warehousePublicId=$W6P&take=200")" | jq -e --argjson r "$R13ID" 'any(.items[]; .id==$r and .pendingPutawayCount==1)' >/dev/null || fail "phase=PENDING_PUTAWAY no trae el recibo con acomodo pendiente"
expect 422 "$(req PATCH "/api/v1/receipts/$R13P" '{"carrier":"X"}')" | jq -e --arg m "El recibo $R13N ya fue confirmado; no se puede modificar." "$HASM" >/dev/null || fail "PATCH de un recibo confirmado → 422"
for T13 in $(echo "$C13" | jq -r '.putawayTasks[].id'); do expect 200 "$(req POST "/api/v1/warehouse-tasks/$T13/complete" '{}')" >/dev/null; done
expect 200 "$(req GET "/api/v1/receipts/$R13P")" | jq -e '.header.statusCode=="PUTAWAY" and .header.pendingPutawayCount==0' >/dev/null || fail "cerrar la última tarea de un Completado con diferencia → PUTAWAY"
expect 200 "$(req GET "/api/v1/status/history/RECEIPT/$R13ID")" | jq -e 'map(.toCode)==["EXPECTED","RECEIVING","DISCREPANCY","RECEIVING","DISCREPANCY","RECEIVED_VARIANCE","PUTAWAY"]' >/dev/null || fail "historial del recibo: $(req GET "/api/v1/status/history/RECEIPT/$R13ID" | sed '$d' | jq -c 'map(.toCode)')"
# Borrar un recibo en EXPECTED → 204; confirm:true sin líneas sigue siendo 400.
RE13=$(expect 200 "$(req POST /api/v1/receipts "$(jq -cn --arg w "$W6P" '{warehousePublicId:$w}')")" | jq -r .header.publicId)
expect 204 "$(req DELETE "/api/v1/receipts/$RE13")" >/dev/null
expect 400 "$(req POST /api/v1/receipts "$(jq -cn --arg w "$W6P" '{warehousePublicId:$w,confirm:true}')")" | jq -e '.errors.lines[0]=="Indique al menos una línea."' >/dev/null || fail "confirm:true sin líneas → 400"
# Avisos: referencia (sin distinguir mayúsculas) y llegada esperada (inclusive).
ASN13=$(expect 200 "$(req POST /api/v1/asns "$(jq -cn --arg w "$W6P" --arg c "$C6P" --arg p "$P3" --arg r "L13-$TS" '{warehousePublicId:$w,clientPublicId:$c,reference:$r,expectedDate:"2026-12-15",lines:[{productPublicId:$p,expectedQty:2}]}')")" | jq -r .id)
expect 200 "$(req GET "/api/v1/asns?reference=l13-$TS&expectedFrom=2026-12-15&expectedTo=2026-12-15")" | jq -e --argjson a "$ASN13" 'length==1 and .[0].id==$a' >/dev/null || fail "avisos por referencia y llegada esperada"
expect 200 "$(req GET "/api/v1/asns?reference=L13-$TS&expectedFrom=2026-12-16")" | jq -e 'length==0' >/dev/null || fail "avisos con llegada posterior"
# Contra aviso nace RECEIVING; lo esperado y el producto de la línea del documento no se cambian; el tipo tampoco.
RA13=$(expect 200 "$(req POST /api/v1/receipts "{\"asnId\":$ASN13,\"stagingBinId\":$B_STG}")"); RA13P=$(echo "$RA13" | jq -r .header.publicId); RA13L=$(echo "$RA13" | jq -r '.lines[0].id')
echo "$RA13" | jq -e '.header.statusCode=="RECEIVING" and .header.expectedDate=="2026-12-15"' >/dev/null || fail "recibo contra aviso nace RECEIVING: $(echo "$RA13" | jq -c .header)"
expect 400 "$(req PUT "/api/v1/receipts/$RA13P/lines/$RA13L" '{"expectedQty":1}')" | jq -e --arg m "La cantidad esperada solo se captura en recibos ciegos o de devolución; en uno con aviso de llegada u orden de compra viene del documento." "$HASM" >/dev/null || fail "esperado en un recibo con aviso → 400"
expect 400 "$(req POST "/api/v1/receipts/$RA13P/lines" "$(jq -cn --arg p "$P3" '{productPublicId:$p,receivedQty:1,expectedQty:1}')")" | jq -e '.errors["line.expectedQty"][0]=="La cantidad esperada solo se captura en recibos ciegos o de devolución; en uno con aviso de llegada u orden de compra viene del documento."' >/dev/null || fail "línea extra con esperado en un recibo con aviso → 400"
expect 400 "$(req PUT "/api/v1/receipts/$RA13P/lines/$RA13L" "{\"productPublicId\":\"$PN\"}")" | jq -e --arg m "El producto de una línea del aviso de llegada o de la orden de compra no se puede cambiar." "$HASM" >/dev/null || fail "cambiar el producto de una línea del aviso → 400"
expect 400 "$(req PATCH "/api/v1/receipts/$RA13P" '{"type":"BLIND"}')" | jq -e --arg m "El tipo de un recibo con aviso de llegada u orden de compra no se puede cambiar." "$HASM" >/dev/null || fail "cambiar el tipo de un recibo con aviso → 400"
expect 204 "$(req DELETE "/api/v1/receipts/$RA13P")" >/dev/null
expect 200 "$(req POST "/api/v1/asns/$ASN13/cancel" '{}')" >/dev/null
ok "ciego solo con encabezado EXPECTED → línea con diferencia DISCREPANCY → PUT igual RECEIVING → distinto DISCREPANCY (esperado negativo 400); phase/variance y sus 400; PATCH de transporte, referencia, muelle y tipo (rowVersion viejo 409, transporte de 81 400, almacén con líneas 400); confirmado RECEIVED_VARIANCE con RECEIPT por lo recibido y en PENDING_PUTAWAY; PATCH de confirmado 422; última tarea → PUTAWAY con historial completo; borrar un EXPECTED 204; confirm sin líneas 400; avisos por referencia y llegada; contra aviso RECEIVING y esperado/producto/tipo fijos 400"

step "compras y faltantes (Lote 6): cerrar en paralelo, reordenar, ajuste manual y orden cancelada"
SUP=$(expect 200 "$(req POST /api/v1/suppliers "{\"name\":\"Proveedor $TS\"}")" | jq -r .id)
expect 409 "$(req POST /api/v1/suppliers "{\"name\":\"Proveedor $TS\"}")" | jq -e '.title=="Ya existe un proveedor activo con ese nombre."' >/dev/null || fail "proveedor repetido → 409"
SUPX=$(expect 200 "$(req POST /api/v1/suppliers "{\"name\":\"Proveedor baja $TS\"}")" | jq -r .id)
expect 200 "$(req POST "/api/v1/suppliers/$SUPX/deactivate" '{}')" | jq -e '.isActive==false' >/dev/null || fail "baja de proveedor"
expect 422 "$(req POST /api/v1/purchase-orders "$(jq -cn --argjson s "$SUPX" --arg w "$W6P" --arg p "$PN" '{supplierId:$s,warehousePublicId:$w,lines:[{productPublicId:$p,qtyOrdered:1,unitCost:1}]}')")" | jq -e '.title=="El proveedor está dado de baja; no admite órdenes de compra nuevas."' >/dev/null || fail "OC con proveedor dado de baja → 422"
expect 200 "$(req POST "/api/v1/suppliers/$SUPX/reactivate" '{}')" | jq -e '.isActive==true' >/dev/null || fail "reactivar proveedor"
po() { expect 200 "$(req POST /api/v1/purchase-orders "$(jq -cn --argjson s "$SUP" --arg w "$W6P" --argjson l "$1" '{supplierId:$s,warehousePublicId:$w,lines:$l}')")"; }
# Maestro L326: un producto inactivo no admite líneas de compra (400 por línea).
expect 200 "$(req POST "/api/v1/products/$PD/deactivate" '{}')" >/dev/null
expect 400 "$(req POST /api/v1/purchase-orders "$(jq -cn --argjson s "$SUP" --arg w "$W6P" --arg p "$PD" '{supplierId:$s,warehousePublicId:$w,lines:[{productPublicId:$p,qtyOrdered:1,unitCost:1}]}')")" | jq -e --arg m "El producto PD$TS está inactivo; no admite órdenes de compra." '.errors["lines[0]"][0]==$m' >/dev/null || fail "línea de compra con producto inactivo (L326)"
expect 200 "$(req POST "/api/v1/products/$PD/reactivate" '{}')" >/dev/null
receivepo() { # po_publicId receivedQty… (en el orden de las líneas); confirma y devuelve el recibo
  local r rp lines i=0; r=$(expect 200 "$(req POST /api/v1/receipts "{\"purchaseOrderPublicId\":\"$1\",\"stagingBinId\":$B_STG}")"); rp=$(echo "$r" | jq -r .header.publicId)
  shift; for q in "$@"; do local lid; lid=$(echo "$r" | jq -r ".lines[$i].id"); expect 200 "$(req PUT "/api/v1/receipts/$rp/lines/$lid" "{\"receivedQty\":$q}")" >/dev/null; i=$((i+1)); done
  expect 200 "$(req POST "/api/v1/receipts/$rp/confirm" '{}')"; }
PO1=$(po "[{\"productPublicId\":\"$PN\",\"qtyOrdered\":10,\"unitCost\":2.5}]"); PO1P=$(pid "$PO1"); PO1L=$(echo "$PO1" | jq -r '.lines[0].id')
# Maestro 13B (D46/D47): en DRAFT la orden se edita (200) y, sin recepciones, se elimina (204; un segundo borrado 404).
PO5=$(po "[{\"productPublicId\":\"$PN\",\"qtyOrdered\":1,\"unitCost\":1}]"); PO5P=$(pid "$PO5")
expect 200 "$(req PATCH "/api/v1/purchase-orders/$PO5P" "{\"notes\":\"ajuste\",\"lines\":[{\"productPublicId\":\"$PN\",\"qtyOrdered\":3,\"unitCost\":2},{\"productPublicId\":\"$PR\",\"qtyOrdered\":1,\"unitCost\":1}]}")" | jq -e '.statusCode=="DRAFT" and .notes=="ajuste" and (.lines|length)==2 and any(.lines[]; .qtyOrdered==3 and .unitCost==2)' >/dev/null || fail "PATCH en DRAFT 200"
expect 200 "$(req PATCH "/api/v1/purchase-orders/$PO5P" "{\"supplierId\":$SUPX,\"warehousePublicId\":\"$W6P\"}")" | jq -e --argjson s "$SUPX" --arg w "$W6P" '.statusCode=="DRAFT" and .supplierId==$s and .warehousePublicId==$w' >/dev/null || fail "cambiar proveedor de la OC en DRAFT"
expect 204 "$(req DELETE "/api/v1/purchase-orders/$PO5P")" >/dev/null
expect 404 "$(req DELETE "/api/v1/purchase-orders/$PO5P")" >/dev/null
expect 200 "$(req POST "/api/v1/purchase-orders/$PO1P/send" '{}')" | jq -e '.statusCode=="SENT" and (.canEdit|not)' >/dev/null || fail "enviar PO1"
expect 422 "$(req PATCH "/api/v1/purchase-orders/$PO1P" '{"notes":"x"}')" >/dev/null   # EDIT_PURCHASE_ORDER negada en SENT
# Ajuste del 2026-09-30: proveedor y almacén solo cambian en DRAFT; en SENT un valor distinto → 409 (antes que la capacidad).
expect 409 "$(req PATCH "/api/v1/purchase-orders/$PO1P" "{\"supplierId\":$SUPX}")" | jq -e '.title=="El proveedor y el almacén solo se cambian mientras la orden de compra está en borrador."' >/dev/null || fail "proveedor de la OC fijo fuera de borrador"
expect 400 "$(req PATCH "/api/v1/purchase-orders/$PO1P" '{"number":"PO-X"}')" | jq -e --arg m "El campo number de la orden de compra no se puede cambiar." "$HASM" >/dev/null || fail "número de la OC fijo"
# Recibir contra una OC exige además purchasing.receive (el controlador solo pide warehouse.receive).
expect 403 "$(req POST /api/v1/receipts "{\"purchaseOrderPublicId\":\"$PO1P\",\"stagingBinId\":$B_STG}" "$TADJ6")" | denied purchasing.receive || fail "recibo contra PO sin purchasing.receive"
receivepo "$PO1P" 8 >/dev/null
expect 200 "$(req GET "/api/v1/purchase-orders/$PO1P")" | jq -e '.statusCode=="PARTIAL"' >/dev/null || fail "PO1 PARTIAL"
expect 200 "$(req GET /api/v1/purchase-orders/shortages)" | jq -e --arg p "$PO1P" 'any(.[]; .publicId==$p and .qtyPending==2)' >/dev/null || fail "faltante de PO1"
# Fase 7 (Ajustes de inventario): el resumen trae el almacén de la orden (ajuste manual) y sus líneas en faltante.
expect 200 "$(req GET /api/v1/purchase-orders/shortages)" | jq -e --arg p "$PO1P" --arg w "$W6P" 'any(.[]; .publicId==$p and .warehousePublicId==$w and .warehouseCode!="" and .linesWithShortage==1)' >/dev/null || fail "almacén y líneas en el resumen de faltantes de PO1"
TMP6=$(mktemp -d)
for i in 1 2; do req POST "/api/v1/purchase-orders/$PO1P/lines/$PO1L/resolve" '{"action":"CLOSE"}' > "$TMP6/$i" & done; wait
CODES=$(codes "$TMP6"/1 "$TMP6"/2); rm -rf "$TMP6"
[[ "$CODES" == "200 409 " ]] || fail "dos resoluciones del mismo faltante: $CODES"
expect 200 "$(req GET "/api/v1/purchase-orders/$PO1P")" | jq -e '.statusCode=="RECEIVED"' >/dev/null || fail "PO1 RECEIVED al resolver"
kardex "refEntity=PURCHASE_ORDER&refId=$(echo "$PO1" | jq -r .id)" | jq -e '.total==0' >/dev/null || fail "CLOSE no mueve inventario"
PO2=$(po "[{\"productPublicId\":\"$PN\",\"qtyOrdered\":5,\"unitCost\":2.5},{\"productPublicId\":\"$PR\",\"qtyOrdered\":4,\"unitCost\":1}]"); PO2P=$(pid "$PO2")
PO2A=$(echo "$PO2" | jq -r '.lines[0].id'); PO2B=$(echo "$PO2" | jq -r '.lines[1].id')
expect 200 "$(req POST "/api/v1/purchase-orders/$PO2P/send" '{}')" >/dev/null; receivepo "$PO2P" 3 2 >/dev/null
# REORDER exige purchasing.manage (el controlador pide inventory.adjust): el rol de ajustes sin compras → 403 sin efecto.
expect 403 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2A/resolve" '{"action":"REORDER"}' "$TADJ6")" | denied purchasing.manage || fail "REORDER sin purchasing.manage"
expect 200 "$(req GET "/api/v1/purchase-orders/$PO2P/shortage-lines")" | jq -e --argjson l "$PO2A" 'any(.[]; .purchaseOrderLineId==$l and .qtyPending==2 and (.resolutions | length)==0)' >/dev/null || fail "el REORDER sin permiso dejó efectos"
expect 200 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2A/resolve" '{"action":"REORDER"}')" | jq -e --argjson s "$SUP" '.reorder.statusCode=="DRAFT" and .reorder.supplierId==$s and .reorder.lines[0].qtyOrdered==2 and .reorder.lines[0].unitCost==2.5' >/dev/null || fail "REORDER"
expect 403 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2B/resolve" "{\"action\":\"MANUAL_ADJUSTMENT\",\"quantity\":1,\"binId\":$B_RSV}" "$T2")" >/dev/null   # sin inventory.adjust
expect 400 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2B/resolve" "{\"action\":\"MANUAL_ADJUSTMENT\",\"quantity\":3,\"binId\":$B_RSV,\"notes\":\"Faltante del humo\"}")" >/dev/null   # excede el pendiente (2)
# Lote 13: la nota es obligatoria al resolver con ajuste manual (400 errors.notes)
expect 400 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2B/resolve" "{\"action\":\"MANUAL_ADJUSTMENT\",\"quantity\":1,\"binId\":$B_RSV}")" | jq -e '.errors.notes[0]=="Escriba una nota que explique el ajuste."' >/dev/null || fail "ajuste manual sin nota"
expect 200 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2B/resolve" "{\"action\":\"MANUAL_ADJUSTMENT\",\"quantity\":1,\"binId\":$B_RSV,\"notes\":\"Faltante del humo\"}")" | jq -e '.line.qtyPending==1 and .purchaseOrder.statusCode=="PARTIAL"' >/dev/null || fail "ajuste manual parcial"
kardex "refEntity=PURCHASE_ORDER&refId=$(echo "$PO2" | jq -r .id)" | jq -e '.total==1 and .items[0].quantity==1 and .items[0].reasonCode=="PO_SHORTAGE"' >/dev/null || fail "ADJUSTMENT +1 con Ref PURCHASE_ORDER"
expect 200 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2B/resolve" "{\"action\":\"MANUAL_ADJUSTMENT\",\"quantity\":1,\"binId\":$B_RSV,\"notes\":\"Faltante del humo\"}")" | jq -e '.purchaseOrder.statusCode=="RECEIVED"' >/dev/null || fail "PO2 RECEIVED"
expect 409 "$(req POST "/api/v1/purchase-orders/$PO2P/lines/$PO2B/resolve" '{"action":"CLOSE"}')" | jq -e '.title=="La línea ya no tiene faltante pendiente."' >/dev/null || fail "segunda resolución 409"
PO3=$(po "[{\"productPublicId\":\"$PN\",\"qtyOrdered\":6,\"unitCost\":2.5}]"); PO3P=$(pid "$PO3"); PO3L=$(echo "$PO3" | jq -r '.lines[0].id')
expect 200 "$(req POST "/api/v1/purchase-orders/$PO3P/send" '{}')" >/dev/null; receivepo "$PO3P" 4 >/dev/null
# D47: con un recibo OPEN contra la orden no se cancela (409); al eliminar el recibo, sí.
RO3P=$(expect 200 "$(req POST /api/v1/receipts "{\"purchaseOrderPublicId\":\"$PO3P\",\"stagingBinId\":$B_STG}")" | jq -r .header.publicId)
expect 409 "$(req POST "/api/v1/purchase-orders/$PO3P/cancel" '{}')" | jq -e '.title=="La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar."' >/dev/null || fail "cancelar PO con recibo OPEN (D47)"
expect 200 "$(req GET "/api/v1/purchase-orders/$PO3P")" | jq -e '.statusCode=="PARTIAL"' >/dev/null || fail "la PO cambió tras el 409"
expect 204 "$(req DELETE "/api/v1/receipts/$RO3P")" >/dev/null
expect 200 "$(req POST "/api/v1/purchase-orders/$PO3P/cancel" '{"comment":"El proveedor no surte el resto"}')" | jq -e '.statusCode=="CANCELLED"' >/dev/null || fail "cancelar desde PARTIAL (D47)"
expect 422 "$(req POST "/api/v1/purchase-orders/$PO3P/lines/$PO3L/resolve" '{"action":"CLOSE"}')" | jq -e '.title=="La orden de compra está cancelada; su faltante ya no se resuelve."' >/dev/null || fail "faltante de una PO cancelada"
expect 200 "$(req GET /api/v1/purchase-orders/shortages)" | jq -e --arg p "$PO3P" 'all(.[]; .publicId!=$p)' >/dev/null || fail "PO cancelada en la lista de faltantes"
expect 409 "$(req DELETE "/api/v1/purchase-orders/$PO3P")" | jq -e '.title=="Una orden de compra cancelada con recepciones se conserva con su bitácora; no se elimina."' >/dev/null || fail "eliminar PO cancelada con recepciones"
ok "proveedor repetido 409, dado de baja no admite OC (422) y se reactiva; línea de compra con producto inactivo 400; OC en DRAFT editada (200) y eliminada (204, luego 404); PO SENT sin edición (422) y proveedor fijo (400); recibo contra PO sin purchasing.receive 403; recibo contra PO → PARTIAL; REORDER sin purchasing.manage 403 sin efecto; dos CLOSE en paralelo 200 + 409 y PO RECEIVED sin movimiento; REORDER → DRAFT al mismo proveedor por 2 a 2.5; MANUAL 1 → ADJUSTMENT +1 PO_SHORTAGE con Ref PURCHASE_ORDER, excedido 400, sin inventory.adjust 403; con recibo OPEN no se cancela (409); cancelada desde PARTIAL: 422 al resolver, fuera de faltantes y no se elimina (409 con su mensaje)"

step "actividad reciente (Lote 7A): recibo confirmado obligatorio, solo obligatorios, belowMin y permiso del módulo"
activity() { # query [token] → todos los eventos de la ventana (páginas de 50) como un solo arreglo JSON
  # las páginas se acumulan en un archivo (no con --argjson): con una base ya usada el arreglo supera el límite de
  # argumentos de Windows/Git Bash ("Argument list too long")
  local q=$1 tok=${2:-$TOKEN} skip=0 page total acc
  acc=$(mktemp -p "$REQ_TMPDIR")
  while :; do
    page=$(expect 200 "$(req GET "/api/v1/analytics/activity?$q&skip=$skip&take=50" '' "$tok")")
    jq -c '.items' <<<"$page" >> "$acc"; total=$(jq -r .total <<<"$page")
    skip=$((skip+50)); [[ $skip -lt $total && $skip -lt 5000 ]] || break
  done
  jq -cs 'add // []' "$acc"
  rm -f "$acc"
}
EV=$(activity "module=WAREHOUSE")
# Tareas (maestro, catálogo de Almacén): PUTAWAY completada, reabasto completado y PUTAWAY cancelada desde la cola.
echo "$EV" | jq -e --argjson t "$PUT6" 'any(.[]; .code=="PUTAWAY_DONE" and .entityType=="WAREHOUSE_TASK" and .entityId==$t)' >/dev/null || fail "PUTAWAY_DONE de la tarea $PUT6"
echo "$EV" | jq -e 'any(.[]; .code=="REPLENISH_DONE" and .entityType=="WAREHOUSE_TASK")' >/dev/null || fail "REPLENISH_DONE del reabasto"
echo "$EV" | jq -e --argjson t "$PUTA" 'any(.[]; .code=="TASK_CANCELLED" and .entityType=="WAREHOUSE_TASK" and .entityId==$t)' >/dev/null || fail "TASK_CANCELLED de la PUTAWAY $PUTA"
# Aviso de llegada cancelado (opcional) y recolección eliminada (PICK_CANCELLED, obligatorio).
echo "$EV" | jq -e --argjson a "$ASNC" 'any(.[]; .code=="ASN_CANCELLED" and .entityType=="ASN" and .entityId==$a and .mandatory==false)' >/dev/null || fail "ASN_CANCELLED del aviso $ASNC"
echo "$EV" | jq -e --argjson r "$R6ID" 'any(.[]; .code=="RECEIPT_CONFIRMED" and .module=="WAREHOUSE" and .entityType=="RECEIPT" and .entityId==$r and .mandatory==true and (.label|length)>0 and (.userId != null) and ((.userName // "") | length) > 0)' >/dev/null || fail "RECEIPT_CONFIRMED obligatorio del recibo $R6ID (con quién lo hizo)"
echo "$EV" | jq -e --arg p "$PO1P" 'any(.[]; .code=="PO_SENT" and .entityType=="PURCHASE_ORDER" and .publicId==$p and .mandatory==false and (.userId != null) and ((.userName // "") | length) > 0)' >/dev/null || fail "PO_SENT (opcional) de la orden enviada (con quién lo hizo)"
# Recibo R6 pasó a PUTAWAY con la última tarea; PO1 quedó RECEIVED al resolver su faltante (CLOSE).
echo "$EV" | jq -e --argjson r "$R6ID" 'any(.[]; .code=="RECEIPT_PUTAWAY_DONE" and .entityType=="RECEIPT" and .entityId==$r and .mandatory==false)' >/dev/null || fail "RECEIPT_PUTAWAY_DONE del recibo $R6ID"
echo "$EV" | jq -e --arg p "$PO1P" 'any(.[]; .code=="PO_RECEIVED" and .entityType=="PURCHASE_ORDER" and .publicId==$p and .mandatory==false)' >/dev/null || fail "PO_RECEIVED de PO1"
# PO2: tres resoluciones de faltante en la ventana (REORDER y dos MANUAL_ADJUSTMENT de 1), enlazadas a la orden.
echo "$EV" | jq -e --arg p "$PO2P" --arg n "$(echo "$PO2" | jq -r .number)" '[.[] | select(.code=="PO_SHORTAGE_RESOLVED" and .entityType=="PURCHASE_ORDER" and .publicId==$p and .reference==$n and .mandatory==false)] | length==3' >/dev/null || fail "PO_SHORTAGE_RESOLVED de PO2 (REORDER + 2 ajustes manuales) con referencia = número de PO2"
# PD se dio de baja dos veces (y se reactivó): cada baja es un PRODUCT_DEACTIVATED aunque hoy esté activo.
echo "$EV" | jq -e --arg p "$PD" '[.[] | select(.code=="PRODUCT_DEACTIVATED" and .entityType=="PRODUCT" and .publicId==$p)] | length>=2' >/dev/null || fail "PRODUCT_DEACTIVATED de las bajas de PD (reactivado)"
expect 200 "$(req GET '/api/v1/analytics/activity?take=1')" | jq -e '.visibleModules==["WAREHOUSE"] and (.items|length)==1 and .total>=2' >/dev/null || fail "sin module → primer módulo visible (WAREHOUSE) con total y take=1"
EM=$(activity "module=WAREHOUSE&onlyMandatory=true")
echo "$EM" | jq -e --arg p "$PO3P" 'any(.[]; .code=="PO_CANCELLED" and .entityType=="PURCHASE_ORDER" and .publicId==$p and .mandatory==true)' >/dev/null || fail "PO_CANCELLED (obligatorio) de PO3"
echo "$EM" | jq -e 'length>=1 and all(.[]; .mandatory==true) and all(.[]; .code!="PO_SENT") and any(.[]; .code=="RECEIPT_CONFIRMED")' >/dev/null || fail "onlyMandatory incluye opcionales (PO_SENT)"
echo "$EM" | jq -e --arg p "$PB7P" 'any(.[]; .code=="PICK_CANCELLED" and .entityType=="PICK_BATCH" and .publicId==$p and .mandatory==true)' >/dev/null || fail "PICK_CANCELLED de la recolección eliminada $PB7P"
expect 200 "$(req GET '/api/v1/analytics/activity?module=WAREHOUSE&window=today&take=1')" >/dev/null
expect 400 "$(req GET '/api/v1/analytics/activity?module=WAREHOUSE&take=51')" | jq -e --arg m "El máximo por página es 50." "$HASM" >/dev/null || fail "take > 50 → 400"
expect 400 "$(req GET '/api/v1/analytics/activity?window=7d')" | jq -e --arg m "La ventana debe ser 24h, 48h o today." "$HASM" >/dev/null || fail "ventana inválida → 400"
TD7=$(login "$DISPATCH_EMAIL" "$PASS")
# El despachador pasa la política (tiene analytics.view): el 403 y su PERMISSION_DENIED los pone el servicio.
PD7=$(pdcount)
expect 403 "$(req GET '/api/v1/analytics/activity?module=WAREHOUSE' '' "$TD7")" | jq -e '.title=="No tiene permiso para ver la actividad del módulo WAREHOUSE."' >/dev/null || fail "despachador sin inventory.view ve la actividad de Almacén"
[[ $(pdcount) -gt $PD7 ]] || fail "PERMISSION_DENIED del 403 de la actividad del módulo"
expect 200 "$(req GET '/api/v1/analytics/activity' '' "$TD7")" | jq -e '.visibleModules==[] and .total==0' >/dev/null || fail "despachador: sin módulos visibles"
# Lote F8a: el Operador de almacén ahora tiene analytics.view (para ver "Actividad reciente" en su Pulso, junto con
# pulse.activity); la política ya no lo bloquea, pero el servicio lo sigue acotando a WAREHOUSE por su inventory.view.
expect 200 "$(req GET '/api/v1/analytics/activity?module=WAREHOUSE' '' "$TWH6")" | jq -e '.visibleModules==["WAREHOUSE"]' >/dev/null || fail "operador de almacén (con analytics.view, Lote F8a) ve la actividad de Almacén"
# Hallazgo S1: analytics.view solo, sin admin.audit, no debe alcanzar para leer AUDIT_LOG/SECURITY_EVENT por §2.2
# (antes de la corrección, la vista previa de informes no aplicaba esa regla y sí dejaba leerlas).
expect 200 "$(req GET /api/v1/analytics/data-sources '' "$TWH6")" | jq -e 'map(.key) | (index("AUDIT_LOG") == null) and (index("SECURITY_EVENT") == null)' >/dev/null || fail "operador de almacén (sin admin.audit) no debe ver AUDIT_LOG/SECURITY_EVENT en data-sources"
expect 404 "$(req POST '/api/v1/analytics/reports/AUDIT_LOG/preview?dateRangeMode=ALL' '{"name":"x","columns":["CreatedAtUtc","Action"]}' "$TWH6")" >/dev/null || fail "operador de almacén (sin admin.audit) no debe poder previsualizar AUDIT_LOG (hallazgo S1)"
# Producto con mínimo y sin saldo: en SQL Server el SUM de un conjunto vacío es NULL (InMemory da 0); debe contar como bajo mínimo.
PBM=$(prod "{\"sku\":\"PBM$TS\",\"name\":\"Bajo mínimo 7A $TS\",\"minQty\":5}")
expect 200 "$(req GET "/api/v1/products?belowMin=true&search=PBM$TS")" | jq -e --arg s "PBM$TS" '.total==1 and .items[0].sku==$s and .items[0].isBelowMin==true' >/dev/null || fail "belowMin no incluye un producto con mínimo y sin saldo (SUM NULL en SQL)"
# PN tiene saldo y no tiene mínimo: no está bajo mínimo.
expect 200 "$(req GET "/api/v1/products?belowMin=true&search=PN$TS")" | jq -e '.total==0' >/dev/null || fail "belowMin incluye un producto sin mínimo (PN)"
BM=$(expect 200 "$(req GET '/api/v1/products?belowMin=true&take=200')")
echo "$BM" | jq -e '.total>=1 and (.items|length)==([.total,200]|min) and all(.items[]; .isBelowMin==true)' >/dev/null || fail "belowMin devuelve productos que no están bajo mínimo"
expect 200 "$(req GET '/api/v1/products?belowMin=true&take=1')" | jq -e --argjson t "$(echo "$BM" | jq .total)" '.total==$t' >/dev/null || fail "belowMin con take=1 cuenta distinto"
ok "RECEIPT_CONFIRMED del recibo confirmado (obligatorio) y PO_SENT (opcional) en Almacén, con quién lo hizo; RECEIPT_PUTAWAY_DONE del recibo R6, PO_RECEIVED de PO1 y PO_CANCELLED (obligatorio) de PO3; PUTAWAY_DONE, REPLENISH_DONE y TASK_CANCELLED de las tareas; ASN_CANCELLED del aviso cancelado; PICK_CANCELLED (obligatorio) de la recolección eliminada; 3 PO_SHORTAGE_RESOLVED de PO2; PRODUCT_DEACTIVATED de PD aunque se reactivó; sin module → WAREHOUSE; onlyMandatory sin PO_SENT; take 51 → 400 y ventana inválida → 400; despachador (sin inventory.view) 403 'No tiene permiso para ver la actividad del módulo WAREHOUSE.' con PERMISSION_DENIED y sin pestañas; operador de almacén (con analytics.view, Lote F8a) ve solo WAREHOUSE; belowMin incluye el producto con mínimo y sin saldo, solo bajo mínimo y con el mismo total en take=1"

step "conteo cíclico con filtro de almacenes (Lote 6)"
CC6=$(expect 200 "$(req POST /api/v1/cycle-counts "{\"warehousePublicId\":\"$W6P\",\"binIds\":[$B_PCK]}")"); CC6ID=$(echo "$CC6" | jq -r .count.id)
expect 200 "$(req GET "/api/v1/cycle-counts?warehousePublicIds=$W6P&warehousePublicIds=$W7P")" | jq -e --argjson c "$CC6ID" 'any(.[]; .id==$c)' >/dev/null || fail "lista de conteos con almacenes múltiples"
expect 200 "$(req GET "/api/v1/cycle-counts?warehousePublicIds=$W7P")" | jq -e --argjson c "$CC6ID" 'all(.[]; .id!=$c)' >/dev/null || fail "filtro de almacén del conteo"
expect 403 "$(req POST "/api/v1/cycle-counts/$CC6ID/reconcile" '{}' "$T2")" >/dev/null   # D22: el despachador no tiene warehouse.count
CT6=$(tasksof "$W6P" COUNT | jq -r --argjson c "$CC6ID" '[.items[] | select(.refEntityCode=="CYCLE_COUNT" and .refId==$c)][0].id')
[[ "$CT6" =~ ^[0-9]+$ ]] || fail "tarea COUNT del conteo"
expect 422 "$(req POST "/api/v1/warehouse-tasks/$CT6/cancel" '{}')" | jq -e '.title=="Las tareas de tipo COUNT se cancelan desde su pantalla, no desde la cola."' >/dev/null || fail "cancelar COUNT desde la cola"
expect 204 "$(req DELETE "/api/v1/cycle-counts/$CC6ID")" >/dev/null   # libera la tarea COUNT (no queda abierta en W6)
# D22 / bitácora L542: el Operador (warehouse.count) concilia contra el saldo ACTUAL bloqueado. Foto 5 y 5; se capturan 6
# y 4; después de la foto sale 1 de PCC1 → su ajuste es 6 − 4 = +2 (SystemQtyChanged) y el de PCC2 4 − 5 = −1.
B_CC=$(bin "$ZPCK" P-CC)
PCC1=$(prod "{\"sku\":\"PCC1$TS\",\"name\":\"Conteo uno $TS\",\"purchaseCost\":1}"); PCC2=$(prod "{\"sku\":\"PCC2$TS\",\"name\":\"Conteo dos $TS\",\"purchaseCost\":1}")
expect 200 "$(adjust "$PCC1" "$W6P" "$B_CC" 5 FOUND)" >/dev/null; expect 200 "$(adjust "$PCC2" "$W6P" "$B_CC" 5 FOUND)" >/dev/null
CC7=$(expect 200 "$(req POST /api/v1/cycle-counts "{\"warehousePublicId\":\"$W6P\",\"binIds\":[$B_CC]}" "$TWH6")"); CC7ID=$(echo "$CC7" | jq -r .count.id)
echo "$CC7" | jq -e '(.lines | length)==2 and all(.lines[]; .systemQty==5)' >/dev/null || fail "foto del conteo: $(echo "$CC7" | jq -c '[.lines[] | {sku,systemQty}]')"
CL1=$(echo "$CC7" | jq -r --arg s "PCC1$TS" '.lines[] | select(.sku==$s) | .id'); CL2=$(echo "$CC7" | jq -r --arg s "PCC2$TS" '.lines[] | select(.sku==$s) | .id')
expect 200 "$(req PUT "/api/v1/cycle-counts/$CC7ID/lines" "{\"lines\":[{\"lineId\":$CL1,\"countedQty\":6},{\"lineId\":$CL2,\"countedQty\":4}]}" "$TWH6")" >/dev/null
expect 200 "$(collect "$W6P" "$PCC1" 1)" >/dev/null
REC7=$(expect 200 "$(req POST "/api/v1/cycle-counts/$CC7ID/reconcile" '{}' "$TWH6")")
echo "$REC7" | jq -e --argjson a "$CL1" --argjson b "$CL2" '.count.statusCode=="RECONCILED_VARIANCE"
  and any(.lines[]; .id==$a and .systemQtyChanged==true and .reconciledSystemQty==4 and .adjustedQty==2 and .adjustmentTxnId!=null)
  and any(.lines[]; .id==$b and .systemQtyChanged==false and .reconciledSystemQty==5 and .adjustedQty==-1 and .adjustmentTxnId!=null)' >/dev/null || fail "conciliación contra el saldo actual: $(echo "$REC7" | jq -c '[.lines[] | {sku,systemQty,countedQty,reconciledSystemQty,systemQtyChanged,adjustedQty}]')"
kardex "refEntity=CYCLE_COUNT&refId=$CC7ID" | jq -e '.total==2 and all(.items[]; .typeCode=="ADJUSTMENT" and .reasonCode=="COUNT_VARIANCE") and ([.items[].quantity] | sort)==[-1,2]' >/dev/null || fail "ADJUSTMENT COUNT_VARIANCE +2 y −1"
[[ $(onhand "$W6P" "$B_CC" "$PCC1") == 6 && $(onhand "$W6P" "$B_CC" "$PCC2") == 4 ]] || fail "saldos tras conciliar (6 y 4)"
expect 422 "$(req PUT "/api/v1/cycle-counts/$CC7ID/lines" "{\"lines\":[{\"lineId\":$CL1,\"countedQty\":7}]}" "$TWH6")" | jq -e '.title=="El conteo ya fue reconciliado; solo se consulta."' >/dev/null || fail "capturar después de conciliar"
expect 200 "$(req GET "/api/v1/warehouse-tasks?warehousePublicId=$W6P&types=COUNT&includeClosed=true&take=200")" | jq -e --argjson c "$CC7ID" 'any(.items[]; .refEntityCode=="CYCLE_COUNT" and .refId==$c and .statusCode=="DONE")' >/dev/null || fail "tarea COUNT DONE al conciliar"
# Lote 7A: la conciliación aparece en Actividad reciente como COUNT_VARIANCE (obligatorio) enlazado al conteo.
EM7=$(activity "module=WAREHOUSE&onlyMandatory=true")
echo "$EM7" | jq -e --argjson c "$CC7ID" 'any(.[]; .code=="COUNT_VARIANCE" and .entityType=="CYCLE_COUNT" and .entityId==$c and .mandatory==true)' >/dev/null || fail "COUNT_VARIANCE del conteo $CC7ID en Actividad reciente"
echo "$EM7" | jq -e --argjson c "$CC7ID" 'any(.[]; .code=="COUNT_RECONCILED" and .entityType=="CYCLE_COUNT" and .entityId==$c and .mandatory==true)' >/dev/null || fail "COUNT_RECONCILED del conteo $CC7ID en Actividad reciente"
ok "conteo conciliado por el Operador contra el saldo actual (+2 con systemQtyChanged, −1 sin él; COUNT_VARIANCE y COUNT_RECONCILED; captura posterior 422; tarea COUNT DONE); conteo CC sobre P-01; filtro warehousePublicIds de selección múltiple (maestro L553); reconciliar sin warehouse.count 403; COUNT no se cancela desde la cola (422)"

step "cruce de muelle (Lote 6, módulo CROSSDOCK): 403 apagado, citas solapadas y tarea de la cola con el módulo"
expect 403 "$(req GET /api/v1/cross-dock-plans)" | jq -e '.code=="module_disabled"' >/dev/null || fail "CROSSDOCK apagado"
expect 403 "$(req GET /api/v1/dock-appointments)" | jq -e '.code=="module_disabled"' >/dev/null || fail "citas con CROSSDOCK apagado"
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken)
expect 200 "$(req PUT /api/v1/modules/CROSSDOCK '{"isEnabled":true}')" >/dev/null
ST=$(date -u -d "tomorrow 09:00" +%FT%T); EN=$(date -u -d "tomorrow 10:00" +%FT%T); ST2=$(date -u -d "tomorrow 09:30" +%FT%T)
TMP6=$(mktemp -d)
req POST /api/v1/dock-appointments "{\"warehousePublicId\":\"$W6P\",\"dockId\":$DK,\"direction\":\"INBOUND\",\"scheduledStartUtc\":\"${ST}Z\",\"scheduledEndUtc\":\"${EN}Z\"}" > "$TMP6/1" &
req POST /api/v1/dock-appointments "{\"warehousePublicId\":\"$W6P\",\"dockId\":$DK,\"direction\":\"INBOUND\",\"scheduledStartUtc\":\"${ST2}Z\"}" > "$TMP6/2" &
wait; CODES=$(codes "$TMP6"/1 "$TMP6"/2); rm -rf "$TMP6"
[[ "$CODES" == "200 409 " ]] || fail "citas solapadas en paralelo: $CODES"
# Agenda (maestro L317, D30): reprogramar sobre otra cita 409; ARRIVED ocupa el muelle y COMPLETED lo libera.
APPTS=$(expect 200 "$(req GET "/api/v1/dock-appointments?warehousePublicId=$W6P&dockId=$DK")")
AP1=$(echo "$APPTS" | jq -r '[.[] | select(.statusCode=="SCHEDULED")][0].id')
ST3=$(date -u -d "tomorrow 11:00" +%FT%T); EN3=$(date -u -d "tomorrow 11:30" +%FT%T)
AP2=$(expect 200 "$(req POST /api/v1/dock-appointments "{\"warehousePublicId\":\"$W6P\",\"dockId\":$DK,\"direction\":\"INBOUND\",\"scheduledStartUtc\":\"${ST3}Z\",\"scheduledEndUtc\":\"${EN3}Z\"}")" | jq -r .id)
expect 409 "$(req PATCH "/api/v1/dock-appointments/$AP2" "{\"scheduledStartUtc\":\"${ST2}Z\"}")" | jq -e '.title=="El muelle ya tiene una cita que se solapa con ese horario."' >/dev/null || fail "reprogramar sobre otra cita → 409"
expect 200 "$(req POST "/api/v1/dock-appointments/$AP1/status" '{"status":"ARRIVED"}')" | jq -e '.statusCode=="ARRIVED" and .dockStatusCode=="OCCUPIED"' >/dev/null || fail "ARRIVED ocupa el muelle"
expect 200 "$(req POST "/api/v1/dock-appointments/$AP1/status" '{"status":"COMPLETED"}')" | jq -e '.statusCode=="COMPLETED" and .dockStatusCode=="FREE"' >/dev/null || fail "COMPLETED libera el muelle"
expect 200 "$(req POST "/api/v1/dock-appointments/$AP2/status" '{"status":"CANCELLED","comment":"Sin carga"}')" | jq -e '.statusCode=="CANCELLED"' >/dev/null || fail "cancelar la cita"
# Modo (a) (D29, maestro L320): asignación sobre un recibo ABIERTO; al confirmar se reparte FIFO con el faltante visible.
B_XD=$(bin "$ZSTG" STG-02)
PLANA=$(expect 200 "$(req POST /api/v1/cross-dock-plans "{\"warehousePublicId\":\"$W6P\",\"stagingZoneId\":$ZSTG}")" | jq -r .id)
RXA=$(expect 200 "$(blind "$W6P" "$B_XD" "$PX" 10)"); RXAP=$(echo "$RXA" | jq -r .header.publicId); RXAID=$(echo "$RXA" | jq -r .header.id); RXAL=$(echo "$RXA" | jq -r '.lines[0].id')
neworder() { expect 200 "$(req POST /api/v1/orders "$(jq -cn --arg c "$C6P" --arg l "$L6" '{clientPublicId:$c,consigneeLocationPublicId:$l,serviceType:"STANDARD",packages:[{pieces:1,packageType:"BOX"}]}')")" | jq -r .publicId; }
OA1=$(neworder); OA2=$(neworder)
alloca() { req POST "/api/v1/cross-dock-plans/$PLANA/allocations" "{\"receiptLineId\":$RXAL,\"orderPublicId\":\"$1\",\"quantity\":$2}"; }
expect 200 "$(alloca "$OA1" 4)" | jq -e 'all(.allocations[]; .confirmedQty==null and .taskId==null)' >/dev/null || fail "asignación sobre recibo abierto sin reserva ni tarea"
expect 409 "$(alloca "$OA2" 8)" | jq -e '.title | contains("excede lo disponible para cruce de muelle")' >/dev/null || fail "asignar más de lo recibido (Exceeds)"
expect 200 "$(alloca "$OA2" 6)" >/dev/null
expect 200 "$(req GET "/api/v1/cross-dock-plans/$PLANA/candidates")" | jq -e --argjson l "$RXAL" 'any(.[]; .receiptLineId==$l and .receiptStatusCode=="RECEIVING" and .allocatedQty==10 and .allocatable==0) or all(.[]; .receiptLineId!=$l)' >/dev/null || fail "candidatos del plan"
expect 200 "$(req PUT "/api/v1/receipts/$RXAP/lines/$RXAL" '{"receivedQty":8}')" >/dev/null
expect 200 "$(req POST "/api/v1/receipts/$RXAP/confirm" '{}')" | jq -e '.header.statusCode=="PUTAWAY"' >/dev/null || fail "recibo todo a cruce de muelle directo a PUTAWAY"
PA=$(expect 200 "$(req GET "/api/v1/cross-dock-plans/$PLANA")")
echo "$PA" | jq -e --arg a "$OA1" --arg b "$OA2" 'any(.allocations[]; .orderPublicId==$a and .confirmedQty==4 and .shortQty==0 and .taskId!=null) and any(.allocations[]; .orderPublicId==$b and .confirmedQty==4 and .shortQty==2 and .taskId!=null)' >/dev/null || fail "reparto FIFO con faltante outbound: $(echo "$PA" | jq -c '[.allocations[] | {quantity,confirmedQty,shortQty,taskId}]')"
tasksof "$W6P" PUTAWAY | jq -e --argjson r "$RXAID" 'all(.items[]; (.refEntityCode=="RECEIPT" and .refId==$r) | not)' >/dev/null || fail "PUTAWAY de un recibo todo a cruce de muelle"
tasksof "$W6P" CROSSDOCK | jq -e --argjson p "$PLANA" '[.items[] | select(.quantity==4)] | length >= 2' >/dev/null || fail "dos tareas CROSSDOCK por 4"
[[ $(reserved "$W6P" "$B_XD" "$PX") == 8 && $(onhand "$W6P" "$B_XD" "$PX") == 8 ]] || fail "staging con 8 en mano y 8 reservados"
expect 409 "$(collect "$W6P" "$PX" 1)" | jq -e '.code=="insufficient_stock"' >/dev/null || fail "lo reservado no se recolecta"
# Selector de recolección (R37, L781): onlyAvailable descuenta lo reservado (traducción EF de la subconsulta en SQL Server).
expect 200 "$(req GET "/api/v1/products?onlyAvailable=true&warehousePublicId=$W6P&search=PX$TS")" | jq -e '.total==0' >/dev/null || fail "onlyAvailable debe descontar lo reservado"
expect 200 "$(req GET "/api/v1/products?warehousePublicId=$W6P&search=PX$TS")" | jq -e '.total==1' >/dev/null || fail "sin onlyAvailable el producto aparece"
expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$W6P&productPublicIds=$PX&onlyAvailable=true")" | jq -e '(.items|length)==0' >/dev/null || fail "saldos onlyAvailable descuentan lo reservado"
# Conteo con contado menor que lo reservado → 409 sin escribir nada (D22).
CCX=$(expect 200 "$(req POST /api/v1/cycle-counts "{\"warehousePublicId\":\"$W6P\",\"binIds\":[$B_XD]}")"); CCXID=$(echo "$CCX" | jq -r .count.id)
CLX=$(echo "$CCX" | jq -r --arg s "PX$TS" '.lines[] | select(.sku==$s) | .id')
expect 200 "$(req PUT "/api/v1/cycle-counts/$CCXID/lines" "{\"lines\":[{\"lineId\":$CLX,\"countedQty\":5}]}")" >/dev/null
KT0=$(kardex "productPublicIds=$PX" | jq .total)
expect 409 "$(req POST "/api/v1/cycle-counts/$CCXID/reconcile" '{}')" | jq -e --arg s "El conteo de PX$TS en STG-02 (5) es menor que lo reservado (8)" '((.title // "") + " " + ([(.errors // {})[][]] | join(" "))) | contains($s)' >/dev/null || fail "conteo menor que lo reservado → 409"
[[ $(kardex "productPublicIds=$PX" | jq .total) == "$KT0" && $(onhand "$W6P" "$B_XD" "$PX") == 8 ]] || fail "el 409 del conteo escribió movimientos"
expect 204 "$(req DELETE "/api/v1/cycle-counts/$CCXID")" >/dev/null
# Mover: O1 desde la cola y O2 desde el plan; el staging queda en cero sin reserva.
TA1=$(echo "$PA" | jq -r --arg a "$OA1" '.allocations[] | select(.orderPublicId==$a) | .taskId'); AA2=$(echo "$PA" | jq -r --arg b "$OA2" '.allocations[] | select(.orderPublicId==$b) | .id')
expect 200 "$(req POST "/api/v1/warehouse-tasks/$TA1/complete" '{}')" | jq -e '.statusCode=="DONE"' >/dev/null || fail "mover O1 desde la cola"
expect 200 "$(req POST "/api/v1/cross-dock-plans/$PLANA/allocations/$AA2/move" '{}')" >/dev/null
[[ $(reserved "$W6P" "$B_XD" "$PX") == 0 && $(onhand "$W6P" "$B_XD" "$PX") == 0 ]] || fail "staging en cero tras mover"
kardex "productPublicIds=$PX&types=CROSSDOCK&binIds=$B_XD" | jq -e '.total==2 and all(.items[]; .quantity==-4 and .type=="Cruce de muelle")' >/dev/null || fail "dos CROSSDOCK −4 con tipo 'Cruce de muelle' (L582)"
expect 200 "$(req POST "/api/v1/cross-dock-plans/$PLANA/complete" '{}')" | jq -e '.statusCode=="COMPLETED"' >/dev/null || fail "completar el plan del modo (a)"
PLAN=$(expect 200 "$(req POST /api/v1/cross-dock-plans "{\"warehousePublicId\":\"$W6P\",\"stagingZoneId\":$ZSTG}")" | jq -r .id)
RX=$(expect 200 "$(blind "$W6P" "$B_STG" "$PX" 3)"); RXP=$(echo "$RX" | jq -r .header.publicId); RXL=$(echo "$RX" | jq -r '.lines[0].id')
expect 200 "$(req POST "/api/v1/receipts/$RXP/confirm" '{}')" >/dev/null
expect 200 "$(req GET "/api/v1/cross-dock-plans/$PLAN/candidates")" | jq -e --argjson l "$RXL" 'any(.[]; .receiptLineId==$l and .receiptStatusCode=="RECEIVED" and .allocatable==3)' >/dev/null || fail "candidatos: línea confirmada con putaway pendiente"
OX=$(expect 200 "$(req POST /api/v1/orders "$(jq -cn --arg c "$C6P" --arg l "$L6" '{clientPublicId:$c,consigneeLocationPublicId:$l,serviceType:"STANDARD",packages:[{pieces:1,packageType:"BOX"}]}')")" | jq -r .publicId)
AL=$(expect 200 "$(req POST "/api/v1/cross-dock-plans/$PLAN/allocations" "{\"receiptLineId\":$RXL,\"orderPublicId\":\"$OX\",\"quantity\":2}")")
XT=$(echo "$AL" | jq -r '.allocations[0].taskId'); [[ "$XT" =~ ^[0-9]+$ ]] || fail "tarea CROSSDOCK: $AL"
[[ $(reserved "$W6P" "$B_STG" "$PX") == 2 ]] || fail "lo asignado queda reservado"
expect 200 "$(req GET "/api/v1/products?onlyAvailable=true&warehousePublicId=$W6P&search=PX$TS")" | jq -e '.total==1 and .items[0].qtyAvailable==1' >/dev/null || fail "onlyAvailable con disponible 1"
# CROSSDOCK no depende de WMS_LOTSERIAL en el catálogo: con WMS apagado, mover (escribe el ledger) → 403 module_disabled.
ALID=$(echo "$AL" | jq -r '.allocations[0].id')
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":false}')" >/dev/null
M4=$(req POST "/api/v1/cross-dock-plans/$PLAN/allocations/$ALID/move" '{}'); M5=$(req DELETE "/api/v1/cross-dock-plans/$PLAN/allocations/$ALID")
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":true}')" >/dev/null
expect 403 "$M4" | jq -e '.code=="module_disabled"' >/dev/null || fail "mover el cruce con WMS_LOTSERIAL apagado"
expect 403 "$M5" | jq -e '.code=="module_disabled"' >/dev/null || fail "cancelar la asignación con WMS_LOTSERIAL apagado"
[[ $(reserved "$W6P" "$B_STG" "$PX") == 2 ]] || fail "el cruce se movió con WMS_LOTSERIAL apagado"
expect 200 "$(req PUT /api/v1/modules/CROSSDOCK '{"isEnabled":false}')" >/dev/null
expect 403 "$(req POST "/api/v1/warehouse-tasks/$XT/complete" '{}')" | jq -e '.code=="module_disabled"' >/dev/null || fail "tarea CROSSDOCK desde la cola con el módulo apagado"
expect 403 "$(req POST "/api/v1/warehouse-tasks/$XT/start" '{}')" | jq -e '.code=="module_disabled"' >/dev/null || fail "iniciar CROSSDOCK con el módulo apagado"
[[ $(reserved "$W6P" "$B_STG" "$PX") == 2 ]] || fail "la cola movió el cruce con el módulo apagado"
expect 200 "$(req PUT /api/v1/modules/CROSSDOCK '{"isEnabled":true}')" >/dev/null
expect 400 "$(req POST "/api/v1/warehouse-tasks/$XT/complete" '{"quantity":1}')" >/dev/null   # ExactQty
expect 200 "$(req POST "/api/v1/warehouse-tasks/$XT/complete" '{}')" | jq -e '.statusCode=="DONE"' >/dev/null || fail "mover desde la cola"
kardex "productPublicIds=$PX&types=CROSSDOCK&binIds=$B_STG" | jq -e '.total==1 and .items[0].quantity==-2' >/dev/null || fail "CROSSDOCK −2"
[[ $(reserved "$W6P" "$B_STG" "$PX") == 0 ]] || fail "reserva liberada al mover"
# Maestro L328 (disponible/reservado/despachado): la serie asignada a cruce de muelle queda RESERVED (no se recolecta) y al
# moverla sale SHIPPED; la otra sigue AVAILABLE. Recorre LockSerialsAsync (OPENJSON) desde ReserveAsync en SQL Server.
PSX=$(prod "{\"sku\":\"PSX$TS\",\"name\":\"Serie cruce $TS\",\"trackingType\":\"SERIAL\",\"purchaseCost\":1}")
RS=$(expect 200 "$(req POST /api/v1/receipts "$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PSX" --arg a "XS1$TS" --arg b "XS2$TS" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:2,serialNumbers:[$a,$b]}]}')")")
RSP=$(echo "$RS" | jq -r .header.publicId); RSL=$(echo "$RS" | jq -r '.lines[0].id')
expect 200 "$(req POST "/api/v1/receipts/$RSP/confirm" '{}')" >/dev/null
ASX=$(expect 200 "$(req POST "/api/v1/cross-dock-plans/$PLAN/allocations" "{\"receiptLineId\":$RSL,\"orderPublicId\":\"$OX\",\"quantity\":1}")" | jq -r --argjson l "$RSL" '.allocations[] | select(.receiptLineId==$l) | .id')
expect 200 "$(req GET "/api/v1/products/$PSX/serials?status=RESERVED")" | jq -e --arg s "XS1$TS" '[.[] | .serialNumber]==[$s]' >/dev/null || fail "serie asignada a cruce de muelle RESERVED"
expect 200 "$(req GET "/api/v1/products/$PSX/serials?status=AVAILABLE")" | jq -e --arg s "XS2$TS" '[.[] | .serialNumber]==[$s]' >/dev/null || fail "la otra serie sigue AVAILABLE"
expect 409 "$(collectj "$(jq -cn --arg w "$W6P" --arg p "$PSX" --arg s "XS1$TS" '{warehousePublicId:$w,lines:[{productPublicId:$p,serialNumbers:[$s]}]}')")" >/dev/null   # reservada: no se recolecta
expect 200 "$(req POST "/api/v1/cross-dock-plans/$PLAN/allocations/$ASX/move" '{}')" >/dev/null
expect 200 "$(req GET "/api/v1/products/$PSX/serials?status=SHIPPED")" | jq -e --arg s "XS1$TS" '[.[] | .serialNumber]==[$s]' >/dev/null || fail "serie movida SHIPPED"
[[ $(reserved "$W6P" "$B_STG" "$PSX") == 0 && $(onhand "$W6P" "$B_STG" "$PSX") == 1 ]] || fail "saldo de la serie tras mover"
expect 200 "$(req POST "/api/v1/cross-dock-plans/$PLAN/complete" '{}')" | jq -e '.statusCode=="COMPLETED"' >/dev/null || fail "completar el plan"
expect 200 "$(req PUT /api/v1/modules/CROSSDOCK '{"isEnabled":false}')" >/dev/null
ok "agenda: reprogramar sobre otra cita 409, ARRIVED → muelle OCCUPIED, COMPLETED → FREE; candidatos del plan (abierto y confirmado); onlyAvailable de productos y saldos descuenta lo reservado; modo (a): asignar sobre recibo abierto (Exceeds 409), confirmar reparte FIFO (4 y 4 con faltante 2), sin PUTAWAY, 8 reservados que no se recolectan, conteo menor que lo reservado 409 sin efectos, mover desde la cola y el plan deja el staging en cero; apagado 403 module_disabled (planes y citas); con WMS_LOTSERIAL apagado mover y cancelar la asignación 403 sin efecto; citas solapadas en paralelo 200 + 409; asignar sobre recibo confirmado reserva 2; con CROSSDOCK apagado la cola responde 403 module_disabled sin mover; encendido: ExactQty 400, mover → CROSSDOCK −2 y reserva 0; serie asignada RESERVED (no se recolecta, 409) y SHIPPED al moverla; plan COMPLETED; módulo apagado de nuevo"

step "RBAC, módulos, resolvers cerrados y aislamiento (Lote 6)"
expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$W6P" '' "$TREAD6")" >/dev/null
expect 403 "$(adjust "$PN" "$W6P" "$B_PCK" 1 FOUND "$TREAD6")" >/dev/null
# (2026-10-01: el Operador de almacén ya puede ajustar — inventory.adjust —; el 403 lo prueba Solo lectura arriba)
expect 200 "$(collect "$W6P" "$PN" 1 "$TWH6")" >/dev/null   # el Operador recolecta (warehouse.pick)
expect 403 "$(req GET /api/v1/inventory/reconciliation '' "$TREAD6")" >/dev/null
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=5')" | jq -e '.total >= 1' >/dev/null || fail "PERMISSION_DENIED"
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":false}')" >/dev/null
M1=$(req GET /api/v1/warehouses); expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":true}')" >/dev/null
expect 403 "$M1" | jq -e '.code=="module_disabled"' >/dev/null || fail "WMS_LOTSERIAL apagado"
PO4=$(po "[{\"productPublicId\":\"$PN\",\"qtyOrdered\":1,\"unitCost\":2.5}]"); PO4P=$(pid "$PO4")
expect 200 "$(req POST "/api/v1/purchase-orders/$PO4P/send" '{}')" >/dev/null
# Maestro L305 / L762: una PO SENT sin recibos confirmados no tiene faltante.
expect 200 "$(req GET /api/v1/purchase-orders/shortages)" | jq -e --arg p "$PO4P" 'all(.[]; .publicId!=$p)' >/dev/null || fail "PO SENT sin recibos en faltantes"
expect 200 "$(req GET "/api/v1/purchase-orders/$PO4P/shortage-lines")" | jq -e 'length==0' >/dev/null || fail "líneas de faltante de una PO sin recibos"
expect 200 "$(req PUT /api/v1/modules/PURCHASING '{"isEnabled":false}')" >/dev/null
M2=$(req POST /api/v1/receipts "{\"purchaseOrderPublicId\":\"$PO4P\",\"stagingBinId\":$B_STG}"); M3=$(req GET /api/v1/purchase-orders)
expect 200 "$(req PUT /api/v1/modules/PURCHASING '{"isEnabled":true}')" >/dev/null
expect 403 "$M2" | jq -e '.code=="module_disabled"' >/dev/null || fail "recibo contra PO con PURCHASING apagado"
expect 403 "$M3" | jq -e '.code=="module_disabled"' >/dev/null || fail "compras con PURCHASING apagado"
# D6: un recibo OPEN contra PO eliminado cancela su ASN; con solo un recibo OPEN la PO sigue sin faltante.
RO4P=$(expect 200 "$(req POST /api/v1/receipts "{\"purchaseOrderPublicId\":\"$PO4P\",\"stagingBinId\":$B_STG}")" | jq -r .header.publicId)
expect 200 "$(req GET /api/v1/purchase-orders/shortages)" | jq -e --arg p "$PO4P" 'all(.[]; .publicId!=$p)' >/dev/null || fail "PO con solo un recibo OPEN en faltantes"
ASN4=$(expect 200 "$(req GET "/api/v1/asns?warehousePublicId=$W6P")" | jq -r --arg p "$PO4P" '[.[] | select(.purchaseOrderPublicId==$p)][0].id')
[[ "$ASN4" =~ ^[0-9]+$ ]] || fail "ASN del recibo contra PO4"
expect 204 "$(req DELETE "/api/v1/receipts/$RO4P")" >/dev/null
expect 200 "$(req GET "/api/v1/asns/$ASN4")" | jq -e '.statusCode=="CANCELLED"' >/dev/null || fail "el ASN de la PO no quedó CANCELLED al borrar su recibo"
SB=$(expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$W6P&productPublicIds=$PN")" | jq -r '.items[0].id')
for P in "STOCK_BALANCE/$SB" "STOCK_BALANCE/999999" "INVENTORY_TRANSACTION/1" "RECEIPT_LINE/$RXL"; do
  expect 404 "$(req PUT "/api/v1/custom-fields/values/$P" '{"values":{}}')" >/dev/null
done
RE3=$(expect 200 "$(req POST /api/v1/auth/reauth '{"password":"Smoke_Admin_2026!"}' "$T3")"); T3A=$(echo "$RE3" | jq -r .accessToken)
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":true}' "$T3A")" >/dev/null
expect 404 "$(req GET "/api/v1/warehouses/$W6P" '' "$T3A")" >/dev/null
expect 404 "$(req GET "/api/v1/products/$PN" '' "$T3A")" >/dev/null
expect 404 "$(req GET "/api/v1/receipts/$R6P" '' "$T3A")" >/dev/null
expect 404 "$(req GET "/api/v1/pick-batches/$PB7P" '' "$T3A")" >/dev/null
expect 200 "$(req GET /api/v1/warehouses '' "$T3A")" | jq -e --arg w "$W6P" 'all(.[]; .publicId!=$w)' >/dev/null || fail "almacenes ajenos en la lista de T3"
expect 200 "$(req GET /api/v1/inventory/balances '' "$T3A")" | jq -e '.total==0' >/dev/null || fail "saldos ajenos en T3"
ok "Solo lectura lee y no ajusta (403); el Operador recolecta pero no ajusta ni concilia (403); WMS_LOTSERIAL y PURCHASING apagados → 403 module_disabled (restaurados); PO SENT sin recibos (o con solo uno OPEN) fuera de faltantes; recibo OPEN contra PO borrado → ASN CANCELLED; STOCK_BALANCE, INVENTORY_TRANSACTION y RECEIPT_LINE con resolver cerrado (404); T3 con WMS: 404 en almacén, producto, recibo y recolección ajenos y listas vacías"

step "análisis y auditoría (Lote 6): fuentes, vistas del mock, indicadores de valorización y AuditLog"
DS6=$(expect 200 "$(req GET /api/v1/analytics/data-sources)")
echo "$DS6" | jq -e '[.[].key] as $k | all(["WAREHOUSE","PRODUCT","STOCK_BALANCE","INVENTORY_TRANSACTION","RECEIPT","WAREHOUSE_TASK","PICK_BATCH"][]; . as $x | $k | index($x))' >/dev/null || fail "7 fuentes WMS"
PMIN=$(prod "{\"sku\":\"PMIN$TS\",\"name\":\"Bajo mínimo $TS\",\"minQty\":50}")
REPS6=$(expect 200 "$(req GET /api/v1/analytics/reports)")
runview() { local id; id=$(echo "$REPS6" | jq -r --arg n "$1" '[.[] | select(.name==$n and .isSystem==true)][0].id'); [[ "$id" =~ ^[0-9]+$ ]] || fail "vista '$1' no sembrada"; expect 200 "$(req POST "/api/v1/analytics/reports/$id/run" '{}')"; }
runview "Inventario bajo mínimo" | jq -e --arg s "PMIN$TS" 'any(.rows[]; .Sku==$s)' >/dev/null || fail "vista 'Inventario bajo mínimo'"
# L887: 'Movimientos por tipo y producto' agrupa por tipo y SKU con suma de cantidad CON signo y fila de totales.
runview "Movimientos por tipo y producto" | jq -e --arg s "PN$TS" '([.columns[].key] | index("TxnType") != null and index("Sku") != null) and .totals != null and ([.rows[] | select(.Sku==$s)] | any(.sum_Quantity < 0) and any(.sum_Quantity > 0))' >/dev/null || fail "vista 'Movimientos por tipo y producto'"
runview "Ajustes de inventario" | jq -e --arg s "PN$TS" 'any(.rows[]; .Sku==$s and .Quantity==-2)' >/dev/null || fail "vista 'Ajustes de inventario'"
# 'Valor de inventario a costo' = Σ costValue de los saldos (4 decimales), paginando la lista de saldos.
SUMCV=0; SK=0
while :; do PG=$(expect 200 "$(req GET "/api/v1/inventory/balances?skip=$SK&take=200")"); SUMCV=$(jq -n --argjson a "$SUMCV" --argjson b "$(echo "$PG" | jq '[.items[].costValue // 0] | add // 0')" '$a + $b'); SK=$((SK+200)); [[ $SK -lt $(echo "$PG" | jq .total) ]] || break; done
IV=$(indval "Valor de inventario a costo")
jq -n --argjson a "$IV" --argjson b "$SUMCV" '(($a - $b) | fabs) < 0.00005' | grep -q true || fail "'Valor de inventario a costo' ($IV) ≠ Σ costValue ($SUMCV)"
BM=$(indval "Productos bajo mínimo"); [[ $(jq -n --argjson v "$BM" '$v >= 1') == true ]] || fail "'Productos bajo mínimo' = $BM"
for ET in WAREHOUSE PRODUCT PRODUCT_CATEGORY RECEIPT PICK_BATCH PURCHASE_ORDER; do
  expect 200 "$(req GET "/api/v1/audit/changes?entityType=$ET&take=20")" | jq -e '.total >= 1 and ([.items[] | select((.changesJson // "") | ascii_downcase | contains("rowversion"))] | length)==0' >/dev/null || fail "auditoría de $ET"
done
for ET in STOCK_BALANCE INVENTORY_TRANSACTION; do
  expect 200 "$(req GET "/api/v1/audit/changes?entityType=$ET&take=1")" | jq -e '.total==0' >/dev/null || fail "AuditLog de $ET (debe estar vacío)"
done
ok "7 fuentes WMS; vistas 'Inventario bajo mínimo', 'Movimientos por tipo y producto' (tipo + SKU, ISSUE negativo y RECEIPT positivo, totales) y 'Ajustes de inventario'; 'Valor de inventario a costo' = Σ costValue ($SUMCV); 'Productos bajo mínimo' $BM; AuditLog de WAREHOUSE, PRODUCT, PRODUCT_CATEGORY, RECEIPT, PICK_BATCH y PURCHASE_ORDER sin rowVersion, ninguno de STOCK_BALANCE ni INVENTORY_TRANSACTION"

step "sin descuadre (Lote 6): conciliación ledger ↔ saldo después de todo"
RC=$(reconcile)
echo "$RC" | jq -e '.mismatches==[] and .balancesChecked >= 1' >/dev/null || fail "descuadres ledger ↔ saldo: $(echo "$RC" | jq -c .mismatches)"
expect 403 "$(req GET /api/v1/inventory/reconciliation '' "$TREAD6")" >/dev/null
ok "mismatches == [] tras recolecciones concurrentes, reversas, recepciones, putaway, reabasto, transferencia entre almacenes (W6→W7), faltantes y cruce de muelle; sin inventory.adjust 403"


step "baja definitiva de almacén (Lote 6, D26): vacío pasa a INACTIVE (terminal); ya dado de baja → 422"
W9=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"W9$TS\",\"name\":\"Almacén baja definitiva $TS\"}")"); W9P=$(wpid "$W9")
expect 200 "$(req POST "/api/v1/warehouses/$W9P/deactivate" '{}')" | jq -e '.warehouse.statusCode=="INACTIVE" and .warehouse.isActive==false' >/dev/null || fail "almacén vacío se da de baja (INACTIVE terminal)"
expect 422 "$(req POST "/api/v1/warehouses/$W9P/deactivate" '{}')" | jq -e --arg m "El almacén está dado de baja; solo se consulta." "$HASM" >/dev/null || fail "segunda baja de un almacén ya inactivo → 422"
expect 200 "$(req GET "/api/v1/warehouses?includeInactive=true")" | jq -e --arg w "$W9P" 'any(.[]; .publicId==$w and .isActive==false)' >/dev/null || fail "almacén inactivo visible con includeInactive"
expect 200 "$(req GET /api/v1/warehouses)" | jq -e --arg w "$W9P" 'all(.[]; .publicId!=$w)' >/dev/null || fail "almacén inactivo oculto por defecto"
# Lote 7A: la baja aparece en Actividad reciente como WAREHOUSE_DEACTIVATED (obligatorio).
activity "module=WAREHOUSE&onlyMandatory=true" | jq -e --arg w "$W9P" 'any(.[]; .code=="WAREHOUSE_DEACTIVATED" and .entityType=="WAREHOUSE" and .publicId==$w and .mandatory==true)' >/dev/null || fail "WAREHOUSE_DEACTIVATED de W9$TS en Actividad reciente"
ok "almacén vacío W9$TS se da de baja (ACTIVE→INACTIVE terminal, isActive=false); segunda baja 422 'El almacén está dado de baja; solo se consulta.'; oculto por defecto y visible con includeInactive; WAREHOUSE_DEACTIVATED obligatorio en Actividad reciente"

# ============================================================================================================
# Lote 14 — Kárdex (filtros nuevos, resumen, detalle con documento), descuadres Kárdex ↔ saldo (conciliación manual y en
# segundo plano), "Necesita tu atención" y conteo cíclico (página con total, "lo cambiado", confirmar en un paso, estatus
# Pendiente/Concordancia/Diferencia). Usa los datos del Lote 6 (W6/W7, PN, PT, P3 del cliente C6, recibo R6) y un producto
# propio P14 en R-01 (zona RSV). El descuadre REAL exige tocar dbo.StockBalance por SQL: solo con SMOKE_SQL.
# ============================================================================================================
TD14=$(login "$DISPATCH_EMAIL" "$PASS")   # despachador: sin inventory.view ni pulse.attention
ksum() { expect 200 "$(req GET "/api/v1/inventory/transactions/summary?$1")"; }

step "Kárdex (Lote 14): dirección, solo manuales, motivos, dueño, origen y destino; resumen coherente con la lista; detalle con su documento"
# Resumen de PN = la lista (una página): movimientos, entradas, salidas e internos con la misma perspectiva que signedQuantity.
KPN=$(kardex "productPublicIds=$PN&take=200"); NPN=$(echo "$KPN" | jq .total)
[[ $NPN -ge 1 && $NPN -le 200 ]] || fail "PN con $NPN movimientos: el resumen no se puede cotejar con una sola página"
SPN=$(ksum "productPublicIds=$PN")
echo "$KPN" | jq -e --argjson s "$SPN" '[.items[].signedQuantity] as $q
  | $s.movements==.total and $s.inCount==([$q[] | select(. > 0)] | length) and $s.outCount==([$q[] | select(. < 0)] | length)
  and $s.internalCount==([$q[] | select(. == 0)] | length) and ($s.inQty - $s.outQty)==($q | add)' >/dev/null || fail "resumen de PN distinto de la lista: $SPN"
# Dirección: IN = entradas y OUT = salidas (sin distinguir mayúsculas), en la lista y en el resumen; otro valor → 400.
kardex "productPublicIds=$PN&direction=IN&take=200" | jq -e --argjson s "$SPN" '.total==$s.inCount and .total>=1 and all(.items[]; .signedQuantity>0)' >/dev/null || fail "Kárdex direction=IN"
kardex "productPublicIds=$PN&direction=out&take=200" | jq -e --argjson s "$SPN" '.total==$s.outCount and .total>=1 and all(.items[]; .signedQuantity<0)' >/dev/null || fail "Kárdex direction=out"
ksum "productPublicIds=$PN&direction=IN" | jq -e --argjson s "$SPN" '.movements==$s.inCount and .inQty==$s.inQty and .outCount==0' >/dev/null || fail "resumen con direction=IN"
expect 400 "$(req GET '/api/v1/inventory/transactions?direction=X')" | jq -e --arg m "La dirección debe ser IN (entradas) u OUT (salidas)." "$HASM" >/dev/null || fail "direction=X en el Kárdex → 400"
expect 400 "$(req GET '/api/v1/inventory/transactions/summary?direction=X')" | jq -e --arg m "La dirección debe ser IN (entradas) u OUT (salidas)." "$HASM" >/dev/null || fail "direction=X en el resumen → 400"
# Solo manuales = sin documento de referencia (los ajustes +10 FOUND y −2 DAMAGE de PN).
kardex "productPublicIds=$PN&manualOnly=true&take=200" | jq -e --argjson n "$(echo "$KPN" | jq '[.items[] | select(.refEntityCode==null)] | length')" '.total==$n and .total>=2 and all(.items[]; .refEntityCode==null)' >/dev/null || fail "Kárdex manualOnly"
# Motivos (uno o varios); desconocido → 400 con el mensaje de AdjustmentRules.
kardex "productPublicIds=$PN&reasons=DAMAGE" | jq -e '.total==1 and .items[0].reasonCode=="DAMAGE" and .items[0].quantity==-2' >/dev/null || fail "Kárdex reasons=DAMAGE"
kardex "productPublicIds=$PN&reasons=FOUND&reasons=DAMAGE" | jq -e '.total==2 and all(.items[]; .reasonCode=="FOUND" or .reasonCode=="DAMAGE")' >/dev/null || fail "Kárdex reasons=FOUND|DAMAGE"
expect 400 "$(req GET '/api/v1/inventory/transactions?reasons=NOPE')" | jq -e --arg m "Motivo de ajuste desconocido: 'NOPE'." "$HASM" >/dev/null || fail "motivo desconocido en el Kárdex → 400"
# Dueño: el cliente C6 (P3 entró por su aviso de llegada), "Propio" (includeOwn) y un cliente que no existe → 404.
kardex "ownerClientPublicIds=$C6P&take=200" | jq -e --arg s "P3$TS" --arg o "Empaque $TS" '.total>=1 and any(.items[]; .sku==$s) and all(.items[]; .ownerName==$o)' >/dev/null || fail "Kárdex por dueño (cliente C6)"
kardex "ownerClientPublicIds=$C6P&productPublicIds=$PN" | jq -e '.total==0' >/dev/null || fail "Kárdex por dueño C6 trae un producto propio"
kardex "includeOwn=true&productPublicIds=$PN&take=200" | jq -e --argjson n "$NPN" '.total==$n and all(.items[]; .ownerName=="Propio")' >/dev/null || fail "Kárdex includeOwn (Propio)"
ksum "ownerClientPublicIds=$C6P&productPublicIds=$P3" | jq -e --argjson n "$(kardex "productPublicIds=$P3" | jq .total)" '.movements==$n and .movements>=1' >/dev/null || fail "resumen por dueño"
expect 404 "$(req GET "/api/v1/inventory/transactions?ownerClientPublicIds=$(randuuid)")" | jq -e --arg m "Cliente no encontrado." "$HASM" >/dev/null || fail "dueño inexistente → 404"
# Origen y destino: la transferencia W6 → W7 de PT (Lote 6); al revés no hay nada. Resumen con y sin perspectiva de almacén.
kardex "productPublicIds=$PT&fromWarehousePublicIds=$W6P&toWarehousePublicIds=$W7P" | jq -e '.total==1 and .items[0].typeCode=="TRANSFER" and .items[0].quantity==2' >/dev/null || fail "Kárdex origen W6 y destino W7"
kardex "productPublicIds=$PT&fromWarehousePublicIds=$W7P&toWarehousePublicIds=$W6P" | jq -e '.total==0' >/dev/null || fail "Kárdex origen W7 y destino W6"
ksum "productPublicIds=$PT" | jq -e '.movements==2 and .inCount==1 and .inQty==3 and .outCount==0 and .internalCount==1' >/dev/null || fail "resumen de PT sin filtro (transferencia interna)"
ksum "productPublicIds=$PT&warehousePublicIds=$W6P" | jq -e '.movements==2 and .inCount==1 and .inQty==3 and .outCount==1 and .outQty==2 and .internalCount==0' >/dev/null || fail "resumen de PT desde W6 (la transferencia sale)"
# Detalle: el RECEIPT del recibo R6 abre su documento (PublicId) y se incluye en los relacionados; la tarea trae su recibo
# padre; un ajuste manual no tiene documento; inexistente y de otro tenant → 404.
TX6=$(kardex "refEntity=RECEIPT&refId=$R6ID" | jq -r '.items[0].id')
expect 200 "$(req GET "/api/v1/inventory/transactions/$TX6")" | jq -e --arg r "$R6P" --argjson rid "$R6ID" --argjson t "$TX6" '.transaction.id==$t and .document.entityCode=="RECEIPT" and .document.publicId==$r and .document.id==$rid and any(.related[]; .id==$t) and (.relatedTruncated|not) and .ownerName=="Propio"' >/dev/null || fail "detalle del RECEIPT de R6"
TXW=$(kardex "refEntity=WAREHOUSE_TASK&productPublicIds=$PN" | jq -r '.items[0].id')
expect 200 "$(req GET "/api/v1/inventory/transactions/$TXW")" | jq -e --arg a "$R6P" --arg b "$R13P" '.document.entityCode=="WAREHOUSE_TASK" and .document.parent.entityCode=="RECEIPT" and (.document.parent.publicId==$a or .document.parent.publicId==$b)' >/dev/null || fail "detalle de un TRANSFER de tarea con su recibo padre"
TXM=$(kardex "productPublicIds=$PN&reasons=DAMAGE" | jq -r '.items[0].id')
expect 200 "$(req GET "/api/v1/inventory/transactions/$TXM")" | jq -e --argjson t "$TXM" '.document==null and any(.related[]; .id==$t) and .transaction.reasonCode=="DAMAGE"' >/dev/null || fail "detalle de un ajuste manual (sin documento)"
expect 404 "$(req GET /api/v1/inventory/transactions/999999999999)" | jq -e --arg m "Movimiento no encontrado." "$HASM" >/dev/null || fail "movimiento inexistente → 404"
expect 404 "$(req GET "/api/v1/inventory/transactions/$TX6" '' "$T3A")" | jq -e --arg m "Movimiento no encontrado." "$HASM" >/dev/null || fail "movimiento de otro tenant → 404"
# Dueños para el filtro ("Propio" primero) y búsqueda de posiciones entre almacenes.
expect 200 "$(req GET /api/v1/inventory/owners)" | jq -e --arg c "$C6P" --arg n "Empaque $TS" '.[0].isOwn and .[0].name=="Propio" and .[0].clientPublicId==null and any(.[]; .clientPublicId==$c and .name==$n and (.isOwn|not))' >/dev/null || fail "dueños del inventario"
expect 200 "$(req GET "/api/v1/warehouses/bins/search?search=P-01&warehousePublicIds=$W6P&warehousePublicIds=$W7P")" | jq -e --arg a "W6$TS" --arg b "W7$TS" '([.[].warehouseCode] | unique)==([$a,$b] | sort) and all(.[]; (.code | contains("P-01")) and .isActive)' >/dev/null || fail "búsqueda de posiciones en W6 y W7"
expect 403 "$(req GET '/api/v1/warehouses/bins/search?search=P-01' '' "$TD14")" >/dev/null   # sin inventory.view
ok "resumen de PN = lista ($NPN movimientos: entradas, salidas, internos y neto); direction IN/out en lista y resumen, X → 400; manualOnly; reasons (uno y varios, desconocido 400); dueño C6, Propio e inexistente 404; origen W6 → destino W7 (y al revés vacío); resumen de PT con y sin perspectiva de almacén; detalle del RECEIPT con documento y relacionados, de una tarea con su recibo padre, de un ajuste manual sin documento; 404 'Movimiento no encontrado.' (inexistente y de otro tenant); dueños con Propio; posiciones de W6 y W7 (sin inventory.view 403)"

step "descuadres y conciliación (Lote 14): revisión automática, ejecutar, lista, 400/403/404 y descuadre real (con SMOKE_SQL)"
recstatus() { expect 200 "$(req GET /api/v1/inventory/reconciliation/status)"; }
P14=$(prod "{\"sku\":\"P14$TS\",\"name\":\"Descuadre 14 $TS\",\"purchaseCost\":1}")
ST14=$(recstatus); PROC14=$(echo "$ST14" | jq .processed)
echo "$ST14" | jq -e '(.enabled|type)=="boolean" and (.consuming|type)=="boolean" and (.pending|type)=="number" and (.processed|type)=="number" and (.dropped|type)=="number"' >/dev/null || fail "estado de la revisión automática: $ST14"
expect 200 "$(adjust "$P14" "$W6P" "$B_RSV" 2 FOUND)" >/dev/null
WORKER14=$(echo "$ST14" | jq -r '.enabled and .consuming')
if [[ "$WORKER14" == true ]]; then   # el ajuste encola la revisión del producto: se espera a que la cola quede al día (≤ 30 s)
  for i in $(seq 1 60); do STN=$(recstatus); [[ $(echo "$STN" | jq --argjson p "$PROC14" '.pending==0 and .processed>$p') == true ]] && break; sleep 0.5; done
  echo "$STN" | jq -e --argjson p "$PROC14" '.pending==0 and .processed>$p' >/dev/null || fail "la revisión automática no procesó el ajuste de P14: $STN (antes processed=$PROC14)"
fi
expect 403 "$(req GET /api/v1/inventory/reconciliation/status '' "$TREAD6")" >/dev/null   # sin inventory.adjust
# Ejecutar conciliación (MANUAL): por productos y de todo el tenant, sin descuadres; más de 200 productos → 400; sin inventory.adjust → 403.
expect 200 "$(req POST /api/v1/inventory/reconciliation/run "$(jq -cn --arg a "$PN" --arg b "$PT" --arg c "$P14" '{productPublicIds:[$a,$b,$c]}')")" | jq -e '.productsChecked==3 and .balancesChecked>=3 and .opened==0 and .stillOpen==0 and .mismatches==[]' >/dev/null || fail "conciliación manual de PN, PT y P14"
expect 200 "$(req POST /api/v1/inventory/reconciliation/run '{}')" | jq -e '.productsChecked>=3 and .opened==0 and .mismatches==[]' >/dev/null || fail "conciliación manual de todo el tenant"
expect 400 "$(req POST /api/v1/inventory/reconciliation/run "$(jq -cn '{productPublicIds:[range(201) | "00000000-0000-4000-8000-" + (("00000000000" + tostring) | .[-12:])]}')")" | jq -e --arg m "La conciliación manual admite como máximo 200 productos a la vez." "$HASM" >/dev/null || fail "conciliación de 201 productos → 400"
expect 403 "$(req POST /api/v1/inventory/reconciliation/run '{}' "$TREAD6")" >/dev/null
# Lista: la ve quien ve inventario (inventory.view); el despachador no; tipo desconocido → 400.
expect 200 "$(req GET "/api/v1/inventory/discrepancies?productPublicIds=$P14")" | jq -e '.total==0 and .openCount==0 and .items==[]' >/dev/null || fail "P14 sin descuadres"
expect 200 "$(req GET '/api/v1/inventory/discrepancies?status=OPEN&take=1' '' "$TREAD6")" | jq -e '(.total|type)=="number" and (.openCount|type)=="number" and (.items|length)<=1' >/dev/null || fail "lista de descuadres con inventory.view"
expect 403 "$(req GET /api/v1/inventory/discrepancies '' "$TD14")" >/dev/null
expect 400 "$(req GET '/api/v1/inventory/discrepancies?kinds=FOO')" | jq -e --arg m "Tipo de descuadre desconocido: 'FOO'." "$HASM" >/dev/null || fail "tipo de descuadre desconocido → 400"
# Resolver: la forma se valida primero (400), luego la existencia (404); sin inventory.adjust → 403.
NOD=$(randuuid)
expect 404 "$(req GET "/api/v1/inventory/discrepancies/$NOD")" | jq -e --arg m "Descuadre no encontrado." "$HASM" >/dev/null || fail "descuadre inexistente → 404"
expect 404 "$(req POST "/api/v1/inventory/discrepancies/$NOD/resolve" '{"action":"DISMISS","notes":"humo"}')" | jq -e --arg m "Descuadre no encontrado." "$HASM" >/dev/null || fail "resolver un descuadre inexistente → 404"
expect 400 "$(req POST "/api/v1/inventory/discrepancies/$NOD/resolve" '{}')" | jq -e --arg m "Indique la acción: REBUILD_BALANCE (corregir el saldo) o DISMISS (descartar)." "$HASM" >/dev/null || fail "resolver sin acción → 400"
expect 400 "$(req POST "/api/v1/inventory/discrepancies/$NOD/resolve" '{"action":"DISMISS"}')" | jq -e --arg m "Escriba una nota que explique por qué se descarta el descuadre." "$HASM" >/dev/null || fail "descartar sin nota → 400"
expect 400 "$(req POST "/api/v1/inventory/discrepancies/$NOD/resolve" "$(jq -cn --arg n "$(printf 'n%.0s' {1..501})" '{action:"REBUILD_BALANCE",notes:$n}')")" | jq -e --arg m "La nota admite como máximo 500 caracteres." "$HASM" >/dev/null || fail "nota de 501 → 400"
expect 403 "$(req POST "/api/v1/inventory/discrepancies/$NOD/resolve" '{"action":"DISMISS","notes":"humo"}' "$TREAD6")" >/dev/null
REAL14="omitido (sin SMOKE_SQL: provocarlo exige tocar dbo.StockBalance por SQL)"
if [[ -n "${SMOKE_SQL:-}" ]]; then
  # Descuadre real: el saldo de P14 en R-01 sube 1 por SQL (sin movimiento) y un ajuste +1 dispara la revisión: Kárdex 3, saldo 4.
  SB14=$(expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$W6P&binIds=$B_RSV&productPublicIds=$P14")" | jq -r '.items[0].id')
  [[ "$SB14" =~ ^[0-9]+$ ]] || fail "saldo de P14 en R-01"
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; UPDATE dbo.StockBalance SET QtyOnHand = QtyOnHand + 1 WHERE StockBalanceId = $SB14;" >/dev/null
  expect 200 "$(adjust "$P14" "$W6P" "$B_RSV" 1 FOUND)" >/dev/null
  if [[ "$WORKER14" == true ]]; then   # EVENT: lo abre la revisión automática (debounce + espera mínima entre revisiones; ≤ 30 s)
    TRG14=EVENT
    for i in $(seq 1 60); do D14=$(expect 200 "$(req GET "/api/v1/inventory/discrepancies?status=OPEN&productPublicIds=$P14")"); [[ $(echo "$D14" | jq .total) -ge 1 ]] && break; sleep 0.5; done
  else                                 # sin revisión en segundo plano: lo abre la conciliación manual
    TRG14=MANUAL
    expect 200 "$(req POST /api/v1/inventory/reconciliation/run "$(jq -cn --arg p "$P14" '{productPublicIds:[$p]}')")" | jq -e '.opened==1' >/dev/null || fail "la conciliación manual no abrió el descuadre de P14"
    D14=$(expect 200 "$(req GET "/api/v1/inventory/discrepancies?status=OPEN&productPublicIds=$P14")")
  fi
  echo "$D14" | jq -e --arg s "P14$TS" --arg t "$TRG14" '.total==1 and .openCount==1 and (.items[0] | .kindCode=="BALANCE" and .sku==$s and .binCode=="R-01" and .ledgerQty==3 and .balanceQty==4 and (.balanceQty - .ledgerQty)==1 and .difference==1 and .triggerCode==$t and .statusCode=="OPEN" and .closedAtUtc==null)' >/dev/null || fail "descuadre abierto de P14 ($TRG14): $(echo "$D14" | jq -c '[.items[] | {kindCode,binCode,ledgerQty,balanceQty,difference,triggerCode,statusCode}]')"
  DP14=$(echo "$D14" | jq -r '.items[0].publicId')
  expect 200 "$(req GET "/api/v1/inventory/discrepancies/$DP14" '' "$TREAD6")" | jq -e --arg d "$DP14" '.discrepancy.publicId==$d and any(.history[]; .toCode=="OPEN") and (.recentMovements|length)>=2' >/dev/null || fail "ficha del descuadre con inventory.view"
  # "Necesita tu atención": el descuadre suma en su grupo y, si está entre los 5 más antiguos, trae su "Revisar".
  expect 200 "$(req GET /api/v1/analytics/attention)" | jq -e --arg d "$DP14" '.total>=1 and any(.groups[]; .code=="INVENTORY_DISCREPANCY" and .total>=1) and (any(.items[]; .code=="INVENTORY_DISCREPANCY" and .params.publicId==$d and .query.discrepancy==$d) or .total>5)' >/dev/null || fail "el descuadre de P14 no aparece en 'Necesita tu atención'"
  expect 403 "$(req POST "/api/v1/inventory/discrepancies/$DP14/resolve" '{"action":"REBUILD_BALANCE"}' "$TREAD6")" >/dev/null
  expect 400 "$(req POST "/api/v1/inventory/discrepancies/$DP14/resolve" '{"action":"DISMISS","notes":"  "}')" | jq -e --arg m "Escriba una nota que explique por qué se descarta el descuadre." "$HASM" >/dev/null || fail "descartar el descuadre real sin nota → 400"
  RV14=$(expect 200 "$(req GET "/api/v1/inventory/discrepancies/$DP14")" | jq -r .discrepancy.rowVersion)
  expect 200 "$(req POST "/api/v1/inventory/discrepancies/$DP14/resolve" "$(jq -cn --arg v "$RV14" '{action:"REBUILD_BALANCE",notes:"Humo Lote 14",rowVersion:$v}')")" | jq -e '.discrepancy.statusCode=="RESOLVED" and .discrepancy.correctedFromQty==4 and .discrepancy.correctedToQty==3 and .discrepancy.correctedToQty==.discrepancy.ledgerQty and .discrepancy.closedAtUtc!=null and any(.history[]; .toCode=="RESOLVED")' >/dev/null || fail "corregir el saldo según el Kárdex"
  [[ $(onhand "$W6P" "$B_RSV" "$P14") == 3 ]] || fail "el saldo de P14 no tomó lo que da el Kárdex (3)"
  expect 200 "$(req GET "/api/v1/inventory/reconciliation?productPublicId=$P14")" | jq -e '.mismatches==[]' >/dev/null || fail "P14 sigue descuadrado tras corregir"
  RV14=$(expect 200 "$(req GET "/api/v1/inventory/discrepancies/$DP14")" | jq -r .discrepancy.rowVersion)
  expect 422 "$(req POST "/api/v1/inventory/discrepancies/$DP14/resolve" "$(jq -cn --arg v "$RV14" '{action:"DISMISS",notes:"otra vez",rowVersion:$v}')")" | jq -e --arg m "El descuadre ya está cerrado; solo se consulta." "$HASM" >/dev/null || fail "resolver un descuadre cerrado → 422"
  expect 200 "$(req GET "/api/v1/inventory/discrepancies?status=RESOLVED&productPublicIds=$P14")" | jq -e '.total==1 and .openCount==0' >/dev/null || fail "P14: un descuadre RESOLVED y ninguno abierto"
  REAL14="saldo +1 por SQL → OPEN ($TRG14, Kárdex 3 / saldo 4 / diferencia 1) en la lista y en 'Necesita tu atención'; descartar sin nota 400; sin inventory.adjust 403; REBUILD_BALANCE → RESOLVED 4 → 3 sin descuadre; resolver otra vez 422"
fi
ok "revisión automática ($([[ "$WORKER14" == true ]] && echo 'al día tras el ajuste, processed subió' || echo 'apagada')); conciliación manual por productos y de todo el tenant sin descuadres (201 productos 400, sin inventory.adjust 403); lista con inventory.view (despachador 403, tipo desconocido 400); 404 'Descuadre no encontrado.'; resolver sin acción / sin nota / nota de 501 → 400 con sus mensajes; sin inventory.adjust 403; descuadre real: $REAL14"

step "Necesita tu atención (Lote 14): total, 5 más antiguos, grupos y permiso pulse.attention"
AT14=$(expect 200 "$(req GET /api/v1/analytics/attention)")
echo "$AT14" | jq -e '(.total|type)=="number" and .total>=0 and (.items|type)=="array" and (.items|length)<=5 and (.items|length)<=.total and (.groups|type)=="array" and ([.groups[].total] | add // 0)==.total' >/dev/null || fail "'Necesita tu atención': $(echo "$AT14" | jq -c '{total, items: (.items|length), groups}')"
expect 200 "$(req GET /api/v1/analytics/attention '' "$TREAD6")" | jq -e '(.total|type)=="number"' >/dev/null || fail "Solo lectura (con pulse.attention) no ve 'Necesita tu atención'"
expect 403 "$(req GET /api/v1/analytics/attention '' "$TD14")" >/dev/null   # el despachador no tiene pulse.attention
ok "total numérico ($(echo "$AT14" | jq .total)), a lo sumo 5 ítems, grupos que suman el total; Solo lectura 200 y despachador sin pulse.attention 403 (el panel ATTENTION del Pulso del admin lo cubre el paso del Lote F8a)"

step "conteo cíclico (Lote 14): página con total, 'lo cambiado' (vista previa, alta, repetido y > 31 días), confirmar en un paso y estatus del catálogo"
expect 200 "$(req GET /api/v1/status/CycleCountStatus)" | jq -e 'any(.[]; .code=="OPEN" and .label=="Pendiente" and .isInitial) and any(.[]; .code=="RECONCILED" and .label=="Concordancia") and any(.[]; .code=="RECONCILED_VARIANCE" and .label=="Diferencia" and .stageKind=="TERMINAL")' >/dev/null || fail "estatus del conteo Pendiente / Concordancia / Diferencia"
expect 200 "$(req GET /api/v1/catalogs/CycleCountOrigin)" | jq -e 'any(.[]; .code=="MANUAL") and any(.[]; .code=="CHANGES")' >/dev/null || fail "catálogo CycleCountOrigin (MANUAL y CHANGES)"
# Ventana explícita que cubre todo el humo (W6 se creó en esta corrida): así la repetición usa la misma ventana.
F14=$(date -u -d '12 hours ago' +%FT%TZ); F40=$(date -u -d '40 days ago' +%FT%TZ)
FCB=$(jq -cn --arg w "$W6P" --arg f "$F14" --argjson z "$ZRSV" '{warehousePublicId:$w,fromUtc:$f,zoneIds:[$z]}')
expect 200 "$(req GET "/api/v1/cycle-counts/changes-preview?warehousePublicId=$W6P&fromUtc=$F14")" | jq -e --arg w "$W6P" '.warehousePublicId==$w and .movements>=1 and .positions>=1 and .problem==null and .maxPositions==200' >/dev/null || fail "vista previa de lo cambiado en W6"
expect 200 "$(req GET "/api/v1/cycle-counts/changes-preview?warehousePublicId=$W6P&fromUtc=$F14&zoneIds=$ZRSV")" | jq -e '.positions==1 and .positionsWithOpenCount==0 and .problem==null' >/dev/null || fail "vista previa de lo cambiado en la zona RSV"
expect 403 "$(req GET "/api/v1/cycle-counts/changes-preview?warehousePublicId=$W6P" '' "$TREAD6")" >/dev/null   # sin warehouse.count
FC14=$(expect 200 "$(req POST /api/v1/cycle-counts/from-changes "$FCB")")
echo "$FC14" | jq -e '.window.positions==1 and (.counts | length)==1 and (.counts[0] | .originCode=="CHANGES" and .statusCode=="OPEN" and .binCount==1 and .binCode=="R-01" and .zoneCode=="RSV" and .taskId!=null and .changesFromUtc!=null and .changesToUtc!=null)' >/dev/null || fail "conteo de lo cambiado en R-01: $(echo "$FC14" | jq -c '[.counts[] | {originCode,statusCode,binCount,binCode,zoneCode,taskId}]')"
CC14=$(echo "$FC14" | jq -r '.counts[0].id'); CT14=$(echo "$FC14" | jq -r '.counts[0].taskId')
tasksof "$W6P" COUNT | jq -e --argjson c "$CC14" --argjson t "$CT14" 'any(.items[]; .id==$t and .refEntityCode=="CYCLE_COUNT" and .refId==$c)' >/dev/null || fail "tarea COUNT del conteo de lo cambiado"
M1CH="Las 1 posiciones con cambios ya tienen un conteo pendiente."
expect 400 "$(req POST /api/v1/cycle-counts/from-changes "$FCB")" | jq -e --arg m "$M1CH" "$HASM" >/dev/null || fail "lo cambiado repetido → 400"
expect 200 "$(req GET "/api/v1/cycle-counts/changes-preview?warehousePublicId=$W6P&fromUtc=$F14&zoneIds=$ZRSV")" | jq -e --arg m "$M1CH" '.positions==0 and .positionsWithOpenCount==1 and .problem==$m' >/dev/null || fail "vista previa con la posición ya en conteo"
expect 400 "$(req POST /api/v1/cycle-counts/from-changes "$(jq -cn --arg w "$W6P" --arg f "$F40" '{warehousePublicId:$w,fromUtc:$f}')")" | jq -e --arg m 'El rango de "lo cambiado" admite como máximo 31 días.' "$HASM" >/dev/null || fail "lo cambiado de 40 días → 400"
expect 400 "$(req GET "/api/v1/cycle-counts/changes-preview?warehousePublicId=$W6P&fromUtc=$F40")" | jq -e --arg m 'El rango de "lo cambiado" admite como máximo 31 días.' "$HASM" >/dev/null || fail "vista previa de 40 días → 400"
# Página con total: por origen, zona y estatus; a ciegas para Solo lectura.
expect 200 "$(req GET "/api/v1/cycle-counts/page?warehousePublicIds=$W6P&origins=CHANGES&take=1")" | jq -e --argjson c "$CC14" '.total==1 and .take==1 and (.items|length)==1 and .items[0].id==$c and .items[0].origin!=null' >/dev/null || fail "página de conteos por origen CHANGES"
expect 200 "$(req GET "/api/v1/cycle-counts/page?warehousePublicIds=$W6P&origins=MANUAL&take=200")" | jq -e --argjson a "$CC7ID" --argjson c "$CC14" 'any(.items[]; .id==$a and .originCode=="MANUAL") and all(.items[]; .id!=$c)' >/dev/null || fail "página de conteos por origen MANUAL"
expect 200 "$(req GET "/api/v1/cycle-counts/page?warehousePublicIds=$W6P&zoneIds=$ZRSV&status=OPEN&status=COUNTED&take=1")" | jq -e --argjson c "$CC14" '.total==1 and .items[0].id==$c' >/dev/null || fail "página de conteos por zona y estatus"
expect 200 "$(req GET "/api/v1/cycle-counts/page?warehousePublicIds=$W6P&take=200" '' "$TREAD6")" | jq -e '.total>=2 and (.items|length)==.total and all(.items[]; .varianceLines==null and .netVariance==null)' >/dev/null || fail "página de conteos a ciegas para Solo lectura"
# Confirmar en un paso desde Pendiente: todo igual salvo P14 (+1) → Diferencia con un solo ajuste COUNT_VARIANCE.
CD14=$(expect 200 "$(req GET "/api/v1/cycle-counts/$CC14")")
echo "$CD14" | jq -e --arg s "P14$TS" --argjson q "$(onhand "$W6P" "$B_RSV" "$P14")" 'any(.lines[]; .sku==$s and .systemQty==$q) and all(.lines[]; .trackingTypeCode!="SERIAL")' >/dev/null || fail "líneas del conteo de R-01"
CAP14=$(echo "$CD14" | jq -c --arg s "P14$TS" '{lines:[.lines[] | {lineId:.id, countedQty:(if .sku==$s then .systemQty + 1 else .systemQty end)}]}')
expect 200 "$(req PUT "/api/v1/cycle-counts/$CC14/lines" "$CAP14")" | jq -e '.count.statusCode=="OPEN" and .count.countedLines==.count.lineCount' >/dev/null || fail "captura del conteo de lo cambiado"
REC14=$(expect 200 "$(req POST "/api/v1/cycle-counts/$CC14/reconcile" '{}')")
echo "$REC14" | jq -e --arg s "P14$TS" '.count.statusCode=="RECONCILED_VARIANCE" and .count.status=="Diferencia" and any(.lines[]; .sku==$s and .adjustedQty==1 and .adjustmentTxnId!=null) and ([.lines[] | select((.adjustedQty // 0) != 0)] | length)==1' >/dev/null || fail "confirmar en un paso con diferencia: $(echo "$REC14" | jq -c '{s:.count.statusCode, l:[.lines[] | {sku,systemQty,countedQty,adjustedQty}]}')"
expect 200 "$(req GET "/api/v1/status/history/CYCLE_COUNT/$CC14")" | jq -e '.[-1].toCode=="RECONCILED_VARIANCE" and all(.[]; .toCode!="COUNTED")' >/dev/null || fail "historial del conteo: de Pendiente a Diferencia sin pasar por Contado"
KCC14=$(kardex "refEntity=CYCLE_COUNT&refId=$CC14")
echo "$KCC14" | jq -e '.total==1 and .items[0].quantity==1 and .items[0].reasonCode=="COUNT_VARIANCE"' >/dev/null || fail "ajuste COUNT_VARIANCE +1 del conteo de lo cambiado: $(echo "$KCC14" | jq -c '[.items[] | {quantity,reasonCode}]')"
TXC=$(echo "$KCC14" | jq -r '.items[0].id')
expect 200 "$(req GET "/api/v1/inventory/transactions/$TXC")" | jq -e --argjson c "$CC14" '.document.entityCode=="CYCLE_COUNT" and .document.id==$c and .document.statusCode=="RECONCILED_VARIANCE"' >/dev/null || fail "detalle del ajuste del conteo con su documento"
expect 200 "$(req GET "/api/v1/warehouse-tasks?warehousePublicId=$W6P&types=COUNT&includeClosed=true&take=200")" | jq -e --argjson t "$CT14" 'any(.items[]; .id==$t and .statusCode=="DONE")' >/dev/null || fail "tarea COUNT del conteo de lo cambiado DONE"
# Sin diferencia → Concordancia (conteo manual de P-02, en un paso desde Pendiente).
CCM14=$(expect 200 "$(req POST /api/v1/cycle-counts "{\"warehousePublicId\":\"$W6P\",\"binIds\":[$B_PCK2]}")"); CCM14ID=$(echo "$CCM14" | jq -r .count.id)
echo "$CCM14" | jq -e '.count.originCode=="MANUAL" and (.lines|length)>=1' >/dev/null || fail "conteo manual de P-02"
expect 200 "$(req PUT "/api/v1/cycle-counts/$CCM14ID/lines" "$(echo "$CCM14" | jq -c '{lines:[.lines[] | {lineId:.id, countedQty:.systemQty}]}')")" >/dev/null
expect 200 "$(req POST "/api/v1/cycle-counts/$CCM14ID/reconcile" '{}')" | jq -e '.count.statusCode=="RECONCILED" and .count.status=="Concordancia" and all(.lines[]; (.adjustedQty // 0)==0)' >/dev/null || fail "confirmar en un paso sin diferencia → Concordancia"
kardex "refEntity=CYCLE_COUNT&refId=$CCM14ID" | jq -e '.total==0' >/dev/null || fail "Concordancia sin movimientos"
expect 200 "$(req GET "/api/v1/inventory/reconciliation?productPublicId=$P14")" | jq -e '.mismatches==[]' >/dev/null || fail "P14 descuadrado tras el conteo"
ok "estatus Pendiente/Concordancia/Diferencia (terminal) y origen MANUAL/CHANGES; vista previa de W6 y de RSV (sin warehouse.count 403); lo cambiado en RSV → 1 conteo CHANGES de R-01 con tarea COUNT; repetido 400 '$M1CH' (y la vista previa lo anticipa); > 31 días 400 (alta y vista previa); página por origen, zona y estatus con total, a ciegas para Solo lectura; confirmar en un paso desde Pendiente: +1 en P14 → Diferencia (COUNT_VARIANCE +1 con su documento, sin pasar por Contado, tarea DONE) y P-02 sin diferencia → Concordancia sin movimientos"

# ============================================================================================================
# Lote 15 — Pulso del día: franja "Almacén hoy" (GET /inventory/pulse/days: 7 días LOCALES de Puerto Rico con hoy; recibido,
# salida y conteos con diferencia por día; bajo mínimo de ahora), panel WAREHOUSE_DAY y los 2 gráficos de almacén DE LA
# COMPAÑÍA (dona de valor por categoría con "Otras" y barras de movimientos en unidades positivas) + 'Descuadres pendientes'.
# Usa W6/W7 y P14 del Lote 14 (R-01). Mover un movimiento de día exige tocar dbo.InventoryTransaction: solo con SMOKE_SQL.
# ============================================================================================================
step "franja 'Almacén hoy' (Lote 15): 7 días locales, recibido/salida/conteos de hoy por almacén, 400/403, panel del Pulso y gráficos de la compañía"
pday() { expect 200 "$(req GET "/api/v1/inventory/pulse/days?$1" '' "${2:-$TOKEN}")"; }
d15() { jq -n --argjson a "$1" --argjson b "$2" "\$a.$3 - \$b.$3"; }   # diferencia de un campo entre dos respuestas
TODAYPR=$(date -u -d '4 hours ago' +%F)   # Puerto Rico es UTC−4 todo el año (sin horario de verano)
YESTPR=$(date -u -d '28 hours ago' +%F)
PD15=$(pday "")
echo "$PD15" | jq -e --arg t "$TODAYPR" '.timeZone=="America/Puerto_Rico" and .today==$t and (.days|length)==7 and .days[-1].date==$t
  and ([.days[].date | . + "T00:00:00Z" | fromdateiso8601] as $d | all(range(1;7); $d[.] - $d[. - 1] == 86400))
  and all(.days[]; .receivedMovements>=0 and .outboundMovements>=0 and .countsWithVariance>=0)
  and .receivedToday==.days[-1].receivedUnits and .outboundToday==.days[-1].outboundUnits and .countsWithVarianceToday==.days[-1].countsWithVariance
  and .countsWithVarianceTotal==([.days[].countsWithVariance]|add) and .belowMinProducts>=0
  and .countsAlert==(.countsWithVarianceToday>0) and .belowMinAlert==(.belowMinProducts>0)' >/dev/null || fail "franja de todos los almacenes: $(echo "$PD15" | jq -c '{timeZone,today,days:[.days[].date],countsWithVarianceToday,belowMinProducts}')"
for d in 0 15; do
  expect 400 "$(req GET "/api/v1/inventory/pulse/days?days=$d")" | jq -e --arg m "Los días deben estar entre 1 y 14." "$HASM" >/dev/null || fail "days=$d → 400"
done
pday "days=14" | jq -e --arg t "$TODAYPR" '(.days|length)==14 and .days[-1].date==$t' >/dev/null || fail "days=14"
# Recibo a ciegas de 3 unidades de P14 en W6: lo recibido de hoy sube exactamente 3 (W7 no cambia).
B15=$(pday "warehousePublicIds=$W6P"); B15W7=$(pday "warehousePublicIds=$W7P")
R15=$(expect 200 "$(blind "$W6P" "$B_STG" "$P14" 3)"); R15ID=$(echo "$R15" | jq -r .header.id)
expect 200 "$(confirmr "$R15")" >/dev/null
A15=$(pday "warehousePublicIds=$W6P")
[[ $(d15 "$A15" "$B15" receivedToday) == 3 && $(d15 "$A15" "$B15" receivedTotal) == 3 ]] || fail "recibo de 3 en W6: hoy $(d15 "$A15" "$B15" receivedToday)"
# Recolección de 1: la salida de hoy sube 1; eliminarla la devuelve al punto de partida (neto).
PB15=$(expect 200 "$(collect "$W6P" "$P14" 1)")
C15=$(pday "warehousePublicIds=$W6P")
[[ $(d15 "$C15" "$A15" outboundToday) == 1 ]] || fail "recolección de 1 en W6: salida de hoy $(d15 "$C15" "$A15" outboundToday)"
expect 204 "$(req DELETE "/api/v1/pick-batches/$(echo "$PB15" | jq -r .publicId)" '{}')" >/dev/null
D15=$(pday "warehousePublicIds=$W6P")
[[ $(d15 "$D15" "$A15" outboundToday) == 0 ]] || fail "eliminar la recolección no devolvió la salida al punto de partida: $(d15 "$D15" "$A15" outboundToday)"
# Conteo de R-01 con +1 en P14 → Diferencia: los conteos con diferencia de hoy suben 1 en W6 (y se pinta en naranja).
CC15=$(expect 200 "$(req POST /api/v1/cycle-counts "{\"warehousePublicId\":\"$W6P\",\"binIds\":[$B_RSV]}")"); CC15ID=$(echo "$CC15" | jq -r .count.id)
expect 200 "$(req PUT "/api/v1/cycle-counts/$CC15ID/lines" "$(echo "$CC15" | jq -c --arg s "P14$TS" '{lines:[.lines[] | {lineId:.id, countedQty:(if .sku==$s then .systemQty + 1 else .systemQty end)}]}')")" >/dev/null
expect 200 "$(req POST "/api/v1/cycle-counts/$CC15ID/reconcile" '{}')" | jq -e '.count.statusCode=="RECONCILED_VARIANCE"' >/dev/null || fail "conteo de R-01 con +1 no quedó en Diferencia"
E15=$(pday "warehousePublicIds=$W6P")
[[ $(d15 "$E15" "$D15" countsWithVarianceToday) == 1 ]] || fail "conteo con diferencia de hoy en W6: $(d15 "$E15" "$D15" countsWithVarianceToday)"
echo "$E15" | jq -e '.countsAlert' >/dev/null || fail "franja sin el tono naranja de conteos con diferencia"
pday "warehousePublicIds=$W7P" | jq -e --argjson b "$B15W7" '.receivedTotal==$b.receivedTotal and .outboundTotal==$b.outboundTotal and .countsWithVarianceTotal==$b.countsWithVarianceTotal' >/dev/null || fail "W7 cambió con los movimientos de W6"
# Almacén desconocido → todo en cero (nunca "todos"); sin inventory.view → 403.
pday "warehousePublicIds=$(randuuid)" | jq -e '(.days|length)==7 and all(.days[]; .receivedUnits==0 and .outboundUnits==0 and .countsWithVariance==0) and .belowMinProducts==0 and (.countsAlert|not)' >/dev/null || fail "almacén desconocido no da cero"
expect 403 "$(req GET /api/v1/inventory/pulse/days '' "$TD14")" >/dev/null
# Panel WAREHOUSE_DAY: primero (−10) para el admin; el despachador (sin pulse.warehouse ni inventory.view) no lo tiene.
expect 204 "$(req DELETE /api/v1/analytics/pulse/layout/mine)" >/dev/null
expect 200 "$(req GET /api/v1/analytics/pulse)" | jq -e '.panels[0].key=="WAREHOUSE_DAY" and .panels[0].sortOrder==-10 and .panels[0].isVisible' >/dev/null || fail "WAREHOUSE_DAY no es el primer panel del admin"
expect 200 "$(req GET /api/v1/analytics/pulse '' "$TD14")" | jq -e 'all(.panels[]; .key!="WAREHOUSE_DAY")' >/dev/null || fail "el despachador ve la franja"
# Los 2 gráficos de almacén: de la compañía (sin dueño), editables por el admin; dona de valor y barras en unidades positivas.
CH15=$(expect 200 "$(req GET /api/v1/analytics/charts)")
VAL15=$(echo "$CH15" | jq -c '[.[] | select(.name=="Valor de inventario por categoría")][0]')
MOV15=$(echo "$CH15" | jq -c '[.[] | select(.name=="Movimientos de inventario por tipo")][0]')
echo "$VAL15" | jq -e '.isSystem==false and .ownerUserId==null and .canEdit and .chartType=="DONUT" and .field=="CostValue" and .aggregateFn=="SUM"' >/dev/null || fail "gráfico de valor: $VAL15"
echo "$MOV15" | jq -e '.isSystem==false and .ownerUserId==null and .canEdit and .chartType=="BAR" and .field=="Units" and .aggregateFn=="SUM"' >/dev/null || fail "gráfico de movimientos: $MOV15"
MOVID=$(echo "$MOV15" | jq -r .id); VALID=$(echo "$VAL15" | jq -r .id)
expect 200 "$(req GET "/api/v1/analytics/charts/$MOVID/data")" | jq -e 'all(.points[]; .value>=0) and any(.points[]; .label=="Recepción" and .value>=3)' >/dev/null || fail "movimientos por tipo en unidades positivas"
VSUM15=$(expect 200 "$(req GET "/api/v1/analytics/charts/$VALID/data")" | jq '[.points[].value] | add // 0')
VIND15=$(indval "Valor de inventario a costo")
jq -n --argjson a "$VSUM15" --argjson b "${VIND15:-0}" '($a - $b) | (if . < 0 then -. else . end) <= 0.05' | grep -q true || fail "la dona (con 'Otras') suma $VSUM15 y el indicador 'Valor de inventario a costo' $VIND15"
# PUT: renombrar y restaurar con analytics.manage (200); Solo lectura sin analytics.manage → 403.
chartbody() { echo "$1" | jq -c --arg n "$2" '{name:$n, descriptions, dataSource, groupByField, field, aggregateFn, chartType, filterJson, businessModule, isMoney, visibility, dateRangeMode, dateFrom, dateTo, showInPulse, sortOrder}'; }
expect 200 "$(req PUT "/api/v1/analytics/charts/$VALID" "$(chartbody "$VAL15" "Valor de inventario (humo $TS)")")" | jq -e --arg n "Valor de inventario (humo $TS)" '.name==$n and .isSystem==false and .ownerUserId==null and .chartType=="DONUT"' >/dev/null || fail "renombrar el gráfico de la compañía"
expect 200 "$(req PUT "/api/v1/analytics/charts/$VALID" "$(chartbody "$VAL15" "Valor de inventario por categoría")")" | jq -e '.name=="Valor de inventario por categoría"' >/dev/null || fail "restaurar el nombre del gráfico"
expect 403 "$(req PUT "/api/v1/analytics/charts/$VALID" "$(chartbody "$VAL15" "X")" "$TREAD6")" >/dev/null
# D16: 'Descuadres pendientes' en Indicadores, apagado en el Pulso.
expect 200 "$(req GET /api/v1/analytics/indicators)" | jq -e 'any(.[]; .name=="Descuadres pendientes" and .dataSource=="INVENTORY_DISCREPANCY" and .showInPulse==false and .isSystem)' >/dev/null || fail "indicador 'Descuadres pendientes'"
SQL15="omitido (sin SMOKE_SQL: mover un movimiento de día exige tocar dbo.InventoryTransaction)"
if [[ -n "${SMOKE_SQL:-}" ]]; then
  # El RECEIPT de R15 pasa a las 23:59 de AYER en Puerto Rico (hoy 03:59Z): hoy baja 3 y ayer sube 3 (con días UTC fallaría).
  MV15="UPDATE dbo.InventoryTransaction SET CreatedAtUtc = '%s' WHERE RefId = $R15ID AND RefEntityLookupId = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'EntityType' AND InternalCode = 'RECEIPT') AND TxnTypeLookupId = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'InventoryTxnType' AND InternalCode = 'RECEIPT');"
  F15=$(pday "warehousePublicIds=$W6P")
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; $(printf "$MV15" "${TODAYPR}T03:59:00")" >/dev/null
  G15=$(pday "warehousePublicIds=$W6P")
  [[ $(d15 "$G15" "$F15" receivedToday) == -3 && $(d15 "$G15" "$F15" 'days[-2].receivedUnits') == 3 ]] || fail "el recibo a las 23:59 de ayer (hora de PR) no pasó a ayer: hoy $(d15 "$G15" "$F15" receivedToday)"
  echo "$G15" | jq -e --arg y "$YESTPR" '.days[-2].date==$y' >/dev/null || fail "el penúltimo día no es ayer ($YESTPR)"
  # De vuelta a HOY a las 00:01 locales (04:01Z): vuelve al punto de partida.
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; $(printf "$MV15" "${TODAYPR}T04:01:00")" >/dev/null
  [[ $(d15 "$(pday "warehousePublicIds=$W6P")" "$F15" receivedToday) == 0 ]] || fail "el recibo a las 00:01 de hoy no volvió a hoy"
  SQL15="RECEIPT a las 23:59 de ayer (PR) → hoy −3 y ayer +3; a las 00:01 de hoy → de vuelta"
fi
ok "franja de 7 días locales (PR, hoy $TODAYPR, fechas seguidas), days 0/15 → 400 'Los días deben estar entre 1 y 14.' y 14 → 14; W6: recibo +3, recolección +1 y eliminarla lo devuelve, conteo con +1 → Diferencia (+1, naranja); W7 sin cambio; almacén desconocido en cero; despachador 403; WAREHOUSE_DAY primero (−10) para el admin y ausente para el despachador; gráficos de la compañía (dona CostValue, barras Units ≥ 0 con Recepción ≥ 3; la dona suma $VSUM15 = 'Valor de inventario a costo'); PUT renombrar/restaurar 200 y sin analytics.manage 403; 'Descuadres pendientes' apagado en el Pulso; con SQL: $SQL15"

# ============================================================================================================
# Lote 16 — Recibo directo a posición: almacén W16 DIRECT sin zona STAGING (zonas RSV, QUA y XD de cruce; R-01 con cupo 5,
# R-02, Q-01, X-01). Recibo ciego sin posición de recepción, destino obligatorio al confirmar, sugerencias con cupo,
# confirmar asienta RECEIPT en la posición final sin tareas (Completado → Acomodado con historial), alta atómica con
# targetBinCode, copia del modo en el recibo y compatibilidad de la app anterior en W6, modo en el encabezado y seguridad.
# ============================================================================================================
step "recibo directo a posición (Lote 16): W16 sin STAGING, destino obligatorio, sugerencias con cupo, confirmar sin tareas, app anterior y seguridad"
W16=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"W16$TS\",\"name\":\"Directo $TS\",\"receivingMode\":\"DIRECT\"}")"); W16P=$(wpid "$W16")
echo "$W16" | jq -e '.warehouse.receivingModeCode=="DIRECT" and .warehouse.receivingMode=="Directo a posición" and .warehouse.defaultReceivingBinId==null' >/dev/null || fail "alta de W16 directo: $(echo "$W16" | jq -c .warehouse)"
z16() { expect 200 "$(req POST "/api/v1/warehouses/$W16P/zones" "{\"code\":\"$1\",\"name\":\"Zona $1\",\"zoneType\":\"$2\"}")" | jq -r .id; }
b16() { expect 200 "$(req POST "/api/v1/warehouses/$W16P/bins" "{\"zoneId\":$1,\"code\":\"$2\"${3:+,\"maxCapacityQty\":$3}}")" | jq -r .id; }
Z16R=$(z16 RSV RESERVE); Z16Q=$(z16 QUA QUARANTINE); Z16X=$(z16 XD CROSSDOCK)
B16R1=$(b16 "$Z16R" R-01 5); B16R2=$(b16 "$Z16R" R-02); B16Q=$(b16 "$Z16Q" Q-01); B16X=$(b16 "$Z16X" X-01)
M16U="Modo de recepción desconocido: 'HALF'. Use PUTAWAY o DIRECT."
expect 400 "$(req PATCH "/api/v1/warehouses/$W16P" '{"receivingMode":"HALF"}')" | jq -e --arg m "$M16U" "$HASM" >/dev/null || fail "PATCH receivingMode HALF → 400"
expect 400 "$(req POST /api/v1/warehouses "{\"code\":\"W16B$TS\",\"name\":\"X\",\"receivingMode\":\"HALF\"}")" | jq -e --arg m "$M16U" "$HASM" >/dev/null || fail "alta con receivingMode HALF → 400"
P16=$(prod "{\"sku\":\"P16$TS\",\"name\":\"Directo 16 $TS\",\"purchaseCost\":1}")
# Recibo ciego sin posición de recepción: nace EXPECTED y DIRECT (el almacén no tiene STAGING).
R16=$(expect 200 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$W16P\",\"type\":\"BLIND\"}")"); R16P=$(echo "$R16" | jq -r .header.publicId); R16ID=$(echo "$R16" | jq -r .header.id)
echo "$R16" | jq -e '.header.statusCode=="EXPECTED" and .header.receivingModeCode=="DIRECT" and .header.defaultStagingBinId==null' >/dev/null || fail "recibo directo sin STAGING: $(echo "$R16" | jq -c .header)"
R16=$(expect 200 "$(req POST "/api/v1/receipts/$R16P/lines" "{\"productPublicId\":\"$P16\",\"receivedQty\":8}")"); L16=$(echo "$R16" | jq -r '.lines[0].id')
echo "$R16" | jq -e '.lines[0].stagingBinId==null and .lines[0].targetBinId==null' >/dev/null || fail "línea directa sin posiciones"
# Sin destino no confirma (400 por línea); X-01 (cruce) → 400 de zona; una posición de W6 → 404.
expect 400 "$(req POST "/api/v1/receipts/$R16P/confirm" '{}')" | jq -e --arg m "Indique la posición destino de P16$TS: el recibo entra directo a posición." '.errors["lines[0].targetBinId"][0]==$m' >/dev/null || fail "confirmar sin destino → 400"
expect 400 "$(req PUT "/api/v1/receipts/$R16P/lines/$L16" "{\"targetBinId\":$B16X}")" | jq -e --arg m "La posición X-01 está en una zona CROSSDOCK; la posición destino debe ser de guardado." "$HASM" >/dev/null || fail "destino en cruce de muelle → 400"
expect 404 "$(req PUT "/api/v1/receipts/$R16P/lines/$L16" "{\"targetBinId\":$B_RSV}")" | jq -e --arg m "Posición no encontrada." "$HASM" >/dev/null || fail "destino de otro almacén → 404"
# Sugerencias: R-01 (cupo 5) no cabe con 8 (al final, fits=false); la primera que cabe es R-02; ni X-01 ni cuarentena.
SG16=$(expect 200 "$(req GET "/api/v1/receipts/$R16P/lines/$L16/target-suggestions?take=5")")
echo "$SG16" | jq -e '(map(select(.binCode=="R-01"))[0] | .fits==false and .maxCapacityQty==5 and .freeQty==5) and (map(select(.fits))[0].binCode=="R-02") and all(.[]; .binCode!="X-01" and .binCode!="Q-01")' >/dev/null || fail "sugerencias con cupo: $SG16"
# Destino R-01: espacio libre 5 (excede, solo aviso) y se confirma igual (D4).
expect 200 "$(req PUT "/api/v1/receipts/$R16P/lines/$L16" "{\"targetBinId\":$B16R1}")" | jq -e '.lines[0].targetBinCode=="R-01" and .lines[0].targetZoneTypeCode=="RESERVE" and .lines[0].targetFreeQty==5' >/dev/null || fail "destino R-01 con targetFreeQty 5"
B16=$(pday "warehousePublicIds=$W16P")
expect 200 "$(req POST "/api/v1/receipts/$R16P/confirm" '{}')" | jq -e '.header.statusCode=="PUTAWAY" and .header.pendingPutawayCount==0 and (.putawayTasks|length)==0' >/dev/null || fail "confirmar directo → Acomodado sin tareas"
expect 200 "$(req GET "/api/v1/status/history/RECEIPT/$R16ID")" | jq -e 'map(.toCode)==["EXPECTED","RECEIVING","RECEIVED","PUTAWAY"]' >/dev/null || fail "historial del recibo directo: $(req GET "/api/v1/status/history/RECEIPT/$R16ID" | sed '$d' | jq -c 'map(.toCode)')"
kardex "refEntity=RECEIPT&refId=$R16ID" | jq -e '.total==1 and .items[0].typeCode=="RECEIPT" and .items[0].quantity==8 and .items[0].toBinCode=="R-01"' >/dev/null || fail "Kárdex: RECEIPT +8 a R-01"
[[ $(onhand "$W16P" "$B16R1" "$P16") == 8 ]] || fail "existencia de P16 en R-01"
[[ $(d15 "$(pday "warehousePublicIds=$W16P")" "$B16" receivedToday) == 8 ]] || fail "franja: lo recibido hoy en W16 no sube 8"
# D13: Actividad reciente muestra un solo evento para el recibo directo sin tareas ("confirmado", sin "acomodado").
activity "module=WAREHOUSE" | jq -e --arg r "$R16P" '[.[] | select(.publicId==$r) | .code] == ["RECEIPT_CONFIRMED"]' >/dev/null || fail "Actividad del recibo directo: $(activity "module=WAREHOUSE" | jq -c --arg r "$R16P" '[.[] | select(.publicId==$r) | .code]')"
# Alta atómica (app nueva) con targetBinCode: Q-01 → 200 directo a cuarentena; ZZ → 400.
expect 200 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$W16P\",\"type\":\"RETURN\",\"confirm\":true,\"lines\":[{\"productPublicId\":\"$P16\",\"receivedQty\":1,\"targetBinCode\":\"Q-01\"}]}")" | jq -e '.header.statusCode=="PUTAWAY" and .header.receivingModeCode=="DIRECT" and .lines[0].targetBinCode=="Q-01"' >/dev/null || fail "confirm con targetBinCode Q-01"
expect 400 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$W16P\",\"type\":\"BLIND\",\"confirm\":true,\"lines\":[{\"productPublicId\":\"$P16\",\"receivedQty\":1,\"targetBinCode\":\"ZZ\"}]}")" | jq -e --arg m "La posición ZZ no existe en el almacén del recibo." "$HASM" >/dev/null || fail "targetBinCode ZZ → 400"
# Encabezado: modo solo de este recibo (DIRECT/PUTAWAY; desconocido 400; a PUTAWAY sin STAGING → 422).
R16B=$(expect 200 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$W16P\",\"type\":\"BLIND\"}")"); R16BP=$(echo "$R16B" | jq -r .header.publicId)
expect 400 "$(req PATCH "/api/v1/receipts/$R16BP" '{"receivingMode":"HALF"}')" | jq -e --arg m "$M16U" "$HASM" >/dev/null || fail "PATCH del recibo con modo HALF → 400"
expect 422 "$(req PATCH "/api/v1/receipts/$R16BP" '{"receivingMode":"PUTAWAY"}')" | jq -e --arg m "El almacén no tiene una posición de recepción (zona STAGING); indíquela." "$HASM" >/dev/null || fail "a PUTAWAY sin STAGING → 422"
expect 204 "$(req DELETE "/api/v1/receipts/$R16BP")" >/dev/null
# W6 (con STAGING): pasa a DIRECT; un recibo abierto en DIRECT conserva su modo al volver W6 a PUTAWAY (D2); la app anterior
# (confirm sin modo ni destinos) en un almacén directo recibe con acomodo (D9); el encabezado cambia el modo del recibo.
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P" '{"receivingMode":"DIRECT"}')" | jq -e '.warehouse.receivingModeCode=="DIRECT"' >/dev/null || fail "W6 a DIRECT"
OLD16=$(expect 200 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$W6P\",\"type\":\"BLIND\",\"confirm\":true,\"lines\":[{\"productPublicId\":\"$P16\",\"receivedQty\":1}]}")")
echo "$OLD16" | jq -e '.header.receivingModeCode=="PUTAWAY" and .header.statusCode=="RECEIVED" and (.putawayTasks|length)==1' >/dev/null || fail "app anterior en almacén directo → con acomodo: $(echo "$OLD16" | jq -c '{m:.header.receivingModeCode,s:.header.statusCode,t:(.putawayTasks|length)}')"
expect 200 "$(req POST "/api/v1/warehouse-tasks/$(echo "$OLD16" | jq -r '.putawayTasks[0].id')/complete" '{}')" | jq -e '.statusCode=="DONE"' >/dev/null || fail "completar el acomodo del recibo de la app anterior"
C16=$(expect 200 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$W6P\",\"type\":\"BLIND\"}")"); C16P=$(echo "$C16" | jq -r .header.publicId)
echo "$C16" | jq -e '.header.receivingModeCode=="DIRECT"' >/dev/null || fail "recibo nuevo en W6 directo"
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P" '{"receivingMode":"PUTAWAY"}')" | jq -e '.warehouse.receivingModeCode=="PUTAWAY"' >/dev/null || fail "W6 de vuelta a PUTAWAY"
expect 200 "$(req GET "/api/v1/receipts/$C16P")" | jq -e '.header.receivingModeCode=="DIRECT"' >/dev/null || fail "el recibo abierto no conservó su modo (D2)"
expect 200 "$(req PATCH "/api/v1/receipts/$C16P" '{"receivingMode":"PUTAWAY"}')" | jq -e '.header.receivingModeCode=="PUTAWAY"' >/dev/null || fail "PATCH del recibo a PUTAWAY"
expect 200 "$(req PATCH "/api/v1/receipts/$C16P" '{"receivingMode":"DIRECT"}')" | jq -e '.header.receivingModeCode=="DIRECT"' >/dev/null || fail "PATCH del recibo a DIRECT"
expect 204 "$(req DELETE "/api/v1/receipts/$C16P")" >/dev/null
# Posición de recepción por defecto (D12) en W6: STG-01; una de reserva → 400; y el acomodo dirigido trae cupo y espacio libre.
expect 200 "$(req PATCH "/api/v1/warehouses/$W6P" "{\"defaultReceivingBinId\":$B_STG}")" | jq -e '.warehouse.defaultReceivingBinCode=="STG-01"' >/dev/null || fail "posición de recepción por defecto STG-01"
expect 400 "$(req PATCH "/api/v1/warehouses/$W6P" "{\"defaultReceivingBinId\":$B_RSV}")" | jq -e --arg m "La posición de recepción debe estar en una zona STAGING o CROSSDOCK." "$HASM" >/dev/null || fail "posición por defecto de reserva → 400"
expect 200 "$(req GET "/api/v1/warehouse-tasks/putaway-suggestions?productPublicId=$P16&warehousePublicId=$W16P&quantity=1")" | jq -e 'all(.[]; .binCode!="R-01")' >/dev/null || fail "R-01 lleno (8 de 5) sugerido para acomodo"
expect 200 "$(req GET "/api/v1/warehouse-tasks/putaway-suggestions?productPublicId=$P16&warehousePublicId=$W16P&quantity=1")" | jq -e 'any(.[]; .binCode=="R-02" and .maxCapacityQty==null and .freeQty==null)' >/dev/null || fail "acomodo dirigido con cupo y espacio libre"
# Seguridad: Solo lectura no cambia el modo del almacén (warehouse.manage); el despachador no ve sugerencias (inventory.view).
expect 403 "$(req PATCH "/api/v1/warehouses/$W16P" '{"receivingMode":"PUTAWAY"}' "$TREAD6")" >/dev/null
expect 403 "$(req GET "/api/v1/receipts/$R16P/lines/$L16/target-suggestions" '' "$TD14")" >/dev/null
expect 403 "$(req POST "/api/v1/receipts/$R16P/targets/suggest" '{}' "$TREAD6")" >/dev/null
ok "W16 DIRECT sin STAGING (HALF → 400 '$M16U'); recibo ciego EXPECTED DIRECT sin posición de recepción; sin destino → 400 por línea; X-01 → 400 de zona; posición de W6 → 404; sugerencias con R-01 (cupo 5) fits=false y R-02 primero; R-01 con targetFreeQty 5; confirmar → Acomodado sin tareas con historial EXPECTED→RECEIVING→RECEIVED→PUTAWAY, Kárdex RECEIPT +8 en R-01, existencia 8, franja +8 y un solo evento RECEIPT_CONFIRMED en Actividad (D13); confirm con targetBinCode Q-01 → 200 y ZZ → 400; modo del recibo HALF 400 y a PUTAWAY sin STAGING 422; en W6: app anterior → con acomodo (D9), recibo abierto conserva DIRECT (D2) y PATCH del modo; posición de recepción por defecto STG-01 (reserva 400); acomodo dirigido con cupo; 403 de Solo lectura y despachador"

# ============================================================================================================
# Lote 8A — aparatos y sincronización (app de almacén): aparato de confianza, PIN, login por aparato, idempotencia,
# sincronización por diferencia, código escaneado, recibo en una llamada, conteo a ciegas en lote y aparato desactivado.
# ============================================================================================================
step "aparatos y sincronización (Lote 8A)"
IDH=$(mktemp)
idem() { # method path idempotency-key body [token] → cuerpo + código; cabeceras de la respuesta en $IDH
  curl -sS -X "$1" "$BASE$2" -H 'Accept: application/json' -H 'Content-Type: application/json' -H 'X-Lang: es' \
    -H "Authorization: Bearer ${5:-$DT}" -H "Idempotency-Key: $3" -D "$IDH" --data "$4" -w '\n%{http_code}'
}
replayed() { grep -qi '^idempotent-replayed: *true' "$IDH"; }
ok2xx() { local c; c=$(echo "$1" | tail -n1); [[ $c == 200 || $c == 204 ]] || fail "$2: HTTP $c $(echo "$1" | sed '$d')"; }
jwtclaims() { local p; p=$(echo "$1" | cut -d. -f2 | tr '_-' '/+'); while (( ${#p} % 4 )); do p="$p="; done; echo "$p" | base64 -d; }
days30() { jq -e '((.refreshExpiresAtUtc | sub("\\.[0-9]+";"") | sub("Z?$";"Z") | fromdateiso8601) - now) / 86400 | (. > 29 and . < 31)' >/dev/null; }
SINCE8=$(date -u -d '-1 hour' +%Y-%m-%dT%H:%M:%SZ)
ME8=$(expect 200 "$(req GET /api/v1/me)" | jq -r .userId)
PZ=$(prod "{\"sku\":\"PZ$TS\",\"name\":\"Aparato $TS\",\"barcode\":\"ZBC$TS\",\"purchaseCost\":1}")
# Alta del aparato (devices.manage): el código de registro se muestra una sola vez; código de aparato repetido → 409; sin
# devices.manage → 403.
DEV=$(expect 200 "$(req POST /api/v1/devices "{\"code\":\"ZB-$TS\",\"name\":\"Zebra smoke $TS\",\"model\":\"MC3300\",\"defaultWarehousePublicId\":\"$W6P\",\"theme\":\"LIGHT\"}")")
DEVP=$(echo "$DEV" | jq -r .device.publicId); ENROLL=$(echo "$DEV" | jq -r .enrollCode)
[[ "$DEVP" =~ ^[0-9a-f-]{36}$ && ${#ENROLL} -eq 8 ]] || fail "alta del aparato con código de registro de 8 caracteres: $DEV"
hours24() { jq -e '((.device.enrollCodeExpiresUtc | sub("\\.[0-9]+";"") | sub("Z?$";"Z") | fromdateiso8601) - now) | (. > 23*3600 and . < 25*3600)' >/dev/null; }
echo "$DEV" | hours24 || fail "el código de registro no vence en 24 h: $(echo "$DEV" | jq -r .device.enrollCodeExpiresUtc)"
expect 409 "$(req POST /api/v1/devices "{\"code\":\"ZB-$TS\"}")" | jq -e --arg m "Ya existe un aparato con ese código." "$HASM" >/dev/null || fail "código de aparato repetido → 409"
expect 403 "$(req POST /api/v1/devices "{\"code\":\"ZX-$TS\"}" "$TWH6")" >/dev/null || fail "alta de aparato sin devices.manage → 403"
# Decisión 12: USER_DEVICE en la ruta polimórfica de campos personalizados exige devices.manage (403 sin él, leer o escribir);
# con el permiso, el resolver cerrado da 404 al escribir (el GET no comprueba que el registro exista).
expect 403 "$(req GET '/api/v1/custom-fields/values/USER_DEVICE/1' '' "$TWH6")" | denied devices.manage || fail "campos de USER_DEVICE sin devices.manage (GET) → 403"
expect 403 "$(req PUT '/api/v1/custom-fields/values/USER_DEVICE/1' '{"values":{}}' "$TWH6")" | denied devices.manage || fail "campos de USER_DEVICE sin devices.manage (PUT) → 403"
expect 404 "$(req PUT '/api/v1/custom-fields/values/USER_DEVICE/1' '{"values":{}}')" >/dev/null || fail "campos de USER_DEVICE con devices.manage → 404 (resolver cerrado)"
expect 200 "$(req POST /api/v1/devices '{"code":""}')" | jq -e '.device.code | test("^AP-[A-Z0-9]{6}$")' >/dev/null || fail "alta de aparato sin código → el servidor genera uno (AP-XXXXXX, Lote F8a)"
expect 400 "$(req POST /api/v1/devices "{\"code\":\"$(printf 'A%.0s' {1..31})\"}")" | jq -e --arg m "El código del aparato admite hasta 30 caracteres." "$HASM" >/dev/null || fail "alta de aparato con código de 31 caracteres → 400"
expect 400 "$(req POST /api/v1/devices "{\"code\":\"ZN-$TS\",\"name\":\"$(printf 'N%.0s' {1..101})\"}")" | jq -e --arg m "El nombre admite hasta 100 caracteres." "$HASM" >/dev/null || fail "alta de aparato con nombre de 101 caracteres → 400"
expect 400 "$(req POST /api/v1/devices "{\"code\":\"ZN-$TS\",\"model\":\"$(printf 'M%.0s' {1..81})\"}")" | jq -e --arg m "El modelo admite hasta 80 caracteres." "$HASM" >/dev/null || fail "alta de aparato con modelo de 81 caracteres → 400"
# Las rutas que devuelven credenciales no pasan por la idempotencia: con Idempotency-Key el alta se ejecuta dos veces (la
# segunda 409 por código repetido, no Replay) y el código de registro no queda en IntegrationMessageLog.
KDX="smoke-$TS-devx"; BDX="{\"code\":\"ZI-$TS\"}"
DX=$(expect 200 "$(idem POST /api/v1/devices "$KDX" "$BDX" "$TOKEN")") || fail "alta de aparato con Idempotency-Key"
echo "$DX" | jq -e '.enrollCode|length==8' >/dev/null || fail "alta de aparato con Idempotency-Key sin código de registro"
expect 409 "$(idem POST /api/v1/devices "$KDX" "$BDX" "$TOKEN")" | jq -e --arg m "Ya existe un aparato con ese código." "$HASM" >/dev/null || fail "/devices no debe pasar por la idempotencia (hubo Replay)"
replayed && fail "/devices respondió con Idempotent-Replayed"
[[ -z "${SMOKE_SQL:-}" ]] || [[ $($SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.IntegrationMessageLog WHERE IdempotencyKey='$KDX';" | tr -d '[:space:]') == 0 ]] || fail "la respuesta con credenciales quedó en IntegrationMessageLog"
expect 200 "$(req POST "/api/v1/devices/$(echo "$DX" | jq -r .device.publicId)/deactivate" '{}')" >/dev/null
ENR=$(expect 200 "$(anon POST /api/v1/devices/enroll "{\"enrollCode\":\"$ENROLL\",\"model\":\"MC3300\",\"appVersion\":\"1.0.0\"}")")
echo "$ENR" | jq -e --arg d "$DEVP" --arg w "$W6P" '.devicePublicId==$d and .defaultWarehousePublicId==$w and .theme=="LIGHT" and (.tenantName|type=="string")' >/dev/null || fail "enroll devuelve el aparato con su almacén y tema: $ENR"
SECRET=$(echo "$ENR" | jq -r .deviceSecret); [[ -n "$SECRET" && "$SECRET" != null ]] || fail "enroll sin secreto"
expect 200 "$(req GET "/api/v1/devices/$DEVP")" | jq -e '.enrollCodeExpiresUtc==null and .isEnrolled==true' >/dev/null || fail "tras el enroll el código de registro ya no está vigente"
# El código es de un solo uso; uno inventado tampoco sirve (mismo 401, sin oráculo).
expect 401 "$(anon POST /api/v1/devices/enroll "{\"enrollCode\":\"$ENROLL\"}")" | jq -e --arg m "El código de registro no es válido o venció." "$HASM" >/dev/null || fail "código de registro reutilizado → 401"
expect 401 "$(anon POST /api/v1/devices/enroll '{"enrollCode":"ZZZZ2222"}')" | jq -e --arg m "El código de registro no es válido o venció." "$HASM" >/dev/null || fail "código de registro inventado → 401"
# Campos de entrada anulables: sin enrollCode responde el servicio (401 en español), no el 400 genérico de MVC.
expect 401 "$(anon POST /api/v1/devices/enroll '{}')" | jq -e --arg m "El código de registro no es válido o venció." "$HASM" >/dev/null || fail "enroll sin código → 401 'El código de registro no es válido o venció.'"
# Regenerar el código (segundo aparato, para no cambiar el secreto del primero): el anterior deja de servir.
DEV2=$(expect 200 "$(req POST /api/v1/devices "{\"code\":\"ZC-$TS\"}")"); C1=$(echo "$DEV2" | jq -r .enrollCode); D2=$(echo "$DEV2" | jq -r .device.publicId)
DEV2B=$(expect 200 "$(req POST "/api/v1/devices/$D2/enroll-code" '{}')"); C2=$(echo "$DEV2B" | jq -r .enrollCode); [[ ${#C2} -eq 8 && "$C2" != "$C1" ]] || fail "código de registro regenerado"
echo "$DEV2B" | hours24 || fail "el código regenerado no vence en 24 h"
expect 401 "$(anon POST /api/v1/devices/enroll "{\"enrollCode\":\"$C1\"}")" | jq -e --arg m "El código de registro no es válido o venció." "$HASM" >/dev/null || fail "el código anterior sigue sirviendo tras regenerarlo"
# Código vencido (hash correcto, fecha pasada) → 401 sin oráculo; se restaura la vigencia y el mismo código registra (el 401
# se debió a la fecha, no al hash).
if [[ -n "${SMOKE_SQL:-}" ]]; then
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; UPDATE dbo.UserDevice SET EnrollCodeExpiresUtc = DATEADD(minute,-1,SYSUTCDATETIME()) WHERE PublicId = '$D2';" >/dev/null
  expect 401 "$(anon POST /api/v1/devices/enroll "{\"enrollCode\":\"$C2\"}")" | jq -e --arg m "El código de registro no es válido o venció." "$HASM" >/dev/null || fail "código de registro vencido → 401"
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; UPDATE dbo.UserDevice SET EnrollCodeExpiresUtc = DATEADD(hour,24,SYSUTCDATETIME()) WHERE PublicId = '$D2';" >/dev/null
fi
expect 200 "$(anon POST /api/v1/devices/enroll "{\"enrollCode\":\"$C2\"}")" | jq -e --arg d "$D2" '.devicePublicId==$d' >/dev/null || fail "el código regenerado no registra el aparato"
# PIN del admin desde Mi cuenta: exige la contraseña actual (incorrecta → 400 en errors.currentPassword).
expect 400 "$(req PUT /api/v1/me/pin '{"currentPassword":"No_Es_La_Clave_2026!","pin":"4826"}')" | jq -e --arg m "La contraseña actual es incorrecta." '(.errors.currentPassword // []) | index($m) != null' >/dev/null || fail "PUT /me/pin con contraseña incorrecta → 400 en errors.currentPassword"
expect 400 "$(req PUT /api/v1/me/pin '{"pin":"4826"}')" | jq -e --arg m "La contraseña actual es incorrecta." '(.errors.currentPassword // []) | index($m) != null' >/dev/null || fail "PUT /me/pin sin currentPassword → 400 en español (no 'The CurrentPassword field is required.')"
expect 400 "$(req PUT /api/v1/me/pin "{\"currentPassword\":\"$PASS\"}")" | jq -e --arg m "El PIN debe tener de 4 a 6 dígitos." '(.errors.pin // []) | index($m) != null' >/dev/null || fail "PUT /me/pin sin pin → 400 en errors.pin en español (no 'The Pin field is required.')"
ok2xx "$(req PUT /api/v1/me/pin "{\"currentPassword\":\"$PASS\",\"pin\":\"4826\"}")" "PUT /me/pin"
# SecurityEvent del PIN (outcome llega traducido por X-Lang: es → Éxito / Fallo).
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PASSWORD_CHANGE&take=20')" | jq -e '([.items[] | select(((.detailJson // "") | contains("\"target\":\"pin\"")) and .outcome=="Éxito")] | length >= 1) and ([.items[] | select(((.detailJson // "") | contains("\"target\":\"pin\"")) and .outcome=="Fallo")] | length >= 1)' >/dev/null || fail "SecurityEvent PASSWORD_CHANGE target=pin (SUCCESS y FAILURE)"
expect 200 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}")" | jq -e --argjson u "$ME8" 'any(.[]; .userId==$u)' >/dev/null || fail "el admin con PIN no aparece en los usuarios del aparato"
DLOGIN() { anon POST /api/v1/auth/device/login "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\",\"userId\":${2:-$ME8},\"pin\":\"$1\"}"; }
HB() { anon POST /api/v1/devices/heartbeat "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"${1:-$SECRET}\",\"appVersion\":\"1.0.1\"}"; }
BADDEV="El aparato no está registrado o fue desactivado."
# Secreto incorrecto: device/users, device/login y heartbeat → 401 (antes de mirar el PIN: no cuenta intentos).
expect 401 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"otro-$TS\"}")" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "device/users con secreto incorrecto → 401"
expect 401 "$(anon POST /api/v1/auth/device/login "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"otro-$TS\",\"userId\":$ME8,\"pin\":\"4826\"}")" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "device/login con secreto incorrecto → 401"
expect 401 "$(anon POST /api/v1/auth/device/login "{\"devicePublicId\":\"$DEVP\",\"userId\":$ME8,\"pin\":\"4826\"}")" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "device/login sin deviceSecret → 401 en español (no el 400 de MVC)"
expect 401 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\"}")" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "device/users sin deviceSecret → 401 en español (no el 400 de MVC)"
expect 401 "$(anon POST /api/v1/devices/heartbeat "{\"devicePublicId\":\"$DEVP\"}")" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "heartbeat sin deviceSecret → 401 en español (no el 400 de MVC)"
expect 401 "$(HB "otro-$TS")" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "heartbeat con secreto incorrecto → 401"
expect 200 "$(HB)" | jq -e --arg w "$W6P" '.isActive==true and .defaultWarehousePublicId==$w and .theme=="LIGHT" and (.serverTimeUtc|type=="string")' >/dev/null || fail "heartbeat del aparato activo"
expect 200 "$(req GET "/api/v1/devices/$DEVP")" | jq -e '.appVersion=="1.0.1" and .lastSeenUtc!=null' >/dev/null || fail "el heartbeat no registró AppVersion ni LastSeenUtc"
# Decisión 4: el admin de plataforma nunca entra por aparato ni aparece en su lista, aunque tenga PIN propio.
T_SOP8=$(expect 200 "$(anon POST /api/v1/auth/login "{\"email\":\"$PLATFORM_EMAIL\",\"password\":\"$PASS\",\"tenantId\":$ME_TID,\"deviceInfo\":\"smoke\"}")" | jq -r .tokens.accessToken)
ok2xx "$(req PUT /api/v1/me/pin "{\"currentPassword\":\"$PASS\",\"pin\":\"4826\"}" "$T_SOP8")" "PIN propio del admin de plataforma"
expect 200 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}")" | jq -e --argjson u "$SOP_ID" 'all(.[]; .userId!=$u)' >/dev/null || fail "el admin de plataforma aparece en los usuarios del aparato"
expect 401 "$(DLOGIN 4826 "$SOP_ID")" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "el admin de plataforma entra por aparato con su PIN"
# Un userId de otra compañía: el mismo 401 y el evento LOGIN/FAILURE de esta compañía sale sin usuario (el id pedido va en
# el detalle), para que su bitácora no revele el nombre ni el correo de usuarios ajenos.
expect 401 "$(DLOGIN 4826 "$UID_T3")" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "device/login con un usuario de otra compañía → 401"
# Decisión 17: el mismo intento con el bearer de un usuario de otra compañía (T3) tampoco deja su UserId en esta bitácora
# (device/login corre como anónimo en la compañía del aparato).
T3D=$(login "admin$TS@smoke.local" "Smoke_Admin_2026!")
expect 401 "$(req POST /api/v1/auth/device/login "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\",\"userId\":$UID_T3,\"pin\":\"4826\"}" "$T3D")" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "device/login con bearer de otra compañía → 401"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=LOGIN&take=50')" | jq -e --argjson u "$UID_T3" '[.items[] | select((.detailJson // "") | contains("\"requestedUserId\":\($u)"))] | length >= 1 and all(.[]; .userId==null and .userName==null)' >/dev/null || fail "el login por aparato con un usuario de otra compañía lo expone en la bitácora de esta"
ok2xx "$(req DELETE /api/v1/me/pin '' "$T_SOP8")" "quitar el PIN del admin de plataforma"
# Intentos: 4 fallos → un acierto reinicia el contador → 4 fallos más dan 401 y el 5.º bloquea (423), también con el PIN
# correcto; volver a guardar el PIN quita el bloqueo.
for i in 1 2 3 4; do expect 401 "$(DLOGIN 1397)" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "PIN incorrecto → 401 (intento $i)"; done
expect 200 "$(DLOGIN 4826)" >/dev/null
for i in 1 2 3 4; do expect 401 "$(DLOGIN 1397)" >/dev/null || fail "tras un acierto el contador vuelve a cero (intento $i)"; done
expect 423 "$(DLOGIN 1397)" | jq -e --arg m "PIN bloqueado por 15 minutos." "$HASM" >/dev/null || fail "5.º PIN incorrecto → 423"
expect 423 "$(DLOGIN 4826)" | jq -e --arg m "PIN bloqueado por 15 minutos." "$HASM" >/dev/null || fail "PIN correcto durante el bloqueo → 423"
expect 200 "$(req GET /api/v1/me/pin)" | jq -e '.hasPin==true and .lockedUntilUtc!=null' >/dev/null || fail "GET /me/pin con el PIN bloqueado no devuelve lockedUntilUtc"
PINX8="vencimiento del bloqueo omitido (sin SMOKE_SQL)"
if [[ -n "${SMOKE_SQL:-}" ]]; then
  # Vence el bloqueo sin restablecer el PIN: un fallo vuelve a contar desde cero (401, no 423) y el PIN correcto entra.
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; UPDATE dbo.UserPin SET LockedUntilUtc = DATEADD(minute,-1,SYSUTCDATETIME()) WHERE UserId = $ME8 AND LockedUntilUtc > SYSUTCDATETIME();" >/dev/null
  expect 401 "$(DLOGIN 1397)" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "vencido el bloqueo, un PIN incorrecto → 401 (contador desde cero)"
  expect 200 "$(DLOGIN 4826)" >/dev/null || fail "vencidos los 15 minutos, el PIN correcto entra sin restablecerlo"
  PINX8="vencido el bloqueo (15 min) se vuelve a entrar sin restablecer el PIN"
fi
ok2xx "$(req PUT /api/v1/me/pin "{\"currentPassword\":\"$PASS\",\"pin\":\"4826\"}")" "PUT /me/pin quita el bloqueo"
# Decisión 9: 10 PIN incorrectos en paralelo → ningún 200, a lo más 4 × 401 y el resto 423 (UPDATE atómico); luego el PIN
# correcto → 423. Volver a guardar el PIN deja el contador en 0.
TMPP=$(mktemp -d); for i in $(seq 1 10); do DLOGIN 1397 > "$TMPP/$i" & done; wait
CPP8=$(codes "$TMPP"/*); N401=$(grep -o 401 <<<"$CPP8" | wc -l); N423=$(grep -o 423 <<<"$CPP8" | wc -l); rm -rf "$TMPP"
(( N401 <= 4 && N401 + N423 == 10 )) || fail "PIN en paralelo sin bloqueo atómico: $CPP8"
expect 423 "$(DLOGIN 4826)" | jq -e --arg m "PIN bloqueado por 15 minutos." "$HASM" >/dev/null || fail "tras 10 fallos en paralelo el PIN correcto entra"
ok2xx "$(req PUT /api/v1/me/pin "{\"currentPassword\":\"$PASS\",\"pin\":\"4826\"}")" "PUT /me/pin quita el bloqueo (paralelo)"
# Sesión de aparato: refresh de DeviceSessionDays (30) días y claim did; el refresh la renueva y conserva el did.
DL8=$(expect 200 "$(DLOGIN 4826)"); DT=$(echo "$DL8" | jq -r .accessToken)
[[ -n "$DT" && "$DT" != null ]] || fail "login por aparato sin token"
echo "$DL8" | days30 || fail "la sesión del aparato no dura DeviceSessionDays (30) días: $(echo "$DL8" | jq -r .refreshExpiresAtUtc)"
[[ $(jwtclaims "$DT" | jq -r .did) == "$DEVP" ]] || fail "el token del aparato no lleva el claim did"
# La sesión de aparato (solo PIN) no administra el segundo factor de la cuenta: enroll y confirm de TOTP → 403.
MFA8="La sesión de un aparato no administra el segundo factor."
expect 403 "$(req POST /api/v1/auth/mfa/totp/enroll '{}' "$DT")" | jq -e --arg m "$MFA8" "$HASM" >/dev/null || fail "la sesión de aparato enroló TOTP"
expect 403 "$(req POST /api/v1/auth/mfa/totp/confirm '{"code":"123456"}' "$DT")" | jq -e --arg m "$MFA8" "$HASM" >/dev/null || fail "la sesión de aparato confirmó TOTP"
expect 200 "$(req GET /api/v1/devices)" | jq -e --arg d "$DEVP" --argjson u "$ME8" 'any(.[]; .publicId==$d and .lastSeenUtc!=null and .lastUserId==$u)' >/dev/null || fail "device/login no registró LastSeenUtc/LastUserId"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=LOGIN&take=50')" | jq -e '([.items[] | select(((.detailJson // "") | contains("\"stage\":\"device\"")) and .outcome=="Éxito")] | length >= 1) and ([.items[] | select(((.detailJson // "") | contains("\"stage\":\"device\"")) and .outcome=="Fallo")] | length >= 1)' >/dev/null || fail "SecurityEvent LOGIN stage=device (SUCCESS y FAILURE)"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=LOCKOUT&take=20')" | jq -e '[.items[] | select((.detailJson // "") | contains("\"stage\":\"device\""))] | length >= 1' >/dev/null || fail "SecurityEvent LOCKOUT del PIN"
# Edición del aparato (PATCH): nombre y tema; tema inválido 400; almacén por defecto dado de baja 422.
# Heartbeat y login con PIN escriben en UserDeviceActivity y NO cambian la rowVersion del aparato: el PATCH con la rowVersion
# leída antes pasa; repetirlo con esa misma rowVersion tras la edición real → 409.
RV8=$(expect 200 "$(req GET "/api/v1/devices/$DEVP")" | jq -r .rowVersion)
expect 200 "$(HB)" >/dev/null; expect 200 "$(DLOGIN 4826)" >/dev/null
[[ $(expect 200 "$(req GET "/api/v1/devices/$DEVP")" | jq -r .rowVersion) == "$RV8" ]] || fail "el heartbeat o el login con PIN cambiaron la rowVersion del aparato"
expect 200 "$(req PATCH "/api/v1/devices/$DEVP" "{\"name\":\"Zebra 2\",\"theme\":\"DARK\",\"rowVersion\":\"$RV8\"}")" | jq -e '.name=="Zebra 2" and .theme=="DARK"' >/dev/null || fail "PATCH del aparato (nombre y tema) con la rowVersion leída antes del heartbeat y del login"
expect 409 "$(req PATCH "/api/v1/devices/$DEVP" "{\"name\":\"Zebra 3\",\"rowVersion\":\"$RV8\"}")" | jq -e --arg m "El registro fue modificado por otro usuario; recargue e intente de nuevo." "$HASM" >/dev/null || fail "PATCH con rowVersion vieja tras una edición real → 409"
expect 400 "$(req PATCH "/api/v1/devices/$DEVP" '{"theme":"BLUE"}')" | jq -e --arg m "El tema no es válido; use LIGHT o DARK." "$HASM" >/dev/null || fail "PATCH con tema inválido → 400"
expect 422 "$(req PATCH "/api/v1/devices/$DEVP" "{\"defaultWarehousePublicId\":\"$W9P\"}")" | jq -e --arg m "El almacén por defecto está dado de baja." "$HASM" >/dev/null || fail "PATCH con almacén dado de baja → 422"
expect 200 "$(req PATCH "/api/v1/devices/$DEVP" '{"theme":"LIGHT"}')" | jq -e --arg w "$W6P" '.theme=="LIGHT" and .defaultWarehousePublicId==$w' >/dev/null || fail "PATCH de vuelta a LIGHT conserva el almacén"
# DeviceSessionDays configurable: 7 días → la sesión del aparato dura ~7 (login y refresh); fuera de rango → 400; se restaura 30.
# El refresh de la sesión de DL8 (emitida con 30 días) se hace DESPUÉS de bajar a 7: si durara ~7 es que recalcula los días
# desde Tenant.DeviceSessionDays y no copia la vigencia anterior; tras restaurar 30, otro refresh vuelve a ~30.
days7() { jq -e '((.refreshExpiresAtUtc | sub("\\.[0-9]+";"") | sub("Z?$";"Z") | fromdateiso8601) - now) / 86400 | (. > 6 and . < 8)' >/dev/null; }
expect 200 "$(req PUT /api/v1/tenant/settings '{"deviceSessionDays":7}')" | jq -e '.deviceSessionDays==7' >/dev/null || fail "PUT settings deviceSessionDays=7"
DL7=$(expect 200 "$(DLOGIN 4826)"); echo "$DL7" | days7 || fail "la sesión del aparato no usa Tenant.DeviceSessionDays (7): $(echo "$DL7" | jq -r .refreshExpiresAtUtc)"
DL8=$(expect 200 "$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$(echo "$DL8" | jq -r .refreshToken)\"}")"); DT=$(echo "$DL8" | jq -r .accessToken)
echo "$DL8" | days7 || fail "el refresh del aparato no recalcula los días desde Tenant.DeviceSessionDays (7): $(echo "$DL8" | jq -r .refreshExpiresAtUtc)"
for v in 0 366; do expect 400 "$(req PUT /api/v1/tenant/settings "{\"deviceSessionDays\":$v}")" | jq -e --arg m "Entre 1 y 365 días." '(.errors.deviceSessionDays // []) | index($m) != null' >/dev/null || fail "deviceSessionDays=$v → 400"; done
expect 200 "$(req PUT /api/v1/tenant/settings '{"deviceSessionDays":30}')" | jq -e '.deviceSessionDays==30' >/dev/null || fail "restaurar deviceSessionDays=30"
DL8=$(expect 200 "$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$(echo "$DL8" | jq -r .refreshToken)\"}")"); DT=$(echo "$DL8" | jq -r .accessToken)
echo "$DL8" | days30 || fail "tras restaurar DeviceSessionDays=30 el refresh del aparato no renueva los 30 días: $(echo "$DL8" | jq -r .refreshExpiresAtUtc)"
[[ $(jwtclaims "$DT" | jq -r .did) == "$DEVP" ]] || fail "el refresh del aparato perdió el claim did"
# PIN de otros (devices.manage o admin.users; AAL2): hasPin en la lista; sin permiso 403; de otra compañía 404; nadie
# impone un PIN a quien tiene más permisos (403) ni sin reauth reciente (403 aal2_required).
RE=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")"); TOKEN=$(echo "$RE" | jq -r .accessToken)
USERS8=$(expect 200 "$(req GET /api/v1/users)")
UWH6=$(echo "$USERS8" | jq -r --arg e "bodega6$TS@teikem.local" '.[] | select(.email==$e) | .id')
URD6=$(echo "$USERS8" | jq -r --arg e "lectura6$TS@teikem.local" '.[] | select(.email==$e) | .id')
expect 200 "$(req PUT "/api/v1/users/$UWH6/pin" '{"pin":"4826"}')" | jq -e '.hasPin==true' >/dev/null || fail "PUT /users/{id}/pin"
expect 200 "$(req GET "/api/v1/audit/security-events?eventType=PASSWORD_CHANGE&userId=$UWH6&take=20")" | jq -e '[.items[] | select(((.detailJson // "") | contains("\"target\":\"pin\"")) and ((.detailJson // "") | contains("\"by\":")) and .outcome=="Éxito")] | length >= 1' >/dev/null || fail "SecurityEvent PASSWORD_CHANGE del PIN asignado por otro (by)"
expect 400 "$(req PUT "/api/v1/users/$UWH6/pin" '{}')" | jq -e --arg m "El PIN debe tener de 4 a 6 dígitos." '(.errors.pin // []) | index($m) != null' >/dev/null || fail "PUT /users/{id}/pin sin pin → 400 en errors.pin en español (no 'The Pin field is required.')"
expect 200 "$(req PUT "/api/v1/users/$URD6/pin" '{"pin":"5937"}')" >/dev/null
expect 200 "$(req GET /api/v1/users)" | jq -e --argjson u "$UWH6" 'any(.[]; .id==$u and .hasPin==true)' >/dev/null || fail "hasPin=true en la lista de usuarios"
# Sin devices.manage ni admin.users (y sin reauth): la política perm:devices.manage|admin.users rechaza antes que AAL2 → 403
# que no es aal2_required y deja SecurityEvent PERMISSION_DENIED.
R403=$(expect 403 "$(req PUT "/api/v1/users/$UWH6/pin" '{"pin":"4826"}' "$TWH6")") || fail "PIN de otro usuario sin devices.manage ni admin.users → 403"
[[ $(echo "$R403" | jq -r '.code? // empty' 2>/dev/null) != aal2_required ]] || fail "PIN de otro usuario sin permiso → 403 aal2_required (debe rechazar el permiso primero)"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=50')" | jq -e '[.items[] | select((.detailJson // "") | contains("devices.manage|admin.users"))] | length >= 1' >/dev/null || fail "PIN de otro sin permiso no dejó SecurityEvent PERMISSION_DENIED"
# Login por aparato de otro usuario (Operador 6, PIN 4826): la sesión es suya (sub, /me, LastUserId y autoría en AuditLog)
# y lleva sus permisos, no los del admin que registró el aparato (POST /devices → 403).
DW6=$(expect 200 "$(DLOGIN 4826 "$UWH6")"); DTW6=$(echo "$DW6" | jq -r .accessToken)
[[ $(jwtclaims "$DTW6" | jq -r .sub) == "$UWH6" ]] || fail "el token del aparato no es del usuario del PIN"
expect 200 "$(req GET /api/v1/me '' "$DTW6")" | jq -e --argjson u "$UWH6" '.userId==$u' >/dev/null || fail "/me con token de aparato no devuelve al usuario del PIN"
expect 403 "$(req POST /api/v1/devices "{\"code\":\"ZW-$TS\"}" "$DTW6")" >/dev/null || fail "la sesión de aparato de Operador 6 heredó permisos del admin"
expect 200 "$(req GET /api/v1/devices)" | jq -e --arg d "$DEVP" --argjson u "$UWH6" 'any(.[]; .publicId==$d and .lastUserId==$u)' >/dev/null || fail "lastUserId no es el usuario del PIN"
AUW6() { expect 200 "$(req GET "/api/v1/audit/changes?entityType=RECEIPT&userId=$UWH6&take=1")" | jq .total; }
AUW6A=$(AUW6)
RW6=$(expect 200 "$(blind "$W6P" "$B_STG" "$PZ" 1 "$DTW6")")
[[ $(AUW6) -gt $AUW6A ]] || fail "el recibo creado con la sesión de aparato de Operador 6 no quedó a su nombre en AuditLog"
expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$RW6" | jq -r .header.publicId)")" >/dev/null
# Con el módulo WMS_LOTSERIAL apagado la sesión del aparato no se renueva (401 y queda revocada), igual que device/login.
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":false}')" >/dev/null
R_MOD8=$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$(echo "$DW6" | jq -r .refreshToken)\"}")
R_MODL=$(DLOGIN 4826 "$UWH6")
R_MODU=$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}")
R_MODH=$(HB)
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":true}')" >/dev/null
expect 401 "$R_MOD8" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "refresh de una sesión de aparato con WMS_LOTSERIAL apagado → 401"
# Con el módulo apagado tampoco se entra por aparato: device/login y device/users 401; el heartbeat responde isActive=false sin error.
expect 401 "$R_MODL" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "device/login con WMS_LOTSERIAL apagado → 401"
expect 401 "$R_MODU" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "device/users con WMS_LOTSERIAL apagado → 401"
expect 200 "$R_MODH" | jq -e '.isActive==false' >/dev/null || fail "heartbeat con WMS_LOTSERIAL apagado → 200 con isActive=false"
# Restablecer (o cambiar) el PIN cierra las sesiones del usuario en aparatos: el refresh de la sesión abierta con el PIN
# anterior → 401 (SecurityEvent TOKEN_REVOKED pin_changed). Solo toca la cadena de Operador 6.
RW6B=$(expect 200 "$(DLOGIN 4826 "$UWH6")" | jq -r .refreshToken)
expect 200 "$(req PUT "/api/v1/users/$UWH6/pin" '{"pin":"4826"}')" >/dev/null
expect 401 "$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$RW6B\"}")" >/dev/null || fail "restablecer el PIN no cerró la sesión del usuario en el aparato"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=TOKEN_REVOKED&take=50')" | jq -e '([.items[] | select((.detailJson // "") | contains("pin_changed"))] | length >= 1) and ([.items[] | select((.detailJson // "") | contains("tenant_unusable"))] | length >= 1)' >/dev/null || fail "SecurityEvent TOKEN_REVOKED pin_changed y tenant_unusable"
expect 404 "$(req PUT "/api/v1/users/$UID_T3/pin" '{"pin":"4826"}')" | jq -e --arg m "Usuario no encontrado." "$HASM" >/dev/null || fail "PIN de un usuario de otra compañía → 404"
expect 404 "$(req DELETE "/api/v1/users/$UID_T3/pin")" >/dev/null || fail "DELETE PIN de otra compañía → 404"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Aparatos $TS\",\"permissions\":[\"inventory.view\",\"devices.manage\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"aparatos8$TS@teikem.local\",\"fullName\":\"Aparatos 8 $TS\",\"password\":\"$PASS\",\"roles\":[\"Aparatos $TS\"]}")" >/dev/null
TDEV8=$(login "aparatos8$TS@teikem.local" "$PASS")
expect 403 "$(req PUT "/api/v1/users/$ME8/pin" '{"pin":"1470"}' "$TDEV8")" | jq -e '.code=="aal2_required"' >/dev/null || fail "PIN de otro sin reauth reciente → 403 aal2_required"
TDEV8=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}" "$TDEV8")" | jq -r .accessToken)
expect 403 "$(req PUT "/api/v1/users/$ME8/pin" '{"pin":"1470"}' "$TDEV8")" | jq -e --arg m "No puede asignar ni quitar el PIN de un usuario con más permisos que usted." "$HASM" >/dev/null || fail "PIN impuesto a un usuario con más permisos → 403"
expect 403 "$(req DELETE "/api/v1/users/$ME8/pin" '' "$TDEV8")" >/dev/null || fail "quitar el PIN de un usuario con más permisos → 403"
# El límite de privilegios del PIN impuesto por otro se vuelve a comprobar en cada login por aparato y en cada refresh de la
# sesión de aparato (no solo al asignarlo): Aparatos 8 asigna el PIN a un usuario con solo inventory.view (200) y entra;
# si después a ese usuario le suben los permisos (admin.users), el PIN deja de servir (403) y la sesión abierta no se
# renueva (401, revocada). Reasignado por alguien con al menos sus permisos (el admin), vuelve a entrar.
PIMP8="Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta."
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Solo inventario $TS\",\"permissions\":[\"inventory.view\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"pinimp8$TS@teikem.local\",\"fullName\":\"PIN impuesto $TS\",\"password\":\"$PASS\",\"roles\":[\"Solo inventario $TS\"]}")" >/dev/null
UPI8=$(expect 200 "$(req GET /api/v1/users)" | jq -r --arg e "pinimp8$TS@teikem.local" '.[] | select(.email==$e) | .id')
expect 200 "$(req PUT "/api/v1/users/$UPI8/pin" '{"pin":"3829"}' "$TDEV8")" | jq -e '.hasPin==true' >/dev/null || fail "PIN impuesto por devices.manage a un usuario con menos permisos → 200"
RPI8=$(expect 200 "$(DLOGIN 3829 "$UPI8")" | jq -r .refreshToken); [[ -n "$RPI8" && "$RPI8" != null ]] || fail "login por aparato con el PIN impuesto (permisos dentro de los de quien lo asignó)"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")" | jq -r .accessToken)
expect 200 "$(req PUT "/api/v1/users/$UPI8/permissions" '{"permissions":["admin.users"]}')" >/dev/null || fail "conceder admin.users al usuario del PIN impuesto"
expect 403 "$(DLOGIN 3829 "$UPI8")" | jq -e --arg m "$PIMP8" "$HASM" >/dev/null || fail "el PIN impuesto abrió una cuenta que ya tiene más permisos que quien lo asignó"
expect 401 "$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$RPI8\"}")" | jq -e --arg m "$PIMP8" "$HASM" >/dev/null || fail "la sesión de aparato del PIN impuesto se renovó con los permisos nuevos"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=LOGIN&take=50')" | jq -e '[.items[] | select((.detailJson // "") | contains("pin_assigner_lower_privileges"))] | length >= 1' >/dev/null || fail "SecurityEvent LOGIN pin_assigner_lower_privileges"
expect 200 "$(req PUT "/api/v1/users/$UPI8/pin" '{"pin":"3829"}')" >/dev/null
expect 200 "$(DLOGIN 3829 "$UPI8")" >/dev/null || fail "el PIN reasignado por el admin (con al menos sus permisos) no entra"
ok2xx "$(req DELETE "/api/v1/users/$UPI8/pin")" "quitar el PIN del usuario del PIN impuesto"
# La sesión de aparato abierta justo antes queda revocada: PASSWORD_CHANGE action=removed y TOKEN_REVOKED pin_removed.
expect 200 "$(req GET "/api/v1/audit/security-events?eventType=PASSWORD_CHANGE&userId=$UPI8&take=20")" | jq -e '[.items[] | select((.detailJson // "") | contains("\"action\":\"removed\""))] | length >= 1' >/dev/null || fail "SecurityEvent PASSWORD_CHANGE action=removed"
expect 200 "$(req GET "/api/v1/audit/security-events?eventType=TOKEN_REVOKED&userId=$UPI8&take=20")" | jq -e '[.items[] | select((.detailJson // "") | contains("pin_removed"))] | length >= 1' >/dev/null || fail "SecurityEvent TOKEN_REVOKED pin_removed"
# Usuarios del aparato: con PIN e inventory.view, por nombre; sin PIN o sin inventory.view no aparecen; login sin
# inventory.view → 403.
ok2xx "$(req PUT "/api/v1/users/$UCH/pin" '{"pin":"5937"}')" "PIN del chofer (sin inventory.view)"
DU8=$(expect 200 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}")")
echo "$DU8" | jq -e --argjson c "$UCH" 'all(.[]; .userId!=$c)' >/dev/null || fail "device/users incluye a un usuario sin inventory.view"
echo "$DU8" | jq -e --argjson a "$URD6" --argjson b "$UWH6" '([.[].userId] | index($a)) as $i | ([.[].userId] | index($b)) as $j | $i != null and $j != null and $i < $j' >/dev/null || fail "device/users por nombre (Lectura 6 antes que Operador 6): $DU8"
expect 403 "$(DLOGIN 5937 "$UCH")" | jq -e --arg m "Falta el permiso 'inventory.view'." "$HASM" >/dev/null || fail "device/login sin inventory.view → 403"
ok2xx "$(req DELETE "/api/v1/users/$UWH6/pin")" "DELETE /users/{id}/pin"
# Carrera: varios PUT /users/{id}/pin a la vez sobre un usuario sin PIN → cada uno 200 o 409 con el mensaje exacto (el
# choque en UQ_UserPin_User no siempre ocurre, así que no se exige el 409) y una sola fila UserPin.
TMPU=$(mktemp -d); for i in 1 2 3 4 5 6; do req PUT "/api/v1/users/$UWH6/pin" '{"pin":"4826"}' > "$TMPU/$i" & done; wait
for i in 1 2 3 4 5 6; do
  C=$(tail -n1 "$TMPU/$i"); B=$(sed '$d' "$TMPU/$i")
  case "$C" in
    200) ;;
    409) echo "$B" | jq -e --arg m "El PIN del usuario cambió al mismo tiempo en otra sesión; intente de nuevo." "$HASM" >/dev/null || fail "PIN en paralelo: 409 con otro mensaje: $B" ;;
    *) fail "PIN en paralelo → $C: $B" ;;
  esac
done
rm -rf "$TMPU"
if [[ -n "${SMOKE_SQL:-}" ]]; then
  [[ $($SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.UserPin WHERE TenantId = $ME_TID AND UserId = $UWH6;" | tr -dc '0-9') == 1 ]] || fail "PIN en paralelo dejó más de una fila UserPin"
fi
ok2xx "$(req DELETE "/api/v1/users/$UWH6/pin")" "quitar el PIN tras la carrera"
expect 200 "$(req GET /api/v1/users)" | jq -e --argjson u "$UWH6" 'any(.[]; .id==$u and .hasPin==false)' >/dev/null || fail "hasPin=false tras quitar el PIN"
expect 200 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}")" | jq -e --argjson u "$UWH6" 'all(.[]; .userId!=$u)' >/dev/null || fail "device/users incluye a un usuario sin PIN"
# El PIN es por compañía: Operador 6 (miembro de esta compañía, ya sin PIN aquí) con un PIN solo en la compañía de T3 no
# aparece en el aparato y ese PIN no lo abre (401 'PIN incorrecto.'). Se copia el hash del PIN 5937 de Lectura 6: el
# PasswordHasher de Identity no liga el hash al usuario.
PINCO8="PIN por compañía omitido (sin SMOKE_SQL)"
if [[ -n "${SMOKE_SQL:-}" ]]; then
  T3_TID=$(expect 200 "$(req GET /api/v1/me '' "$T3D")" | jq -r .tenantId)
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; INSERT dbo.UserPin (TenantId, UserId, PinHash, FailedCount) SELECT $T3_TID, $UWH6, PinHash, 0 FROM dbo.UserPin WHERE UserId = $URD6 AND TenantId = $ME_TID;" >/dev/null
  expect 200 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}")" | jq -e --argjson u "$UWH6" 'all(.[]; .userId!=$u)' >/dev/null || fail "device/users incluye a un usuario con PIN solo en otra compañía"
  expect 401 "$(DLOGIN 5937 "$UWH6")" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "el PIN definido en otra compañía abre el aparato de esta"
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; DELETE dbo.UserPin WHERE TenantId = $T3_TID AND UserId = $UWH6;" >/dev/null
  PINCO8="un PIN definido en otra compañía no lista al usuario ni abre el aparato"
fi
# Con PIN e inventory.view pero con la membresía suspendida o el usuario desactivado: fuera de la lista y login 401 (el mismo
# 'PIN incorrecto.', sin enumeración); al restaurarlo vuelve a aparecer. Se usa el usuario "Aparatos 8" (su token ya no se
# usa: desactivarlo cambia su sello de seguridad).
UDV8=$(expect 200 "$(req GET /api/v1/users)" | jq -r --arg e "aparatos8$TS@teikem.local" '.[] | select(.email==$e) | .id')
expect 200 "$(req PUT "/api/v1/users/$UDV8/pin" '{"pin":"6048"}')" >/dev/null
DUSERS() { anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}"; }
expect 200 "$(DUSERS)" | jq -e --argjson u "$UDV8" 'any(.[]; .userId==$u and (.initials | length) > 0)' >/dev/null || fail "device/users no incluye al usuario con PIN e inventory.view"
expect 200 "$(req PUT "/api/v1/users/$UDV8/membership" '{"status":"SUSPENDED"}')" >/dev/null
expect 200 "$(DUSERS)" | jq -e --argjson u "$UDV8" 'all(.[]; .userId!=$u)' >/dev/null || fail "device/users incluye a un usuario con membresía suspendida"
expect 401 "$(DLOGIN 6048 "$UDV8")" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "device/login con membresía suspendida → 401"
expect 200 "$(req PUT "/api/v1/users/$UDV8/membership" '{"status":"ACTIVE"}')" >/dev/null
expect 200 "$(req PUT "/api/v1/users/$UDV8" '{"isActive":false}')" >/dev/null
expect 200 "$(DUSERS)" | jq -e --argjson u "$UDV8" 'all(.[]; .userId!=$u)' >/dev/null || fail "device/users incluye a un usuario desactivado"
expect 401 "$(DLOGIN 6048 "$UDV8")" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "device/login de un usuario desactivado → 401"
expect 200 "$(req PUT "/api/v1/users/$UDV8" '{"isActive":true}')" >/dev/null
expect 200 "$(DUSERS)" | jq -e --argjson u "$UDV8" 'any(.[]; .userId==$u)' >/dev/null || fail "device/users no devuelve al usuario restaurado"
ok2xx "$(req DELETE "/api/v1/users/$UDV8/pin")" "quitar el PIN de Aparatos 8"
# Idempotencia: misma clave y mismo cuerpo → mismo REC con Idempotent-Replayed; misma clave con otro cuerpo → 409.
KEY8="smoke-$TS-rec"
BODY8=$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PZ" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:2}]}')
R8=$(expect 200 "$(idem POST /api/v1/receipts "$KEY8" "$BODY8")"); N8=$(echo "$R8" | jq -r .header.number)
replayed && fail "la primera llamada no debe marcarse como repetida"
[[ $(expect 200 "$(idem POST /api/v1/receipts "$KEY8" "$BODY8")" | jq -r .header.number) == "$N8" ]] || fail "la repetición con la misma clave creó otro recibo"
replayed || fail "la repetición no trae Idempotent-Replayed: true"
# La repetición vuelve a aplicar [RequireModule]: con WMS_LOTSERIAL apagado la misma clave responde 403 (sin repetir).
TOKEN=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")" | jq -r .accessToken)
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":false}')" >/dev/null || fail "apagar WMS_LOTSERIAL"
expect 403 "$(idem POST /api/v1/receipts "$KEY8" "$BODY8")" | jq -e '.code=="module_disabled"' >/dev/null || fail "repetición con el módulo apagado → 403 module_disabled"
replayed && fail "la repetición con el módulo apagado no debe traer Idempotent-Replayed"
expect 200 "$(req PUT /api/v1/modules/WMS_LOTSERIAL '{"isEnabled":true}')" >/dev/null || fail "volver a encender WMS_LOTSERIAL"
[[ $(expect 200 "$(idem POST /api/v1/receipts "$KEY8" "$BODY8")" | jq -r .header.number) == "$N8" ]] || fail "con el módulo encendido de nuevo la repetición no devolvió el mismo recibo"
replayed || fail "la repetición tras reencender el módulo no trae Idempotent-Replayed: true"
# La repetición vuelve a aplicar [RequireAal2]: la misma clave con un token del mismo usuario sin AAL2 (login con
# contraseña) → 403 aal2_required, sin Idempotent-Replayed (la clave es por usuario y la huella no cubre el token).
TNOAAL=$(login "$EMAIL" "$PASS")
TOKEN=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")" | jq -r .accessToken)
KA8="smoke-$TS-aal2"; BA8='{"isEnabled":true}'
expect 200 "$(idem PUT /api/v1/modules/WMS_LOTSERIAL "$KA8" "$BA8" "$TOKEN")" >/dev/null || fail "PUT de módulo con Idempotency-Key → 200"
expect 403 "$(idem PUT /api/v1/modules/WMS_LOTSERIAL "$KA8" "$BA8" "$TNOAAL")" | jq -e '.code=="aal2_required"' >/dev/null || fail "repetición sin AAL2 → 403 aal2_required"
replayed && fail "la repetición sin AAL2 no debe traer Idempotent-Replayed"
expect 409 "$(idem POST /api/v1/receipts "$KEY8" "$(echo "$BODY8" | jq -c '.lines[0].receivedQty=3')")" | jq -e --arg m "La clave de idempotencia ya se usó con otro contenido." "$HASM" >/dev/null || fail "misma clave con otro cuerpo → 409"
# La clave es por usuario (TenantId, UserId, clave): la misma clave y el mismo cuerpo de otro usuario crean otro recibo.
TWH8=$(login "bodega6$TS@teikem.local" "$PASS")
R8B=$(expect 200 "$(idem POST /api/v1/receipts "$KEY8" "$BODY8" "$TWH8")")
replayed && fail "la misma clave de otro usuario se trató como repetición (la clave es por usuario)"
[[ $(echo "$R8B" | jq -r .header.number) != "$N8" ]] || fail "otro usuario con la misma clave recibió el recibo del primero"
expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$R8B" | jq -r .header.publicId)")" >/dev/null
expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$R8" | jq -r .header.publicId)")" >/dev/null
# Carrera: la misma clave y el mismo cuerpo a la vez → un solo REC; el resto recibe Replay (mismo número) o 409 en vuelo.
KRC="smoke-$TS-race"; TMPI=$(mktemp -d)
for i in 1 2 3 4; do idem POST /api/v1/receipts "$KRC" "$BODY8" > "$TMPI/$i" & done; wait
NRC=""; PRC=""
for i in 1 2 3 4; do
  C=$(tail -n1 "$TMPI/$i"); B=$(sed '$d' "$TMPI/$i")
  case "$C" in
    200) N=$(echo "$B" | jq -r .header.number); [[ -z "$NRC" || "$NRC" == "$N" ]] || fail "la carrera con la misma clave creó dos recibos ($NRC y $N)"; NRC=$N; PRC=$(echo "$B" | jq -r .header.publicId) ;;
    409) echo "$B" | jq -e --arg m "La operación con esta clave todavía se está procesando." "$HASM" >/dev/null || fail "carrera: 409 con otro mensaje: $B" ;;
    *) fail "carrera con la misma clave → $C: $B" ;;
  esac
done
rm -rf "$TMPI"
[[ -n "$NRC" ]] || fail "carrera: ninguna petición obtuvo 200"
# No se consumió otro número REC: el siguiente recibo es NRC+1.
RNX=$(expect 200 "$(blind "$W6P" "$B_STG" "$PZ" 1)")
[[ $((10#$(echo "$RNX" | jq -r .header.number | tr -dc '0-9'))) -eq $((10#$(echo "$NRC" | tr -dc '0-9') + 1)) ]] || fail "la carrera con la misma clave consumió más de un número REC ($NRC → $(echo "$RNX" | jq -r .header.number))"
for P in "$PRC" "$(echo "$RNX" | jq -r .header.publicId)"; do expect 204 "$(req DELETE "/api/v1/receipts/$P")" >/dev/null; done
# DELETE con respuesta vacía (204) y PATCH con rowVersion, repetidos con la misma clave: la repetición devuelve lo guardado
# (204 sin cuerpo; 200 con el mismo cuerpo) con Idempotent-Replayed, no 404 por el recibo ya eliminado ni 409 por la
# rowVersion ya usada.
RDEL=$(expect 200 "$(blind "$W6P" "$B_STG" "$PZ" 1)" | jq -r .header.publicId)
DEL8=$(idem DELETE "/api/v1/receipts/$RDEL" "smoke-$TS-del" "")
expect 204 "$DEL8" >/dev/null || fail "DELETE con Idempotency-Key → 204"
replayed && fail "el primer DELETE no debe marcarse como repetido"
DEL8B=$(idem DELETE "/api/v1/receipts/$RDEL" "smoke-$TS-del" "")
DEL8BB=$(expect 204 "$DEL8B") || fail "DELETE repetido con la misma clave → 204 (no 404)"
[[ -z "$DEL8BB" ]] || fail "DELETE repetido: debe ser 204 sin cuerpo: $DEL8BB"
replayed || fail "DELETE repetido sin Idempotent-Replayed"
RVZ=$(expect 200 "$(req GET "/api/v1/products/$PZ")" | jq -r .rowVersion)
BPZ=$(jq -cn --arg rv "$RVZ" --arg n "Aparato $TS (8A)" '{name:$n,rowVersion:$rv}')
PA8=$(expect 200 "$(idem PATCH "/api/v1/products/$PZ" "smoke-$TS-patch" "$BPZ" "$TOKEN")") || fail "PATCH con Idempotency-Key → 200"
replayed && fail "el primer PATCH no debe marcarse como repetido"
PA8B=$(expect 200 "$(idem PATCH "/api/v1/products/$PZ" "smoke-$TS-patch" "$BPZ" "$TOKEN")") || fail "PATCH repetido con la misma clave → 409 por rowVersion (se volvió a ejecutar)"
replayed || fail "PATCH repetido sin Idempotent-Replayed"
[[ $(echo "$PA8" | jq -cS .) == $(echo "$PA8B" | jq -cS .) ]] || fail "PATCH repetido con otro cuerpo de respuesta"
# Con otro nombre para que EF emita el UPDATE (un PATCH sin cambios no revisa la concurrencia).
BPZ2=$(jq -cn --arg rv "$RVZ" --arg n "Aparato $TS (8A-bis)" '{name:$n,rowVersion:$rv}')
expect 409 "$(req PATCH "/api/v1/products/$PZ" "$BPZ2")" >/dev/null || fail "el PATCH sin clave con la rowVersion usada debe dar 409"
# Ramas de la idempotencia que dependen de la BD (con SMOKE_SQL): registro sin respuesta (en vuelo) → 409; registro de más
# de 7 días → la operación se vuelve a ejecutar (sin Idempotent-Replayed) y los vencidos se borran perezosamente al insertar.
# QUOTED_IDENTIFIER ON: sqlcmd lo apaga por defecto y la tabla tiene un índice filtrado (UX_IntegrationLog_Idem).
IDEM8="ramas en vuelo y vencida omitidas (sin SMOKE_SQL)"
if [[ -n "${SMOKE_SQL:-}" ]]; then
  KIF="smoke-$TS-inflight"
  RIF=$(expect 200 "$(idem POST /api/v1/receipts "$KIF" "$BODY8")")
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; UPDATE dbo.IntegrationMessageLog SET ResponseCode=NULL, ResponseJson=NULL WHERE IdempotencyKey='$KIF' AND UserId=$ME8;" >/dev/null
  expect 409 "$(idem POST /api/v1/receipts "$KIF" "$BODY8")" | jq -e --arg m "La operación con esta clave todavía se está procesando." "$HASM" >/dev/null || fail "clave en vuelo → 409"
  $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; UPDATE dbo.IntegrationMessageLog SET CreatedAtUtc=DATEADD(day,-8,SYSUTCDATETIME()) WHERE IdempotencyKey IN ('$KEY8','$KIF') AND UserId=$ME8;" >/dev/null
  R8X=$(expect 200 "$(idem POST /api/v1/receipts "$KEY8" "$BODY8")")
  replayed && fail "una clave de más de 7 días se trató como repetición"
  [[ $(echo "$R8X" | jq -r .header.number) != "$N8" ]] || fail "la clave vencida devolvió el recibo guardado"
  [[ $($SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.IntegrationMessageLog WHERE IdempotencyKey='$KIF' AND UserId=$ME8;" | tr -d '[:space:]') == 0 ]] || fail "el registro de idempotencia vencido no se borró al insertar"
  for RX in "$R8X" "$RIF"; do expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$RX" | jq -r .header.publicId)")" >/dev/null; done
  IDEM8="clave en vuelo 409, clave de más de 7 días se vuelve a ejecutar y los vencidos se borran al insertar"
fi
# Un rechazo que NO se guarda (403 del servicio: recibo contra OC sin purchasing.receive) libera la clave: tras conceder el
# permiso, el reintento con la misma clave y el mismo cuerpo se EJECUTA (200 sin Idempotent-Replayed, no 409 en vuelo).
PO8R=$(po "[{\"productPublicId\":\"$PZ\",\"qtyOrdered\":3,\"unitCost\":1}]"); PO8RP=$(pid "$PO8R")
expect 200 "$(req POST "/api/v1/purchase-orders/$PO8RP/send" '{}')" >/dev/null || fail "enviar la OC del paso de clave liberada"
ROL8R=$(expect 200 "$(req POST /api/v1/roles "{\"name\":\"Recibos sin compras $TS\",\"permissions\":[\"inventory.view\",\"purchasing.view\",\"warehouse.receive\"]}")" | jq -r .id)
expect 200 "$(req POST /api/v1/users "{\"email\":\"recibos8$TS@teikem.local\",\"fullName\":\"Recibos 8 $TS\",\"password\":\"$PASS\",\"roles\":[\"Recibos sin compras $TS\"]}")" >/dev/null
TRC8=$(login "recibos8$TS@teikem.local" "$PASS"); URC8=$(expect 200 "$(req GET /api/v1/me '' "$TRC8")" | jq -r .userId)
KRL="smoke-$TS-release"; BRL=$(jq -cn --arg o "$PO8RP" --argjson s "$B_STG" '{purchaseOrderPublicId:$o,stagingBinId:$s}')
expect 403 "$(idem POST /api/v1/receipts "$KRL" "$BRL" "$TRC8")" | denied purchasing.receive || fail "recibo contra OC sin purchasing.receive con clave → 403"
replayed && fail "el 403 con clave no debe marcarse como repetido"
[[ -z "${SMOKE_SQL:-}" ]] || [[ $($SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.IntegrationMessageLog WHERE IdempotencyKey='$KRL' AND UserId=$URC8;" | tr -d '[:space:]') == 0 ]] || fail "el 403 dejó la clave de idempotencia registrada"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")" | jq -r .accessToken)
expect 200 "$(req PUT "/api/v1/roles/$ROL8R" "{\"name\":\"Recibos sin compras $TS\",\"permissions\":[\"inventory.view\",\"purchasing.view\",\"warehouse.receive\",\"purchasing.receive\"]}")" >/dev/null || fail "conceder purchasing.receive al rol"
RRL=$(expect 200 "$(idem POST /api/v1/receipts "$KRL" "$BRL" "$TRC8")") || fail "tras el 403 la misma clave no se liberó (el reintento no se ejecutó)"
replayed && fail "el reintento tras un 403 no guardado llegó como Idempotent-Replayed"
expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$RRL" | jq -r .header.publicId)")" >/dev/null
expect 200 "$(req POST "/api/v1/purchase-orders/$PO8RP/cancel" '{"comment":"smoke"}')" >/dev/null || fail "cancelar la OC del paso de clave liberada"
# Sincronización por diferencia con el token del aparato: el producto del paso aparece (paginando por cursor si hace falta).
FOUND8=false; CUR8=""
while :; do
  PG8=$(expect 200 "$(req GET "/api/v1/sync/products?since=$SINCE8&take=500${CUR8:+&cursor=$CUR8}" '' "$DT")")
  echo "$PG8" | jq -e --arg p "$PZ" --arg b "ZBC$TS" 'any(.items[]; .publicId==$p and .barcode==$b and .isActive==true)' >/dev/null && { FOUND8=true; break; }
  CUR8=$(echo "$PG8" | jq -r '.nextCursor // empty'); [[ -n "$CUR8" ]] || break
done
[[ $FOUND8 == true ]] || fail "sync/products desde hace 1 h no trae PZ$TS"
echo "$PG8" | jq -e '.serverTimeUtc != null' >/dev/null || fail "sync sin serverTimeUtc"
expect 400 "$(req GET '/api/v1/sync/products?take=501' '' "$DT")" | jq -e --arg m "El máximo por página es 500." "$HASM" >/dev/null || fail "take > 500 → 400"
expect 400 "$(req GET '/api/v1/sync/products?cursor=zzz' '' "$DT")" | jq -e --arg m "El cursor no es válido." "$HASM" >/dev/null || fail "cursor inválido → 400"
expect 200 "$(req GET "/api/v1/sync/bins?warehousePublicId=$W6P" '' "$DT")" | jq -e --argjson b "$B_STG" 'any(.items[]; .id==$b and .zoneCode=="STG" and .zoneTypeCode=="STAGING" and .isActive==true)' >/dev/null || fail "sync/bins con su zona"
for R in purchase-orders asns warehouse-tasks product-categories; do expect 200 "$(req GET "/api/v1/sync/$R?since=$SINCE8" '' "$DT")" | jq -e '.items | type=="array"' >/dev/null || fail "sync/$R"; done
# Las órdenes de compra conservan la defensa del recurso nativo: Solo lectura (inventory.view sin purchasing.view) → 403.
expect 403 "$(req GET "/api/v1/sync/purchase-orders" '' "$TREAD6")" >/dev/null || fail "sync/purchase-orders sin purchasing.view → 403"
expect 200 "$(req GET "/api/v1/sync/products?take=1" '' "$TREAD6")" >/dev/null || fail "sync/products con inventory.view"
# Código escaneado: código de barras o SKU exactos; inexistente → 404 con el mensaje exacto.
expect 200 "$(req GET "/api/v1/products/by-barcode/PZ$TS" '' "$DT")" | jq -e --arg p "$PZ" '.product.publicId==$p' >/dev/null || fail "by-barcode por SKU"
expect 200 "$(req GET "/api/v1/products/by-barcode/ZBC$TS" '' "$DT")" | jq -e --arg p "$PZ" '.product.publicId==$p' >/dev/null || fail "by-barcode por código de barras"
expect 404 "$(req GET "/api/v1/products/by-barcode/NOEXISTE$TS" '' "$DT")" | jq -e --arg m "No hay un producto con ese código." "$HASM" >/dev/null || fail "by-barcode inexistente → 404"
# Recibo en una llamada (cola del aparato): crea, captura y confirma → RECEIVED con su PUTAWAY.
RC8=$(expect 200 "$(idem POST /api/v1/receipts "smoke-$TS-rec2" "$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PZ" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:4}],confirm:true}')")")
echo "$RC8" | jq -e '.header.statusCode=="RECEIVED" and .header.receivedAtUtc!=null and (.putawayTasks | length)==1 and .putawayTasks[0].quantity==4' >/dev/null || fail "recibo confirmado en una llamada: $(echo "$RC8" | jq -c .header)"
# Atomicidad: si la confirmación falla (serie sin capturar) no queda el recibo ni se consume el número REC.
N_ANTES=$(echo "$RC8" | jq -r .header.number)
expect 400 "$(req POST /api/v1/receipts "$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PS" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:1}],confirm:true}')" "$DT")" | jq -e --arg m "El producto PS$TS se controla por serie: capture 1 número(s) de serie (hay 0)." "$HASM" >/dev/null || fail "recibo en una llamada con serie sin capturar → 400"
RN8=$(expect 200 "$(blind "$W6P" "$B_STG" "$PZ" 1)")
[[ $((10#$(echo "$RN8" | jq -r .header.number | tr -dc '0-9'))) -eq $((10#$(echo "$N_ANTES" | tr -dc '0-9') + 1)) ]] || fail "el recibo fallido consumió un número REC ($N_ANTES → $(echo "$RN8" | jq -r .header.number))"
expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$RN8" | jq -r .header.publicId)")" >/dev/null
# Un rechazo de negocio con clave se guarda y se repite igual (Idempotent-Replayed); una clave inválida da 400.
BAD8=$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PS" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:1}],confirm:true}')
MSG8="El producto PS$TS se controla por serie: capture 1 número(s) de serie (hay 0)."
expect 400 "$(idem POST /api/v1/receipts "smoke-$TS-rej" "$BAD8")" | jq -e --arg m "$MSG8" "$HASM" >/dev/null || fail "rechazo con clave → 400"
replayed && fail "el primer rechazo no debe marcarse como repetido"
expect 400 "$(idem POST /api/v1/receipts "smoke-$TS-rej" "$BAD8")" | jq -e --arg m "$MSG8" "$HASM" >/dev/null || fail "el rechazo repetido no devuelve el mismo 400"
replayed || fail "el rechazo repetido no trae Idempotent-Replayed: true"
# La clave distingue mayúsculas (collation binaria de IntegrationMessageLog.IdempotencyKey): la misma clave en mayúsculas con
# otro cuerpo es otra clave → se ejecuta (400 del negocio), no 409 'ya se usó con otro contenido' ni repetición.
expect 400 "$(idem POST /api/v1/receipts "SMOKE-$TS-REJ" "$(echo "$BAD8" | jq -c '.lines[0].receivedQty=2')")" >/dev/null || fail "la clave en mayúsculas se trató como la misma (409): la clave debe distinguir mayúsculas"
replayed && fail "la clave en mayúsculas llegó como Idempotent-Replayed"
expect 400 "$(idem POST /api/v1/receipts "$(printf 'k%.0s' {1..81})" "$BAD8")" | jq -e --arg m "La clave de idempotencia no es válida." "$HASM" >/dev/null || fail "clave de 81 caracteres → 400"
expect 400 "$(curl -sS -X POST "$BASE/api/v1/receipts" -H 'Accept: application/json' -H 'Content-Type: application/json' -H "Authorization: Bearer $DT" -H 'Idempotency-Key;' --data "$BAD8" -w '\n%{http_code}')" | jq -e --arg m "La clave de idempotencia no es válida." "$HASM" >/dev/null || fail "clave vacía → 400"
expect 200 "$(req POST "/api/v1/warehouse-tasks/$(echo "$RC8" | jq -r '.putawayTasks[0].id')/complete" '{}' "$DT")" >/dev/null
# Decisión 18: el cliente se desconecta a mitad de la operación; la operación termina y el reintento recibe Replay sin
# duplicar (una sola entrada de inventario y un solo número REC).
KAB="smoke-$TS-abort"; QAB=$(onhandall "$W6P" "$PZ")
BAB=$(jq -cn --arg w "$W6P" --argjson s "$B_STG" --arg p "$PZ" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:5}],confirm:true}')
curl -sS -o /dev/null --max-time 0.1 -X POST "$BASE/api/v1/receipts" -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $DT" -H "Idempotency-Key: $KAB" --data "$BAB" 2>/dev/null || true
for i in $(seq 1 50); do RAB=$(idem POST /api/v1/receipts "$KAB" "$BAB"); [[ $(echo "$RAB" | tail -n1) == 409 ]] || break; sleep 0.2; done
RAB=$(expect 200 "$RAB"); replayed || fail "tras la desconexión el reintento no trae Idempotent-Replayed: true (se volvió a ejecutar)"
[[ $(onhandall "$W6P" "$PZ") == $((QAB + 5)) ]] || fail "la desconexión con Idempotency-Key duplicó la entrada de inventario"
RNX=$(expect 200 "$(blind "$W6P" "$B_STG" "$PZ" 1)")
[[ $((10#$(echo "$RNX" | jq -r .header.number | tr -dc '0-9'))) -eq $((10#$(echo "$RAB" | jq -r .header.number | tr -dc '0-9') + 1)) ]] || fail "la desconexión consumió más de un número REC"
expect 204 "$(req DELETE "/api/v1/receipts/$(echo "$RNX" | jq -r .header.publicId)")" >/dev/null
for T in $(echo "$RAB" | jq -r '.putawayTasks[].id'); do expect 200 "$(req POST "/api/v1/warehouse-tasks/$T/complete" '{}' "$DT")" >/dev/null; done
# Contra una orden de compra, en una llamada: lo escaneado (3 de 10) manda sobre lo esperado; la orden queda PARTIAL y el
# recibo, con diferencia, en RECEIVED_VARIANCE (Lote 13).
PO8=$(po "[{\"productPublicId\":\"$PZ\",\"qtyOrdered\":10,\"unitCost\":1}]"); PO8P=$(pid "$PO8")
expect 200 "$(req POST "/api/v1/purchase-orders/$PO8P/send" '{}')" >/dev/null
RP8=$(expect 200 "$(idem POST /api/v1/receipts "smoke-$TS-rec-po" "$(jq -cn --arg o "$PO8P" --argjson s "$B_STG" --arg p "$PZ" '{purchaseOrderPublicId:$o,stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:3}],confirm:true}')")")
echo "$RP8" | jq -e '.header.statusCode=="RECEIVED_VARIANCE" and (.lines | length)==1 and .lines[0].expectedQty==10 and .lines[0].receivedQty==3' >/dev/null || fail "recibo contra OC en una llamada con lo escaneado: $(echo "$RP8" | jq -c '[.header.statusCode, [.lines[] | {expectedQty, receivedQty}]]')"
expect 200 "$(req GET "/api/v1/purchase-orders/$PO8P")" | jq -e '.statusCode=="PARTIAL" and .lines[0].qtyReceived==3' >/dev/null || fail "la OC no quedó PARTIAL con 3 recibidos"
for T8 in $(echo "$RP8" | jq -r '.putawayTasks[].id'); do expect 200 "$(req POST "/api/v1/warehouse-tasks/$T8/complete" '{}' "$DT")" >/dev/null; done
# Segunda recepción parcial (PARTIAL→PARTIAL: sin historial de estatus ni auditoría del encabezado): la diferencia desde antes
# de recibir trae la OC con el pendiente nuevo (la detecta la rama de sus líneas).
S8PO=$(expect 200 "$(req GET '/api/v1/sync/products?take=1' '' "$DT")" | jq -r .serverTimeUtc)
RP8B=$(expect 200 "$(req POST /api/v1/receipts "$(jq -cn --arg o "$PO8P" --argjson s "$B_STG" --arg p "$PZ" '{purchaseOrderPublicId:$o,stagingBinId:$s,lines:[{productPublicId:$p,receivedQty:2}],confirm:true}')")")
expect 200 "$(req GET "/api/v1/purchase-orders/$PO8P")" | jq -e '.statusCode=="PARTIAL" and .lines[0].qtyReceived==5' >/dev/null || fail "segunda recepción parcial de PO8 no quedó PARTIAL con 5 recibidos"
expect 200 "$(req GET "/api/v1/sync/purchase-orders?since=$(jq -rn --arg s "$S8PO" '$s|@uri')&warehousePublicId=$W6P&take=500" '' "$DT")" | jq -e --arg p "$PO8P" 'any(.items[]; .publicId==$p and .isActive==true and .lines[0].qtyReceived==5 and .lines[0].qtyPending==5)' >/dev/null || fail "sync/purchase-orders no trae la OC cuya línea cambió sin cambiar de estatus (PARTIAL→PARTIAL)"
for T8 in $(echo "$RP8B" | jq -r '.putawayTasks[].id'); do expect 200 "$(req POST "/api/v1/warehouse-tasks/$T8/complete" '{}' "$DT")" >/dev/null; done
# Recolectar y empacar en una llamada (collect-and-pack): PACKED con su orden EMP, idempotente; 'pack' en POST /pick-batches
# → 400; un empaque que falla después de recolectar no deja recolección ni saca inventario.
CP8=$(jq -cn --arg w "$W6P" --arg p "$PZ" --argjson pk "$(packbody)" '{warehousePublicId:$w,lines:[{productPublicId:$p,quantity:1}],pack:$pk}')
expect 400 "$(req POST /api/v1/pick-batches "$CP8")" | jq -e --arg m "Para recolectar y empacar en una llamada use POST /api/v1/pick-batches/collect-and-pack." "$HASM" >/dev/null || fail "'pack' en POST /pick-batches → 400"
PK8=$(expect 200 "$(idem POST /api/v1/pick-batches/collect-and-pack "smoke-$TS-pk" "$CP8" "$TOKEN")")
echo "$PK8" | jq -e '.batch.statusCode=="PACKED" and .order.packBatchNumber==.batch.number and .order.publicId!=null' >/dev/null || fail "recolectar y empacar en una llamada: $PK8"
[[ $(expect 200 "$(idem POST /api/v1/pick-batches/collect-and-pack "smoke-$TS-pk" "$CP8" "$TOKEN")" | jq -r .order.publicId) == $(echo "$PK8" | jq -r .order.publicId) ]] || fail "la repetición de recolectar y empacar creó otra orden"
replayed || fail "recolectar y empacar repetido sin Idempotent-Replayed"
N8B=$(expect 200 "$(req GET "/api/v1/pick-batches?productPublicIds=$PZ&includeDeleted=true")" | jq .total)
Q8B=$(onhandall "$W6P" "$PZ")
expect 404 "$(req POST /api/v1/pick-batches/collect-and-pack "$(echo "$CP8" | jq -c '.pack.order.clientPublicId="00000000-0000-0000-0000-000000000000"')")" | jq -e --arg m "Cliente no encontrado." "$HASM" >/dev/null || fail "empaque con cliente inexistente → 404"
[[ $(expect 200 "$(req GET "/api/v1/pick-batches?productPublicIds=$PZ&includeDeleted=true")" | jq .total) == "$N8B" ]] || fail "el empaque fallido dejó la recolección"
[[ $(onhandall "$W6P" "$PZ") == "$Q8B" ]] || fail "el empaque fallido sacó inventario"
# El empaque fallido tampoco consumió el número EMP: la siguiente recolección es la del empaque correcto + 1.
N8E=$(echo "$PK8" | jq -r .batch.number | sed -E 's/^EMP-0*//')
PB8N=$(expect 200 "$(collect "$W6P" "$PZ" 1)")
[[ $(echo "$PB8N" | jq -r .number | sed -E 's/^EMP-0*//') -eq $((N8E+1)) ]] || fail "el empaque fallido consumió número EMP ($N8E → $(echo "$PB8N" | jq -r .number))"
expect 204 "$(req DELETE "/api/v1/pick-batches/$(echo "$PB8N" | jq -r .publicId)" '{}')" >/dev/null
expect 204 "$(req DELETE "/api/v1/pick-batches/$(echo "$PK8" | jq -r .batch.publicId)")" >/dev/null
# Conteo a ciegas (sin warehouse.count) y captura en lote (lo encontrado se agrega); renglón repetido → 400.
CC8=$(expect 200 "$(req POST /api/v1/cycle-counts "{\"warehousePublicId\":\"$W6P\",\"binIds\":[$B_CC]}")"); CC8ID=$(echo "$CC8" | jq -r .count.id); L8=$(echo "$CC8" | jq -r '.lines[0].id')
expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID" '' "$TREAD6")" | jq -e '.isBlind==true and (.lines | length) >= 1 and all(.lines[]; .systemQty==null and .currentQty==null and .varianceQty==null) and .count.varianceLines==null and .count.netVariance==null' >/dev/null || fail "conteo a ciegas para Solo lectura"
expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID")" | jq -e '.isBlind==false and all(.lines[]; .systemQty!=null)' >/dev/null || fail "ficha informada con warehouse.count"
expect 403 "$(req PUT "/api/v1/cycle-counts/$CC8ID/lines/batch" "{\"lines\":[{\"lineId\":$L8,\"countedQty\":1}]}" "$TREAD6")" >/dev/null || fail "Solo lectura no captura (sin warehouse.count.capture)"
BATCH8="{\"lines\":[{\"lineId\":$L8,\"countedQty\":1},{\"binId\":$B_CC,\"productPublicId\":\"$PZ\",\"countedQty\":2}]}"
expect 200 "$(idem PUT "/api/v1/cycle-counts/$CC8ID/lines/batch" "smoke-$TS-cc" "$BATCH8")" | jq -e --arg p "$PZ" --argjson l "$L8" 'any(.lines[]; .id==$l and .countedQty==1) and any(.lines[]; .productPublicId==$p and .countedQty==2)' >/dev/null || fail "captura en lote con línea encontrada"
expect 200 "$(idem PUT "/api/v1/cycle-counts/$CC8ID/lines/batch" "smoke-$TS-cc" "$BATCH8")" >/dev/null; replayed || fail "captura en lote repetida sin Idempotent-Replayed"
# A ciegas onlyVariance se ignora (filtrar por diferencia revelaría lo esperado): Solo lectura recibe todas las líneas; con
# warehouse.count el filtro sí aplica (hay al menos una línea con diferencia: la encontrada, 0 → 2).
NB8=$(expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID" '' "$TREAD6")" | jq '.lines | length')
expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID?onlyVariance=true" '' "$TREAD6")" | jq -e --argjson n "$NB8" '.isBlind==true and (.lines | length)==$n and all(.lines[]; .systemQty==null and .varianceQty==null)' >/dev/null || fail "onlyVariance a ciegas filtró líneas (revela lo esperado)"
# Con warehouse.count el filtro quita algo (menos líneas que las $NB8 del conteo) y deja solo líneas contadas con diferencia
# (varianceQty != null excluye las que siguen sin contar; != 0 las contadas sin diferencia).
expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID?onlyVariance=true")" | jq -e --argjson n "$NB8" '.isBlind==false and (.lines | length) >= 1 and (.lines | length) < $n and all(.lines[]; .varianceQty != null and .varianceQty != 0)' >/dev/null || fail "onlyVariance con warehouse.count"
# Lote inválido → 400/404 sin guardar nada: el primer renglón (L8 → 7) no se aplica y no se agregan líneas.
N8L=$(expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID")" | jq '.lines | length')
expect 400 "$(req PUT "/api/v1/cycle-counts/$CC8ID/lines/batch" "{\"lines\":[{\"lineId\":$L8,\"countedQty\":7},{\"lineId\":$L8,\"countedQty\":2}]}" "$DT")" | jq -e --arg m "La línea se repite en la solicitud." "$HASM" >/dev/null || fail "renglón repetido en el lote → 400"
expect 404 "$(req PUT "/api/v1/cycle-counts/$CC8ID/lines/batch" "{\"lines\":[{\"lineId\":$L8,\"countedQty\":7},{\"binId\":999999999,\"productPublicId\":\"$PZ\",\"countedQty\":1}]}" "$DT")" | jq -e --arg m "Posición no encontrada." "$HASM" >/dev/null || fail "renglón con posición inexistente en el lote → 404"
# Un lote creado por número en un renglón (producto con lote PL) se revierte si otro renglón falla: el conteo no cambia y el
# lote LCC8 no queda creado (repetirlo con otras fechas daría 409 si existiera; se agrega con 200 y se ve su número).
expect 404 "$(req PUT "/api/v1/cycle-counts/$CC8ID/lines/batch" "{\"lines\":[{\"binId\":$B_CC,\"productPublicId\":\"$PL\",\"lot\":{\"number\":\"LCC8$TS\",\"expiryDate\":\"2030-01-31\"},\"countedQty\":1},{\"binId\":999999999,\"productPublicId\":\"$PZ\",\"countedQty\":1}]}" "$DT")" | jq -e --arg m "Posición no encontrada." "$HASM" >/dev/null || fail "renglón de lote nuevo con otro renglón inválido → 404"
expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID")" | jq -e --argjson n "$N8L" '(.lines | length)==$n' >/dev/null || fail "el lote inválido agregó la línea del lote nuevo"
expect 200 "$(req PUT "/api/v1/cycle-counts/$CC8ID/lines/batch" "{\"lines\":[{\"binId\":$B_CC,\"productPublicId\":\"$PL\",\"lot\":{\"number\":\"LCC8$TS\",\"expiryDate\":\"2031-06-30\"},\"countedQty\":1}]}" "$DT")" | jq -e --arg l "LCC8$TS" 'any(.lines[]; .lotNumber==$l and .countedQty==1)' >/dev/null || fail "el lote creado en el renglón revertido quedó creado (otras fechas → 409)"
N8L=$((N8L+1))
expect 200 "$(req GET "/api/v1/cycle-counts/$CC8ID")" | jq -e --argjson l "$L8" --argjson n "$N8L" 'any(.lines[]; .id==$l and .countedQty==1) and (.lines | length)==$n' >/dev/null || fail "el lote inválido guardó algo"
expect 204 "$(req DELETE "/api/v1/cycle-counts/$CC8ID")" >/dev/null
# La tarea COUNT del conteo eliminado queda CANCELLED y la diferencia de sync/warehouse-tasks la trae con isActive=false
# (para que el aparato la borre).
FOUNDT8=false; CUR8=""
while :; do
  PT8=$(expect 200 "$(req GET "/api/v1/sync/warehouse-tasks?since=$SINCE8&warehousePublicId=$W6P&take=500${CUR8:+&cursor=$CUR8}" '' "$DT")")
  echo "$PT8" | jq -e --argjson c "$CC8ID" 'any(.items[]; .refEntityCode=="CYCLE_COUNT" and .refId==$c and .statusCode=="CANCELLED" and .isActive==false)' >/dev/null && { FOUNDT8=true; break; }
  CUR8=$(echo "$PT8" | jq -r '.nextCursor // empty'); [[ -n "$CUR8" ]] || break
done
[[ $FOUNDT8 == true ]] || fail "sync/warehouse-tasks no trae la tarea COUNT del conteo eliminado con isActive=false"
# Contador a ciegas (inventory.view + warehouse.count.capture, sin warehouse.count): crea, captura en lote y termina viendo
# la ficha a ciegas; reconciliar sigue siendo de warehouse.count (403).
expect 200 "$(req POST /api/v1/roles "{\"name\":\"Conteo a ciegas $TS\",\"permissions\":[\"inventory.view\",\"warehouse.count.capture\"]}")" >/dev/null
expect 200 "$(req POST /api/v1/users "{\"email\":\"conteo8$TS@teikem.local\",\"fullName\":\"Conteo 8 $TS\",\"password\":\"$PASS\",\"roles\":[\"Conteo a ciegas $TS\"]}")" >/dev/null
TCNT8=$(login "conteo8$TS@teikem.local" "$PASS")
CC9=$(expect 200 "$(req POST /api/v1/cycle-counts "{\"warehousePublicId\":\"$W6P\",\"binIds\":[$B_CC]}" "$TCNT8")"); CC9ID=$(echo "$CC9" | jq -r .count.id)
echo "$CC9" | jq -e '.isBlind==true and (.lines | length) >= 1 and all(.lines[]; .systemQty==null and .currentQty==null and .varianceQty==null)' >/dev/null || fail "alta del conteo a ciegas: $(echo "$CC9" | jq -c '{isBlind,lines:[.lines[]|{systemQty}]}')"
# Las respuestas de PUT /lines y POST /lines (captura de la app y línea encontrada) también llegan a ciegas.
L9=$(echo "$CC9" | jq -r '.lines[0].id')
expect 200 "$(req PUT "/api/v1/cycle-counts/$CC9ID/lines" "{\"lines\":[{\"lineId\":$L9,\"countedQty\":1}]}" "$TCNT8")" | jq -e '.isBlind==true and all(.lines[]; .systemQty==null and .varianceQty==null)' >/dev/null || fail "PUT /cycle-counts/{id}/lines del contador no llega a ciegas"
expect 200 "$(req POST "/api/v1/cycle-counts/$CC9ID/lines" "{\"binId\":$B_CC,\"productPublicId\":\"$PZ\",\"countedQty\":1}" "$TCNT8")" | jq -e '.isBlind==true and all(.lines[]; .systemQty==null and .varianceQty==null) and .count.varianceLines==null' >/dev/null || fail "POST /cycle-counts/{id}/lines del contador no llega a ciegas"
BATCH9=$(echo "$CC9" | jq -c '{lines:[.lines[] | {lineId:.id,countedQty:1}]}')
expect 200 "$(req PUT "/api/v1/cycle-counts/$CC9ID/lines/batch" "$BATCH9" "$TCNT8")" | jq -e '.isBlind==true and all(.lines[]; .systemQty==null and .countedQty==1) and .count.varianceLines==null and .count.netVariance==null' >/dev/null || fail "captura en lote a ciegas (sin diferencia en el encabezado)"
expect 200 "$(req GET "/api/v1/cycle-counts?search=$(echo "$CC9" | jq -r .count.number)" '' "$TCNT8")" | jq -e --argjson i "$CC9ID" 'any(.[]; .id==$i and .varianceLines==null and .netVariance==null)' >/dev/null || fail "lista de conteos a ciegas sin diferencia"
expect 200 "$(req GET "/api/v1/cycle-counts?search=$(echo "$CC9" | jq -r .count.number)")" | jq -e --argjson i "$CC9ID" 'any(.[]; .id==$i and .varianceLines!=null and .netVariance!=null)' >/dev/null || fail "lista de conteos informada con diferencia"
expect 200 "$(req POST "/api/v1/cycle-counts/$CC9ID/finish" '{}' "$TCNT8")" | jq -e '.isBlind==true and .count.statusCode=="COUNTED" and all(.lines[]; .systemQty==null)' >/dev/null || fail "terminar el conteo a ciegas"
expect 403 "$(req POST "/api/v1/cycle-counts/$CC9ID/reconcile" '{}' "$TCNT8")" >/dev/null || fail "reconciliar sin warehouse.count → 403"
# Quitar el PIN propio cierra las sesiones del aparato (el refresh se revisa al final, tras el paso de sesiones) y sin PIN
# no se entra; se vuelve a definir para que la desactivación falle por el aparato.
DRT_PIN=$(echo "$DL8" | jq -r .refreshToken)
# Sin gastar el token (un refresh lo rotaría): TOKEN_REVOKED pin_removed nuevo del usuario con count ≥ 1 y, con SMOKE_SQL,
# la fila de DRT_PIN (sesión de aparato) viva antes del DELETE y revocada después. Así el 401 del paso final no se explica
# por la desactivación del aparato, la reutilización del refresh web ni el vencimiento.
pinrm8() { expect 200 "$(req GET "/api/v1/audit/security-events?eventType=TOKEN_REVOKED&userId=$ME8&take=1000")" | jq '[.items[] | select((.detailJson // "") | (fromjson? // {}) | (.reason=="pin_removed" and .count >= 1))] | length'; }
DRT_PIN_H=$(printf '%s' "$DRT_PIN" | sha256sum | cut -d' ' -f1)
rtrevoked8() { $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.RefreshToken WHERE TokenHash='$DRT_PIN_H' AND UserDeviceId IS NOT NULL AND RevokedAtUtc IS NOT NULL;" | tr -d '[:space:]'; }
[[ -z "${SMOKE_SQL:-}" ]] || [[ $(rtrevoked8) == 0 ]] || fail "la sesión del aparato DRT_PIN ya estaba revocada antes de quitar el PIN"
PRM8=$(pinrm8)
expect 204 "$(req DELETE /api/v1/me/pin)" >/dev/null
[[ $(pinrm8) == $((PRM8 + 1)) ]] || fail "DELETE /me/pin no dejó TOKEN_REVOKED con reason pin_removed y count ≥ 1 (no cerró la sesión del aparato)"
[[ -z "${SMOKE_SQL:-}" ]] || [[ $(rtrevoked8) == 1 ]] || fail "DELETE /me/pin no revocó el refresh token de la sesión del aparato (DRT_PIN)"
expect 200 "$(req GET /api/v1/me/pin)" | jq -e '.hasPin==false' >/dev/null || fail "DELETE /me/pin deja hasPin=false"
expect 401 "$(DLOGIN 4826)" | jq -e --arg m "PIN incorrecto." "$HASM" >/dev/null || fail "sin PIN no se entra en el aparato"
ok2xx "$(req PUT /api/v1/me/pin "{\"currentPassword\":\"$PASS\",\"pin\":\"4826\"}")" "PUT /me/pin de nuevo"
DL9=$(expect 200 "$(DLOGIN 4826)"); DT=$(echo "$DL9" | jq -r .accessToken); DRT_DEV=$(echo "$DL9" | jq -r .refreshToken)
# La sesión abierta con PIN no salta de compañía (ni siquiera a una de la que el usuario es miembro); el 403 va antes de
# revocar, así que DRT_DEV sigue vivo.
expect 403 "$(anon POST /api/v1/auth/switch-tenant "{\"refreshToken\":\"$DRT_DEV\",\"tenantId\":$ME_TID}")" | jq -e --arg m "La sesión de un aparato no cambia de compañía." "$HASM" >/dev/null || fail "la sesión de un aparato cambió de compañía"
# ...y ese 403 no la revocó: el refresh da 200 (rota el token; se sigue con el par nuevo).
DR9=$(expect 200 "$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$DRT_DEV\"}")") || fail "el 403 de switch-tenant revocó la sesión del aparato"
DT=$(echo "$DR9" | jq -r .accessToken); DRT_DEV=$(echo "$DR9" | jq -r .refreshToken)
expect 200 "$(req GET '/api/v1/sync/products?take=1' '' "$DT")" >/dev/null
# Aparato desactivado: deja de entrar y de sincronizar en el acto (el access token vivo también se rechaza).
# Que la baja revoca la sesión del aparato se comprueba aquí, sin gastar DRT_DEV (un refresh lo rotaría) y antes de que el
# paso de sesiones reutilice el refresh web: TOKEN_REVOKED device_deactivated nuevo del usuario con count ≥ 1 y, con
# SMOKE_SQL, la fila de DRT_DEV viva antes de desactivar y revocada después.
devrevoked8() { expect 200 "$(req GET "/api/v1/audit/security-events?eventType=TOKEN_REVOKED&userId=$ME8&take=1000")" | jq --arg r "$1" --argjson min "$2" '[.items[] | select((.detailJson // "") | (fromjson? // {}) | (.reason==$r and .count >= $min))] | length'; }
DRT_DEV_H=$(printf '%s' "$DRT_DEV" | sha256sum | cut -d' ' -f1)
drtrevoked8() { $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.RefreshToken WHERE TokenHash='$DRT_DEV_H' AND RevokedAtUtc IS NOT NULL;" | tr -d '[:space:]'; }
[[ -z "${SMOKE_SQL:-}" ]] || [[ $(drtrevoked8) == 0 ]] || fail "la sesión del aparato DRT_DEV ya estaba revocada antes de desactivarlo"
DOFF8=$(devrevoked8 device_deactivated 1)
ok2xx "$(req POST "/api/v1/devices/$DEVP/deactivate" '{}')" "desactivar el aparato"
[[ $(devrevoked8 device_deactivated 1) == $((DOFF8 + 1)) ]] || fail "desactivar el aparato no dejó TOKEN_REVOKED con reason device_deactivated y count ≥ 1 (no cerró su sesión)"
[[ -z "${SMOKE_SQL:-}" ]] || [[ $(drtrevoked8) == 1 ]] || fail "desactivar el aparato no revocó el refresh token de su sesión (DRT_DEV)"
expect 200 "$(req GET '/api/v1/audit/changes?entityType=USER_DEVICE&take=50')" | jq -e '.total >= 1 and ([.items[] | select((.changesJson // "") | ascii_downcase | (contains("secrethash") or contains("enrollcodehash") or contains("rowversion")))] | length)==0' >/dev/null || fail "AuditLog de USER_DEVICE (alta/PATCH/baja) sin SecretHash ni EnrollCodeHash"
expect 401 "$(req GET '/api/v1/sync/products?take=1' '' "$DT")" >/dev/null || fail "el access token de un aparato desactivado sigue sincronizando"
expect 401 "$(DLOGIN 4826)" | jq -e --arg m "El aparato no está registrado o fue desactivado." "$HASM" >/dev/null || fail "aparato desactivado → device/login 401"
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=LOGIN&take=50')" | jq -e '[.items[] | select(((.detailJson // "") | contains("\"reason\":\"device_inactive\"")) and .outcome=="Fallo")] | length >= 1' >/dev/null || fail "el login rechazado por aparato desactivado no dejó SecurityEvent LOGIN/FAILURE"
expect 401 "$(anon POST /api/v1/auth/device/users "{\"devicePublicId\":\"$DEVP\",\"deviceSecret\":\"$SECRET\"}")" | jq -e --arg m "$BADDEV" "$HASM" >/dev/null || fail "aparato desactivado → device/users 401"
expect 200 "$(HB)" | jq -e '.isActive==false' >/dev/null || fail "heartbeat de un aparato desactivado debe devolver isActive=false (no 401)"
expect 422 "$(req POST "/api/v1/devices/$DEVP/enroll-code" '{}')" | jq -e --arg m "El aparato está desactivado; reactívelo antes de generar un código de registro." "$HASM" >/dev/null || fail "código de registro de un aparato desactivado → 422"
expect 200 "$(req GET /api/v1/devices)" | jq -e --arg d "$DEVP" 'all(.[]; .publicId!=$d)' >/dev/null || fail "aparato desactivado oculto por defecto"
expect 200 "$(req GET '/api/v1/devices?includeInactive=true')" | jq -e --arg d "$DEVP" 'any(.[]; .publicId==$d and .isActive==false)' >/dev/null || fail "aparato desactivado visible con includeInactive"
# Límite de intentos del enroll (10 por minuto por IP y ruta): se repite con un código inventado hasta el primer 429 (tope 25,
# cubre dos ventanas). Es la última llamada a enroll del smoke.
RL8=""; for i in $(seq 1 25); do R=$(anon POST /api/v1/devices/enroll '{"enrollCode":"ZZZZ3333"}'); if [[ "$(echo "$R" | tail -n1)" == 429 ]]; then RL8="$R"; break; fi; done
[[ -n "$RL8" ]] || fail "enroll sin límite de intentos: nunca respondió 429"
expect 429 "$RL8" | jq -e --arg m "Demasiados intentos; espere un minuto e intente de nuevo." '.title==$m and .code=="rate_limited" and .status==429' >/dev/null || fail "429 de enroll sin el ProblemDetails rate_limited"
rm -f "$IDH"
ok "aparato ZB-$TS (código vacío o de 31 caracteres, nombre de 101 y modelo de 81 → 400) (código repetido 409, sin devices.manage 403; código de registro de 24 h), enroll (código reutilizado, inventado, regenerado o vencido 401; 429 al exceder el límite de intentos), secreto incorrecto 401 en device/users, device/login y heartbeat; heartbeat (activo con almacén y tema, desactivado isActive=false); PATCH (tema inválido 400, almacén dado de baja 422), lastSeenUtc/lastUserId; admin de plataforma fuera del aparato; DeviceSessionDays 7 (0 y 366 → 400); SecurityEvent PASSWORD_CHANGE/LOGIN/LOCKOUT del aparato; misma clave de otro usuario → otro recibo; rechazo 400 repetido con Idempotent-Replayed, la misma clave en mayúsculas es otra clave (400, no 409) y clave inválida 400; recibo contra OC en una llamada con lo escaneado (PARTIAL); sync/purchase-orders sin purchasing.view 403; conteo a ciegas sin varianceLines/netVariance (ficha y lista); PIN desde Mi cuenta (contraseña incorrecta 400), bloqueo al 5.º PIN incorrecto (423, el acierto reinicia), device/login con did y 30 días (el refresh recalcula desde DeviceSessionDays: 7 y de vuelta 30); PIN de otros: hasPin, 403 sin permiso, 404 de otra compañía, 403 aal2_required y 403 a quien tiene más permisos; usuarios del aparato por nombre sin chofer ni usuarios sin PIN, con membresía suspendida o desactivados (login 401; sin inventory.view 403); Idempotency-Key: repetición con el mismo $N8 e Idempotent-Replayed, otro cuerpo 409; sync/products desde hace 1 h con PZ$TS (take 501 y cursor inválido 400), sync/bins con zona y los demás recursos; by-barcode por SKU y código de barras (inexistente 404); recibo confirmado en una llamada y atómico (serie sin capturar 400 sin consumir el REC); collect-and-pack PACKED idempotente y atómico (sin consumir el EMP) ('pack' en POST /pick-batches 400); conteo a ciegas para Solo lectura (captura 403) y del contador con warehouse.count.capture (alta, lote y terminar a ciegas; reconciliar 403); captura en lote idempotente (renglón repetido 400 y posición inexistente 404 sin guardar nada); switch-tenant con sesión de aparato 403 (sin revocar la sesión); DELETE /me/pin (TOKEN_REVOKED pin_removed de la sesión del aparato); aparato desactivado → token vivo 401 y device/login 401 (SecurityEvent LOGIN/FAILURE device_inactive); enroll con almacén y tema; login por aparato de Operador 6 a su nombre (sub, /me, lastUserId, AuditLog del recibo) y con sus permisos (POST /devices 403); refresh de aparato con WMS_LOTSERIAL apagado 401; restablecer el PIN cierra sus sesiones de aparato (refresh 401, TOKEN_REVOKED pin_changed); AuditLog de USER_DEVICE sin secretos; login por aparato con un usuario de otra compañía sin exponerlo en la bitácora; idempotencia en BD: $IDEM8; heartbeat y login no cambian la rowVersion (PATCH con la rowVersion anterior 200, tras una edición real 409); enroll sin código 401, device/login sin secreto 401 y PUT /me/pin sin contraseña 400 en español; PIN de otro sin permiso 403 (no aal2_required) con PERMISSION_DENIED; 10 PIN incorrectos en paralelo (≤4 × 401, resto 423); $PINX8; carrera con la misma clave (un solo REC; 200 con Replay o 409 en vuelo); desconexión del cliente (Replay sin duplicar inventario ni REC); segunda recepción parcial en sync/purchase-orders (pendiente 5); onlyVariance ignorado a ciegas (con warehouse.count solo contadas con diferencia); PUT y POST /lines a ciegas; tarea COUNT cancelada en sync/warehouse-tasks (isActive=false); campos de USER_DEVICE sin devices.manage 403 (con él 404); device/users y heartbeat sin secreto 401, PUT /me/pin y PUT /users/{id}/pin sin pin 400 en español; device/login con bearer de otra compañía sin exponerlo; sesión de aparato sin enroll/confirm de TOTP (403); $PINCO8"

step "Pulso del día por paneles (Lote F8a): mi orden, volver al de la compañía y orden de la compañía para otro usuario"
# Un panel por permiso pulse.* (+ permisos de datos y módulo); orden en dos niveles: compañía (pulse.organize_company) y usuario.
TOKEN=$(login "$EMAIL" "$PASS")
TDF8=$(login "$DISPATCH_EMAIL" "$PASS")
expect 204 "$(req DELETE /api/v1/analytics/pulse/layout/mine)" >/dev/null
PF8=$(expect 200 "$(req GET /api/v1/analytics/pulse)")
echo "$PF8" | jq -e '(.panels | map(.key) | sort) == ["ACTIVITY","ATTENTION","CHARTS","INDICATORS","WAREHOUSE","WAREHOUSE_DAY"] and .panels[0].key=="WAREHOUSE_DAY" and .panels[0].sortOrder==-10 and .canOrganizeCompany and (.hasPersonalLayout|not) and all(.panels[]; .source != "user") and all(.indicators[]; .source == "company")' >/dev/null || fail "Pulso del admin (6 paneles con ATTENTION del Lote 14 y WAREHOUSE_DAY del Lote 15 primero, sin orden propio): $(echo "$PF8" | jq -c '{panels,hasPersonalLayout,canOrganizeCompany}')"
INDF8=$(echo "$PF8" | jq -r '[.indicators[] | select(.isVisible)] | last | .id')
[[ "$INDF8" =~ ^[0-9]+$ ]] || fail "el Pulso del admin no trae indicadores visibles"
# mine: Actividad arriba, Gráficos oculto y el último indicador visible al principio.
BF8=$(jq -cn --argjson id "$INDF8" '{items:[{kind:"indicator",id:$id,sortOrder:-100,isVisible:true}],panels:[{key:"ACTIVITY",sortOrder:1,isVisible:true},{key:"charts",sortOrder:2,isVisible:false}]}')
MINEF8='.panels[0].key=="WAREHOUSE_DAY" and .panels[1].key=="ACTIVITY" and .panels[1].source=="user" and .panels[2].key=="CHARTS" and (.panels[2].isVisible|not) and .indicators[0].id==$id and .indicators[0].source=="user" and .indicators[0].sortOrder==-100 and .hasPersonalLayout'
expect 200 "$(req PUT '/api/v1/analytics/pulse/layout?scope=mine' "$BF8")" | jq -e --argjson id "$INDF8" "$MINEF8" >/dev/null || fail "PUT layout?scope=mine"
expect 200 "$(req PUT '/api/v1/analytics/pulse/layout?scope=mine' "$BF8")" >/dev/null   # idempotente
expect 200 "$(req GET /api/v1/analytics/pulse)" | jq -e --argjson id "$INDF8" "$MINEF8" >/dev/null || fail "GET pulse no conserva mi orden (hasPersonalLayout)"
# Validaciones con los mensajes exactos.
expect 400 "$(req PUT '/api/v1/analytics/pulse/layout?scope=todos' '{}')" | jq -e --arg m "Alcance inválido: use mine o company." "$HASM" >/dev/null || fail "scope inválido → 400"
expect 400 "$(req PUT /api/v1/analytics/pulse/layout '{}')" | jq -e --arg m "Alcance inválido: use mine o company." "$HASM" >/dev/null || fail "sin scope → 400"
expect 400 "$(req PUT '/api/v1/analytics/pulse/layout?scope=mine' '{"panels":[{"key":"RADAR","sortOrder":1,"isVisible":true}]}')" | jq -e --arg m "Panel de Pulso desconocido: RADAR." '.errors["panels[0].key"] | index($m) != null' >/dev/null || fail "panel desconocido → 400"
expect 400 "$(req PUT '/api/v1/analytics/pulse/layout?scope=mine' '{"items":[{"kind":"report","id":1,"sortOrder":1,"isVisible":true}]}')" | jq -e --arg m "Tipo inválido: use indicator o chart." '.errors["items[0].kind"] | index($m) != null' >/dev/null || fail "tipo inválido → 400"
expect 404 "$(req PUT '/api/v1/analytics/pulse/layout?scope=mine' '{"items":[{"kind":"chart","id":999999,"sortOrder":1,"isVisible":true}]}')" >/dev/null || fail "gráfico inexistente → 404"
# El despachador no tiene inventory.view ni pulse.warehouse: el panel Almacén no existe para él (404, no se revela).
expect 404 "$(req PUT '/api/v1/analytics/pulse/layout?scope=mine' '{"panels":[{"key":"WAREHOUSE","sortOrder":1,"isVisible":true}]}' "$TDF8")" >/dev/null || fail "despachador ordena el panel Almacén"
# Volver al de la compañía: se borra mi orden (y mis ocultos).
expect 204 "$(req DELETE /api/v1/analytics/pulse/layout/mine)" >/dev/null
expect 200 "$(req GET /api/v1/analytics/pulse)" | jq -e '(.hasPersonalLayout|not) and all(.panels[]; .source != "user") and all(.indicators[]; .source == "company")' >/dev/null || fail "DELETE layout/mine no volvió al Pulso de la compañía"
# company: sin pulse.organize_company → 403; con él (admin) el orden aplica a quien no tiene uno propio (despachador).
expect 403 "$(req PUT '/api/v1/analytics/pulse/layout?scope=company' '{"panels":[{"key":"ACTIVITY","sortOrder":5,"isVisible":true}]}' "$TDF8")" >/dev/null || fail "despachador organiza el Pulso de la compañía"
expect 204 "$(req DELETE /api/v1/analytics/pulse/layout/mine '' "$TDF8")" >/dev/null   # el despachador sin orden propio
expect 200 "$(req PUT '/api/v1/analytics/pulse/layout?scope=company' '{"panels":[{"key":"ACTIVITY","sortOrder":5,"isVisible":true},{"key":"CHARTS","sortOrder":6,"isVisible":true},{"key":"INDICATORS","sortOrder":7,"isVisible":true},{"key":"WAREHOUSE","sortOrder":8,"isVisible":true}]}')" | jq -e '.panels[0].key=="WAREHOUSE_DAY" and .panels[1].key=="ACTIVITY" and .panels[1].source=="company"' >/dev/null || fail "PUT layout?scope=company"
PDF8=$(expect 200 "$(req GET /api/v1/analytics/pulse '' "$TDF8")")
echo "$PDF8" | jq -e '(.panels | map(.key)) == ["ACTIVITY","CHARTS","INDICATORS"] and all(.panels[]; .source=="company") and (.canOrganizeCompany|not) and (.hasPersonalLayout|not)' >/dev/null || fail "el orden de la compañía no le aplica al despachador: $(echo "$PDF8" | jq -c '.panels')"
# Se deja el orden de la compañía igual al registro (20/30/40/50, visibles) y queda en la bitácora (PULSE_PANEL_SETTING).
expect 200 "$(req PUT '/api/v1/analytics/pulse/layout?scope=company' '{"panels":[{"key":"INDICATORS","sortOrder":20,"isVisible":true},{"key":"CHARTS","sortOrder":30,"isVisible":true},{"key":"WAREHOUSE","sortOrder":40,"isVisible":true},{"key":"ACTIVITY","sortOrder":50,"isVisible":true}]}')" >/dev/null
expect 200 "$(req GET /api/v1/analytics/pulse '' "$TDF8")" | jq -e '(.panels | map(.key)) == ["INDICATORS","CHARTS","ACTIVITY"]' >/dev/null || fail "orden de la compañía restaurado"
expect 200 "$(req GET '/api/v1/audit/changes?entityType=PULSE_PANEL_SETTING&take=50')" | jq -e '.total >= 4' >/dev/null || fail "AuditLog de PULSE_PANEL_SETTING"
ok "6 paneles del admin (con ATTENTION del Lote 14 y WAREHOUSE_DAY del Lote 15, primero con −10); mine (Actividad arriba, Gráficos oculto, indicador $INDF8 primero, idempotente, hasPersonalLayout); 400 con los mensajes exactos (scope, panel desconocido, tipo), 404 (gráfico inexistente, panel Almacén para el despachador); DELETE mine vuelve al de la compañía; company sin pulse.organize_company 403; el orden de la compañía le aplica al despachador (sin Almacén) y se restaura; AuditLog PULSE_PANEL_SETTING"

step "MFA por usuario (Lote F8a): exigirlo a una persona sin tocar la política de la compañía, y resetearlo"
# Tenant.MfaRequired del tenant demo está en falso (DemoTenantSeeder lo apaga para no romper el resto del smoke con
# el segundo factor); aquí se prueba el portón por MEMBRESÍA (UserTenant.MfaRequired), que no depende de ese ajuste.
MFAEMAIL="mfa$TS@teikem.local"; MFAPASS="Smoke_Mfa_2026!"
MFAU=$(expect 200 "$(req POST /api/v1/users "{\"email\":\"$MFAEMAIL\",\"fullName\":\"MFA Smoke\",\"password\":\"$MFAPASS\"}")")
MFAID=$(echo "$MFAU" | jq -r .user.id)
# reautenticación fresca: si el humo tarda más que la ventana AAL2 del tenant (p. ej. en una base local ya usada), la del
# paso de sesiones ya venció y [RequireAal2] respondería 403 aal2_required
TOKEN=$(expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PASS\"}")" | jq -r .accessToken)
expect 200 "$(req PUT "/api/v1/users/$MFAID/mfa" '{"required":true}')" | jq -e '.mfaRequired and (.mfaEnabled|not)' >/dev/null || fail "PUT mfa {required:true}"
expect 200 "$(anon POST /api/v1/auth/login "{\"email\":\"$MFAEMAIL\",\"password\":\"$MFAPASS\"}")" | jq -e '.status=="mfa_required" and .mfaEnrollmentRequired and (.tokens==null)' >/dev/null || fail "login exige MFA por la membresía"
expect 204 "$(req DELETE "/api/v1/users/$MFAID/mfa")" >/dev/null   # resetear (usuario que perdió su dispositivo): sin TOTP confirmado, no hay nada que deshacer aquí, pero no falla
expect 200 "$(req PUT "/api/v1/users/$MFAID/mfa" '{"required":false}')" | jq -e '.mfaRequired|not' >/dev/null || fail "PUT mfa {required:false}"
expect 200 "$(anon POST /api/v1/auth/login "{\"email\":\"$MFAEMAIL\",\"password\":\"$MFAPASS\"}")" | jq -e '.status=="ok"' >/dev/null || fail "login ya no exige MFA tras quitarlo"
ok "exigir/quitar MFA de un usuario en particular y resetearlo, sin tocar el ajuste de la compañía"

step "login concurrente con un intento fallido del mismo usuario (sin 500)"
# Un login correcto que coincide con un intento de contraseña incorrecta del mismo usuario (que cambia el ConcurrencyStamp
# de Identity) respondía 500 por DbUpdateConcurrencyException al guardar el usuario completo; ahora LastLoginUtc se escribe
# con un UPDATE dirigido. 3 fallos en paralelo (bajo el tope de 5 del lockout) mezclados con 3 logins correctos: ninguno
# distinto de 200/401. El login final en serie deja el contador de fallos en 0.
RACEDIR=$(mktemp -d)
for i in 1 2 3; do
  (anon POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}" | tail -n1 > "$RACEDIR/ok$i") &
  (anon POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"incorrecta-$TS\"}" | tail -n1 > "$RACEDIR/bad$i") &
done
wait
for i in 1 2 3; do
  [[ $(cat "$RACEDIR/ok$i") == 200 ]] || fail "login correcto concurrente con un intento fallido → HTTP $(cat "$RACEDIR/ok$i") (esperado 200)"
  [[ $(cat "$RACEDIR/bad$i") == 401 ]] || fail "intento fallido concurrente → HTTP $(cat "$RACEDIR/bad$i") (esperado 401)"
done
rm -rf "$RACEDIR"
TOKEN=$(login "$EMAIL" "$PASS")
ok "3 logins correctos y 3 intentos fallidos del admin en paralelo: 200 y 401 respectivamente, ningún 500"

step "sesiones: refresh con rotación y logout"
NEW=$(expect 200 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")")
expect 401 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")" >/dev/null   # reutilización → rechazada
expect 204 "$(req POST /api/v1/auth/logout "{\"refreshToken\":\"$(echo "$NEW" | jq -r .refreshToken)\"}")" >/dev/null
ok "rotación, detección de reutilización y logout"

step "sesiones de aparato revocadas (Lote 8A)"
# Después del paso de sesiones: refrescar un token revocado dispara la detección de reutilización (revoca la cadena del admin).
# Que DRT_PIN quedó revocado por quitar el PIN ya se comprobó sin gastarlo (TOKEN_REVOKED pin_removed y, con SMOKE_SQL, la fila).
expect 401 "$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$DRT_PIN\"}")" >/dev/null || fail "quitar el PIN no cerró la sesión del aparato"
expect 401 "$(anon POST /api/v1/auth/refresh "{\"refreshToken\":\"$DRT_DEV\"}")" >/dev/null || fail "el refresh del aparato desactivado sigue vivo"
# Reactivar: el aparato vuelve a entrar con PIN, pero las sesiones revocadas no reviven (la reactivación fija el sello
# SessionsNotBeforeUtc y revoca en su transacción las que quedaran vivas; deja TOKEN_REVOKED device_reactivated, aunque sean 0).
# Con SMOKE_SQL se simula una sesión que quedó viva durante la baja (DRT_DEV sin revocar): la reactivación debe revocarla
# (count ≥ 1).
TOKEN=$(login "$EMAIL" "$PASS")
DON8=$(devrevoked8 device_reactivated 0)
DON8C=$(devrevoked8 device_reactivated 1)
[[ -z "${SMOKE_SQL:-}" ]] || $SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; UPDATE dbo.RefreshToken SET RevokedAtUtc=NULL WHERE TokenHash='$DRT_DEV_H';" >/dev/null
[[ -z "${SMOKE_SQL:-}" ]] || [[ $(drtrevoked8) == 0 ]] || fail "no se pudo simular la sesión viva del aparato (DRT_DEV)"
# El iat va en segundos enteros y el sello se compara truncado al segundo: se deja pasar 1 s para que la reactivación caiga
# en un segundo posterior a la emisión de $DT (si no, un smoke muy rápido la aceptaría por empate de segundo).
sleep 1
expect 200 "$(req POST "/api/v1/devices/$DEVP/reactivate" '{}')" | jq -e '.isActive==true' >/dev/null || fail "reactivar el aparato"
[[ $(devrevoked8 device_reactivated 0) == $((DON8 + 1)) ]] || fail "reactivar el aparato no dejó TOKEN_REVOKED con reason device_reactivated"
[[ -z "${SMOKE_SQL:-}" ]] || [[ $(drtrevoked8) == 1 ]] || fail "reactivar no revocó la sesión del aparato que seguía viva (DRT_DEV)"
[[ -z "${SMOKE_SQL:-}" ]] || [[ $(devrevoked8 device_reactivated 1) == $((DON8C + 1)) ]] || fail "reactivar con una sesión viva no dejó TOKEN_REVOKED device_reactivated con count ≥ 1"
# El access token de antes de la baja ($DT) no revive con la reactivación (iat anterior al sello SessionsNotBeforeUtc).
expect 401 "$(req GET '/api/v1/sync/products?take=1' '' "$DT")" >/dev/null || fail "tras reactivar, el access token de antes de la baja volvió a sincronizar"
DL10=$(expect 200 "$(DLOGIN 4826)") || fail "tras reactivar, device/login con PIN"
# El access token nuevo sirve enseguida (ReactivateAsync limpia la caché 'did' de 60 s que dejó el sync rechazado).
expect 200 "$(req GET '/api/v1/sync/products?take=1' '' "$(echo "$DL10" | jq -r .accessToken)")" >/dev/null || fail "tras reactivar, el access token nuevo del aparato sigue rechazado (caché did sin limpiar)"
ok "desactivar revoca la sesión del aparato antes de cualquier refresh (TOKEN_REVOKED device_deactivated; con SMOKE_SQL, la fila de DRT_DEV); refresh de las sesiones de aparato tras quitar el PIN y tras desactivar el aparato → 401; reactivar deja entrar con PIN (y el token nuevo sincroniza al momento) sin revivir sesiones: el access token de antes de la baja sigue en 401 (sello SessionsNotBeforeUtc) y TOKEN_REVOKED device_reactivated (con SMOKE_SQL, la sesión DRT_DEV que se simuló viva queda revocada y el evento lleva count ≥ 1)"

step "migración (Lote 10): dry-run del importador con la muestra sintética"
# El verbo CLI import-legacy corre fuera del pipeline HTTP. En dry-run solo lee (la compañía de prueba no existe: todo se
# informa como 'se crearía') y escribe el reporte en TestResults/migracion (en .gitignore). SMOKE_MIGRATION_RUN permite
# cambiar el comando: el CI pasa el build Release que ya compiló ("dotnet run --project src/Teikem.Api -c Release --no-build");
# sin la variable se usa dotnet run a secas, que compila Debug de forma incremental y ejecuta el código actual.
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
MIG_OUT="$ROOT/TestResults/migracion"
mkdir -p "$MIG_OUT"
MIG_MARK="$MIG_OUT/.smoke-start"; touch "$MIG_MARK"; sleep 1
if [[ -n "${SMOKE_MIGRATION_RUN:-}" ]]; then read -r -a MIG_CMD <<< "$SMOKE_MIGRATION_RUN"
else
  MIG_CMD=(dotnet run --project src/Teikem.Api)
fi
set +e
MIG_LOG=$(cd "$ROOT" && "${MIG_CMD[@]}" -- import-legacy docs/migracion/sample/import.sample.json --dry-run 2>&1); MIG_RC=$?
set -e
[[ $MIG_RC == 0 ]] || fail "import-legacy --dry-run terminó con código $MIG_RC: $MIG_LOG"
MIG_MD=$(find "$MIG_OUT" -maxdepth 1 -name 'reporte-muestra-*.md' -newer "$MIG_MARK" | head -n1)
[[ -n "$MIG_MD" ]] || fail "import-legacy --dry-run no generó el reporte .md en $MIG_OUT"
grep -q 'SIMULACIÓN (dry-run)' "$MIG_MD" || fail "el reporte no está marcado como SIMULACIÓN (dry-run): $MIG_MD"
grep -q '| Productos | 6 | 5 | 0 | 1 | 0 |' "$MIG_MD" || fail "resumen de productos inesperado en $MIG_MD"
grep -q 'no se carga saldo inicial' "$MIG_MD" || fail "el reporte no informa la existencia negativa de la muestra"
[[ $(find "$MIG_OUT" -maxdepth 1 -name 'reporte-muestra-*.csv' -newer "$MIG_MARK" | wc -l) -ge 6 ]] || fail "faltan los CSV del reporte"
# Con SMOKE_SQL se comprueba que la simulación no aprovisionó la compañía de prueba.
[[ -z "${SMOKE_SQL:-}" ]] || [[ $($SMOKE_SQL "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM dbo.Tenant WHERE Name = N'Compañía de prueba (migración)';" | tr -dc '0-9') == 0 ]] || fail "el dry-run aprovisionó la compañía de prueba"
# Uso incorrecto → código 2 con el mensaje de uso.
set +e
MIG_USAGE=$(cd "$ROOT" && "${MIG_CMD[@]}" -- import-legacy 2>&1); MIG_RC=$?
set -e
[[ $MIG_RC == 2 ]] || fail "import-legacy sin configuración debería salir con código 2 (salió $MIG_RC)"
echo "$MIG_USAGE" | grep -q 'import-legacy <config.json> \[--dry-run\] \[--update\]' || fail "falta el mensaje de uso: $MIG_USAGE"
rm -f "$MIG_MARK"
ok "dry-run con la muestra: código 0, reporte .md (SIMULACIÓN) y CSV generados sin escribir en la base; sintaxis incorrecta → código 2"

step "migración (Lote 10, D51): --update sobre una compañía nueva sí carga el saldo inicial (dry-run)"
MIG_UPDATE_MARK="$MIG_OUT/.smoke-update-start"; touch "$MIG_UPDATE_MARK"; sleep 1
set +e
MIG_UPDATE_LOG=$(cd "$ROOT" && "${MIG_CMD[@]}" -- import-legacy docs/migracion/sample/import.sample.json --dry-run --update 2>&1); MIG_UPDATE_RC=$?
set -e
[[ $MIG_UPDATE_RC == 0 ]] || fail "import-legacy --dry-run --update terminó con código $MIG_UPDATE_RC: $MIG_UPDATE_LOG"
MIG_UPDATE_MD=$(find "$MIG_OUT" -maxdepth 1 -name 'reporte-muestra-*.md' -newer "$MIG_UPDATE_MARK" | head -n1)
[[ -n "$MIG_UPDATE_MD" ]] || fail "import-legacy --dry-run --update no generó el reporte .md en $MIG_OUT"
grep -q -- "--update" "$MIG_UPDATE_MD" || fail "el reporte no marca el modo --update: $MIG_UPDATE_MD"
# D51: la compañía de la muestra no existe en el humo (solo dry-run), así que --update es su primera carga y trae su saldo
# inicial igual que sin --update; el aviso 'no se toca' es solo para una compañía que ya existía (LegacyImportServiceTests)
! grep -q 'El saldo inicial no se toca en modo --update' "$MIG_UPDATE_MD" || fail "--update en la primera carga omitió el saldo inicial (D51): $MIG_UPDATE_MD"
grep -q '^## Saldo inicial (' "$MIG_UPDATE_MD" || fail "el reporte de --update en la primera carga no trae el saldo inicial: $MIG_UPDATE_MD"
rm -f "$MIG_UPDATE_MARK"
ok "dry-run con --update: modo marcado en el título y, en la primera carga de la compañía, con su saldo inicial (D51)"

step "db-reset (Lote 10): sin --yes rehúsa borrar la base"
set +e
DBRESET_LOG=$(cd "$ROOT" && "${MIG_CMD[@]}" -- db-reset 2>&1); DBRESET_RC=$?
set -e
[[ $DBRESET_RC == 2 ]] || fail "db-reset sin --yes debería salir con código 2 (salió $DBRESET_RC)"
echo "$DBRESET_LOG" | grep -q -- '--yes' || fail "falta el mensaje de confirmación de db-reset: $DBRESET_LOG"
ok "db-reset sin --yes: código 2, no se tocó la base"

printf '\n\033[1;32mSMOKE OK\033[0m\n'
