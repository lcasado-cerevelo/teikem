#!/usr/bin/env bash
# Instalador de Teikem (API + web con HTTPS) para un servidor Linux. Hace preguntas, instala Docker si falta, guarda la
# configuración en deploy/.env y levanta todo. Se puede volver a correr: conserva la configuración si usted quiere.
#   bash install.sh
set -euo pipefail
cd "$(dirname "$0")"
source ./lib.sh

say()  { printf '\n\033[1;36m== %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m%s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m%s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m%s\033[0m\n' "$*" >&2; exit 1; }

ask() { # ask "Pregunta" "valor por defecto" -> imprime la respuesta
  local prompt="$1" def="${2:-}" ans
  if [[ -n "$def" ]]; then read -r -p "$prompt [$def]: " ans; echo "${ans:-$def}"; else read -r -p "$prompt: " ans; echo "$ans"; fi
}
ask_secret() { local ans; read -r -s -p "$1: " ans; echo >&2; echo "$ans"; }
check_value() { [[ "$2" != *"'"* ]] || die "$1 no puede llevar comilla simple (')."; }

# ---------------------------------------------------------------- Docker
say "Docker"
if ! command -v docker >/dev/null 2>&1; then
  warn "Docker no está instalado."
  if command -v apt-get >/dev/null 2>&1; then
    echo "Se instala con el script oficial (get.docker.com); pedirá su contraseña de sudo."
    curl -fsSL https://get.docker.com | sudo sh
  elif command -v dnf >/dev/null 2>&1; then
    sudo dnf install -y docker
    sudo mkdir -p /usr/local/lib/docker/cli-plugins
    sudo curl -fsSL "https://github.com/docker/compose/releases/latest/download/docker-compose-linux-$(uname -m)" -o /usr/local/lib/docker/cli-plugins/docker-compose
    sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
  else
    die "No sé instalar Docker en esta distribución. Instálelo (https://docs.docker.com/engine/install/) y vuelva a correr este script."
  fi
  sudo systemctl enable --now docker
fi
DC="docker"
if ! docker info >/dev/null 2>&1; then DC="sudo docker"; fi
$DC compose version >/dev/null 2>&1 || die "Falta el plugin 'docker compose'. Instálelo y vuelva a correr este script."
ok "$($DC --version)"

# ---------------------------------------------------------------- configuración (.env)
say "Configuración"
write_env=true
if [[ -f .env ]]; then
  warn "Ya existe deploy/.env (una instalación anterior)."
  read -r -p "¿Conservar esa configuración? [S/n]: " keep
  if [[ "${keep:-S}" =~ ^[sS]?$ ]]; then write_env=false; fi
fi

if $write_env; then
  domain=$(ask "Dominio de la web (para probar por IP sin HTTPS escriba :80)" "teikem.advancelogisticspr.com")
  if [[ "$domain" == ":80" ]]; then
    acme_email="admin@example.com"
  else
    acme_email=$(ask "Correo para los avisos de Let's Encrypt (certificado HTTPS automático)")
    [[ "$acme_email" == *@*.* ]] || die "Escriba un correo válido para el certificado."
  fi
  echo
  echo "Base de datos de Teikem (la que ya subió):"
  db_server=$(ask "  Servidor y puerto (ej. 10.0.0.5,1433)")
  db_name=$(ask "  Nombre de la base" "Teikem")
  db_user=$(ask "  Usuario")
  db_pass=$(ask_secret "  Contraseña (no se ve al escribir)")
  [[ -n "$db_server" && -n "$db_user" && -n "$db_pass" ]] || die "Servidor, usuario y contraseña de la base son obligatorios."
  echo
  echo "Correo (Brevo) para el código de verificación del primer ingreso:"
  brevo_key=$(host_var Brevo__ApiKey); brevo_from=$(host_var Brevo__FromEmail)
  if [[ -n "$brevo_key" && -n "$brevo_from" ]]; then
    ok "  Se encontraron Brevo__ApiKey y Brevo__FromEmail en las variables de este servidor: se usan (no se piden)."
  else
    warn "  No encontré Brevo__ApiKey / Brevo__FromEmail en las variables de este servidor (ni en /etc/environment)."
    brevo_key=$(ask "  Brevo API key (Enter para dejarlo vacío)" "$brevo_key")
    brevo_from=$(ask "  Correo remitente" "$brevo_from")
    [[ -n "$brevo_key" && -n "$brevo_from" ]] || warn "Sin Brevo no se podrá enviar el código del primer ingreso (se puede completar después en deploy/.env)."
  fi

  for v in "$domain" "$acme_email" "$db_server" "$db_name" "$db_user" "$db_pass" "$brevo_key" "$brevo_from"; do check_value "Un valor" "$v"; done

  if command -v openssl >/dev/null 2>&1; then jwt=$(openssl rand -base64 48 | tr -d '\n'); else jwt=$(head -c 48 /dev/urandom | base64 | tr -d '\n'); fi

  umask 077
  cat > .env <<EOF
TEIKEM_DOMAIN='$domain'
TEIKEM_ACME_EMAIL='$acme_email'
ConnectionStrings__Teikem='Server=$db_server;Database=$db_name;User Id=$db_user;Password=$db_pass;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True'
Jwt__SigningKey='$jwt'
Brevo__ApiKey='$brevo_key'
Brevo__FromEmail='$brevo_from'
Brevo__FromName='Teikem'
EOF
  chmod 600 .env
  ok "Configuración guardada en deploy/.env (solo la puede leer su usuario)."
fi

# Si las llaves de Brevo están en las variables del servidor, se copian a deploy/.env (también al conservar la configuración).
if sync_brevo_from_host; then ok "Brevo: llaves tomadas de las variables del servidor."; fi

# ---------------------------------------------------------------- construir y levantar
say "Construyendo (la primera vez tarda varios minutos)"
$DC compose build
say "Levantando"
$DC compose up -d

say "Comprobando"
domain_now=$(grep -E '^TEIKEM_DOMAIN=' .env | sed -E "s/^TEIKEM_DOMAIN='?([^']*)'?$/\1/")
healthy=false
for i in $(seq 1 30); do
  if $DC compose exec -T web wget -qO- http://api:8080/health 2>/dev/null | grep -q '"status":"ok"'; then healthy=true; break; fi
  sleep 3
done
if $healthy; then
  ok "El API responde."
  echo
  if [[ "$domain_now" == ":80" ]]; then echo "Abra:  http://IP-DE-ESTE-SERVIDOR"; else echo "Abra:  https://$domain_now"; fi
  if [[ "$domain_now" != ":80" ]]; then
    echo "Certificado HTTPS: lo pide Caddy a Let's Encrypt y lo renueva solo; puede tardar un minuto la primera vez."
    echo "Requisitos: el DNS de $domain_now debe apuntar a este servidor y los puertos 80 y 443 deben estar abiertos"
    echo "(en AWS: grupo de seguridad de la instancia). Si falla:  $DC compose logs web"
  fi
else
  warn "El API no respondió a tiempo. Últimas líneas del registro:"
  $DC compose logs --tail 40 api || true
  die "Revise la cadena de conexión a la base (deploy/.env) y que este servidor pueda llegar a ella."
fi
echo
echo "Comandos útiles (desde esta carpeta):"
echo "  $DC compose logs -f api      # ver el registro del API"
echo "  $DC compose ps               # estado"
echo "  bash update.sh               # después de subir una versión nueva"
