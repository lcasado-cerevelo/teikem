import { mineFirst } from './dispatchTake'

describe('mineFirst', () => {
  it('pone primero lo mío, luego lo sin asignar y al final lo de otras personas, sin alterar el orden dentro de cada grupo', () => {
    const list = [
      { n: 'a', assignedToUserId: 9 },
      { n: 'b', assignedToUserId: null },
      { n: 'c', assignedToUserId: 7 },
      { n: 'd', assignedToUserId: 7 },
      { n: 'e' },
    ]
    expect(mineFirst(list, 7).map((x) => x.n)).toEqual(['c', 'd', 'b', 'e', 'a'])
  })
})
