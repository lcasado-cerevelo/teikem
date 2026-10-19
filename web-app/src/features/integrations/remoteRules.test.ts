// Lote 134 — reglas puras del formulario de SFTP/FTP: validación con los mismos mensajes del servidor, huella, secretos que no se mandan si no se escribieron y el cuerpo del API.
import { describe, expect, it } from 'vitest'
import { buildCreateRequest, buildPatchRequest, integrationSchema, type IntegrationValues } from './integrationRules'
import { emptyEmail } from './emailRules'
import { buildRemoteConfig, buildRemoteSecret, emptyRemote, normalizeFingerprint, parseFileTypes, remoteIssues, remoteValuesOf, type RemoteMessages, type RemoteValues } from './remoteRules'

const FP = 'nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8'
const M: RemoteMessages = {
  host: 'host', port: 'puerto', username: 'usuario', secretPassword: 'contraseña', secretKey: 'llave', dir: 'carpeta', dirInvalid: 'carpeta inválida', fingerprint: 'huella', keyOnlySftp: 'solo sftp', fileName: 'nombre',
}
const ok = (): RemoteValues => ({ ...emptyRemote('SFTP'), host: ' sftp.x.com ', username: ' teikem ', password: 'secreto', dir: '/entrada' })
const issues = (r: Partial<RemoteValues>, kind = 'SFTP', saved = false) => remoteIssues({ ...ok(), ...r }, kind, saved, M).map((i) => `${i.path}:${i.message}`)

describe('remoteIssues', () => {
  it('acepta lo mínimo y pide servidor, puerto, usuario, secreto (solo si no hay guardado) y carpeta', () => {
    expect(issues({})).toEqual([])
    expect(issues({ host: '', port: '0', username: '', password: '', dir: ' ' })).toEqual(['host:host', 'port:puerto', 'username:usuario', 'password:contraseña', 'dir:carpeta'])
    expect(issues({ password: '' }, 'SFTP', true)).toEqual([])
  })

  it('la llave privada pide el texto de la llave y solo se usa con SFTP', () => {
    expect(issues({ authKind: 'KEY', password: '' })).toEqual(['privateKey:llave'])
    expect(issues({ authKind: 'KEY', privateKey: '-----BEGIN-----', password: '' })).toEqual([])
    expect(issues({ authKind: 'KEY', privateKey: 'x' }, 'FTP')).toEqual(['authKind:solo sftp'])
  })

  it('las carpetas no admiten «..» y la huella debe ser un SHA-256', () => {
    expect(issues({ dir: '../fuera', processedDir: 'a\nb' })).toEqual(['dir:carpeta inválida', 'processedDir:carpeta inválida'])
    expect(issues({ hostFingerprint: 'SHA256:' + FP })).toEqual([])
    expect(issues({ hostFingerprint: 'no-es-huella' })).toEqual(['hostFingerprint:huella'])
  })
})

describe('huella y tipos', () => {
  it('normaliza la huella (con o sin prefijo y relleno)', () => {
    expect(normalizeFingerprint('SHA256:' + FP)).toBe(FP)
    expect(normalizeFingerprint('sha256:' + FP + '=')).toBe(FP)
    expect(normalizeFingerprint(' ' + FP + ' ')).toBe(FP)
    expect(normalizeFingerprint('abc')).toBeNull()
    expect(normalizeFingerprint('')).toBeNull()
  })

  it('los tipos de archivo se normalizan sin repetidos', () => {
    expect(parseFileTypes('.CSV, txt; csv')).toEqual(['csv', 'txt'])
  })
})

