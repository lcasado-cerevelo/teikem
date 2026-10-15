import { buildDispatchBody, buildDispatchLines, buildTasks, checkPickQty, dispatchBatchNumber, nextTask, parseQty, taskKey, totalPicked, type LocalSalesLine } from './salesDispatchLogic'

const LINES: LocalSalesLine[] = [
  {
    id: 10, productPublicId: 'p1', sku: 'MED', productName: 'Medicina', qtyOrdered: 6, qtyReserved: 6, qtyShipped: 0, qtyBackordered: 0,
    reservations: [
      { binId: 1, binCode: 'P-01', lotId: 7, lotNumber: 'L7', quantity: 4 },
      { binId: 2, binCode: 'P-02', lotId: null, lotNumber: null, quantity: 2 },
    ],
  },
  { id: 11, productPublicId: 'p2', sku: 'VIT', productName: 'Vitamina', qtyOrdered: 3, qtyReserved: 0, qtyShipped: 0, qtyBackordered: 3, reservations: [] },
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
