#!/usr/bin/env bash
# Prueba de humo de Compras con plantilla y QuickBooks (Lote 246, Lote F del plan de Solutions) contra un API levantado: plantilla de importación PURCHASE_ORDER (campos, validaciones,
# 400/409, sin cliente, tipo cruzado), importar con plantilla (vista previa, crear SENT sin mover inventario, origen EXCEL, no duplica, dos importaciones simultáneas, BAD_VALUE, COST_CONFLICT,
# 403), origen QUICKBOOKS, exportación IIF PURCHORD (cuadre de cada transacción, 422/400/409, dos exportaciones simultáneas), confirmar / «No pasó» / quitar la marca, «ir y venir»,
# filtros de la lista, cuenta de órdenes de compra, permiso purchasing.quickbooks y aislamiento entre compañías. Cada mensaje de error se compara con el texto exacto.
# LIMITACIÓN: el formato PURCHORD se construyó con la especificación IIF y NO se ha probado contra un QuickBooks Desktop real (pendiente: copia de la compañía de Solutions); este humo solo
# comprueba que el archivo sea consistente (PURCHORD, !VEND, cuenta y que cada transacción sume 0.00), no que QuickBooks lo acepte.
# NUNCA contra Brevo ni correo real: se NIEGA a correr si el canal EMAIL está configurado y no es local (buzón). Solo contra un API en Development, nunca contra TEST ni producción: crea datos
# con el sufijo $TS (proveedor, productos, almacén, órdenes, plantilla, usuario, compañía) y no los borra.
# Requiere: curl, jq, base64, awk. Uso: scripts/smoke-compras-quickbooks.sh [base_url]   (API de pruebas: scripts/api-para-pruebas.ps1, con Auth__Onboarding__Enabled=false)
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
# imp ARCHIVO CAMPOS_FORM... : POST multipart a /purchase-orders/import (token de $TOKEN o de IMPTOK); imprime cuerpo y código en la última línea
imp() { local file=$1; shift; local args=(-sS -X POST "$BASE/api/v1/purchase-orders/import" -H 'Accept: application/json' -H 'X-Lang: es' -H "Authorization: Bearer ${IMPTOK:-$TOKEN}" -F "file=@$file")
  local kv; for kv in "$@"; do args+=(-F "$kv"); done; curl "${args[@]}" -w '\n%{http_code}'; }
PO_=/api/v1/purchase-orders
QB_=/api/v1/purchase-orders/quickbooks
TPL_=/api/v1/purchase-orders/import-templates
TS=$(date +%s)

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-compras-qb\"}")" | jq -r .tokens.accessToken); ok "token"
TID=$(expect 200 "$(req GET /api/v1/me)" | jq -r .tenantId)

step "guarda de correo: nunca contra Brevo"
CH=$(expect 200 "$(req GET /api/v1/notifications/channels)")
EMAIL_CONF=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isConfigured // false')
EMAIL_LOCAL=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isLocal // false')
if [[ "$EMAIL_CONF" == "true" && "$EMAIL_LOCAL" != "true" ]]; then fail "el API tiene correo real (Brevo): esta prueba no corre así; levante el API sin llave (scripts/api-para-pruebas.ps1)"; fi
ok "sin correo real"

# Los módulos (PURCHASING, SALES_ORDERS) los enciende solo el administrador de la plataforma, con reautenticación. Encender uno que ya está encendido no hace daño.
PEMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"; PPASS="${TEIKEM_PLATFORM_PASSWORD:-$PASS}"
platform_token() { # tenantId -> token de la plataforma reautenticado en esa compañía
  local t; t=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\",\"tenantId\":$1,\"deviceInfo\":\"smoke-compras-qb\"}")" | jq -r .tokens.accessToken)
  expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PPASS\"}" "$t")" | jq -r .accessToken; }
enable_module() { # TOKEN_PLATAFORMA MODULO: lo enciende y, si el API dice «'X' depende de 'Y'», enciende Y antes
  local ptok=$1 want=$2 mod=$2 r c dep
  for _ in 1 2 3 4 5 6 7 8; do
    r=$(req PUT /api/v1/modules/$mod '{"isEnabled":true}' "$ptok"); c=$(echo "$r" | tail -n1)
    if [[ "$c" == "200" ]]; then [[ "$mod" == "$want" ]] && return 0; mod=$want; continue; fi
    dep=$(echo "$r" | sed '$d' | jq -r '.title' | sed -n "s/.*depende de '\([A-Z_]*\)'.*/\1/p"); [[ -n "$dep" ]] || fail "no se pudo encender $mod: $r"
    mod=$dep
  done
  fail "$want no quedó encendido: $r"; }
