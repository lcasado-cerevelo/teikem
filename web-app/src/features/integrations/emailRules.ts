// Reglas puras del conector de correo de Integraciones (Lote 133): valores del formulario, validación en cliente (mismos mensajes que el servidor) y el cuerpo del API.
// El cuerpo de un correo se lee con «campos a leer» (etiqueta, expresión regular o bloque de renglones); cada campo va a una columna de la plantilla de importación de la integración.
import type { components } from '../../kernel/api/schema'

type EmailConfig = components['schemas']['EmailConfig']

export const MATCH_OPERATORS = ['CONTAINS', 'EQUALS', 'STARTS', 'ENDS', 'WILDCARD', 'REGEX'] as const
export const ATTACHMENT_MODES = ['ALL', 'FIRST', 'BY_NAME'] as const
export const BODY_KINDS = ['LABEL', 'REGEX', 'BLOCK'] as const
export const TRANSFORMS = ['NONE', 'UPPER', 'DIGITS', 'MONEY', 'SPACES'] as const

export interface BodyFieldValues {
  position: string
  kind: string
  label: string
  pattern: string
  required: boolean
  transform: string
  start: string
  end: string
  separator: string
  /** Solo bloques: «6=1, 7=2» (posición de la plantilla = número de columna del renglón, o número de grupo si hay expresión por línea). */
  columns: string
  /** Solo bloques (2026-10-22): expresión regular por línea; cada línea que coincide es una fila y sus grupos (1, 2, 3…) van a las columnas. Las demás líneas se ignoran. */
  lineRegex: string
  /** Solo bloques: el bloque se lee cada vez que aparece (el encabezado se repite en cada página). */
  repeat: boolean
  /** Lectores por registro: el campo se lee una vez del encabezado y vale para todos los registros. */
  fromHeader: boolean
  /** Si la posición ya trae un valor, este se agrega a continuación con este texto en medio (varias partes en una nota). */
  appendWith: string
}

/** Una condición de un lector (2026-10-22): sobre qué (nombre del adjunto, asunto o texto del documento) y cómo se compara. */
export interface ConditionValues {
  scope: string
  operator: string
  value: string
}

/** Un lector: qué hace la integración con cada documento que llega (cuerpo, PDF o archivo de datos) según sus condiciones, y a qué destino va. */
export interface ReaderValues {
  name: string
  source: string
  target: string
  conditions: ConditionValues[]
  /** Cuerpo y PDF: los campos a leer. */
  fields: BodyFieldValues[]
  /** Vacío = la plantilla de la integración. */
  templatePublicId: string
  /** Vacío = el cliente de la integración. */
  clientPublicId: string
  firstOnly: boolean
  /** Documento con varias órdenes: cada línea que contiene este texto empieza un registro (vacío = un solo registro). */
  recordStart: string
  /** Solo en un lector de cierre (ORDER_COMPLETION): SUGGEST deja una propuesta por confirmar; AUTO la aplica en el acto. */
  mode: string
  /** Solo en un lector de cierre: a qué estatus lleva la orden (PICKUP o DELIVERED). */
  toStatus: string
  /** Solo en un lector de avisos de llegada o de salida: el almacén del aviso (vacío = el único activo de la compañía). */
  warehousePublicId: string
  /** Confianza mínima del OCR (0–100) de los números del documento; por debajo, la lectura es dudosa (vacío = 80; 0 = no se vigila). */
  minConfidence: string
  /** Chequeos de completitud contra los totales que declara el documento: si no cuadran, la lectura es dudosa. */
  checks: ReadCheckValues[]
}

export interface ReadCheckValues {
  kind: string
  /** Expresión que encuentra el total declarado en el texto del documento (grupo 1 si lo hay). */
  pattern: string
  position: string
  distinct: boolean
  tolerance: string
  name: string
  /** RECORD (cada registro), HEADER (lo anterior al primer registro, contra todas las filas) o DOCUMENT (todo el texto contra todas las filas). */
  scope: string
}

