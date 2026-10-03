// Pruebas de "Seguridad y auditoría" (/system/audit, lote F10) sobre un fetch simulado: las dos pestañas, filtro de tipo y
// buscador al API, orden por columna, "Exportar CSV" con todo lo filtrado, sesiones de la compañía (la propia marcada, revocar
// una y las demás), política (guardar solo lo que cambió, validación en pantalla, 400 del servidor junto al campo) y solo
// lectura sin admin.tenant / sin botones de revocar sin admin.users.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { resetFormatSettings } from '../../kernel/format'
import { setLang } from '../../kernel/i18n/i18n'
import { buildExportData, toCsv, type ExportableColumn } from '../../kernel/ui/exportTable'
import AuditScreen from './AuditScreen'

type Req = { method: string; url: URL; body: unknown }
const mock = vi.hoisted(() => ({
  requests: [] as Req[],
  settings: {} as Record<string, unknown>,
  sessions: [] as Record<string, unknown>[],
  exported: [] as { format: string; columns: unknown[]; rows: unknown[]; opts: Record<string, unknown> }[],
}))

vi.mock('../../kernel/ui/exportTable', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/ui/exportTable')>()
  return {
    ...actual,
    exportTable: async (format: string, columns: unknown[], rows: unknown[], opts: Record<string, unknown>) => {
      mock.exported.push({ format, columns, rows, opts })
    },
  }
})

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const p = url.pathname
    let body: unknown = null
    try {
      body = req.method === 'GET' || req.method === 'DELETE' ? null : await req.clone().json()
    } catch {
      body = null
    }
    mock.requests.push({ method: req.method, url, body })
    if (p === '/api/v1/audit/activity') {
      const kind = url.searchParams.get('kind') ?? 'all'
      const text = (url.searchParams.get('text') ?? '').toLowerCase()
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 50)
      const all = ACTIVITY.filter(
        (r) => (kind === 'all' || (kind === 'changes' ? r.kind === 'change' : r.kind === 'security')) && (!text || JSON.stringify(r).toLowerCase().includes(text)),
      )
      return json({ items: all.slice(skip, skip + take), total: all.length, skip, take })
    }
    if (p === '/api/v1/audit/sessions' && req.method === 'GET') return json(mock.sessions)
    if (p.startsWith('/api/v1/audit/sessions/') && req.method === 'DELETE') {
      const id = Number(p.split('/').pop())
      mock.sessions = mock.sessions.filter((s) => s.id !== id)
      return new Response(null, { status: 204 })
    }
    if (p === '/api/v1/audit/sessions/revoke-others' && req.method === 'POST') {
      const n = mock.sessions.filter((s) => !s.isCurrent).length
      mock.sessions = mock.sessions.filter((s) => s.isCurrent)
      return json({ revoked: n })
    }
    if (p === '/api/v1/tenant/settings' && req.method === 'GET') return json(mock.settings)
    if (p === '/api/v1/tenant/settings' && req.method === 'PUT') {
      const b = body as Record<string, unknown>
      if (b.deviceSessionDays === 200) {
        return problem(400, { title: 'Entre 1 y 365 días.', code: 'validation', errors: { deviceSessionDays: ['Entre 1 y 365 días.'] } })
      }
      mock.settings = { ...mock.settings, ...b }
      return json(mock.settings)
    }
    return problem(404, { title: 'no encontrado' })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
