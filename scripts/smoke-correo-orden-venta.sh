#!/usr/bin/env bash
# Prueba de humo del correo con la orden de venta al solicitarla (Lote 243, como Formsite) contra un API levantado: eventos y plantillas, vista previa (con ejemplo y con la orden
# real), validaciones de la regla, destinatarios del evento (vendedor y cliente), el aviso por el canal APP, los correos al buzón local, el motivo de lo que no sale, una sola vez por orden
# y la regla apagada.
# NUNCA contra Brevo: se NIEGA a correr si el canal EMAIL está configurado y no es local (buzón). Sin correo configurado recorre la vista previa y el canal APP; con el canal EMAIL local
# (API en Development con Email__Smtp__Host, p. ej. scripts/api-para-pruebas.ps1 -Mailpit) enciende también EMAIL y, si hay MAILPIT_URL (p. ej. http://localhost:8025), lee los correos en Mailpit.
# Requiere: curl, jq. Uso: [MAILPIT_URL=http://localhost:8025] scripts/smoke-correo-orden-venta.sh [base_url]
set -euo pipefail
BASE="${1:-http://localhost:5000}"
EMAIL="${TEIKEM_ADMIN_EMAIL:-teikem+admin@cerevelo.com}"
PASS="${TEIKEM_ADMIN_PASSWORD:-Teikem_Admin_2026!}"
MAILPIT="${MAILPIT_URL:-}"
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
TOKEN=$(expect 200 "$(req POST /api/v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PASS\",\"deviceInfo\":\"smoke-correo-orden-venta\"}")" | jq -r .tokens.accessToken); ok "token"

