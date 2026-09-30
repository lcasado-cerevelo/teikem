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
  },
  webServer: process.env.WEB_URL ? undefined : {
    command: 'npm run dev -- --port 5173 --strictPort',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 60_000,
  },
  projects: [
    { name: 'escritorio', testIgnore: /(f8a|lote16)\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
    // Móvil al ancho mínimo que exige el kit (360 px), con el resto del perfil de Pixel 7 (táctil, isMobile).
    { name: 'movil', testIgnore: /lote16\.spec\.ts/, use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
    // El recorrido de escritorio de F8a reorganiza el Pulso personal del admin y el de toda la compañía (oculta el panel
    // Almacén unos segundos): corre después de los demás para no chocar con los recorridos que leen ese Pulso cuando hay
    // varios workers en paralelo.
    { name: 'escritorio-f8a', testMatch: /f8a\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Desktop Chrome'] } },
    // Lote 16: pasa ALM-01 a "Directo a posición" un rato (y lo devuelve): corre después de los demás para no chocar con los
    // recorridos que esperan tareas de acomodo en ALM-01.
    { name: 'escritorio-lote16', testMatch: /lote16\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Desktop Chrome'] } },
    { name: 'movil-lote16', testMatch: /lote16\.spec\.ts/, dependencies: ['escritorio', 'movil'], use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
  ],
})
