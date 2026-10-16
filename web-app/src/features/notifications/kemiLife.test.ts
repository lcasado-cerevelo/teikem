// Kemi con vida: los tiempos al azar, qué gesto toca y dónde queda al arrastrarla.
import { afterEach, describe, expect, it } from 'vitest'
import { BIG_MOODS, BLINK_EVERY, anchorStyle, DEFAULT_POS, MICRO_MOODS, between, clampPos, isMoved, pickMood, pupilOffset, quadrant, readPos, writePos } from './kemiLife'

afterEach(() => localStorage.clear())

describe('esperas al azar', () => {
  it('siempre caen dentro del rango, nunca un intervalo fijo', () => {
    expect(between(() => 0, BLINK_EVERY)).toBe(2500)
    expect(between(() => 0.999999, BLINK_EVERY)).toBe(6000)
    const seen = new Set([0.1, 0.4, 0.7].map((r) => between(() => r, BLINK_EVERY)))
    expect(seen.size).toBe(3)
  })

  it('un gesto nunca se repite dos veces seguidas', () => {
    for (const list of [MICRO_MOODS, BIG_MOODS]) {
      for (const last of list) {
        for (const r of [0, 0.3, 0.6, 0.99]) expect(pickMood(() => r, list, last)).not.toBe(last)
      }
    }
    expect(pickMood(() => 0.5, ['happy'], 'happy')).toBe('happy')   // con uno solo no hay a dónde ir
    expect(pickMood(() => 0.99, MICRO_MOODS, null)).toBe(MICRO_MOODS[MICRO_MOODS.length - 1])
  })
})

describe('pupilas', () => {
  it('siguen al puntero con un tope y se quedan quietas en el centro', () => {
    expect(pupilOffset(0, 0)).toEqual({ x: 0, y: 0 })
    expect(pupilOffset(1000, 0)).toEqual({ x: 4, y: 0 })
    expect(pupilOffset(0, -1000)).toEqual({ x: 0, y: -4 })
    const near = pupilOffset(10, 0)
    expect(near.x).toBeGreaterThan(0)
    expect(near.x).toBeLessThan(4)
  })
})

describe('posición de Kemi', () => {
  it('se mantiene dentro de la ventana', () => {
    expect(clampPos({ right: -50, bottom: 5000 }, 1000, 800)).toEqual({ right: 8, bottom: 734 })
    expect(clampPos({ right: 300, bottom: 200 }, 1000, 800)).toEqual({ right: 300, bottom: 200 })
    expect(clampPos({ right: 24, bottom: 24 }, 30, 30)).toEqual({ right: 8, bottom: 8 })   // ventana minúscula
  })

  it('abre el globo y el panel hacia donde hay lugar', () => {
    expect(quadrant(DEFAULT_POS, 1000, 800)).toEqual({ v: 'bottom', h: 'right' })
    expect(quadrant({ right: 900, bottom: 700 }, 1000, 800)).toEqual({ v: 'top', h: 'left' })
  })

  it('se ancla por el borde más cercano para no correrse al abrir el panel', () => {
    expect(anchorStyle({ right: 40, bottom: 60 }, 1000, 800)).toEqual({ top: 'auto', bottom: 60, left: 'auto', right: 40 })
    expect(anchorStyle({ right: 900, bottom: 700 }, 1000, 800)).toEqual({ top: 42, bottom: 'auto', left: 42, right: 'auto' })
  })

  it('se guarda por usuario y la esquina de siempre no deja rastro', () => {
    expect(isMoved(DEFAULT_POS)).toBe(false)
    expect(readPos(7)).toEqual(DEFAULT_POS)
    writePos(7, { right: 120, bottom: 300 })
    expect(readPos(7)).toEqual({ right: 120, bottom: 300 })
    expect(readPos(8)).toEqual(DEFAULT_POS)
    writePos(7, DEFAULT_POS)
    expect(localStorage.getItem('teikem.kemi.pos.7')).toBeNull()
  })

  it('con datos dañados vuelve a su esquina', () => {
    localStorage.setItem('teikem.kemi.pos.7', '{no es json')
    expect(readPos(7)).toEqual(DEFAULT_POS)
    localStorage.setItem('teikem.kemi.pos.7', JSON.stringify({ right: 'x', bottom: 1 }))
    expect(readPos(7)).toEqual(DEFAULT_POS)
  })
})