step "módulos PURCHASING y SALES_ORDERS encendidos (los ajustes de QuickBooks viven en ventas)"
PTOK=$(platform_token "$TID"); enable_module "$PTOK" PURCHASING; enable_module "$PTOK" SALES_ORDERS; ok "módulos encendidos"

step "datos de la prueba: proveedor, productos, almacén, cliente y usuario sin purchasing.quickbooks"
SUPNAME="Proveedor QB $TS"
SUP=$(expect 200 "$(req POST /api/v1/suppliers "{\"name\":\"$SUPNAME\"}")" | jq -r .id)
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"QB$TS\",\"name\":\"Compras QB $TS\"}")" | jq -r .warehouse.publicId)
SKA="QA$TS"; SKB="QB$TS"; SKX="NOEXISTE$TS"
PA=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"$SKA\",\"name\":\"Producto A $TS\",\"purchaseCost\":2.5,\"salePrice\":7}")" | jq -r .product.publicId)
PB=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"$SKB\",\"name\":\"Producto B $TS\",\"purchaseCost\":1,\"salePrice\":4}")" | jq -r .product.publicId)
CLI=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Cliente QB $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId)
RN="Compras sin QB $TS"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"$RN\",\"permissions\":[\"purchasing.view\",\"purchasing.manage\"]}")" >/dev/null
UEMAIL="comprasqb$TS@teikem.local"
expect 200 "$(req POST /api/v1/users "{\"email\":\"$UEMAIL\",\"fullName\":\"Compras sin QB $TS\",\"password\":\"$PASS\",\"roles\":[\"$RN\"]}")" >/dev/null
T2=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$UEMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-compras-qb\"}")" | jq -r .tokens.accessToken)
RN3="Compras solo ver $TS"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"$RN3\",\"permissions\":[\"purchasing.view\"]}")" >/dev/null
U3EMAIL="comprasver$TS@teikem.local"
expect 200 "$(req POST /api/v1/users "{\"email\":\"$U3EMAIL\",\"fullName\":\"Compras solo ver $TS\",\"password\":\"$PASS\",\"roles\":[\"$RN3\"]}")" >/dev/null
T3V=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$U3EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-compras-qb\"}")" | jq -r .tokens.accessToken); ok "SUP=$SUP W=$W"

step "campos de la plantilla PURCHASE_ORDER"
F=$(expect 200 "$(req GET $TPL_/fields)")
echo "$F" | jq -e 'length==8 and ([.[]|select(.required)|.field]|sort==["documentNumber","quantity","sku","supplier"]) and ([.[]|select(.defaultAllowed)|.field]==["supplier"])' >/dev/null || fail "campos: $F"
ok "8 campos; obligatorios supplier, documentNumber, sku y quantity; solo supplier con valor fijo"

