// Contraseñas (Lote 219) con el API real: el ojito y el aviso de mayúsculas en el login de la web del personal, el mensaje claro cuando la contraseña es mala, y en el PORTAL del cliente:
// el logo arriba, el ojito, el mensaje claro y «¿Olvidó su contraseña?» de punta a punta — pedir el enlace, elegir la contraseña nueva, que el enlace NO baste para quien tiene la verificación en dos
// pasos (pide el código), y que la contraseña vieja ya no entre y la nueva sí.
//
// Datos propios por API. Requiere el API con Portal:ReturnInviteTokenInResponse, Auth:TrustedBrowsers:ReturnLinkInResponse, Auth:PasswordReset:ReturnLinkInResponse y Portal:WebBaseUrl = la app del cliente
// (CLIENT_APP_URL, por omisión http://localhost:8081). Capturas (solo escritorio): docs/manual/frontend/img/l219-<paso>.png.
import { expect, test, type Page } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, loginAsClientAdmin, loginConfirmingTheComputer, shot } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

/** Simula la tecla con las mayúsculas activadas (un navegador de pruebas no tiene el seguro de mayúsculas del teclado). */
async function pressWithCaps(page: Page, selector: string, caps: boolean) {
  await page.locator(selector).first().evaluate((el, on) => {
    el.dispatchEvent(new KeyboardEvent('keydown', { key: 'A', bubbles: true, modifierCapsLock: on }))
  }, caps)
}

test('Login de la web del personal: ojito, aviso de mayúsculas y mensaje claro', async ({ page, isMobile }) => {
  await page.setViewportSize(isMobile ? { width: 360, height: 780 } : { width: 1280, height: 800 })
  await page.goto('/login')
  const pwd = page.locator('#login-password')
  await page.getByLabel('Correo electrónico').fill('nadie@demo.test')
  await pwd.fill('Clave-Equivocada-1')
  // el ojito
  await expect(pwd).toHaveAttribute('type', 'password')
  await page.getByRole('button', { name: 'Mostrar contraseña' }).click()
  await expect(pwd).toHaveAttribute('type', 'text')
  await expect(pwd).toHaveValue('Clave-Equivocada-1')
  await page.getByRole('button', { name: 'Ocultar contraseña' }).click()
  await expect(pwd).toHaveAttribute('type', 'password')
  // el aviso de mayúsculas sale al teclear con ellas activadas y se quita al apagarlas
  await expect(page.getByTestId('caps-warning')).toHaveCount(0)
  await pressWithCaps(page, '#login-password', true)
  await expect(page.getByTestId('caps-warning')).toHaveText('Las mayúsculas están activadas.')
  await shot(page, isMobile, 'l219-web-mayusculas')
  await pressWithCaps(page, '#login-password', false)
  await expect(page.getByTestId('caps-warning')).toHaveCount(0)
  // contraseña mala: el mensaje dice qué revisar
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('alert')).toContainText('no son correctos')
  await expect(page.getByRole('alert')).toContainText('mayúsculas')
  await expect(page.getByRole('alert')).toContainText('ojito')
  await shot(page, isMobile, 'l219-web-error')
  // sigue ahí el «¿Olvidó su contraseña?»
  await expect(page.getByRole('link', { name: '¿Olvidó su contraseña?' })).toBeVisible()
})

