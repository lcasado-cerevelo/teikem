#!/usr/bin/env bash
# Crea las fixtures para E2E mobile: dispositivo, usuario con PIN, productos, cliente y ubicación para Maestro.
# Uso: scripts/e2e-mobile-fixtures.sh [base_url]
set -euo pipefail

BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-teikem+admin@cerevelo.com}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
TS=$(date +%s)

# jq: los listados pueden venir como arreglo plano o como página { total, skip, take, items } (p. ej. las posiciones del
# almacén desde el Lote 1). Este filtro entrega siempre el arreglo de filas; se usa como `jq "$ROWS | .[] | ..."`.
ROWS='(if type=="array" then . else (.items // []) end)'

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
WAREHOUSE_ID=$(echo "$WAREHOUSES" | jq -r "$ROWS | .[] | select(.code==\"ALM-01\" and .statusCode==\"ACTIVE\") | .publicId" | head -n1)
[[ -n "$WAREHOUSE_ID" ]] || fail "no se encontró almacén ALM-01 ACTIVE"
ok "almacén demo: $WAREHOUSE_ID"

step "registrar dispositivo E2E"
DEVICE_RAW=$(req POST /api/v1/devices "{\"code\":\"E2E-DEV-1\",\"name\":\"Emulador E2E\",\"defaultWarehousePublicId\":\"$WAREHOUSE_ID\"}" "$TOKEN")
DEVICE_CODE=$(echo "$DEVICE_RAW" | tail -n1)
if [[ "$DEVICE_CODE" == "409" ]]; then
  # Reintento local (no pasa en CI, donde la BD siempre arranca vacía): el aparato E2E-DEV-1 ya existe de una corrida
  # anterior. En vez de fallar, se reusa: se busca por código y se le regenera el código de registro de un solo uso.
  ok "dispositivo E2E-DEV-1 ya existía, reutilizando (regenerando código)"
  DEVICES=$(expect 200 "$(req GET /api/v1/devices "" "$TOKEN")")
  DEVICE_PUBLIC_ID=$(echo "$DEVICES" | jq -r "$ROWS | .[] | select(.code==\"E2E-DEV-1\") | .publicId" | head -n1)
  [[ -n "$DEVICE_PUBLIC_ID" ]] || fail "no se encontró el aparato E2E-DEV-1 tras el 409"
  DEVICE=$(expect 200 "$(req POST /api/v1/devices/$DEVICE_PUBLIC_ID/enroll-code "{}" "$TOKEN")")
else
  DEVICE=$(expect 200 "$DEVICE_RAW")
fi
DEVICE_ENROLL=$(echo "$DEVICE" | jq -r .enrollCode)
[[ -n "$DEVICE_ENROLL" ]] || fail "enrollCode ausente en respuesta: $DEVICE"
ok "dispositivo registrado, código: $DEVICE_ENROLL"

step "crear producto para Recibir (ciego, sin dueño)"
PROD_RECV_RAW=$(req POST /api/v1/products "{\"sku\":\"E2E-SKU-RECV\",\"name\":\"Producto E2E Recibir\",\"barcode\":\"E2E-BARCODE-RECV\",\"trackingType\":\"NONE\"}" "$TOKEN")
if [[ "$(echo "$PROD_RECV_RAW" | tail -n1)" == "409" ]]; then
  # Reintento local: el producto ya existe de una corrida anterior (CI siempre arranca con BD vacía, así que no le pasa).
  ok "producto E2E-SKU-RECV ya existía, reutilizando"
  PROD_RECV=$(expect 200 "$(req GET /api/v1/products/by-barcode/E2E-BARCODE-RECV "" "$TOKEN")")
else
  PROD_RECV=$(expect 200 "$PROD_RECV_RAW")
fi
PROD_RECV_ID=$(echo "$PROD_RECV" | jq -r .product.publicId)
[[ -n "$PROD_RECV_ID" ]] || fail "publicId ausente en producto recibido: $PROD_RECV"
ok "producto recibir: $PROD_RECV_ID"

step "cliente 3PL para Despacho"
# El producto E2E-SKU-DISP (más abajo) se reutiliza entre corridas locales, y su dueño queda fijo desde que se creó
# la primera vez; crear un cliente nuevo en cada corrida lo dejaría con la tarifa y el consignatario en un cliente
# que el producto ya no apunta. Por eso primero se busca el producto por código de barras (sin crear nada): si ya
# existe, todo lo demás (cliente, tarifa, consignatario) se resuelve contra SU dueño, no contra uno nuevo.
PROD_DISP_LOOKUP_RAW=$(req GET /api/v1/products/by-barcode/E2E-BARCODE-DISP "" "$TOKEN")
if [[ "$(echo "$PROD_DISP_LOOKUP_RAW" | tail -n1)" == "200" ]]; then
  PROD_DISP_EXISTING=$(echo "$PROD_DISP_LOOKUP_RAW" | sed '$d')
  CLIENT_ID=$(echo "$PROD_DISP_EXISTING" | jq -r .product.ownerClientPublicId)
  [[ -n "$CLIENT_ID" && "$CLIENT_ID" != "null" ]] || fail "producto E2E-SKU-DISP ya existe pero sin dueño: $PROD_DISP_EXISTING"
  CLIENT=$(expect 200 "$(req GET /api/v1/clients/$CLIENT_ID "" "$TOKEN")")
  ok "cliente 3PL ya existía (dueño de E2E-SKU-DISP): $CLIENT_ID"