step "plantillas inválidas, repetida, con cliente y tipo cruzado"
msg "$(expect 400 "$(req POST $TPL_ "{\"name\":\"Sin sku $TS\",\"delimiter\":\";\",\"hasHeader\":false,\"columns\":[{\"position\":1,\"field\":\"documentNumber\"},{\"position\":2,\"field\":\"quantity\"}],\"defaults\":{\"supplier\":\"$SUPNAME\"}}")")" "La plantilla debe incluir la columna sku."
msg "$(expect 400 "$(req POST $TPL_ "{\"name\":\"Sin numero $TS\",\"delimiter\":\";\",\"hasHeader\":false,\"columns\":[{\"position\":1,\"field\":\"sku\"},{\"position\":2,\"field\":\"quantity\"}],\"defaults\":{\"supplier\":\"$SUPNAME\"}}")")" "La plantilla debe incluir la columna documentNumber."
msg "$(expect 400 "$(req POST $TPL_ "{\"name\":\"Sin proveedor $TS\",\"delimiter\":\";\",\"hasHeader\":false,\"columns\":[{\"position\":1,\"field\":\"documentNumber\"},{\"position\":2,\"field\":\"sku\"},{\"position\":3,\"field\":\"quantity\"}]}")")" "La plantilla debe indicar el proveedor: en la columna supplier o como valor fijo."
TPLBODY="{\"name\":\"Excel compras $TS\",\"delimiter\":\";\",\"hasHeader\":false,\"columns\":[{\"position\":1,\"field\":\"documentNumber\"},{\"position\":2,\"field\":\"sku\"},{\"position\":3,\"field\":\"quantity\"},{\"position\":4,\"field\":\"unitCost\"},{\"position\":5,\"field\":\"orderDate\"}],\"defaults\":{\"supplier\":\"$SUPNAME\"}}"
TP=$(expect 200 "$(req POST $TPL_ "$TPLBODY")")
echo "$TP" | jq -e '.kind=="PURCHASE_ORDER"' >/dev/null || fail "plantilla: $TP"
TPID=$(echo "$TP" | jq -r .publicId)
msg "$(expect 409 "$(req POST $TPL_ "$TPLBODY")")" "Ya existe una plantilla de importación con ese nombre."
msg "$(expect 400 "$(req POST $TPL_ "$(echo "$TPLBODY" | jq -c --arg c "$CLI" '.name+=" cliente" | .clientPublicId=$c')")")" "Este tipo de plantilla no lleva cliente."
msg "$(expect 400 "$(req GET "/api/v1/import-templates?kind=PURCHASE_ORDER")")" "Tipo de plantilla desconocido: PURCHASE_ORDER."
expect 200 "$(req GET $TPL_/$TPID)" | jq -e '.kind=="PURCHASE_ORDER"' >/dev/null || fail "GET de la plantilla"
expect 200 "$(req GET $TPL_)" | jq -e --arg p "$TPID" 'any(.[]; .publicId==$p)' >/dev/null || fail "la plantilla no sale en la lista"; ok "400 por columna faltante, 409 por nombre, sin cliente, ruta de envíos no la conoce"

step "importar con plantilla: errores de la petición"
F0="$TMPD/vacio.csv"; printf '' > "$F0"
printf 'X1;%s;1;1;2026-11-20\n' "$SKA" > "$TMPD/x.csv"
msg "$(expect 400 "$(imp "$TMPD/x.csv" "source=TEMPLATE" "commit=false")")" "Elija la plantilla de importación."
msg "$(expect 400 "$(imp "$TMPD/x.csv" "source=GENERIC" "templatePublicId=$TPID" "commit=false")")" "La plantilla solo se usa con el origen TEMPLATE."
msg "$(expect 400 "$(imp "$TMPD/x.csv" "source=OTRO" "commit=false")")" "El origen de la importación no es válido: use GENERIC, QUICKBOOKS o TEMPLATE."
expect 403 "$(IMPTOK=$T3V imp "$TMPD/x.csv" "source=TEMPLATE" "templatePublicId=$TPID" "commit=false")" >/dev/null   # el permiso lo corta el atributo: 403 sin cuerpo
ok "plantilla obligatoria, solo con TEMPLATE, origen inválido y 403 sin purchasing.manage"

