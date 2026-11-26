// Lote 249 — la vista previa de una modificación (y el «antes y después» del historial): la diferencia línea por línea, los términos, los totales, lo que pasa en el
// almacén (recolecciones que se deshacen, empaques que se reabren), el crédito y si pide segunda aprobación. Los textos salen de datos del servidor (campos, códigos,
// números), nunca del texto en español que el servidor también manda.
import { useMemo } from 'react'
import { useCan } from '../../../kernel/access'
import { useFormat } from '../../../kernel/format'
import { useT, useLang } from '../../../kernel/i18n'
import { useLookups } from '../../../kernel/catalogs'
import { Chip } from '../../../kernel/ui'
import { approvalRuleKey, fieldKind, numericValue, serverText } from './changeRules'
import type { ChangeDiffDto, ChangePreviewDto } from './changesApi'
import './changes.css'

type DiffLine = NonNullable<ChangeDiffDto['added']>[number]
type DiffField = NonNullable<DiffLine['fields']>[number]

/** Un valor de la diferencia con el formato de la compañía (dinero, cantidad, %) o su etiqueta (forma de pago, entrega). */
function useFieldValue() {
  const t = useT()
  const f = useFormat()
  const methods = useLookups('SalesPaymentMethod')
  const terms = useLookups('PaymentTerm', { includeDisabled: true })
  return useMemo(
    () => (field: string, value: string | null | undefined): string => {
      if (value === null || value === undefined || value === '') return '—'
      const kind = fieldKind(field)
      const n = numericValue(value)
      if (kind === 'money' && n !== null) return f.money(n)
      if (kind === 'number' && n !== null) return f.number(n)
      if (kind === 'percent' && n !== null) return `${f.number(n)} %`
      if (field === 'deliveryMode') return t(value === 'PICKUP' ? 'sales.badge.pickup' : 'sales.badge.delivery')
      if (field === 'isConsignment') return t(value === 'true' ? 'salesChanges.yes' : 'salesChanges.no')
      if (field === 'paymentMethod') return (methods.data ?? []).find((m) => m.code === value)?.label ?? value
      if (field === 'paymentTerm') return (terms.data ?? []).find((m) => m.code === value)?.label ?? value
      if (field === 'taxReason') return value === 'MANUAL_OVERRIDE' ? t('salesChanges.taxManual') : value
      return value
    },
    [t, f, methods.data, terms.data],
  )
}

function fieldLabel(t: ReturnType<typeof useT>, field: string | null | undefined): string {
  const key = `salesChanges.fields.${field ?? ''}`
  const text = t(key)
  return text === key ? field ?? '' : text
}

function DiffItem({ line, kind }: { line: DiffLine; kind: 'ADDED' | 'REMOVED' | 'CHANGED' }) {
  const t = useT()
  const value = useFieldValue()
  return (
    <li className="soc-diff-item">
      <div className="soc-diff-head">
        <Chip tone={kind === 'ADDED' ? 'deliv' : kind === 'REMOVED' ? 'fail' : 'warn'}>{t(`salesChanges.diff.${kind}`)}</Chip>
        <b className="mono">{line.sku}</b>
        <span>{line.name}</span>
      </div>
      <dl className="soc-diff-fields">
        {(line.fields ?? []).map((fd: DiffField) => (
          <div key={fd.field}>
            <dt>{fieldLabel(t, fd.field)}</dt>
            <dd>
              {value(fd.field ?? '', fd.before)} <span className="soc-arrow" aria-hidden="true">→</span> {value(fd.field ?? '', fd.after)}
            </dd>
          </div>
        ))}
      </dl>
    </li>
  )
}

