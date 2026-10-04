import { defineConfig, devices } from '@playwright/test'

// Recorre la aplicación real contra el API real (http://localhost:5000, ya levantado con db-init hecho), igual que scripts/smoke.sh.
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  // la primera carga en frío del Pulso (varias consultas del API a la vez con 4 workers) pasa a veces de los 5 s por defecto
  expect: { timeout: 15_000 },
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.WEB_URL ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // entorno sin `playwright install` (contenedor de Claude Code): PW_CHROMIUM_PATH=/opt/pw-browsers/chromium
    ...(process.env.PW_CHROMIUM_PATH ? { launchOptions: { executablePath: process.env.PW_CHROMIUM_PATH } } : {}),
  },
  webServer: process.env.WEB_URL ? undefined : {
    command: 'npm run dev -- --port 5173 --strictPort',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 60_000,
    // Vite lee web-app/.env.development (VITE_API_URL=https://localhost:5001, el perfil https de launchSettings): sin esto el
    // navegador del recorrido apunta a un puerto donde en el CI no hay nada ("No se pudo conectar con el servidor" en el login).
    // El recorrido habla con el mismo API que las pruebas por request (API_URL, :5000 por defecto).
    env: { VITE_API_URL: process.env.VITE_API_URL ?? process.env.API_URL ?? 'http://localhost:5000' },
  },
  projects: [
    { name: 'escritorio', testIgnore: /(f8a|lote16|loteF9-region|loteF11-marca|loteF12-conteo|loteF13-conteo-web|loteF14-codigos|loteF15|loteF16)\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
    // Móvil al ancho mínimo que exige el kit (360 px), con el resto del perfil de Pixel 7 (táctil, isMobile).
    { name: 'movil', testIgnore: /(lote16|loteF9-region|loteF11-marca|loteF12-conteo|loteF13-conteo-web|loteF14-codigos|loteF15|loteF16)\.spec\.ts/, use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // El recorrido de escritorio de F8a reorganiza el Pulso personal del admin y el de toda la compañía (oculta el panel
    // Almacén unos segundos): corre después de los demás para no chocar con los recorridos que leen ese Pulso cuando hay
    // varios workers en paralelo.
    { name: 'escritorio-f8a', testMatch: /f8a\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Desktop Chrome'] } },
    // Lote 16: pasa ALM-01 a "Directo a posición" un rato (y lo devuelve): corre después de los demás para no chocar con los
    // recorridos que esperan tareas de acomodo en ALM-01.
    { name: 'escritorio-lote16', testMatch: /lote16\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Desktop Chrome'] } },
    { name: 'movil-lote16', testMatch: /lote16\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // Lote F12 (conteo por producto): siembra posiciones con movimientos en ALM-01 y conteos Contados; corre después de los demás para
    // que esos movimientos no entren en el "Conteo de lo cambiado" del Lote 14 (que crea un conteo por posición cambiada).
    { name: 'escritorio-f12', testMatch: /loteF12-conteo\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Desktop Chrome'] } },
    { name: 'movil-f12', testMatch: /loteF12-conteo\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // Lote F13 (crear un conteo por producto desde la web): crea posiciones con movimientos en ALM-01 y conteos abiertos; corre después
    // de los demás y de F12 para que esos movimientos no entren en el "Conteo de lo cambiado" del Lote 14 ni en la lista de F12.
    { name: 'escritorio-f13', testMatch: /loteF13-conteo-web\.spec\.ts/, dependencies: ['escritorio', 'movil', 'escritorio-f12', 'movil-f12'], use: { ...devices['Desktop Chrome'] } },
    { name: 'movil-f13', testMatch: /loteF13-conteo-web\.spec\.ts/, dependencies: ['escritorio', 'movil', 'escritorio-f12', 'movil-f12'], use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // Lote F14 (reportes de códigos de barras): siembra productos y posiciones propios y descarga los PDF; corre después de F13
    // (no deja movimientos ni conteos) y antes de F9, para que la región y los formatos de la compañía sean los de siempre.
    { name: 'escritorio-f14', testMatch: /loteF14-codigos\.spec\.ts/, dependencies: ['escritorio-f13', 'movil-f13'], use: { ...devices['Desktop Chrome'] } },
    { name: 'movil-f14', testMatch: /loteF14-codigos\.spec\.ts/, dependencies: ['escritorio-f13', 'movil-f13'], use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // Lote F15 (hojas de posición): siembra posiciones y un producto propios y los MUEVE en ALM-01 (ajuste y transferencia);
    // corre después de F14 (y por eso de F12/F13: sus movimientos no entran en el "Conteo de lo cambiado" de otros
    // recorridos) y antes de F9, con la región y los formatos de siempre ("Impresa el …").
    { name: 'escritorio-f15', testMatch: /loteF15\.spec\.ts/, dependencies: ['escritorio-f14', 'movil-f14'], use: { ...devices['Desktop Chrome'] } },
    { name: 'movil-f15', testMatch: /loteF15\.spec\.ts/, dependencies: ['escritorio-f14', 'movil-f14'], use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // Lote F16 (etiquetas de posición): siembra posiciones vacías propias en ALM-01 (sin movimientos) y descarga los PDF;
    // corre después de F15 (sus posiciones no entran en las listas ni en los avisos que F15 cuenta) y antes de F9.
    { name: 'escritorio-f16', testMatch: /loteF16\.spec\.ts/, dependencies: ['escritorio-f15', 'movil-f15'], use: { ...devices['Desktop Chrome'] } },
    { name: 'movil-f16', testMatch: /loteF16\.spec\.ts/, dependencies: ['escritorio-f15', 'movil-f15'], use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // Lote F9: cambia la región y los formatos de la compañía demo (y los restaura): al final de todo, solo, para no cambiarle
    // la hora o la fecha a otro recorrido a medio camino.
    {
      name: 'escritorio-f9',
      testMatch: /loteF9-region\.spec\.ts/,
      dependencies: ['escritorio-f8a', 'escritorio-lote16', 'movil-lote16', 'escritorio-f12', 'movil-f12', 'escritorio-f13', 'movil-f13', 'escritorio-f14', 'movil-f14', 'escritorio-f15', 'movil-f15', 'escritorio-f16', 'movil-f16'],
      use: { ...devices['Desktop Chrome'] },
    },
    // Lote F11: sube logos y un tema a la compañía demo (y los restaura): al final de todo, después de F9, para no repintar la marca
    // de otro recorrido a medio camino.
    {
      name: 'escritorio-f11',
      testMatch: /loteF11-marca\.spec\.ts/,
      dependencies: ['escritorio-f9'],
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