export const CHECK_KINDS = ['LINE_COUNT', 'SUM', 'MATCH_COUNT'] as const
export const CHECK_SCOPES = ['RECORD', 'HEADER', 'DOCUMENT'] as const
export const emptyCheck = (): ReadCheckValues => ({ kind: 'SUM', pattern: '', position: '1', distinct: false, tolerance: '', name: '', scope: 'RECORD' })

export const READER_SOURCES = ['BODY', 'PDF', 'FILE'] as const
export const READER_TARGETS = ['ORDER', 'ARRIVAL_NOTICE', 'EXIT_NOTICE', 'ORDER_COMPLETION'] as const
export const READER_MODES = ['SUGGEST', 'AUTO'] as const
export const COMPLETION_TARGETS = ['PICKUP', 'DELIVERED'] as const
export const CONDITION_SCOPES = ['ATTACHMENT_NAME', 'SUBJECT', 'TEXT'] as const

/** La condición se evalúa sobre algo que ese tipo de documento tiene: un cuerpo no tiene nombre de adjunto y un archivo de datos no tiene texto. */
export function scopeApplies(scope: string, source: string): boolean {
  if (scope === 'ATTACHMENT_NAME') return source !== 'BODY'
  if (scope === 'TEXT') return source !== 'FILE'
  return scope === 'SUBJECT'
}

export const emptyCondition = (source = 'PDF'): ConditionValues => ({ scope: source === 'BODY' ? 'SUBJECT' : 'TEXT', operator: 'CONTAINS', value: '' })
export const emptyReader = (): ReaderValues => ({ name: '', source: 'BODY', target: 'ORDER', conditions: [], fields: [], templatePublicId: '', clientPublicId: '', firstOnly: false, recordStart: '', mode: 'SUGGEST', toStatus: 'PICKUP', warehousePublicId: '', minConfidence: '', checks: [] })

export const EMAIL_PROVIDERS = ['IMAP', 'MICROSOFT365'] as const
export const MARK_MODES = ['MAILBOX', 'LOG'] as const

export interface EmailValues {
  /** 2026-10-24: IMAP (servidor, usuario y contraseña) o MICROSOFT365 (aplicación de Entra ID; el «usuario» es la dirección del buzón y la «contraseña» es el secreto de la aplicación). */
  provider: string
  /** 2026-10-24: dónde se anota que un correo ya se leyó: LOG = en el log de Teikem (el buzón no se toca; por omisión) o MAILBOX = marcar leído y mover a «Procesados». */
  markMode: string
  /** AAAA-MM-DD: solo se leen los correos recibidos desde ese día (modo LOG); vacío = todos los de la carpeta. */
  readSince: string
  tenantId: string
  clientId: string
  host: string
  port: string
  useSsl: boolean
  username: string
  /** Vacío al editar = se conserva la guardada. */
  password: string
  mailbox: string
  processedFolder: string
  subjectOperator: string
  subjectValue: string
  /** Un remitente (correo o dominio) por línea o separados por coma. */
  allowedSenders: string
  requireAttachment: boolean
  attachmentMode: string
  attachmentNameOperator: string
  attachmentNameValue: string
  /** Extensiones permitidas, separadas por coma (csv, txt). */
  attachmentTypes: string
  readBody: boolean
  /** 2026-10-22: los PDF adjuntos se leen (con OCR si son escaneados) con los mismos campos a leer del cuerpo. */
  readPdf: boolean
  bodyFields: BodyFieldValues[]
  /** Lectores (2026-10-22): si hay alguno, mandan sobre los ajustes de adjuntos, cuerpo y PDF de arriba. */
  readers: ReaderValues[]
}

export interface EmailMessages {
  tenantId: string
  clientId: string
  secret: string
  host: string
  port: string
  username: string
  password: string
  mailbox: string
  noFields: string
  position: string
  label: string
  pattern: string
  block: string
  blockColumns: string
  regex: string
  repeatStart: string
  readerName: string
  readerNameDuplicate: string
  readerFields: string
  conditionValue: string
  conditionScope: string
  nameFilter: string
  senders: string
}

