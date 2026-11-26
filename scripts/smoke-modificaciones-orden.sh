#!/usr/bin/env bash
# Prueba de humo de las modificaciones de órdenes de venta (Lote 249, Lote E del plan de rutas, cadenas, precios e IVU) contra un API levantado: modificar una orden ya solicitada
# con autorización del cliente (errores con su texto exacto, vista previa sin efectos, aplicar con diff), precio e IVU manual con sus permisos, la reversa mínima de
# recolecciones, reabrir el empaque, unir una adición al envío de su orden, la segunda aprobación (aprobar, rechazar, retirar, huella vencida), el adjunto de la
# autorización, el crédito, la orden facturada (solo se le añade), la adición facturada aparte con su referencia, la ruta vieja de líneas, la fuente de datos
# SALES_ORDER_CHANGE, aislamiento entre compañías y el módulo apagado. Cada mensaje de error se compara con el texto exacto.
# NUNCA contra Brevo ni correo real: se NIEGA a correr si el canal EMAIL está configurado y no es local (buzón). No verifica correos (los avisos se ven en la bandeja de la
# aplicación). Solo contra un API en Development, nunca contra TEST ni producción: crea datos con el sufijo $CTS (almacén, productos, clientes, órdenes, roles, usuarios) y
# no los borra; los ajustes de la compañía que cambia (segunda aprobación, adjunto del cliente) los deja como los encontró.
# Requiere: curl, jq, base64. Uso: scripts/smoke-modificaciones-orden.sh [base_url]   (API de pruebas: scripts/api-para-pruebas.ps1, con Auth__Onboarding__Enabled=false)
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
# upload RUTA ARCHIVO [TOKEN]: POST multipart con el campo «file»
upload() { local p=$1 f=$2 tk=${3:-$TOKEN}
  curl -sS -X POST "$BASE$p" -H 'Accept: application/json' -H 'X-Lang: es' -H "Authorization: Bearer $tk" -F "file=@$f;type=application/pdf" -w '\n%{http_code}'; }
expect() { local code; code=$(echo "$2" | tail -n1); local body; body=$(echo "$2" | sed '$d'); [[ "$code" == "$1" ]] || fail "esperado HTTP $1, recibido $code: $body"; echo "$body"; }
# expect_any "409|422" RESPUESTA: acepta cualquiera de los códigos
expect_any() { local code; code=$(echo "$2" | tail -n1); local body; body=$(echo "$2" | sed '$d'); [[ "$code" =~ ^($1)$ ]] || fail "esperado HTTP $1, recibido $code: $body"; echo "$body"; }
# msg: el cuerpo de error trae exactamente ese texto (en title o en errors)
msg() { echo "$1" | jq -e --arg m "$2" 'tostring | contains($m)' >/dev/null || fail "mensaje esperado «$2»: $1"; }
# money: compara dos importes con el redondeo de centavos
money() { echo "$1" | jq -e "$2" >/dev/null || fail "$3: $1"; }
dplus() { date -d "$(date +%F) $1 day" +%F; }

CTS=$(date +%s)
HOY=$(date +%F)
PDF="$TMPD/aut.pdf"; printf '%%PDF-1.4\n%% humo modificaciones\n%%%%EOF\n' > "$PDF"
PDF2="$TMPD/aut2.pdf"; printf '%%PDF-1.4\n%% humo modificaciones 2\n%%%%EOF\n' > "$PDF2"

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-modificaciones-orden\"}")" | jq -r .tokens.accessToken); ok "token"
ME=$(expect 200 "$(req GET /api/v1/me)"); TID=$(echo "$ME" | jq -r .tenantId); ADMIN=$(echo "$ME" | jq -r .userId)

step "guarda de correo: nunca contra Brevo"
CHN=$(expect 200 "$(req GET /api/v1/notifications/channels)")
EMAIL_CONF=$(echo "$CHN" | jq -r '[.[] | select(.channel=="EMAIL")][0].isConfigured // false')
EMAIL_LOCAL=$(echo "$CHN" | jq -r '[.[] | select(.channel=="EMAIL")][0].isLocal // false')
if [[ "$EMAIL_CONF" == "true" && "$EMAIL_LOCAL" != "true" ]]; then fail "el API tiene correo real (Brevo): esta prueba no corre así; levante el API sin llave (scripts/api-para-pruebas.ps1)"; fi
ok "sin correo real"

# SALES_ORDERS, WMS_LOTSERIAL y TAX los enciende solo el administrador de la plataforma (con reautenticación). Encenderlos otra vez no hace daño.
PEMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"; PPASS="${TEIKEM_PLATFORM_PASSWORD:-$PASS}"
platform_token() { # tenantId -> token de la plataforma reautenticado en esa compañía
  local t; t=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\",\"tenantId\":$1,\"deviceInfo\":\"smoke-modificaciones-orden\"}")" | jq -r .tokens.accessToken)
  expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PPASS\"}" "$t")" | jq -r .accessToken; }
