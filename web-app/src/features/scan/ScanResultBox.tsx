// Lote F-E1 — Resultado grande del último escaneo (con `aria-live`): el mensaje EXACTO del servidor, el tipo de resultado y, cuando
// la orden no quedó asignada, qué hacer (lenguaje claro, manual 05 §7). Un error del código o de la red se pinta igual, en rojo.
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { IconAlert, IconCheck, IconClose } from '../../kernel/ui/icons'
import { hintKey, outcomeInfo, type ScanResult } from './scanRules'

export type LastScan =
  | { kind: 'result'; result: ScanResult; ref: string }
  | { kind: 'error'; message: string }

const ICON = {
  ok: <IconCheck />,
  dup: <IconCheck />,
  unassigned: <IconAlert />,
  ineligible: <IconAlert />,
  beyond: <IconAlert />,
  notfound: <IconClose />,
} as const

export function ScanResultBox({ last, busy, onCloseCount, closing }: { last: LastScan | null; busy: number; onCloseCount?: (orderPublicId: string) => void; closing?: boolean }) {
  const t = useT()
  let tone: keyof typeof ICON | 'idle' = 'idle'
  let body: React.ReactNode = <p className="scn-res-msg">{t('scan.result.ready')}</p>

  if (last?.kind === 'error') {
    tone = 'notfound'
    body = (
      <div className="scn-res-main">
        <p className="scn-res-kind">{t('scan.result.error')}</p>
        <p className="scn-res-msg">{last.message}</p>
      </div>
    )
  } else if (last?.kind === 'result') {
    const info = outcomeInfo(last.result.outcome)
    tone = info.tone
    const hk = hintKey(last.result)
    const matched = last.result.matchedBy ? t(`scan.matchedBy.${last.result.matchedBy}`) : null
    body = (
      <div className="scn-res-main">
        <p className="scn-res-kind">
          <Chip tone={info.chip}>{t(info.labelKey)}</Chip>
          <span className="ref">{last.ref}</span>
          {last.result.zoneCode && <span className="tag">{t('scan.result.zone', { zone: last.result.zoneCode })}</span>}
          {last.result.tripCode && <span className="tag">{t('scan.result.trip', { trip: last.result.tripCode })}</span>}
        </p>
        <p className="scn-res-msg">{last.result.message}</p>
        {matched && <p className="scn-res-sub">{t('scan.result.matchedBy', { by: matched })}</p>}
        {last.result.total != null && last.result.total > 0 && (
          <p className="scn-res-count" data-testid="scan-count">
            {t('scan.result.count', { scanned: last.result.scanned ?? 0, total: last.result.total })}
            {(last.result.missing ?? 0) > 0 && <span className="scn-res-missing"> · {t('scan.result.missing', { n: last.result.missing ?? 0 })}</span>}
            {onCloseCount && last.result.orderPublicId && (
              <button type="button" className="btn sm" style={{ marginLeft: 10 }} disabled={closing} onClick={() => onCloseCount(last.result.orderPublicId as string)}>
                {t('scan.closeCount.button')}
              </button>
            )}
          </p>
        )}
        {hk && <p className="scn-res-hint">{t(hk)}</p>}
      </div>
    )
  }

  return (
    <div className={`scn-res ${tone}`} role="status" aria-live="polite" aria-label={t('scan.result.label')}>
      <span className="scn-res-ic" aria-hidden="true">
        {tone === 'idle' ? null : ICON[tone]}
      </span>
      {body}
      {busy > 0 && <span className="scn-res-busy">{t('scan.result.busy')}</span>}
    </div>
  )
}
