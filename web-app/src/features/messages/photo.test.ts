// Lote 212 — reglas puras de las fotos del chat.
import { describe, expect, it } from 'vitest'
import { fitSize, isAcceptedImage, MAX_PHOTOS, roomLeft } from './photo'

describe('fotos del chat', () => {
  it('reduce al lado mayor permitido sin agrandar', () => {
    expect(fitSize(4000, 3000)).toEqual({ width: 1280, height: 960 })
    expect(fitSize(3000, 4000)).toEqual({ width: 960, height: 1280 })
    expect(fitSize(800, 600)).toEqual({ width: 800, height: 600 })
    expect(fitSize(0, 0)).toEqual({ width: 0, height: 0 })
  })

  it('cuántas fotos caben todavía', () => {
    expect(roomLeft(0)).toBe(MAX_PHOTOS)
    expect(roomLeft(3)).toBe(1)
    expect(roomLeft(9)).toBe(0)
  })

  it('solo JPG, PNG y WEBP', () => {
    expect(isAcceptedImage({ type: 'image/jpeg' })).toBe(true)
    expect(isAcceptedImage({ type: 'IMAGE/PNG' })).toBe(true)
    expect(isAcceptedImage({ type: 'application/pdf' })).toBe(false)
  })
})

describe('foto bajada del servidor', () => {
  it('se vuelve un data URL con su tipo', async () => {
    const { blobToDataUrl } = await import('./photo')
    expect(await blobToDataUrl(new Blob([new Uint8Array([65, 66, 67])], { type: 'image/png' }))).toBe('data:image/png;base64,QUJD')
    expect(await blobToDataUrl(new Blob([new Uint8Array(0x20000)]))).toMatch(/^data:image\/jpeg;base64,/)   // grande: no revienta la pila
  })
})