# set_module TOKEN_PLATAFORMA MÓDULO true|false: lo cambia y, si el API dice que depende de otro («'X' depende de 'Y'»), enciende ese antes
set_module() {
  local ptok=$1 target=$2 on=$3 mod=$2 r c dep
  for _ in 1 2 3 4 5 6; do
    r=$(req PUT /api/v1/modules/$mod "{\"isEnabled\":$on}" "$ptok"); c=$(echo "$r" | tail -n1)
    if [[ "$c" == "200" ]]; then [[ "$mod" == "$target" ]] && return 0; mod=$target; continue; fi
    dep=$(echo "$r" | sed '$d' | jq -r '.title' | sed -n "s/.*depende de '\([A-Z_]*\)'.*/\1/p"); [[ -n "$dep" && "$on" == "true" ]] || fail "no se pudo cambiar $mod: $r"
    mod=$dep
  done
  fail "$target no quedó como se pidió: $r"; }
step "módulos SALES_ORDERS, WMS_LOTSERIAL y TAX encendidos"
PTOK=$(platform_token "$TID"); set_module "$PTOK" SALES_ORDERS true; set_module "$PTOK" WMS_LOTSERIAL true; set_module "$PTOK" TAX true; ok "SALES_ORDERS, WMS_LOTSERIAL y TAX"

step "preparar: IVU 11.5 % (10.5 + 1), almacén con posición de picking, 3 productos con existencia y clientes"
RATE="IVU$CTS"
expect 200 "$(req POST /api/v1/tax/rates "{\"code\":\"$RATE\",\"name\":\"IVU humo $CTS\",\"effectiveFrom\":\"$(dplus -30)\",\"components\":[{\"code\":\"STATE\",\"name\":\"Estatal\",\"percent\":10.5},{\"code\":\"CITY\",\"name\":\"Municipal\",\"percent\":1}]}")" >/dev/null
expect 200 "$(req PATCH /api/v1/tax/settings "{\"defaultRateCode\":\"$RATE\",\"roundingMode\":\"LINE\",\"pricesIncludeTax\":false}")" | jq -e --arg r "$RATE" '.defaultRateCode==$r' >/dev/null || fail "tasa por omisión"
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"MO$CTS\",\"name\":\"Modificaciones $CTS\"}")" | jq -r .warehouse.publicId)
ZP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"PCK","name":"Picking","zoneType":"PICKING"}')" | jq -r .id)
BP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$ZP,\"code\":\"P-01\"}")" | jq -r .id)
mkprod() { expect 200 "$(req POST /api/v1/products "{\"sku\":\"$1$CTS\",\"name\":\"$2 $CTS\",\"purchaseCost\":2,\"salePrice\":$3}")" | jq -r .product.publicId; }
P1=$(mkprod MO1 "Libreta" 100); P2=$(mkprod MO2 "Mochila" 50); P3=$(mkprod MO3 "Lapiz" 20)
adj() { expect 200 "$(req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$1" --arg w "$W" --argjson b "$BP" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:200,reason:"FOUND",notes:"humo modificaciones"}')")" >/dev/null; }
adj "$P1"; adj "$P2"; adj "$P3"
# clientes: CLI (A, B, D, G y la adición del envío), CLF (la orden facturada) y CLC (límite de crédito bajo). Cada uno con su contrato y una tarifa para poder empacar con entrega.
mkclient() { local r; r=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"$1 $CTS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")")
  expect 200 "$(req POST "/api/v1/contracts/$(echo "$r" | jq -r .currentContract.publicId)/rate-components" '{"kind":"PER_SERVICE","serviceType":"STANDARD","packageType":"BOX","rate":6.5,"effectiveFrom":"2026-01-01"}')" >/dev/null
  echo "$r" | jq -r .publicId; }
