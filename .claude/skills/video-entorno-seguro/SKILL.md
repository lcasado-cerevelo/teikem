---
name: video-entorno-seguro
description: Chequeo de 6 pasos que hay que pasar ANTES de levantar el API, sembrar datos o grabar un video de Teikem (base de video, sin correo, localhost, sin TEST ni producción). Si algo falla, se detiene e informa.
---
# Entorno seguro para producir videos

Es la regla R1 de `docs/video/REGLAS.md` convertida en pasos. Si **cualquiera** falla: detente, di cuál y no sigas.

1. **Brevo vacío.** El proceso del API se levanta con `Brevo__ApiKey` vacía. `Brevo__ApiKey` es una variable de usuario de Windows: un API iniciado con `dotnet run`, Visual Studio o `preview_start «api»` la hereda y mandaría correo de verdad. Usa solo `scripts/api-para-pruebas.ps1` (o quita la variable del proceso).
2. **Base `Teikem_video*`.** `ConnectionStrings__Teikem` apunta a una base cuyo nombre empieza con `Teikem_video`. Nunca `Teikem`, `SEPHAS`, `MSWM`, TEST ni producción. Comprobación: `SELECT DB_NAME()` desde la misma cadena.
3. **Localhost.** `API_URL` y `WEB_URL` son `http://localhost:<puerto>`; los puertos 5000/5173 pueden estar ocupados por el desarrollo de Luis: usa otros (p. ej. 5733 y 5174). No uses el API que Luis tiene abierto.
4. **Sin onboarding.** `Auth__Onboarding__Enabled=false`. Si no, el administrador pide pantalla de bienvenida y MFA.
5. **Archivos propios.** `Files__Root` apunta a la copia `F:/Download/TeikemVideo/base/files` (las fotos y firmas no viven en la base; el API no debe escribir en la carpeta del desarrollo de Luis).
6. **`globalSetup` pasa.** `web-app/e2e/globalSetup.ts` se niega si el API tiene el correo configurado; si se niega, no lo evites con `E2E_ALLOW_REAL_EMAIL`.

7. **Holgura de la máquina.** `scripts/demo/video-solutions-proceso/guardia-recursos.ps1 -Estado` debe dar OK (RAM libre ≥ 16 GB y procesador ≤ 60 %). Con PRECAUCION se espera; con ALTO no se arranca nada. Todo lo pesado se corre con `guardia-recursos.ps1 -Archivo <script.ps1>` (prioridad baja, 8 hilos, perro guardián). Ver «Guardia de recursos» en `docs/video/REGLAS.md`.

Además: una corrida pesada a la vez (sin suites ni builds en paralelo), un worker, y al terminar apagar el API y Vite que levantaste. Nunca copies contraseñas ni cadenas de conexión a archivos, logs ni informes.
