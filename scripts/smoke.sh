#!/usr/bin/env bash
# Prueba de humo del Lote 1 contra un API levantado (default http://localhost:5000).
# Requiere: curl, jq. Uso: scripts/smoke.sh [base_url]
set -euo pipefail
BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-admin@teikem.local}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
DISPATCH_EMAIL="${TEIKEM_DISPATCH_EMAIL:-despacho@teikem.local}"

step() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m   ok\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m   FAIL\033[0m %s\n' "$*"; exit 1; }
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
expect 200 "$(req GET '/api/v1/audit/activity/export.csv')" | head -1 | grep -q 'Cuando' || fail "csv"; ok "bitácora unificada + CSV"

step "RBAC: despachador sin admin.users → 403 + PERMISSION_DENIED"
R2=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$DISPATCH_EMAIL\",\"password\":\"$PASS\"}")")
T2=$(echo "$R2" | jq -r .tokens.accessToken)
expect 403 "$(req GET /api/v1/users '' "$T2")" >/dev/null
expect 200 "$(req GET '/api/v1/audit/security-events?eventType=PERMISSION_DENIED&take=5')" | jq -e '.total >= 1' >/dev/null || fail "PERMISSION_DENIED no registrado"
ok "permiso denegado registrado"

step "sesiones: refresh con rotación y logout"
NEW=$(expect 200 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")")
expect 401 "$(req POST /api/v1/auth/refresh "{\"refreshToken\":\"$REFRESH\"}")" >/dev/null   # reutilización → rechazada
expect 204 "$(req POST /api/v1/auth/logout "{\"refreshToken\":\"$(echo "$NEW" | jq -r .refreshToken)\"}")" >/dev/null
ok "rotación, detección de reutilización y logout"

printf '\n\033[1;32mSMOKE OK\033[0m\n'
