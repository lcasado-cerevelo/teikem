import { buildDispatchBody, buildDispatchLines, buildTasks, checkPickQty, dispatchBatchNumber, kitsReady, kitSummaries, nextTask, parseQty, taskKey, totalPicked, type LocalSalesLine } from './salesDispatchLogic'

const LINES: LocalSalesLine[] = [
  {
    id: 10, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 6, qtyReserved: 6, qtyShipped: 0, qtyBackordered: 0, isKit: false, components: [],
    reservations: [
      { binId: 1, binCode: 'P-01', lotId: 7, lotNumber: 'L7', quantity: 4, productPublicId: null, sku: null, productName: null },
      { binId: 2, binCode: 'P-02', lotId: null, lotNumber: null, quantity: 2, productPublicId: null, sku: null, productName: null },
    ],
  },
  { id: 11, productPublicId: 'p2', sku: 'VIT', productName: 'Vitamina', qtyOrdered: 3, qtyReserved: 0, qtyShipped: 0, qtyBackordered: 3, isKit: false, components: [], reservations: [] },
]

describe('despachar orden de venta: tareas', () => {
  it('una tarea por reserva, en orden; una línea sin reservas no tiene tarea', () => {
    const tasks = buildTasks(LINES)
    expect(tasks.map((t) => [t.binCode, t.qty, t.lotNumber])).toEqual([['P-01', 4, 'L7'], ['P-02', 2, null]])
    expect(tasks[0].key).toBe(taskKey(10, 1, 7))
  })

  it('la siguiente tarea salta lo cumplido y lo omitido', () => {
    const tasks = buildTasks(LINES)
    expect(nextTask(tasks, [])?.binCode).toBe('P-01')
    expect(nextTask(tasks, [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }])?.binCode).toBe('P-02')
    expect(nextTask(tasks, [], new Set([tasks[0].key, tasks[1].key]))).toBeNull()
  })
})

describe('cantidad', () => {
  const task = buildTasks(LINES)[0]
  it('acepta coma decimal y rechaza vacío, cero y texto', () => {
    expect(parseQty('2,5')).toBe(2.5)
    expect(parseQty('')).toBeNull()
    expect(parseQty('0')).toBeNull()
    expect(parseQty('abc')).toBeNull()
  })
  it('no deja sacar más de lo reservado en la tarea', () => {
    expect(checkPickQty(task, '4')).toBeNull()
    expect(checkPickQty(task, '1')).toBeNull()
    expect(checkPickQty(task, '4.5')).toBe('qtyTooMuch')
    expect(checkPickQty(task, 'x')).toBe('qtyInvalid')
  })
})

describe('cuerpo del despacho', () => {
  const tasks = buildTasks(LINES)
  it('manda posición y lote de lo reservado', () => {
    const picks = [{ taskKey: tasks[0].key, binId: 1, binCode: 'P-01', qty: 4 }]
    expect(buildDispatchLines(tasks, picks)).toEqual([{ lineId: 10, quantity: 4, binId: 1, lotId: 7 }])
    expect(totalPicked(picks)).toBe(4)
  })
  it('de otra posición no manda el lote (el servidor lo decide ahí)', () => {
    const picks = [{ taskKey: tasks[0].key, binId: 9, binCode: 'P-09', qty: 3 }]
    expect(buildDispatchLines(tasks, picks)).toEqual([{ lineId: 10, quantity: 3, binId: 9 }])
  })
  it('sin entrega no lleva delivery; con entrega, servicio y bultos estándar y confirmación', () => {
    const picks = [{ taskKey: tasks[1].key, binId: 2, binCode: 'P-02', qty: 2 }]
    expect(buildDispatchBody(tasks, picks, null).delivery).toBeUndefined()
    expect(buildDispatchBody(tasks, picks, { packages: 3 }).delivery).toEqual({ serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: 3 }], confirmNow: true })
    expect(buildDispatchBody(tasks, picks, { packages: 0 }).delivery?.packages[0].pieces).toBe(1)
  })
  it('el número de la recolección sale de la respuesta', () => {
    expect(dispatchBatchNumber({ pickBatchNumber: 'EMP-00012' })).toBe('EMP-00012')
    expect(dispatchBatchNumber(null)).toBeNull()
    expect(dispatchBatchNumber({})).toBeNull()
  })
})

