// Kemi en el portal del cliente (Lote 218) en la app del cliente contra el API real: la mascota flotante, su panel, preguntarle ESCRIBIENDO y HABLÁNDOLE (el reconocimiento de voz del navegador se simula,
// porque un navegador de pruebas no tiene micrófono), «¿Te contesté?» y «busca por otro lado», el globo del mensaje nuevo con «Ver», dormirla, despertarla (se acuerda al recargar) y que a un
// CONSIGNATARIO no le sale. Además comprueba que el índice del portal NO trae lo interno del personal.
//
// Datos propios por API: un cliente con persona administradora y un mensaje del equipo sin leer. Requiere el API con Portal:ReturnInviteTokenInResponse y la app del cliente (CLIENT_APP_URL, por omisión
// http://localhost:8081). Capturas (solo escritorio): docs/manual/frontend/img/l218-<paso>.png.
import { expect, test, type Page } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, createConsigneeAccess, loginAsClientAdmin, loginConfirmingTheComputer, post, shot } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

/** Un navegador de pruebas no trae reconocimiento de voz: se simula uno que «oye» lo que la prueba le dicte (window.__say). */
async function fakeSpeech(page: Page) {
  await page.addInitScript(() => {
    class FakeRecognition extends EventTarget {
      lang = ''
      interimResults = false
      continuous = false
      maxAlternatives = 1
      start() {
        ;(window as unknown as { __lang: string }).__lang = this.lang
        ;(window as unknown as { __rec: FakeRecognition }).__rec = this
        this.dispatchEvent(new Event('start'))
      }
      stop() {
        this.dispatchEvent(new Event('end'))
      }
      abort() {
        this.dispatchEvent(new Event('end'))
      }
      say(text: string, final: boolean) {
        const alt = { transcript: text, confidence: 0.9 }
        const list = Object.assign([alt], { isFinal: final, item: () => alt })
        const ev = Object.assign(new Event('result'), { resultIndex: 0, results: Object.assign([list], { item: () => list }) })
        this.dispatchEvent(ev)
        if (final) this.dispatchEvent(new Event('end'))
      }
      fail(error: string) {
        this.dispatchEvent(Object.assign(new Event('error'), { error, message: error }))
        this.dispatchEvent(new Event('end'))
      }
    }
    // Chromium trae las dos (la estándar y la de prefijo): se reemplazan las dos para que ninguna deje pasar el reconocimiento de verdad
    ;(window as unknown as { webkitSpeechRecognition: unknown; SpeechRecognition: unknown }).webkitSpeechRecognition = FakeRecognition
    ;(window as unknown as { SpeechRecognition: unknown }).SpeechRecognition = FakeRecognition
  })
}
const say = (page: Page, text: string, final: boolean) => page.evaluate(([t, f]) => (window as unknown as { __rec: { say(t: string, f: boolean): void } }).__rec.say(t as string, f as boolean), [text, final] as const)

