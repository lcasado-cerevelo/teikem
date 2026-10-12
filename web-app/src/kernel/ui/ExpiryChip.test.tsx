// Chip de vencimiento compartido (F-D1; lo reutiliza F-D2): tono y texto por estado, singular/plural, hoy, reemplazado y en inglés.
import { render, screen } from '@testing-library/react'
import { afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { ExpiryChip } from './ExpiryChip'
import { expiryChipInfo } from './expiry'

describe('expiryChipInfo', () => {
  it('vigente, sin vencimiento, por vencer (hoy, 1 día, N días) y vencida (1 día, N días)', () => {
    expect(expiryChipInfo('OK', 120)).toMatchObject({ tone: 'deliv', key: 'ui.expiry.ok' })
    expect(expiryChipInfo('NO_EXPIRY', null)).toMatchObject({ tone: 'cap', key: 'ui.expiry.none' })
    expect(expiryChipInfo('EXPIRING', 0)).toMatchObject({ tone: 'warn', key: 'ui.expiry.today' })
    expect(expiryChipInfo('EXPIRING', 1)).toMatchObject({ tone: 'warn', key: 'ui.expiry.inOne', params: { days: 1 } })
    expect(expiryChipInfo('EXPIRING', 12)).toMatchObject({ tone: 'warn', key: 'ui.expiry.in', params: { days: 12 } })
    expect(expiryChipInfo('EXPIRED', -1)).toMatchObject({ tone: 'fail', key: 'ui.expiry.expiredOne', params: { days: 1 } })
    expect(expiryChipInfo('EXPIRED', -40)).toMatchObject({ tone: 'fail', key: 'ui.expiry.expired', params: { days: 40 } })
  })

  it('un documento reemplazado es neutro aunque esté vencido; un estado desconocido o vacío se trata como sin vencimiento', () => {
    expect(expiryChipInfo('EXPIRED', -40, true)).toMatchObject({ tone: 'cap', key: 'ui.expiry.superseded' })
    expect(expiryChipInfo(undefined, undefined)).toMatchObject({ tone: 'cap', key: 'ui.expiry.none' })
    expect(expiryChipInfo('RARO', 3)).toMatchObject({ key: 'ui.expiry.none' })
    // minúsculas del servidor
    expect(expiryChipInfo('expired', -2)).toMatchObject({ tone: 'fail' })
  })
})

describe('ExpiryChip', () => {
  beforeAll(() => setLang('es'))
  afterEach(() => setLang('es'))

  it('pinta el texto en español con el tono de la maqueta y un title explicativo', () => {
    const { rerender } = render(<ExpiryChip state="EXPIRED" days={-40} />)
    const chip = screen.getByText('Vencida hace 40 días')
    expect(chip).toHaveClass('chip', 's-fail')
    expect(chip).toHaveAttribute('title')
    rerender(<ExpiryChip state="EXPIRING" days={0} />)
    expect(screen.getByText('Vence hoy')).toHaveClass('s-warn')
    rerender(<ExpiryChip state="EXPIRING" days={5} />)
    expect(screen.getByText('Vence en 5 días')).toBeInTheDocument()
    rerender(<ExpiryChip state="OK" days={200} />)
    expect(screen.getByText('Vigente')).toHaveClass('s-deliv')
    rerender(<ExpiryChip state="NO_EXPIRY" />)
    expect(screen.getByText('Sin vencimiento')).toHaveClass('s-cap')
    rerender(<ExpiryChip state="EXPIRED" days={-3} superseded />)
    expect(screen.getByText('Reemplazada')).toHaveClass('s-cap')
  })

  it('singulares y el inglés', () => {
    const { rerender } = render(<ExpiryChip state="EXPIRING" days={1} />)
    expect(screen.getByText('Vence en 1 día')).toBeInTheDocument()
    rerender(<ExpiryChip state="EXPIRED" days={-1} />)
    expect(screen.getByText('Vencida hace 1 día')).toBeInTheDocument()
    setLang('en')
    rerender(<ExpiryChip state="EXPIRED" days={-1} />)
    expect(screen.getByText('Expired 1 day ago')).toBeInTheDocument()
    rerender(<ExpiryChip state="EXPIRING" days={7} />)
    expect(screen.getByText('Expires in 7 days')).toBeInTheDocument()
    rerender(<ExpiryChip state="OK" days={90} />)
    expect(screen.getByText('Valid')).toBeInTheDocument()
  })
})
