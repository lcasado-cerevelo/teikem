// Antes de correr cualquier recorrido: se niega a seguir si el API contra el que van a correr tiene el correo REAL configurado (Brevo).
// Los recorridos crean usuarios y mandan invitaciones, avisos y confirmaciones de computadora; contra Brevo real una sola corrida se come el tope diario
// de la cuenta (300 correos al día en el plan gratis) y deja a la compañía sin correos de verdad durante el resto del día (pasó el 2026-10-24).
// Para correr contra un API con correo real a propósito: E2E_ALLOW_REAL_EMAIL=1. Para levantar el API sin correo: scripts/api-para-pruebas.ps1.
// Solo se detiene cuando SABE que el correo está configurado: si el API no contesta o no hay clientes, no estorba (los recorridos fallarán por su cuenta).
import { request } from '@playwright/test'

type Login = { status?: string; tenants?: { tenantId?: number; isDefault?: boolean }[]; tokens?: { accessToken?: string } }

export default async function globalSetup() {
  if (process.env.E2E_ALLOW_REAL_EMAIL === '1') return
  const api = process.env.API_URL ?? 'http://localhost:5000'
  const email = process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com'
  const password = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
  const ctx = await request.newContext()
  let configured = false
  try {
    let res = await ctx.post(`${api}/api/v1/auth/login`, { data: { email, password } })
    if (!res.ok()) return
    let body = (await res.json()) as Login
    if (body.status === 'tenant_selection') {
      const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
      res = await ctx.post(`${api}/api/v1/auth/login`, { data: { email, password, tenantId: tenant?.tenantId } })
      if (!res.ok()) return
      body = (await res.json()) as Login
    }
    const token = body.tokens?.accessToken
    if (!token) return
    const headers = { Authorization: `Bearer ${token}` }
    const clients = await ctx.get(`${api}/api/v1/clients?take=1`, { headers })
    if (!clients.ok()) return
    const first = ((await clients.json()) as { publicId?: string }[])[0]
    if (!first?.publicId) return
    const settings = await ctx.get(`${api}/api/v1/clients/${first.publicId}/notifications/settings`, { headers })
    if (!settings.ok()) return
    configured = ((await settings.json()) as { emailConfigured?: boolean }).emailConfigured === true
  } catch {
    return
  } finally {
    await ctx.dispose()
  }
  if (configured) {
    throw new Error(
      `El API en ${api} tiene el correo REAL configurado (Brevo): los recorridos mandarían cientos de correos de verdad y se comerían el tope diario de la cuenta.\n` +
        `  · Levante el API sin correo:  powershell -ExecutionPolicy Bypass -File scripts\\api-para-pruebas.ps1\n` +
        `  · O, si quiere correo real a propósito:  E2E_ALLOW_REAL_EMAIL=1`,
    )
  }
}
