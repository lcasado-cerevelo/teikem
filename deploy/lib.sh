#!/usr/bin/env bash
# Funciones comunes de install.sh y update.sh (no se ejecuta solo).

# Lee una variable de entorno del SERVIDOR (no del contenedor): primero del entorno de quien corre el script y, si no está ahí,
# de /etc/environment (donde suelen definirse las variables de todo el sistema). Imprime vacío si no existe.
host_var() {
  local name="$1" val="${!1:-}"
  if [[ -z "$val" && -r /etc/environment ]]; then
    val=$(grep -E "^(export )?${name}=" /etc/environment | tail -n1 | sed -E "s/^(export )?${name}=//; s/^['\"]//; s/['\"]\$//") || true
  fi
  printf '%s' "$val"
}

# Pone NOMBRE='valor' en deploy/.env (lo cambia si ya está, lo agrega si no). El valor no puede llevar comilla simple.
set_env() {
  local name="$1" val="$2"
  [[ "$val" != *"'"* ]] || return 1
  umask 077
  { grep -v "^${name}=" .env 2>/dev/null || true; printf "%s='%s'\n" "$name" "$val"; } > .env.tmp
  mv .env.tmp .env
  chmod 600 .env
}

# Copia las llaves de Brevo de las variables del servidor a deploy/.env (los contenedores no heredan el entorno del servidor,
# por eso hay que pasárselas). Devuelve 0 si encontró la llave y el remitente.
sync_brevo_from_host() {
  local key from name
  key=$(host_var Brevo__ApiKey); from=$(host_var Brevo__FromEmail)
  [[ -n "$key" && -n "$from" ]] || return 1
  set_env Brevo__ApiKey "$key" && set_env Brevo__FromEmail "$from" || return 1
  name=$(host_var Brevo__FromName); [[ -z "$name" ]] || set_env Brevo__FromName "$name" || true
  return 0
}
