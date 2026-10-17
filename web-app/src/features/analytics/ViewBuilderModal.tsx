// Lote F-F2 — constructor de vistas («Nueva vista» / «Editar vista», `analytics.manage`): nombre, fuente principal (ya decidida por el
// panel desde el que se abrió), fuentes combinadas, columnas por casillas, filtros (los 16 operadores del DSL, unidos con Y), agrupar
// por varios campos con agregados y fila de totales, orden, visibilidad y vista previa con datos reales (`POST .../preview`).
// Los errores del servidor salen con `applyProblemDetails`: el título en un aviso y cada campo bajo su sección.
import { useId, useMemo, useState, type ReactNode } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import { Modal } from '../../kernel/ui/Modal'
import { SearchMultiSelect } from '../../kernel/ui/SearchSelect'
import { toast } from '../../kernel/ui/toast'
import { useRoles } from '../system/api'
import { useAnalyticsShareUsers, useDataSources } from './api'
import { blankFilterRow, type ShareDto } from './definitions'
import { FilterRowEditor } from './FilterRowEditor'
import { ReportResultTable } from './ReportResultTable'
import { ReportScheduleFields } from './ReportScheduleParts'
import { usePreviewReport, useSaveReport } from './reportsApi'
import { useDebounced } from '../warehouse/lineRules'
import {
  AGGREGATE_FNS,
  PREVIEW_ROWS,
  blankDraft,
  buildReportBody,
  combinableRelations,
  decorateResult,
  draftFromReport,
  fieldGroups,
  fieldIndex,
  flatFields,
  hasIssues,
  newAggregate,
  nameTaken,
  previewBody,
  setUsersCanEdit,
  serializeGroupJson,
  sortChoices,
  usersCanEdit,
  validateDraft,
  withAggregateFn,
  withColumn,
  withFilterRows,
  withGroupBy,
  withRelation,
  type AggregateFn,
  type AggregateRow,
  type DraftIssues,
  type ReportDto,
  type ViewDraft,
} from './viewRules'

export interface ViewBuilderModalProps {
  /** Fuente principal (alta). En la edición sale de la vista. */
  source: string
  /** `null` = alta. */
  report: ReportDto | null
  /** Vistas ya cargadas (para el nombre repetido al editar). */
  reports: readonly ReportDto[]
  onClose: () => void
  onSaved?: (report: ReportDto) => void
}

const EMPTY_LOOKUPS: ReturnType<typeof useLookups>['data'] = []

function Block({ title, hint, error, children }: { title: string; hint?: string; error?: string | null; children: ReactNode }) {
  return (
    <section className="vw-block">
      <div className="ph2 vw-ph">{title}</div>
      {hint && <p className="help">{hint}</p>}
      {children}
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </section>
  )
}

