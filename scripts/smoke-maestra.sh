#!/usr/bin/env bash
# Prueba de humo de la recolección maestra (Lotes 176-178) contra un API levantado: recolectar dos avisos de salida del mismo cliente, ver la ficha con sus movimientos,
# empacar (UN envío), rechazar la mezcla de clientes y eliminar (el inventario y las reservas vuelven). Independiente de smoke.sh (que sigue el modelo viejo de estatus de órdenes).
# Requiere: curl, jq. Uso: scripts/smoke-maestra.sh [base_url]
set -euo pipefail
BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-teikem+admin@cerevelo.com}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
TMPD=$(mktemp -d); trap 'rm -rf "$TMPD"' EXIT
step() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m   ok\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m   FAIL\033[0m %s\n' "$*" >&2; exit 1; }
req()  { local m=$1 p=$2 body=${3:-}; local args=(-sS -X "$m" "$BASE$p" -H 'Accept: application/json' -H 'Content-Type: application/json' -H 'X-Lang: es')
  [[ -n "${TOKEN:-}" ]] && args+=(-H "Authorization: Bearer $TOKEN")
  if [[ -n "$body" ]]; then local f; f=$(mktemp -p "$TMPD"); printf '%s' "$body" > "$f"; args+=(--data-binary "@$f"); fi
  curl "${args[@]}" -w '\n%{http_code}'; }
expect() { local code; code=$(echo "$2" | tail -n1); local body; body=$(echo "$2" | sed '$d'); [[ "$code" == "$1" ]] || fail "esperado HTTP $1, recibido $code: $body"; echo "$body"; }
TS=$(date +%s)

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-maestra\"}")" | jq -r .tokens.accessToken); ok "token"

step "almacén, zona de picking y posición"
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"M$TS\",\"name\":\"Maestra $TS\"}")" | jq -r .warehouse.publicId)
Z=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"PCK","name":"Picking","zoneType":"PICKING"}')" | jq -r .id)
B1=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$Z,\"code\":\"P-01\"}")" | jq -r .id)
B2=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$Z,\"code\":\"P-02\"}")" | jq -r .id); ok "W=$W bins $B1/$B2"

step "clientes, productos y existencia"
CAR=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Maestra A $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")"); CA=$(echo "$CAR" | jq -r .publicId); CTR=$(echo "$CAR" | jq -r .currentContract.publicId)
expect 200 "$(req POST "/api/v1/contracts/$CTR/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":6.5,"effectiveFrom":"2026-01-01"}')" >/dev/null
CB=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Maestra B $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId)
PA=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"MA$TS\",\"name\":\"Producto A $TS\",\"ownerClientPublicId\":\"$CA\"}")" | jq -r .product.publicId)
PB=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"MB$TS\",\"name\":\"Producto B $TS\",\"ownerClientPublicId\":\"$CB\"}")" | jq -r .product.publicId)
adjust() { expect 200 "$(req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$1" --arg w "$W" --argjson b "$2" --argjson q "$3" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:$q,reason:"FOUND",notes:"humo maestra"}')")" >/dev/null; }
adjust "$PA" "$B1" 6; adjust "$PA" "$B2" 4; adjust "$PB" "$B1" 5; ok "A: 6+4, B: 5"
onhand() { expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$W&productPublicIds=$1&includeZero=true")" | jq '([.items[].qtyOnHand] | add // 0) + 0'; }

step "avisos de salida (reservan)"
notice() { expect 200 "$(req POST /api/v1/exit-notices "$(jq -cn --arg w "$W" --arg c "$1" --arg p "$2" --argjson q "$3" '{warehousePublicId:$w,clientPublicId:$c,reference:"humo",destination:"Muelle",lines:[{productPublicId:$p,quantity:$q}]}')")" | jq -r .publicId; }
N1=$(notice "$CA" "$PA" 4); N2=$(notice "$CA" "$PA" 5); N3=$(notice "$CB" "$PB" 2)
expect 200 "$(req GET "/api/v1/exit-notices/$N2")" | jq -e '.lines[0].qtyReserved==5' >/dev/null || fail "el aviso 2 no reservó 5"; ok "3 avisos"

step "recolectar juntos: otro cliente y uno solo se rechazan"
expect 400 "$(req POST /api/v1/pick-batches/groups "$(jq -cn --arg a "$N1" --arg b "$N3" '{documents:[{kind:"EXIT_NOTICE",publicId:$a},{kind:"EXIT_NOTICE",publicId:$b}]}')")" | jq -e '.errors["documents[1]"][0] | contains("clientes distintos")' >/dev/null || fail "mezcla de clientes"
expect 400 "$(req POST /api/v1/pick-batches/groups "$(jq -cn --arg a "$N1" '{documents:[{kind:"EXIT_NOTICE",publicId:$a}]}')")" >/dev/null; ok "400 por cliente distinto y por un solo documento"