# SALES_ORDERS viene apagado en una base recién creada (db-init) y solo el administrador de la plataforma lo enciende (con reautenticación, como
# ptokfor en smoke.sh). Encenderlo otra vez no hace daño. TOKEN=… delante de req solo vale dentro de esa sustitución: el token del administrador
# de la compañía queda igual para el resto del recorrido.
step "módulo SALES_ORDERS encendido (administrador de la plataforma, con reautenticación)"
PEMAIL="${TEIKEM_PLATFORM_EMAIL:-teikem+support@cerevelo.com}"; PPASS="${TEIKEM_PLATFORM_PASSWORD:-$PASS}"
TID=$(expect 200 "$(req GET /api/v1/me)" | jq -r .tenantId)
PTOK=$(expect 200 "$(TOKEN= req POST /api/v1/auth/login "{\"email\":\"$PEMAIL\",\"password\":\"$PPASS\",\"tenantId\":$TID,\"deviceInfo\":\"smoke-correo-orden-venta\"}")" | jq -r .tokens.accessToken)
PTOK=$(expect 200 "$(TOKEN=$PTOK req POST /api/v1/auth/reauth "{\"password\":\"$PPASS\"}")" | jq -r .accessToken)
expect 200 "$(TOKEN=$PTOK req PUT /api/v1/modules/SALES_ORDERS '{"isEnabled":true}')" | jq -e '.[] | select(.key=="SALES_ORDERS") | .isEnabled' >/dev/null || fail "encender SALES_ORDERS"
ok "SALES_ORDERS encendido"

step "guarda de correo: nunca contra Brevo"
CH=$(expect 200 "$(req GET /api/v1/notifications/channels)")
EMAIL_CONF=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isConfigured // false')
EMAIL_LOCAL=$(echo "$CH" | jq -r '[.[] | select(.channel=="EMAIL")][0].isLocal // false')
if [[ "$EMAIL_CONF" == "true" && "$EMAIL_LOCAL" != "true" ]]; then fail "el API tiene correo real (Brevo): esta prueba no corre así; levante el API sin llave (scripts/api-para-pruebas.ps1) o con el buzón local"; fi
if [[ "$EMAIL_LOCAL" == "true" ]]; then MODE=mailbox; ok "canal EMAIL al buzón local"; else MODE=app; ok "canal EMAIL sin configurar: se recorre la vista previa y el canal APP"; fi
[[ -n "$MAILPIT" && "$MODE" != "mailbox" ]] && { printf '   (MAILPIT_URL ignorada: el canal EMAIL del API no es local)\n'; MAILPIT=""; }

step "el evento «Orden de venta solicitada»: bloque, destinatarios del evento y plantilla de correo"
EV=$(expect 200 "$(req GET /api/v1/notifications/events)")
echo "$EV" | jq -e '[.[] | select(.code=="SALES_ORDER_REQUESTED")][0] | ([.variables[] | select(.name=="orderDetail" and .isBlock==true)] | length==1)
  and ([.contextRecipients[] | select(.key=="SELLER" and .isDefault==true)] | length==1)
  and ([.contextRecipients[] | select(.key=="CLIENT" and .isDefault==false)] | length==1)
  and ([.channelDefaults[] | select(.channel=="EMAIL" and (.body | contains("{orderDetail}")))] | length==1)' >/dev/null || fail "evento SALES_ORDER_REQUESTED: $(echo "$EV" | jq -c '[.[] | select(.code=="SALES_ORDER_REQUESTED")][0]')"; ok "evento completo"

step "vista previa con el ejemplo (sin documento real)"
PV=$(expect 200 "$(req POST /api/v1/notifications/preview '{"eventCode":"SALES_ORDER_REQUESTED","channel":"EMAIL","subject":"Orden {orderNumber}","body":"Hola\n\n{orderDetail}"}')")
echo "$PV" | jq -e '.usedSample==true and (.bodyHtml | contains("Farmacia Central"))' >/dev/null || fail "vista previa con ejemplo: $PV"; ok "ejemplo con Farmacia Central"

step "datos: almacén, kit de 2 piezas, producto con stock y clientes con y sin correos"
W=$(expect 200 "$(req POST /api/v1/warehouses "{\"code\":\"O$TS\",\"name\":\"Correo orden $TS\"}")" | jq -r .warehouse.publicId)
ZP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/zones" '{"code":"PCK","name":"Picking","zoneType":"PICKING"}')" | jq -r .id)
BP=$(expect 200 "$(req POST "/api/v1/warehouses/$W/bins" "{\"zoneId\":$ZP,\"code\":\"P-01\"}")" | jq -r .id)
mkprod() { expect 200 "$(req POST /api/v1/products "{\"sku\":\"$1$TS\",\"name\":\"$2 $TS\",\"purchaseCost\":2,\"salePrice\":$3}")" | jq -r .product.publicId; }
PA=$(mkprod CA "Pieza A" 5); PB=$(mkprod CB "Pieza B" 3); PN=$(mkprod CN "Producto normal" 7)
# el kit que se arma al pedir es un kit de oferta (Lote 231: Productos ya no convierte un producto en kit) y solo se vende si está en una oferta publicada y
# vigente: se crea una oferta (nace publicada, para todos los clientes) con el kit nuevo
OF=$(expect 200 "$(req POST /api/v1/offers "$(jq -cn --arg n "Oferta correo $TS" --arg f "$(date -u -d '2 days ago' +%F)" '{name:$n,validFrom:$f}')")" | jq -r .publicId)
PK=$(expect 200 "$(req POST "/api/v1/offers/$OF/kits" "$(jq -cn --arg s "CK$TS" --arg n "Kit correo $TS" --arg a "$PA" --arg b "$PB" '{sku:$s,name:$n,price:11,components:[{productPublicId:$a,quantity:1},{productPublicId:$b,quantity:2}]}')")" | jq -r .productPublicId)
adj() { expect 200 "$(req POST /api/v1/inventory/adjustments "$(jq -cn --arg p "$1" --arg w "$W" --argjson b "$BP" --argjson q "$2" '{productPublicId:$p,warehousePublicId:$w,binId:$b,quantity:$q,reason:"FOUND",notes:"humo correo orden"}')")" >/dev/null; }
adj "$PA" 50; adj "$PB" 50; adj "$PN" 50
mkclient() { expect 200 "$(req POST /api/v1/clients "{\"name\":\"$1 $TS\",\"contract\":{\"startDate\":\"2026-01-01\"}}")" | jq -r .publicId; }
CA=$(mkclient "Cliente A"); CB=$(mkclient "Cliente B")
addcontact() { # cliente nombre principal marcada correo
  local body; body=$(jq -cn --arg n "$2" --argjson p "$3" --argjson m "$4" --arg e "$5" '{fullName:$n,isPrimary:$p,receivesEmailNotifications:$m,contactPoints:[{contactType:"EMAIL",value:$e,isPrimary:true}]}')
  expect 200 "$(req POST "/api/v1/clients/$1/contacts" "$body")" >/dev/null; }
MAIL_PRINCIPAL="principal-$TS@example.test"; MAIL_MARCADO="marcado-$TS@example.test"; MAIL_NOMARCADO="nomarcado-$TS@example.test"; MAIL_ALMACEN="almacen-$TS@example.test"
addcontact "$CA" "Contacto principal" true false "$MAIL_PRINCIPAL"
addcontact "$CA" "Contacto marcado" false true "$MAIL_MARCADO"
addcontact "$CA" "Contacto sin marcar" false false "$MAIL_NOMARCADO"
CT=$(expect 200 "$(req GET "/api/v1/clients/$CA/contacts")")
echo "$CT" | jq -e '([.[] | select(.fullName=="Contacto marcado")][0] | .receivesEmailNotifications==true and .hasEmail==true)
  and ([.[] | select(.fullName=="Contacto sin marcar")][0] | .receivesEmailNotifications==false and .hasEmail==true)' >/dev/null || fail "marca del contacto: $CT"
ok "W=$W; clientes A y B; marca y hasEmail del contacto"

mkorder() { # cliente -> publicId (borrador con el kit y el producto normal)
  local body; body=$(jq -cn --arg w "$W" --arg c "$1" --arg k "$PK" --arg n "$PN" --arg note "Nota <b>prueba</b> $TS" \
    '{warehousePublicId:$w,clientPublicId:$c,notes:$note,lines:[{productPublicId:$k,quantity:2},{productPublicId:$n,quantity:3}]}')
  expect 200 "$(req POST /api/v1/sales-orders "$body")" | jq -r .publicId; }
request() { expect 200 "$(req POST "/api/v1/sales-orders/$1/request")"; }

step "orden del cliente A: solicitar y ver el correo por PublicId"
O1=$(mkorder "$CA"); R1=$(request "$O1"); N1=$(echo "$R1" | jq -r .number)
echo "$R1" | jq -e '.status=="REQUESTED" and (.number | startswith("OV-"))' >/dev/null || fail "solicitar: $R1"; ok "$N1 solicitada"
EP=$(expect 200 "$(req GET "/api/v1/sales-orders/$O1/email-preview?audience=STAFF&lang=es")")
echo "$EP" | jq -e --arg n "$N1" --arg k "CK$TS" --arg a "CA$TS" --arg b "CB$TS" --arg c "Cliente A $TS" \
  '.bodyHtml | contains($n) and contains($k) and contains($a) and contains($b) and contains($c) and contains("Subtotal") and contains("Total")' >/dev/null || fail "correo del personal sin número, kit, piezas, cliente o totales: $EP"
echo "$EP" | jq -e '(.attachments | length)==0 and (.bodyHtml | contains("&lt;b&gt;prueba&lt;/b&gt;")) and (.bodyHtml | contains("<b>prueba") | not)' >/dev/null || fail "adjuntos o notas sin codificar: $EP"; ok "personal: número, kit con piezas, cliente, totales y notas codificadas"
EC=$(expect 200 "$(req GET "/api/v1/sales-orders/$O1/email-preview?audience=CUSTOMER&lang=es")")
echo "$EC" | jq -e --arg t "prueba $TS" '.bodyHtml | (contains("Abrir la orden") | not) and (contains($t) | not)' >/dev/null || fail "la copia al cliente no debe llevar enlace ni notas: $EC"; ok "cliente: sin enlace ni notas"
expect 404 "$(req GET "/api/v1/sales-orders/00000000-0000-0000-0000-000000000000/email-preview")" | jq -e '.title=="Orden de venta no encontrada."' >/dev/null || fail "404 de la vista previa por PublicId"
expect 404 "$(req POST /api/v1/notifications/preview '{"eventCode":"SALES_ORDER_REQUESTED","channel":"EMAIL","reference":"OV-99999"}')" | jq -e '.title=="Orden de venta '"'"'OV-99999'"'"' no encontrada."' >/dev/null || fail "404 de la vista previa por número"
expect 200 "$(req POST /api/v1/notifications/preview "$(jq -cn --arg r "$N1" '{eventCode:"SALES_ORDER_REQUESTED",channel:"EMAIL",reference:$r}')")" | jq -e '.usedSample==false' >/dev/null || fail "vista previa por número"; ok "404 con el mensaje exacto y vista previa por número"

step "validaciones de la regla"
expect 400 "$(req POST /api/v1/notifications/rules "{\"eventCode\":\"SALES_ORDER_REQUESTED\",\"name\":\"Mala $TS\",\"recipients\":[{\"kind\":\"CONTEXT\",\"value\":\"FOO\"}]}")" \
  | jq -e '(tostring | contains("Este evento no tiene el destinatario '"'"'FOO'"'"'."))' >/dev/null || fail "destinatario del evento desconocido"
expect 400 "$(req POST /api/v1/notifications/rules "{\"eventCode\":\"SALES_ORDER_REQUESTED\",\"name\":\"Mala2 $TS\",\"channels\":[{\"channel\":\"EMAIL\",\"subjectEs\":\"Orden {orderDetail}\",\"bodyEs\":\"x\"}]}")" \
  | jq -e '(tostring | contains("La variable {orderDetail} es un bloque y solo puede ir en el texto, no en el asunto."))' >/dev/null || fail "bloque en el asunto"; ok "400 con los mensajes exactos"

step "regla del evento: vendedor, almacén y cliente de la orden"
if [[ "$MODE" == "mailbox" ]]; then
  CHANNELS='[{"channel":"APP","isEnabled":true},{"channel":"EMAIL","isEnabled":true}]'
  RECIPIENTS=$(jq -cn --arg a "$MAIL_ALMACEN" '[{kind:"CONTEXT",value:"SELLER"},{kind:"EMAIL",value:$a},{kind:"CONTEXT",value:"CLIENT"}]')
else
  CHANNELS='[{"channel":"APP","isEnabled":true}]'
  RECIPIENTS='[{"kind":"CONTEXT","value":"SELLER"},{"kind":"CONTEXT","value":"CLIENT"}]'
fi
RULE=$(expect 200 "$(req POST /api/v1/notifications/rules "{\"eventCode\":\"SALES_ORDER_REQUESTED\",\"name\":\"Correo con la orden $TS\",\"isActive\":true,\"channels\":$CHANNELS,\"recipients\":$RECIPIENTS}")")
RP=$(echo "$RULE" | jq -r .publicId)
echo "$RULE" | jq -e '([.recipients[] | select(.kind=="CONTEXT" and .value=="SELLER" and .label=="Vendedor de la orden")] | length==1) and ([.recipients[] | select(.value=="CLIENT" and (.label | startswith("Cliente de la orden")))] | length==1) and .isActive==true' >/dev/null || fail "regla: $RULE"; ok "regla $RP activa"

logrows() { expect 200 "$(req GET "/api/v1/notifications/log?eventCode=SALES_ORDER_REQUESTED&take=200")" | jq -c --arg r "Correo con la orden $TS" '[.items[] | select(.ruleName==$r)]'; }
waitlog() { # filtro jq sobre las filas de la regla; espera hasta 60 s
  local rows i; for i in $(seq 1 30); do rows=$(logrows); if echo "$rows" | jq -e "$1" >/dev/null; then echo "$rows"; return 0; fi; sleep 2; done
  fail "el registro de avisos no mostró lo esperado ($1) en 60 s: $rows"; }

step "segunda orden del cliente A: el aviso sale por la regla"
O2=$(mkorder "$CA"); R2=$(request "$O2"); N2=$(echo "$R2" | jq -r .number); ok "$N2 solicitada"
if [[ "$MODE" == "app" ]]; then
  waitlog '[.[] | select(.channel=="APP" and .status=="SENT")] | length>=1' >/dev/null
  IN=$(expect 200 "$(req GET "/api/v1/inbox?take=50")")
  echo "$IN" | jq -e '[.items[] | select(.eventCode=="SALES_ORDER_REQUESTED" and (.linkUrl | startswith("/sales-orders?order=")))] | length>=1' >/dev/null || fail "campanita sin el aviso con enlace a la orden: $IN"; ok "APP enviado y en la campanita con enlace"
else
  # Las reglas se evalúan al enviar: si el aviso de la orden anterior aún estaba en la cola cuando se creó la regla, también la cumple.
  # Por eso se mira solo el aviso (outboxId) más reciente, el de esta orden.
  ROWS=$(waitlog '(max_by(.outboxId).outboxId) as $m | ([.[] | select(.outboxId==$m and .channel=="EMAIL" and .status=="SENT")] | length) >= 3')
  ROWS=$(echo "$ROWS" | jq -c '(max_by(.outboxId).outboxId) as $m | [.[] | select(.outboxId==$m)]')
  for e in "$MAIL_ALMACEN" "$MAIL_PRINCIPAL" "$MAIL_MARCADO"; do
    echo "$ROWS" | jq -e --arg e "$e" '[.[] | select(.channel=="EMAIL" and .status=="SENT" and (.recipient | ascii_downcase)==$e)] | length==1' >/dev/null || fail "falta el correo SENT a $e: $ROWS"; done
  echo "$ROWS" | jq -e --arg e "$MAIL_NOMARCADO" '[.[] | select(.recipient==$e)] | length==0' >/dev/null || fail "el contacto sin marcar no debe recibir: $ROWS"; ok "EMAIL SENT a almacén, principal y marcado; ninguno al sin marcar"

  step "cliente B sin correos: fila SKIPPED con el motivo"
  O3=$(mkorder "$CB"); request "$O3" >/dev/null
  waitlog '[.[] | select(.channel=="EMAIL" and .status=="SKIPPED" and .recipient=="(Cliente de la orden)" and (.error | contains("El cliente no tiene correo principal ni contactos marcados con correo.")))] | length==1' >/dev/null; ok "(Cliente de la orden) SKIPPED con su motivo"

  if [[ -n "$MAILPIT" ]]; then
    step "Mailpit: el correo recibido"
    mp() { curl -sS "$MAILPIT$1"; }
    find_msg() { local to=$1 i id; for i in $(seq 1 15); do
        id=$(mp "/api/v1/search?query=$(jq -rn --arg q "to:$to subject:\"$N2\"" '$q|@uri')" | jq -r '.messages[0].ID // empty'); [[ -n "$id" ]] && { echo "$id"; return 0; }; sleep 2; done; return 1; }
    MID=$(find_msg "$MAIL_ALMACEN") || fail "Mailpit no recibió el correo de $N2 para $MAIL_ALMACEN"
    MSG=$(mp "/api/v1/message/$MID")
    echo "$MSG" | jq -e --arg n "$N2" --arg k "CK$TS" '(.Subject | startswith("Orden de venta " + $n + " — Cliente A")) and (.HTML | contains($k) and contains("Subtotal") and contains("Abrir la orden"))' >/dev/null || fail "correo del almacén: asunto o cuerpo incompletos"
    CID=$(find_msg "$MAIL_PRINCIPAL") || fail "Mailpit no recibió la copia al cliente ($MAIL_PRINCIPAL)"
    mp "/api/v1/message/$CID" | jq -e --arg t "prueba $TS" '.HTML | (contains("Abrir la orden") | not) and (contains($t) | not)' >/dev/null || fail "la copia al cliente en Mailpit lleva enlace o notas"; ok "personal con enlace y cliente sin enlace ni notas"
  fi
fi

step "una sola vez por orden"
BEFORE=$(logrows | jq 'length')
expect 422 "$(req POST "/api/v1/sales-orders/$O2/request")" | jq -e '.title=="Solo se puede solicitar una orden en borrador."' >/dev/null || fail "volver a solicitar debe ser 422"
sleep 5; [[ "$(logrows | jq 'length')" == "$BEFORE" ]] || fail "volver a solicitar cambió el registro de avisos"; ok "422 y registro sin cambios"

step "regla apagada: no sale nada"
expect 200 "$(req PATCH "/api/v1/notifications/rules/$RP" '{"isActive":false}')" | jq -e '.isActive==false' >/dev/null || fail "apagar la regla"
sleep 3; BEFORE=$(logrows | jq 'length')
O4=$(mkorder "$CA"); request "$O4" >/dev/null; sleep 20
[[ "$(logrows | jq 'length')" == "$BEFORE" ]] || fail "la regla apagada registró avisos de la tercera orden"; ok "sin avisos nuevos"

printf '\n\033[1;32mSMOKE CORREO DE LA ORDEN DE VENTA OK (%s)\033[0m\n' "$MODE"