/** La diferencia entre antes y después: líneas agregadas, quitadas y cambiadas, los términos y los totales. */
export function ChangeDiffView({ diff }: { diff: ChangeDiffDto }) {
  const t = useT()
  const f = useFormat()
  const value = useFieldValue()
  const added = diff.added ?? []
  const removed = diff.removed ?? []
  const changed = diff.changed ?? []
  const termsDiff = diff.terms ?? []
  const delta = diff.delta ?? 0
  return (
    <div className="soc-form">
      <p className="soc-totals" role="status">
        <span>{t('salesChanges.preview.totalBefore')}: <b>{f.money(diff.totalBefore ?? 0)}</b></span>
        <span className="soc-arrow" aria-hidden="true">→</span>
        <span>{t('salesChanges.preview.totalAfter')}: <b>{f.money(diff.totalAfter ?? 0)}</b></span>
        <span className="soc-up">({delta > 0 ? '+' : ''}{f.money(delta)})</span>
      </p>
      {added.length + removed.length + changed.length + termsDiff.length === 0 ? (
        <p className="help">{t('salesChanges.preview.noDiff')}</p>
      ) : (
        <ul className="soc-diff" aria-label={t('salesChanges.preview.diff')}>
          {added.map((l) => <DiffItem key={`a${l.lineId}`} line={l} kind="ADDED" />)}
          {removed.map((l) => <DiffItem key={`r${l.lineId}`} line={l} kind="REMOVED" />)}
          {changed.map((l) => <DiffItem key={`c${l.lineId}`} line={l} kind="CHANGED" />)}
          {termsDiff.length > 0 && (
            <li className="soc-diff-item">
              <div className="soc-diff-head"><Chip tone="warn">{t('salesChanges.diff.TERMS')}</Chip></div>
              <dl className="soc-diff-fields">
                {termsDiff.map((fd) => (
                  <div key={fd.field}>
                    <dt>{fieldLabel(t, fd.field)}</dt>
                    <dd>
                      {value(fd.field ?? '', fd.before)} <span className="soc-arrow" aria-hidden="true">→</span> {value(fd.field ?? '', fd.after)}
                    </dd>
                  </div>
                ))}
              </dl>
            </li>
          )}
        </ul>
      )}
    </div>
  )
}

/** Lo que pasa en el almacén: recolecciones que se deshacen y empaques que se reabren. */
export function ChangeEffectsView({ reversed, reopened }: { reversed: readonly string[]; reopened: readonly string[] }) {
  const t = useT()
  if (reversed.length === 0 && reopened.length === 0) return null
  return (
    <div className="soc-alert warn" role="status">
      <b>{t('salesChanges.preview.effects')}</b>
      <ul>
        {reopened.map((p) => <li key={`o${p}`}>{t('salesChanges.preview.reopened', { pack: p })}</li>)}
        {reversed.map((b) => <li key={`r${b}`}>{t('salesChanges.preview.reversed', { batch: b })}</li>)}
      </ul>
      <p className="help">{t('salesChanges.preview.effectsHelp')}</p>
    </div>
  )
}

export function ChangePreview({ preview, approvalAmount, overrideCredit, onOverrideCredit }: {
  preview: ChangePreviewDto
  /** El monto de la segunda aprobación vigente (para decir por qué la pide). */
  approvalAmount: number | null | undefined
  overrideCredit: boolean
  onOverrideCredit: (v: boolean) => void
}) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const canOverride = useCan('orders.credit_override')
  const rule = approvalRuleKey(preview.approvalRule)
  const blockers = preview.blockers ?? []
  const credit = preview.credit
  return (
    <div className="soc-form">
      {blockers.length > 0 && (
        <div className="soc-alert bad" role="alert">
          <b>{t('salesChanges.preview.blocked')}</b>
          <ul>
            {blockers.map((b) => <li key={b}>{serverText(t, lang, b)}</li>)}
          </ul>
        </div>
      )}
      {preview.after && <ChangeDiffView diff={preview.diff!} />}
      <ChangeEffectsView reversed={preview.effects?.reversedBatches ?? []} reopened={preview.effects?.reopenedPacks ?? []} />
      {preview.creditChecked && credit && (
        <div className={`soc-alert${preview.creditExceeded ? ' warn' : ''}`} role="status">
          <span>
            {t('salesChanges.preview.credit', { limit: credit.limit == null ? '—' : f.money(credit.limit), exposure: f.money(credit.exposure ?? 0), available: credit.available == null ? '—' : f.money(credit.available) })}
          </span>
          {preview.creditExceeded && (
            <>
              <b>{t('salesChanges.preview.creditExceeded')}</b>
              {canOverride ? (
                <label className="sw">
                  <input type="checkbox" role="switch" checked={overrideCredit} onChange={(e) => onOverrideCredit(e.target.checked)} />
                  <span className="tk" aria-hidden="true" />
                  <span>{t('salesChanges.preview.creditOverride')}</span>
                </label>
              ) : (
                <span className="help">{t('salesChanges.preview.creditNoPermission')}</span>
              )}
            </>
          )}
        </div>
      )}
      {preview.requiresApproval && (
        <div className="soc-alert warn" role="status">
          <b>{t('salesChanges.preview.needsApproval')}</b>
          <span>{rule === 'aboveAmount' ? t('salesChanges.preview.ruleAbove', { amount: f.money(approvalAmount ?? 0) }) : t('salesChanges.preview.ruleAlways')}</span>
        </div>
      )}
    </div>
  )
}