step "importar con plantilla: vista previa, crear SENT, origen EXCEL y no duplicar"
D1="D1-$TS"; D2="D2-$TS"
printf '%s;%s;5;2.50;2026-11-20\n%s;%s;2;1.25;2026-11-20\n%s;%s;3;1.00;2026-11-20\n' "$D1" "$SKA" "$D1" "$SKB" "$D2" "$SKX" > "$TMPD/plantilla.csv"
NPO0=$(expect 200 "$(req GET "$PO_")" | jq 'if type=="array" then length else (.total // (.items|length)) end')
R=$(expect 200 "$(imp "$TMPD/plantilla.csv" "source=TEMPLATE" "templatePublicId=$TPID" "status=SENT" "warehousePublicId=$W" "commit=false")")
echo "$R" | jq -e --arg s "$SKX" '.committed==false and .source=="TEMPLATE" and .ready==1 and .blocked==1 and .created==0 and any(.missingProducts[]; (.sku|ascii_upcase)==($s|ascii_upcase))' >/dev/null || fail "vista previa: $R"
[[ "$(expect 200 "$(req GET "$PO_")" | jq 'if type=="array" then length else (.total // (.items|length)) end')" == "$NPO0" ]] || fail "la vista previa creó órdenes"
STK0=$(expect 200 "$(req GET "/api/v1/inventory/transactions?productPublicId=$PA")" | jq '.total // 0')
R=$(expect 200 "$(imp "$TMPD/plantilla.csv" "source=TEMPLATE" "templatePublicId=$TPID" "status=SENT" "warehousePublicId=$W" "commit=true")")
echo "$R" | jq -e '.committed==true and .created==1 and .failed==0 and .templateName != null' >/dev/null || fail "importar: $R"
POA=$(echo "$R" | jq -r '[.documents[]|select(.purchaseOrderPublicId!=null)][0].purchaseOrderPublicId'); [[ -n "$POA" && "$POA" != null ]] || fail "sin orden creada: $R"
A=$(expect 200 "$(req GET "$PO_/$POA")")
echo "$A" | jq -e '.statusCode=="SENT" and .originCode=="EXCEL" and .quickBooksState=="PENDING" and (.notes|contains("Importada con la plantilla")) and (.lines|length)==2' >/dev/null || fail "orden importada: $A"
POA_N=$(echo "$A" | jq -r .number)
[[ "$(expect 200 "$(req GET "/api/v1/inventory/transactions?productPublicId=$PA")" | jq '.total // 0')" == "$STK0" ]] || fail "la orden de compra movió inventario"
R=$(expect 200 "$(imp "$TMPD/plantilla.csv" "source=TEMPLATE" "templatePublicId=$TPID" "status=SENT" "warehousePublicId=$W" "commit=true")")
echo "$R" | jq -e '.created==0 and .duplicates==1' >/dev/null || fail "reimportar: $R"; ok "vista previa sin guardar; SENT, EXCEL, PENDING, sin inventario; reimportar → duplicates==1"

step "importación simultánea del mismo archivo nuevo: se crea una sola vez"
D3="D3-$TS"
printf '%s;%s;4;2.50;2026-11-21\n' "$D3" "$SKA" > "$TMPD/nuevo.csv"
for i in 1 2; do imp "$TMPD/nuevo.csv" "source=TEMPLATE" "templatePublicId=$TPID" "status=SENT" "warehousePublicId=$W" "commit=true" > "$TMPD/c$i" & done; wait
for i in 1 2; do [[ "$(tail -n1 "$TMPD/c$i")" == "200" ]] || fail "importación simultánea $i: $(cat "$TMPD/c$i")"; done
CR=$(for i in 1 2; do sed '$d' "$TMPD/c$i" | jq '.created'; done | awk "{s+=\$1} END{print s}"); DU=$(for i in 1 2; do sed '$d' "$TMPD/c$i" | jq '.duplicates'; done | awk "{s+=\$1} END{print s}")
[[ "$CR" == "1" && "$DU" == "1" ]] || fail "simultáneas: created=$CR duplicates=$DU (esperado 1 y 1)"
POC=$(for i in 1 2; do sed '$d' "$TMPD/c$i" | jq -r '[.documents[]|select(.purchaseOrderPublicId!=null)][0].purchaseOrderPublicId // empty'; done | head -n1); [[ -n "$POC" ]] || fail "sin la orden de la importación simultánea"; ok "created=1 y duplicates=1"

step "importación con valores ilegibles, sin número y con costos distintos"
printf 'E1-%s;%s;1;1;abc\n' "$TS" "$SKA" > "$TMPD/fecha.csv"
R=$(expect 200 "$(imp "$TMPD/fecha.csv" "source=TEMPLATE" "templatePublicId=$TPID" "warehousePublicId=$W" "commit=false")")
echo "$R" | jq -e 'any(.documents[].issues[]; .code=="BAD_VALUE" and .blocking) and .blocked==1' >/dev/null || fail "fecha abc: $R"
printf ';%s;1;1;2026-11-20\n' "$SKA" > "$TMPD/sinnumero.csv"
R=$(expect 200 "$(imp "$TMPD/sinnumero.csv" "source=TEMPLATE" "templatePublicId=$TPID" "warehousePublicId=$W" "commit=false")")
echo "$R" | jq -e 'any(.documents[].issues[]; .code=="BAD_VALUE" and .blocking) and .blocked>=1' >/dev/null || fail "sin número: $R"
printf 'E3-%s;%s;1;1.00;2026-11-20\nE3-%s;%s;1;1.10;2026-11-20\n' "$TS" "$SKA" "$TS" "$SKA" > "$TMPD/costos.csv"
R=$(expect 200 "$(imp "$TMPD/costos.csv" "source=TEMPLATE" "templatePublicId=$TPID" "warehousePublicId=$W" "commit=false")")
echo "$R" | jq -e '.blocked==1 and any(.documents[].issues[]; .code=="COST_CONFLICT" and .blocking)' >/dev/null || fail "COST_CONFLICT: $R"; ok "BAD_VALUE (fecha y número de documento) y COST_CONFLICT bloquean"

