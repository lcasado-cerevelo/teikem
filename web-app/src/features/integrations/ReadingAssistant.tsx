// Asistente de lectura (2026-10-24): una ventana que arma las expresiones de un lector SIN que la persona las escriba. Se sube (o pega) un documento de muestra, se señala en él un renglón
// (tabla), un dato suelto o el total que el documento declara, y el asistente propone la expresión, la interpreta en palabras y cuenta cuántos renglones del documento coinciden.
import { useMemo, useState } from 'react'
import { useT } from '../../kernel/i18n'
import { Modal } from '../../kernel/ui'
import { useReadSample } from './integrationsApi'
import { columnsFor, countMatches, lineRegexFor, matchingLines, sampleLines, singlePatternFor, splitCells, totalPatternFor, type CellAssignment, type Datum } from './assistantRules'

export type AssistantMode = 'table' | 'single' | 'total'
export type AssistantResult =
  | { kind: 'BLOCK'; position: number; lineRegex: string; columns: string }
  | { kind: 'REGEX'; pattern: string }
  | { kind: 'TOTAL'; pattern: string }

// la última muestra se recuerda mientras no se cierre la pantalla: el mismo documento sirve para armar varios campos
let lastSample = ''

const show = (line: string) => line.replace(/\t/g, '  ⇥  ')