test('Portal: logo arriba, ojito, mensaje claro y recuperar la contraseña sin saltarse la verificación en dos pasos', async ({ page, request, isMobile }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil') && !process.env.PORTAL_MOBILE, 'Recorrido largo: escritorio (el móvil se prueba con PORTAL_MOBILE=1).')
  const appUp = await request.get(`${APP_URL}/login`, { timeout: 5_000 }).then((r) => r.ok(), () => false)
  test.skip(!appUp, `La app del cliente no responde en ${APP_URL}`)
  const suffix = `P${String(Date.now()).slice(-6).replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])}`
  const c = await createClientWithAdmin(request, suffix, 'Clave portal')
  const NEW_PASSWORD = `Nueva_${suffix}_Clave-2027!`

  await page.setViewportSize(isMobile ? { width: 390, height: 844 } : { width: 1280, height: 860 })
  await page.goto(`${APP_URL}/login`)

  // ------------------------------------------------------------------ 1) la pantalla de entrada: logo, ojito, contraseña mala con mensaje claro
  await expect(page.getByTestId('login-brand')).toBeVisible({ timeout: 60_000 })
  await page.getByTestId('login-email').fill(c.adminMail)
  const pwd = page.getByTestId('login-password')
  await pwd.fill('Clave-Equivocada-1')
  await expect(pwd).toHaveAttribute('type', 'password')
  await page.getByTestId('login-password-eye').click()
  await expect(pwd).not.toHaveAttribute('type', 'password')   // visible (RN-web no escribe el tipo «text»)
  await expect(pwd).toHaveValue('Clave-Equivocada-1')
  await page.getByTestId('login-password-eye').click()
  await expect(pwd).toHaveAttribute('type', 'password')
  await pwd.dispatchEvent('keydown', { key: 'A', modifierCapsLock: true })
  await expect(page.getByTestId('caps-warning')).toHaveText('Las mayúsculas están activadas.')
  await shot(page, isMobile, 'l219-portal-mayusculas')
  await page.getByTestId('login-submit').click()
  await expect(page.getByText(/no son correctos/)).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l219-portal-error')

  // ------------------------------------------------------------------ 2) «¿Olvidó su contraseña?»: pedir el enlace (la respuesta es la misma exista o no el correo)
  await page.getByTestId('login-forgot').click()
  await expect(page).toHaveURL(/\/forgot-password/)
  await page.getByTestId('forgot-email').fill('nadie-existe@demo.test')
  await page.getByTestId('forgot-submit').click()
  await expect(page.getByText('Si el correo está registrado, le enviamos un enlace para restablecer su contraseña.')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('forgot-devlink')).toHaveCount(0)   // un correo que no existe no recibe enlace
  await page.getByTestId('forgot-back').click()
  await page.getByTestId('login-forgot').click()
  await page.getByTestId('forgot-email').fill(c.adminMail)
  await shot(page, isMobile, 'l219-portal-olvide')
  await page.getByTestId('forgot-submit').click()
  const dev = page.getByTestId('forgot-devlink')
  await expect(dev).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l219-portal-enlace-enviado')

  // ------------------------------------------------------------------ 3) elegir la contraseña nueva: el enlace NO basta si tiene la verificación en dos pasos
  await dev.click()
  await expect(page).toHaveURL(/\/reset-password\?email=.*&token=/)
  await page.getByTestId('reset-password').fill(NEW_PASSWORD)
  await page.getByTestId('reset-confirm').fill(NEW_PASSWORD)
  await page.getByTestId('reset-submit').click()
  await expect(page.getByText('Escriba el código de verificación en dos pasos para cambiar la contraseña.')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('reset-mfa')).toBeVisible()
  await shot(page, isMobile, 'l219-portal-pide-codigo')
  // la contraseña vieja sigue valiendo: nada cambió y el enlace no se gastó
  expect((await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email: c.adminMail, password: c.password, trustedBrowserToken: 'x'.repeat(40) } })).status()).not.toBe(401)
  // un código malo no cambia nada
  await page.getByTestId('reset-mfa').fill('000000')
  await page.getByTestId('reset-submit').click()
  await expect(page.getByText('Código MFA inválido.')).toBeVisible({ timeout: 30_000 })
  // con un código de recuperación, sí
  await page.getByTestId('reset-mfa').fill(c.recovery[0])
  await page.getByTestId('reset-submit').click()
  await expect(page.getByText('Contraseña cambiada')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l219-portal-cambiada')

  // ------------------------------------------------------------------ 4) la vieja ya no entra y la nueva sí (con su verificación en dos pasos y la computadora confirmada de nuevo)
  const old = await request.post(`${API_URL}/api/v1/portal/auth/login`, { data: { email: c.adminMail, password: c.password, trustedBrowserToken: 'x'.repeat(40) } })
  expect(old.status(), 'la contraseña vieja ya no entra').toBe(401)
  await loginConfirmingTheComputer(page, c.adminMail, NEW_PASSWORD)
  await expect(page.getByRole('heading', { name: 'Código de seguridad' })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Código').fill(c.recovery[1])
  await page.getByRole('button', { name: 'Verificar' }).click()
  await expect(page.getByRole('heading', { name: 'Hola, Ana' })).toBeVisible({ timeout: 30_000 })
})