export function ViewBuilderModal({ source, report, reports, onClose, onSaved }: ViewBuilderModalProps) {
  const t = useT()
  const sources = useDataSources().data
  const visibilities = useLookups('ReportVisibility').data ?? EMPTY_LOOKUPS
  const canBrowseUsers = useCan('admin.users')
  const shareUsers = useAnalyticsShareUsers(canBrowseUsers)
  const shareRoles = useRoles()
  const save = useSaveReport()
  const formId = useId()

  const [draft, setDraft] = useState<ViewDraft>(() => (report ? draftFromReport(report) : blankDraft(source)))
  const [attempted, setAttempted] = useState(false)
  const [nameError, setNameError] = useState<string | null>(null)
  const [serverErrors, setServerErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const isNew = report == null
  // tras el primer intento de guardar, los avisos se quitan solos al corregir
  const issues: DraftIssues | null = attempted ? validateDraft(draft) : null

  const allSources = useMemo(() => sources ?? [], [sources])
  const src = allSources.find((s) => s.key === draft.source)
  const groups = useMemo(() => fieldGroups(src, allSources, draft.secondary), [src, allSources, draft.secondary])
  const fields = useMemo(() => flatFields(groups), [groups])
  const index = useMemo(() => fieldIndex(src, allSources), [src, allSources])
  const numericFields = useMemo(() => fields.filter((f) => f.type === 'Number'), [fields])
  const relations = combinableRelations(src, allSources)
  const grouped = draft.groupBy.length > 0

  const aggLabel = (a: AggregateRow) => {
    const fn = t(`analytics.reports.fn.${a.fn}`)
    return a.field ? `${fn} · ${index.get(a.field)?.label ?? a.field}` : fn
  }
  const choices = useMemo(() => sortChoices(draft, index, aggLabel), [draft, index, t]) // eslint-disable-line react-hooks/exhaustive-deps

  const update = (patch: Partial<ViewDraft>) => setDraft((d) => ({ ...d, ...patch }))

  // ---- vista previa (con retardo entre cambios) ----
  const previewKey = JSON.stringify(previewBody(draft, choices))
  const settledKey = useDebounced(previewKey, 400)
  const settledBody = useMemo(() => JSON.parse(settledKey) as ReturnType<typeof previewBody>, [settledKey])
  const preview = usePreviewReport(draft.source, settledBody, PREVIEW_ROWS)

  // ---- compartir ----
  const userOptions = (shareUsers.data ?? []).map((u) => ({ value: String(u.id), label: u.email ? `${u.fullName ?? ''} · ${u.email}` : (u.fullName ?? '') }))
  const shareUserValue = draft.shares.filter((s) => s.userId != null).map((s) => String(s.userId))
  const shareRoleValue = draft.shares.filter((s) => s.roleId != null).map((s) => String(s.roleId))
  const roleOptions = (shareRoles.data ?? [])
    .filter((r) => r.id != null && (r.isActive !== false || shareRoleValue.includes(String(r.id))))
    .map((r) => ({ value: String(r.id), label: r.name ?? String(r.id) }))
  const setShareUsers = (value: string[]) =>
    update({
      shares: [
        ...draft.shares.filter((s) => s.userId == null),
        ...value.map((id): ShareDto => ({ userId: Number(id), roleId: null, canEdit: draft.shares.find((s) => s.userId === Number(id))?.canEdit ?? false })),
      ],
    })
  const setShareRoles = (value: string[]) =>
    update({ shares: [...draft.shares.filter((s) => s.roleId == null), ...value.map((id): ShareDto => ({ userId: null, roleId: Number(id), canEdit: false }))] })

  const submit = async () => {
    setServerErrors({})
    const found = validateDraft(draft)
    setAttempted(true)
    const taken = !isNew && nameTaken(draft.name, draft.source, reports, draft.id)
    setNameError(taken ? t('analytics.reports.builder.nameTaken', { name: draft.name.trim(), source: draft.source }) : null)
    if (hasIssues(found) || taken) return
    setBusy(true)
    try {
      const saved = await save.mutateAsync({ id: draft.id, source: draft.source, body: buildReportBody(draft, choices) })
      toast.success(t(isNew ? 'analytics.reports.builder.saved' : 'analytics.reports.builder.updated', { name: saved.name ?? draft.name }))
      onSaved?.(saved)
      onClose()
    } catch (err) {
      const p = applyProblemDetails(err)
      const byField: Record<string, string> = {}
      for (const [k, v] of Object.entries(p.errors)) byField[k] = v.join(' ')
      // 409 (nombre repetido) no trae campo: es del nombre
      if (p.code === 'conflict' && !byField.name) byField.name = p.title
      byField._title = p.title
      setServerErrors(byField)
      toast.error(p.title)
    } finally {
      setBusy(false)
    }
  }

  const decorated = decorateResult(preview.data, { groupJson: serializeGroupJson({ by: draft.groupBy, aggregates: draft.aggregates, totals: false }) }, index, (fn) => t(`analytics.reports.fn.${fn}`))
  const err = (field: string) => serverErrors[field] ?? null
  const otherServerError = serverErrors._title && !['name', 'columns', 'secondary', 'filterJson', 'groupJson', 'sortJson', 'scheduleCron', 'deliveryEmails', 'deliveryRangeMode'].some((k) => serverErrors[k]) && !(serverErrors.name === serverErrors._title) ? serverErrors._title : null
  const title = isNew ? t('analytics.reports.builder.newTitle') : t('analytics.reports.builder.editTitle')

  const fieldSelect = (value: string, onChange: (v: string) => void, label: string, options: { key: string; label: string }[], extra?: ReactNode) => (
    <div className="f">
      <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)}>
        {extra}
        {options.map((o) => (
          <option key={o.key} value={o.key}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  )

  return (
    <Modal
      open
      size="lg"
      title={title}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('analytics.reports.builder.saveBtn')}
          </button>
        </>
      }
    >
      <form
        id={formId}
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
      >
        {otherServerError && (
          <p className="ferr" role="alert">
            {otherServerError}
          </p>
        )}
        <div className="f">
          <label htmlFor={`${formId}-name`}>{t('analytics.reports.builder.nameLabel')} *</label>
          <input
            id={`${formId}-name`}
            value={draft.name}
            maxLength={150}
            placeholder={t('analytics.reports.builder.namePh')}
            aria-invalid={Boolean(issues?.name || nameError || err('name')) || undefined}
            onChange={(e) => update({ name: e.target.value })}
          />
          {issues?.name && (
            <p className="ferr" role="alert">
              {t('analytics.reports.builder.needName')}
            </p>
          )}
          {(nameError || err('name')) && (
            <p className="ferr" role="alert">
              {nameError ?? err('name')}
            </p>
          )}
        </div>

        <div className="r2">
          <div className="f">
            <span className="vw-lbl">{t('analytics.reports.builder.primaryLabel')}</span>
            <div className="vw-primary mono">{src?.label ?? draft.source}</div>
          </div>
          <div className="f">
            <span className="vw-lbl">{t('analytics.reports.builder.combineLabel')}</span>
            {relations.length === 0 ? (
              <p className="help">{t('analytics.reports.builder.noCombine')}</p>
            ) : (
              <div className="vw-checks">
                {relations.map((r) => (
                  <label key={r.key} className="vw-check">
                    <input type="checkbox" checked={draft.secondary.includes(r.key ?? '')} onChange={(e) => setDraft((d) => withRelation(d, r.key ?? '', e.target.checked))} />{' '}
                    {r.label ?? r.key}
                  </label>
                ))}
              </div>
            )}
            {err('secondary') && (
              <p className="ferr" role="alert">
                {err('secondary')}
              </p>
            )}
          </div>
        </div>

        <Block
          title={t('analytics.reports.builder.columnsTitle')}
          hint={grouped ? t('analytics.reports.builder.columnsGrouped') : undefined}
          error={grouped ? null : issues?.columns ? t('analytics.reports.builder.needColumn') : err('columns')}
        >
          {!grouped &&
            groups.map((g) => (
              <div key={g.id || 'main'} className="vw-cols" role="group" aria-label={g.id === 'cf' ? t('analytics.reports.builder.customFields') : g.label}>
                <div className="vw-cols-h">{g.id === 'cf' ? t('analytics.reports.builder.customFields') : g.label}</div>
                <div className="vw-checks">
                  {g.fields.map((f) => (
                    <label key={f.key} className="vw-check">
                      <input type="checkbox" checked={draft.columns.includes(f.key)} onChange={(e) => setDraft((d) => withColumn(d, f.key, e.target.checked))} />{' '}
                      {g.id && g.id !== 'cf' ? f.label.replace(`${g.label} · `, '') : f.label}
                    </label>
                  ))}
                </div>
              </div>
            ))}
        </Block>

        <Block title={t('analytics.reports.builder.filtersTitle')} hint={t('analytics.reports.builder.filtersHint')} error={err('filterJson')}>
          {draft.keepFilter && <p className="help">{t('analytics.editor.filterUnrepresentable')}</p>}
          {draft.filters.map((row, i) => (
            <FilterRowEditor
              key={i}
              row={row}
              fields={fields}
              onChange={(patch) => setDraft((d) => withFilterRows(d, d.filters.map((r, j) => (j === i ? { ...r, ...patch } : r))))}
              onRemove={() => setDraft((d) => withFilterRows(d, d.filters.filter((_, j) => j !== i)))}
            />
          ))}
          <button
            type="button"
            className="btn sm"
            disabled={fields.length === 0}
            onClick={() => setDraft((d) => withFilterRows(d, [...d.filters, blankFilterRow(fields[0]?.key ?? '')]))}
          >
            {t('analytics.editor.addFilter')}
          </button>
        </Block>

        <Block title={t('analytics.reports.builder.groupTitle')} hint={t('analytics.reports.builder.groupHint')} error={err('groupJson')}>
          {draft.groupBy.map((g, i) => (
            <div key={i} className="vw-row">
              {fieldSelect(g, (v) => setDraft((d) => withGroupBy(d, d.groupBy.map((x, j) => (j === i ? v : x)))), t('analytics.reports.builder.groupField', { n: i + 1 }), fields)}
              <button
                type="button"
                className="btn sm"
                aria-label={t('analytics.reports.builder.removeGroup', { n: i + 1 })}
                onClick={() => setDraft((d) => withGroupBy(d, d.groupBy.filter((_, j) => j !== i)))}
              >
                ✕
              </button>
            </div>
          ))}
          <button type="button" className="btn sm" disabled={fields.length === 0} onClick={() => setDraft((d) => withGroupBy(d, [...d.groupBy, fields[0]?.key ?? '']))}>
            {t('analytics.reports.builder.addGroup')}
          </button>
          {grouped && (
            <>
              <div className="vw-totals">
                <label className="vw-check">
                  <input type="checkbox" checked={draft.showTotals} onChange={(e) => update({ showTotals: e.target.checked })} /> {t('analytics.reports.builder.showTotals')}
                </label>
              </div>
              <div className="ph2 vw-ph">{t('analytics.reports.builder.aggregatesTitle')}</div>
              <p className="help">{t('analytics.reports.builder.aggHint')}</p>
              {draft.aggregates.map((a, i) => (
                <div key={i} className="vw-row vw-agg">
                  <div className="f">
                    <select
                      aria-label={t('analytics.reports.builder.aggFn', { n: i + 1 })}
                      value={a.fn}
                      onChange={(e) =>
                        update({ aggregates: draft.aggregates.map((x, j) => (j === i ? withAggregateFn(x, e.target.value as AggregateFn, numericFields.map((f) => f.key)) : x)) })
                      }
                    >
                      {AGGREGATE_FNS.map((fn) => (
                        <option key={fn} value={fn}>
                          {t(`analytics.reports.fn.${fn}`)}
                        </option>
                      ))}
                    </select>
                  </div>
                  {fieldSelect(
                    a.field,
                    (v) => update({ aggregates: draft.aggregates.map((x, j) => (j === i ? { ...x, field: v } : x)) }),
                    t('analytics.reports.builder.aggField', { n: i + 1 }),
                    a.fn === 'COUNT' ? fields : numericFields,
                    a.fn === 'COUNT' ? <option value="">{t('analytics.reports.builder.allRows')}</option> : undefined,
                  )}
                  <button
                    type="button"
                    className="btn sm"
                    aria-label={t('analytics.reports.builder.removeAgg', { n: i + 1 })}
                    onClick={() => update({ aggregates: draft.aggregates.filter((_, j) => j !== i) })}
                  >
                    ✕
                  </button>
                  {issues?.aggregateFields.includes(i) && (
                    <p className="ferr" role="alert">
                      {t('analytics.reports.builder.aggFieldRequired', { fn: a.fn })}
                    </p>
                  )}
                </div>
              ))}
              <button type="button" className="btn sm" onClick={() => update({ aggregates: [...draft.aggregates, newAggregate()] })}>
                {t('analytics.reports.builder.addAgg')}
              </button>
            </>
          )}
        </Block>

        <Block title={t('analytics.reports.builder.sortTitle')} hint={t('analytics.reports.builder.sortHint')} error={err('sortJson')}>
          {draft.sort.map((s, i) => (
            <div key={i} className="vw-row vw-sort">
              {fieldSelect(
                s.field,
                (v) => update({ sort: draft.sort.map((x, j) => (j === i ? { ...x, field: v } : x)) }),
                t('analytics.reports.builder.sortField', { n: i + 1 }),
                choices,
                choices.some((c) => c.key === s.field) ? undefined : <option value={s.field}>{s.field}</option>,
              )}
              <div className="f">
                <select
                  aria-label={t('analytics.reports.builder.sortDir', { n: i + 1 })}
                  value={s.dir}
                  onChange={(e) => update({ sort: draft.sort.map((x, j) => (j === i ? { ...x, dir: e.target.value as 'asc' | 'desc' } : x)) })}
                >
                  <option value="asc">{t('analytics.reports.builder.asc')}</option>
                  <option value="desc">{t('analytics.reports.builder.desc')}</option>
                </select>
              </div>
              <button
                type="button"
                className="btn sm"
                aria-label={t('analytics.reports.builder.removeSort', { n: i + 1 })}
                onClick={() => update({ sort: draft.sort.filter((_, j) => j !== i) })}
              >
                ✕
              </button>
            </div>
          ))}
          <button
            type="button"
            className="btn sm"
            disabled={choices.length === 0}
            onClick={() => update({ sort: [...draft.sort, { field: choices[0]?.key ?? '', dir: 'asc' }] })}
          >
            {t('analytics.reports.builder.addSort')}
          </button>
        </Block>

        <Block title={t('analytics.reports.builder.visibilityTitle')} error={err('shares')}>
          <div className="f">
            <label htmlFor={`${formId}-vis`}>{t('analytics.indicators.visibilityLabel')}</label>
            <select id={`${formId}-vis`} value={draft.visibility} onChange={(e) => update({ visibility: e.target.value, shares: e.target.value === 'SHARED' ? draft.shares : [] })}>
              {visibilities.map((v) => (
                <option key={v.code} value={v.code}>
                  {v.label}
                </option>
              ))}
            </select>
          </div>
          {draft.visibility === 'SHARED' && (
            <>
              <div className="r2">
                {canBrowseUsers ? (
                  <div className="f">
                    <label htmlFor={`${formId}-share-users`} id={`${formId}-share-users-lbl`}>
                      {t('analytics.indicators.shareWithUsersLabel')}
                    </label>
                    <SearchMultiSelect
                      id={`${formId}-share-users`}
                      labelledBy={`${formId}-share-users-lbl`}
                      options={userOptions}
                      value={shareUserValue}
                      onChange={setShareUsers}
                      placeholder={t('analytics.indicators.shareWithUsersPh')}
                    />
                  </div>
                ) : (
                  <p className="help">{t('analytics.editor.shareNeedsAdminUsers')}</p>
                )}
                <div className="f">
                  <label htmlFor={`${formId}-share-roles`} id={`${formId}-share-roles-lbl`}>
                    {t('analytics.indicators.shareWithRolesLabel')}
                  </label>
                  <SearchMultiSelect
                    id={`${formId}-share-roles`}
                    labelledBy={`${formId}-share-roles-lbl`}
                    options={roleOptions}
                    value={shareRoleValue}
                    onChange={setShareRoles}
                    placeholder={t('analytics.indicators.shareWithRolesPh')}
                  />
                </div>
              </div>
              {shareUserValue.length > 0 && (
                <label className="vw-check">
                  <input type="checkbox" checked={usersCanEdit(draft.shares)} onChange={(e) => update({ shares: setUsersCanEdit(draft.shares, e.target.checked) })} />{' '}
                  {t('analytics.reports.builder.shareCanEdit')}
                </label>
              )}
            </>
          )}
          <p className="help">{t('analytics.reports.builder.visibilityHint')}</p>
        </Block>

        <Block title={t('analytics.reports.schedule.title')} error={err('scheduleCron') ?? err('deliveryEmails') ?? err('deliveryRangeMode')}>
          <ReportScheduleFields
            value={{ scheduleCron: draft.scheduleCron, deliveryEmails: draft.deliveryEmails, deliveryRangeMode: draft.deliveryRangeMode }}
            onChange={(patch) => update(patch)}
            report={report}
            dirty={report != null && (draft.scheduleCron !== (report.scheduleCron ?? null) || draft.deliveryEmails !== (report.deliveryEmails ?? null) || draft.deliveryRangeMode !== (report.deliveryRangeMode ?? null))}
          />
        </Block>

        <Block title={t('analytics.reports.builder.previewTitle')} hint={previewBody(draft, choices) ? t('analytics.reports.builder.previewHint', { n: PREVIEW_ROWS }) : undefined}>
          {previewBody(draft, choices) == null ? (
            <p className="help">{t('analytics.reports.builder.previewIncomplete')}</p>
          ) : preview.error ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(preview.error).title}
            </p>
          ) : (
            <ReportResultTable result={decorated} name={draft.name || t('analytics.reports.builder.previewTitle')} loading={preview.isFetching && !preview.data} emptyText={t('analytics.reports.builder.previewEmpty')} compact />
          )}
        </Block>
      </form>
    </Modal>
  )
}