step "recolectar juntos N1 + N2"
G=$(expect 200 "$(req POST /api/v1/pick-batches/groups "$(jq -cn --arg a "$N1" --arg b "$N2" '{documents:[{kind:"EXIT_NOTICE",publicId:$a},{kind:"EXIT_NOTICE",publicId:$b}],note:"humo"}')")")
GP=$(echo "$G" | jq -r .publicId); GN=$(echo "$G" | jq -r .number)
echo "$G" | jq -e '.statusCode=="COLLECTED" and (.documents|length)==2 and .totalQty==9 and ([.movements[].quantity]|add)==9 and .canPack and .canDelete' >/dev/null || fail "ficha de la maestra: $G"
echo "$G" | jq -e '[.movements[].binCode] | (index("P-01")!=null and index("P-02")!=null)' >/dev/null || fail "los movimientos deben salir de las dos posiciones"
[[ $(onhand "$PA") == 1 ]] || fail "existencia de A tras recolectar: $(onhand "$PA")"
expect 200 "$(req GET "/api/v1/exit-notices/$N1")" | jq -e '.statusCode=="SHIPPED"' >/dev/null || fail "N1 despachado"
ok "maestra $GN: 2 documentos, 9 unidades de dos posiciones, N1 SHIPPED"

step "lista y búsqueda"
expect 200 "$(req GET "/api/v1/pick-batches/groups?search=$GN")" | jq -e --arg g "$GP" '.total==1 and .items[0].publicId==$g' >/dev/null || fail "búsqueda por número"
MEMBER=$(echo "$G" | jq -r '.documents[0].batchPublicId')
expect 200 "$(req GET "/api/v1/pick-batches/$MEMBER")" | jq -e --arg n "$GN" '.canPack==false and .groupNumber==$n' >/dev/null || fail "la recolección miembro no se empaca suelta"
expect 422 "$(req POST "/api/v1/pick-batches/$MEMBER/pack" '{"order":{"serviceType":"STANDARD","packages":[{"pieces":1}]}}')" | jq -e '.title | contains("pertenece a la recolección maestra")' >/dev/null || fail "empacar suelta una del grupo → 422"; ok "búsqueda y 422 de empacar suelta"

step "empacar la maestra: UN envío"
LOC=$(expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$CA\",\"name\":\"Destino $TS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 1\",\"city\":\"Ponce\",\"country\":\"PR\"}")" | jq -r .publicId)
PK=$(expect 200 "$(req POST "/api/v1/pick-batches/groups/$GP/pack" "$(jq -cn --arg c "$CA" --arg l "$LOC" '{order:{clientPublicId:$c,consigneeLocationPublicId:$l,serviceType:"STANDARD",packages:[{pieces:1,packageType:"BOX"}]}}')")")
echo "$PK" | jq -e --arg n "$GN" '.group.statusCode=="PACKED" and .order.packBatchNumber==$n and .group.canPack==false' >/dev/null || fail "empaque de la maestra: $PK"
OPUB=$(echo "$PK" | jq -r .order.publicId)
expect 200 "$(req GET "/api/v1/pick-batches/groups/$GP")" | jq -e --arg o "$OPUB" '.orderPublicId==$o and .statusCode=="PACKED"' >/dev/null || fail "ficha empacada"
expect 200 "$(req GET "/api/v1/pick-batches/$MEMBER")" | jq -e --arg o "$OPUB" '.statusCode=="PACKED" and .orderPublicId==$o' >/dev/null || fail "los miembros comparten el envío"
expect 422 "$(req POST "/api/v1/pick-batches/groups/$GP/pack" "$(jq -cn --arg c "$CA" --arg l "$LOC" '{order:{clientPublicId:$c,consigneeLocationPublicId:$l,serviceType:"STANDARD",packages:[{pieces:1}]}}')")" >/dev/null || fail "segundo empaque debe ser 422"; ok "un solo envío $OPUB, miembros PACKED, segundo empaque 422"

step "eliminar la maestra empacada: vuelve todo"
expect 204 "$(req DELETE "/api/v1/pick-batches/groups/$GP" '{}')" >/dev/null
[[ $(onhand "$PA") == 10 ]] || fail "existencia de A tras eliminar: $(onhand "$PA")"
expect 200 "$(req GET "/api/v1/pick-batches/groups/$GP")" | jq -e '.statusCode=="CANCELLED" and .isActive==false' >/dev/null || fail "maestra cancelada"
expect 200 "$(req GET "/api/v1/exit-notices/$N1")" | jq -e '.statusCode=="REQUESTED" and .lines[0].qtyReserved==4' >/dev/null || fail "N1 recuperó su reserva"
expect 200 "$(req GET "/api/v1/exit-notices/$N2")" | jq -e '.lines[0].qtyReserved==5' >/dev/null || fail "N2 recuperó su reserva"
expect 200 "$(req GET "/api/v1/orders/$OPUB")" | jq -e '.isActive==false' >/dev/null || fail "el envío debe quedar dado de baja"
ok "inventario 10, avisos con su reserva, maestra CANCELLED"

printf '\n\033[1;32mSMOKE MAESTRA OK\033[0m\n'