step "origen QUICKBOOKS y el estado FROM_QUICKBOOKS"
QBN="QBPO-$TS"; FECHAQB=$(date +%m/%d/%Y)
printf 'Type,Date,Num,Name,Item,Qty,Cost Price\nPurchase Order,%s,%s,%s,%s (Producto A),3,2.50\n' "$FECHAQB" "$QBN" "$SUPNAME" "$SKA" > "$TMPD/qb.csv"
R=$(expect 200 "$(imp "$TMPD/qb.csv" "source=QUICKBOOKS" "status=SENT" "warehousePublicId=$W" "commit=true")")
echo "$R" | jq -e '.source=="QUICKBOOKS" and .created==1' >/dev/null || fail "importar de QuickBooks: $R"
POQ=$(echo "$R" | jq -r '[.documents[]|select(.purchaseOrderPublicId!=null)][0].purchaseOrderPublicId')
expect 200 "$(req GET "$PO_/$POQ")" | jq -e '.originCode=="QUICKBOOKS" and .quickBooksState=="FROM_QUICKBOOKS"' >/dev/null || fail "orden de QuickBooks"; ok "originCode QUICKBOOKS y FROM_QUICKBOOKS"

step "órdenes extra: la que se usará en «No pasó» y un borrador"
D4="D4-$TS"; printf '%s;%s;1;1.00;2026-11-22\n' "$D4" "$SKB" > "$TMPD/d4.csv"
R=$(expect 200 "$(imp "$TMPD/d4.csv" "source=TEMPLATE" "templatePublicId=$TPID" "status=SENT" "warehousePublicId=$W" "commit=true")")
POB=$(echo "$R" | jq -r '[.documents[]|select(.purchaseOrderPublicId!=null)][0].purchaseOrderPublicId'); [[ -n "$POB" && "$POB" != null ]] || fail "sin la orden D4: $R"
DR=$(expect 200 "$(req POST $PO_ "$(jq -cn --argjson s "$SUP" --arg w "$W" --arg p "$PA" '{supplierId:$s,warehousePublicId:$w,lines:[{productPublicId:$p,qtyOrdered:1,unitCost:1}]}')")")
POD=$(echo "$DR" | jq -r .publicId); POD_N=$(echo "$DR" | jq -r .number); ok "POB=$POB borrador=$POD_N"

step "por pasar"
P=$(expect 200 "$(req GET $QB_)")
echo "$P" | jq -e --arg a "$POA" --arg q "$POQ" --arg d "$POD" '(.orders|any(.publicId==$a)) and (.orders|all(.publicId!=$q and .publicId!=$d)) and .purchaseOrderAccount=="Purchase Orders"' >/dev/null || fail "por pasar: $(echo "$P" | jq -c '{orders:[.orders[].number],purchaseOrderAccount}')"; ok "la orden EXCEL sale; la de QuickBooks y el borrador no; cuenta «Purchase Orders»"

step "errores al exportar"
msg "$(expect 422 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$POQ\"]}")")" "vino de QuickBooks: no se exporta ni se marca."
msg "$(expect 422 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$POD\"]}")")" "está en Borrador: solo se pasan a QuickBooks las enviadas, recibidas en parte o recibidas."
msg "$(expect 400 "$(req POST $QB_/export '{}')")" "Indique las órdenes de compra (purchaseOrderIds) o pida todas (all = true)."
msg "$(expect 400 "$(req POST $QB_/export '{"purchaseOrderIds":["00000000-0000-0000-0000-000000000001"]}')")" "Alguna de las órdenes de compra indicadas no existe."; ok "422 QuickBooks, 422 borrador, 400 sin selección, 400 inexistente"

