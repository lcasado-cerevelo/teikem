import { describe, expect, it } from 'vitest'
import { parseServiceTypeMarks, serviceTypeMarksJson } from './serviceTypeMarks'

describe('serviceTypeMarks', () => {
  it('sin marcas: no es pick up y sí es facturable', () => {
    for (const j of [null, undefined, '', '{}', 'no es json', '[1]', '{"pickUp":"si"}']) expect(parseServiceTypeMarks(j)).toEqual({ pickUp: false, billable: true })
  })
  it('lee las marcas sin distinguir mayúsculas', () => {
    expect(parseServiceTypeMarks('{"pickUp":true,"billable":false}')).toEqual({ pickUp: true, billable: false })
    expect(parseServiceTypeMarks('{"PICKUP":true}')).toEqual({ pickUp: true, billable: true })
  })
  it('escribe el JSON que lee el servidor', () => {
    expect(JSON.parse(serviceTypeMarksJson({ pickUp: true, billable: false }))).toEqual({ pickUp: true, billable: false })
  })
})
