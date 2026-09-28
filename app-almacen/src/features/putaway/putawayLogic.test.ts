import { sortTasksMineFirst, type PutawayTask } from './putawayLogic'

function task(id: number, assignedToUserId: number | null): PutawayTask {
  return { id, sku: `SKU-${id}`, productName: `P${id}`, quantity: 1, toBinCode: null, assignedToUserId }
}

describe('sortTasksMineFirst', () => {
  it('pone primero las tareas asignadas a mi usuario, en su orden original', () => {
    const tasks = [task(1, 5), task(2, 7), task(3, 7), task(4, null)]
    expect(sortTasksMineFirst(tasks, 7).map((t) => t.id)).toEqual([2, 3, 1, 4])
  })

  it('sin ninguna mía, conserva el orden original', () => {
    const tasks = [task(1, 5), task(2, 6)]
    expect(sortTasksMineFirst(tasks, 99).map((t) => t.id)).toEqual([1, 2])
  })
})