test('Kemi en el portal: preguntarle escribiendo y hablando, el globo del mensaje, dormirla y despertarla', async ({ page, request, isMobile }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil') && !process.env.PORTAL_MOBILE, 'Capturas y recorrido largo: escritorio (el móvil se prueba con PORTAL_MOBILE=1).')
  const appUp = await request.get(`${APP_URL}/login`, { timeout: 5_000 }).then((r) => r.ok(), () => false)
  test.skip(!appUp, `La app del cliente no responde en ${APP_URL}`)
  const suffix = `K${String(Date.now()).slice(-6).replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])}`
  const c = await createClientWithAdmin(request, suffix, 'Kemi portal')

  // el equipo de servicio le escribe primero a la administradora: al entrar tiene un mensaje sin leer y Kemi lo anuncia
  const people = (await (await request.get(`${API_URL}/api/v1/client-messages/clients/${c.client.publicId}/people`, { headers: c.staff })).json()) as { personId: number; fullName: string; email?: string }[]
  const ana = people.find((p) => p.email === c.adminMail) ?? people.find((p) => p.fullName === 'Ana Admin')
  expect(ana, 'la administradora aparece entre las personas del cliente').toBeTruthy()
  await post(request, '/api/v1/client-messages/conversations', c.staff, { clientPublicId: c.client.publicId, personId: ana!.personId, body: 'Hola Ana, ¿puedes confirmarnos la dirección de tu envío?' })

  await fakeSpeech(page)
  await page.setViewportSize(isMobile ? { width: 390, height: 844 } : { width: 1440, height: 900 })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)

  // ------------------------------------------------------------------ 1) Kemi aparece y anuncia el mensaje nuevo
  const fab = page.getByTestId('kemi-fab')
  await expect(fab).toBeVisible({ timeout: 60_000 })
  await expect(page.getByTestId('kemi-bubble')).toContainText('Te llegó un mensaje', { timeout: 60_000 })
  await shot(page, isMobile, 'l218-portal-globo')
  await page.getByTestId('kemi-see').click()
  await expect(page).toHaveURL(/\/messages/)
  await expect(page.getByTestId('kemi-bubble')).toHaveCount(0)

  // ------------------------------------------------------------------ 2) el panel de opciones
  await page.goto(`${APP_URL}/warehouse`)
  await fab.click()
  const panel = page.getByTestId('kemi-panel')
  await expect(panel).toBeVisible()
  for (const opt of ['ask', 'mine', 'avisos', 'sleep']) await expect(page.getByTestId(`kemi-opt-${opt}`)).toBeVisible()
  await shot(page, isMobile, 'l218-portal-panel')

  // ------------------------------------------------------------------ 3) preguntarle escribiendo: encuentra lo del cliente
  await page.getByTestId('kemi-opt-ask').click()
  await expect(page.getByTestId('kemi-send')).toBeDisabled()   // sin texto no se pregunta
  await page.getByTestId('kemi-input').fill('cómo cancelo un aviso de llegada')
  await page.getByTestId('kemi-send').click()
  await expect(page.getByText('Esto encontré:')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l218-portal-opciones')
  await page.getByTestId('kemi-hit-0').click()
  const answer = page.getByTestId('kemi-answer')
  await expect(answer).toBeVisible()
  await expect(answer).toContainText(/cancel/i)
  await shot(page, isMobile, 'l218-portal-respuesta')
  // «No»: busca por otro lado sin repetir; «Sí»: agradece
  await page.getByTestId('kemi-no').click()
  await expect(page.getByText('Busquemos por otro lado:')).toBeVisible({ timeout: 30_000 })
  await page.getByTestId('kemi-hit-0').click()
  await page.getByTestId('kemi-yes').click()
  await expect(page.getByText('¡Qué bueno que te sirvió!')).toBeVisible()

  // ------------------------------------------------------------------ 4) preguntarle HABLANDO
  await page.getByText('Hacer otra pregunta').click()
  const mic = page.getByTestId('kemi-mic')
  await expect(mic).toBeVisible()
  await mic.click()
  await expect(page.getByTestId('kemi-listening')).toBeVisible()
  expect(await page.evaluate(() => (window as unknown as { __lang: string }).__lang)).toBe('es-PR')
  await say(page, 'cómo cambio', false)
  await expect(page.getByTestId('kemi-input')).toHaveValue('cómo cambio')
  await shot(page, isMobile, 'l218-portal-hablando')
  await say(page, 'cómo cambio al modo oscuro', true)
  await expect(page.getByText('Esto encontré:')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('kemi-you')).toHaveText('cómo cambio al modo oscuro')
  await expect(page.getByTestId('kemi-hit-0')).toBeVisible()
  // sin permiso del micrófono se explica
  await page.getByText('Cambiar la pregunta').click()
  await page.getByTestId('kemi-mic').click()
  await page.evaluate(() => (window as unknown as { __rec: { fail(e: string): void } }).__rec.fail('not-allowed'))
  await expect(page.getByTestId('kemi-voice-error')).toContainText('permiso para usar el micrófono')

  // ------------------------------------------------------------------ 5) lo interno del personal no está en lo que Kemi sabe
  await page.getByTestId('kemi-input').fill('liquidación de choferes y permisos de roles')
  await page.getByTestId('kemi-send').click()
  await expect(page.getByTestId('kemi-hit-0').or(page.getByText(/No encontré nada sobre eso/))).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(/Liquidación de choferes|Roles y usuarios/)).toHaveCount(0)

  // ------------------------------------------------------------------ 6) dormirla y despertarla; se acuerda al recargar
  await page.getByTestId('kemi-close').click()
  await fab.click()
  await page.getByTestId('kemi-opt-sleep').click()
  await expect(fab).toHaveCount(0)
  await expect(page.getByTestId('kemi-wake')).toBeVisible()
  await shot(page, isMobile, 'l218-portal-dormida')
  await page.reload()
  await expect(page.getByTestId('kemi-wake')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByTestId('kemi-fab')).toHaveCount(0)
  await page.getByTestId('kemi-wake').click()
  await expect(page.getByTestId('kemi-fab')).toBeVisible()

  // ------------------------------------------------------------------ 7) a un consignatario no le sale Kemi
  const loc = await c.locations.post(`Tienda ${suffix}`)
  const cons = await createConsigneeAccess(request, c, loc.publicId, `k${suffix}`)
  const other = await page.context().browser()!.newContext({ locale: 'es-PR', viewport: { width: 1280, height: 800 } })
  try {
    const p2 = await other.newPage()
    await loginConfirmingTheComputer(p2, cons.mail, c.password)
    await expect(p2).toHaveURL(/\/notices|\/orders|\/home/, { timeout: 60_000 })
    await p2.waitForTimeout(3_000)
    await expect(p2.getByTestId('kemi-fab')).toHaveCount(0)   // el consignatario no tiene a Kemi
    await expect(p2.getByTestId('kemi-wake')).toHaveCount(0)
  } finally {
    await other.close()
  }
})
