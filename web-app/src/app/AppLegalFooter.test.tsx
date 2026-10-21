import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../kernel/i18n/i18n'
import { AppLegalFooter, LEGAL_PRIVACY_PATH, LEGAL_TERMS_PATH } from './AppLegalFooter'

describe('AppLegalFooter', () => {
  beforeAll(() => setLang('es'))

  it('enlaces a privacidad y términos en pestaña nueva, derechos reservados del dueño y Powered by Teikem', () => {
    render(
      <MemoryRouter>
        <AppLegalFooter year={2026} />
      </MemoryRouter>,
    )
    const priv = screen.getByRole('link', { name: 'Privacidad' })
    const terms = screen.getByRole('link', { name: 'Términos de uso' })
    expect(priv).toHaveAttribute('href', LEGAL_PRIVACY_PATH)
    expect(terms).toHaveAttribute('href', LEGAL_TERMS_PATH)
    for (const a of [priv, terms]) {
      expect(a).toHaveAttribute('target', '_blank')
      expect(a).toHaveAttribute('rel', expect.stringContaining('noopener'))
    }
    expect(screen.getByText('© 2026 Cerevelo LLC.')).toBeInTheDocument()
    expect(screen.getByText('Powered by Teikem')).toBeInTheDocument()
  })

  it('en inglés', () => {
    setLang('en')
    render(
      <MemoryRouter>
        <AppLegalFooter year={2026} />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'Privacy' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Terms of use' })).toBeInTheDocument()
    expect(screen.getByText('© 2026 Cerevelo LLC.')).toBeInTheDocument()
    expect(screen.getByText('All rights reserved.')).toBeInTheDocument()   // en su propia línea
    setLang('es')
  })
})
