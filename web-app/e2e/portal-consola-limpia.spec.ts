// El portal del cliente (web) no debe llenar la consola de avisos de React: p. ej. «React does not recognize the `importantForAccessibility` prop» (la mascota de Kemi en web, 2026-10-26).
// OJO: React solo escribe esos avisos en modo DESARROLLO; contra un `expo export` (producción) esta prueba pasa siempre. Córrala contra `npx expo start --web --port 8090` con CLIENT_APP_URL=http://localhost:8090.
// Entra a Almacén y a Inicio con la mascota a la vista y falla si algún aviso de React sale en la consola.
import { expect, test } from '@playwright/test'

import { APP_URL, createClientWithAdmin, loginAsClientAdmin } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('el portal del cliente no deja avisos de React en la consola', async ({ page, request }) => {
  const suffix = Date.now().toString(36)
  const c = await createClientWithAdmin(request, suffix, 'Consola limpia')
  const avisos: string[] = []
  page.on('console', (m) => {
    if (['error', 'warning'].includes(m.type()) && /React does not recognize|Unknown prop|Invalid DOM property|validateDOMNesting/i.test(m.text())) avisos.push(m.text())
  })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)
  await expect(page.getByTestId('kemi-mascot').first()).toBeVisible({ timeout: 30_000 })
  await page.waitForTimeout(1500)
  expect(avisos).toEqual([])
})