CLI=$(mkclient "Modif A"); CLF=$(mkclient "Modif Facturada"); CLC=$(mkclient "Modif Credito")
mkloc() { expect 200 "$(req POST /api/v1/locations "{\"clientPublicId\":\"$1\",\"name\":\"Destino $2 $CTS\",\"locationType\":\"DELIVERY\",\"line1\":\"Calle 1\",\"city\":\"Ponce\",\"country\":\"PR\"}")" | jq -r .publicId; }
LOCI=$(mkloc "$CLI" A); LOCF=$(mkloc "$CLF" F)
expect 200 "$(req PATCH /api/v1/clients/$CLC/profile '{"creditLimit":1000}')" >/dev/null
# el administrador es el vendedor de las órdenes de CLI y CLF: así recibe en su bandeja los avisos de la orden
SA=/api/v1/sales-analysis
ROUTE=$(expect 200 "$(req POST $SA/routes "{\"name\":\"Mod$CTS\"}")" | jq -r .id)
expect 200 "$(req POST $SA/routes/$ROUTE/people "{\"userId\":$ADMIN,\"role\":\"PRIMARY\"}")" >/dev/null
for c in "$CLI" "$CLF" "$CLC"; do
  CID=$(expect 200 "$(req GET /api/v1/clients/$c)" | jq -r .id)
  expect 200 "$(req PUT $SA/clients/$CID/sales "{\"routeId\":$ROUTE,\"preferredUserId\":$ADMIN}")" >/dev/null; done
# reglas de aviso por la bandeja de la aplicación (sin correo): el vendedor de la orden
for code in SALES_ORDER_CHANGED SALES_ORDER_ADDITION_CREATED SALES_ORDER_REQUESTED; do
  expect 200 "$(req POST /api/v1/notifications/rules "{\"eventCode\":\"$code\",\"name\":\"Humo mod $code $CTS\",\"isActive\":true,\"channels\":[{\"channel\":\"APP\",\"isEnabled\":true}],\"recipients\":[{\"kind\":\"CONTEXT\",\"value\":\"SELLER\"}]}")" >/dev/null; done
ok "tasa $RATE, almacén $W, P1=100 P2=50 P3=20 con 200 en picking, clientes y ruta"

# ---------------------------------------------------------------- helpers de órdenes
mkorder() { # CLIENTE LÍNEAS_JSON -> publicId solicitado
  local body id; body=$(jq -cn --arg w "$W" --arg c "$1" --argjson l "$2" '{warehousePublicId:$w,clientPublicId:$c,lines:$l}')
  id=$(expect 200 "$(req POST /api/v1/sales-orders "$body")" | jq -r .publicId)
  expect 200 "$(req POST /api/v1/sales-orders/$id/request)" >/dev/null; echo "$id"; }
getorder() { expect 200 "$(req GET /api/v1/sales-orders/$1)"; }
ordnum() { getorder "$1" | jq -r .number; }
lineid() { getorder "$1" | jq -r --arg s "$2$CTS" '[.lines[] | select(.sku==$s)][0].id'; }
# body OPS_JSON [EXTRA_JSON]: una modificación con su autorización del cliente
chbody() { jq -cn --argjson ops "$1" --argjson x "${2:-"{}"}" '{ops:$ops,authorizedByName:"Maria Gomez",authorizedByTitle:"Compradora",authorizationChannel:"PHONE",reason:"Pedido del cliente por teléfono"} + $x'; }
qty() { jq -cn --argjson l "$1" --argjson q "$2" '[{kind:"QTY",lineId:$l,quantity:$q}]'; }
CH() { echo "/api/v1/sales-orders/$1/changes"; }
dispatch() { # ORDEN LÍNEA CANTIDAD -> publicId de la recolección
  expect 200 "$(req POST /api/v1/sales-orders/$1/dispatch "$(jq -cn --argjson l "$2" --argjson q "$3" '{lines:[{lineId:$l,quantity:$q}]}')")" | jq -r .pickBatchPublicId; }
packb() { # RECOLECCIÓN CLIENTE UBICACIÓN -> respuesta del empaque
  expect 200 "$(req POST /api/v1/pick-batches/$1/pack "$(jq -cn --arg c "$2" --arg l "$3" '{order:{clientPublicId:$c,consigneeLocationPublicId:$l,serviceType:"STANDARD",packages:[{pieces:1,packageType:"BOX"}]}}')")"; }
batch() { expect 200 "$(req GET /api/v1/pick-batches/$1)"; }

# usuarios de apoyo: U2 solo modifica (sin sales.price, sales.tax ni warehouse.reopen-pack); U3 aprueba; U4 solo crea órdenes
mkuser() { # ETIQUETA PERMISOS_JSON -> token
  local rn="Mod$1 $CTS" ue="mod$1$CTS@teikem.local"
  expect 200 "$(req POST /api/v1/roles "$(jq -cn --arg n "$rn" --argjson p "$2" '{name:$n,permissions:$p}')")" >/dev/null
  expect 200 "$(req POST /api/v1/users "{\"email\":\"$ue\",\"fullName\":\"Humo $1 $CTS\",\"password\":\"$PASS\",\"roles\":[\"$rn\"]}")" >/dev/null
  expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$ue\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-modificaciones-orden\"}")" | jq -r .tokens.accessToken; }
T2=$(mkuser U2 '["sales.view","sales.modify","clients.read"]')
T3=$(mkuser U3 '["sales.view","sales.modify","sales.modify.approve"]')
T4=$(mkuser U4 '["sales.view","sales.create"]')
ok "usuarios U2 (modifica), U3 (aprueba) y U4 (solo crea)"

# ================================================================ orden A
step "orden A (2 líneas) solicitada y sus opciones de modificación"
A=$(mkorder "$CLI" "$(jq -cn --arg a "$P1" --arg b "$P2" '[{productPublicId:$a,quantity:5},{productPublicId:$b,quantity:3}]')")
NA=$(ordnum "$A"); LA1=$(lineid "$A" MO1); LA2=$(lineid "$A" MO2)
money "$(getorder "$A")" '.status=="REQUESTED" and .subtotal==650 and .taxAmount==74.75' "orden A: 650 + 74.75"
EO=$(expect 200 "$(req GET /api/v1/sales-orders/$A/edit-options)")
money "$EO" '.status=="REQUESTED" and .permissions.modify==true and (.batches|length)==0 and (.invoices|length)==0 and (.lines|length)==2 and .mustUseAddition==false and ([.lines[].minQuantity]==[0,0])' "edit-options de A"
ok "$NA solicitada; edit-options: sin recolección, sin facturas, con posición por línea"

step "errores de la modificación con su texto exacto"
msg "$(expect 400 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 6)" '{"authorizedByName":null}')")")" "Indique quién autorizó el cambio en el cliente."
msg "$(expect 400 "$(req POST "$(CH $A)" "$(chbody '[]')")")" "La modificación no cambia nada: indique al menos un cambio."
msg "$(expect 400 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 6)" "{\"authorizedAtUtc\":\"$(date -u -d 'tomorrow' +%Y-%m-%dT%H:%M:%SZ)\"}")")")" "La fecha de la autorización no puede ser futura."
ok "sin autorizador, sin operaciones y fecha futura: 400"

