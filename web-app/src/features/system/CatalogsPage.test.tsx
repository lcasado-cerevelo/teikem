// Pruebas de "Catálogos de valores" (Lote F8a P6) sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import CatalogsPage from './CatalogsPage'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; url: URL; body: unknown }[] }))

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

    if (req.method === 'GET' && p === '/api/v1/catalogs/domains') return json(DOMAINS)
    if (req.method === 'GET' && p === '/api/v1/catalogs/WarehouseTaskType') return json(TASK_TYPE_VALUES)
    if (req.method === 'GET' && p === '/api/v1/catalogs/AdjustmentReason') return json(ADJUSTMENT_VALUES)
    if (req.method === 'GET' && p === '/api/v1/catalogs/T1_MOTIVO') return json(MOTIVO_VALUES)
    if (req.method === 'PUT' && p === '/api/v1/catalogs/WarehouseTaskType/CROSSDOCK/override') {
      return json({ ...TASK_TYPE_VALUES[3], isOverridden: true })
    }
    if (req.method === 'DELETE' && p === '/api/v1/catalogs/WarehouseTaskType/PUTAWAY/override') return new Response(null, { status: 204 })
    if (req.method === 'PUT' && p === '/api/v1/catalogs/T1_MOTIVO/PROPIO') return json({ ...MOTIVO_VALUES[0] })
    if (req.method === 'POST' && p === '/api/v1/catalogs/T1_MOTIVO') return json({ ...MOTIVO_VALUES[0], code: 'NEW_ONE' })
    if (req.method === 'DELETE' && p === '/api/v1/catalogs/T1_MOTIVO/PROPIO') return new Response(null, { status: 204 })
    if (req.method === 'DELETE' && p === '/api/v1/catalogs/lists/T1_MOTIVO') return new Response(null, { status: 204 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const DOMAINS = [
  { id: 1, domainKey: 'WarehouseTaskType', scope: 1, label: 'Tipo de tarea de almacén', labels: { es: 'Tipo de tarea de almacén', en: 'Warehouse task type' }, description: null, isSystem: true, isActive: true, tenantId: null },
  { id: 2, domainKey: 'AdjustmentReason', scope: 1, label: 'Motivo de ajuste', labels: { es: 'Motivo de ajuste', en: 'Adjustment reason' }, description: null, isSystem: true, isActive: true, tenantId: null },
  { id: 3, domainKey: 'T1_MOTIVO', scope: 1, label: 'Motivo (Advance)', labels: { es: 'Motivo (Advance)', en: 'Reason (Advance)' }, description: null, isSystem: false, isActive: true, tenantId: 1 },
]

const TASK_TYPE_VALUES = [
  { id: 10, entity: 'WarehouseTaskType', code: 'PUTAWAY', label: 'Acomodo', labels: { es: 'Acomodo', en: 'Putaway' }, description: null, extraJson: null, sortOrder: 10, isSystem: true, isEnabled: true, isOverridden: false, tenantId: null },
  { id: 11, entity: 'WarehouseTaskType', code: 'REPLENISH', label: 'Reabasto', labels: { es: 'Reabasto', en: 'Replenish' }, description: null, extraJson: null, sortOrder: 20, isSystem: true, isEnabled: true, isOverridden: false, tenantId: null },
  { id: 12, entity: 'WarehouseTaskType', code: 'COUNT', label: 'Recuento cíclico', labels: { es: 'Recuento cíclico', en: 'Cycle count' }, description: null, extraJson: null, sortOrder: 30, isSystem: true, isEnabled: true, isOverridden: true, tenantId: null },
  { id: 13, entity: 'WarehouseTaskType', code: 'CROSSDOCK', label: 'Cross-dock', labels: { es: 'Cross-dock', en: 'Cross-dock' }, description: null, extraJson: null, sortOrder: 40, isSystem: true, isEnabled: false, isOverridden: false, tenantId: null },
]

// AdjustmentReason es un dominio de sistema (global, tenantId null): sus valores también, como en la BD real (el
// admin de la compañía solo puede Ajustar/Restaurar ahí, nunca Editar/Nuevo valor — eso es para listas propias).
const ADJUSTMENT_VALUES = [
  { id: 20, entity: 'AdjustmentReason', code: 'DAMAGE', label: 'Daño', labels: { es: 'Daño', en: 'Damage' }, description: null, extraJson: null, sortOrder: 10, isSystem: true, isEnabled: true, isOverridden: false, tenantId: null, isActive: true },
]

// Motivo (Advance) es una lista propia del tenant (T1_MOTIVO): aquí sí aplican Nuevo valor/Editar/Desactivar.
const MOTIVO_VALUES = [
  { id: 30, entity: 'T1_MOTIVO', code: 'PROPIO', label: 'Propio', labels: { es: 'Propio', en: 'Own' }, description: null, extraJson: null, sortOrder: 10, isSystem: false, isEnabled: true, isOverridden: false, tenantId: 1, isActive: true },
]

function wrap(ui: ReactNode, permissions: string[] = ['admin.catalogs'], modules: string[] = ['SYSTEM']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={modules}>
        {ui}
      </AccessProvider>
    </QueryClientProvider>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
})

describe('CatalogsPage', () => {
  it('lista los dominios y las columnas de la tabla del primero seleccionado', async () => {
    wrap(<CatalogsPage />)
    await screen.findByText('Tipo de tarea de almacén', { selector: 'h2' })
    expect(await screen.findByText('PUTAWAY')).toBeInTheDocument()
    expect(screen.getAllByText('Cross-dock').length).toBeGreaterThan(0)
  })

  it('cambiar de dominio recarga la tabla derecha', async () => {
    const user = userEvent.setup()
    wrap(<CatalogsPage />)
    await screen.findByText('PUTAWAY')

    await user.click(screen.getByText('Motivo de ajuste'))
    expect(await screen.findByText('DAMAGE')).toBeInTheDocument()
    expect(screen.queryByText('PUTAWAY')).toBeNull()
  })

  it('"Ajustar" llama a PUT override y "Restaurar" llama a DELETE override', async () => {
    const user = userEvent.setup()
    wrap(<CatalogsPage />)
    await screen.findByText('CROSSDOCK')

    // Ajustar el cuarto valor (Cross-dock)
    const rows = screen.getAllByRole('row').slice(1)
    const crossdockRow = rows.find((r) => r.textContent?.includes('CROSSDOCK'))!
    await userEvent.click(within(crossdockRow).getByRole('button', { name: 'Ajustar' }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))

    await waitFor(() =>
      expect(mock.requests.some((r) => r.method === 'PUT' && r.url.pathname === '/api/v1/catalogs/WarehouseTaskType/CROSSDOCK/override')).toBe(true),
    )

    // Restaurar el valor ya ajustado (COUNT, isOverridden=true)
    const countRow = screen.getAllByRole('row').slice(1).find((r) => r.textContent?.includes('COUNT'))!
    await user.click(within(countRow).getByRole('button', { name: 'Restaurar' }))
    const confirmButtons = screen.getAllByRole('button', { name: 'Restaurar' })
    await user.click(confirmButtons[confirmButtons.length - 1])

    await waitFor(() =>
      expect(mock.requests.some((r) => r.method === 'DELETE' && r.url.pathname === '/api/v1/catalogs/WarehouseTaskType/COUNT/override')).toBe(true),
    )
  })

  it('"Nuevo valor", "Editar" y "Desactivar" en un valor propio (lista propia del tenant)', async () => {
    const user = userEvent.setup()
    wrap(<CatalogsPage />)
    await screen.findByText('Tipo de tarea de almacén', { selector: 'h2' })

    await user.click(screen.getByText('Motivo (Advance)'))
    await screen.findByText('PROPIO')

    await user.click(screen.getByRole('button', { name: 'Nuevo valor' }))
    await user.type(screen.getByLabelText(/^Código/), 'FOUND')
    await user.type(screen.getByLabelText(/^Etiqueta \(es\)/), 'Encontrado')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.url.pathname === '/api/v1/catalogs/T1_MOTIVO')).toBe(true))

    await user.click(screen.getByRole('button', { name: 'Editar' }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT' && r.url.pathname === '/api/v1/catalogs/T1_MOTIVO/PROPIO')).toBe(true))

    await user.click(screen.getByRole('button', { name: 'Desactivar' }))
    const confirmButtons = screen.getAllByRole('button', { name: 'Desactivar' })
    await user.click(confirmButtons[confirmButtons.length - 1])
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'DELETE' && r.url.pathname === '/api/v1/catalogs/T1_MOTIVO/PROPIO')).toBe(true))
  })

  it('en un dominio global (Motivo de ajuste) no se ofrece "Nuevo valor" ni "Editar"/"Desactivar", aunque el valor no venga marcado isSystem', async () => {
    // Hallazgo S5: lo que decide es tenantId (como el servidor), no isSystem por fila.
    const user = userEvent.setup()
    wrap(<CatalogsPage />)
    await screen.findByText('Tipo de tarea de almacén', { selector: 'h2' })

    await user.click(screen.getByText('Motivo de ajuste'))
    await screen.findByText('DAMAGE')

    expect(screen.queryByRole('button', { name: 'Nuevo valor' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Desactivar' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Ajustar' })).toBeInTheDocument()
    expect(screen.getByText('Sistema')).toBeInTheDocument()
  })

  it('eliminar una lista propia llama al DELETE correcto', async () => {
    const user = userEvent.setup()
    wrap(<CatalogsPage />)
    await screen.findByText('PUTAWAY')

    await user.click(screen.getByText('Motivo (Advance)'))
    await user.click(screen.getByRole('button', { name: 'Eliminar lista' }))
    const confirmButtons = screen.getAllByRole('button', { name: 'Eliminar lista' })
    await user.click(confirmButtons[confirmButtons.length - 1])

    await waitFor(() => expect(mock.requests.some((r) => r.method === 'DELETE' && r.url.pathname === '/api/v1/catalogs/lists/T1_MOTIVO')).toBe(true))
  })

  it('no ofrece "Eliminar lista" para un dominio de sistema', async () => {
    wrap(<CatalogsPage />)
    await screen.findByText('PUTAWAY')
    expect(screen.queryByRole('button', { name: 'Eliminar lista' })).toBeNull()
  })

  it('sin admin.catalogs no se ofrece "Nueva lista" ni acciones de fila', async () => {
    wrap(<CatalogsPage />, [], ['SYSTEM'])
    await screen.findByText('PUTAWAY')
    expect(screen.queryByRole('button', { name: /Nueva lista/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Ajustar' })).toBeNull()
  })
})
