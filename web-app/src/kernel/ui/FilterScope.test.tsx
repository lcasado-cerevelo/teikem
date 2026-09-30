// Ámbito de filtros del kit: los controles (SelectFilter, SearchSelect, DateRangeFilter, QBox) anotan su etiqueta y su valor
// legible solo con un valor distinto de vacío/"Todos"; la exportación de DataTable/ListPager recibe la compañía y la
// oración; dentro de un Modal no hay línea de filtros; al desmontar un control deja de contar.
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { DataTable, type DataColumn } from './DataTable'
import { exportTable } from './exportTable'
import { DateRangeFilter, Filters, SelectFilter } from './Filters'
import { ExportCompanyProvider, FilterScope } from './FilterScope'
import { useExportHeading, useRegisterFilter, type ExportHeading } from './filterScopeContext'
import { Modal } from './Modal'
import { QBox } from './QBox'
import { SearchSelect } from './SearchSelect'

// la descarga real (SheetJS/jsPDF/Blob) no corre en jsdom: se verifica qué recibe
vi.mock('./exportTable', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./exportTable')>()),
  exportTable: vi.fn(() => Promise.resolve()),
}))

beforeAll(() => setLang('es'))

/** Lee el encabezado de exportación del ámbito al hacer clic (como DataTable al exportar). */
function Probe({ onRead }: { onRead: (h: ExportHeading) => void }) {
  const read = useExportHeading()
  return (
    <button type="button" onClick={() => onRead(read())}>
      leer
    </button>
  )
}

const STATUS = [
  { value: 'OPEN', label: 'Recibiendo' },
  { value: 'DONE', label: 'Completado' },
]
const WAREHOUSES = [
  { value: 'w1', label: 'ALM-DEPOT' },
  { value: 'w2', label: 'ALM-NORTE' },
]

function Bar({ initial }: { initial: { status: string; warehouses: string[]; from: string; to: string; q: string } }) {
  const [status, setStatus] = useState(initial.status)
  const [warehouses, setWarehouses] = useState(initial.warehouses)
  const [range, setRange] = useState({ from: initial.from, to: initial.to })
  const [q, setQ] = useState(initial.q)
  return (
    <Filters
      onClear={() => {
        setStatus('')
        setWarehouses([])
        setRange({ from: '', to: '' })
        setQ('')
      }}
    >
      <SearchSelect label="Almacén" options={WAREHOUSES} value={warehouses} onChange={setWarehouses} />
      <SelectFilter label="Estatus" value={status} onChange={setStatus} options={STATUS} />
      <DateRangeFilter label="Creado" value={range} onChange={setRange} />
      <QBox value={q} onChange={setQ} />
    </Filters>
  )
}