step "vista previa sin efectos"
OPS=$(jq -cn --argjson a "$LA1" --argjson b "$LA2" --arg p "$P3" '[{kind:"QTY",lineId:$a,quantity:6},{kind:"ADD",productPublicId:$p,quantity:4},{kind:"REMOVE",lineId:$b}]')
PV=$(expect 200 "$(req POST "$(CH $A)/preview" "$(chbody "$OPS")")")
money "$PV" '(.diff.added|length)==1 and (.diff.removed|length)==1 and (.diff.changed|length)==1 and .after.subtotal==680 and .after.taxAmount==78.2 and ((.blockers|length)==0)' "vista previa"
money "$(getorder "$A")" '.subtotal==650 and .taxAmount==74.75 and ([.lines[]|select(.isRemoved)]|length)==0 and (.lines|length)==2' "la vista previa no debía cambiar la orden"
expect 200 "$(req GET "$(CH $A)")" | jq -e 'length==0' >/dev/null || fail "la vista previa no debía dejar historial"
ok "diff: 1 agregada, 1 quitada, 1 cambiada; 680 + 78.20; la orden sigue igual"

step "aplicar la modificación n.º 1 (canal teléfono)"
R=$(expect 200 "$(req POST "$(CH $A)" "$(chbody "$OPS")")"); CHG1=$(echo "$R" | jq -r .item.publicId)
money "$R" '.item.status=="APPLIED" and .item.changeNo==1 and .item.channel=="PHONE" and .linesAdded==1 and .linesRemoved==1 and .linesChanged==1' "modificación aplicada"
O=$(getorder "$A")
money "$O" '.subtotal==680 and .taxAmount==78.2 and ([.lines[]|select(.sku=="MO2'$CTS'")][0] | .isRemoved==true and .qtyOrdered==0) and ([.lines[]|select(.sku=="MO3'$CTS'")][0] | .qtyOrdered==4 and .qtyReserved==4 and .isRemoved==false) and ([.lines[]|select(.sku=="MO1'$CTS'")][0].qtyOrdered==6)' "orden tras la modificación"
expect 200 "$(req GET "$(CH $A)")" | jq -e 'length==1 and .[0].changeNo==1' >/dev/null || fail "historial de A"
D1=$(expect 200 "$(req GET "$(CH $A)/$CHG1")")
money "$D1" '(.before.lines|length)>=2 and (.after.lines|length)>=3 and (.diff.added|length)==1 and .item.totalDelta==(.item.totalAfter-.item.totalBefore)' "modificación con antes, después y diff"
LA3=$(lineid "$A" MO3)
ok "L2 quitada (pedido 0), L3 nueva reservada, subtotal 680 e IVU 78.20; historial y detalle con diff"

step "precio e IVU manual con sus permisos"
msg "$(expect 403 "$(req POST "$(CH $A)" "$(chbody "$(jq -cn --argjson l "$LA1" '[{kind:"PRICE",lineId:$l,unitPrice:90}]')")" "$T2")")" "Cambiar el precio exige el permiso de precios (sales.price)."
expect 403 "$(req POST "$(CH $A)" "$(chbody "$(jq -cn --argjson l "$LA1" '[{kind:"TAX",lineId:$l,taxPct:1,note:"Exento parcial"}]')")" "$T2")" >/dev/null
R=$(expect 200 "$(req POST "$(CH $A)" "$(chbody "$(jq -cn --argjson l "$LA1" '[{kind:"TAX",lineId:$l,taxPct:1,note:"Orden administrativa de humo"}]')")")")
O=$(getorder "$A")
money "$O" '([.lines[]|select(.sku=="MO1'$CTS'")][0] | .taxReason=="MANUAL_OVERRIDE" and .taxAmount==6 and .taxComputedAmount==69) and ([.lines[]|select(.sku=="MO3'$CTS'")][0].taxAmount==9.2)' "IVU manual del 1 % solo en la línea 1 (6.00 contra 69.00 calculado; la línea 3 sigue al 11.5 %)"
ok "403 de precio y de IVU sin permiso; IVU manual MANUAL_OVERRIDE solo en esa línea"