function problem(status: number, body: object) {
  return new Response(JSON.stringify({ status, ...body }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

const ACTIVITY = [
  {
    kind: 'change', id: 11, createdAtUtc: '2026-10-02T14:00:00', type: 'Modificación · Compañía #1', userId: 1, userName: 'Luis Casado',
    detail: '{"SessionDays":{"from":30,"to":15}}', typeCode: 'UPDATE', outcomeCode: null, ipAddress: '10.0.0.1', correlationId: null,
  },
  {
    kind: 'security', id: 21, createdAtUtc: '2026-10-02T13:00:00', type: 'Permiso denegado · Bloqueado', userId: 3, userName: 'Carlos Rivera',
    detail: '{"permission":"admin.users"}', typeCode: 'PERMISSION_DENIED', outcomeCode: 'BLOCKED', ipAddress: null, correlationId: null,
  },
  {
    kind: 'security', id: 22, createdAtUtc: '2026-10-02T12:00:00', type: 'Inicio de sesión · Éxito', userId: 2, userName: 'Ana Pérez, "Supervisora"',
    detail: null, typeCode: 'LOGIN', outcomeCode: 'SUCCESS', ipAddress: null, correlationId: null,
  },
]
const SESSIONS = [
  { id: 101, userId: 1, userName: 'Luis Casado', userEmail: 'luis@t.local', deviceInfo: 'Mozilla/5.0 (Windows NT 10.0) Chrome/120', deviceName: null, isDevice: false, ipAddress: '10.0.0.1', lastActivityUtc: '2026-10-02T14:00:00', expiresAtUtc: '2026-11-01T00:00:00', isCurrent: true },
  { id: 102, userId: 2, userName: 'Ana Pérez', userEmail: 'ana@t.local', deviceInfo: 'Mozilla/5.0 (iPhone) Safari/604', deviceName: null, isDevice: false, ipAddress: '10.0.0.2', lastActivityUtc: '2026-10-02T11:00:00', expiresAtUtc: '2026-11-01T00:00:00', isCurrent: false },
  { id: 103, userId: 3, userName: 'Carlos Rivera', userEmail: 'c@t.local', deviceInfo: 'okhttp', deviceName: 'ZEBRA-01 · Muelle 1', isDevice: true, ipAddress: null, lastActivityUtc: '2026-10-01T09:00:00', expiresAtUtc: '2026-11-01T00:00:00', isCurrent: false },
]
const SETTINGS = { id: 1, name: 'Advance Logistics', mfaRequired: false, aal2WindowMinutes: 30, sessionDays: 30, deviceSessionDays: 30 }

const ALL = ['admin.audit', 'admin.tenant', 'admin.users']

function wrap(path = '/system/audit', permissions = ALL) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const ui: ReactNode = (
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={['SYSTEM']}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/system/audit" element={<AuditScreen />} />
          </Routes>
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>
  )
  return render(ui)
}

const activityReqs = () => mock.requests.filter((r) => r.url.pathname === '/api/v1/audit/activity')
const putReqs = () => mock.requests.filter((r) => r.method === 'PUT' && r.url.pathname === '/api/v1/tenant/settings')
const bodyRows = () => screen.getAllByRole('row').slice(1)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.exported = []
  mock.settings = { ...SETTINGS }
  mock.sessions = SESSIONS.map((s) => ({ ...s }))
})
afterEach(() => resetFormatSettings())

