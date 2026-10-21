import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { Link, MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { RemountOnSearch } from './RemountOnSearch'

// Un contador que arranca en 0: si se vuelve a montar, vuelve a 0.
function Counter() {
  const [n, setN] = useState(0)
  return <button type="button" onClick={() => setN(n + 1)}>veces {n}</button>
}

describe('RemountOnSearch', () => {
  it('vuelve a montar cuando cambia el ?q= de la dirección y no cuando no cambia', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter initialEntries={['/lista?q=A']}>
        <Link to="/lista?q=A">igual</Link>
        <Link to="/lista?q=B">otro</Link>
        <RemountOnSearch>
          <Counter />
        </RemountOnSearch>
      </MemoryRouter>,
    )
    await user.click(screen.getByRole('button', { name: 'veces 0' }))
    expect(screen.getByRole('button', { name: 'veces 1' })).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'igual' }))
    expect(screen.getByRole('button', { name: 'veces 1' })).toBeInTheDocument()   // misma dirección: no se toca
    await user.click(screen.getByRole('link', { name: 'otro' }))
    expect(screen.getByRole('button', { name: 'veces 0' })).toBeInTheDocument()   // otra dirección: de nuevo desde cero
  })
})