describe('cuerpo del API', () => {
  it('arma la configuración sin secretos: SFTP con huella, FTP con FTPS y el filtro de nombre', () => {
    const sftp = buildRemoteConfig({ ...ok(), hostFingerprint: ' SHA256:' + FP + ' ', fileNameValue: ' ordenes-* ' }, 'SFTP')
    expect(sftp).toMatchObject({ host: 'sftp.x.com', port: 22, username: 'teikem', dir: '/entrada', authKind: 'PASSWORD', hostFingerprint: 'SHA256:' + FP, useFtps: false, fileName: { operator: 'WILDCARD', value: 'ordenes-*' }, fileTypes: ['csv', 'txt'] })
    expect(sftp).not.toHaveProperty('password')
    const ftp = buildRemoteConfig({ ...ok(), port: '21', hostFingerprint: FP, authKind: 'KEY' }, 'FTP')
    expect(ftp).toMatchObject({ port: 21, authKind: 'PASSWORD', hostFingerprint: null, useFtps: true, fileName: null })
  })

  it('el secreto es la contraseña o la llave con su frase; vacío = null (se conserva el guardado)', () => {
    expect(buildRemoteSecret(ok(), 'SFTP')).toEqual({ password: 'secreto', privateKey: null, passphrase: null })
    expect(buildRemoteSecret({ ...ok(), password: '' }, 'SFTP')).toBeNull()
    expect(buildRemoteSecret({ ...ok(), authKind: 'KEY', privateKey: 'PEM', password: 'frase' }, 'SFTP')).toEqual({ privateKey: 'PEM', passphrase: 'frase', password: null })
    expect(buildRemoteSecret({ ...ok(), authKind: 'KEY', privateKey: ' ' }, 'SFTP')).toBeNull()
    expect(buildRemoteSecret({ ...ok(), authKind: 'KEY', privateKey: 'PEM' }, 'FTP')).toEqual({ password: 'secreto', privateKey: null, passphrase: null })
  })

  it('al editar parte de lo guardado (los secretos vuelven vacíos)', () => {
    const back = remoteValuesOf(buildRemoteConfig({ ...ok(), hostFingerprint: FP }, 'SFTP'), 'SFTP')
    expect(back).toMatchObject({ host: 'sftp.x.com', password: '', privateKey: '', hostFingerprint: FP, fileTypes: 'csv, txt' })
    expect(remoteValuesOf(null, 'FTP')).toMatchObject({ port: '21', useFtps: true })
  })
})

describe('formulario de integración con SFTP/FTP', () => {
  const base: IntegrationValues = {
    name: 'AxisCare', description: '', clientPublicId: 'c1', templatePublicId: 't1', connectorKind: 'SFTP', folder: '', scheduleKind: 'INTERVAL', intervalMinutes: '5', scopes: ['files.orders_import'], notifyUserIds: [], includeMe: true,
    retentionDays: '30', email: emptyEmail(), remote: ok(),
  }
  const msgs = { nameRequired: 'n', nameMax: 'm', clientRequired: 'c', templateRequired: 't', folderRequired: 'f', folderInvalid: 'fi', interval: 'i', retention: 'r', scopes: 's' }

  it('el alta valida el servidor con la ruta remote.* y no pide carpeta local', () => {
    const run = (v: IntegrationValues) => {
      const r = integrationSchema(msgs, false, undefined, { hasSavedSecret: false, messages: M }).safeParse(v)
      return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}:${i.message}`)
    }
    expect(run(base)).toEqual([])
    expect(run({ ...base, remote: { ...ok(), host: '' } })).toEqual(['remote.host:host'])
  })

  it('el alta manda la configuración y el secreto; la edición solo manda el secreto si se escribió uno nuevo', () => {
    expect(buildCreateRequest(base)).toMatchObject({ connectorKind: 'SFTP', folder: null, remote: { host: 'sftp.x.com' }, secret: { password: 'secreto' } })
    const keep = buildPatchRequest({ ...base, remote: { ...ok(), password: '' } }, { connectorKind: 'SFTP' })
    expect(keep.secret).toBeUndefined()
    expect(keep).toMatchObject({ remote: { host: 'sftp.x.com' } })
    expect(keep).not.toHaveProperty('folder')
    expect(buildPatchRequest(base, { connectorKind: 'FTP' }).secret).toEqual({ password: 'secreto', privateKey: null, passphrase: null })
    expect(buildCreateRequest({ ...base, connectorKind: 'FOLDER', folder: 'x' })).not.toHaveProperty('remote')
  })
})
