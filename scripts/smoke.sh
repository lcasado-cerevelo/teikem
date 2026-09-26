#!/usr/bin/env bash
# Prueba de humo de los Lotes 1, 2 y 3 contra un API levantado (default http://localhost:5000).
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
mpost() { curl -sS -X POST "$BASE/api/v1/orders/import/validate" -H "Authorization: Bearer $TOKEN" -F "templatePublicId=$1" -F "clientPublicId=$CLIENT_O_PID" -F "file=@$2;type=text/csv" -w '\n%{http_code}'; }
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

step "sesiones: refresh con rotación y logout"
NEW=$(expect 200 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")")
expect 401 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")" >/dev/null   # reutilización → rechazada
expect 204 "$(req POST /api/v1/auth/logout "{\"refreshToken\":\"$(echo "$NEW" | jq -r .refreshToken)\"}")" >/dev/null
ok "rotación, detección de reutilización y logout"

printf '\n\033[1;32mSMOKE OK\033[0m\n'
