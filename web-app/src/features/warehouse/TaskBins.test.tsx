// «De → a» de una tarea con acomodo repartido (2026-10-07): una posición destino guardada, pero el detalle real son los movimientos del ledger.
import { render, screen } from '@testing-library/react'
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import type { WarehouseTaskDto } from './api'
import { TaskBins } from './TaskBins'
import { txnTypeTone } from './kardexView'

const task = (over: Partial<WarehouseTaskDto>): WarehouseTaskDto => ({ id: 1, fromBinCode: 'R1', toBinCode: '01-E-09', moves: [], ...over }) as WarehouseTaskDto
const mv = (to: string, quantity: number) => ({ transactionId: 1, fromBinCode: 'R1', toBinCode: to, quantity, lotNumber: null, atUtc: '2026-10-07T22:46:00Z' })

describe('TaskBins', () => {
  beforeAll(async () => {
    await setLang('es')
  })

  it('un acomodo repartido dice «R1 → 5 posiciones» y lista cada posición con su cantidad', () => {
    const moves = ['01-E-03', '01-E-04', '01-E-05', '01-E-06', '01-E-09'].map((b) => mv(b, 20))
    render(<TaskBins task={task({ moves })} />)
    expect(screen.getByText(/R1 → 5 posiciones/)).toBeInTheDocument()
    expect(screen.getByText('01-E-03 · 20, 01-E-04 · 20, 01-E-05 · 20, 01-E-06 · 20, 01-E-09 · 20')).toBeInTheDocument()
  })

  it('con una sola posición, o sin movimientos, se ve «de → a» como siempre', () => {
    const { unmount } = render(<TaskBins task={task({ moves: [mv('01-E-04', 100)], toBinCode: '01-E-09' })} />)
    expect(screen.getByText('R1 → 01-E-04')).toBeInTheDocument()
    unmount()
    render(<TaskBins task={task({ moves: [], toBinCode: '09-A-01' })} />)
    expect(screen.getByText('R1 → 09-A-01')).toBeInTheDocument()
  })

  it('el tipo Acomodo tiene su propio color, distinto del de Transferencia', () => {
    expect(txnTypeTone('PUTAWAY')).not.toBe('neutral')
    expect(txnTypeTone('PUTAWAY')).not.toBe(txnTypeTone('TRANSFER'))
  })
})
