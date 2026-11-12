// Lote 240: la foto se amplía con un CLIC (no con el mouse encima), ahí mismo, y se cierra con la X, con un clic fuera o con Esc.
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { ZoomPhoto } from './ZoomPhoto'

describe('ZoomPhoto', () => {
  beforeEach(() => setLang('es'))

  it('pasar el mouse no la amplía; el clic sí, y Esc la cierra', () => {
    render(<ZoomPhoto src="blob:foto" alt="Guantes" className="pl-rowphoto" />)
    const thumb = screen.getByRole('button', { name: 'Ampliar la foto de Guantes' })
    fireEvent.mouseEnter(thumb)
    fireEvent.mouseOver(thumb)
    expect(screen.queryByRole('dialog')).toBeNull()

    fireEvent.click(thumb)
    const dialog = screen.getByRole('dialog', { name: 'Guantes' })
    expect(dialog.querySelector('img')?.getAttribute('src')).toBe('blob:foto')

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('un clic fuera de la foto grande o en la X la cierra, y el clic no llega a la fila de abajo', () => {
    let rowClicks = 0
    render(
      <div onClick={() => rowClicks++}>
        <ZoomPhoto src="blob:foto" alt="Guantes" />
      </div>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Ampliar la foto de Guantes' }))
    expect(rowClicks).toBe(0)
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar' }))
    expect(screen.queryByRole('dialog')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Ampliar la foto de Guantes' }))
    fireEvent.click(screen.getByRole('dialog'))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(rowClicks).toBe(0)
  })
})
