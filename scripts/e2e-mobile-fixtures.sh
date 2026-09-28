#!/usr/bin/env bash
# Crea las fixtures para E2E mobile: dispositivo, usuario con PIN, productos, cliente y ubicación para Maestro.
# Uso: scripts/e2e-mobile-fixtures.sh [base_url]
set -euo pipefail

BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-teikem+admin@cerevelo.com}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
TS=$(date +%s)

# Helpers (copiados de smoke.sh)
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

step "health"
expect 200 "$(req GET /health)" >/dev/null
ok "servidor responde"

step "login admin"
LOGIN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"e2e-fixtures\"}")")
[[ $(echo "$LOGIN" | jq -r .status) == "ok" ]] || fail "login status: $LOGIN"
TOKEN=$(echo "$LOGIN" | jq -r .tokens.accessToken)
ok "admin autenticado"

step "crear PIN para admin"
expect 200 "$(req PUT /api/v1/me/pin "{\"currentPassword\":\"$PASS\",\"pin\":\"2846\"}" "$TOKEN")" >/dev/null
ok "PIN 2846 establecido"

step "obtener almacén demo ALM-01"
WAREHOUSES=$(expect 200 "$(req GET /api/v1/warehouses "" "$TOKEN")")
WAREHOUSE_ID=$(echo "$WAREHOUSES" | jq -r '.[] | select(.code=="ALM-01" and .statusCode=="ACTIVE") | .publicId')
[[ -n "$WAREHOUSE_ID" ]] || fail "no se encontró almacén ALM-01 ACTIVE"
ok "almacén demo: $WAREHOUSE_ID"

step "registrar dispositivo E2E"
DEVICE=$(expect 200 "$(req POST /api/v1/devices "{\"code\":\"E2E-DEV-1\",\"name\":\"Emulador E2E\",\"defaultWarehousePublicId\":\"$WAREHOUSE_ID\"}" "$TOKEN")")
DEVICE_ENROLL=$(echo "$DEVICE" | jq -r .enrollCode)
[[ -n "$DEVICE_ENROLL" ]] || fail "enrollCode ausente en respuesta: $DEVICE"
ok "dispositivo registrado, código: $DEVICE_ENROLL"

step "crear producto para Recibir (ciego, sin dueño)"
PROD_RECV=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"E2E-SKU-RECV\",\"name\":\"Producto E2E Recibir\",\"barcode\":\"E2E-BARCODE-RECV\",\"trackingType\":\"NONE\"}" "$TOKEN")")
PROD_RECV_ID=$(echo "$PROD_RECV" | jq -r .product.publicId)
[[ -n "$PROD_RECV_ID" ]] || fail "publicId ausente en producto recibido: $PROD_RECV"
ok "producto recibir: $PROD_RECV_ID"

step "crear cliente 3PL para Despacho"
CLIENT=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Cliente E2E\",\"contract\":{\"startDate\":\"2026-01-01\"}}" "$TOKEN")")
CLIENT_ID=$(echo "$CLIENT" | jq -r .publicId)
[[ -n "$CLIENT_ID" ]] || fail "publicId ausente en cliente: $CLIENT"
ok "cliente 3PL: $CLIENT_ID"

step "crear ubicación consignatario para cliente"
LOCATION=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_ID\",\"name\":\"Sucursal E2E\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle Test\",\"city\":\"San Juan\",\"country\":\"PR\"}" "$TOKEN")")
LOCATION_NAME=$(echo "$LOCATION" | jq -r .name)
LOCATION_CITY=$(echo "$LOCATION" | jq -r .city)
CONSIGNEE_LABEL="$LOCATION_NAME · $LOCATION_CITY"
ok "consignatario: $CONSIGNEE_LABEL"

step "crear producto para Despacho (propiedad del cliente)"
PROD_DISP=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"E2E-SKU-DISP\",\"name\":\"Producto E2E Despacho\",\"barcode\":\"E2E-BARCODE-DISP\",\"trackingType\":\"NONE\",\"ownerClientPublicId\":\"$CLIENT_ID\"}" "$TOKEN")")
PROD_DISP_ID=$(echo "$PROD_DISP" | jq -r .product.publicId)
[[ -n "$PROD_DISP_ID" ]] || fail "publicId ausente en producto despacho: $PROD_DISP"
ok "producto despacho (propiedad cliente): $PROD_DISP_ID"

step "recibir y confirmar en una sola llamada (confirm:true, igual que hace la app)"
RECEIPT=$(expect 200 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$WAREHOUSE_ID\",\"purchaseOrderPublicId\":null,\"asnId\":null,\"confirm\":true,\"lines\":[{\"productPublicId\":\"$PROD_DISP_ID\",\"receivedQty\":50}]}" "$TOKEN")")
PUTAWAY_TASK_ID=$(echo "$RECEIPT" | jq -r '.putawayTasks[0].id')
[[ -n "$PUTAWAY_TASK_ID" && "$PUTAWAY_TASK_ID" != "null" ]] || fail "putawayTasks ausente en respuesta: $RECEIPT"
ok "recibo confirmado, tarea PUTAWAY: $PUTAWAY_TASK_ID"

step "completar PUTAWAY a bin A01-R01-N1-P01"
BINS_PCK=$(expect 200 "$(req GET /api/v1/warehouses/$WAREHOUSE_ID/bins?search=A01-R01-N1-P01 "" "$TOKEN")")
BIN_PCK=$(echo "$BINS_PCK" | jq -r '.[0].id')
[[ -n "$BIN_PCK" ]] || fail "bin A01-R01-N1-P01 no encontrado"

expect 200 "$(req POST /api/v1/warehouse-tasks/$PUTAWAY_TASK_ID/start "{}" "$TOKEN")" >/dev/null
expect 200 "$(req POST /api/v1/warehouse-tasks/$PUTAWAY_TASK_ID/complete "{\"toBinId\":$BIN_PCK,\"quantity\":50}" "$TOKEN")" >/dev/null
ok "producto en stock en bin A01-R01-N1-P01"

step "guardar variables en GITHUB_ENV para CI"
if [[ -n "${GITHUB_ENV:-}" ]]; then
  {
    echo "E2E_ENROLL_CODE=$DEVICE_ENROLL"
    echo "E2E_CONSIGNEE_LABEL=$CONSIGNEE_LABEL"
    echo "E2E_SERVER_URL=http://10.0.2.2:5000"
  } >> "$GITHUB_ENV"
  ok "variables exportadas a GITHUB_ENV"
else
  # Si no estamos en CI, simplemente mostrarlas
  echo ""
  echo "Exportar las siguientes variables en el contexto de Maestro:"
  echo "  E2E_ENROLL_CODE=$DEVICE_ENROLL"
  echo "  E2E_CONSIGNEE_LABEL=$CONSIGNEE_LABEL"
  echo "  E2E_SERVER_URL=http://10.0.2.2:5000"
fi

ok "todas las fixtures creadas"
