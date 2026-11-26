// Lote 249 — una modificación completa: quién la registró, quién la autorizó en el cliente (nombre, cargo, cómo y cuándo), el motivo, la evidencia, la decisión de la
// segunda aprobación, el «antes y después» (diferencia línea por línea) y los efectos en el almacén. Pendiente: aquí también se aprueba, se rechaza o se retira.
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { Chip, Modal, Spinner } from '../../../kernel/ui'
import { ChangeDecisionActions } from './ChangeDecisionActions'
import { ChangeDiffView, ChangeEffectsView } from './ChangePreview'
import { useOrderChange } from './changesApi'
import './changes.css'

export function ChangeDetailModal({ orderPublicId, changePublicId, onClose }: { orderPublicId: string; changePublicId: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const change = useOrderChange(orderPublicId, changePublicId)
  const item = change.data?.item
  return (
    <Modal
      open
      size="lg"
      title={item ? t('salesChanges.detail.title', { n: item.changeNo ?? 0, number: item.orderNumber ?? '' }) : t('salesChanges.detail.loading')}
      onClose={onClose}
      footer={<button type="button" className="btn" onClick={onClose}>{t('common.close')}</button>}
    >
      <div className="pb soc-form">
        {change.isPending && <Spinner block />}
        {change.isError && <p className="ferr" role="alert">{applyProblemDetails(change.error).title}</p>}
        {change.data && item && (
          <>
            <p>
              <Chip color={item.statusColor ?? undefined}>{item.statusLabel}</Chip> <Chip tone="neutral">{t(`salesChanges.kinds.${item.kind}`)}</Chip>
              {item.additionNumber && <> <Chip tone="cap">{t('salesChanges.detail.addition', { number: item.additionNumber })}</Chip></>}
            </p>
            <dl className="soc-facts">
              <div><dt>{t('salesChanges.history.by')}</dt><dd>{item.registeredByName ?? '—'} · {f.dateTime(item.registeredAtUtc)}</dd></div>
              <div><dt>{t('salesChanges.history.authorized')}</dt><dd>{[item.authorizedByName, item.authorizedByTitle].filter(Boolean).join(' · ')}</dd></div>
              <div><dt>{t('salesChanges.auth.channel')}</dt><dd>{item.channelLabel} · {f.dateTime(change.data.authorizedAtUtc)}</dd></div>
              <div><dt>{t('salesChanges.auth.reason')}</dt><dd>{item.reason}</dd></div>
              {change.data.attachmentFileName && <div><dt>{t('salesChanges.auth.attachment')}</dt><dd>{change.data.attachmentFileName}</dd></div>}
              {item.decidedByName && (
                <div><dt>{t('salesChanges.detail.decision')}</dt><dd>{item.decidedByName} · {f.dateTime(item.decidedAtUtc)}{change.data.decisionNote ? ` · ${change.data.decisionNote}` : ''}</dd></div>
              )}
            </dl>
            {change.data.diff && <ChangeDiffView diff={change.data.diff} />}
            <ChangeEffectsView reversed={change.data.effects?.reversedBatches ?? []} reopened={change.data.effects?.reopenedPacks ?? []} />
            {item.isPending && <ChangeDecisionActions orderPublicId={orderPublicId} item={item} onDone={onClose} />}
          </>
        )}
      </div>
    </Modal>
  )
}
