// Editor del reparto por posición (web): cantidad por posición + elegir las posiciones una por una; cada renglón muestra lo que recibirá y se puede quitar
// (✕); la posición que recibe solo el resto (la décima de 185 de 20) lleva una alerta FIJA (se queda a la vista aunque se desplace el cuadro) que se cierra;
// una posición con cupo que no alcanza avisa SIN bloquear. Controlado: quien lo usa guarda `perBinText` y `bins` y manda el reparto (`splitPlan.ts`).
import { useState } from 'react'
import { useT } from '../../kernel/i18n'
import { formatNumber } from './lineRules'
import { useLang } from '../../kernel/i18n'
import { BinPicker } from './pickers'
import { addSplitBin, chunkAt, exceedsCapacity, freeQtyOf, isRestBin, parsePerBin, splitSummary, type SplitBin } from './splitPlan'

export interface SplitBinsEditorProps {
  /** Cantidad total a repartir (lo pendiente de la tarea o lo recibido de la línea). */
  total: number
  perBinText: string
  onPerBinChange: (text: string) => void
  bins: readonly SplitBin[]
  onBinsChange: (bins: SplitBin[]) => void
  warehousePublicId: string | null | undefined
  /** Zonas que no pueden recibir (p. ej. recepción y cruce de muelle en un recibo directo). */
  excludeZoneTypeCodes?: readonly string[]
  suggestedBinIds?: readonly (number | null | undefined)[]
}

export function SplitBinsEditor({ total, perBinText, onPerBinChange, bins, onBinsChange, warehousePublicId, excludeZoneTypeCodes, suggestedBinIds }: SplitBinsEditorProps) {
  const t = useT()
  const lang = useLang()
  const fmt = (n: number) => formatNumber(n, lang)
  const perBin = parsePerBin(perBinText)
  const [pickerKey, setPickerKey] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [closedAlertBin, setClosedAlertBin] = useState<number | null>(null)

  const restAt = perBin > 0 ? bins.findIndex((_, i) => isRestBin(total, perBin, i)) : -1
  const restBin = restAt >= 0 ? bins[restAt] : null
  const summary = splitSummary(total, perBin, bins.length)

  return (
    <div className="split">
      {restBin && closedAlertBin !== restBin.id && (
        <div className="split-alert" role="alert">
          <span>{t('warehouse.split.restAlert', { bin: restBin.code, qty: fmt(chunkAt(total, perBin, restAt)), per: fmt(perBin) })}</span>
          <button type="button" className="btn sm" aria-label={t('warehouse.split.closeAlert')} onClick={() => setClosedAlertBin(restBin.id)}>
            ✕
          </button>
        </div>
      )}
      <div className="f">
        <label htmlFor="split-per-bin">{t('warehouse.split.perBinLabel')}</label>
        <input
          id="split-per-bin"
          type="text"
          inputMode="decimal"
          autoComplete="off"
          value={perBinText}
          onChange={(e) => {
            onPerBinChange(e.target.value)
            onBinsChange([])
            setError(null)
          }}
        />
        <p className="help">{t(perBin > 0 ? 'warehouse.split.perBinHelpOn' : 'warehouse.split.perBinHelp', { total: fmt(total) })}</p>
      </div>
      {perBin > 0 && (
        <>
          <div className="f">
            <label>{t('warehouse.split.addBin')}</label>
            <BinPicker
              key={pickerKey}
              warehousePublicId={warehousePublicId}
              value={null}
              excludeZoneTypeCodes={excludeZoneTypeCodes}
              suggestedBinIds={suggestedBinIds}
              aria-label={t('warehouse.split.addBin')}
              onChange={(binId, bin) => {
                if (binId == null || !bin) return
                const added = addSplitBin(bins, { id: binId, code: bin.code ?? String(binId), free: freeQtyOf(bin.maxCapacityQty, bin.qtyOnHand) }, total, perBin)
                if ('bins' in added) {
                  setError(null)
                  onBinsChange(added.bins)
                } else {
                  setError(added.reason === 'duplicate' ? t('warehouse.split.repeated') : t('warehouse.split.noRoom', { total: fmt(total) }))
                }
                setPickerKey((k) => k + 1)
              }}
            />
            {error && (
              <p className="ferr" role="alert">
                {error}
              </p>
            )}
          </div>
          {bins.length > 0 && (
            <>
              <ul className="split-list">
                {bins.map((b, i) => {
                  const qty = chunkAt(total, perBin, i)
                  return (
                    <li key={b.id}>
                      <span className="mono">{t('warehouse.split.line', { bin: b.code, qty: fmt(qty) })}</span>
                      {exceedsCapacity(b.free, qty) && <span className="split-cap">{t('warehouse.split.capacityWarn', { free: fmt(b.free ?? 0), qty: fmt(qty) })}</span>}
                      <button type="button" className="btn sm" aria-label={t('warehouse.split.removeBin', { bin: b.code })} onClick={() => onBinsChange(bins.filter((x) => x.id !== b.id))}>
                        ✕
                      </button>
                    </li>
                  )
                })}
              </ul>
              <p className="help" role="status">
                {t('warehouse.split.summary', { total: fmt(summary.total), left: fmt(summary.left) })}
              </p>
            </>
          )}
        </>
      )}
    </div>
  )
}
