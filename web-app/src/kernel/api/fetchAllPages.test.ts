import { describe, expect, it, vi } from 'vitest'
import { EXPORT_MAX_ROWS, EXPORT_PAGE_SIZE, fetchAllPages } from './fetchAllPages'

/** Servidor falso con `total` filas (0…total-1) que respeta skip/take (y un tope propio de take). */
function server(total: number, withTotal = true, cap = 200) {
  return vi.fn((skip: number, take: number) => {
    const n = Math.max(0, Math.min(take, cap, total - skip))
    const items = Array.from({ length: n }, (_, i) => skip + i)
    return Promise.resolve(withTotal ? { items, total } : { items })
  })
}

describe('fetchAllPages', () => {
  it('recorre skip/take de a 200 hasta el total', async () => {
    const fetchPage = server(450)
    const { items, truncated } = await fetchAllPages(fetchPage)
    expect(items).toHaveLength(450)
    expect(items[449]).toBe(449)
    expect(truncated).toBe(false)
    expect(fetchPage.mock.calls).toEqual([
      [0, EXPORT_PAGE_SIZE],
      [200, EXPORT_PAGE_SIZE],
      [400, EXPORT_PAGE_SIZE],
    ])
  })

  it('un total múltiplo exacto del tamaño de página no pide una página vacía de más', async () => {
    const fetchPage = server(400)
    const { items, truncated } = await fetchAllPages(fetchPage)
    expect(items).toHaveLength(400)
    expect(truncated).toBe(false)
    expect(fetchPage).toHaveBeenCalledTimes(2)
  })

  it('sin total se detiene en la primera página incompleta', async () => {
    const fetchPage = server(250, false)
    const { items, truncated } = await fetchAllPages(fetchPage)
    expect(items).toHaveLength(250)
    expect(truncated).toBe(false)
  })

  it('corta en el tope de lectura (100 000) y marca truncated', async () => {
    const fetchPage = server(EXPORT_MAX_ROWS + 345)
    const { items, truncated } = await fetchAllPages(fetchPage)
    expect(items).toHaveLength(EXPORT_MAX_ROWS)
    expect(truncated).toBe(true)
    expect(fetchPage).toHaveBeenCalledTimes(EXPORT_MAX_ROWS / EXPORT_PAGE_SIZE)
  })

  it('respeta pageSize y max propios; la última página pide solo lo que falta', async () => {
    const fetchPage = server(100)
    const { items, truncated } = await fetchAllPages(fetchPage, { pageSize: 30, max: 50 })
    expect(items).toHaveLength(50)
    expect(truncated).toBe(true)
    expect(fetchPage.mock.calls).toEqual([
      [0, 30],
      [30, 20],
    ])
  })

  it('con total conocido sigue aunque el API devuelva menos de lo pedido (tope propio de take)', async () => {
    const fetchPage = server(250, true, 100)
    const { items, truncated } = await fetchAllPages(fetchPage)
    expect(items).toHaveLength(250)
    expect(truncated).toBe(false)
    expect(fetchPage.mock.calls.map((c) => c[0])).toEqual([0, 100, 200])
  })

  it('sin filas devuelve vacío sin truncar; un error se propaga', async () => {
    await expect(fetchAllPages(server(0))).resolves.toEqual({ items: [], truncated: false })
    await expect(fetchAllPages(() => Promise.reject(new Error('500')))).rejects.toThrow('500')
  })
})
