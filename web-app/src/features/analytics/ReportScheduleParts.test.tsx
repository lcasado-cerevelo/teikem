// Lote 92 — bloque «Enviar por correo» del constructor de vistas sobre un fetch simulado: activar, elegir frecuencia, destinatarios,
// último envío y «Enviar ahora».
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ReportScheduleFields, type ScheduleValue } from './ReportScheduleParts'
import type { ReportDto } from './viewRules'

const mock = vi.hoisted(() => ({ posts: [] as string[], status: 'SENT' as string }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    if (req.method === 'POST' && url.pathname === '/api/v1/analytics/reports/7/deliver') {
      mock.posts.push(url.pathname)
      return new Response(JSON.stringify({ status: mock.status, message: mock.status === 'SKIPPED' ? 'El envío de correo no está configurado.' : 'ok', recipients: ['a@b.co', 'c@d.org'], rows: 12 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }
    return new Response(JSON.stringify({ items: [] }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const REPORT = {
  id: 7,
  name: 'Facturas',
  baseEntityType: 'INVOICE',
  scheduleCron: '0 7 * * *',
  deliveryEmails: 'a@b.co, c@d.org',
  deliveryRangeMode: 'LAST7',
  lastDeliveryUtc: '2026-10-16T11:00:00',
  lastDeliveryStatus: 'FAILED',
  lastDeliveryMessage: 'No se pudo enviar a: a@b.co',
} as unknown as ReportDto

function Harness({ initial, report, dirty = false, onValue }: { initial: ScheduleValue; report: ReportDto | null; dirty?: boolean; onValue?: (v: ScheduleValue) => void }) {
  const [value, setValue] = useState(initial)
  return (
    <ReportScheduleFields
      value={value}
      report={report}
      dirty={dirty}
      onChange={(patch) => {
        const next = { ...value, ...patch }
        setValue(next)
        onValue?.(next)
      }}
    />
  )
}

function wrap(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

const EMPTY: ScheduleValue = { scheduleCron: null, deliveryEmails: null, deliveryRangeMode: null }
const SAVED: ScheduleValue = { scheduleCron: '0 7 * * *', deliveryEmails: 'a@b.co, c@d.org', deliveryRangeMode: 'LAST7' }

describe('Enviar por correo', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.posts.length = 0
    mock.status = 'SENT'
  })

  it('activar arma una programación diaria a las 7 con el período por omisión; desactivar lo limpia todo', async () => {
    const seen: ScheduleValue[] = []
    wrap(<Harness initial={EMPTY} report={null} onValue={(v) => seen.push(v)} />)
    expect(screen.queryByLabelText('Frecuencia')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('checkbox', { name: /Enviar este informe por correo/ }))
    expect(seen.at(-1)).toEqual({ scheduleCron: '0 7 * * *', deliveryEmails: null, deliveryRangeMode: 'LAST7' })
    expect(screen.getByText('Escriba al menos un correo destinatario.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('checkbox', { name: /Enviar este informe por correo/ }))
    expect(seen.at(-1)).toEqual(EMPTY)
  })

  it('elegir semanal y un día, la hora, el período y los destinatarios actualiza los valores', async () => {
    const seen: ScheduleValue[] = []
    wrap(<Harness initial={{ scheduleCron: '0 7 * * *', deliveryEmails: null, deliveryRangeMode: 'LAST7' }} report={null} onValue={(v) => seen.push(v)} />)
    await userEvent.selectOptions(screen.getByLabelText('Frecuencia'), 'WEEKLY')
    expect(seen.at(-1)?.scheduleCron).toBe('0 7 * * 1')
    await userEvent.selectOptions(screen.getByLabelText('Día de la semana'), '5')
    expect(seen.at(-1)?.scheduleCron).toBe('0 7 * * 5')
    await userEvent.selectOptions(screen.getByLabelText('Frecuencia'), 'MONTHLY')
    await userEvent.selectOptions(screen.getByLabelText('Día del mes'), '15')
    expect(seen.at(-1)?.scheduleCron).toBe('0 7 15 * *')
    await userEvent.selectOptions(screen.getByLabelText('Período del informe enviado'), 'THIS_MONTH')
    expect(seen.at(-1)?.deliveryRangeMode).toBe('THIS_MONTH')
    await userEvent.type(screen.getByLabelText('Destinatarios'), 'ana@x.co, nope')
    expect(await screen.findByText("El correo 'nope' no es válido.")).toBeInTheDocument()
    expect(seen.at(-1)?.deliveryEmails).toBe('ana@x.co, nope')
  })

  it('una vista guardada muestra el último envío y «Enviar ahora» manda a los destinatarios', async () => {
    wrap(<Harness initial={SAVED} report={REPORT} />)
    expect(screen.getByTestId('last-delivery')).toHaveTextContent('Con error')
    expect(screen.getByTestId('last-delivery')).toHaveTextContent('No se pudo enviar a: a@b.co')
    await userEvent.click(screen.getByRole('button', { name: 'Enviar ahora' }))
    await waitFor(() => expect(mock.posts).toEqual(['/api/v1/analytics/reports/7/deliver']))
    expect((await screen.findAllByText('Enviado a 2 destinatario(s) con 12 fila(s).')).length).toBeGreaterThan(0)   // el aviso del bloque y el toast
  })

  it('con cambios sin guardar, «Enviar ahora» se apaga; un envío omitido explica por qué', async () => {
    const { unmount } = wrap(<Harness initial={SAVED} report={REPORT} dirty />)
    expect(screen.getByRole('button', { name: 'Enviar ahora' })).toBeDisabled()
    expect(screen.getByText(/Guarde los cambios/)).toBeInTheDocument()
    unmount()
    mock.status = 'SKIPPED'
    wrap(<Harness initial={SAVED} report={REPORT} />)
    await userEvent.click(screen.getByRole('button', { name: 'Enviar ahora' }))
    expect((await screen.findAllByText('No se envió: El envío de correo no está configurado.')).length).toBeGreaterThan(0)
  })

  it('una programación que la pantalla no sabe armar se conserva y se avisa', () => {
    wrap(<Harness initial={{ scheduleCron: '*/5 * * * *', deliveryEmails: 'a@b.co', deliveryRangeMode: 'LAST7' }} report={null} />)
    expect(screen.getByText(/Programación personalizada \(\*\/5 \* \* \* \*\)/)).toBeInTheDocument()
  })
})
