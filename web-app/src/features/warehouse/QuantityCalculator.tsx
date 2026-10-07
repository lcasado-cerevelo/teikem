// Calculadora de cantidad del conteo cíclico (pedido del dueño 2026-10-05 en la app; 2026-10-07 también en la web): para contar estibas sin sumar de
// cabeza. Cada BLOQUE es filas × columnas × fondo (cuántas hay una detrás de otra; en blanco = 1) y se suma lo SUELTO. Solo el total sale de aquí:
// lo que se guarda es la cantidad, no la fórmula. Controlada: quien la usa guarda el CalcState y decide qué hacer con el total (`quantityCalc.ts`).
import { useId } from 'react'
import { useT } from '../../kernel/i18n'
import { addBlock, calcTotal, removeBlock, setBlock, totalToText, type CalcState } from './quantityCalc'

export interface QuantityCalculatorProps {
  state: CalcState
  onChange: (next: CalcState) => void
  /** Se llama con Enter en cualquier campo (por ejemplo, para usar el total). */
  onEnter?: () => void
}

export function QuantityCalculator({ state, onChange, onEnter }: QuantityCalculatorProps) {
  const t = useT()
  const id = useId()
  const result = calcTotal(state)
  const issueText =
    result.issue === 'incompleteBlock'
      ? t('warehouse.cycleCounts.calc.incompleteBlock')
      : result.issue === 'invalid'
        ? t('warehouse.cycleCounts.calc.invalid')
        : result.issue === 'tooLarge'
          ? t('warehouse.cycleCounts.calc.tooLarge')
          : null
  const enter = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' && onEnter) {
      e.preventDefault()
      onEnter()
    }
  }
  return (
    <div className="qcalc" data-testid="quantity-calculator">
      {state.blocks.map((b, i) => (
        <div key={i} className="qcalc-block">
          <label className="qcalc-cell">
            <span>{t('warehouse.cycleCounts.calc.rows')}</span>
            <input
              type="text"
              inputMode="numeric"
              autoComplete="off"
              aria-label={t('warehouse.cycleCounts.calc.rowsAt', { n: i + 1 })}
              value={b.rows}
              autoFocus={i === 0}
              onChange={(e) => onChange(setBlock(state, i, { rows: e.target.value }))}
              onKeyDown={enter}
              onFocus={(e) => e.currentTarget.select()}
            />
          </label>
          <span className="qcalc-times" aria-hidden="true">
            ×
          </span>
          <label className="qcalc-cell">
            <span>{t('warehouse.cycleCounts.calc.cols')}</span>
            <input
              type="text"
              inputMode="numeric"
              autoComplete="off"
              aria-label={t('warehouse.cycleCounts.calc.colsAt', { n: i + 1 })}
              value={b.cols}
              onChange={(e) => onChange(setBlock(state, i, { cols: e.target.value }))}
              onKeyDown={enter}
              onFocus={(e) => e.currentTarget.select()}
            />
          </label>
          <span className="qcalc-times" aria-hidden="true">
            ×
          </span>
          <label className="qcalc-cell">
            <span>{t('warehouse.cycleCounts.calc.depth')}</span>
            <input
              type="text"
              inputMode="numeric"
              autoComplete="off"
              placeholder="1"
              aria-label={t('warehouse.cycleCounts.calc.depthAt', { n: i + 1 })}
              aria-describedby={`${id}-depth`}
              value={b.depth}
              onChange={(e) => onChange(setBlock(state, i, { depth: e.target.value }))}
              onKeyDown={enter}
              onFocus={(e) => e.currentTarget.select()}
            />
          </label>
          {state.blocks.length > 1 && (
            <button type="button" className="btn sm qcalc-remove" aria-label={t('warehouse.cycleCounts.calc.removeBlock', { n: i + 1 })} onClick={() => onChange(removeBlock(state, i))}>
              ✕
            </button>
          )}
        </div>
      ))}
      <p className="help" id={`${id}-depth`}>
        {t('warehouse.cycleCounts.calc.depthHelp')}
      </p>
      <button type="button" className="btn sm qcalc-add" onClick={() => onChange(addBlock(state))}>
        {t('warehouse.cycleCounts.calc.addBlock')}
      </button>
      <label className="qcalc-extra">
        <span>{t('warehouse.cycleCounts.calc.extra')}</span>
        <input
          type="text"
          inputMode="decimal"
          autoComplete="off"
          aria-label={t('warehouse.cycleCounts.calc.extra')}
          value={state.extra}
          onChange={(e) => onChange({ ...state, extra: e.target.value })}
          onKeyDown={enter}
          onFocus={(e) => e.currentTarget.select()}
        />
      </label>
      <div className="qcalc-sum" role="status" aria-live="polite">
        {result.expression && <span className="qcalc-expr">{result.expression}</span>}
        {issueText ? (
          <span className="ferr">{issueText}</span>
        ) : result.total !== null ? (
          <strong className="qcalc-total" data-testid="quantity-calculator-total">
            {t('warehouse.cycleCounts.calc.total', { total: totalToText(result.total) })}
          </strong>
        ) : (
          <span className="help">{t('warehouse.cycleCounts.calc.hint')}</span>
        )}
      </div>
    </div>
  )
}
