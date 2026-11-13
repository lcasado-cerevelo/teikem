import { defineConfig } from '@playwright/test'

// Configuración propia para GRABAR los videos (docs/video/REGLAS.md). No toca playwright.config.ts.
// 1920×1080, una toma por escena, un solo worker. El storageState lo deja el spec en una pasada previa (fuera de cámara).
export default defineConfig({
  testDir: './video/escenas',
  testMatch: /.*\.spec\.ts/,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  workers: 1,
  retries: 0,
  reporter: [['list']],
  outputDir: process.env.VIDEO_OUT ?? 'F:/Download/TeikemVideo/solutions-proceso/tomas/_pw',
  use: {
    baseURL: process.env.WEB_URL ?? 'http://localhost:5174',
    viewport: { width: 1920, height: 1080 },
    video: { mode: 'on', size: { width: 1920, height: 1080 } },
    locale: 'es-PR',
    trace: 'off',
    screenshot: 'off',
  },
})