// ---------------------------------------------------------------- Lote 71: kits
const KIT: LocalSalesLine = {
  id: 20, productPublicId: 'kit', sku: 'KIT-1', productName: 'Kit uno', qtyOrdered: 3, qtyReserved: 3, qtyShipped: 0, qtyBackordered: 0, isKit: true,
  components: [
    { productPublicId: 'a', sku: 'A', name: 'Tornillo', qtyPerKit: 2 },
    { productPublicId: 'b', sku: 'B', name: 'Tuerca', qtyPerKit: 1 },
  ],
  reservations: [
    { binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 4, productPublicId: 'a', sku: 'A', productName: 'Tornillo' },
    { binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 2, productPublicId: 'a', sku: 'A', productName: 'Tornillo' },
    { binId: 2, binCode: 'P-02', lotId: null, lotNumber: null, quantity: 3, productPublicId: 'b', sku: 'B', productName: 'Tuerca' },
  ],
}

describe('kits en el aparato', () => {
  const tasks = buildTasks([KIT])

  it('una tarea por reserva de cada componente, con su kit y las piezas por kit; las claves no chocan aunque compartan posición', () => {
    expect(tasks.map((t) => [t.sku, t.binCode, t.qty, t.kitSku, t.perKit])).toEqual([['A', 'P-01', 6, 'KIT-1', 2], ['B', 'P-02', 3, 'KIT-1', 1]])   // las dos reservas de A en la misma posición son una tarea
    expect(taskKey(20, 1, null, 'a')).not.toBe(taskKey(20, 1, null, 'b'))
    expect(taskKey(20, 1, null)).toBe('20|1|')   // sin componente: igual que antes
  })

  it('los kits completos son el menor cociente de lo recogido entre las piezas por kit', () => {
    const a = tasks[0]
    const b = tasks[1]
    const all = [{ taskKey: a.key, binId: 1, binCode: 'P-01', qty: 4 }, { taskKey: b.key, binId: 2, binCode: 'P-02', qty: 3 }]
    expect(kitsReady(tasks, all, 20)).toBe(2)   // A: 4/2 = 2, B: 3/1 = 3 -> 2 kits
    expect(kitsReady(tasks, [all[0]], 20)).toBe(0)   // sin B no hay kit
    expect(kitsReady(tasks, [{ ...all[0], qty: 3 }, all[1]], 20)).toBe(1)   // 3/2 = 1,5 -> 1
  })

  it('el cuerpo manda UNA línea con los kits completos, sin posición ni lote', () => {
    const a = tasks[0]
    const b = tasks[1]
    const picks = [{ taskKey: a.key, binId: 1, binCode: 'P-01', qty: 4 }, { taskKey: b.key, binId: 2, binCode: 'P-02', qty: 3 }]
    expect(buildDispatchLines(tasks, picks)).toEqual([{ lineId: 20, quantity: 2 }])
    expect(buildDispatchBody(tasks, picks, { packages: 2 }).delivery?.packages).toEqual([{ packageType: 'BOX', pieces: 2 }])
    expect(kitSummaries(tasks, picks)).toEqual([{ lineId: 20, kitSku: 'KIT-1', kits: 2, hasPicks: true }])
    expect(kitSummaries(tasks, [])[0]).toMatchObject({ kits: 0, hasPicks: false })
  })
})

describe('reservas repetidas', () => {
  it('varias reservas de un producto normal en la misma posición y lote suman en una sola tarea', () => {
    const line: LocalSalesLine = {
      id: 1, productPublicId: 'p', sku: 'MED', productName: 'Med', qtyOrdered: 6, qtyReserved: 6, qtyShipped: 0, qtyBackordered: 0, isKit: false, components: [],
      reservations: [
        { binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 4, productPublicId: null, sku: null, productName: null },
        { binId: 1, binCode: 'P-01', lotId: null, lotNumber: null, quantity: 2, productPublicId: null, sku: null, productName: null },
      ],
    }
    expect(buildTasks([line]).map((t) => [t.binCode, t.qty])).toEqual([['P-01', 6]])
  })
})
