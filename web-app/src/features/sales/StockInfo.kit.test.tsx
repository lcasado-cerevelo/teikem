// Lote 236 — el disponible de un kit de oferta sale desglosado por pieza: «Se pueden armar: N», cada pieza con lo que lleva el kit y lo que hay, la que limita
// marcada; con más de 3 piezas el desglose va plegado («Ver piezas (N) · limita …»); con una pieza sustituida usa la real; un kit armado no se desglosa; el
// backorder y la nota de otros almacenes siguen saliendo.
import { render, screen, within } from '@testing-library/react'
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { StockInfo } from './StockInfo'
import type { StockInput } from './stockRules'
import { WizardProviders } from './wizard/wizardTestKit'

const piece = (i: number) => ({ productPublicId: `p-${i}`, sku: `A0${i}`, name: `Producto ${i}`, qtyPerKit: i })
const STOCK: StockInput = {
  warehouses: [
    { publicId: 'w-1', code: 'ALM-01', name: 'Bayamón', isMain: true },
    { publicId: 'w-2', code: 'ALM-02', name: 'Ponce', isMain: false },
  ],
  products: [
    { productPublicId: 'strips', stock: [{ warehousePublicId: 'w-1', available: 600 }, { warehousePublicId: 'w-2', available: 13 }] },
    { productPublicId: 'monitor', stock: [{ warehousePublicId: 'w-1', available: 500 }, { warehousePublicId: 'w-2', available: 1 }] },
    { productPublicId: 'other', stock: [{ warehousePublicId: 'w-1', available: 40 }] },
    { productPublicId: 'assembled', stock: [{ warehousePublicId: 'w-1', available: 7 }] },
    ...[1, 2, 3, 4, 5].map((i) => ({ productPublicId: `p-${i}`, stock: [{ warehousePublicId: 'w-1', available: i === 3 ? 60 : i * 25 }] })),
  ],
  kits: [
    { kitPublicId: 'kit', components: [{ productPublicId: 'strips', sku: '52800', name: 'Tirillas', qtyPerKit: 6 }, { productPublicId: 'monitor', sku: '070120', name: 'Monitor', qtyPerKit: 1 }] },
    { kitPublicId: 'big', components: [1, 2, 3, 4, 5].map(piece) },
  ],
}

const show = (props: Partial<Parameters<typeof StockInfo>[0]> & { productPublicId: string }) =>
  render(
    <WizardProviders>
      <StockInfo stock={STOCK} warehousePublicId="w-1" quantity="" {...props} />
    </WizardProviders>,
  )

describe('StockInfo — desglose del kit', () => {
  beforeAll(() => setLang('es'))

  it('el kit de oferta dice cuántos se pueden armar y lista sus piezas, con la que limita marcada', () => {
    show({ productPublicId: 'kit' })
    expect(screen.getByText('Se pueden armar: 100')).toBeInTheDocument()
    expect(screen.queryByText(/^Disponible:/)).toBeNull()
    const rows = within(screen.getByRole('list', { name: 'Piezas del kit y su disponible' })).getAllByRole('listitem')
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent('52800 Tirillas × 6')
    expect(rows[0]).toHaveTextContent('600 disp.')
    expect(rows[0]).toHaveTextContent('Limita')
    expect(rows[0]).toHaveClass('limit')
    expect(rows[1]).toHaveTextContent('070120 Monitor × 1')
    expect(rows[1]).toHaveTextContent('500 disp.')
    expect(rows[1]).not.toHaveTextContent('Limita')
    // la nota de otros almacenes sigue (en Ponce se arma 1)
    expect(screen.getByText('Hay disponible en Ponce: 1')).toBeInTheDocument()
  })

  it('pedir más kits de los que se pueden armar sigue marcando el backorder', () => {
    show({ productPublicId: 'kit', quantity: '130' })
    expect(screen.getByText('Backorder: 30')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Solo hay 100 disponibles en Bayamón; 30 quedan en backorder.')
  })

  it('con más de 3 piezas el desglose va plegado y el resumen dice cuál limita', () => {
    const { container } = show({ productPublicId: 'big' })
    expect(screen.getByText('Se pueden armar: 20')).toBeInTheDocument()
    const fold = container.querySelector('details.sales-kitstock-more')!
    expect(fold).not.toHaveAttribute('open')
    expect(fold.querySelector('summary')).toHaveTextContent('Ver piezas (5) · limita A03')
    expect(fold.querySelectorAll('li')).toHaveLength(5)
    expect(fold.querySelectorAll('li.limit')).toHaveLength(1)
  })

  it('con una pieza sustituida en la orden, el desglose usa la pieza real', () => {
    show({ productPublicId: 'kit', pieces: [{ productPublicId: 'other', sku: '52900', name: 'Tirillas alternas', quantity: '6' }, { productPublicId: 'monitor', sku: '070120', name: 'Monitor', quantity: '1' }] })
    expect(screen.getByText('Se pueden armar: 6')).toBeInTheDocument()
    const rows = screen.getAllByRole('listitem')
    expect(rows[0]).toHaveTextContent('52900 Tirillas alternas × 6')
    expect(rows[0]).toHaveTextContent('40 disp.')
    expect(rows[0]).toHaveTextContent('Limita')
    expect(screen.queryByText(/52800/)).toBeNull()
  })

  it('un kit armado (con existencia propia) no se desglosa', () => {
    show({ productPublicId: 'assembled' })
    expect(screen.getByText('Disponible: 7')).toBeInTheDocument()
    expect(screen.queryByRole('list')).toBeNull()
  })

  it('el resumen de la entrega (solo backorder) no pinta el desglose', () => {
    show({ productPublicId: 'kit', quantity: '130', onlyBackorder: true })
    expect(screen.getByText('Backorder: 30')).toBeInTheDocument()
    expect(screen.queryByRole('list')).toBeNull()
    expect(screen.queryByText(/Se pueden armar/)).toBeNull()
  })

  it('en inglés', () => {
    setLang('en')
    show({ productPublicId: 'kit' })
    expect(screen.getByText('Can build: 100')).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')[0]).toHaveTextContent('600 avail.')
    expect(screen.getAllByRole('listitem')[0]).toHaveTextContent('Limiting')
    setLang('es')
  })
})
