// Kemi en la web del personal (Lote 218) con el API real: preguntarle HABLANDO (el reconocimiento de voz del navegador se simula, un navegador de pruebas no tiene micrófono) además de escribiendo, con el
// cerebro compartido con el portal del cliente. Captura: docs/manual/frontend/img/l218-web-hablando.png.
import { expect, test, type Page } from '@playwright/test'

import { login, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(120_000)

async function fakeSpeech(page: Page) {
  await page.addInitScript(() => {
    class FakeRecognition extends EventTarget {
      lang = ''
      interimResults = false
      continuous = false
      maxAlternatives = 1
      onresult: ((e: unknown) => void) | null = null
      onerror: ((e: unknown) => void) | null = null
      onend: (() => void) | null = null
      start() {
        ;(window as unknown as { __lang: string; __rec: FakeRecognition }).__lang = this.lang
        ;(window as unknown as { __rec: FakeRecognition }).__rec = this
      }
      stop() {
        this.onend?.()
      }
      abort() {
        this.onend?.()
      }
      say(text: string, final: boolean) {
        const alt = { transcript: text, confidence: 0.9 }
        const list = Object.assign([alt], { isFinal: final })
        this.onresult?.({ resultIndex: 0, results: [list] })
        if (final) this.onend?.()
      }
      fail(error: string) {
        this.onerror?.({ error })
        this.onend?.()
      }
    }
    // Chromium trae las dos (la estándar y la de prefijo): se reemplazan las dos para que ninguna deje pasar el reconocimiento de verdad
    ;(window as unknown as { webkitSpeechRecognition: unknown; SpeechRecognition: unknown }).webkitSpeechRecognition = FakeRecognition
    ;(window as unknown as { SpeechRecognition: unknown }).SpeechRecognition = FakeRecognition
  })
}

test('Kemi (web del personal): preguntarle hablando, con micrófono, errores y el mismo cerebro', async ({ page, isMobile }) => {
  await fakeSpeech(page)
  await page.setViewportSize(isMobile ? { width: 360, height: 780 } : { width: 1440, height: 900 })
  await login(page)
  await page.getByRole('button', { name: 'Kemi, asistente de Teikem' }).click()
  const panel = page.getByRole('dialog', { name: 'Kemi' })
  await panel.getByRole('button', { name: 'Pregúntame cómo usar el sistema' }).click()
  const mic = panel.getByTestId('kemi-mic')
  await expect(mic).toBeVisible()
  await mic.click()
  await expect(panel.getByTestId('kemi-listening')).toBeVisible()
  expect(await page.evaluate(() => (window as unknown as { __lang: string }).__lang)).toBe('es-PR')
  await page.evaluate(() => (window as unknown as { __rec: { say(t: string, f: boolean): void } }).__rec.say('cómo cambio mi', false))
  await expect(panel.getByRole('textbox', { name: 'Tu pregunta para Kemi' })).toHaveValue('cómo cambio mi')
  await shot(page, isMobile, 'l218-web-hablando')
  await page.evaluate(() => (window as unknown as { __rec: { say(t: string, f: boolean): void } }).__rec.say('cómo cambio mi contraseña', true))
  await expect(panel.getByText('Esto encontré:')).toBeVisible({ timeout: 30_000 })
  await expect(panel.getByRole('button', { name: /contraseña/i }).first()).toBeVisible()
  // el micrófono sin permiso se explica
  await panel.getByRole('button', { name: 'Cambiar la pregunta' }).click()
  await panel.getByTestId('kemi-mic').click()
  await page.evaluate(() => (window as unknown as { __rec: { fail(e: string): void } }).__rec.fail('not-allowed'))
  await expect(panel.getByTestId('kemi-voice-error')).toContainText('permiso para usar el micrófono')
})
