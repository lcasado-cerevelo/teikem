#!/usr/bin/env bash
# Prueba de humo de las rutas de venta (Lote 244, Lote A del plan de rutas) contra un API levantado: ajuste de la cuota (REP | ROUTE), rutas y representantes con titular automático,
# personas de la ruta (titular, apoyo, cierre, anulación y concurrencia), ficha de venta del cliente (ruta y vendedor preferido), cuotas por ruta o por persona, vendedor de la orden
# (propuesto, elección y copia al solicitar), inmutabilidad de la copia, desactivar una ruta, análisis por ruta, «Pasar rutas desde representantes» (simulación), permisos,
# módulo apagado y aislamiento entre compañías. Cada mensaje de error se compara con el texto exacto.
# NUNCA contra Brevo ni correo real: se NIEGA a correr si el canal EMAIL está configurado y no es local (buzón). No verifica correos. Solo contra un API en Development, nunca contra
# TEST ni producción: crea datos con el sufijo $TS (rutas, usuarios, cliente, orden) y no los borra.
# Requiere: curl, jq. Uso: scripts/smoke-rutas.sh [base_url]   (API de pruebas: scripts/api-para-pruebas.ps1, con Auth__Onboarding__Enabled=false)
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
SA=/api/v1/sales-analysis
TS=$(date +%s)
MES=$(date +%Y-%m); MES_SIG=$(date -d "$(date +%Y-%m-01) +1 month" +%Y-%m); MANANA=$(date -d tomorrow +%F)

step "login"
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-rutas\"}")" | jq -r .tokens.accessToken); ok "token"
ME=$(expect 200 "$(req GET /api/v1/me)"); TID=$(echo "$ME" | jq -r .tenantId); ADMIN=$(echo "$ME" | jq -r .userId)
[[ "$ADMIN" =~ ^[0-9]+$ ]] || fail "no se pudo leer el id del administrador en /me: $ME"; ok "administrador $ADMIN"

step "guarda de correo: nunca contra Brevo"
CH=$(expect 200 "$(req GET /api/v1/notifications/channels)")
EMAIL_CONF=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isConfigured // false')
EMAIL_LOCAL=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isLocal // false')
if [[ "$EMAIL_CONF" == "true" && "$EMAIL_LOCAL" != "true" ]]; then fail "el API tiene correo real (Brevo): esta prueba no corre así; levante el API sin llave (scripts/api-para-pruebas.ps1)"; fi
ok "sin correo real"

# SALES_ORDERS viene apagado en una base recién creada y solo el administrador de la plataforma lo enciende (con reautenticación). Encenderlo otra vez no hace daño.
PEMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"; PPASS="${TEIKEM_PLATFORM_PASSWORD:-$PASS}"
platform_token() { # tenantId -> token de la plataforma reautenticado en esa compañía
  local t; t=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\",\"tenantId\":$1,\"deviceInfo\":\"smoke-rutas\"}")" | jq -r .tokens.accessToken)
  expect 200 "$(req POST /api/v1/auth/reauth "{\"password\":\"$PPASS\"}" "$t")" | jq -r .accessToken; }
step "módulo SALES_ORDERS encendido"
PTOK=$(platform_token "$TID")
expect 200 "$(req PUT /api/v1/modules/SALES_ORDERS '{"isEnabled":true}' "$PTOK")" | jq -e '.[] | select(.key=="SALES_ORDERS") | .isEnabled' >/dev/null || fail "encender SALES_ORDERS"
ok "SALES_ORDERS encendido"

