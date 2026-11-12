// Lote 241 — piezas pequeñas del Análisis de ventas: tarjetas de cifras, barra de cumplimiento, chips de ABC / estado /
// cobertura, selector de mes y el aviso de error de una consulta.
import type { ReactNode } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLang, useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import {
  COMPLIANCE_TONE,
  COVERAGE_TONE,
  abcTone,
  complianceLevel,
  complianceWidth,
  coverageLevel,
  monthLong,
  statusKey,
  statusTone,
} from './salesAnalysisRules'
import { useSaFormat } from './saFormat'

export interface KpiTile {
  key: string
  label: string
  value: ReactNode
  sub?: ReactNode
  tone?: 'up' | 'down' | 'flat' | 'met' | 'near' | 'below' | 'none'
  extra?: ReactNode
}

/** Tarjetas de cifras de arriba de una vista (rejilla que envuelve: una por renglón a 360 px). */
export function KpiTiles({ items, label, loading }: { items: KpiTile[]; label: string; loading?: boolean }) {
  return (
    <div className="sa-kpis" role="group" aria-label={label} aria-busy={loading || undefined}>
      {items.map((x) => (
        <div key={x.key} className={`sa-kpi${x.tone ? ` t-${x.tone}` : ''}`} role="group" aria-label={x.label}>
          <div className="sa-kpi-label">{x.label}</div>
          <div className="sa-kpi-value">{loading ? '…' : x.value}</div>
          {x.sub && <div className="sa-kpi-sub">{x.sub}</div>}
          {x.extra}
        </div>
      ))}
    </div>
  )
}

/** Barra de cumplimiento de cuota con el porcentaje (semáforo: verde ≥ 100 %, ámbar ≥ 90 %, rojo debajo). */
export function ComplianceBar({ value }: { value: number | null | undefined }) {
  const t = useT()
  const fm = useSaFormat()
  const level = complianceLevel(value)
  if (level === 'none') return <span className="sa-muted">{t('salesAnalysis.noQuota')}</span>
  return (
    <span className={`sa-comp c-${level}`} title={t(`salesAnalysis.compliance.${level}`)}>
      <span className="sa-comp-track" aria-hidden="true">
        <span className="sa-comp-fill" style={{ width: `${complianceWidth(value)}%` }} />
      </span>
      <Chip tone={COMPLIANCE_TONE[level]}>{fm.pct(value)}</Chip>
    </span>
  )
}

export function AbcChip({ abc }: { abc: string | null | undefined }) {
  return <Chip tone={abcTone(abc)}>{abc || '-'}</Chip>
}

export function MovementStatusChip({ status }: { status: string | null | undefined }) {
  const t = useT()
  const key = statusKey(status)
  return <Chip tone={statusTone(status)}>{key ? t(key) : (status ?? '—')}</Chip>
}

/** Semanas de cobertura con su color (rojo poco, ámbar, verde mucho; sin promedio semanal = «sin base»). */
export function CoverageChip({ weeks, months }: { weeks: number | null | undefined; months?: number | null }) {
  const t = useT()
  const fm = useSaFormat()
  const level = coverageLevel(weeks)
  if (level === 'none') return <Chip tone="cap">{t('salesAnalysis.coverage.none')}</Chip>
  return (
    <Chip tone={COVERAGE_TONE[level]} title={months !== null && months !== undefined ? t('salesAnalysis.coverage.months', { n: fm.dec1(months) }) : undefined}>
      {t('salesAnalysis.charts.weeks', { n: fm.dec1(weeks) })}
    </Chip>
  )
}

export interface MonthSelectProps {
  id: string
  label: string
  value: string
  options: readonly string[]
  onChange: (v: string) => void
  /** Texto de la opción vacía (p. ej. «El anterior»); sin él no hay opción vacía. */
  emptyLabel?: string
}

/** Selector de mes ('AAAA-MM') con el nombre del mes en el idioma. */
export function MonthSelect({ id, label, value, options, onChange, emptyLabel }: MonthSelectProps) {
  const lang = useLang()
  const opts = value && !options.includes(value) ? [value, ...options] : options
  return (
    <div className="f">
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
        {emptyLabel !== undefined && <option value="">{emptyLabel}</option>}
        {opts.map((m) => (
          <option key={m} value={m}>
            {monthLong(m, lang)}
          </option>
        ))}
      </select>
    </div>
  )
}

/** Error de una consulta (el título del ProblemDetails). */
export function QueryError({ error }: { error: unknown }) {
  return (
    <p className="ferr" role="alert">
      {applyProblemDetails(error).title}
    </p>
  )
}

/** Avisos del servidor (`warnings`). */
export function Warnings({ items }: { items: readonly string[] | null | undefined }) {
  if (!items || items.length === 0) return null
  return (
    <div className="note warn sa-warnings" role="status">
      <ul>
        {items.map((w) => (
          <li key={w}>{w}</li>
        ))}
      </ul>
    </div>
  )
}
