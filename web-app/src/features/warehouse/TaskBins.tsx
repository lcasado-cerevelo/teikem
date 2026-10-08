import { useLang, useT } from '../../kernel/i18n'
import type { WarehouseTaskDto } from './api'
import { formatNumber } from './lineRules'

/**
 * «De → a» de una tarea (2026-10-07). La tarea guarda una sola posición destino, pero un acomodo repartido movió el inventario a varias: el detalle real son
 * los movimientos del ledger (`moves`, una línea por posición). Con varias posiciones se ve «R1 → 5 posiciones» y debajo cada una con su cantidad.
 */
export function TaskBins({ task }: { task: WarehouseTaskDto }) {
  const t = useT()
  const lang = useLang()
  const moves = task.moves ?? []
  const from = task.fromBinCode ?? moves[0]?.fromBinCode ?? null
  if (moves.length > 1) {
    const detail = moves.map((m) => `${m.toBinCode ?? '—'} · ${formatNumber(m.quantity, lang)}`).join(', ')
    return (
      <span title={detail}>
        <span>
          {from ?? '—'} → {t('warehouse.receipts.detail.binsMany', { count: moves.length })}
        </span>
        <small className="task-moves">{detail}</small>
      </span>
    )
  }
  const to = moves.length === 1 ? moves[0].toBinCode : task.toBinCode
  return <>{[from, to].filter(Boolean).join(' → ') || '—'}</>
}

/** Valor para ordenar/exportar la columna «De → a». */
export function taskBinsSort(task: WarehouseTaskDto): string | null {
  return task.fromBinCode ?? task.toBinCode ?? null
}