else
  CLIENT=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Cliente E2E\",\"contract\":{\"startDate\":\"2026-01-01\"}}" "$TOKEN")")
  CLIENT_ID=$(echo "$CLIENT" | jq -r .publicId)
  [[ -n "$CLIENT_ID" ]] || fail "publicId ausente en cliente: $CLIENT"
  ok "cliente 3PL creado: $CLIENT_ID"
fi

step "tarifa STANDARD/BOX en el contrato (la app manda ese servicio+paquete fijo, sin selector)"
CONTRACT_ID=$(echo "$CLIENT" | jq -r .currentContract.publicId)
[[ -n "$CONTRACT_ID" && "$CONTRACT_ID" != "null" ]] || fail "currentContract.publicId ausente en cliente: $CLIENT"
RATE_RAW=$(req POST "/api/v1/contracts/$CONTRACT_ID/rate-components" "{\"kind\":\"PER_SERVICE\",\"serviceType\":\"STANDARD\",\"packageType\":\"BOX\",\"rate\":10.00}" "$TOKEN")
RATE_CODE=$(echo "$RATE_RAW" | tail -n1)
if [[ "$RATE_CODE" == "409" ]]; then
  ok "tarifa STANDARD/BOX ya existía, reutilizando"
else
  expect 200 "$RATE_RAW" >/dev/null
  ok "tarifa STANDARD/BOX creada"
fi

step "ubicación consignatario para el cliente"
LOCATIONS=$(expect 200 "$(req GET "/api/v1/locations?clientId=$CLIENT_ID" "" "$TOKEN")")
LOCATION=$(echo "$LOCATIONS" | jq -c "$ROWS | .[] | select(.name==\"Sucursal E2E\" and .city==\"San Juan\")" | head -n1)
if [[ -n "$LOCATION" ]]; then
  ok "consignatario ya existía, reutilizando"
else
  LOCATION=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CLIENT_ID\",\"name\":\"Sucursal E2E\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle Test\",\"city\":\"San Juan\",\"country\":\"PR\"}" "$TOKEN")")
fi
LOCATION_NAME=$(echo "$LOCATION" | jq -r .name)
LOCATION_CITY=$(echo "$LOCATION" | jq -r .city)
CONSIGNEE_LABEL="$LOCATION_NAME · $LOCATION_CITY"
ok "consignatario: $CONSIGNEE_LABEL"

step "producto para Despacho (propiedad del cliente)"
if [[ "$(echo "$PROD_DISP_LOOKUP_RAW" | tail -n1)" == "200" ]]; then
  ok "producto E2E-SKU-DISP ya existía, reutilizando"
  PROD_DISP="$PROD_DISP_EXISTING"
else
  PROD_DISP=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"E2E-SKU-DISP\",\"name\":\"Producto E2E Despacho\",\"barcode\":\"E2E-BARCODE-DISP\",\"trackingType\":\"NONE\",\"ownerClientPublicId\":\"$CLIENT_ID\"}" "$TOKEN")")
fi
PROD_DISP_ID=$(echo "$PROD_DISP" | jq -r .product.publicId)
[[ -n "$PROD_DISP_ID" ]] || fail "publicId ausente en producto despacho: $PROD_DISP"
ok "producto despacho (propiedad cliente): $PROD_DISP_ID"

step "recibir y confirmar en una sola llamada (confirm:true, igual que hace la app)"
RECEIPT=$(expect 200 "$(req POST /api/v1/receipts "{\"warehousePublicId\":\"$WAREHOUSE_ID\",\"purchaseOrderPublicId\":null,\"asnId\":null,\"confirm\":true,\"lines\":[{\"productPublicId\":\"$PROD_DISP_ID\",\"receivedQty\":50}]}" "$TOKEN")")
PUTAWAY_TASK_ID=$(echo "$RECEIPT" | jq -r '.putawayTasks[0].id')
[[ -n "$PUTAWAY_TASK_ID" && "$PUTAWAY_TASK_ID" != "null" ]] || fail "putawayTasks ausente en respuesta: $RECEIPT"
ok "recibo confirmado, tarea PUTAWAY: $PUTAWAY_TASK_ID"

step "completar PUTAWAY a bin A01-R01-N1-P01"
# GET .../bins devuelve una página { total, skip, take, items }; se toma la posición de código exacto (search es "contiene").
BINS_PCK=$(expect 200 "$(req GET "/api/v1/warehouses/$WAREHOUSE_ID/bins?search=A01-R01-N1-P01" "" "$TOKEN")")
BIN_PCK=$(echo "$BINS_PCK" | jq -r "$ROWS | map(select(.code==\"A01-R01-N1-P01\")) | .[0].id // empty")
[[ -n "$BIN_PCK" ]] || fail "bin A01-R01-N1-P01 no encontrado en $BINS_PCK"

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