export const emptyBodyField = (): BodyFieldValues => ({ position: '', kind: 'LABEL', label: '', pattern: '', required: false, transform: 'NONE', start: '', end: '', separator: ' ', columns: '', lineRegex: '', repeat: false, fromHeader: false, appendWith: '' })

export function emptyEmail(): EmailValues {
  return {
    provider: 'IMAP', markMode: 'MAILBOX', readSince: '', tenantId: '', clientId: '', host: '', port: '993', useSsl: true, username: '', password: '', mailbox: 'INBOX', processedFolder: 'Procesados', subjectOperator: 'CONTAINS', subjectValue: '', allowedSenders: '', requireAttachment: false,
    attachmentMode: 'ALL', attachmentNameOperator: 'WILDCARD', attachmentNameValue: '', attachmentTypes: 'csv, txt', readBody: false, readPdf: false, bodyFields: [], readers: [],
  }
}

/** «6=1, 7=2, 3=4/3» → [{position: 6, index: 1}, …, {position: 3, index: 4, divide: 3}] (el último = grupo 4 ÷ grupo 3); null si algo no es «número=número» o «número=número/número». */
export function parseBlockColumns(text: string): { position: number; index: number; divide?: number }[] | null {
  const parts = text.split(/[,;]/).map((p) => p.trim()).filter(Boolean)
  if (parts.length === 0) return null
  const out: { position: number; index: number; divide?: number }[] = []
  for (const p of parts) {
    const m = /^(\d+)\s*=\s*(\d+)(?:\s*\/\s*(\d+))?$/.exec(p)
    if (!m) return null
    out.push(m[3] ? { position: Number(m[1]), index: Number(m[2]), divide: Number(m[3]) } : { position: Number(m[1]), index: Number(m[2]) })
  }
  return out
}

export function isValidRegex(pattern: string): boolean {
  try {
    new RegExp(pattern)
    return true
  } catch {
    return false
  }
}

export const parseSenders = (text: string) => [...new Set(text.split(/[\n,;]/).map((s) => s.trim().toLowerCase()).filter(Boolean))]
export const parseTypes = (text: string) => [...new Set(text.split(/[\n,;]/).map((s) => s.trim().toLowerCase().replace(/^\./, '')).filter(Boolean))]

export interface EmailIssue {
  path: string
  message: string
}

/** Errores del formulario de correo (ruta relativa a `email.`): lo mínimo que el servidor también exige. */
/** Errores de una lista de campos a leer (la de siempre o la de un lector); `base` es la ruta de la lista. */
function bodyFieldIssues(fields: BodyFieldValues[], base: string, m: EmailMessages): EmailIssue[] {
  const issues: EmailIssue[] = []
  fields.forEach((f, i) => {
    const pos = Number(f.position)
    if (!Number.isInteger(pos) || pos < 1 || pos > 500) issues.push({ path: `${base}.${i}.position`, message: m.position })
    if (f.kind === 'LABEL' && !f.label.trim()) issues.push({ path: `${base}.${i}.label`, message: m.label })
    if (f.kind === 'REGEX' && !f.pattern.trim()) issues.push({ path: `${base}.${i}.pattern`, message: m.pattern })
    if (f.kind === 'BLOCK') {
      if (f.lineRegex.trim()) {
        if (!isValidRegex(f.lineRegex.trim())) issues.push({ path: `${base}.${i}.lineRegex`, message: m.regex })
      } else if (!f.start.trim() || !f.end.trim()) issues.push({ path: `${base}.${i}.start`, message: m.block })
      if (f.repeat && !f.start.trim()) issues.push({ path: `${base}.${i}.start`, message: m.repeatStart })
      if (!parseBlockColumns(f.columns)) issues.push({ path: `${base}.${i}.columns`, message: m.blockColumns })
    }
  })
  return issues
}

