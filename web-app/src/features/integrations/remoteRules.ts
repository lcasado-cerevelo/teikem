// Reglas puras de los conectores SFTP y FTP de Integraciones (Lote 134): valores del formulario, validación en cliente (mismos mensajes que el servidor) y el cuerpo del API.
// La contraseña o la llave privada nunca vuelven del servidor: al editar, vacío = se conserva la guardada.
import type { components } from '../../kernel/api/schema'

type RemoteConfig = components['schemas']['RemoteConfig']
type RemoteSecret = components['schemas']['RemoteSecret']

export const AUTH_KINDS = ['PASSWORD', 'KEY'] as const

export interface RemoteValues {
  host: string
  port: string
  username: string
  authKind: string
  /** Contraseña (o frase de la llave): vacía al editar = se conserva. */
  password: string
  /** Llave privada (solo SFTP): el texto PEM completo. Vacía al editar = se conserva. */
  privateKey: string
  dir: string
  processedDir: string
  errorDir: string
  /** SFTP: huella SHA-256 del servidor, fijada. */
  hostFingerprint: string
  /** FTP: cifrado TLS explícito. */
  useFtps: boolean
  fileNameOperator: string
  fileNameValue: string
  /** Extensiones permitidas, separadas por coma. */
  fileTypes: string
}

export interface RemoteMessages {
  host: string
  port: string
  username: string
  secretPassword: string
  secretKey: string
  dir: string
  dirInvalid: string
  fingerprint: string
  keyOnlySftp: string
  fileName: string
}

export const emptyRemote = (kind: string): RemoteValues => ({
  host: '', port: kind === 'FTP' ? '21' : '22', username: '', authKind: 'PASSWORD', password: '', privateKey: '', dir: '/', processedDir: 'procesados', errorDir: 'errores', hostFingerprint: '', useFtps: true,
  fileNameOperator: 'WILDCARD', fileNameValue: '', fileTypes: 'csv, txt',
})

export const normalizeFingerprint = (f: string): string | null => {
  let v = f.trim()
  if (!v) return null
  if (/^sha256:/i.test(v)) v = v.slice(7)
  v = v.replace(/=+$/, '')
  return /^[A-Za-z0-9+/]{43}$/.test(v) ? v : null
}

export interface RemoteIssue {
  path: string
  message: string
}

/** `kind` = 'SFTP' | 'FTP'; `hasSavedSecret` = ya hay contraseña o llave guardada (al editar). */
export function remoteIssues(r: RemoteValues, kind: string, hasSavedSecret: boolean, m: RemoteMessages): RemoteIssue[] {
  const issues: RemoteIssue[] = []
  if (!r.host.trim()) issues.push({ path: 'host', message: m.host })
  const port = Number(r.port)
  if (!Number.isInteger(port) || port < 1 || port > 65535) issues.push({ path: 'port', message: m.port })
  if (!r.username.trim()) issues.push({ path: 'username', message: m.username })
  if (r.authKind === 'KEY' && kind !== 'SFTP') issues.push({ path: 'authKind', message: m.keyOnlySftp })
  if (!hasSavedSecret) {
    if (r.authKind === 'KEY' && !r.privateKey.trim()) issues.push({ path: 'privateKey', message: m.secretKey })
    if (r.authKind !== 'KEY' && !r.password) issues.push({ path: 'password', message: m.secretPassword })
  }
  if (!r.dir.trim()) issues.push({ path: 'dir', message: m.dir })
  for (const [path, value] of [['dir', r.dir], ['processedDir', r.processedDir], ['errorDir', r.errorDir]] as const)
    if (value.includes('..') || /[\r\n]/.test(value)) issues.push({ path, message: m.dirInvalid })
  if (r.hostFingerprint.trim() && !normalizeFingerprint(r.hostFingerprint)) issues.push({ path: 'hostFingerprint', message: m.fingerprint })
  return issues
}

export function remoteValuesOf(c: RemoteConfig | null | undefined, kind: string): RemoteValues {
  if (!c) return emptyRemote(kind)
  return {
    host: c.host ?? '',
    port: String(c.port ?? (kind === 'FTP' ? 21 : 22)),
    username: c.username ?? '',
    authKind: c.authKind ?? 'PASSWORD',
    password: '',
    privateKey: '',
    dir: c.dir ?? '/',
    processedDir: c.processedDir ?? '',
    errorDir: c.errorDir ?? '',
    hostFingerprint: c.hostFingerprint ?? '',
    useFtps: c.useFtps ?? true,
    fileNameOperator: c.fileName?.operator ?? 'WILDCARD',
    fileNameValue: c.fileName?.value ?? '',
    fileTypes: (c.fileTypes ?? ['csv', 'txt']).map((t) => t.replace(/^\./, '')).join(', '),
  }
}

export const parseFileTypes = (text: string) => [...new Set(text.split(/[\n,;]/).map((s) => s.trim().toLowerCase().replace(/^\./, '')).filter(Boolean))]

/** La configuración para el API (sin secretos, que van aparte). */
export function buildRemoteConfig(r: RemoteValues, kind: string): RemoteConfig {
  return {
    host: r.host.trim(),
    port: Number(r.port),
    username: r.username.trim(),
    dir: r.dir.trim(),
    processedDir: r.processedDir.trim(),
    errorDir: r.errorDir.trim(),
    authKind: kind === 'SFTP' ? r.authKind : 'PASSWORD',
    hostFingerprint: kind === 'SFTP' && r.hostFingerprint.trim() ? r.hostFingerprint.trim() : null,
    useFtps: kind === 'FTP' ? r.useFtps : false,
    fileName: r.fileNameValue.trim() ? { operator: r.fileNameOperator, value: r.fileNameValue.trim() } : null,
    fileTypes: parseFileTypes(r.fileTypes),
  }
}

/** El secreto para el API: null si no se escribió nada (al editar, se conserva el guardado). */
export function buildRemoteSecret(r: RemoteValues, kind: string): RemoteSecret | null {
  if (kind === 'SFTP' && r.authKind === 'KEY') return r.privateKey.trim() ? { privateKey: r.privateKey, passphrase: r.password || null, password: null } : null
  return r.password ? { password: r.password, privateKey: null, passphrase: null } : null
}
