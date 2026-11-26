#!/usr/bin/env bash
# Prueba de humo de los precios especiales y las ofertas por cliente y por cadena (Lote 247, Lote C del plan de rutas, cadenas y precios) contra un API levantado:
# precios especiales de la ficha del cliente y de la cadena (lista dedicada oculta por omisión), el precio que gana en el catálogo y en la orden (el más bajo entre
# cliente, cadena, oferta y lista general), avisos, cruce de fechas, lista dedicada bloqueada, público de la oferta (incluir / excluir y cadenas), listas con cadenas,
# bloqueo al desactivar una cadena con precios u ofertas, fuente de datos SPECIAL_PRICE, permisos, módulo apagado y aislamiento entre compañías. Cada mensaje de error
# se compara con el texto exacto. El precio especial es ANTES del IVU: las exenciones del cliente se aplican después y se cubren en SpecialPriceTaxExemptionTests.
# NUNCA contra Brevo ni correo real: se NIEGA a correr si el canal EMAIL está configurado y no es local (buzón). No verifica correos. Solo contra un API en Development,
# nunca contra TEST ni producción: crea datos con el sufijo $CTS (productos, cadenas, clientes, listas, ofertas, usuario) y no los borra.
# Requiere: curl, jq. Uso: scripts/smoke-precios-especiales.sh [base_url]   (API de pruebas: scripts/api-para-pruebas.ps1, con Auth__Onboarding__Enabled=false)
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

SP=/api/v1/special-prices
PL=/api/v1/price-lists
OF=/api/v1/offers
CH_=/api/v1/client-chains
CTS=$(date +%s)
D_AYER2=$(date -u -d '2 days ago' +%F); D_HACE3=$(date -u -d '3 days ago' +%F); D_HACE10=$(date -u -d '10 days ago' +%F)
D_MANANA=$(date -u -d 'tomorrow' +%F); D_MAS10=$(date -u -d '10 days' +%F); D_MAS30=$(date -u -d '30 days' +%F); D_HACE1=$(date -u -d 'yesterday' +%F)

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-precios-especiales\"}")" | jq -r .tokens.accessToken); ok "token"
TID=$(expect 200 "$(req GET /api/v1/me)" | jq -r .tenantId)

step "guarda de correo: nunca contra Brevo"
CHN=$(expect 200 "$(req GET /api/v1/notifications/channels)")
EMAIL_CONF=$(echo "$CHN" | jq -r '[.[] | select(.channel=="EMAIL")][0].isConfigured // false')
EMAIL_LOCAL=$(echo "$CHN" | jq -r '[.[] | select(.channel=="EMAIL")][0].isLocal // false')
if [[ "$EMAIL_CONF" == "true" && "$EMAIL_LOCAL" != "true" ]]; then fail "el API tiene correo real (Brevo): esta prueba no corre así; levante el API sin llave (scripts/api-para-pruebas.ps1)"; fi
ok "sin correo real"

# SALES_ORDERS viene apagado en una base recién creada y solo el administrador de la plataforma lo enciende (con reautenticación). Encenderlo otra vez no hace daño.
PEMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"; PPASS="${TEIKEM_PLATFORM_PASSWORD:-$PASS}"
platform_token() { # tenantId -> token de la plataforma reautenticado en esa compañía
  local t; t=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\",\"tenantId\":$1,\"deviceInfo\":\"smoke-precios-especiales\"}")" | jq -r .tokens.accessToken)
  expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PPASS\"}" "$t")" | jq -r .accessToken; }
# enable_module TOKEN_PLATAFORMA MÓDULO: lo enciende y, si el API dice que depende de otro («'X' depende de 'Y'»), enciende ese antes
enable_module() {
  local ptok=$1 target=$2 mod=$2 r c dep
  for _ in 1 2 3 4 5 6; do
    r=$(req PUT /api/v1/modules/$mod '{"isEnabled":true}' "$ptok"); c=$(echo "$r" | tail -n1)
    if [[ "$c" == "200" ]]; then [[ "$mod" == "$target" ]] && return 0; mod=$target; continue; fi
    dep=$(echo "$r" | sed '$d' | jq -r '.title' | sed -n "s/.*depende de '\([A-Z_]*\)'.*/\1/p"); [[ -n "$dep" ]] || fail "no se pudo encender $mod: $r"
    mod=$dep
  done
  fail "$target no quedó encendido: $r"; }