export function emailIssues(e: EmailValues, hasSavedPassword: boolean, m: EmailMessages): EmailIssue[] {
  const issues: EmailIssue[] = []
  const m365 = e.provider === 'MICROSOFT365'
  if (m365) {
    if (!e.tenantId.trim()) issues.push({ path: 'tenantId', message: m.tenantId })
    if (!e.clientId.trim()) issues.push({ path: 'clientId', message: m.clientId })
  } else {
    if (!e.host.trim()) issues.push({ path: 'host', message: m.host })
    const port = Number(e.port)
    if (!Number.isInteger(port) || port < 1 || port > 65535) issues.push({ path: 'port', message: m.port })
  }
  if (!e.username.trim()) issues.push({ path: 'username', message: m.username })
  if (!hasSavedPassword && !e.password) issues.push({ path: 'password', message: m365 ? m.secret : m.password })
  if (!e.mailbox.trim()) issues.push({ path: 'mailbox', message: m.mailbox })
  if (e.attachmentMode === 'BY_NAME' && !e.attachmentNameValue.trim()) issues.push({ path: 'attachmentNameValue', message: m.nameFilter })
  if (parseSenders(e.allowedSenders).some((s) => !/^(@?[a-z0-9.-]+\.[a-z]{2,}|[^@\s]+@[a-z0-9.-]+\.[a-z]{2,})$/.test(s))) issues.push({ path: 'allowedSenders', message: m.senders })
  if ((e.readBody || e.readPdf) && e.bodyFields.length === 0) issues.push({ path: 'bodyFields', message: m.noFields })
  issues.push(...bodyFieldIssues(e.bodyFields, 'bodyFields', m))
  const seen = new Set<string>()
  e.readers.forEach((r, i) => {
    const key = r.name.trim().toLowerCase()
    if (!key) issues.push({ path: `readers.${i}.name`, message: m.readerName })
    else if (seen.has(key)) issues.push({ path: `readers.${i}.name`, message: m.readerNameDuplicate })
    seen.add(key)
    r.conditions.forEach((c, j) => {
      if (!scopeApplies(c.scope, r.source)) issues.push({ path: `readers.${i}.conditions.${j}.scope`, message: m.conditionScope })
      if (!c.value.trim()) issues.push({ path: `readers.${i}.conditions.${j}.value`, message: m.conditionValue })
    })
    if (r.source !== 'FILE' && r.fields.length === 0) issues.push({ path: `readers.${i}.fields`, message: m.readerFields })
    issues.push(...bodyFieldIssues(r.fields, `readers.${i}.fields`, m))
  })
  return issues
}

type ApiBodyField = NonNullable<NonNullable<EmailConfig['bodyFields']>[number]>

function fieldValuesOf(f: ApiBodyField): BodyFieldValues {
  return {
    position: String(f.position ?? ''),
    kind: f.kind ?? 'LABEL',
    label: f.label ?? '',
    pattern: f.pattern ?? '',
    required: f.required ?? false,
    transform: f.transform ?? 'NONE',
    start: f.start ?? '',
    end: f.end ?? '',
    separator: f.separator ?? ' ',
    columns: (f.columns ?? []).map((c) => `${c.position}=${c.index}${c.divide ? `/${c.divide}` : ''}`).join(', '),
    lineRegex: f.lineRegex ?? '',
    repeat: f.repeat ?? false,
    fromHeader: f.fromHeader ?? false,
    appendWith: f.appendWith ?? '',
  }
}