step "reversa mínima: se deshace la última recolección y la anterior queda"
R1=$(dispatch "$A" "$LA1" 2); R2=$(dispatch "$A" "$LA1" 2)
batch "$R1" | jq -e '.statusCode=="COLLECTED"' >/dev/null || fail "R1 recolectada"
expect 200 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 3)")")" | jq -e '.item.status=="APPLIED" and .item.changeNo==3' >/dev/null || fail "bajar L1 a 3"
batch "$R2" | jq -e '.statusCode=="CANCELLED"' >/dev/null || fail "R2 debía quedar CANCELLED (reversa mínima)"
batch "$R1" | jq -e '.statusCode=="COLLECTED"' >/dev/null || fail "R1 debía seguir COLLECTED"
KX=$(expect 200 "$(req GET "/api/v1/inventory/transactions?productPublicIds=$P1&warehousePublicIds=$W&includeSystem=true&take=50")")
echo "$KX" | jq -e 'tostring | contains("PICK_BATCH_REVERSAL")' >/dev/null || fail "el Kárdex no trae el ajuste PICK_BATCH_REVERSAL: $KX"
ok "R2 CANCELLED, R1 COLLECTED y PICK_BATCH_REVERSAL en el Kárdex"
IN=""; for _ in $(seq 1 15); do
  IN=$(expect 200 "$(req GET "/api/v1/inbox?take=50")")
  echo "$IN" | jq -e '[.items[] | select(.eventCode=="SALES_ORDER_CHANGED")] | length>=1' >/dev/null && break; sleep 2; done
echo "$IN" | jq -e '[.items[] | select(.eventCode=="SALES_ORDER_CHANGED")] | length>=1' >/dev/null || fail "la bandeja no trae SALES_ORDER_CHANGED en 30 s: $IN"
ok "aviso SALES_ORDER_CHANGED en la bandeja del vendedor"

step "orden empacada: reabrir el empaque con su permiso"
PK=$(packb "$R1" "$CLI" "$LOCI"); EMP=$(echo "$PK" | jq -r .batch.packBatchNumber)
batch "$R1" | jq -e '.statusCode=="PACKED"' >/dev/null || fail "R1 empacada"
SHIP=$(expect 200 "$(req GET /api/v1/sales-orders/$A/edit-options)" | jq -r '.batches[0].shipmentPublicId')
RJ=$(expect 422 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 4)")")")
msg "$RJ" "para cambiarlo hay que reabrir el empaque (permiso warehouse.reopen-pack) o usar una orden interna de adición."
msg "$(expect 403 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 4)" '{"reopenPack":true}')" "$T2")")" "Reabrir el empaque exige el permiso warehouse.reopen-pack."
expect 200 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 4)" '{"reopenPack":true,"reopenReason":"El cliente pidio una unidad mas"}')")" | jq -e '.item.status=="APPLIED" and .reopenedPack==true' >/dev/null || fail "reabrir con el administrador"
batch "$R1" | jq -e '.statusCode=="COLLECTED" and .packBatchNumber==null' >/dev/null || fail "R1 debía volver a COLLECTED"
msg "$(expect 404 "$(req GET /api/v1/orders/$SHIP)")" "Envío no encontrado."
ok "422 sin reabrir, 403 sin el permiso, 200 con el administrador: envío dado de baja y R1 de vuelta en COLLECTED ($EMP)"

step "reabrir suelto"
msg "$(expect 422 "$(req POST /api/v1/pick-batches/$R1/reopen-pack '{"reason":"Prueba de humo"}')")" "no está empacada."
ok "422 sobre una recolección que no está empacada"

# ================================================================ unir al envío
step "adición unida al envío de su orden"
B=$(mkorder "$CLI" "$(jq -cn --arg a "$P1" '[{productPublicId:$a,quantity:2}]')"); NB=$(ordnum "$B"); LB1=$(lineid "$B" MO1)
RB=$(dispatch "$B" "$LB1" 2); PKB=$(packb "$RB" "$CLI" "$LOCI"); EMPB=$(echo "$PKB" | jq -r .batch.packBatchNumber)
ADDB=$(jq -cn --arg p "$P2" '{lines:[{productPublicId:$p,quantity:1}],authorizedByName:"Maria Gomez",authorizationChannel:"PHONE",reason:"Se le olvido una mochila"}')
AR=$(expect 200 "$(req POST /api/v1/sales-orders/$B/additions "$ADDB")"); BH=$(echo "$AR" | jq -r .item.additionPublicId)
OH=$(getorder "$BH")
echo "$OH" | jq -e --arg n "$NB" '.status=="REQUESTED" and .parentNumber==$n and ([.lines[]|select(.qtyReserved==1)]|length)==1' >/dev/null || fail "la adición nace solicitada, reservada y con su madre: $OH"
IN=""; for _ in $(seq 1 15); do
  IN=$(expect 200 "$(req GET "/api/v1/inbox?take=50")")
  echo "$IN" | jq -e '[.items[] | select(.eventCode=="SALES_ORDER_REQUESTED" or .eventCode=="SALES_ORDER_ADDITION_CREATED")] | length>=1' >/dev/null && break; sleep 2; done
