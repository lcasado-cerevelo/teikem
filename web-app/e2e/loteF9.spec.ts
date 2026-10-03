// Recorrido del lote F9 (Ajustes de la compañía) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics"). Este archivo NO deja cambios: abre las pestañas y comprueba el
// ancho (móvil, 360 px) y lo que se lee. El cambio de región (que afecta a toda la compañía) está en loteF9-region.spec.ts,
// en su propio proyecto después de los demás.
import { expect, test, type Page } from '@playwright/test'

const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }

test.use({ locale: 'es-PR' })

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login', { timeout: 30_000 })
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que lote15/16). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    const describe = (el: Element) => `${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')}`
    document.querySelectorAll('.stage *, .bar *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      const r = el.getBoundingClientRect()
      if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${describe(el)} [${Math.round(r.left)}, ${Math.round(r.right)}] fuera de 0..${vw}`)
    })
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

const TABS: [string, string, RegExp | string][] = [
  ['', 'General', 'Datos de la compañía'],
  ['region', 'Región y formatos', 'Vista previa'],
  ['calendar', 'Calendario', 'Días laborables'],
  ['modules', 'Módulos', 'Módulos activos de la compañía'],
  ['ops', 'Operación', 'Valores por defecto'],
  ['brand', 'Marca', 'Colores y logo de la compañía'],
]

test('Ajustes de la compañía: las seis pestañas se abren con su contenido y sin scroll horizontal', async ({ page }) => {
  await login(page)
  await page.goto('/system/settings')
  await expect(page.getByRole('heading', { level: 1, name: 'Ajustes de la compañía' })).toBeVisible()
  for (const [param, tab, heading] of TABS) {
    await page.getByRole('tab', { name: tab }).click()
    await expect(page).toHaveURL(param ? new RegExp(`tab=${param}`) : /\/system\/settings$/)
    await expect(page.getByRole('heading', { name: heading }).first()).toBeVisible()
    await page.waitForLoadState('networkidle')
    await expectNoHorizontalScroll(page)
  }
  // Región y formatos: la vista previa con los valores de Puerto Rico de la demo
  await page.goto('/system/settings?tab=region')
  const preview = page.getByTestId('format-preview')
  await expect(preview.locator('[data-pv="pvMoney"]')).toHaveText('$1,234,567.50')
  await expect(preview.locator('[data-pv="pvPhone"]')).toHaveText('(787) 555-0142')
  await expect(preview.locator('[data-pv="pvToday"]')).toHaveText(/^\d{2}\/\d{2}\/\d{4}$/)
  await expect(page.getByRole('button', { name: 'Puerto Rico' })).toHaveAttribute('aria-pressed', 'true')
  await expectNoHorizontalScroll(page)
})
