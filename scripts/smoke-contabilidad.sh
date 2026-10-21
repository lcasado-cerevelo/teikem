#!/usr/bin/env bash
# Prueba de humo de la contabilización de compras y de despachos (Lote 181, H1) contra un API levantado: un recibo ciego de producto PROPIO (con costo) y una recolección, la corrida de compras
# y la de despachos (ver antes, generar, no repetir líneas, aceptar, exportar con la plantilla, cancelar y volver a generar) y que lo de un cliente no se contabilice.
# Requiere: curl, jq. Uso: scripts/smoke-contabilidad.sh [base_url]
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
TODAY=$(date -u +%F); FROM=$(date -u -d '-1 day' +%F); TO=$(date -u -d '+1 day' +%F)

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-contabilidad\"}")" | jq -r .tokens.accessToken); ok "token"

step "almacén, zonas y posiciones"
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"K$TS\",\"name\":\"Contabilidad $TS\"}")" | jq -r .warehouse.publicId)
ZS=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"STG","name":"Recepcion","zoneType":"STAGING"}')" | jq -r .id)
ZP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"PCK","name":"Picking","zoneType":"PICKING"}')" | jq -r .id)
BS=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$ZS,\"code\":\"S-01\"}")" | jq -r .id)
BP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$ZP,\"code\":\"P-01\"}")" | jq -r .id); ok "W=$W"

step "producto PROPIO (costo 5) y producto de un cliente (costo 9)"
CL=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Cliente conta $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId)
PO=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"KP$TS\",\"name\":\"Propio $TS\",\"purchaseCost\":5}")" | jq -r .product.publicId)
PC=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"KC$TS\",\"name\":\"Del cliente $TS\",\"ownerClientPublicId\":\"$CL\",\"purchaseCost\":9}")" | jq -r .product.publicId); ok "propio y del cliente"

step "recibos ciegos confirmados: 10 propios y 4 del cliente"
blind() { expect 200 "$(req POST /api/v1/receipts "$(jq -cn --arg w "$W" --argjson s "$BS" --arg p "$1" --argjson q "$2" '{warehousePublicId:$w,type:"BLIND",stagingBinId:$s,confirm:true,lines:[{productPublicId:$p,receivedQty:$q}]}')")" >/dev/null; }
blind "$PO" 10; blind "$PC" 4; ok "recibos confirmados"

step "stock en picking y una recolección de 3 propios (+ 2 del cliente)"
adj() { expect 200 "$(req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$1" --arg w "$W" --argjson b "$BP" --argjson q "$2" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:$q,reason:"FOUND",notes:"humo contabilidad"}')")" >/dev/null; }
adj "$PO" 8; adj "$PC" 5
expect 200 "$(req POST /api/v1/pick-batches "$(jq -cn --arg w "$W" --arg p "$PO" '{warehousePublicId:$w,lines:[{productPublicId:$p,quantity:3}]}')")" >/dev/null
expect 200 "$(req POST /api/v1/pick-batches "$(jq -cn --arg w "$W" --arg p "$PC" '{warehousePublicId:$w,lines:[{productPublicId:$p,quantity:2}]}')")" >/dev/null; ok "2 recolecciones"

step "compras: ver antes y generar (solo lo propio)"
RQ="{\"periodStart\":\"$FROM\",\"periodEnd\":\"$TO\"}"
PV=$(expect 200 "$(req POST /api/v1/purchase-accounting/preview "$RQ")")
echo "$PV" | jq -e '[.lines[] | select(.sku=="KP'$TS'")] | length==1 and (.[0].quantity==10) and (.[0].unitCost==5) and (.[0].totalCost==50)' >/dev/null || fail "vista previa de compras: $PV"
echo "$PV" | jq -e '[.lines[] | select(.sku=="KC'$TS'")] | length==0' >/dev/null || fail "lo del cliente no debe contabilizarse"
R=$(expect 200 "$(req POST /api/v1/purchase-accounting "$RQ")"); RP=$(echo "$R" | jq -r .publicId); RN=$(echo "$R" | jq -r .number)
echo "$R" | jq -e '.statusCode=="GENERATED" and .kind=="PURCHASES" and (.number|startswith("CMP-"))' >/dev/null || fail "corrida de compras: $R"
ok "corrida $RN"