step "exportar: archivo IIF PURCHORD que cuadra"
X=$(expect 200 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$POA\"]}")")
echo "$X" | jq -e '(.fileName|endswith(".iif")) and .orderCount==1' >/dev/null || fail "export: $(echo "$X" | jq -c 'del(.contentBase64)')"
echo "$X" | jq -r .contentBase64 | base64 -d > "$TMPD/a.iif"
grep -aq 'PURCHORD' "$TMPD/a.iif" || fail "el IIF no trae PURCHORD"
grep -aq '^!TRNS' "$TMPD/a.iif" || fail "el IIF no trae !TRNS"
grep -aq '^!INVITEM' "$TMPD/a.iif" || fail "el IIF no trae !INVITEM"
grep -aq '^VEND' "$TMPD/a.iif" && fail "el proveedor ya está en QuickBooks (la orden de QuickBooks lo marcó): no debía salir como nuevo"
grep -aq 'Purchase Orders' "$TMPD/a.iif" || fail "el IIF no trae la cuenta «Purchase Orders»"
tr -d '\r' < "$TMPD/a.iif" | awk -F'\t' '$1=="TRNS"||$1=="SPL"{s+=$7} $1=="ENDTRNS"{if(s>0.004||s<-0.004)bad=1; s=0} END{exit bad}' || fail "una transacción del IIF no cuadra a 0.00"
expect 200 "$(req GET "$PO_/$POA")" | jq -e '.quickBooksState=="EXPORTED" and .quickBooksAtUtc==null and (.quickBooksExportedByName|length)>0' >/dev/null || fail "la orden exportada no quedó EXPORTED"; ok "PURCHORD, !TRNS, !VEND, cuenta, TRNS + Σ SPL = 0.00; la orden queda «exportada, por confirmar»"

step "proveedor nuevo: sale en !VEND"
SUP2NAME="Proveedor nuevo QB $TS"
SUP2=$(expect 200 "$(req POST /api/v1/suppliers "{\"name\":\"$SUP2NAME\"}")" | jq -r .id)
PON=$(expect 200 "$(req POST $PO_ "$(jq -cn --argjson s "$SUP2" --arg w "$W" --arg p "$PA" '{supplierId:$s,warehousePublicId:$w,lines:[{productPublicId:$p,qtyOrdered:2,unitCost:3}]}')")" | jq -r .publicId)
expect 200 "$(req POST "$PO_/$PON/send" '{}')" >/dev/null
expect 200 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$PON\"]}")" | jq -r .contentBase64 | base64 -d > "$TMPD/n.iif"
grep -aq '^!VEND' "$TMPD/n.iif" && grep -aq "^VEND.*$SUP2NAME" "$TMPD/n.iif" || fail "el IIF no trae el proveedor nuevo en !VEND"
expect 200 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$PON\"],\"includeNew\":false}")" | jq -r .contentBase64 | base64 -d > "$TMPD/n2.iif"
grep -aq '^VEND' "$TMPD/n2.iif" && fail "includeNew=false no debía llevar proveedores"; ok "!VEND con el proveedor nuevo; includeNew=false sin !VEND"

step "dos exportaciones simultáneas de la misma orden: el candado las pone en fila"
for i in 1 2; do req POST $QB_/export "{\"purchaseOrderIds\":[\"$POC\"]}" > "$TMPD/e$i" & done; wait
for i in 1 2; do [[ "$(tail -n1 "$TMPD/e$i")" == "200" ]] || fail "export simultáneo $i: $(cat "$TMPD/e$i" | cut -c1-300)"; done
AE=$(for i in 1 2; do sed '$d' "$TMPD/e$i" | jq -c '.alreadyExportedCount'; done | sort | paste -sd,)
[[ "$AE" == "0,1" ]] || fail "alreadyExportedCount de los dos exports: $AE (esperado uno 0 y uno 1)"; ok "los dos 200 y exactamente uno con alreadyExportedCount==1"

