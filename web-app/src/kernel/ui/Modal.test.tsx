import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Modal } from './Modal'

describe('Modal', () => {
  it('el clic fuera (o en la barra de desplazamiento) no lo cierra; Escape y ✕ sí', () => {
    const onClose = vi.fn()
    render(
      <Modal open title="Editar rol" onClose={onClose}>
        <p>contenido</p>
      </Modal>,
    )
    const scrim = document.querySelector('.scrim') as HTMLElement
    fireEvent.mouseDown(scrim)
    fireEvent.click(scrim)
    expect(onClose).not.toHaveBeenCalled()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getAllByRole('button')[0]!)
    expect(onClose).toHaveBeenCalledTimes(2)
  })
})
