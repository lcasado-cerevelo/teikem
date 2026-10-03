// Lógica pura de Seguridad y auditoría (lote F10): pestañas, consulta de la Actividad, Cambio/Evento/Alerta, detalle legible,
// orden local, armado y escapado del CSV, y reglas de la política (mismos límites que `TenantService`).
import { describe, expect, it } from 'vitest'
import { buildExportData, toCsv, type ExportableColumn } from '../../../kernel/ui/exportTable'
import {
  activityBadge,
  activityDetailParts,
  activityDetailText,
  activityQuery,
  auditTabFromParam,
  EMPTY_ACTIVITY_FILTERS,
  policyErrors,
  policyRequestBody,
  reauthOptions,
  sessionLocation,
  sortRows,
  toPolicyValues,
  validSessionDays,
  type ActivityRowDto,
} from './auditView'

const TX = { yes: 'Sí', no: 'No', empty: '(vacío)' }

const row = (over: Partial<ActivityRowDto>): ActivityRowDto => ({
  kind: 'security',
  id: 1,
  createdAtUtc: '2026-10-02T14:00:00',
  type: 'Inicio de sesión · Éxito',
  detail: null,
  userId: 1,
  userName: 'Ana',
  ipAddress: null,
  correlationId: null,
  typeCode: 'LOGIN',
  outcomeCode: 'SUCCESS',
  ...over,
})

describe('pestañas', () => {
  it('?tab=sessions abre Sesiones y MFA; sin parámetro o desconocido, Actividad', () => {
    expect(auditTabFromParam('sessions')).toBe('sessions')
    expect(auditTabFromParam(null)).toBe('activity')
    expect(auditTabFromParam('otra')).toBe('activity')
  })
})

describe('consulta de la Actividad', () => {
  it('sin filtros: solo el tipo y la página', () => {
    expect(activityQuery(EMPTY_ACTIVITY_FILTERS, 25, 25)).toEqual({ kind: 'all', text: undefined, from: undefined, to: undefined, skip: 25, take: 25 })
  })

  it('texto recortado y días LOCALES de la compañía: desde la medianoche del primero hasta la del día siguiente al último', () => {
    const q = activityQuery({ kind: 'security', text: '  admin  ', range: { from: '2026-10-01', to: '2026-10-02' } }, 0, 50, 'America/Puerto_Rico')
    expect(q.kind).toBe('security')
    expect(q.text).toBe('admin')
    // Puerto Rico = UTC-4 todo el año
    expect(q.from).toBe('2026-10-01T04:00:00.000Z')
    expect(q.to).toBe('2026-10-03T04:00:00.000Z')
  })

  it('el día siguiente cruza el fin de mes y de año', () => {
    expect(activityQuery({ ...EMPTY_ACTIVITY_FILTERS, range: { from: '', to: '2026-12-31' } }, 0, 10, 'UTC').to).toBe('2027-01-01T00:00:00.000Z')
  })
})

describe('Cambio / Evento / Alerta (por código, no por texto)', () => {
  it('un cambio es Cambio', () => {
    expect(activityBadge(row({ kind: 'change', typeCode: 'UPDATE', outcomeCode: null }))).toBe('change')
  })
  it('evento exitoso es Evento; fallido o bloqueado es Alerta', () => {
    expect(activityBadge(row({}))).toBe('event')
    expect(activityBadge(row({ outcomeCode: 'FAILURE' }))).toBe('alert')
    expect(activityBadge(row({ outcomeCode: 'blocked' }))).toBe('alert')
  })
  it('permiso denegado y bloqueo de cuenta son Alerta aunque digan éxito', () => {
    expect(activityBadge(row({ typeCode: 'PERMISSION_DENIED', outcomeCode: 'SUCCESS' }))).toBe('alert')
    expect(activityBadge(row({ typeCode: 'LOCKOUT', outcomeCode: null }))).toBe('alert')
  })
})

describe('detalle legible', () => {
  it('un cambio: "Campo: antes → después", sin la llave interna', () => {
    const json = JSON.stringify({ SessionDays: { from: 30, to: 15 }, MfaRequired: { from: false, to: true }, Name: { from: null, to: 'X' }, $key: '{"id":1}' })
    expect(activityDetailParts(json, TX)).toEqual(['SessionDays: 30 → 15', 'MfaRequired: No → Sí', 'Name: (vacío) → X'])
  })
  it('un alta o un evento: "Campo: valor"; objetos compactos y textos largos recortados', () => {
    const long = 'x'.repeat(100)
    const parts = activityDetailParts(JSON.stringify({ scope: 'all', count: 3, extra: { a: 1 }, note: long }), TX)
    expect(parts.slice(0, 3)).toEqual(['scope: all', 'count: 3', 'extra: {"a":1}'])
    expect(parts[3]).toHaveLength('note: '.length + 80)
    expect(parts[3].endsWith('…')).toBe(true)
  })
  it('vacío → sin partes; texto que no es JSON → tal cual', () => {
    expect(activityDetailParts(null, TX)).toEqual([])
    expect(activityDetailParts('  ', TX)).toEqual([])
    expect(activityDetailParts('contraseña cambiada', TX)).toEqual(['contraseña cambiada'])
    expect(activityDetailParts('"texto"', TX)).toEqual(['texto'])
  })
  it('texto completo para el archivo: "Tipo: parte; parte"', () => {
    expect(activityDetailText(row({ detail: '{"stage":"reauth","n":2}' }), TX)).toBe('Inicio de sesión · Éxito: stage: reauth; n: 2')
    expect(activityDetailText(row({ detail: null }), TX)).toBe('Inicio de sesión · Éxito')
  })
})