/** Un campo del formulario → el del API. */
function fieldBodyOf(f: BodyFieldValues): ApiBodyField {
  return {
    position: Number(f.position),
    kind: f.kind,
    label: f.kind === 'LABEL' ? f.label.trim() : null,
    pattern: f.kind === 'REGEX' ? f.pattern.trim() : null,
    required: f.required,
    transform: f.transform,
    start: f.kind === 'BLOCK' ? f.start.trim() : null,
    end: f.kind === 'BLOCK' ? f.end.trim() : null,
    separator: f.kind === 'BLOCK' ? f.separator : null,
    columns: f.kind === 'BLOCK' ? parseBlockColumns(f.columns) : null,
    lineRegex: f.kind === 'BLOCK' && f.lineRegex.trim() ? f.lineRegex.trim() : null,
    repeat: f.kind === 'BLOCK' && f.repeat,
    fromHeader: f.fromHeader,
    appendWith: f.appendWith === '' ? null : f.appendWith,
  }
}

export function emailValuesOf(c: EmailConfig | null | undefined): EmailValues {
  if (!c) return emptyEmail()
  return {
    provider: c.provider === 'MICROSOFT365' ? 'MICROSOFT365' : 'IMAP',
    markMode: c.markMode === 'LOG' ? 'LOG' : 'MAILBOX',
    readSince: c.readSince ?? '',
    tenantId: c.tenantId ?? '',
    clientId: c.clientId ?? '',
    host: c.host ?? '',
    port: String(c.port ?? 993),
    useSsl: c.useSsl ?? true,
    username: c.username ?? '',
    password: '',
    mailbox: c.mailbox ?? 'INBOX',
    processedFolder: c.processedFolder ?? '',
    subjectOperator: c.subject?.operator ?? 'CONTAINS',
    subjectValue: c.subject?.value ?? '',
    allowedSenders: (c.allowedSenders ?? []).join(', '),
    requireAttachment: c.requireAttachment ?? false,
    attachmentMode: c.attachmentMode ?? 'ALL',
    attachmentNameOperator: c.attachmentName?.operator ?? 'WILDCARD',
    attachmentNameValue: c.attachmentName?.value ?? '',
    attachmentTypes: (c.attachmentTypes ?? ['csv', 'txt']).map((t) => t.replace(/^\./, '')).join(', '),
    readBody: c.readBody ?? false,
    readPdf: c.readPdf ?? false,
    readers: (c.readers ?? []).map((r) => ({
      name: r.name ?? '',
      source: r.source ?? 'BODY',
      target: r.target ?? 'ORDER',
      conditions: (r.when ?? []).map((w) => ({ scope: w.scope ?? 'SUBJECT', operator: w.operator ?? 'CONTAINS', value: w.value ?? '' })),
      fields: (r.fields ?? []).map(fieldValuesOf),
      templatePublicId: r.templatePublicId ?? '',
      clientPublicId: r.clientPublicId ?? '',
      firstOnly: r.firstOnly ?? false,
      recordStart: r.recordStart ?? '',
      mode: r.mode ?? 'SUGGEST',
      toStatus: r.toStatus ?? 'PICKUP',
      warehousePublicId: r.warehousePublicId ?? '',
      minConfidence: r.minConfidence === null || r.minConfidence === undefined ? '' : String(r.minConfidence),
      checks: (r.checks ?? []).map((c) => ({ kind: c.kind ?? 'SUM', pattern: c.pattern ?? '', position: String(c.position ?? 1), distinct: c.distinct ?? false, tolerance: c.tolerance === null || c.tolerance === undefined ? '' : String(c.tolerance), name: c.name ?? '', scope: c.scope ?? 'RECORD' })),
    })),
    bodyFields: (c.bodyFields ?? []).map(fieldValuesOf),
  }
}

