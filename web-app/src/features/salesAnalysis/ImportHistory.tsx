// Lote 241 — «Importar historial de QuickBooks»: subir los reportes «Sales by Item Detail» y/o «Purchases by Item Detail» (CSV),
// SIMULAR primero (no escribe nada: líneas leídas, por clase, rango de fechas, total contra el TOTAL del reporte, totales por mes y
// la lista de clientes e ítems SIN emparejar) y luego «Importar de verdad» con confirmación. Solo con `sales.analysis.manage`.
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { Panel } from '../../kernel/ui/Panel'
import { useImportHistory, useSalesStatus } from './salesAnalysisApi'
import { LINE_CLASSES, classRows, monthShort, totalsMatch, type SalesImportFile, type SalesImportReport, type SalesUnmatched } from './salesAnalysisRules'
import { Warnings } from './SaParts'
import { useSaFormat } from './saFormat'

export function ImportHistory({ onLink }: { onLink?: () => void }) {
  const t = useT()
  const fm = useSaFormat()
  const status = useSalesStatus()
  const [salesFile, setSalesFile] = useState<File | null>(null)
  const [purchasesFile, setPurchasesFile] = useState<File | null>(null)
  const [cutoffDate, setCutoffDate] = useState('')
  const [repsFromCustomField, setRepsFromCustomField] = useState(true)
  const [report, setReport] = useState<SalesImportReport | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirm, setConfirm] = useState(false)
  const run = useImportHistory()
  const hasFile = !!salesFile || !!purchasesFile
  const simulated = report?.dryRun === true

  const go = async (dryRun: boolean) => {
    setError(null)
    try {
      const r = await run.mutateAsync({ salesFile, purchasesFile, dryRun, cutoffDate, repsFromCustomField })
      setReport(r)
    } catch (e) {
      setError(applyProblemDetails(e).title)
      if (!dryRun) throw e
    }
  }
  // cambiar un archivo invalida la simulación anterior
  const pick = (setter: (f: File | null) => void) => (e: React.ChangeEvent<HTMLInputElement>) => {
    setter(e.target.files?.[0] ?? null)
    setReport(null)
  }

  const batches = (status.data?.batches ?? []).filter((b) => b.isActive)

  return (
    <Panel title={t('salesAnalysis.import.title')} subtitle={t('salesAnalysis.import.sub')}>
      <ol className="sa-steps">
        <li>{t('salesAnalysis.import.step1')}</li>
        <li>{t('salesAnalysis.import.step2')}</li>
        <li>{t('salesAnalysis.import.step3')}</li>
      </ol>
      {batches.length > 0 && (
        <div className="sa-scroll">
          <table className="sa-mini" aria-label={t('salesAnalysis.import.batches')}>
            <caption>{t('salesAnalysis.import.batches')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('salesAnalysis.import.kind')}</th>
                <th scope="col">{t('salesAnalysis.import.file')}</th>
                <th scope="col">{t('salesAnalysis.import.range')}</th>
                <th scope="col">{t('salesAnalysis.import.lines')}</th>
                <th scope="col">{t('salesAnalysis.import.total')}</th>
                <th scope="col">{t('salesAnalysis.import.importedAt')}</th>
              </tr>
            </thead>
            <tbody>
              {batches.map((b) => (
                <tr key={b.publicId}>
                  <th scope="row">{t(`salesAnalysis.import.kinds.${b.kind}`)}</th>
                  <td>{b.fileName}</td>
                  <td>{`${fm.date(b.dateFrom)} – ${fm.date(b.dateTo)}`}</td>
                  <td>{fm.units(b.lineCount)}</td>
                  <td>{fm.money(b.totalAmount)}</td>
                  <td>{`${fm.date(b.importedAtUtc)}${b.importedBy ? ` · ${b.importedBy}` : ''}`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div className="r2 sa-import-form">
        <div className="f">
          <label htmlFor="sa-imp-sales">{t('salesAnalysis.import.salesFile')}</label>
          <input id="sa-imp-sales" type="file" accept=".csv,text/csv" onChange={pick(setSalesFile)} />
          <span className="sa-muted">{t('salesAnalysis.import.salesHelp')}</span>
        </div>
        <div className="f">
          <label htmlFor="sa-imp-purchases">{t('salesAnalysis.import.purchasesFile')}</label>
          <input id="sa-imp-purchases" type="file" accept=".csv,text/csv" onChange={pick(setPurchasesFile)} />
          <span className="sa-muted">{t('salesAnalysis.import.purchasesHelp')}</span>
        </div>
        <div className="f">
          <label htmlFor="sa-imp-cutoff">{t('salesAnalysis.import.cutoff')}</label>
          <input id="sa-imp-cutoff" type="date" value={cutoffDate} onChange={(e) => (setCutoffDate(e.target.value), setReport(null))} />
          <span className="sa-muted">{t('salesAnalysis.import.cutoffHelp')}</span>
        </div>
        <div className="f sa-check">
          <label>
            <input type="checkbox" checked={repsFromCustomField} onChange={(e) => (setRepsFromCustomField(e.target.checked), setReport(null))} /> {t('salesAnalysis.import.repsFromField')}
          </label>
        </div>
      </div>
      {error && <p className="ferr" role="alert">{error}</p>}
      <div className="sa-inline sa-actions-row">
        <button type="button" className="btn flow" disabled={!hasFile || run.isPending} onClick={() => void go(true)}>
          {run.isPending && !confirm ? t('salesAnalysis.import.simulating') : t('salesAnalysis.import.simulate')}
        </button>
        <button type="button" className="btn" disabled={!hasFile || !simulated || run.isPending} onClick={() => setConfirm(true)} title={!simulated ? t('salesAnalysis.import.simulateFirst') : undefined}>
          {t('salesAnalysis.import.real')}
        </button>
      </div>
      {report && <ImportReport report={report} onLink={onLink} />}
      <ConfirmDialog
        open={confirm}
        title={t('salesAnalysis.import.confirmTitle')}
        message={t('salesAnalysis.import.confirmMsg', { files: [salesFile?.name, purchasesFile?.name].filter(Boolean).join(', ') })}
        confirmLabel={t('salesAnalysis.import.real')}
        onConfirm={() => go(false)}
        onClose={() => setConfirm(false)}
      />
    </Panel>
  )
}

export function ImportReport({ report, onLink }: { report: SalesImportReport; onLink?: () => void }) {
  const t = useT()
  return (
    <section className="sa-report" aria-label={t('salesAnalysis.import.result')}>
      <h3>
        {report.dryRun ? t('salesAnalysis.import.dryRunResult') : t('salesAnalysis.import.realResult')} <Chip tone={report.dryRun ? 'disp' : 'deliv'}>{report.dryRun ? t('salesAnalysis.import.nothingSaved') : t('salesAnalysis.import.saved')}</Chip>
      </h3>
      {report.cutoffDate && <p className="sa-muted">{t('salesAnalysis.import.cutoffIs', { date: report.cutoffDate })}</p>}
      <Warnings items={report.warnings} />
      {(report.files ?? []).map((f, i) => (
        <FileReport key={`${f.kind}-${i}`} file={f} onLink={onLink} />
      ))}
    </section>
  )
}

function FileReport({ file, onLink }: { file: SalesImportFile; onLink?: () => void }) {
  const t = useT()
  const lang = useLang()
  const fm = useSaFormat()
  const ok = totalsMatch(file)
  const unmatchedClients = file.unmatchedClients ?? []
  const unmatchedItems = file.unmatchedItems ?? []
  return (
    <article className="sa-file">
      <h4>
        {t(`salesAnalysis.import.kinds.${file.kind}`)} · <span className="mono">{file.fileName}</span>{' '}
        {file.action && <Chip tone="cap">{t(`salesAnalysis.import.actions.${file.action}`, { n: file.replacedLines ?? 0 })}</Chip>}
      </h4>
      <dl className="sa-facts">
        <div><dt>{t('salesAnalysis.import.linesRead')}</dt><dd>{fm.units(file.linesRead)}</dd></div>
        <div><dt>{t('salesAnalysis.import.linesSaved')}</dt><dd>{fm.units(file.linesSaved)}</dd></div>
        {(file.zeroQtySkipped ?? 0) > 0 && <div><dt>{t('salesAnalysis.import.zeroSkipped')}</dt><dd>{fm.units(file.zeroQtySkipped)}</dd></div>}
        {(file.duplicatesSkipped ?? 0) > 0 && <div><dt>{t('salesAnalysis.import.dupSkipped')}</dt><dd>{fm.units(file.duplicatesSkipped)}</dd></div>}
        <div><dt>{t('salesAnalysis.import.range')}</dt><dd>{`${fm.date(file.dateFrom)} – ${fm.date(file.dateTo)}`}</dd></div>
        <div><dt>{t('salesAnalysis.import.total')}</dt><dd>{fm.money(file.totalAmount)}</dd></div>
        <div>
          <dt>{t('salesAnalysis.import.reportTotal')}</dt>
          <dd>
            {fm.money(file.reportTotal)} <Chip tone={ok ? 'deliv' : 'fail'}>{ok ? t('salesAnalysis.import.totalsMatch') : t('salesAnalysis.import.totalsDiffer')}</Chip>
          </dd>
        </div>
        {file.kind === 'SALES' && <div><dt>{t('salesAnalysis.import.matchedClients')}</dt><dd>{fm.units(file.matchedClients)}</dd></div>}
        <div><dt>{t('salesAnalysis.import.matchedItems')}</dt><dd>{fm.units(file.matchedItems)}</dd></div>
      </dl>
      <div className="sa-scroll">
        <table className="sa-mini" aria-label={t('salesAnalysis.import.byClass')}>
          <caption>{t('salesAnalysis.import.byClass')}</caption>
          <thead>
            <tr>
              <th scope="col">{file.kind === 'PURCHASES' ? t('salesAnalysis.import.txType') : t('salesAnalysis.import.class')}</th>
              <th scope="col">{t('salesAnalysis.import.lines')}</th>
              <th scope="col">{t('salesAnalysis.import.amount')}</th>
            </tr>
          </thead>
          <tbody>
            {classRows(file).map((c) => (
              <tr key={c.cls}>
                <th scope="row">{(LINE_CLASSES as readonly string[]).includes(c.cls) ? t(`salesAnalysis.lineClass.${c.cls}`) : c.cls}</th>
                <td>{fm.units(c.lines)}</td>
                <td>{fm.money(c.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {(file.byMonth?.length ?? 0) > 0 && (
        <details className="sa-chart-data">
          <summary>{t('salesAnalysis.import.byMonth')}</summary>
          <div className="sa-scroll">
            <table className="sa-mini">
              <thead>
                <tr>
                  <th scope="col">{t('salesAnalysis.charts.month')}</th>
                  <th scope="col">{t('salesAnalysis.import.lines')}</th>
                  <th scope="col">{t('salesAnalysis.col.units')}</th>
                  <th scope="col">{t('salesAnalysis.import.amount')}</th>
                  <th scope="col">{t('salesAnalysis.import.productAmount')}</th>
                </tr>
              </thead>
              <tbody>
                {(file.byMonth ?? []).map((m) => (
                  <tr key={m.month}>
                    <th scope="row">{monthShort(m.month ?? '', lang)}</th>
                    <td>{fm.units(m.lines)}</td>
                    <td>{fm.units(m.quantity)}</td>
                    <td>{fm.money(m.amount)}</td>
                    <td>{fm.money(m.productAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}
      {file.kind !== 'PURCHASES' && <UnmatchedList title={t('salesAnalysis.import.unmatchedClients', { n: unmatchedClients.length })} rows={unmatchedClients} />}
      <UnmatchedList title={t('salesAnalysis.import.unmatchedItems', { n: unmatchedItems.length })} rows={unmatchedItems} />
      {(unmatchedClients.length > 0 || unmatchedItems.length > 0) && <p className="sa-muted">{t('salesAnalysis.import.unmatchedHelp')}{onLink && <> <button type="button" className="btn sm" onClick={onLink}>{t('salesAnalysis.import.goLink')}</button></>}</p>}
      <Warnings items={file.warnings} />
    </article>
  )
}

export function UnmatchedList({ title, rows, action }: { title: string; rows: readonly SalesUnmatched[]; action?: (r: SalesUnmatched) => React.ReactNode }) {
  const t = useT()
  const fm = useSaFormat()
  return (
    <div className="sa-scroll">
      <table className="sa-mini" aria-label={title}>
        <caption>{title}</caption>
        {rows.length === 0 ? (
          <tbody>
            <tr>
              <td className="sa-muted">{t('salesAnalysis.import.allMatched')}</td>
            </tr>
          </tbody>
        ) : (
          <>
            <thead>
              <tr>
                <th scope="col">{t('salesAnalysis.import.raw')}</th>
                <th scope="col">{t('salesAnalysis.import.lines')}</th>
                <th scope="col">{t('salesAnalysis.import.amount')}</th>
                <th scope="col">{t('salesAnalysis.import.range')}</th>
                {action && <th scope="col"><span className="sr-only">{t('salesAnalysis.link.action')}</span></th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.raw}>
                  <th scope="row">{r.raw}</th>
                  <td>{fm.units(r.lines)}</td>
                  <td>{fm.money(r.amount)}</td>
                  <td>{`${fm.date(r.firstDate)} – ${fm.date(r.lastDate)}`}</td>
                  {action && <td>{action(r)}</td>}
                </tr>
              ))}
            </tbody>
          </>
        )}
      </table>
    </div>
  )
}
