// Lote F-E2a — Avisos y bloqueantes de una ruta (manual 05 §6.1): los que BLOQUEAN el despacho en rojo, los demás en ámbar. Los textos son los
// del servidor (`TripIssueDto.message`); aquí solo se marca el tipo. F-E2b: el aviso de pines aproximados ofrece «Poner pin manual» si se puede.
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { sortedIssues, type TripIssue } from './dispatchRules'

/** Código del aviso «pines aproximados» del servidor (`TripRules.ApproximatePins`). */
export const APPROXIMATE_PINS = 'APPROXIMATE_PINS'

export function TripIssues({ issues, onApproximate }: { issues: readonly TripIssue[] | null | undefined; onApproximate?: () => void }) {
  const t = useT()
  const list = sortedIssues(issues)
  if (list.length === 0) return null
  return (
    <ul className="disp-issues" aria-label={t('dispatch.issues.label')}>
      {list.map((i, n) => (
        <li key={`${i.code}-${n}`} data-blocking={i.blocking ? 'true' : 'false'}>
          <Chip tone={i.blocking ? 'fail' : 'warn'}>{i.blocking ? t('dispatch.issues.blocks') : t('dispatch.issues.warns')}</Chip>
          <span>
            {i.message}
            {i.code === APPROXIMATE_PINS && onApproximate && (
              <>
                {' '}
                <button type="button" className="btn sm" onClick={onApproximate}>
                  {t('dispatch.pin.fromIssue')}
                </button>
              </>
            )}
          </span>
        </li>
      ))}
    </ul>
  )
}
