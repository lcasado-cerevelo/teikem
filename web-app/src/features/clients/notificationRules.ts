// Reglas puras de «Avisos al cliente» (Lote 98): el orden de los avisos, comparar lo editado con lo guardado y revisar los correos y teléfonos antes de enviar. El servidor repite todas las validaciones.
export const NOTIFICATION_KINDS = ['PICKED_UP', 'OUT_FOR_DELIVERY', 'DELIVERED', 'COD_PROCESSED', 'INVOICE_ISSUED'] as const
export type NotificationKind = (typeof NOTIFICATION_KINDS)[number]
export const MAX_NOTIFICATION_LIST = 400
export const MAX_RECIPIENTS = 10

export interface NotificationRow {
  kind: string
  emailEnabled: boolean
  appEnabled: boolean
  smsEnabled: boolean
  extraEmails: string
  extraPhones: string
}

export function rowsFrom(settings: readonly { kind?: string; emailEnabled?: boolean; appEnabled?: boolean; smsEnabled?: boolean; extraEmails?: string | null; extraPhones?: string | null }[] | undefined): NotificationRow[] {
  return NOTIFICATION_KINDS.map((kind) => {
    const s = settings?.find((x) => x.kind === kind)
    return { kind, emailEnabled: s?.emailEnabled ?? false, appEnabled: s?.appEnabled ?? false, smsEnabled: s?.smsEnabled ?? false, extraEmails: s?.extraEmails ?? '', extraPhones: s?.extraPhones ?? '' }
  })
}

const norm = (s: string) => s.split(/[,;\n]+/).map((x) => x.trim()).filter(Boolean).join(', ')

export function isDirty(draft: readonly NotificationRow[], saved: readonly NotificationRow[]): boolean {
  return draft.some((d, i) => {
    const s = saved[i]
    return !s || d.emailEnabled !== s.emailEnabled || d.appEnabled !== s.appEnabled || d.smsEnabled !== s.smsEnabled || norm(d.extraEmails) !== norm(s.extraEmails) || norm(d.extraPhones) !== norm(s.extraPhones)
  })
}

export type RowError = 'email' | 'phone' | 'tooMany' | 'tooLong'

const EMAIL = /^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$/

/** Primer problema de una fila (o null) y el valor que lo causó. */
export function rowError(r: NotificationRow): { error: RowError; value?: string } | null {
  if (r.extraEmails.length > MAX_NOTIFICATION_LIST || r.extraPhones.length > MAX_NOTIFICATION_LIST) return { error: 'tooLong' }
  const emails = norm(r.extraEmails).split(', ').filter(Boolean)
  const bad = emails.find((e) => !EMAIL.test(e) || e.length > 200)
  if (bad) return { error: 'email', value: bad }
  const phones = norm(r.extraPhones).split(', ').filter(Boolean)
  const badPhone = phones.find((p) => {
    const digits = p.replace(/\D/g, '').length
    return p.length > 30 || digits < 7 || digits > 15 || /[^\d+\s\-()]/.test(p)
  })
  if (badPhone) return { error: 'phone', value: badPhone }
  if (emails.length > MAX_RECIPIENTS || phones.length > MAX_RECIPIENTS) return { error: 'tooMany' }
  return null
}

/** Cuántos avisos tienen algún canal encendido. */
export function enabledCount(rows: readonly NotificationRow[]): number {
  return rows.filter((r) => r.emailEnabled || r.appEnabled || r.smsEnabled).length
}