/** El cuerpo del API (sin la contraseña, que va aparte). */
export function buildEmailConfig(e: EmailValues): EmailConfig {
  const m365 = e.provider === 'MICROSOFT365'
  return {
    provider: e.provider,
    markMode: e.markMode,
    readSince: e.readSince.trim() !== '' ? e.readSince.trim() : null,
    tenantId: m365 ? e.tenantId.trim() : null,
    clientId: m365 ? e.clientId.trim() : null,
    host: m365 ? '' : e.host.trim(),
    port: m365 ? 993 : Number(e.port),
    useSsl: e.useSsl,
    username: e.username.trim(),
    mailbox: e.mailbox.trim(),
    processedFolder: e.processedFolder.trim(),
    subject: e.subjectValue.trim() ? { operator: e.subjectOperator, value: e.subjectValue.trim() } : null,
    allowedSenders: parseSenders(e.allowedSenders),
    requireAttachment: e.requireAttachment,
    attachmentMode: e.attachmentMode,
    attachmentName: e.attachmentMode === 'BY_NAME' ? { operator: e.attachmentNameOperator, value: e.attachmentNameValue.trim() } : null,
    attachmentTypes: parseTypes(e.attachmentTypes),
    readBody: e.readBody,
    readPdf: e.readPdf,
    bodyFields: e.readBody || e.readPdf ? e.bodyFields.map(fieldBodyOf) : [],
    readers: e.readers.map((r) => ({
      name: r.name.trim(),
      source: r.source,
      target: r.target,
      when: r.conditions.map((c) => ({ scope: c.scope, operator: c.operator, value: c.value.trim() })),
      fields: r.source === 'FILE' ? [] : r.fields.map(fieldBodyOf),
      templatePublicId: r.templatePublicId || null,
      clientPublicId: r.clientPublicId || null,
      firstOnly: r.firstOnly,
      recordStart: r.recordStart.trim() === '' ? null : r.recordStart.trim(),
      mode: r.target === 'ORDER_COMPLETION' ? r.mode : 'SUGGEST',
      toStatus: r.target === 'ORDER_COMPLETION' ? r.toStatus : null,
      warehousePublicId: (r.target === 'ARRIVAL_NOTICE' || r.target === 'EXIT_NOTICE') && r.warehousePublicId ? r.warehousePublicId : null,
      minConfidence: r.source === 'PDF' && r.minConfidence.trim() !== '' ? Number(r.minConfidence) : null,
      checks: r.checks.length === 0 ? null : r.checks.map((c) => ({ kind: c.kind, pattern: c.pattern.trim(), position: Number(c.position) || 1, distinct: c.kind !== 'SUM' && c.distinct, tolerance: c.tolerance.trim() === '' ? null : Number(c.tolerance), name: c.name.trim() === '' ? null : c.name.trim(), scope: c.scope === 'RECORD' ? null : c.scope })),
    })),
  }
}

/**
 * Lo que la integración hace hoy con los ajustes de adjuntos, cuerpo y PDF, escrito como lectores (2026-10-22): un lector de archivos de datos, uno del cuerpo y uno de PDF.
 * Sirve para pasar de los ajustes de siempre a los lectores sin perder nada y seguir desde ahí (agregar condiciones, otro destino, otro cliente).
 */
export function legacyToReaders(e: EmailValues): ReaderValues[] {
  const readers: ReaderValues[] = []
  const byName: ConditionValues[] = e.attachmentMode === 'BY_NAME' && e.attachmentNameValue.trim() ? [{ scope: 'ATTACHMENT_NAME', operator: e.attachmentNameOperator, value: e.attachmentNameValue.trim() }] : []
  const types = parseTypes(e.attachmentTypes).filter((t) => t !== 'pdf')
  if (types.length > 0) {
    readers.push({ ...emptyReader(), name: 'Archivos de datos', source: 'FILE', conditions: [{ scope: 'ATTACHMENT_NAME', operator: 'REGEX', value: `\\.(${types.join('|')})$` }, ...byName], firstOnly: e.attachmentMode === 'FIRST' })
  }
  if (e.readBody) readers.push({ ...emptyReader(), name: 'Cuerpo del correo', source: 'BODY', fields: e.bodyFields.map((f) => ({ ...f })) })
  if (e.readPdf) readers.push({ ...emptyReader(), name: 'PDF adjuntos', source: 'PDF', conditions: [{ scope: 'ATTACHMENT_NAME', operator: 'REGEX', value: '\\.pdf$' }, ...byName], fields: e.bodyFields.map((f) => ({ ...f })) })
  return readers
}
