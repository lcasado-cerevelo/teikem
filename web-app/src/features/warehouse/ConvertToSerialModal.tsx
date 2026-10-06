// Lote F17 (Rentas F-R1) — "Convertir a serie" desde la ficha del producto (servidor: Lote 26, manual 06 §2.1). Para un
// producto sin seguimiento (NONE) con existencia: una caja por posición con existencia para capturar tantos números de serie
// como unidades en mano (uno por renglón; se pueden pegar varias líneas), con el conteo "n de m", las series repetidas (en
// cualquier posición, sin distinguir mayúsculas) y las largas marcadas con el mensaje exacto del servidor. Antes de enviar,
// una confirmación clara: es un movimiento NETO CERO (ajuste de salida del saldo sin serie y uno de entrada por cada serie,
// motivo "Conversión a serie") y el producto pasa a controlarse por serie. Los 400/409/422 del servidor se muestran tal cual.
// Lo muestra quien tiene `inventory.manage` e `inventory.adjust` con WMS_LOTSERIAL (`ProductDetailScreen`).
import { useMemo, useState } from 'react'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { useConvertToSerial, useInventoryBalances, type ProductDetailDto } from './api'
import { problemText } from './problemText'
import { capturedSerials, CONVERSION_LIMITS, conversionBody, conversionIssues, conversionPlan, type ConversionIssue } from './serialConversion'
import './warehouse.css'

export interface ConvertToSerialModalProps {
  open: boolean
  product: ProductDetailDto
  onClose: () => void
}

export function ConvertToSerialModal({ open, product, onClose }: ConvertToSerialModalProps) {
  if (!open) return null
  return <ConvertBody product={product} onClose={onClose} />
}