echo "$IN" | jq -e '[.items[] | select(.eventCode=="SALES_ORDER_REQUESTED" or .eventCode=="SALES_ORDER_ADDITION_CREATED")] | length>=1' >/dev/null || fail "la bandeja no avisó de la adición: $IN"
LH1=$(echo "$OH" | jq -r '.lines[0].id'); RH=$(dispatch "$BH" "$LH1" 1)
msg "$(expect 422 "$(req POST /api/v1/pick-batches/$RH/join-order-shipment '{}')")" "ya está empacada: marque «Reabrir el empaque» (permiso warehouse.reopen-pack) para unir esta recolección a su envío."
JG=$(expect 200 "$(req POST /api/v1/pick-batches/$RH/join-order-shipment '{"reopenPack":true,"reason":"Se une la adicion al envio"}')")
GPUB=$(echo "$JG" | jq -r .publicId)
echo "$JG" | jq -e --arg e "$EMPB" '.number==$e' >/dev/null || fail "la maestra debía tomar el número del empaque reabierto $EMPB: $JG"
GPK=$(expect 200 "$(req POST /api/v1/pick-batches/groups/$GPUB/pack "$(jq -cn --arg c "$CLI" --arg l "$LOCI" '{order:{clientPublicId:$c,consigneeLocationPublicId:$l,serviceType:"STANDARD",packages:[{pieces:1,packageType:"BOX"}]}}')")")
echo "$GPK" | jq -e --arg e "$EMPB" '.order.packBatchNumber==$e' >/dev/null || fail "un solo envío con el número $EMPB: $GPK"
# una recolección de otra compañía de clientes no se une
OX=$(mkorder "$CLF" "$(jq -cn --arg a "$P3" '[{productPublicId:$a,quantity:1}]')"); RX=$(dispatch "$OX" "$(lineid "$OX" MO3)" 1)
msg "$(expect 422 "$(req POST /api/v1/pick-batches/$RX/join-order-shipment '{}')")" "no es de la misma orden ni de una adición de ella"
ok "adición reservada, aviso en la bandeja, 422 sin reabrir, maestra con el número $EMPB y un solo envío; otro cliente: 422"

# ================================================================ segunda aprobación
step "segunda aprobación: aprobar, rechazar, retirar"
D=$(mkorder "$CLI" "$(jq -cn --arg a "$P1" '[{productPublicId:$a,quantity:5}]')"); ND=$(ordnum "$D"); LD1=$(lineid "$D" MO1)
SET=/api/v1/sales-order-changes
expect 200 "$(req PATCH $SET/settings '{"approvalMode":"ALWAYS"}')" | jq -e '.approvalMode=="ALWAYS"' >/dev/null || fail "approvalMode ALWAYS"
RP=$(expect 200 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 6)")")"); CP=$(echo "$RP" | jq -r .item.publicId)
money "$RP" '.item.status=="PENDING_APPROVAL" and .item.requiresApproval==true' "queda pendiente de segunda aprobación"
getorder "$D" | jq -e '([.lines[]|select(.sku=="MO1'$CTS'")][0].qtyOrdered==5) and .pendingChangeNo==1' >/dev/null || fail "la orden no debía cambiar mientras está pendiente"
expect 409 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 7)")")" >/dev/null
PG=$(expect 200 "$(req GET "$SET/pending?skip=0&take=50&q=$ND" '' "$T3")")
echo "$PG" | jq -e '.total==1 and (.items|length)==1' >/dev/null || fail "bandeja «Por aprobar»: $PG"
msg "$(expect 403 "$(req POST "$(CH $D)/$CP/approve" '{}')")" "La segunda aprobación la da otra persona: usted registró esta modificación."
expect 200 "$(req POST "$(CH $D)/$CP/approve" '{"note":"Autorizado"}' "$T3")" | jq -e '.item.status=="APPLIED" and .decisionNote=="Autorizado"' >/dev/null || fail "aprobar con otra persona"
getorder "$D" | jq -e '[.lines[]|select(.sku=="MO1'$CTS'")][0].qtyOrdered==6' >/dev/null || fail "la orden debía quedar en 6"
RP=$(expect 200 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 7)")")"); CP2=$(echo "$RP" | jq -r .item.publicId)
expect 400 "$(req POST "$(CH $D)/$CP2/reject" '{}' "$T3")" >/dev/null
expect 200 "$(req POST "$(CH $D)/$CP2/reject" '{"note":"No procede"}' "$T3")" | jq -e '.item.status=="REJECTED"' >/dev/null || fail "rechazar con nota"
RP=$(expect 200 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 8)")")"); CP3=$(echo "$RP" | jq -r .item.publicId)
expect 200 "$(req POST "$(CH $D)/$CP3/withdraw" '{}')" | jq -e '.item.status=="WITHDRAWN"' >/dev/null || fail "retirar"
ok "pendiente, 409 de la segunda, bandeja con 1 fila, 403 al aprobar quien registró, aprobada por otra persona; rechazo sin nota 400; retirada"

step "la orden cambió desde que se registró la modificación"
RP=$(expect 200 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 8)")")"); CP4=$(echo "$RP" | jq -r .item.publicId)
dispatch "$D" "$LD1" 1 >/dev/null
msg "$(expect 409 "$(req POST "$(CH $D)/$CP4/approve" '{}' "$T3")")" "La orden cambió desde que se registró la modificación n.º"
expect 200 "$(req POST "$(CH $D)/$CP4/withdraw" '{}')" >/dev/null
expect 200 "$(req PATCH $SET/settings '{"approvalMode":"NONE"}')" | jq -e '.approvalMode=="NONE"' >/dev/null || fail "volver a NONE"
ok "409 con la huella vencida; segunda aprobación otra vez en NONE"