step "una línea no entra dos veces"
expect 422 "$(req POST /api/v1/purchase-accounting "$RQ")" | jq -e '.title=="No hay nada por contabilizar en el período."' >/dev/null || fail "segunda corrida debe ser 422"
expect 200 "$(req POST /api/v1/purchase-accounting/preview "$RQ")" | jq -e '[.lines[] | select(.sku=="KP'$TS'")] | length==0' >/dev/null || fail "la vista previa no debe repetir la línea"; ok "422 y vista previa vacía"

step "exportar antes de aceptar: 422; aceptar; exportar con la plantilla"
expect 422 "$(req POST "/api/v1/purchase-accounting/$RP/export")" | jq -e '.title=="Solo se exporta una corrida aceptada o exportada."' >/dev/null || fail "exportar sin aceptar"
expect 200 "$(req POST "/api/v1/purchase-accounting/$RP/accept")" | jq -e '.statusCode=="ACCEPTED"' >/dev/null || fail "aceptar"
TPL=$(expect 200 "$(req GET "/api/v1/export-templates?scope=PURCHASE_ACCOUNTING")"); echo "$TPL" | jq -e 'length>=1 and .[0].scope=="PURCHASE_ACCOUNTING"' >/dev/null || fail "plantilla de compras: $TPL"
F=$(expect 200 "$(req POST "/api/v1/purchase-accounting/$RP/export")")
echo "$F" | jq -e '.text | startswith("RunNo,PeriodStart") and contains("'$RN'") and contains("KP'$TS'")' >/dev/null || fail "archivo de compras: $F"
expect 200 "$(req GET "/api/v1/purchase-accounting/$RP")" | jq -e '.statusCode=="EXPORTED" and .exportedAtUtc!=null' >/dev/null || fail "exportada"; ok "exportada con $(echo "$F" | jq -r .fileName)"

step "una corrida de compras no se ve desde despachos"
expect 404 "$(req GET "/api/v1/dispatch-accounting/$RP")" >/dev/null; ok "404 de otro tipo"

step "despachos: solo lo propio, aceptar, cancelar y volver a generar"
DR=$(expect 200 "$(req POST /api/v1/dispatch-accounting "$RQ")"); DP=$(echo "$DR" | jq -r .publicId)
echo "$DR" | jq -e '.kind=="DISPATCHES" and (.number|startswith("DSP-")) and ([.lines[].sku]|index("KP'$TS'")!=null) and ([.lines[].sku]|index("KC'$TS'")==null)' >/dev/null || fail "despachos: $DR"
echo "$DR" | jq -e '[.lines[] | select(.sku=="KP'$TS'")][0] | .quantity==3 and .unitCost==5 and .totalCost==15' >/dev/null || fail "línea de despacho: $DR"
expect 200 "$(req POST "/api/v1/dispatch-accounting/$DP/cancel" '{"comment":"humo"}')" | jq -e '.statusCode=="CANCELLED"' >/dev/null || fail "cancelar"
DR2=$(expect 200 "$(req POST /api/v1/dispatch-accounting "$RQ")"); echo "$DR2" | jq -e '([.lines[].sku]|index("KP'$TS'")!=null)' >/dev/null || fail "tras cancelar, la línea debe volver a ofrecerse"
expect 422 "$(req POST "/api/v1/dispatch-accounting/$DP/cancel" '{}')" | jq -e '.title=="Solo se cancela una corrida generada o aceptada."' >/dev/null || fail "cancelar una cancelada"
expect 200 "$(req GET "/api/v1/dispatch-accounting?status=GENERATED")" | jq -e '.total>=1' >/dev/null || fail "lista"; ok "despachos OK"

printf '\n\033[1;32mSMOKE CONTABILIDAD OK\033[0m\n'