describe('FilterScope + controles del kit', () => {
  it('arma la oración con los valores legibles, en el orden de la barra, y la compañía activa', async () => {
    const user = userEvent.setup()
    const onRead = vi.fn()
    render(
      <ExportCompanyProvider company=" Advance Logistics ">
        <FilterScope>
          <Bar initial={{ status: 'OPEN', warehouses: ['w1', 'w2'], from: '2026-09-01', to: '2026-09-30', q: 'caja' }} />
          <Probe onRead={onRead} />
        </FilterScope>
      </ExportCompanyProvider>,
    )
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(onRead).toHaveBeenLastCalledWith({
      company: 'Advance Logistics',
      filters: 'Filtros: Almacén ALM-DEPOT, ALM-NORTE · Estatus Recibiendo · Creado del 01/09/2026 al 30/09/2026 · Buscar "caja"',
    })

    // "Limpiar": todos vacíos → la barra existe pero no filtra
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(onRead).toHaveBeenLastCalledWith({ company: 'Advance Logistics', filters: 'Sin filtros' })
  })

  it('los vacíos y "Todos" no salen; un valor que cambia se actualiza', async () => {
    const user = userEvent.setup()
    const onRead = vi.fn()
    render(
      <FilterScope>
        <Bar initial={{ status: '', warehouses: [], from: '', to: '2026-09-30', q: '  ' }} />
        <Probe onRead={onRead} />
      </FilterScope>,
    )
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(onRead).toHaveBeenLastCalledWith({ company: null, filters: 'Filtros: Creado hasta 30/09/2026' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Estatus' }), 'DONE')
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(onRead).toHaveBeenLastCalledWith({ company: null, filters: 'Filtros: Estatus Completado · Creado hasta 30/09/2026' })
  })

  it('sin ámbito, sin controles o dentro de un Modal: sin línea de filtros (la compañía sí llega al modal)', async () => {
    const user = userEvent.setup()
    const outside = vi.fn()
    const { unmount } = render(<Probe onRead={outside} />)
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(outside).toHaveBeenLastCalledWith({ company: null, filters: null })
    unmount()

    const inModal = vi.fn()
    const empty = vi.fn()
    render(
      <ExportCompanyProvider company="Advance Logistics">
        <FilterScope>
          <Bar initial={{ status: 'OPEN', warehouses: [], from: '', to: '', q: '' }} />
          {/* un detalle con su propio ámbito, sin controles */}
          <FilterScope>
            <Probe onRead={empty} />
          </FilterScope>
          <Modal open title="Detalle" onClose={() => {}}>
            <SelectFilter label="Tipo" value="X" onChange={() => {}} options={[{ value: 'X', label: 'Equis' }]} />
            <Probe onRead={inModal} />
          </Modal>
        </FilterScope>
      </ExportCompanyProvider>,
    )
    const [detailProbe, modalProbe] = screen.getAllByRole('button', { name: 'leer' })
    await user.click(detailProbe)
    expect(empty).toHaveBeenLastCalledWith({ company: 'Advance Logistics', filters: null })
    await user.click(modalProbe)
    expect(inModal).toHaveBeenLastCalledWith({ company: 'Advance Logistics', filters: null })
  })

  it('useRegisterFilter: un control que se desmonta deja de contar; enabled=false no se anota', async () => {
    const user = userEvent.setup()
    const onRead = vi.fn()
    function Extra({ on }: { on: boolean }) {
      useRegisterFilter('Vista', 'Activos', undefined, on)
      return null
    }
    function Screen() {
      const [shown, setShown] = useState(true)
      const [enabled, setEnabled] = useState(true)
      return (
        <FilterScope>
          {shown && <Extra on={enabled} />}
          <button type="button" onClick={() => setEnabled(false)}>
            apagar
          </button>
          <button type="button" onClick={() => setShown(false)}>
            quitar
          </button>
          <Probe onRead={onRead} />
        </FilterScope>
      )
    }
    render(<Screen />)
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(onRead).toHaveBeenLastCalledWith({ company: null, filters: 'Filtros: Vista Activos' })
    await user.click(screen.getByRole('button', { name: 'apagar' }))
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(onRead).toHaveBeenLastCalledWith({ company: null, filters: null })
    await user.click(screen.getByRole('button', { name: 'quitar' }))
    await user.click(screen.getByRole('button', { name: 'leer' }))
    expect(onRead).toHaveBeenLastCalledWith({ company: null, filters: null })
  })
})

describe('DataTable exporta con compañía y filtros del ámbito', () => {
  interface Row {
    code: string
  }
  const COLS: DataColumn<Row>[] = [{ id: 'code', header: 'Código', cell: (r) => r.code, sortValue: (r) => r.code }]
  const ROWS: Row[] = [{ code: 'A' }]

  it('PDF: company y filters en las opciones de exportTable; dentro de un Modal, sin filters', async () => {
    const user = userEvent.setup()
    vi.mocked(exportTable).mockClear()
    render(
      <ExportCompanyProvider company="Advance Logistics">
        <FilterScope>
          <SelectFilter label="Estatus" value="OPEN" onChange={() => {}} options={STATUS} />
          <DataTable columns={COLS} rows={ROWS} rowKey={(r) => r.code} label="Recibos" />
        </FilterScope>
      </ExportCompanyProvider>,
    )
    await user.click(screen.getByRole('button', { name: 'Exportar' }))
    await user.click(screen.getByRole('menuitem', { name: 'PDF (.pdf)' }))
    expect(vi.mocked(exportTable).mock.calls[0][3]).toMatchObject({
      title: 'Recibos',
      company: 'Advance Logistics',
      filters: 'Filtros: Estatus Recibiendo',
    })
  })

  it('tabla dentro de un Modal: compañía sí, filtros no', async () => {
    const user = userEvent.setup()
    vi.mocked(exportTable).mockClear()
    render(
      <ExportCompanyProvider company="Advance Logistics">
        <FilterScope>
          <SelectFilter label="Estatus" value="OPEN" onChange={() => {}} options={STATUS} />
          <Modal open title="Movimientos" onClose={() => {}}>
            <DataTable columns={COLS} rows={ROWS} rowKey={(r) => r.code} />
          </Modal>
        </FilterScope>
      </ExportCompanyProvider>,
    )
    await user.click(screen.getByRole('button', { name: 'Exportar' }))
    await user.click(screen.getByRole('menuitem', { name: 'Excel (.xlsx)' }))
    expect(vi.mocked(exportTable).mock.calls[0][3]).toMatchObject({ title: 'Movimientos', company: 'Advance Logistics', filters: null })
  })
})
