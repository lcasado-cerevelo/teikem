import { defineConfig, devices } from '@playwright/test'

// Recorre la aplicación real contra el API real (http://localhost:5000, ya levantado con db-init hecho), igual que scripts/smoke.sh.
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
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
    { name: 'escritorio', use: { ...devices['Desktop Chrome'] } },
    // Móvil al ancho mínimo que exige el kit (360 px), con el resto del perfil de Pixel 7 (táctil, isMobile).
    { name: 'movil', use: { ...devices['Pixel 7'], viewport: { width: 360, height: 780 } } },
  ],
})
