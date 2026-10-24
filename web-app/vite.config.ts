import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // lo compartido con el portal del cliente (motor de ayuda y reglas de Kemi) vive fuera de web-app: ../shared
  server: { fs: { allow: ['..'] } },
})
