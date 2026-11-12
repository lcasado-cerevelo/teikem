// Lote 241 — Cuotas: rejilla representante × mes del año con edición en bloque. Cada celda es la cuota PROPIA del mes; vacía =
// vale la del mes anterior más reciente (se ve tenue como sugerencia). «Copiar del mes anterior» llena la columna con lo que vale
// el mes de antes. «Guardar» manda solo lo que cambió, un `PUT quotas/{mes}` por mes. Sin `sales.analysis.manage`, solo lectura.
// La rejilla se desplaza dentro de su contenedor (nunca la página) y la columna del representante queda fija.
import { useEffect, useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Panel } from '../../kernel/ui/Panel'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useQuotaMonths, useSaveQuotas } from './salesAnalysisApi'
import {
  SALES_ANALYSIS_MANAGE,
  copyPreviousMonth,
  monthShort,
  parseQuota,
  quotaCell,
  quotaChanges,
  quotaDraftFrom,
  quotaMonthTotal,
  type QuotaDraft,
} from './salesAnalysisRules'
import { QueryError } from './SaParts'
import { useSaFormat } from './saFormat'

export function QuotasGrid() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const fm = useSaFormat()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const thisYear = Number(f.today().slice(0, 4))
  const [year, setYear] = useState(thisYear)
  const months = useMemo(() => Array.from({ length: 12 }, (_, i) => `${year}-${String(i + 1).padStart(2, '0')}`), [year])
  const q = useQuotaMonths(months)
  const data = q.data
  const [draft, setDraft] = useState<QuotaDraft>({})
  useEffect(() => {
    if (data) setDraft(quotaDraftFrom(data))
  }, [data])
  const save = useSaveQuotas()
  const { changes, invalid } = data ? quotaChanges(data, draft) : { changes: [], invalid: [] }
  const dirty = changes.length > 0
  const reps = data?.[0]?.rows ?? []

  const onSave = async () => {
    if (!data || invalid.length > 0) return
    try {
      const n = await save.mutateAsync(changes)
      toast.success(t('salesAnalysis.quotas.saved', { n }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel
      title={t('salesAnalysis.quotas.title')}
      subtitle={t('salesAnalysis.quotas.sub')}
      actions={
        <div className="sa-inline">
          <label htmlFor="sa-q-year" className="sr-only">{t('salesAnalysis.quotas.year')}</label>
          <select id="sa-q-year" value={year} onChange={(e) => setYear(Number(e.target.value))}>
            {[thisYear - 2, thisYear - 1, thisYear, thisYear + 1].map((y) => (
              <option key={y} value={y}>{y}</option>
            ))}
          </select>
        </div>
      }
      flush
    >
      {q.isError ? (
        <div className="pb"><QueryError error={q.error} /></div>
      ) : !data ? (
        <Spinner block label={t('common.loading')} />
      ) : reps.length === 0 ? (
        <p className="pb sa-muted">{t('salesAnalysis.quotas.noReps')}</p>
      ) : (
        <>
          <div className="sa-scroll sa-matrix-wrap" tabIndex={0} role="region" aria-label={t('salesAnalysis.quotas.title')}>
            <table className="sa-matrix sa-quota">
              <caption className="sr-only">{t('salesAnalysis.quotas.caption', { year })}</caption>
              <thead>
                <tr>
                  <th scope="col" className="stick">{t('salesAnalysis.col.rep')}</th>
                  {months.map((m, i) => (
                    <th key={m} scope="col">
                      <span>{monthShort(m, lang)}</span>
                      {canManage && i > 0 && (
                        <button type="button" className="btn sm sa-copy" onClick={() => setDraft((d) => copyPreviousMonth(d, data, m))} aria-label={t('salesAnalysis.quotas.copyPrevOf', { month: monthShort(m, lang) })}>
                          {t('salesAnalysis.quotas.copyPrev')}
                        </button>
                      )}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {reps.map((r) => (
                  <tr key={r.repId} className={r.repActive === false ? 'dim' : undefined}>
                    <th scope="row" className="stick">
                      <span>{r.repName}</span>
                      {r.routeName && <span className="sa-matrix-name">{r.routeName}</span>}
                    </th>
                    {data.map((mon) => {
                      const row = mon.rows?.find((x) => x.repId === r.repId)
                      const cell = quotaCell(r.repId ?? 0, mon.month ?? '')
                      const value = draft[cell] ?? ''
                      const bad = parseQuota(value) === undefined
                      const inherited = value === '' && row?.effective !== null && row?.effective !== undefined
                      const label = t('salesAnalysis.quotas.cellLabel', { rep: r.repName ?? '', month: monthShort(mon.month ?? '', lang) })
                      return (
                        <td key={mon.month}>
                          {canManage ? (
                            <input
                              type="text"
                              inputMode="decimal"
                              className={`sa-qin${bad ? ' bad' : ''}`}
                              aria-label={label}
                              aria-invalid={bad || undefined}
                              value={value}
                              placeholder={inherited ? fm.money0(row?.effective) : ''}
                              title={inherited ? t('salesAnalysis.quotas.inherited', { month: row?.effectiveFrom ? monthShort(row.effectiveFrom, lang) : '' }) : undefined}
                              onChange={(e) => setDraft((d) => ({ ...d, [cell]: e.target.value }))}
                            />
                          ) : (
                            <span className={inherited ? 'sa-muted' : undefined} aria-label={label}>
                              {value !== '' ? fm.money0(parseQuota(value) ?? 0) : inherited ? fm.money0(row?.effective) : '—'}
                            </span>
                          )}
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th scope="row" className="stick">{t('salesAnalysis.charts.total')}</th>
                  {data.map((mon) => (
                    <td key={mon.month}>{fm.money0(quotaMonthTotal(draft, mon))}</td>
                  ))}
                </tr>
              </tfoot>
            </table>
          </div>
          <div className="pb sa-actions-row">
            <p className="sa-muted">{t('salesAnalysis.quotas.help')}</p>
            {invalid.length > 0 && <p className="ferr" role="alert">{t('salesAnalysis.quotas.invalid')}</p>}
            {canManage && (
              <div className="sa-inline">
                <button type="button" className="btn" disabled={!dirty || save.isPending} onClick={() => setDraft(quotaDraftFrom(data))}>
                  {t('salesAnalysis.quotas.discard')}
                </button>
                <button type="button" className="btn flow" disabled={!dirty || invalid.length > 0 || save.isPending} onClick={() => void onSave()}>
                  {save.isPending ? t('salesAnalysis.saving') : t('salesAnalysis.quotas.save', { n: changes.reduce((s, c) => s + c.items.length, 0) })}
                </button>
              </div>
            )}
          </div>
        </>
      )}
    </Panel>
  )
}
