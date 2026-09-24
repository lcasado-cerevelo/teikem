#!/usr/bin/env bash
# Levanta SQL Server 2022 dentro de un contenedor Ubuntu 24.04 sin systemd ni Docker (entorno de Claude Code).
# Requiere red hacia packages.microsoft.com, pmc-geofence.trafficmanager.net y archive.ubuntu.com (HTTPS).
# Uso: scripts/dev-sqlserver.sh            → instala si falta, inicializa con la contraseña de appsettings y arranca.
set -euo pipefail
PASS="${MSSQL_SA_PASSWORD:-Teikem_Dev_2026!}"
export DEBIAN_FRONTEND=noninteractive
if [[ ! -x /opt/mssql/bin/sqlservr ]]; then
  echo "deb [arch=amd64 trusted=yes] https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main" > /etc/apt/sources.list.d/mssql-server.list
  echo "deb [arch=amd64 trusted=yes] https://packages.microsoft.com/ubuntu/22.04/prod jammy main" > /etc/apt/sources.list.d/mssql-tools.list
  sed -i 's|http://archive.ubuntu.com|https://archive.ubuntu.com|g; s|http://security.ubuntu.com|https://security.ubuntu.com|g' /etc/apt/sources.list /etc/apt/sources.list.d/*.sources 2>/dev/null || true
  apt-get update -qq
  apt-get install -y -qq mssql-server jq
  ACCEPT_EULA=Y apt-get install -y -qq mssql-tools18 unixodbc-dev
  # El paquete es de 22.04: en 24.04 falta libldap 2.5
  f=$(curl -sS https://archive.ubuntu.com/ubuntu/pool/main/o/openldap/ | grep -o 'libldap-2.5-0_[^"]*22.04[^"]*_amd64.deb' | sort -V | tail -1)
  curl -sSLo /tmp/libldap25.deb "https://archive.ubuntu.com/ubuntu/pool/main/o/openldap/$f" && dpkg -i /tmp/libldap25.deb
fi
if pgrep -x sqlservr >/dev/null; then echo "SQL Server ya está corriendo."; exit 0; fi
if [[ ! -f /var/opt/mssql/data/master.mdf ]]; then rm -rf /var/opt/mssql/data/* /var/opt/mssql/log/*; fi
# La contraseña de sa se fija desde el entorno en el PRIMER arranque (el contenedor no tiene systemd para mssql-conf)
(ACCEPT_EULA=Y MSSQL_PID=Developer MSSQL_SA_PASSWORD="$PASS" nohup /opt/mssql/bin/sqlservr >/tmp/sqlservr.log 2>&1 &)
for i in $(seq 1 40); do
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$PASS" -Q "SELECT 1" >/dev/null 2>&1 && { echo "SQL Server listo en localhost:1433"; exit 0; }
  sleep 2
done
echo "SQL Server no respondió; revisa /tmp/sqlservr.log" >&2; exit 1
