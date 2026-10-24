// Lo que el portal recuerda de Kemi: una posición dañada no rompe nada.
import { DEFAULT_POS } from '../../../../shared/kemi/rules'
import { parsePos } from './kemiPrefs'

describe('parsePos', () => {
  it('lee una posición guardada', () => {
    expect(parsePos('{"right":120,"bottom":300}')).toEqual({ right: 120, bottom: 300 })
  })
  it('sin nada o con datos dañados vuelve a la esquina de siempre', () => {
    expect(parsePos(null)).toEqual(DEFAULT_POS)
    expect(parsePos('no es json')).toEqual(DEFAULT_POS)
    expect(parsePos('{"right":"a","bottom":1}')).toEqual(DEFAULT_POS)
    expect(parsePos('{"right":1}')).toEqual(DEFAULT_POS)
  })
})
