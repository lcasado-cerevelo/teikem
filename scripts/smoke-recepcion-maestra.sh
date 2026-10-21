#!/usr/bin/env bash
# Prueba de humo de la recepción maestra (Lote 182, C4) contra un API levantado: dos avisos de llegada del mismo cliente se abren y se confirman juntos, un aviso de otro dueño se rechaza,
# eliminar una maestra abierta libera los avisos y una confirmada ya no se elimina. Requiere: curl, jq. Uso: scripts/smoke-recepcion-maestra.sh [base_url]
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
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-recepcion-maestra\"}")" | jq -r .tokens.accessToken); ok "token"

step "almacén, posición de recepción, clientes y productos"
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"R$TS\",\"name\":\"Recepcion $TS\"}")" | jq -r .warehouse.publicId)
Z=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"STG","name":"Recepcion","zoneType":"STAGING"}')" | jq -r .id)
BS=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$Z,\"code\":\"S-01\"}")" | jq -r .id)
CA=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Recibo A $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId)
CB=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Recibo B $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId)
PA=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"RA$TS\",\"name\":\"Producto A $TS\",\"ownerClientPublicId\":\"$CA\"}")" | jq -r .product.publicId)
PB=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"RB$TS\",\"name\":\"Producto B $TS\",\"ownerClientPublicId\":\"$CB\"}")" | jq -r .product.publicId); ok "W=$W"
asn() { expect 200 "$(req POST /api/v1/asns "$(jq -cn --arg w "$W" --arg c "$1" --arg p "$2" --argjson q "$3" '{warehousePublicId:$w,clientPublicId:$c,reference:"humo",lines:[{productPublicId:$p,expectedQty:$q}]}')")" | jq -r .id; }
onhand() { expect 200 "$(req GET "/api/v1/inventory/balances?warehousePublicIds=$W&productPublicIds=$1&includeZero=true")" | jq '([.items[].qtyOnHand] | add // 0) + 0'; }
A1=$(asn "$CA" "$PA" 4); A2=$(asn "$CA" "$PA" 6); A3=$(asn "$CB" "$PB" 2); A4=$(asn "$CA" "$PA" 1); A5=$(asn "$CA" "$PA" 3); ok "avisos $A1 $A2 (A), $A3 (B), $A4 $A5 (A)"

step "validaciones: otro dueño, uno solo, repetido"
body() { jq -cn --argjson b "$BS" "$1"; }
expect 400 "$(req POST /api/v1/receipts/groups "$(body "{documents:[{asnId:$A1},{asnId:$A3}],stagingBinId:\$b}")")" | jq -e '.errors["documents[1]"][0] | contains("mismo dueño")' >/dev/null || fail "mezcla de dueños"
expect 400 "$(req POST /api/v1/receipts/groups "$(body "{documents:[{asnId:$A1}],stagingBinId:\$b}")")" | jq -e '.errors.documents[0] | contains("al menos dos")' >/dev/null || fail "uno solo"
expect 400 "$(req POST /api/v1/receipts/groups "$(body "{documents:[{asnId:$A1},{asnId:$A1}],stagingBinId:\$b}")")" | jq -e '.errors["documents[1]"][0]=="El mismo documento está repetido."' >/dev/null || fail "repetido"; ok "400 en los tres casos"

step "recibir juntos A1 + A2"
G=$(expect 200 "$(req POST /api/v1/receipts/groups "$(body "{documents:[{asnId:$A1},{asnId:$A2}],stagingBinId:\$b,carrier:\"Transportes $TS\",reference:\"BL-$TS\",note:\"humo\"}")")")
GP=$(echo "$G" | jq -r .publicId); GN=$(echo "$G" | jq -r .number)
echo "$G" | jq -e '.statusCode=="OPEN" and .memberCount==2 and .expectedQty==10 and .canConfirm and .canDelete and (.number|startswith("RCM-")) and ([.members[].originKind]|all(.=="ASN"))' >/dev/null || fail "ficha de la maestra: $G"
M1=$(echo "$G" | jq -r '.members[0].receiptPublicId')
expect 200 "$(req GET "/api/v1/receipts/$M1")" | jq -e --arg c "Transportes $TS" '.header.carrier==$c and .header.reference=="BL-'$TS'"' >/dev/null || fail "el recibo trae el transporte compartido"
expect 200 "$(req GET "/api/v1/receipts/groups?search=$GN")" | jq -e --arg g "$GP" '.total==1 and .items[0].publicId==$g' >/dev/null || fail "búsqueda por número"
expect 200 "$(req GET "/api/v1/receipts/groups?status=OPEN")" | jq -e --arg g "$GP" 'any(.items[]; .publicId==$g)' >/dev/null || fail "filtro OPEN"; ok "maestra $GN con 2 recibos"