function ConvertBody({ product, onClose }: { product: ProductDetailDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const p = product.product ?? {}
  const sku = p.sku ?? ''
  const convert = useConvertToSerial()
  const balances = useInventoryBalances({ productPublicIds: p.publicId ? [p.publicId] : undefined, includeZero: false, take: 200 }, { handleAccessDenied: false })
  const plan = useMemo(() => conversionPlan(sku, balances.data?.items ?? []), [sku, balances.data])
  const [texts, setTexts] = useState<Record<number, string>>({})
  const [notes, setNotes] = useState('')
  const [step, setStep] = useState<'capture' | 'confirm'>('capture')
  const [touched, setTouched] = useState(false)
  const [alert, setAlert] = useState<string | null>(null)

  const check = useMemo(() => conversionIssues(plan.positions, texts), [plan.positions, texts])
  const totalUnits = plan.positions.reduce((s, x) => s + x.onHand, 0)
  const notesTooLong = notes.trim().length > CONVERSION_LIMITS.notes
  const issueText = (i: ConversionIssue) => t(`warehouse.convertSerial.errors.${i.code}`, i.params)
  const blocked = plan.blockers.length > 0 || plan.positions.length === 0
  const busy = convert.isPending

  function review() {
    setTouched(true)
    setAlert(null)
    if (blocked || check.total > 0 || notesTooLong) return
    setStep('confirm')
  }

  async function submit() {
    setAlert(null)
    try {
      const result = await convert.mutateAsync({ publicId: p.publicId ?? '', body: conversionBody(plan.positions, texts, notes, product.rowVersion) })
      toast.success(t('warehouse.convertSerial.done', { sku, count: f.number(result.serialCount ?? 0) }))
      onClose()
    } catch (err) {
      setAlert(problemText(err))
      setStep('capture')
    }
  }

  const footer =
    step === 'capture' ? (
      <>
        <button type="button" className="btn" onClick={onClose} disabled={busy}>
          {t('common.cancel')}
        </button>
        <button type="button" className="btn flow" onClick={review} disabled={busy || blocked || balances.isPending}>
          {t('warehouse.convertSerial.review')}
        </button>
      </>
    ) : (
      <>
        <button type="button" className="btn" onClick={() => setStep('capture')} disabled={busy}>
          {t('warehouse.convertSerial.back')}
        </button>
        <button type="button" className="btn flow" onClick={() => void submit()} disabled={busy}>
          {busy ? t('common.loading') : t('warehouse.convertSerial.confirm')}
        </button>
      </>
    )

  return (
    <Modal open size="lg" title={t('warehouse.convertSerial.title', { sku })} onClose={onClose} dismissible={!busy} footer={footer}>
      {alert && (
        <div className="form-alert" role="alert">
          {alert}
        </div>
      )}
      {step === 'confirm' ? (
        <div className="cs-confirm">
          <p>
            <strong>
              {t('warehouse.convertSerial.confirmBody', { serials: f.number(check.captured), positions: f.number(plan.positions.length), sku })}
            </strong>
          </p>
          <p>{t('warehouse.convertSerial.netZero')}</p>
          <p>{t('warehouse.convertSerial.oneWay')}</p>
          <ul className="cs-summary">
            {plan.positions.map((x) => (
              <li key={x.binId}>
                <span className="mono">{x.binCode}</span> · {x.warehouseCode} — {t('warehouse.convertSerial.summaryLine', { count: f.number(x.onHand) })}
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <>
          <p className="help">{t('warehouse.convertSerial.intro', { sku, units: f.number(totalUnits) })}</p>
          {balances.isPending ? (
            <p className="note">{t('common.loading')}</p>
          ) : balances.error ? (
            <p className="ferr" role="alert">
              {problemText(balances.error)}
            </p>
          ) : (
            <>
              {plan.blockers.map((b, i) => (
                <p key={i} className="ferr" role="alert">
                  {issueText(b)}
                </p>
              ))}
              {plan.positions.length === 0 && plan.blockers.length === 0 && <p className="note">{t('warehouse.convertSerial.noStock')}</p>}
              {plan.positions.map((x) => {
                const id = `cs-bin-${x.binId}`
                const serials = capturedSerials(texts[x.binId])
                const issues = check.byBin[x.binId] ?? []
                const ok = serials.length === x.onHand && issues.length === 0
                const show = touched || serials.length > 0
                return (
                  <div className="f cs-pos" key={x.binId}>
                    <label htmlFor={id}>
                      {t('warehouse.convertSerial.positionLabel', { bin: x.binCode, warehouse: x.warehouseCode, count: f.number(x.onHand) })}
                    </label>
                    <textarea
                      id={id}
                      rows={Math.min(8, Math.max(2, x.onHand))}
                      value={texts[x.binId] ?? ''}
                      disabled={blocked}
                      spellCheck={false}
                      autoComplete="off"
                      aria-invalid={show && issues.length > 0 ? true : undefined}
                      aria-describedby={`${id}-count${show && issues.length > 0 ? ` ${id}-err` : ''}`}
                      onChange={(e) => setTexts((cur) => ({ ...cur, [x.binId]: e.target.value }))}
                    />
                    <p className={ok ? 'help cs-count ok' : 'help cs-count'} id={`${id}-count`} aria-live="polite">
                      {t('warehouse.convertSerial.counter', { m: f.number(serials.length), n: f.number(x.onHand) })}
                    </p>
                    {show && issues.length > 0 && (
                      <ul className="ferr cs-errs" id={`${id}-err`}>
                        {issues.map((i, k) => (
                          <li key={k}>{issueText(i)}</li>
                        ))}
                      </ul>
                    )}
                  </div>
                )
              })}
              <p className="help">{t('warehouse.convertSerial.pasteHelp')}</p>
              <div className="f">
                <label htmlFor="cs-notes">{t('warehouse.convertSerial.notes')}</label>
                <textarea id="cs-notes" rows={2} maxLength={CONVERSION_LIMITS.notes + 50} value={notes} onChange={(e) => setNotes(e.target.value)} aria-describedby="cs-notes-help" />
                <p className="help" id="cs-notes-help">
                  {t('warehouse.convertSerial.notesHelp')}
                </p>
                {notesTooLong && (
                  <p className="ferr" role="alert">
                    {t('warehouse.convertSerial.errors.notesTooLong')}
                  </p>
                )}
              </div>
              {touched && check.first && (
                <p className="ferr" role="alert">
                  {issueText(check.first)}
                </p>
              )}
            </>
          )}
        </>
      )}
    </Modal>
  )
}