describe('Seguridad y auditoría', () => {
  it('Actividad: pestañas, filas con Cambio / Alerta / Evento, usuario y detalle legible', async () => {
    wrap()
    const tabs = screen.getByRole('tablist', { name: 'Secciones de Seguridad y auditoría' })
    expect(within(tabs).getAllByRole('tab').map((b) => b.textContent?.trim())).toEqual(['Actividad', 'Sesiones y MFA'])
    expect(within(tabs).getByRole('tab', { name: 'Actividad' })).toHaveAttribute('aria-selected', 'true')
    await screen.findByText('Modificación · Compañía #1')
    const rows = bodyRows()
    expect(rows).toHaveLength(3)
    expect(within(rows[0]).getByText('Cambio')).toBeInTheDocument()
    expect(within(rows[0]).getByText('SessionDays: 30 → 15')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Luis Casado')).toBeInTheDocument()
    // 14:00 UTC → 10:00 a. m. en Puerto Rico (formatos de la compañía)
    expect(within(rows[0]).getByText(/10\/02\/2026 10:00/)).toBeInTheDocument()
    expect(within(rows[1]).getByText('Alerta')).toBeInTheDocument()
    expect(within(rows[2]).getByText('Evento')).toBeInTheDocument()
    expect(activityReqs()[0].url.searchParams.get('kind')).toBe('all')
  })

  it('filtro de tipo y buscador van al API; orden por columna', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Modificación · Compañía #1')
    await user.click(screen.getByRole('button', { name: 'Seguridad' }))
    expect(screen.getByRole('button', { name: 'Seguridad' })).toHaveAttribute('aria-pressed', 'true')
    await waitFor(() => expect(activityReqs().at(-1)!.url.searchParams.get('kind')).toBe('security'))
    await waitFor(() => expect(bodyRows()).toHaveLength(2))

    await user.type(screen.getByRole('searchbox', { name: 'Buscar en la actividad…' }), 'carlos')
    await waitFor(() => expect(activityReqs().at(-1)!.url.searchParams.get('text')).toBe('carlos'))
    await waitFor(() => expect(bodyRows()).toHaveLength(1))
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(bodyRows()).toHaveLength(3))

    // Usuario ascendente: Ana, Carlos, Luis
    await user.click(within(screen.getByRole('columnheader', { name: /Usuario/ })).getByRole('button'))
    await waitFor(() => expect(within(bodyRows()[0]).getByText(/Ana Pérez/)).toBeInTheDocument())
    expect(within(bodyRows()[2]).getByText('Luis Casado')).toBeInTheDocument()
  })

  it('Exportar CSV: todas las filas filtradas en el orden de la tabla, con valores escapados', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Modificación · Compañía #1')
    await user.click(screen.getByRole('button', { name: 'Cambios' }))
    await waitFor(() => expect(bodyRows()).toHaveLength(1))
    await user.click(screen.getByRole('button', { name: 'Todo' }))
    await waitFor(() => expect(bodyRows()).toHaveLength(3))
    await user.click(screen.getByRole('button', { name: /Exportar CSV/ }))
    await waitFor(() => expect(mock.exported).toHaveLength(1))
    const { format, columns, rows, opts } = mock.exported[0]
    expect(format).toBe('csv')
    expect(opts.title).toBe('auditoria')
    // se pidió todo (página de exportación de 200), no solo la página visible
    expect(activityReqs().at(-1)!.url.searchParams.get('take')).toBe('200')
    const csv = toCsv(buildExportData(columns as ExportableColumn<unknown>[], rows))
    const lines = csv.split('\r\n')
    expect(lines[0]).toBe('Cuándo,Tipo,Usuario,Detalle')
    expect(lines[1]).toBe('2026-10-02 10:00:00,Cambio,Luis Casado,Modificación · Compañía #1: SessionDays: 30 → 15')
    expect(lines[2]).toBe('2026-10-02 09:00:00,Alerta,Carlos Rivera,Permiso denegado · Bloqueado: permission: admin.users')
    expect(lines[3]).toBe('2026-10-02 08:00:00,Evento,"Ana Pérez, ""Supervisora""",Inicio de sesión · Éxito')
    expect(await screen.findByText('Descargando 3 filas')).toBeInTheDocument()
  })

  it('Sesiones y MFA (?tab=sessions): la propia marcada y sin revocar; revocar una y las demás', async () => {
    const user = userEvent.setup()
    wrap('/system/audit?tab=sessions')
    expect(screen.getByRole('tab', { name: 'Sesiones y MFA' })).toHaveAttribute('aria-selected', 'true')
    const table = await screen.findByRole('table', { name: 'Sesiones activas' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    const mine = rows.find((r) => within(r).queryByText('Esta sesión'))!
    expect(within(mine).getByText('Ahora mismo')).toBeInTheDocument()
    expect(within(mine).getByText('Chrome · Windows')).toBeInTheDocument()
    expect(within(mine).queryByRole('button', { name: 'Revocar' })).toBeNull()
    expect(within(table).getByText('ZEBRA-01 · Muelle 1 (Aparato de almacén)')).toBeInTheDocument()
    expect(within(table).getByText('10.0.0.2')).toBeInTheDocument()

    const ana = rows.find((r) => within(r).queryByText('Ana Pérez'))!
    await user.click(within(ana).getByRole('button', { name: 'Revocar' }))
    const dialog = await screen.findByRole('dialog', { name: '¿Revocar esta sesión?' })
    await user.click(within(dialog).getByRole('button', { name: 'Revocar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'DELETE' && r.url.pathname === '/api/v1/audit/sessions/102')).toBe(true))
    await waitFor(() => expect(within(screen.getByRole('table', { name: 'Sesiones activas' })).getAllByRole('row')).toHaveLength(3))

    await user.click(screen.getByRole('button', { name: /Cerrar las demás sesiones/ }))
    const confirm = await screen.findByRole('dialog', { name: '¿Cerrar las demás sesiones?' })
    expect(confirm).toHaveTextContent('Se cerrarán 1 sesiones de la compañía')
    await user.click(within(confirm).getByRole('button', { name: 'Cerrar las demás sesiones' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.url.pathname === '/api/v1/audit/sessions/revoke-others')).toBe(true))
    expect(await screen.findByText('Se cerraron las demás sesiones (1)')).toBeInTheDocument()
  })

  it('Política: guarda solo lo que cambió, valida en pantalla y pone el 400 del servidor junto al campo', async () => {
    const user = userEvent.setup()
    wrap('/system/audit?tab=sessions')
    const mfa = await screen.findByRole('switch', { name: 'Requerir MFA a todos los usuarios' })
    expect(mfa).not.toBeChecked()
    const reauth = screen.getByLabelText('Ventana de reautenticación (AAL2)')
    expect(Array.from((reauth as HTMLSelectElement).options).map((o) => o.textContent)).toEqual(['15 minutos', '30 minutos', '60 minutos'])
    expect(reauth).toHaveValue('30')
    const days = screen.getByLabelText('Duración de la sesión (días)')
    const deviceDays = screen.getByLabelText('Duración de la sesión del aparato (días)')

    // validación en pantalla: 0 días no se manda
    await user.clear(days)
    await user.type(days, '0')
    await user.click(screen.getByRole('button', { name: 'Guardar política' }))
    expect(await screen.findByText('Entre 1 y 365 días.')).toBeInTheDocument()
    expect(putReqs()).toHaveLength(0)

    await user.clear(days)
    await user.type(days, '15')
    await user.click(mfa)
    await user.selectOptions(reauth, '60')
    await user.click(screen.getByRole('button', { name: 'Guardar política' }))
    await waitFor(() => expect(putReqs()).toHaveLength(1))
    expect(putReqs()[0].body).toEqual({ mfaRequired: true, aal2WindowMinutes: 60, sessionDays: 15 })
    expect(await screen.findByText('Política guardada')).toBeInTheDocument()

    // 400 del servidor bajo su campo
    await user.clear(deviceDays)
    await user.type(deviceDays, '200')
    await user.click(screen.getByRole('button', { name: 'Guardar política' }))
    await waitFor(() => expect(putReqs()).toHaveLength(2))
    expect(putReqs()[1].body).toEqual({ deviceSessionDays: 200 })
    await waitFor(() => expect(deviceDays).toHaveAttribute('aria-invalid', 'true'))
    const errId = deviceDays.getAttribute('aria-describedby')!.split(' ').find((id) => document.getElementById(id)?.textContent === 'Entre 1 y 365 días.')
    expect(errId).toBeTruthy()
  })

  it('sin admin.tenant la política es de solo lectura; sin admin.users no hay botones para revocar', async () => {
    wrap('/system/audit?tab=sessions', ['admin.audit'])
    expect(await screen.findByText(/Solo lectura: cambiar la política exige/)).toBeInTheDocument()
    expect(await screen.findByRole('switch', { name: 'Requerir MFA a todos los usuarios' })).toBeDisabled()
    expect(screen.getByLabelText('Duración de la sesión (días)')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar política' })).toBeNull()
    await screen.findByRole('table', { name: 'Sesiones activas' })
    expect(screen.queryByRole('button', { name: 'Revocar' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Cerrar las demás sesiones/ })).toBeNull()
  })
})
