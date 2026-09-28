import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
    // zona horaria fija distinta de UTC: las pruebas "sin zona = UTC" (fechas del API sin zona) distinguen hora local de UTC.
    env: { TZ: 'America/Puerto_Rico' },
  },
})