step "adjunto de la autorización"
expect 200 "$(req PATCH $SET/settings/clients/$CLI '{"attachmentRequired":true}')" | jq -e '.effective.attachmentRequired==true' >/dev/null || fail "attachmentRequired del cliente"
expect 400 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 7)")")" >/dev/null
UP=$(expect 200 "$(upload /api/v1/sales-orders/$D/change-attachments "$PDF")"); FILE=$(echo "$UP" | jq -r .publicId)
expect 200 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 7)" "{\"attachmentFilePublicId\":\"$FILE\"}")")" | jq -e '.item.status=="APPLIED" and .attachmentFilePublicId==$f' --arg f "$FILE" >/dev/null || fail "con adjunto"
msg "$(expect 409 "$(req POST "$(CH $D)" "$(chbody "$(qty "$LD1" 8)" "{\"attachmentFilePublicId\":\"$FILE\"}")")")" "Ese archivo ya respalda otra modificación."
expect 200 "$(req PATCH $SET/settings/clients/$CLI '{"attachmentRequired":null}')" >/dev/null
ok "400 sin adjunto, 200 con el archivo, 409 al reusarlo; ajuste del cliente restablecido"

# ================================================================ crédito
step "crédito: el total nuevo pasa del límite"
K=$(mkorder "$CLC" "$(jq -cn --arg a "$P1" '[{productPublicId:$a,quantity:2}]')"); LK1=$(lineid "$K" MO1)
KR=$(expect 422 "$(req POST "$(CH $K)" "$(chbody "$(qty "$LK1" 20)")")")
echo "$KR" | jq -e 'tostring | contains("credit_exceeded")' >/dev/null || fail "debía ser credit_exceeded: $KR"
expect 200 "$(req POST "$(CH $K)" "$(chbody "$(qty "$LK1" 20)" '{"overrideCredit":true}')")" | jq -e '.item.status=="APPLIED" and .creditOverride==true' >/dev/null || fail "overrideCredit del administrador"
ok "422 credit_exceeded y, con overrideCredit, 200 con la bandera creditOverride (el evento credit_override queda en la bitácora de seguridad)"