step "confirmar: «Sí, ya están»"
msg "$(expect 422 "$(req POST $QB_/confirm "{\"purchaseOrderIds\":[\"$POB\"]}")")" "no ha salido en ningún archivo de QuickBooks"
expect 200 "$(req POST $QB_/confirm "{\"purchaseOrderIds\":[\"$POA\"]}")" | jq -e '.marked==1' >/dev/null || fail "confirmar"
expect 200 "$(req GET "$PO_/$POA")" | jq -e '.quickBooksState=="IN_QUICKBOOKS" and (.quickBooksByName|length)>0' >/dev/null || fail "IN_QUICKBOOKS"
msg "$(expect 409 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$POA\"]}")")" "ya está en QuickBooks; para volver a exportarla, quítele la marca."; ok "422 sin exportar; IN_QUICKBOOKS con quién; 409 al exportar otra vez"

step "«No pasó»"
expect 200 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$POB\"]}")" >/dev/null
expect 200 "$(req POST $QB_/not-passed "{\"purchaseOrderIds\":[\"$POB\"]}")" | jq -e '.reset==1' >/dev/null || fail "not-passed"
expect 200 "$(req GET "$PO_/$POB")" | jq -e '.quickBooksState=="PENDING" and .quickBooksExportedAtUtc==null' >/dev/null || fail "POB no volvió a PENDING"
msg "$(expect 409 "$(req POST $QB_/not-passed "{\"purchaseOrderIds\":[\"$POA\"]}")")" "ya está marcada como en QuickBooks: quítele la marca en vez de usar «No pasó»."
msg "$(expect 422 "$(req POST $QB_/not-passed "{\"purchaseOrderIds\":[\"$POQ\"]}")")" "vino de QuickBooks: no se exporta ni se marca."; ok "vuelve a PENDING; marcada 409; de QuickBooks 422"

step "quitar la marca"
expect 200 "$(req POST $QB_/unmark "{\"purchaseOrderIds\":[\"$POA\"]}")" >/dev/null
expect 200 "$(req GET "$PO_/$POA")" | jq -e '.quickBooksState=="EXPORTED"' >/dev/null || fail "unmark → EXPORTED"
expect 200 "$(req POST $QB_/confirm "{\"purchaseOrderIds\":[\"$POA\"]}")" >/dev/null; ok "EXPORTED y de nuevo IN_QUICKBOOKS"

step "ir y venir: lo que sale de Teikem y vuelve en un archivo de QuickBooks no se duplica"
printf 'Type,Date,Num,Name,Item,Qty,Cost Price\nPurchase Order,%s,%s,%s,%s (Producto A),5,2.50\n' "$FECHAQB" "$POA_N" "$SUPNAME" "$SKA" > "$TMPD/vuelta.csv"
R=$(expect 200 "$(imp "$TMPD/vuelta.csv" "source=QUICKBOOKS" "status=SENT" "warehousePublicId=$W" "commit=true")")
echo "$R" | jq -e '.duplicates==1 and .created==0' >/dev/null || fail "ir y venir: $R"
msg "$R" "nació en Teikem y ya se pasó a QuickBooks; no se vuelve a importar."; ok "duplicates==1 con el aviso"

step "filtros de la lista"
expect 200 "$(req GET "$PO_?quickBooks=IN_QUICKBOOKS&take=200")" | jq -e --arg a "$POA" '[(.items // .)[]|select(.publicId==$a)]|length==1' >/dev/null || fail "filtro quickBooks=IN_QUICKBOOKS"
expect 200 "$(req GET "$PO_?origin=EXCEL&take=200")" | jq -e --arg a "$POA" '[(.items // .)[]|select(.publicId==$a)]|length==1' >/dev/null || fail "filtro origin=EXCEL"
expect 200 "$(req GET "$PO_?quickBooks=FROM_QUICKBOOKS&take=200")" | jq -e --arg q "$POQ" --arg a "$POA" '(.items // .) as $l | ([$l[]|select(.publicId==$q)]|length)==1 and ([$l[]|select(.publicId==$a)]|length)==0' >/dev/null || fail "filtro quickBooks=FROM_QUICKBOOKS"
msg "$(expect 400 "$(req GET "$PO_?origin=OTRO")")" "El origen «OTRO» no es válido: use TEIKEM, EXCEL o QUICKBOOKS."
msg "$(expect 400 "$(req GET "$PO_?quickBooks=X")")" "El estado de QuickBooks «X» no es válido: use PENDING, EXPORTED, IN_QUICKBOOKS, FROM_QUICKBOOKS o NOT_EXPORTABLE."; ok "filtros origin y quickBooks, y sus 400"