describe('orden local', () => {
  const rows = [row({ id: 1, userName: 'Beto' }), row({ id: 2, userName: null }), row({ id: 3, userName: 'ana' }), row({ id: 4, userName: 'Carla' })]
  const value = (r: ActivityRowDto, id: string) => (id === 'user' ? (r.userName ?? null) : null)
  it('sin orden: el del servidor', () => {
    expect(sortRows(rows, null, value).map((r) => r.id)).toEqual([1, 2, 3, 4])
  })
  it('ascendente y descendente sin mayúsculas, vacíos siempre al final', () => {
    expect(sortRows(rows, { id: 'user', desc: false }, value).map((r) => r.id)).toEqual([3, 1, 4, 2])
    expect(sortRows(rows, { id: 'user', desc: true }, value).map((r) => r.id)).toEqual([4, 1, 3, 2])
  })
  it('fechas ISO ordenan como texto en el tiempo', () => {
    const dated = [row({ id: 1, createdAtUtc: '2026-10-01T09:00:00' }), row({ id: 2, createdAtUtc: '2026-10-02T08:00:00' })]
    expect(sortRows(dated, { id: 'when', desc: true }, (r) => r.createdAtUtc ?? null).map((r) => r.id)).toEqual([2, 1])
  })
})

describe('armado y escapado del CSV (columnas Cuándo, Tipo, Usuario, Detalle)', () => {
  const cols: ExportableColumn<ActivityRowDto>[] = [
    { header: 'Cuándo', cell: () => '', exportValue: (r) => r.createdAtUtc ?? null },
    { header: 'Tipo', cell: () => '', exportValue: (r) => (activityBadge(r) === 'alert' ? 'Alerta' : 'Evento') },
    { header: 'Usuario', cell: () => '', exportValue: (r) => r.userName ?? null },
    { header: 'Detalle', cell: () => '', exportValue: (r) => activityDetailText(r, TX) },
  ]
  it('comillas, comas y saltos de línea entre comillas; fórmulas neutralizadas; fecha legible por Excel', () => {
    const rows = [
      row({ userName: 'Rivera, Carlos', detail: '{"motivo":"dijo \\"hola\\"\\nadiós"}', outcomeCode: 'FAILURE' }),
      row({ userName: '=HYPERLINK("x")', detail: null }),
    ]
    const csv = toCsv(buildExportData(cols, rows))
    // filas separadas por CRLF; el salto de línea dentro de un campo queda entre comillas.
    // 14:00 UTC = 10:00 en Puerto Rico (zona de la compañía por defecto)
    expect(csv.split('\r\n')).toEqual([
      'Cuándo,Tipo,Usuario,Detalle',
      '2026-10-02 10:00:00,Alerta,"Rivera, Carlos","Inicio de sesión · Éxito: motivo: dijo ""hola""\nadiós"',
      `2026-10-02 10:00:00,Evento,"'=HYPERLINK(""x"")",Inicio de sesión · Éxito`,
    ])
  })
})

describe('sesiones', () => {
  it('la ubicación es la IP; sin IP, nada', () => {
    expect(sessionLocation({ ipAddress: ' 10.0.0.1 ' })).toBe('10.0.0.1')
    expect(sessionLocation({ ipAddress: null })).toBeNull()
  })
})

describe('política de la compañía', () => {
  it('ventanas de la maqueta (15/30/60) más el valor guardado si es otro', () => {
    expect(reauthOptions(30)).toEqual([15, 30, 60])
    expect(reauthOptions(45)).toEqual([15, 30, 45, 60])
    expect(reauthOptions(null)).toEqual([15, 30, 60])
  })
  it('días de 1 a 365, enteros', () => {
    expect([1, 365, 30].every(validSessionDays)).toBe(true)
    expect([0, 366, 1.5, null].some((n) => validSessionDays(n))).toBe(false)
  })
  it('errores por campo con los mismos límites que el servidor', () => {
    const ok = toPolicyValues({ mfaRequired: true, aal2WindowMinutes: 15, sessionDays: 30, deviceSessionDays: 7 })
    expect(policyErrors(ok)).toEqual({})
    expect(policyErrors({ ...ok, sessionDays: 0, deviceSessionDays: null, aal2WindowMinutes: '4' })).toEqual({
      sessionDays: 'daysRange',
      deviceSessionDays: 'daysRange',
      aal2WindowMinutes: 'reauthInvalid',
    })
  })
  it('el PUT lleva solo lo que cambió', () => {
    const saved = { mfaRequired: false, aal2WindowMinutes: 30, sessionDays: 30, deviceSessionDays: 30 }
    expect(policyRequestBody(saved, toPolicyValues(saved))).toEqual({})
    expect(policyRequestBody(saved, { ...toPolicyValues(saved), mfaRequired: true, aal2WindowMinutes: '60', deviceSessionDays: 7 })).toEqual({
      mfaRequired: true,
      aal2WindowMinutes: 60,
      deviceSessionDays: 7,
    })
  })
})
