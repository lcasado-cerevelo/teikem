// Textos del editor de plantillas según el tipo: título y ayuda nombran lo que se importa (no «consignatario»/«paquete» fuera de envíos).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ImportTemplateEditorModal } from './ImportTemplateEditorModal'

const KINDS = ['PRODUCT', 'SUPPLIER', 'WAREHOUSE', 'BIN', 'PURCHASE_ORDER'] as const
const NOUN_ES = { PRODUCT: 'productos', SUPPLIER: 'proveedores', WAREHOUSE: 'almacenes', BIN: 'posiciones', PURCHASE_ORDER: 'órdenes de compra' }
const NOUN_EN = { PRODUCT: 'products', SUPPLIER: 'suppliers', WAREHOUSE: 'warehouses', BIN: 'bins', PURCHASE_ORDER: 'purchase orders' }

function open(kind: (typeof KINDS)[number] | 'ORDER') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ImportTemplateEditorModal kind={kind} template={null} fields={[{ field: 'sku', required: true, defaultAllowed: false }]} fieldLabel={(f) => f} onClose={() => {}} onSaved={() => {}} />
    </QueryClientProvider>,
  )
}

describe('Editor de plantillas: textos por tipo', () => {
  beforeEach(() => setLang('es'))

  it.each(KINDS)('%s en español: título y ayuda propios, sin consignatario/paquete', (kind) => {
    open(kind)
    expect(screen.getByRole('dialog', { name: `Nueva plantilla de importación de ${NOUN_ES[kind]}` })).toBeInTheDocument()
    const text = document.body.textContent ?? ''
    expect(text).not.toMatch(/consignatario|paquete|envíos/i)
  })

  it.each(KINDS)('%s en inglés', (kind) => {
    setLang('en')
    open(kind)
    expect(screen.getByRole('dialog', { name: `New ${NOUN_EN[kind]} import template` })).toBeInTheDocument()
    expect(document.body.textContent ?? '').not.toMatch(/consignee|package|shipment/i)
  })

  it('órdenes (envíos) conserva sus textos', () => {
    open('ORDER')
    expect(screen.getByRole('dialog', { name: 'Nueva plantilla de importación de envíos' })).toBeInTheDocument()
    expect(document.body.textContent).toMatch(/consignatario/)
  })
})