step "usuario de ventas sin sales.analysis.manage (segundo usuario)"
RN="Ventas $TS"
expect 200 "$(req POST /api/v1/roles "{\"name\":\"$RN\",\"permissions\":[\"sales.view\",\"sales.create\"]}")" >/dev/null
U2EMAIL="vendedor$TS@teikem.local"
U2=$(expect 200 "$(req POST /api/v1/users "{\"email\":\"$U2EMAIL\",\"fullName\":\"Vendedor Dos $TS\",\"password\":\"$PASS\",\"roles\":[\"$RN\"]}")" | jq -r .user.id)
U3=$(expect 200 "$(req POST /api/v1/users "{\"email\":\"vendedor3$TS@teikem.local\",\"fullName\":\"Vendedor Tres $TS\",\"password\":\"$PASS\",\"roles\":[\"$RN\"]}")" | jq -r .user.id)
T2=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$U2EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-rutas\"}")" | jq -r .tokens.accessToken)
ok "usuarios $U2 y $U3"

step "ajustes: cuota por persona (REP) o por ruta (ROUTE)"
# En una base nueva empieza en REP; se vuelve a poner para poder repetir el humo sobre la misma base.
expect 200 "$(req PUT $SA/settings '{"quotaBy":"REP"}')" | jq -e '.quotaBy=="REP"' >/dev/null || fail "quotaBy REP"
expect 200 "$(req GET $SA/settings)" | jq -e '.quotaBy=="REP"' >/dev/null || fail "quotaBy debía ser REP"
msg "$(expect 400 "$(req PUT $SA/settings '{"quotaBy":"X"}')")" "La cuota se lleva por ruta (ROUTE) o por persona (REP)."
expect 200 "$(req PUT $SA/settings '{"quotaBy":"ROUTE"}')" | jq -e '.quotaBy=="ROUTE"' >/dev/null || fail "quotaBy ROUTE"; ok "quotaBy validado y puesto en ROUTE"

step "rutas y representantes con titular automático"
mkroute() { expect 200 "$(req POST $SA/routes "{\"name\":\"$1$TS\"}")" | jq -r .id; }
NORTE=$(mkroute Norte); METRO=$(mkroute Metro); SUR=$(mkroute Sur); ok "rutas Norte $NORTE, Metro $METRO, Sur $SUR"
msg "$(expect 400 "$(req POST $SA/reps "{\"name\":\"Sin usuario $TS\",\"routeId\":$SUR}")")" "Escoja el usuario del representante: el vendedor siempre es un usuario de Teikem."
REP=$(expect 200 "$(req POST $SA/reps "{\"name\":\"Rep$TS\",\"routeId\":$SUR,\"userId\":$U2}")")
echo "$REP" | jq -e '.becamePrimary==true' >/dev/null || fail "el representante debía quedar de titular: $REP"
expect 200 "$(req GET $SA/routes/$SUR/people)" | jq -e --argjson u "$U2" '.primary.userId==$u and .primary.role=="PRIMARY"' >/dev/null || fail "titular automático de Sur"
msg "$(expect 409 "$(req POST $SA/reps "{\"name\":\"Otro$TS\",\"routeId\":$SUR,\"userId\":$U2}")")" "El usuario ya es el representante «Rep$TS»."
ok "titular automático y usuario único"

step "personas de la ruta Norte: titular, apoyo, cierre, anulación"
asg() { req POST "$SA/routes/$NORTE/people" "$1"; }
expect 200 "$(asg "{\"userId\":$ADMIN,\"role\":\"PRIMARY\"}")" | jq -e --argjson u "$ADMIN" '.primary.userId==$u' >/dev/null || fail "admin titular de Norte"
msg "$(expect 409 "$(asg "{\"userId\":$ADMIN,\"role\":\"PRIMARY\"}")")" "La persona ya es titular de esta ruta."
expect 200 "$(asg "{\"userId\":$U2,\"role\":\"SUPPORT\"}")" | jq -e --argjson u "$U2" '[.supports[] | select(.userId==$u)] | length==1' >/dev/null || fail "apoyo de Norte"
expect 200 "$(asg "{\"userId\":$U2,\"role\":\"PRIMARY\"}")" >/dev/null
H=$(expect 200 "$(req GET "$SA/routes/$NORTE/people?history=true")")
echo "$H" | jq -e --argjson u "$U2" --argjson a "$ADMIN" '.primary.userId==$u and ([.history[] | select(.userId==$a and .validTo != null)] | length>=1)' >/dev/null || fail "historial de Norte: $H"
ASG_ADMIN=$(echo "$H" | jq -r --argjson a "$ADMIN" '[.history[] | select(.userId==$a)][0].id')
msg "$(expect 400 "$(asg "{\"userId\":$ADMIN,\"role\":\"PRIMARY\",\"validFrom\":\"$MANANA\"}")")" "La fecha de inicio no puede ser futura."
msg "$(expect 400 "$(asg "{\"userId\":$ADMIN,\"role\":\"JEFE\"}")")" "El papel debe ser PRIMARY (titular) o SUPPORT (apoyo)."
expect 200 "$(req POST "$SA/routes/$NORTE/people/$ASG_ADMIN/void")" >/dev/null
expect 200 "$(req GET "$SA/routes/$NORTE/people?history=true")" | jq -e --argjson i "$ASG_ADMIN" '[.history[] | select(.id==$i)] | length==0' >/dev/null || fail "la asignación anulada no debía salir"
msg "$(expect 409 "$(req POST "$SA/routes/$NORTE/people/$ASG_ADMIN/void")")" "La asignación ya está anulada."
expect 200 "$(asg "{\"userId\":$ADMIN,\"role\":\"PRIMARY\"}")" | jq -e --argjson u "$ADMIN" '.primary.userId==$u' >/dev/null || fail "devolver el titular de Norte al admin"
ok "personas de la ruta con sus mensajes"

step "concurrencia: dos titulares a la vez en Metro (nunca dos vigentes)"
# Cada petición responde 200 (entró y cerró al otro) o 409 (perdió la carrera por el índice o la versión); lo que no puede pasar es que queden dos titulares vigentes.
for u in "$ADMIN" "$U3"; do
  ( req POST "$SA/routes/$METRO/people" "{\"userId\":$u,\"role\":\"PRIMARY\"}" | tail -n1 > "$TMPD/conc.$u" ) &
done
wait
for u in "$ADMIN" "$U3"; do c=$(cat "$TMPD/conc.$u"); [[ "$c" == "200" || "$c" == "409" ]] || fail "titular simultáneo de $u: se esperaba 200 o 409, dio $c"; done
expect 200 "$(req GET "$SA/routes/$METRO/people")" | jq -e '.primary != null and (.supports | length)==0' >/dev/null || fail "Metro debía quedar con un solo titular vigente"
ok "respuestas $(cat "$TMPD/conc.$ADMIN") y $(cat "$TMPD/conc.$U3"); un solo titular vigente"

step "ficha del cliente: ruta y vendedor preferido"
CL=$(expect 200 "$(req POST /api/v1/clients "{\"name\":\"Cliente rutas $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")"); CID=$(echo "$CL" | jq -r .id); CPID=$(echo "$CL" | jq -r .publicId)
msg "$(expect 400 "$(req PUT $SA/clients/$CID/sales "{\"preferredUserId\":$ADMIN}")")" "El cliente necesita una ruta antes de asignarle un vendedor preferido."
expect 200 "$(req PUT $SA/clients/$CID/sales "{\"routeId\":$NORTE,\"preferredUserId\":$ADMIN}")" >/dev/null
CS=$(expect 200 "$(req GET $SA/clients/$CID/sales)")
echo "$CS" | jq -e --argjson r "$NORTE" --argjson a "$ADMIN" '.routeId==$r and .routeFromRep==false and .preferred.userId==$a and .proposedUserId==$a and .preferredOutOfRoute==false' >/dev/null || fail "ficha de venta: $CS"
expect 200 "$(req GET /api/v1/clients/$CPID)" | jq -e --arg n "Norte$TS" --argjson a "$ADMIN" '.salesRouteName==$n and .preferredSalesUserId==$a' >/dev/null || fail "ficha del cliente sin ruta o preferido"
msg "$(expect 400 "$(req PUT $SA/clients/$CID/sales '{"preferredUserId":999999}')")" "El vendedor preferido debe ser una persona activa de la ruta del cliente."
IDS=$(jq -cn '{clientIds:[range(1;2002)],routeId:null}')
msg "$(expect 400 "$(req POST $SA/clients/assign-route "$IDS")")" "No se pueden mandar más de 2000 renglones en una sola llamada."; ok "ruta, preferido y límites"

step "cuotas por ruta y por persona (conviven en el mismo mes)"
expect 200 "$(req PUT $SA/quotas/$MES "{\"items\":[{\"routeId\":$NORTE,\"amount\":1000.00}]}")" >/dev/null
RID=$(expect 200 "$(req GET $SA/reps)" | jq -r --arg n "Rep$TS" '[.[] | select(.name==$n)][0].id')
msg "$(expect 400 "$(req PUT $SA/quotas/$MES "{\"items\":[{\"repId\":$RID,\"routeId\":$NORTE,\"amount\":5}]}")")" "Cada renglón lleva el representante (repId) o la ruta (routeId), uno de los dos."
expect 200 "$(req PUT $SA/quotas/$MES "{\"items\":[{\"repId\":$RID,\"amount\":500}]}")" >/dev/null
expect 200 "$(req GET "$SA/quotas?month=$MES_SIG&by=ROUTE")" | jq -e --argjson r "$NORTE" '.quotaBy=="ROUTE" and ([.routeRows[] | select(.routeId==$r and .effective==1000)] | length==1)' >/dev/null || fail "cuota heredada de Norte"; ok "cuota de ruta heredada al mes siguiente"

step "orden de venta: vendedor propuesto, elección y copia al solicitar"
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"R$TS\",\"name\":\"Rutas $TS\"}")" | jq -r .warehouse.publicId)
ZP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"PCK","name":"Picking","zoneType":"PICKING"}')" | jq -r .id)
BP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$ZP,\"code\":\"P-01\"}")" | jq -r .id)
PN=$(expect 200 "$(req POST /api/v1/products "{\"sku\":\"RT$TS\",\"name\":\"Producto rutas $TS\",\"purchaseCost\":2,\"salePrice\":7}")" | jq -r .product.publicId)
expect 200 "$(req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$PN" --arg w "$W" --argjson b "$BP" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:50,reason:"FOUND",notes:"humo rutas"}')")" >/dev/null
O=$(expect 200 "$(req POST /api/v1/sales-orders "$(jq -cn --arg w "$W" --arg c "$CPID" --arg n "$PN" '{warehousePublicId:$w,clientPublicId:$c,lines:[{productPublicId:$n,quantity:3}]}')")")
OID=$(echo "$O" | jq -r .publicId)
echo "$O" | jq -e --argjson a "$ADMIN" '.sellerUserId==$a and .sellerProposed==true and .salesCopyAtUtc==null' >/dev/null || fail "borrador con vendedor propuesto: $O"
SO=$(expect 200 "$(req GET "/api/v1/sales-orders/seller-options?client=$CPID")")
echo "$SO" | jq -e --argjson a "$ADMIN" '.routeId != null and ([.options[] | select(.userId==$a and .isPreferred==true)] | length>=1) and ([.options[] | select(.kind=="PRIMARY")] | length>=1) and (tostring | contains("@") | not)' >/dev/null || fail "opciones de vendedor: $SO"
msg "$(expect 400 "$(req PATCH /api/v1/sales-orders/$OID "{\"sellerUserId\":$U3}")")" "El vendedor debe ser el vendedor preferido del cliente o una persona vigente de su ruta."
RQ=$(expect 200 "$(req POST /api/v1/sales-orders/$OID/request)")
echo "$RQ" | jq -e --argjson r "$NORTE" --argjson a "$ADMIN" '.salesRouteId==$r and .sellerUserId==$a and .salesCopyAtUtc != null' >/dev/null || fail "copia al solicitar: $RQ"
# la marca se lee de la orden guardada (la de la respuesta de solicitar puede traer otra precisión de fracción)
COPY=$(expect 200 "$(req GET /api/v1/sales-orders/$OID)" | jq -r .salesCopyAtUtc); ok "ruta y vendedor copiados a la orden"

step "inmutabilidad: cambiar el cliente de ruta no toca la orden ya solicitada"
expect 200 "$(req PUT $SA/clients/$CID/sales "{\"routeId\":$METRO,\"clearPreferred\":true}")" >/dev/null
expect 200 "$(req GET /api/v1/sales-orders/$OID)" | jq -e --argjson r "$NORTE" --argjson a "$ADMIN" --arg c "$COPY" '.salesRouteId==$r and .sellerUserId==$a and .salesCopyAtUtc==$c' >/dev/null || fail "la orden solicitada cambió con el cliente"
expect 200 "$(req GET "/api/v1/sales-orders?routeId=$NORTE&take=200")" | jq -e --arg o "$OID" '[(.items // .)[] | select(.publicId==$o)] | length==1' >/dev/null || fail "filtro routeId=Norte debía incluir la orden"
expect 200 "$(req GET "/api/v1/sales-orders?routeId=$METRO&take=200")" | jq -e --arg o "$OID" '[(.items // .)[] | select(.publicId==$o)] | length==0' >/dev/null || fail "filtro routeId=Metro no debía incluir la orden"
expect 200 "$(req GET "/api/v1/sales-orders?sellerUserId=$ADMIN&take=200")" | jq -e --arg o "$OID" '[(.items // .)[] | select(.publicId==$o)] | length==1' >/dev/null || fail "filtro sellerUserId debía incluir la orden"; ok "orden congelada y filtros por ruta y vendedor"

step "desactivar una ruta"
msg "$(expect 409 "$(req POST $SA/routes/$METRO/deactivate)")" "La ruta tiene clientes asignados: páselos a otra ruta antes de desactivarla."
expect 200 "$(req PUT $SA/clients/$CID/sales "{\"routeId\":$NORTE}")" >/dev/null
expect 200 "$(req POST $SA/routes/$METRO/deactivate)" | jq -e '.isActive==false' >/dev/null || fail "desactivar Metro"
expect 200 "$(req GET "$SA/routes/$METRO/people")" | jq -e '.primary==null and (.supports | length)==0' >/dev/null || fail "Metro desactivada debía quedar sin personas vigentes"; ok "desactivada con sus personas cerradas"

step "análisis por ruta, estado, simulación de «Pasar rutas» y por representante"
BR=$(expect 200 "$(req GET "$SA/by-route?month=$MES")")
echo "$BR" | jq -e --argjson r "$NORTE" '.quotaBy=="ROUTE" and .reconciliation.difference==0 and ([.routes[] | select(.routeId==$r and .quota==1000)] | length==1)' >/dev/null || fail "by-route: $(echo "$BR" | jq -c '{quotaBy,reconciliation,r:[.routes[]|{routeId,quota}]}')"
expect 200 "$(req GET $SA/status)" | jq -e 'has("routesWithoutPrimary") and has("repsWithoutUser") and has("clientsWithoutRoute")' >/dev/null || fail "status sin los avisos de rutas"
RB=$(expect 200 "$(req GET $SA/routes)")
FR=$(expect 200 "$(req POST "$SA/routes/from-reps?dryRun=true")")
echo "$FR" | jq -e '.dryRun==true and has("repsWithoutUser")' >/dev/null || fail "from-reps dryRun: $FR"
[[ "$(expect 200 "$(req GET $SA/routes)" | jq -S -c .)" == "$(echo "$RB" | jq -S -c .)" ]] || fail "la simulación escribió: las rutas cambiaron"
expect 200 "$(req GET "$SA/by-rep?month=$MES")" >/dev/null; ok "by-route, status, simulación sin escribir y by-rep vigente"

step "seguridad: sin sales.analysis.manage"
expect 403 "$(req POST "$SA/routes/$NORTE/people" "{\"userId\":$ADMIN,\"role\":\"SUPPORT\"}" "$T2")" >/dev/null
expect 200 "$(req GET "/api/v1/sales-orders/seller-options?client=$CPID" '' "$T2")" >/dev/null; ok "403 al escribir; seller-options con sales.create"

step "otra compañía: módulo apagado y aislamiento"
BADM="adminrutas$TS@smoke.local"; BPASS="Smoke_Admin_2026!"
expect 200 "$(req POST /api/v1/platform/tenants "{\"name\":\"Tenant Rutas $TS\",\"phoneNumber\":\"787-555-0100\",\"address\":{\"line1\":\"Calle Smoke 1\",\"city\":\"San Juan\"},\"modules\":[\"CATALOG\"],\"adminEmail\":\"$BADM\",\"adminFullName\":\"Admin Rutas\",\"adminPassword\":\"$BPASS\",\"mfaRequired\":false}" "$PTOK")" >/dev/null
TB=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$BADM\",\"password\":\"$BPASS\"}")" | jq -r .tokens.accessToken)
expect 403 "$(req GET "$SA/routes/$NORTE/people" '' "$TB")" >/dev/null; ok "módulo SALES_ORDERS apagado: 403"
TBID=$(expect 200 "$(req GET /api/v1/me '' "$TB")" | jq -r .tenantId)
PTOKB=$(platform_token "$TBID")
# SALES_ORDERS depende de otros módulos (el API dice cuál falta: «'X' depende de 'Y'»): se encienden de a uno hasta que entra
MOD=SALES_ORDERS
for _ in 1 2 3 4 5 6; do
  R=$(req PUT /api/v1/modules/$MOD '{"isEnabled":true}' "$PTOKB"); C=$(echo "$R" | tail -n1)
  [[ "$C" == "200" && "$MOD" == "SALES_ORDERS" ]] && break
  if [[ "$C" == "200" ]]; then MOD=SALES_ORDERS; continue; fi
  DEP=$(echo "$R" | sed '$d' | jq -r '.title' | sed -n "s/.*depende de '\([A-Z_]*\)'.*/\1/p"); [[ -n "$DEP" ]] || fail "no se pudo encender $MOD: $R"
  MOD=$DEP
done
[[ "$C" == "200" && "$MOD" == "SALES_ORDERS" ]] || fail "SALES_ORDERS no quedó encendido en la compañía nueva: $R"
expect 404 "$(req GET "$SA/routes/$NORTE/people" '' "$TB")" >/dev/null
expect 404 "$(req PUT $SA/clients/$CID/sales "{\"routeId\":$NORTE}" "$TB")" >/dev/null
RB1=$(expect 200 "$(req POST $SA/routes "{\"name\":\"Propia$TS\"}" "$TB")" | jq -r .id)
expect 400 "$(req POST "$SA/routes/$RB1/people" "{\"userId\":$ADMIN,\"role\":\"PRIMARY\"}" "$TB")" >/dev/null; ok "404 en rutas y clientes ajenos; 400 con un usuario de otra compañía"

printf '\n\033[1;32mHumo de rutas de venta: todo en orden\033[0m\n'