step "módulo SALES_ORDERS encendido"
PTOK=$(platform_token "$TID"); enable_module "$PTOK" SALES_ORDERS; ok "SALES_ORDERS encendido"
# la fuente de datos exige el módulo de análisis; si no se puede encender, solo se omite esa comprobación
ANALYTICS_ON=1; (enable_module "$PTOK" ANALYTICS >/dev/null 2>&1) || ANALYTICS_ON=0

step "usuario de ventas sin sales.pricelist (solo ve)"
RN="VentasPrecios $CTS"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"$RN\",\"permissions\":[\"sales.view\",\"sales.create\"]}")" >/dev/null
U2EMAIL="vendprecio$CTS@teikem.local"
expect 200 "$(req POST /api/v1/users "{\"email\":\"$U2EMAIL\",\"fullName\":\"Vendedor Precios $CTS\",\"password\":\"$PASS\",\"roles\":[\"$RN\"]}")" >/dev/null
T2=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$U2EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-precios-especiales\"}")" | jq -r .tokens.accessToken); ok "usuario $U2EMAIL"

step "datos: almacén, productos A y B, lista general, cadena y clientes"
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"E$CTS\",\"name\":\"Precios $CTS\"}")" | jq -r .warehouse.publicId)
ZP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"PCK","name":"Picking","zoneType":"PICKING"}')" | jq -r .id)
BP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$ZP,\"code\":\"P-01\"}")" | jq -r .id)
mkprod() { expect 200 "$(req POST /api/v1/products "{\"sku\":\"$1$CTS\",\"name\":\"$2 $CTS\",\"purchaseCost\":2,\"salePrice\":10}")" | jq -r .product.publicId; }
SKA="PA$CTS"; SKB="PB$CTS"; SKD="PD$CTS"
PA=$(mkprod PA "Producto A"); PB=$(mkprod PB "Producto B"); PD=$(mkprod PD "Producto D")
adj() { expect 200 "$(req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$1" --arg w "$W" --argjson b "$BP" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:50,reason:"FOUND",notes:"humo precios especiales"}')")" >/dev/null; }
adj "$PA"; adj "$PB"
# ensure_published RUTA_LISTA_U_OFERTA: publica si no nació publicada
ensure_published() { local pub; pub=$(expect 200 "$(req GET "$1")" | jq -r .isPublished); [[ "$pub" == "true" ]] || expect 200 "$(req POST "$1/publish")" >/dev/null; }
LG=$(expect 200 "$(req POST $PL "$(jq -cn --arg n "General $CTS" --arg f "$D_HACE10" '{name:$n,validFrom:$f,appliesToAll:true}')")" | jq -r .publicId)
expect 200 "$(req POST $PL/$LG/items "$(jq -cn --arg a "$PA" --arg b "$PB" '{items:[{productPublicId:$a,price:9},{productPublicId:$b,price:9}]}')")" >/dev/null
ensure_published $PL/$LG
CHAIN=$(expect 200 "$(req POST $CH_ "{\"name\":\"Walg$CTS\"}")" | jq -r .publicId)
mkclient() { expect 200 "$(req POST /api/v1/clients "{\"name\":\"$1 $CTS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId; }
C1=$(mkclient "Cliente Uno"); C2=$(mkclient "Cliente Dos"); C3=$(mkclient "Cliente Tres")
expect 200 "$(req PUT $CH_/by-client/$C1 "{\"chainPublicId\":\"$CHAIN\"}")" >/dev/null
expect 200 "$(req PUT $CH_/by-client/$C3 "{\"chainPublicId\":\"$CHAIN\"}")" >/dev/null
# cat_item CLIENTE SKU: el producto del catálogo con su precio resuelto para el cliente
cat_item() { expect 200 "$(req GET "/api/v1/sales-orders/catalog?clientPublicId=$1")" | jq -c --arg s "$2" '[.brands[].products[] | select(.sku==$s)][0]'; }
ok "almacén, 3 productos, lista general (A=9, B=9), cadena Walg$CTS con C1 y C3; C2 sin cadena"

step "precio especial de la cadena: la dedicada no sale en la lista normal"
R=$(expect 200 "$(req POST $SP/chains/$CHAIN "$(jq -cn --arg p "$PA" --arg f "$D_HACE3" '{productPublicId:$p,price:7,validFrom:$f}')")")
echo "$R" | jq -e --arg s "$SKA" '.ownerKind=="CHAIN" and ([.rows[] | select(.sku==$s and .price==7 and .state=="ACTIVE")] | length==1)' >/dev/null || fail "precio de la cadena: $R"
LD=$(echo "$R" | jq -r --arg s "$SKA" '[.rows[] | select(.sku==$s)][0].priceListPublicId')
expect 200 "$(req GET $PL)" | jq -e --arg l "$LD" '[.[] | select(.publicId==$l)] | length==0' >/dev/null || fail "la dedicada no debía salir sin includeSpecial"
expect 200 "$(req GET "$PL?includeSpecial=true")" | jq -e --arg l "$LD" '[.[] | select(.publicId==$l and .specialOwnerKind=="CHAIN")] | length==1' >/dev/null || fail "includeSpecial debía mostrar la dedicada de la cadena"
ok "dedicada oculta por omisión; includeSpecial la muestra con specialOwnerKind=CHAIN"

step "precio especial del cliente: propio, de la cadena y lo que se cobra hoy"
R=$(expect 200 "$(req POST $SP/clients/$C1 "$(jq -cn --arg p "$PA" --arg f "$D_HACE3" '{productPublicId:$p,price:8,validFrom:$f}')")")
echo "$R" | jq -e --arg s "$SKA" '.ownerKind=="CLIENT" and ([.rows[] | select(.sku==$s and .price==8)] | length==1) and ([.chainRows[] | select(.sku==$s and .price==7)] | length==1)
  and ([.rows[] | select(.sku==$s and .chargedPrice==7 and .chargedSource=="CHAIN_LIST")] | length==1)' >/dev/null || fail "ficha de C1: $R"
ITEM_A1=$(echo "$R" | jq -r --arg s "$SKA" '[.rows[] | select(.sku==$s)][0].itemId')
msg "$(expect 400 "$(req POST $SP/clients/$C1 "$(jq -cn --arg p "$PA" --arg f "$D_HACE10" --arg t "$D_HACE3" '{productPublicId:$p,price:5,validFrom:$f,validTo:$t}')")")" \
  "La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después."; ok "propio 8, de la cadena 7, se cobra 7 (CHAIN_LIST); «hasta» pasado rechazado"

step "catálogo: gana el más bajo"
I=$(cat_item $C1 $SKA); echo "$I" | jq -e '.price==7 and .priceSource=="CHAIN_LIST" and .chainName=="Walg'"$CTS"'"' >/dev/null || fail "catálogo C1/A: $I"
I=$(cat_item $C2 $SKA); echo "$I" | jq -e '.price==9 and .priceSource=="GENERAL_LIST"' >/dev/null || fail "catálogo C2/A: $I"
ok "C1 paga 7 (CHAIN_LIST, cadena Walg$CTS); C2 sin cadena paga 9 (GENERAL_LIST)"

step "lista normal por grupo: sin 409 contra la dedicada; el catálogo sigue en 7"
LGR=$(expect 200 "$(req POST $PL "$(jq -cn --arg n "Grupo$CTS" --arg f "$D_HACE3" '{name:$n,validFrom:$f,appliesToAll:false}')")" | jq -r .publicId)
expect 200 "$(req POST $PL/$LGR/items "$(jq -cn --arg a "$PA" '{items:[{productPublicId:$a,price:7.5}]}')")" >/dev/null
expect 200 "$(req POST $PL/$LGR/clients "{\"clientPublicIds\":[\"$C1\"]}")" >/dev/null
ensure_published $PL/$LGR
I=$(cat_item $C1 $SKA); echo "$I" | jq -e '.price==7' >/dev/null || fail "con la lista de grupo C1/A debía seguir en 7: $I"; ok "lista de grupo A=7.50 publicada; C1 sigue en 7"

step "aviso: precio especial más caro que la lista general"
R=$(expect 200 "$(req POST $SP/clients/$C1 "$(jq -cn --arg p "$PB" --arg f "$D_HACE3" '{productPublicId:$p,price:9.5,validFrom:$f}')")")
msg "$R" "Precio especial más caro que la lista general: $SKB a \$9.50 y en la lista general a \$9.00. Se cobra el más bajo; corrija el precio especial."
echo "$R" | jq -e '[.warnings[] | select(.code=="SPECIAL_ABOVE_GENERAL")] | length==1' >/dev/null || fail "warning SPECIAL_ABOVE_GENERAL: $R"
I=$(cat_item $C1 $SKB); echo "$I" | jq -e '.price==9 and .priceSource=="GENERAL_LIST"' >/dev/null || fail "catálogo C1/B: $I"
msg "$I" "pone $SKB a \$9.50, más caro que la lista general (\$9.00): se aplica el de la lista general. Corrija el precio especial."; ok "aviso en la ficha y en el catálogo; se cobra 9"

step "cruce de fechas (409) y cambio de fechas (400 / mueve la fila)"
msg "$(expect 409 "$(req POST $SP/clients/$C1 "$(jq -cn --arg p "$PA" --arg f "$D_MANANA" --arg t "$D_MAS10" '{productPublicId:$p,price:6,validFrom:$f,validTo:$t}')")")" \
  "en las mismas fechas y para los mismos clientes o cadenas en: $SKA. Cambie las fechas o quite esos productos de una de las dos."
msg "$(expect 400 "$(req PATCH $SP/clients/$C1/$ITEM_A1 "{\"validTo\":\"$D_HACE10\"}")")" "La fecha «hasta» no puede ser anterior a la fecha «desde»."
R=$(expect 200 "$(req PATCH $SP/clients/$C1/$ITEM_A1 "{\"validFrom\":\"$D_HACE1\",\"validTo\":\"$D_MAS30\"}")")
echo "$R" | jq -e --arg s "$SKA" --arg f "$D_HACE1" --arg t "$D_MAS30" '[.rows[] | select(.sku==$s and .validFrom==$f and .validTo==$t and .price==8)] | length==1' >/dev/null || fail "la fila no se movió: $R"
ok "409 Overlap, 400 fechas, la fila pasa a la dedicada de las fechas nuevas"

step "lista dedicada bloqueada"
LDC=$(echo "$R" | jq -r --arg s "$SKA" '[.rows[] | select(.sku==$s)][0].priceListPublicId')
msg "$(expect 422 "$(req PATCH $PL/$LDC "{\"validFrom\":\"$D_HACE10\"}")")" "Esta lista es el precio especial de «Cliente Uno $CTS»: el nombre, las fechas y a quién aplica se cambian en «Precios especiales» de su ficha."; ok "422 DedicatedLocked"

step "ofertas con público: excluir y incluir cadenas"
msg "$(expect 400 "$(req POST $OF "$(jq -cn --arg n "Conflicto $CTS" --arg f "$D_HACE3" '{name:$n,validFrom:$f,appliesToAll:true,audienceMode:"EXCLUDE"}')")")" \
  "“Aplica a todos” no coincide con el público escogido: indique solo el público (todos, solo los escogidos o todos menos los escogidos)."
OX=$(expect 200 "$(req POST $OF "$(jq -cn --arg n "Excl$CTS" --arg f "$D_HACE3" '{name:$n,validFrom:$f,audienceMode:"EXCLUDE"}')")" | jq -r .publicId)
expect 200 "$(req POST $OF/$OX/chains "{\"chainPublicIds\":[\"$CHAIN\"]}")" >/dev/null
expect 200 "$(req POST $OF/$OX/items "$(jq -cn --arg a "$PA" '{items:[{productPublicId:$a,price:6}]}')")" >/dev/null
ensure_published $OF/$OX
I=$(cat_item $C1 $SKA); echo "$I" | jq -e '.price==7 and .priceSource=="CHAIN_LIST"' >/dev/null || fail "C1 (excluido) debía seguir en 7: $I"
I=$(cat_item $C2 $SKA); echo "$I" | jq -e '.price==6 and .priceSource=="OFFER"' >/dev/null || fail "C2 debía recibir la oferta a 6: $I"
msg "$(expect 422 "$(req PATCH $OF/$OX '{"audienceMode":"INCLUDE"}')")" "Quite los clientes y cadenas de la oferta antes de cambiar entre incluir y excluir."
OI=$(expect 200 "$(req POST $OF "$(jq -cn --arg n "Incl$CTS" --arg f "$D_HACE3" '{name:$n,validFrom:$f,audienceMode:"INCLUDE"}')")" | jq -r .publicId)
expect 200 "$(req POST $OF/$OI/chains "{\"chainPublicIds\":[\"$CHAIN\"]}")" >/dev/null
expect 200 "$(req POST $OF/$OI/items "$(jq -cn --arg b "$PB" '{items:[{productPublicId:$b,price:5}]}')")" >/dev/null
ensure_published $OF/$OI
I=$(cat_item $C3 $SKB); echo "$I" | jq -e '.price==5 and .priceSource=="OFFER"' >/dev/null || fail "C3 debía recibir B=5: $I"
I=$(cat_item $C2 $SKB); echo "$I" | jq -e '.price==9' >/dev/null || fail "C2 debía quedarse en 9: $I"
expect 200 "$(req GET "$OF?chainPublicId=$CHAIN")" | jq -e --arg x "$OX" --arg i "$OI" '([.[] | select(.publicId==$i)] | length==1) and ([.[] | select(.publicId==$x)] | length==0)' >/dev/null \
  || fail "GET /offers?chainPublicId debía traer solo la oferta que INCLUYE la cadena"
msg "$(expect 404 "$(req POST $OF/$OI/chains '{"chainPublicIds":["00000000-0000-0000-0000-000000000001"]}')")" "Cadena no encontrada."
ok "EXCLUDE y INCLUDE por cadena, cambio de público bloqueado, filtro por cadena, 404 de cadena inexistente"

step "orden de venta: el origen del precio queda guardado"
O=$(expect 200 "$(req POST /api/v1/sales-orders "$(jq -cn --arg w "$W" --arg c "$C1" --arg a "$PA" '{warehousePublicId:$w,clientPublicId:$c,lines:[{productPublicId:$a,quantity:2}]}')")")
OID=$(echo "$O" | jq -r .publicId)
echo "$O" | jq -e --arg n "Walg$CTS" '.lines[0].unitPrice==7 and .linePrices[0].priceSource=="CHAIN_LIST" and .linePrices[0].chainName==$n' >/dev/null || fail "borrador de C1: $O"
expect 200 "$(req POST /api/v1/sales-orders/$OID/request)" >/dev/null
expect 200 "$(req GET /api/v1/sales-orders/$OID)" | jq -e --arg n "Walg$CTS" '.lines[0].priceSource=="CHAIN_LIST" and .lines[0].priceChainName==$n and .lines[0].resolvedUnitPrice==7 and .lines[0].isManualPrice==false' >/dev/null \
  || fail "la orden solicitada no guardó el origen del precio"
ok "borrador a 7 con CHAIN_LIST y cadena; solicitada conserva priceSource, priceChainName y resolvedUnitPrice"

step "precio especial vencido y quitar la última fila de una vigencia"
R=$(expect 200 "$(req POST $SP/clients/$C2 "$(jq -cn --arg p "$PB" --arg f "$D_HACE10" '{productPublicId:$p,price:4,validFrom:$f}')")")
ITEM_B2=$(echo "$R" | jq -r --arg s "$SKB" '[.rows[] | select(.sku==$s)][0].itemId')
R=$(expect 200 "$(req PATCH $SP/clients/$C2/$ITEM_B2 "{\"validTo\":\"$D_AYER2\"}")")
echo "$R" | jq -e '[.rows[]] | length==0' >/dev/null || fail "el vencido no debía salir sin includeExpired: $R"
R=$(expect 200 "$(req GET "$SP/clients/$C2?includeExpired=true")")
echo "$R" | jq -e --arg s "$SKB" '[.rows[] | select(.sku==$s and .state=="EXPIRED")] | length==1' >/dev/null || fail "includeExpired debía traer el vencido: $R"
I=$(cat_item $C2 $SKB); echo "$I" | jq -e '.price==9 and .priceSource=="GENERAL_LIST"' >/dev/null || fail "el vencido no debía cobrarse: $I"
ITEM_B2=$(echo "$R" | jq -r --arg s "$SKB" '[.rows[] | select(.sku==$s)][0].itemId'); LVEN=$(echo "$R" | jq -r --arg s "$SKB" '[.rows[] | select(.sku==$s)][0].priceListPublicId')
expect 200 "$(req DELETE "$SP/clients/$C2/$ITEM_B2?includeExpired=true")" >/dev/null
expect 200 "$(req GET "$PL?includeSpecial=true&includeInactive=true")" | jq -e --arg l "$LVEN" '[.[] | select(.publicId==$l and .isActive==false)] | length==1' >/dev/null || fail "la dedicada sin renglones debía quedar inactiva"
ok "vencido oculto y sin cobrarse; al quitar la última fila la dedicada queda inactiva"

step "lista normal con cadenas: publicar y quitar la última cadena"
LC=$(expect 200 "$(req POST $PL "$(jq -cn --arg n "ListaCadena$CTS" --arg f "$D_HACE3" '{name:$n,validFrom:$f,appliesToAll:false}')")" | jq -r .publicId)
expect 200 "$(req POST $PL/$LC/items "$(jq -cn --arg d "$PD" '{items:[{productPublicId:$d,price:9.9}]}')")" >/dev/null
expect 200 "$(req POST $PL/$LC/chains "{\"chainPublicIds\":[\"$CHAIN\"]}")" >/dev/null
ensure_published $PL/$LC
msg "$(expect 422 "$(req DELETE $PL/$LC/chains/$CHAIN)")" "Escoja al menos un cliente o una cadena, o marque que la lista aplica a todos los clientes."; ok "422 PublishNeedsClients al quitar la última cadena de una lista publicada"

step "desactivar una cadena con precios especiales"
VAC=$(expect 200 "$(req POST $CH_ "{\"name\":\"Vacia$CTS\"}")" | jq -r .publicId)
R=$(expect 200 "$(req POST $SP/chains/$VAC "$(jq -cn --arg p "$PA" --arg f "$D_HACE3" '{productPublicId:$p,price:7,validFrom:$f}')")")
ITEM_V=$(echo "$R" | jq -r --arg s "$SKA" '[.rows[] | select(.sku==$s)][0].itemId')
msg "$(expect 409 "$(req POST $CH_/$VAC/deactivate)")" "La cadena tiene precios especiales u ofertas publicados y vigentes (1 listas de precios y 0 ofertas): quítela de ellos antes de desactivarla."
expect 200 "$(req DELETE $SP/chains/$VAC/$ITEM_V)" >/dev/null
expect 200 "$(req POST $CH_/$VAC/deactivate)" | jq -e '.isActive==false' >/dev/null || fail "tras quitar el precio debía poder desactivarse"; ok "409 HasPricesOrOffers y, sin el precio, 200"

step "fuente de datos SPECIAL_PRICE"
if [[ "$ANALYTICS_ON" == "1" ]]; then
  expect 200 "$(req GET /api/v1/analytics/data-sources)" | jq -e '[.[] | select(.key=="SPECIAL_PRICE")] | length==1' >/dev/null || fail "SPECIAL_PRICE no está entre las fuentes de datos"; ok "SPECIAL_PRICE disponible"
else ok "omitido: no se pudo encender el módulo de análisis en esta base"; fi

step "seguridad: sales.view sin sales.pricelist"
expect 200 "$(req GET $SP/clients/$C1 '' "$T2")" >/dev/null
expect 403 "$(req POST $SP/clients/$C1 "$(jq -cn --arg p "$PA" --arg f "$D_HACE3" '{productPublicId:$p,price:6,validFrom:$f}')" "$T2")" >/dev/null
expect 403 "$(req PATCH $SP/clients/$C1/$ITEM_A1 '{"price":6}' "$T2")" >/dev/null
expect 403 "$(req DELETE $SP/clients/$C1/$ITEM_A1 '' "$T2")" >/dev/null; ok "lectura 200; alta, cambio y baja 403"

step "otra compañía: módulo apagado y aislamiento"
BADM="adminprec$CTS@smoke.local"; BPASS="Smoke_Admin_2026!"
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Precios $CTS\",\"phoneNumber\":\"787-555-0100\",\"address\":{\"line1\":\"Calle Smoke 1\",\"city\":\"San Juan\"},\"modules\":[\"CATALOG\"],\"adminEmail\":\"$BADM\",\"adminFullName\":\"Admin Precios\",\"adminPassword\":\"$BPASS\",\"mfaRequired\":false}" "$PTOK")" >/dev/null
TB=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$BADM\",\"password\":\"$BPASS\"}")" | jq -r .tokens.accessToken)
expect 403 "$(req GET $SP/chains/$CHAIN '' "$TB")" >/dev/null; ok "módulo SALES_ORDERS apagado: 403"
TBID=$(expect 200 "$(req GET /api/v1/me '' "$TB")" | jq -r .tenantId)
enable_module "$(platform_token "$TBID")" SALES_ORDERS
msg "$(expect 404 "$(req GET $SP/chains/$CHAIN '' "$TB")")" "Cadena no encontrada."
expect 404 "$(req GET $SP/clients/$C1 '' "$TB")" >/dev/null
OB=$(expect 200 "$(req POST $CH_ "{\"name\":\"Ajena$CTS\"}" "$TB")" | jq -r .publicId)
msg "$(expect 404 "$(req POST $OF/$OI/chains "{\"chainPublicIds\":[\"$OB\"]}")")" "Cadena no encontrada."; ok "404 en cadenas y clientes ajenos, también al ponerla en una oferta propia"

printf '\n\033[1;32mHumo de precios especiales y ofertas por cliente y cadena: todo en orden\033[0m\n'
