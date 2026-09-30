// Lote 13 — estado de la lista maestra de Recibo (pestañas Recibos y 'Acomodo pendiente'): filtros, buscador libre (con
// pausa, va al API como `search`), página y filas por página (paginación del servidor). Cualquier cambio de filtro o de
// búsqueda vuelve a la página 1. La consulta sale de `receiptListQuery` (receiptFilters.ts).
import { useCallback, useMemo, useState } from 'react'
import { exportReceipts, useReceipts } from './api'
import { useDebounced } from './lineRules'
import { EMPTY_RECEIPT_FILTERS, receiptListQuery, type ReceiptFilterState, type ReceiptPhase } from './receiptFilters'

export const RECEIPT_PAGE_SIZE = 25

export function useReceiptList(phase?: ReceiptPhase) {
  const [filters, setFiltersState] = useState<ReceiptFilterState>(EMPTY_RECEIPT_FILTERS)
  const [q, setQState] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSizeState] = useState(RECEIPT_PAGE_SIZE)
  const search = useDebounced(q.trim())

  const setFilters = useCallback((next: ReceiptFilterState) => {
    setPage(1)
    setFiltersState(next)
  }, [])
  const setQ = useCallback((v: string) => {
    setPage(1)
    setQState(v)
  }, [])
  const setPageSize = useCallback((n: number) => {
    setPageSizeState(n)
    setPage(1)
  }, [])

  const query = useMemo(() => receiptListQuery(filters, { phase, search, page, pageSize }), [filters, phase, search, page, pageSize])
  const list = useReceipts(query)
  const exportRows = useCallback(() => exportReceipts(query), [query])

  return { filters, setFilters, q, setQ, page, setPage, pageSize, setPageSize, query, list, exportRows }
}
