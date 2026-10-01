#!/usr/bin/env bash
# Actualiza Teikem con la versión que acaba de subir a esta carpeta (reconstruye y reinicia; la configuración y los
# certificados se conservan).   bash update.sh
set -euo pipefail
cd "$(dirname "$0")"
[[ -f .env ]] || { echo "No hay deploy/.env: corra primero  bash install.sh" >&2; exit 1; }
DC="docker"
docker info >/dev/null 2>&1 || DC="sudo docker"
$DC compose build
$DC compose up -d
$DC image prune -f >/dev/null
for i in $(seq 1 30); do
  if $DC compose exec -T web wget -qO- http://api:8080/health 2>/dev/null | grep -q '"status":"ok"'; then echo "Listo: el API responde."; exit 0; fi
  sleep 3
done
echo "El API no respondió a tiempo:" >&2
$DC compose logs --tail 40 api >&2 || true
exit 1