step "un aviso que ya está en una maestra no se vuelve a recibir en otra"
expect 409 "$(req POST /api/v1/receipts/groups "$(body "{documents:[{asnId:$A2},{asnId:$A4}],stagingBinId:\$b}")")" >/dev/null; ok "409"

step "confirmar juntos"
D=$(expect 200 "$(req POST "/api/v1/receipts/groups/$GP/confirm" '{}')")
echo "$D" | jq -e '.statusCode=="CONFIRMED" and .confirmedCount==2 and .receivedQty==10 and (.canConfirm|not) and (.canDelete|not)' >/dev/null || fail "confirmación: $D"
[[ $(onhand "$PA") == 10 ]] || fail "existencia tras confirmar: $(onhand "$PA")"
expect 422 "$(req POST "/api/v1/receipts/groups/$GP/confirm" '{}')" | jq -e '.title=="Todos los recibos de esta recepción maestra ya están confirmados."' >/dev/null || fail "segunda confirmación"
expect 422 "$(req DELETE "/api/v1/receipts/groups/$GP")" | jq -e '.title | contains("ya está confirmado")' >/dev/null || fail "eliminar una confirmada"; ok "existencia 10 y 422 de repetir y de eliminar"

step "eliminar una maestra abierta libera los avisos"
G2=$(expect 200 "$(req POST /api/v1/receipts/groups "$(body "{documents:[{asnId:$A4},{asnId:$A5}],stagingBinId:\$b}")")"); G2P=$(echo "$G2" | jq -r .publicId)
expect 204 "$(req DELETE "/api/v1/receipts/groups/$G2P")" >/dev/null
expect 200 "$(req GET "/api/v1/receipts/groups/$G2P")" | jq -e '.statusCode=="CANCELLED" and .isActive==false' >/dev/null || fail "maestra cancelada"
G3=$(expect 200 "$(req POST /api/v1/receipts/groups "$(body "{documents:[{asnId:$A4},{asnId:$A5}],stagingBinId:\$b}")")"); echo "$G3" | jq -e '.statusCode=="OPEN"' >/dev/null || fail "los avisos deben quedar libres"; ok "eliminada y vuelta a abrir"

step "recibir juntos desde el aparato: la misma llave arma la maestra"
A6=$(asn "$CA" "$PA" 2); A7=$(asn "$CA" "$PA" 3); A8=$(asn "$CB" "$PB" 1); KEY=$(cat /proc/sys/kernel/random/uuid)
rc() { jq -cn --argjson a "$1" --arg k "$2" --argjson b "$BS" '{asnId:$a,stagingBinId:$b,groupKey:$k}'; }
expect 200 "$(req POST /api/v1/receipts "$(rc "$A6" "$KEY")")" >/dev/null
expect 200 "$(req POST /api/v1/receipts "$(rc "$A7" "$KEY")")" >/dev/null
expect 400 "$(req POST /api/v1/receipts "$(rc "$A8" "$KEY")")" | jq -e '.errors.groupKey[0] | contains("otro dueño")' >/dev/null || fail "otro dueño con la misma llave"
expect 200 "$(req GET "/api/v1/receipts/groups?search=LLE")" | jq -e --arg a "$A6" '[.items[] | select(.memberCount==2 and (.members|length)==2)] | length>=1' >/dev/null || fail "la maestra del aparato con dos recibos"; ok "dos recibos en una maestra y 400 por otro dueño"

printf '\n\033[1;32mSMOKE RECEPCIÓN MAESTRA OK\033[0m\n'