export function ReadingAssistant({ open, mode, datums, onClose, onApply }: { open: boolean; mode: AssistantMode; datums: Datum[] | null; onClose: () => void; onApply: (r: AssistantResult) => void }) {
  const t = useT()
  const readSample = useReadSample()
  const [text, setText] = useState(lastSample)
  const [info, setInfo] = useState('')
  const [picked, setPicked] = useState<number | null>(null)
  const [token, setToken] = useState('')
  const [assign, setAssign] = useState<Record<number, CellAssignment>>({})
  const lines = useMemo(() => sampleLines(text), [text])
  const sample = picked !== null ? (lines[picked] ?? '') : ''
  const cells = useMemo(() => (sample ? splitCells(sample) : []), [sample])

  const onFile = async (file: File | undefined) => {
    if (!file) return
    setInfo('')
    try {
      const r = await readSample.mutateAsync(file)
      if (r.problem) setInfo(r.problem)
      else setInfo(r.usedOcr ? t('integrations.assistant.readOcr', { pages: r.pages, confidence: r.confidence ?? 0 }) : t('integrations.assistant.readText', { pages: r.pages }))
      lastSample = r.text ?? ''
      setText(lastSample)
      setPicked(null)
    } catch {
      setInfo(t('integrations.assistant.readFailed'))
    }
  }
  const onPaste = (v: string) => {
    lastSample = v
    setText(v)
    setPicked(null)
  }

  // lo que propone el asistente con lo señalado
  const proposal = useMemo<AssistantResult | null>(() => {
    if (picked === null || !sample) return null
    if (mode === 'table') {
      const columns = columnsFor(assign)
      if (!columns) return null
      const first = Math.min(...Object.entries(assign).filter(([, a]) => a.cell > 0).map(([p]) => Number(p)))
      return { kind: 'BLOCK', position: first, lineRegex: lineRegexFor(sample), columns }
    }
    if (!token) return null
    const pattern = mode === 'total' ? totalPatternFor(sample, token) : singlePatternFor(sample, token)
    return pattern ? (mode === 'total' ? { kind: 'TOTAL', pattern } : { kind: 'REGEX', pattern }) : null
  }, [picked, sample, mode, assign, token])

  const regexOf = proposal?.kind === 'BLOCK' ? proposal.lineRegex : proposal?.kind === 'REGEX' || proposal?.kind === 'TOTAL' ? proposal.pattern : ''
  const found = regexOf ? countMatches(lines, regexOf) : 0
  const examples = regexOf ? matchingLines(lines, regexOf, 6) : []
  const setCell = (position: number, cell: number) => setAssign((a) => ({ ...a, [position]: { cell, divideBy: a[position]?.divideBy ?? 0 } }))
  const setDivide = (position: number, divideBy: number) => setAssign((a) => ({ ...a, [position]: { cell: a[position]?.cell ?? 0, divideBy } }))
  const cellOptions = cells.map((c, i) => ({ value: i + 1, label: `${i + 1}: ${c || t('integrations.assistant.emptyCell')}` }))
  const rows: Datum[] = datums ?? Array.from({ length: 12 }, (_, i) => ({ position: i + 1, label: t('integrations.assistant.columnN', { n: i + 1 }) }))
  const tokens = sample.split(/[\s\t]+/).filter(Boolean)

  const explain = () => {
    if (!proposal) return null
    if (proposal.kind === 'BLOCK') {
      const parts = Object.entries(assign)
        .filter(([, a]) => a.cell > 0)
        .sort(([a], [b]) => Number(a) - Number(b))
        .map(([p, a]) => {
          const name = rows.find((r) => r.position === Number(p))?.label ?? t('integrations.assistant.columnN', { n: Number(p) })
          return a.divideBy > 0 ? t('integrations.assistant.explainDivide', { name, a: a.cell, b: a.divideBy }) : t('integrations.assistant.explainCell', { name, a: a.cell })
        })
      return `${t('integrations.assistant.explainRows', { n: cells.length })} ${parts.join('; ')}.`
    }
    return proposal.kind === 'TOTAL' ? t('integrations.assistant.explainTotal', { value: token }) : t('integrations.assistant.explainSingle', { value: token })
  }

  const titleKey = mode === 'table' ? 'integrations.assistant.titleTable' : mode === 'single' ? 'integrations.assistant.titleSingle' : 'integrations.assistant.titleTotal'
  return (
    <Modal
      open={open}
      title={t(titleKey)}
      onClose={onClose}
      size="xl"
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('integrations.assistant.cancel')}
          </button>
          <button type="button" className="btn pri" disabled={!proposal || found < 1} onClick={() => proposal && onApply(proposal)}>
            {t('integrations.assistant.apply')}
          </button>
        </>
      }
    >
      <div className="int-as">
        <p className="help">{t(mode === 'table' ? 'integrations.assistant.introTable' : mode === 'single' ? 'integrations.assistant.introSingle' : 'integrations.assistant.introTotal')}</p>

        <section className="int-as-sec">
          <h4 className="int-step-h">
            <span className="int-step-n" aria-hidden="true">
              1
            </span>
            {t('integrations.assistant.step1')}
          </h4>
          <label className="int-as-file">
            <span>{t('integrations.assistant.upload')}</span>
            <input type="file" accept="application/pdf,.pdf" onChange={(e) => void onFile(e.target.files?.[0])} disabled={readSample.isPending} />
          </label>
          {readSample.isPending && <p className="help">{t('integrations.assistant.reading')}</p>}
          {info && <p className="help" role="status">{info}</p>}
          <label className="int-as-paste">
            <span>{t('integrations.assistant.paste')}</span>
            <textarea rows={3} value={text} onChange={(e) => onPaste(e.target.value)} aria-label={t('integrations.assistant.paste')} />
          </label>
        </section>

        {lines.length > 0 && (
          <section className="int-as-sec">
            <h4 className="int-step-h">
              <span className="int-step-n" aria-hidden="true">
                2
              </span>
              {t(mode === 'table' ? 'integrations.assistant.step2Table' : mode === 'single' ? 'integrations.assistant.step2Single' : 'integrations.assistant.step2Total')}
            </h4>
            <div className="int-as-lines" role="listbox" aria-label={t('integrations.assistant.linesLabel')}>
              {lines.map((l, i) => (
                <button key={i} type="button" role="option" aria-selected={picked === i} className={`int-as-line${picked === i ? ' on' : ''}`} onClick={() => { setPicked(i); setToken(''); setAssign({}) }}>
                  <span className="int-as-ln">{i + 1}</span>
                  <span className="int-as-lt">{show(l)}</span>
                </button>
              ))}
            </div>
          </section>
        )}

        {picked !== null && mode === 'table' && (
          <section className="int-as-sec">
            <h4 className="int-step-h">
              <span className="int-step-n" aria-hidden="true">
                3
              </span>
              {t('integrations.assistant.step3Table')}
            </h4>
            <p className="help">{t('integrations.assistant.cellsHelp', { n: cells.length })}</p>
            <div className="int-as-cells">
              {cells.map((c, i) => (
                <span key={i} className="int-as-cell">
                  <b>{i + 1}</b> {c || '—'}
                </span>
              ))}
            </div>
            <table className="int-as-map">
              <thead>
                <tr>
                  <th scope="col">{t('integrations.assistant.colDatum')}</th>
                  <th scope="col">{t('integrations.assistant.colCell')}</th>
                  <th scope="col">{t('integrations.assistant.colDivide')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((d) => (
                  <tr key={d.position}>
                    <th scope="row">{d.label}</th>
                    <td>
                      <select aria-label={t('integrations.assistant.cellFor', { name: d.label })} value={assign[d.position]?.cell ?? 0} onChange={(e) => setCell(d.position, Number(e.target.value))}>
                        <option value={0}>{t('integrations.assistant.notUsed')}</option>
                        {cellOptions.map((o) => (
                          <option key={o.value} value={o.value}>
                            {o.label}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <select aria-label={t('integrations.assistant.divideFor', { name: d.label })} value={assign[d.position]?.divideBy ?? 0} disabled={!assign[d.position]?.cell} onChange={(e) => setDivide(d.position, Number(e.target.value))}>
                        <option value={0}>{t('integrations.assistant.noDivide')}</option>
                        {cellOptions.map((o) => (
                          <option key={o.value} value={o.value}>
                            {o.label}
                          </option>
                        ))}
                      </select>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="help">{t('integrations.assistant.divideHelp')}</p>
          </section>
        )}

        {picked !== null && mode !== 'table' && (
          <section className="int-as-sec">
            <h4 className="int-step-h">
              <span className="int-step-n" aria-hidden="true">
                3
              </span>
              {t(mode === 'total' ? 'integrations.assistant.step3Total' : 'integrations.assistant.step3Single')}
            </h4>
            <div className="int-as-cells">
              {tokens.map((k, i) => (
                <button key={i} type="button" aria-pressed={token === k} className={`int-as-cell btn-like${token === k ? ' on' : ''}`} onClick={() => setToken(k)}>
                  {k}
                </button>
              ))}
            </div>
          </section>
        )}

        {picked !== null && (
          <section className="int-as-sec int-as-result" aria-live="polite">
            <h4 className="int-step-h">
              <span className="int-step-n" aria-hidden="true">
                4
              </span>
              {t('integrations.assistant.step4')}
            </h4>
            {proposal ? (
              <>
                <p>{explain()}</p>
                <p className={found > 0 ? 'int-as-ok' : 'int-as-bad'}>{found > 0 ? t('integrations.assistant.matches', { n: found, total: lines.length }) : t('integrations.assistant.noMatches')}</p>
                {examples.length > 0 && (
                  <ul className="int-as-examples">
                    {examples.map((l, i) => (
                      <li key={i}>{show(l)}</li>
                    ))}
                  </ul>
                )}
                <details className="int-adv">
                  <summary>{t('integrations.assistant.seeExpression')}</summary>
                  <code className="int-as-code">{regexOf}</code>
                  {proposal.kind === 'BLOCK' && <code className="int-as-code">{proposal.columns}</code>}
                </details>
              </>
            ) : (
              <p className="help">{t(mode === 'table' ? 'integrations.assistant.waitTable' : 'integrations.assistant.waitToken')}</p>
            )}
          </section>
        )}
      </div>
    </Modal>
  )
}