# ================================================================ facturada
step "orden facturada: solo se le añade"
F=$(mkorder "$CLF" "$(jq -cn --arg a "$P1" --arg b "$P2" '[{productPublicId:$a,quantity:3},{productPublicId:$b,quantity:2}]')"); NF=$(ordnum "$F"); LF1=$(lineid "$F" MO1)
RF1=$(dispatch "$F" "$LF1" 1); RF2=$(dispatch "$F" "$LF1" 1)
packb "$RF1" "$CLF" "$LOCF" >/dev/null
BR=$(expect 200 "$(req POST /api/v1/billing/runs "{\"clientPublicId\":\"$CLF\",\"periodStart\":\"$(dplus -2)\",\"periodEnd\":\"$(dplus 2)\",\"includeProducts\":true,\"includeFreight\":false}")")
RUN1=$(echo "$BR" | jq -r .id); INV1=$(echo "$BR" | jq -r '.invoices[0].publicId'); INV1N=$(echo "$BR" | jq -r '.invoices[0].number')
msg "$(expect 422 "$(req POST "$(CH $F)" "$(chbody "$(qty "$LF1" 4)")")")" "La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito."
msg "$(expect_any '409|422' "$(req DELETE /api/v1/pick-batches/$RF2 '{}')")" "ya está facturada"
msg "$(expect_any '409|422' "$(req POST /api/v1/pick-batches/$RF1/reopen-pack '{"reason":"Prueba de humo"}')")" "tiene productos ya facturados"
ok "orden $NF facturada en la factura $INV1N: modificar 422, eliminar la recolección facturada y reabrir su empaque bloqueados"

step "adición facturada aparte, con la referencia de la factura que complementa"
AR=$(expect 200 "$(req POST /api/v1/sales-orders/$F/additions "$(jq -cn --arg p "$P3" '{lines:[{productPublicId:$p,quantity:2}],authorizedByName:"Maria Gomez",authorizationChannel:"EMAIL",reason:"Complemento del pedido"}')")")
FH=$(echo "$AR" | jq -r .item.additionPublicId); LFH=$(getorder "$FH" | jq -r '.lines[0].id'); dispatch "$FH" "$LFH" 2 >/dev/null
BR2=$(expect 200 "$(req POST /api/v1/billing/runs "{\"clientPublicId\":\"$CLF\",\"periodStart\":\"$(dplus -2)\",\"periodEnd\":\"$(dplus 2)\",\"includeProducts\":true,\"includeFreight\":false}")")
INV2=$(echo "$BR2" | jq -r '.invoices[0].publicId')
INVD=$(expect 200 "$(req GET /api/v1/billing/invoices/$INV2)")
echo "$INVD" | jq -e --arg n "$NF" --arg f "$INV1N" '([.lines[].description] | map(select(contains("adición de "+$n) and contains("complementa") and contains($f))) | length)>=1 and (.complements|index($f))!=null' >/dev/null \
  || fail "la línea de la adición debía decir «(adición de $NF · complementa $INV1N)»: $INVD"
QX=$(expect 200 "$(req POST /api/v1/sales-orders/quickbooks/export '{"includeNew":true}')")
echo "$QX" | jq -r .contentBase64 | base64 -d 2>/dev/null | iconv -f WINDOWS-1252 -t UTF-8 2>/dev/null | grep -q "$INV1N" || fail "el MEMO del IIF no lleva la referencia $INV1N"
QIDS=$(echo "$QX" | jq -c '[.invoices[] | (.publicId // .invoicePublicId)]')
expect 200 "$(req POST /api/v1/sales-orders/quickbooks/confirm "{\"invoiceIds\":$QIDS}")" >/dev/null
expect 200 "$(req GET /api/v1/sales-orders/$F/edit-options)" | jq -e '[.invoices[].quickBooksState] | index("IN_QUICKBOOKS")!=null' >/dev/null || fail "edit-options debía mostrar IN_QUICKBOOKS"
msg "$(expect 422 "$(req POST /api/v1/sales-orders/$F/cancel '{}')")" "La orden tiene adiciones abiertas"
ok "descripción «adición de … · complementa $INV1N», complements en la factura, MEMO del IIF, IN_QUICKBOOKS y cancelar con adición abierta: 422"

# ================================================================ ruta vieja
step "ruta vieja de líneas: PATCH /lines/{lineId}"
G=$(mkorder "$CLI" "$(jq -cn --arg a "$P1" '[{productPublicId:$a,quantity:2}]')"); LG1=$(lineid "$G" MO1)
expect 400 "$(req PATCH /api/v1/sales-orders/$G/lines/$LG1 '{"quantity":3}')" >/dev/null
expect 403 "$(req PATCH /api/v1/sales-orders/$G/lines/$LG1 '{"quantity":3}' "$T4")" >/dev/null
ok "sin autorización 400; con solo sales.create 403"

# ================================================================ fuente de datos
step "fuente de datos SALES_ORDER_CHANGE"
expect 200 "$(req GET /api/v1/analytics/data-sources)" | jq -e '[.[] | select(.key=="SALES_ORDER_CHANGE")] | length==1' >/dev/null || fail "SALES_ORDER_CHANGE no está entre las fuentes"
PVA=$(expect 200 "$(req POST '/api/v1/analytics/reports/SALES_ORDER_CHANGE/preview?dateRangeMode=ALL' '{"name":"x","columns":["OrderNumber","ChangeNo","TotalDelta"]}')")
echo "$PVA" | jq -e --arg n "$NA" 'tostring | contains($n) and contains("TotalDelta")' >/dev/null || fail "la fuente debía traer las modificaciones de $NA con TotalDelta: $PVA"
TV=$(mkuser U5 '["clients.read"]')
expect 200 "$(req GET /api/v1/analytics/data-sources '' "$TV")" | jq -e '[.[] | select(.key=="SALES_ORDER_CHANGE")] | length==0' >/dev/null || fail "sin sales.view no debía ver SALES_ORDER_CHANGE"
ok "filas con TotalDelta; oculta para quien no tiene sales.view"

# ================================================================ aislamiento y módulo
step "otra compañía: aislamiento"
BADM="admmodord$CTS@smoke.local"; BPASS="Smoke_Admin_2026!"
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Mod Orden $CTS\",\"phoneNumber\":\"787-555-0100\",\"address\":{\"line1\":\"Calle Smoke 1\",\"city\":\"San Juan\"},\"modules\":[\"CATALOG\"],\"adminEmail\":\"$BADM\",\"adminFullName\":\"Admin Mod Orden\",\"adminPassword\":\"$BPASS\",\"mfaRequired\":false}" "$PTOK")" >/dev/null
TB=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$BADM\",\"password\":\"$BPASS\"}")" | jq -r .tokens.accessToken)
TBID=$(expect 200 "$(req GET /api/v1/me '' "$TB")" | jq -r .tenantId)
set_module "$(platform_token "$TBID")" SALES_ORDERS true
msg "$(expect 404 "$(req GET /api/v1/sales-orders/$A/edit-options '' "$TB")")" "Orden de venta no encontrada."
msg "$(expect 404 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 9)")" "$TB")")" "Orden de venta no encontrada."
expect 404 "$(upload /api/v1/sales-orders/$A/change-attachments "$PDF2" "$TB")" >/dev/null
ok "edit-options, modificación y adjunto sobre una orden ajena: 404"

step "módulo SALES_ORDERS apagado"
set_module "$PTOK" SALES_ORDERS false
expect 403 "$(req GET /api/v1/sales-orders/$A/edit-options)" >/dev/null
expect 403 "$(req POST "$(CH $A)" "$(chbody "$(qty "$LA1" 9)")")" >/dev/null
set_module "$PTOK" SALES_ORDERS true
ok "403 por módulo con SALES_ORDERS apagado; encendido otra vez"

printf '\n\033[1;32mHumo de modificaciones de órdenes de venta (Lote 249): todo en orden\033[0m\n'
