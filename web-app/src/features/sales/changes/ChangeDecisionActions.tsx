// Lote 249 — las decisiones sobre una modificación PENDIENTE de segunda aprobación: aprobar (sales.modify.approve, otra persona que quien la registró), rechazar
// (nota obligatoria) y retirar (quien la registró o quien puede modificar órdenes). Lo usan el aviso de la ficha y el detalle de la modificación.
import { useState } from 'react'
import { Can, useCan } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useLang, useT } from '../../../kernel/i18n'
import { toast } from '../../../kernel/ui'
import { useSession } from '../../../app/session'
import { serverText } from './changeRules'
import { useChangeMutations, type ChangeListItemDto } from './changesApi'
import './changes.css'

export function ChangeDecisionActions({ orderPublicId, item, onDone }: { orderPublicId: string; item: ChangeListItemDto; onDone?: () => void }) {
  const t = useT()
  const lang = useLang()
  const { me } = useSession()
  const canOverride = useCan('orders.credit_override')
  const { approve, reject, withdraw } = useChangeMutations()
  const [note, setNote] = useState('')
  const [override, setOverride] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const busy = approve.isPending || reject.isPending || withdraw.isPending
  const mine = me?.userId !== undefined && me.userId === item.registeredBy
  const id = item.publicId ?? ''

  const run = async (p: Promise<unknown>, ok: string) => {
    setError(null)
    try {
      await p
      toast.success(ok)
      onDone?.()
    } catch (e) {
      setError(serverText(t, lang, applyProblemDetails(e).title))
    }
  }

  return (
    <div className="soc-form">
      <div className="f">
        <label htmlFor={`soc-note-${id}`}>{t('salesChanges.decision.note')}</label>
        <input id={`soc-note-${id}`} value={note} maxLength={500} autoComplete="off" onChange={(e) => setNote(e.target.value)} />
        <p className="help">{t('salesChanges.decision.noteHelp')}</p>
      </div>
      <Can perm="sales.modify.approve">
        {mine && <p className="help" role="note">{t('salesChanges.decision.otherPerson')}</p>}
        {canOverride && !mine && (
          <label className="sw">
            <input type="checkbox" role="switch" checked={override} onChange={(e) => setOverride(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('salesChanges.preview.creditOverride')}</span>
          </label>
        )}
      </Can>
      <div className="soc-pending-acts">
        <Can perm="sales.modify.approve">
          {!mine && (
            <button
              type="button"
              className="btn flow"
              disabled={busy}
              onClick={() => void run(approve.mutateAsync({ orderId: orderPublicId, changeId: id, note: note.trim(), overrideCredit: override }), t('salesChanges.decision.approved', { n: item.changeNo ?? 0 }))}
            >
              {t('salesChanges.decision.approve')}
            </button>
          )}
          <button
            type="button"
            className="btn danger"
            disabled={busy}
            onClick={() => {
              if (note.trim() === '') return setError(t('salesChanges.decision.noteRequired'))
              void run(reject.mutateAsync({ orderId: orderPublicId, changeId: id, note: note.trim() }), t('salesChanges.decision.rejected', { n: item.changeNo ?? 0 }))
            }}
          >
            {t('salesChanges.decision.reject')}
          </button>
        </Can>
        <Can perm="sales.modify">
          <button
            type="button"
            className="btn"
            disabled={busy}
            onClick={() => void run(withdraw.mutateAsync({ orderId: orderPublicId, changeId: id, note: note.trim() }), t('salesChanges.decision.withdrawn', { n: item.changeNo ?? 0 }))}
          >
            {t('salesChanges.decision.withdraw')}
          </button>
        </Can>
      </div>
      {error && <p className="ferr" role="alert">{error}</p>}
    </div>
  )
}