step "cuenta de órdenes de compra"
SET=/api/v1/sales-orders/quickbooks/settings
expect 200 "$(req PATCH $SET '{"purchaseOrderAccount":"Ordenes de compra"}')" >/dev/null
expect 200 "$(req GET $SET)" | jq -e '.purchaseOrderAccount=="Ordenes de compra"' >/dev/null || fail "la cuenta no se guardó"
expect 200 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$POB\"]}")" | jq -r .contentBase64 | base64 -d > "$TMPD/b.iif"
grep -aq 'Ordenes de compra' "$TMPD/b.iif" || fail "el TRNS no lleva la cuenta configurada"
msg "$(expect 400 "$(req PATCH $SET "{\"purchaseOrderAccount\":\"$(printf 'c%.0s' $(seq 1 160))\"}")")" "El nombre de una cuenta de QuickBooks no puede pasar de 159 caracteres."
expect 200 "$(req PATCH $SET '{"purchaseOrderAccount":""}')" >/dev/null
expect 200 "$(req GET $SET)" | jq -e '.purchaseOrderAccount=="Purchase Orders"' >/dev/null || fail "no volvió a la cuenta por omisión"; ok "se guarda, sale en el TRNS, 160 caracteres 400 y '' restaura"

step "permiso purchasing.quickbooks"
for r in "GET $QB_" "POST $QB_/export {\"purchaseOrderIds\":[\"$POB\"]}" "POST $QB_/not-passed {\"purchaseOrderIds\":[\"$POB\"]}"; do
  set -- $r; m=$1; p=$2; b=${3:-}
  expect 403 "$(req "$m" "$p" "$b" "$T2")" >/dev/null
done; ok "403 con purchasing.manage pero sin purchasing.quickbooks (GET, export y not-passed)"

step "otra compañía: aislamiento y módulo apagado"
BADM="adminpoqb$TS@smoke.local"; BPASS="Smoke_Admin_2026!"
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Compras QB $TS\",\"phoneNumber\":\"787-555-0100\",\"address\":{\"line1\":\"Calle Smoke 1\",\"city\":\"San Juan\"},\"modules\":[\"CATALOG\"],\"adminEmail\":\"$BADM\",\"adminFullName\":\"Admin Compras QB\",\"adminPassword\":\"$BPASS\",\"mfaRequired\":false}" "$PTOK")" >/dev/null
TB=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$BADM\",\"password\":\"$BPASS\"}")" | jq -r .tokens.accessToken)
for r in "GET $QB_" "GET $TPL_" "GET $TPL_/fields"; do set -- $r; expect 403 "$(req "$1" "$2" '' "$TB")" | jq -e '.code=="module_disabled"' >/dev/null || fail "$1 $2 con PURCHASING apagado debía dar 403 module_disabled"; done
TBID=$(expect 200 "$(req GET /api/v1/me '' "$TB")" | jq -r .tenantId)
enable_module "$(platform_token "$TBID")" PURCHASING
expect 404 "$(req GET $TPL_/$TPID '' "$TB")" >/dev/null
expect 404 "$(req GET "$PO_/$POA" '' "$TB")" >/dev/null
msg "$(expect 400 "$(req POST $QB_/export "{\"purchaseOrderIds\":[\"$POA\"]}" "$TB")")" "Alguna de las órdenes de compra indicadas no existe."
expect 200 "$(req GET $QB_ '' "$TB")" | jq -e --arg a "$POA" '.orders|all(.publicId!=$a)' >/dev/null || fail "la lista por pasar de otra compañía trae una orden ajena"; ok "módulo apagado 403; plantilla y orden ajenas 404; export ajeno 400; por pasar sin lo ajeno"

printf '\n\033[1;32mHumo de compras con plantilla y QuickBooks: todo en orden\033[0m\n'
ok "plantillas PURCHASE_ORDER (campos, 400/409, documentNumber obligatorio, sin cliente, tipo cruzado), importar con plantilla (vista previa, SENT sin inventario, origen EXCEL, no duplica, concurrencia, BAD_VALUE, COST_CONFLICT, 403), origen QuickBooks, por pasar, IIF PURCHORD cuadrado (422/400/409, concurrencia), confirmar/No pasó/quitar la marca, ir y venir, filtros, cuenta de órdenes de compra, permiso, tenant y módulo (el IIF NO se probó contra QuickBooks real)"
