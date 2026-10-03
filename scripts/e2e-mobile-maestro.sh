#!/usr/bin/env bash
# Corre los flujos Maestro de la app del almacén dentro del emulador Android (lo invoca el job android-e2e de ci.yml).
# Requisitos: emulador ya arrancado, APK en /tmp/apk/app-release.apk y las variables E2E_ENROLL_CODE, E2E_CONSIGNEE_LABEL
# y E2E_SERVER_URL (las deja scripts/e2e-mobile-fixtures.sh en GITHUB_ENV).
#
# Va en un archivo y no inline en `script:` del emulador porque android-emulator-runner ejecuta CADA LÍNEA del `script:` en
# un shell aparte (sh): se perderían `export PATH`, `set -e` y el arreglo ENV_ARGS entre línea y línea.
set -euo pipefail

: "${E2E_ENROLL_CODE:?falta E2E_ENROLL_CODE}" "${E2E_CONSIGNEE_LABEL:?falta E2E_CONSIGNEE_LABEL}" "${E2E_SERVER_URL:?falta E2E_SERVER_URL}"

# Instalar Maestro CLI
curl -Ls "https://get.maestro.mobile.dev" | bash
export PATH="$HOME/.maestro/bin:$PATH"

# Instalar APK en el emulador
adb install -r "${E2E_APK:-/tmp/apk/app-release.apk}"

# Pase lo que pase, la red del emulador queda encendida al terminar (04a la corta)
trap 'adb shell svc wifi enable || true; adb shell svc data enable || true' EXIT

FLOWS="${E2E_FLOWS:-app-almacen/e2e-maestro}"
ENV_ARGS=(-e "E2E_ENROLL_CODE=$E2E_ENROLL_CODE" -e "E2E_CONSIGNEE_LABEL=$E2E_CONSIGNEE_LABEL" -e "E2E_SERVER_URL=$E2E_SERVER_URL")

maestro test "$FLOWS/01-registrar-y-entrar.yaml" "${ENV_ARGS[@]}"
maestro test "$FLOWS/02-recibir.yaml" "${ENV_ARGS[@]}"
maestro test "$FLOWS/03-despacho.yaml" "${ENV_ARGS[@]}"

# 04: recibo sin señal (verifica que queda pendiente) y, ya con señal, que se manda solo. Maestro no tiene un comando
# confiable para cortar la red del emulador, así que se hace con adb alrededor de los dos flujos (ver comentario en
# 04a-recibo-sin-senal.yaml).
adb shell svc wifi disable
adb shell svc data disable
maestro test "$FLOWS/04a-recibo-sin-senal.yaml" "${ENV_ARGS[@]}"
adb shell svc wifi enable
adb shell svc data enable
maestro test "$FLOWS/04b-verificar-envio.yaml" "${ENV_ARGS[@]}"
