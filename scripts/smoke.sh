#!/usr/bin/env bash
# Prueba de humo de los Lotes 1 y 2 contra un API levantado (default http://localhost:5000).
# Requiere: curl, jq. Uso: scripts/smoke.sh [base_url]
set -euo pipefail
BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-admin@teikem.local}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
DISPATCH_EMAIL="${TEIKEM_DISPATCH_EMAIL:-despacho@teikem.local}"

step() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m   ok\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m   FAIL\033[0m %s\n' "$*" >&2; exit 1; }
req()  { # method path [json] [token]
  local m=$1 p=$2 body=${3:-} tok=${4:-${TOKEN:-}}
  local args=(-sS -X "$m" "$BASE$p" -H 'Accept: application/json' -H 'Content-Type: application/json' -H 'X-Lang: es')
  [[ -n "$tok" ]] && args+=(-H "Authorization: Bearer $tok")
  [[ -n "$body" ]] && args+=(--data "$body")
  curl "${args[@]}" -w '\n%{http_code}'
}
expect() { # expected_code response
  local code; code=$(echo "$2" | tail -n1); local body; body=$(echo "$2" | sed '$d')
  [[ "$code" == "$1" ]] || fail "esperado HTTP $1, recibido $code: $body"
  echo "$body"
}

step "health"; expect 200 "$(req GET /health)" >/dev/null; ok "/health"

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
PLATFORM_EMAIL="${TEIKEM_PLATFORM_EMAIL:-soporte@teikem.local}"

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
CLIENT2_PID=$(echo "$C2" | jq -r .publicId); CONTRACT_C2_PID=$(echo "$C2" | jq -r .currentContract.publicId)
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
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Smoke $TS\",\"modules\":[\"CATALOG\",\"CLIENT_PORTAL\"],\"adminEmail\":\"admin$TS@smoke.local\",\"adminFullName\":\"Admin Smoke\",\"adminPassword\":\"Smoke_Admin_2026!\"}" "$T_SOP")" >/dev/null
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

step "sesiones: refresh con rotación y logout"
NEW=$(expect 200 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")")
expect 401 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")" >/dev/null   # reutilización → rechazada
expect 204 "$(req POST /api/v1/auth/logout "{\"refreshToken\":\"$(echo "$NEW" | jq -r .refreshToken)\"}")" >/dev/null
ok "rotación, detección de reutilización y logout"

printf '\n\033[1;32mSMOKE OK\033[0m\n'
