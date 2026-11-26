// Lote 249 (Lote E) — «Modificar la orden» (sales.modify): todo cambio a una orden YA SOLICITADA pasa por aquí, con quién lo autorizó en el cliente. Primero se pide
// `edit-options` (la etapa real de cada recolección y la posición por línea); pasos: Líneas → Términos → Autorización → Vista previa (no escribe nada) → Guardar.
// Facturada = solo admite la orden interna de adición (mensaje E-11). Empacada = se reabre el empaque en la misma llamada (warehouse.reopen-pack). Con segunda
// aprobación la modificación queda pendiente y no se aplica nada hasta que otra persona la apruebe.
import { useEffect, useMemo, useState } from 'react'
import { Can, useCan } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { utcFromZonedInput, zonedInputFromUtc } from '../../../kernel/api/tenantZone'
import { useLookups } from '../../../kernel/catalogs'
import { useLang, useT } from '../../../kernel/i18n'
import { Chip, Modal, Spinner, toast } from '../../../kernel/ui'
import { ConsigneePicker } from '../../consignees/ConsigneePicker'
import { isCreditTerm } from '../salesRules'
import type { SalesOrderDto } from '../salesApi'
import { ChangeAuthorizationStep } from './ChangeAuthorizationStep'
import { ChangeLinesEditor } from './ChangeLinesEditor'
import { ChangePreview } from './ChangePreview'
import { OrderInvoices } from './ChangeHistoryPanel'
import {
  authError,
  buildChangeRequest,
  EMPTY_AUTH,
  EMPTY_DRAFT,
  hasChanges,
  lineError,
  serverText,
  type AuthDraft,
  type BaseTerms,
  type ChangeDraft,
} from './changeRules'
import { useChangeMutations, useEditOptions, type EditOptionsDto } from './changesApi'
import './changes.css'

type Step = 'lines' | 'terms' | 'auth' | 'preview'
const STEPS: readonly Step[] = ['lines', 'terms', 'auth', 'preview']

function baseTermsOf(order: SalesOrderDto): BaseTerms {
  return {
    paymentMethod: order.terms?.paymentMethod ?? '',
    paymentTerm: order.terms?.paymentTerm ?? '',
    deliveryMode: order.terms?.deliveryMode ?? '',
    deliveryModeSet: order.terms?.deliveryModeSet === true,
    consigneePublicId: order.consigneePublicId ?? '',
    notes: order.notes ?? '',
  }
}

/** Paso «Términos»: forma de pago, entrega o recogido, consignatario y notas. Si ya salió parte de la orden, el pago, la entrega y el consignatario no se cambian. */
function TermsStep({ order, opts, draft, onDraft }: { order: SalesOrderDto; opts: EditOptionsDto; draft: ChangeDraft; onDraft: (next: (d: ChangeDraft) => ChangeDraft) => void }) {
  const t = useT()
  const base = baseTermsOf(order)
  const methods = useLookups('SalesPaymentMethod')
  const termOptions = useLookups('PaymentTerm', { includeDisabled: true })
  const left = (opts.lines ?? []).some((l) => (l.out ?? 0) > 0)
  const terms = draft.terms
  const method = terms.paymentMethod ?? base.paymentMethod ?? ''
  const set = (patch: Partial<ChangeDraft['terms']>) => onDraft((d) => ({ ...d, terms: { ...d.terms, ...patch } }))
  return (
    <div className="soc-form">
      {left && <p className="soc-alert warn" role="note">{t('salesChanges.blockers.termsLeft')}</p>}
      <div className="r2">
        <div className="f">
          <label htmlFor="soc-pay">{t('sales.extra.payment')}</label>
          <select id="soc-pay" value={method} disabled={left} onChange={(e) => set({ paymentMethod: e.target.value, paymentTerm: e.target.value === 'CREDIT' ? terms.paymentTerm : '' })}>
            <option value="">{t('sales.extra.paymentNone')}</option>
            {(methods.data ?? []).map((m) => (
              <option key={m.code} value={m.code}>{m.label}</option>
            ))}
          </select>
        </div>
        <div className="f">
          <label htmlFor="soc-delivery">{t('sales.extra.delivery')}</label>
          <select id="soc-delivery" value={terms.deliveryMode ?? (base.deliveryModeSet ? base.deliveryMode ?? '' : '')} disabled={left} onChange={(e) => set({ deliveryMode: e.target.value })}>
            <option value="">{t('sales.extra.deliveryAuto')}</option>
            <option value="DELIVERY">{t('sales.badge.delivery')}</option>
            <option value="PICKUP">{t('sales.badge.pickup')}</option>
          </select>
        </div>
      </div>
      {method === 'CREDIT' && (
        <div className="f">
          <label htmlFor="soc-term">{t('sales.extra.term')}</label>
          <select id="soc-term" value={terms.paymentTerm ?? base.paymentTerm ?? ''} disabled={left} onChange={(e) => set({ paymentTerm: e.target.value })}>
            <option value="">{t('sales.extra.termNone')}</option>
            {(termOptions.data ?? []).filter((o) => isCreditTerm(o.code)).map((o) => (
              <option key={o.code} value={o.code}>{o.label}</option>
            ))}
          </select>
        </div>
      )}
      <div className="f">
        <label htmlFor="soc-consignee">{t('salesChanges.terms.consignee')}</label>
        <ConsigneePicker id="soc-consignee" clientPublicId={order.clientPublicId ?? null} value={terms.consigneePublicId ?? base.consigneePublicId ?? ''} onChange={(id) => !left && set({ consigneePublicId: id })} />
      </div>
      <div className="f">
        <label htmlFor="soc-notes">{t('sales.notes')}</label>
        <input id="soc-notes" value={terms.notes ?? base.notes ?? ''} maxLength={500} autoComplete="off" onChange={(e) => set({ notes: e.target.value })} />
      </div>
    </div>
  )
}

function ModifyBody({ order, opts, onClose, onAddition }: { order: SalesOrderDto; opts: EditOptionsDto; onClose: () => void; onAddition: () => void }) {
  const t = useT()
  const lang = useLang()
  const canReopen = useCan('warehouse.reopen-pack')
  const { preview, create } = useChangeMutations()
  const [step, setStep] = useState<Step>('lines')
  const [draft, setDraft] = useState<ChangeDraft>(EMPTY_DRAFT)
  const [auth, setAuth] = useState<AuthDraft>(() => ({ ...EMPTY_AUTH, at: zonedInputFromUtc(new Date(Date.now() + 59_000).toISOString()) }))
  const [reopen, setReopen] = useState(false)
  const [allowBackorder, setAllowBackorder] = useState(false)
  const [overrideCredit, setOverrideCredit] = useState(false)
  const [showAuthError, setShowAuthError] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const positions = opts.lines ?? []
  const base = useMemo(() => baseTermsOf(order), [order])
  const reopenable = (opts.batches ?? []).filter((b) => b.stage === 'REOPENABLE')
  const settings = opts.settings
  const attachmentRequired = settings?.attachmentRequired === true
  const authErr = authError(auth, attachmentRequired)
  const anyLineError = positions.some((p) => lineError(p, draft.edits[p.lineId ?? 0], reopen) !== null)
  const changed = hasChanges(draft, positions, base)
  const busy = create.isPending
  const atUtc = utcFromZonedInput(auth.at)
  const request = () => buildChangeRequest(draft, positions, base, auth, atUtc, { reopenPack: reopen, allowBackorder, overrideCredit, rowVersion: opts.rowVersion })

  // la vista previa se pide al entrar al paso (no escribe nada)
  useEffect(() => {
    if (step === 'preview') preview.mutate({ orderId: order.publicId!, body: request() })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [step])

  const idx = STEPS.indexOf(step)
  const previewData = preview.data
  const blocked = (previewData?.blockers ?? []).length > 0 || (previewData?.creditExceeded === true && !(overrideCredit))
  const goNext = () => {
    setError(null)
    if (step === 'lines' && anyLineError) return
    if (step === 'auth') {
      if (authErr) return setShowAuthError(true)
      setShowAuthError(false)
    }
    setStep(STEPS[idx + 1])
  }
  const save = async () => {
    setError(null)
    try {
      const dto = await create.mutateAsync({ orderId: order.publicId!, body: request() })
      const n = dto.item?.changeNo ?? 0
      toast.success(dto.item?.isPending ? t('salesChanges.savedPending', { n }) : t('salesChanges.saved', { n }))
      onClose()
    } catch (e) {
      setError(serverText(t, lang, applyProblemDetails(e).title))
    }
  }

  // sin cambio posible: facturada / ya salió completa (solo se añade), borrador o cancelada
  if (opts.orderBlocker) {
    return (
      <Modal
        open
        size="md"
        title={t('salesChanges.title', { number: order.number ?? '' })}
        onClose={onClose}
        footer={
          <>
            <button type="button" className="btn" onClick={onClose}>{t('common.close')}</button>
            {opts.mustUseAddition && opts.canAddition && (
              <Can perm="sales.modify">
                <button type="button" className="btn flow" onClick={onAddition}>{t('salesChanges.addition.button')}</button>
              </Can>
            )}
          </>
        }
      >
        <div className="pb soc-form">
          <p className="soc-alert warn" role="alert">{serverText(t, lang, opts.orderBlocker)}</p>
          {opts.mustUseAddition && <p className="help">{t('salesChanges.addition.help')}</p>}
          <OrderInvoices invoices={opts.invoices ?? []} />
        </div>
      </Modal>
    )
  }

  const pending = opts.pending
  return (
    <Modal
      open
      size="lg"
      title={t('salesChanges.title', { number: order.number ?? '' })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          {idx > 0 && <button type="button" className="btn" onClick={() => setStep(STEPS[idx - 1])} disabled={busy}>{t('salesChanges.back')}</button>}
          {step !== 'preview' ? (
            <button type="button" className="btn flow" onClick={goNext} disabled={Boolean(pending) || (step === 'lines' && anyLineError)}>{t('salesChanges.next')}</button>
          ) : (
            <button type="button" className="btn flow" onClick={() => void save()} disabled={busy || preview.isPending || preview.isError || blocked || Boolean(pending)}>
              {busy ? t('common.loading') : t('salesChanges.save')}
            </button>
          )}
        </>
      }
    >
      <div className="pb soc-form">
        <ol className="soc-steps" aria-label={t('salesChanges.stepsLabel')}>
          {STEPS.map((s, i) => (
            <li key={s} className={`soc-step${s === step ? ' on' : i < idx ? ' done' : ''}`} aria-current={s === step ? 'step' : undefined}>
              {i + 1}. {t(`salesChanges.steps.${s}`)}
            </li>
          ))}
        </ol>
        {pending && <p className="soc-alert warn" role="alert">{t('salesChanges.pendingExists', { n: pending.changeNo ?? 0 })}</p>}
        {step === 'lines' && (
          <>
            {(opts.batches ?? []).length > 0 && (
              <ul className="soc-batches" aria-label={t('salesChanges.batches.title')}>
                {(opts.batches ?? []).map((b) => (
                  <li key={b.publicId} className="soc-batch">
                    <b>{b.packNumber || b.number}</b>
                    <Chip tone={b.stage === 'REVERSIBLE' ? 'wh' : b.stage === 'REOPENABLE' ? 'warn' : 'neutral'}>{t(`salesChanges.stage.${b.stage}`)}</Chip>
                    {b.shipmentNumber && <span className="help">{t('salesChanges.batches.shipment', { number: b.shipmentNumber })}</span>}
                    {b.stage === 'LEFT' && b.reason && <span className="help">{serverText(t, lang, b.reason)}</span>}
                  </li>
                ))}
              </ul>
            )}
            {reopenable.length > 0 &&
              (canReopen ? (
                <label className="sw">
                  <input type="checkbox" role="switch" checked={reopen} onChange={(e) => setReopen(e.target.checked)} />
                  <span className="tk" aria-hidden="true" />
                  <span>{t('salesChanges.reopen.label', { packs: reopenable.map((b) => `${b.packNumber || b.number}${b.shipmentNumber ? ` (${t('salesChanges.reopen.shipment', { number: b.shipmentNumber })})` : ''}`).join(', ') })}</span>
                </label>
              ) : (
                <p className="soc-alert" role="note">
                  {t('salesChanges.reopen.noPermission', { packs: reopenable.map((b) => b.packNumber || b.number).join(', ') })}{' '}
                  <button type="button" className="btn sm" onClick={onAddition}>{t('salesChanges.addition.button')}</button>
                </p>
              ))}
            <ChangeLinesEditor order={order} opts={opts} draft={draft} onDraft={setDraft} reopen={reopen && canReopen} allowBackorder={allowBackorder} onAllowBackorder={setAllowBackorder} />
          </>
        )}
        {step === 'terms' && <TermsStep order={order} opts={opts} draft={draft} onDraft={setDraft} />}
        {step === 'auth' && (
          <ChangeAuthorizationStep
            orderPublicId={order.publicId!}
            clientPublicId={order.clientPublicId ?? null}
            auth={auth}
            onChange={(patch) => setAuth((a) => ({ ...a, ...patch }))}
            attachmentRequired={attachmentRequired}
            error={showAuthError ? authErr : null}
          />
        )}
        {step === 'preview' && (
          <>
            {!changed && <p className="soc-alert warn" role="alert">{t('salesChanges.blockers.nothing')}</p>}
            {preview.isPending && <Spinner block />}
            {preview.isError && <p className="ferr" role="alert">{serverText(t, lang, applyProblemDetails(preview.error).title)}</p>}
            {previewData && <ChangePreview preview={previewData} approvalAmount={settings?.approvalAmount} overrideCredit={overrideCredit} onOverrideCredit={setOverrideCredit} />}
            {changed && authErr && <p className="soc-alert bad" role="alert">{t(`salesChanges.auth.errors.${authErr}`)}</p>}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

/** La ventana «Modificar la orden». `onAddition` abre la orden interna de adición (cuando la orden ya no admite otro cambio). */
export function ModifyOrderModal({ order, onClose, onAddition }: { order: SalesOrderDto; onClose: () => void; onAddition: () => void }) {
  const t = useT()
  const opts = useEditOptions(order.publicId ?? null)
  if (opts.isPending || opts.isError) {
    return (
      <Modal open size="md" title={t('salesChanges.title', { number: order.number ?? '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('common.close')}</button>}>
        <div className="pb">{opts.isError ? <p className="ferr" role="alert">{applyProblemDetails(opts.error).title}</p> : <Spinner block />}</div>
      </Modal>
    )
  }
  // `key` por versión de la orden: si cambió mientras se escribía, se reinicia con lo nuevo
  return <ModifyBody key={opts.data.rowVersion ?? 'v'} order={order} opts={opts.data} onClose={onClose} onAddition={onAddition} />
}
