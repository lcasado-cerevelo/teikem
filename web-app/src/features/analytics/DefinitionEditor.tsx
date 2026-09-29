// Lote F8a (P3) — editor modal compartido entre Indicadores y Gráficos (`analytics.manage`): alta y edición de la
// definición. `dataSource`, `aggregateFn`, `businessModule`, "Quién puede verlo" y "Tipo" (gráficos) son catálogos
// del tenant (`useLookups`); el filtro son filas campo · operador · valor sobre los campos de la fuente elegida, con
// los operadores del DSL de `kernel/dsl` (mismo motor que evalúa `RuleEvaluator.ts`). Al cambiar de fuente se limpian
// el campo, el filtro y "Agrupar por". Errores del servidor por campo con `Form`; el título general, en el toast.
// Fase 10a (indicadores y gráficos por igual): "Compartido" comparte con usuarios (`admin.users` para listarlos) y/o
// con roles del tenant (`GET /roles`, sin permiso propio); cada `ShareDto` lleva `userId` o `roleId`, nunca ambos.
// Fase 10b (solo gráficos, `kind === 'chart'`): sin selector de "Módulo de negocio" (el módulo es el de la fuente,
// `defaultBusinessModule`; al editar se conserva el guardado hasta que cambie la fuente) y con "Vista previa" en vivo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useCan } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import { DateInput, Field, Form, NumberInput, Select, TextArea, TextInput, Toggle } from '../../kernel/ui/Form'
import { Modal } from '../../kernel/ui/Modal'
import { SearchMultiSelect } from '../../kernel/ui/SearchSelect'
import { toast } from '../../kernel/ui/toast'
import { useRoles } from '../system/api'
import { ChartPreview } from './ChartPreviewPanel'
import {
  useAnalyticsShareUsers,
  useDataSources,
  useSaveChart,
  useSaveIndicator,
  type AnalyticsDefinition,
} from './api'
import {
  buildChartRequest,
  buildIndicatorRequest,
  CUSTOM_RANGE,
  definitionToFormValues,
  FILTER_OPS,
  LIST_OPS,
  NO_VALUE_OPS,
  RANGE_OPS,
  blankFilterRow,
  filterIsUnrepresentable,
  parseFilterRows,
  serializeFilterRows,
  sourceFieldOptions,
  type DataSource,
  type FilterOp,
  type FilterRow,
  type ShareDto,
} from './definitions'

export interface DefinitionEditorProps {
  kind: 'indicator' | 'chart'
  /** `null` = alta; con datos, edición (el `id` decide POST vs. PUT). */
  definition: AnalyticsDefinition | null
  onClose: () => void
}

const CHART_TYPE_CODES = ['BAR', 'DONUT', 'LINE']
const EMPTY_LOOKUPS: ReturnType<typeof useLookups>['data'] = []

export function DefinitionEditor({ kind, definition, onClose }: DefinitionEditorProps) {
  const t = useT()
  const dataSources = useDataSources()
  const aggregateFns = useLookups('AggregateFn').data ?? EMPTY_LOOKUPS
  const businessModules = useLookups('BusinessModule').data ?? EMPTY_LOOKUPS
  const visibilities = useLookups('ReportVisibility').data ?? EMPTY_LOOKUPS
  const dateModes = useLookups('DateRangeMode').data ?? EMPTY_LOOKUPS
  const chartTypesAll = useLookups('ReportChartType').data ?? EMPTY_LOOKUPS
  const chartTypes = useMemo(() => chartTypesAll.filter((c) => CHART_TYPE_CODES.includes(c.code)), [chartTypesAll])
  const canBrowseUsers = useCan('admin.users')
  const shareUsers = useAnalyticsShareUsers(canBrowseUsers)
  const shareRoles = useRoles()

  const saveIndicator = useSaveIndicator()
  const saveChart = useSaveChart()

  const [filterRows, setFilterRows] = useState<FilterRow[]>(() => parseFilterRows(definition?.filterJson))
  // El filtro guardado usa una forma (or/not) que este editor no arma con filas: se conserva tal cual hasta que el
  // usuario agregue una fila a propósito (ahí se vuelve editable, y guardar reemplaza el filtro anterior).
  const [keepOriginalFilter, setKeepOriginalFilter] = useState(() => filterIsUnrepresentable(definition?.filterJson))
  const [shares, setShares] = useState<ShareDto[]>(() => [...(definition?.shares ?? [])])

  const schema = useMemo(
    () =>
      z
        .object({
          name: z.string().trim().min(1, t(kind === 'indicator' ? 'analytics.editor.errors.nameRequiredIndicator' : 'analytics.editor.errors.nameRequiredChart')),
          descriptionEs: z.string(),
          descriptionEn: z.string(),
          dataSource: z.string().min(1, t('analytics.editor.errors.dataSourceRequired')),
          aggregateFn: z.string().min(1, t('analytics.editor.errors.aggregateRequired')),
          field: z.string(),
          // Gráficos: sin selector; vacío = el servidor usa el módulo por defecto de la fuente.
          businessModule: kind === 'chart' ? z.string() : z.string().min(1, t('analytics.editor.errors.moduleRequired')),
          isMoney: z.boolean(),
          visibility: z.string().min(1, t('analytics.editor.errors.visibilityRequired')),
          dateRangeMode: z.string(),
          dateFrom: z.string(),
          dateTo: z.string(),
          sortOrder: z.number().nullable(),
          showInPulse: z.boolean(),
          groupByField: z.string(),
          chartType: z.string(),
        })
        .superRefine((v, ctx) => {
          if (v.aggregateFn !== 'COUNT' && !v.field) ctx.addIssue({ code: 'custom', path: ['field'], message: t('analytics.editor.errors.fieldRequired') })
          if (kind === 'chart' && !v.groupByField)
            ctx.addIssue({ code: 'custom', path: ['groupByField'], message: t('analytics.editor.errors.groupByRequired') })
          if (v.dateRangeMode === CUSTOM_RANGE) {
            if (!v.dateFrom) ctx.addIssue({ code: 'custom', path: ['dateFrom'], message: t('analytics.range.errors.fromRequired') })
            if (!v.dateTo) ctx.addIssue({ code: 'custom', path: ['dateTo'], message: t('analytics.range.errors.toRequired') })
            if (v.dateFrom && v.dateTo && v.dateFrom > v.dateTo)
              ctx.addIssue({ code: 'custom', path: ['dateFrom'], message: t('analytics.range.errors.fromAfterTo') })
          }
        }),
    [t, kind],
  )

  const form = useForm({ resolver: zodResolver(schema), defaultValues: definitionToFormValues(definition, kind) })
  const dataSourceValue = useWatch({ control: form.control, name: 'dataSource' })
  const aggregateFnValue = useWatch({ control: form.control, name: 'aggregateFn' })
  const visibilityValue = useWatch({ control: form.control, name: 'visibility' })
  const dateRangeModeValue = useWatch({ control: form.control, name: 'dateRangeMode' })
  // Solo para la vista previa del gráfico (valores en vivo).
  const [fieldValue, groupByValue, chartTypeValue, dateFromValue, dateToValue, isMoneyValue, nameValue] = useWatch({
    control: form.control,
    name: ['field', 'groupByField', 'chartType', 'dateFrom', 'dateTo', 'isMoney', 'name'],
  })

  const selectedSource: DataSource | undefined = dataSources.data?.find((ds) => ds.key === dataSourceValue)
  const prevDataSource = useRef(dataSourceValue)
  useEffect(() => {
    if (prevDataSource.current !== dataSourceValue) {
      form.setValue('field', '')
      form.setValue('groupByField', '')
      setFilterRows([])
      // Gráficos: el módulo sigue siempre a la fuente (no hay selector); vacío = el del servidor para esa fuente.
      if (kind === 'chart') form.setValue('businessModule', selectedSource?.defaultBusinessModule ?? '')
      else if (selectedSource?.defaultBusinessModule) form.setValue('businessModule', selectedSource.defaultBusinessModule)
      prevDataSource.current = dataSourceValue
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dataSourceValue, selectedSource])
  useEffect(() => {
    if (visibilityValue !== 'SHARED') setShares([])
  }, [visibilityValue])

  const fieldOptions = sourceFieldOptions(selectedSource)
  const numericFieldOptions = fieldOptions.filter((f) => f.type === 'Number')
  const dateRangeApplies = selectedSource ? Boolean(selectedSource.dateField) : (definition?.dateRangeApplies ?? false)

  const formId = `def-editor-${kind}-${definition?.id ?? 'new'}`
  const isNew = definition == null
  const title = isNew
    ? t(kind === 'indicator' ? 'analytics.indicators.newTitle' : 'analytics.charts.newTitle')
    : t(kind === 'indicator' ? 'analytics.indicators.editTitle' : 'analytics.charts.editTitle')

  const addFilterRow = () => {
    setKeepOriginalFilter(false)
    setFilterRows((rows) => [...rows, blankFilterRow(fieldOptions[0]?.key ?? '')])
  }
  const updateFilterRow = (index: number, patch: Partial<FilterRow>) =>
    setFilterRows((rows) => rows.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  const removeFilterRow = (index: number) => setFilterRows((rows) => rows.filter((_, i) => i !== index))

  const userOptions = (shareUsers.data ?? []).map((u) => ({ value: String(u.id), label: u.email ? `${u.fullName ?? ''} · ${u.email}` : (u.fullName ?? '') }))
  const shareUserValue = shares.filter((s) => s.userId != null).map((s) => String(s.userId))
  // Cada selector reemplaza solo su tipo de compartición: el de usuarios conserva las de rol y viceversa.
  const setShareUserValue = (value: string[]) =>
    setShares((prev) => [...prev.filter((s) => s.userId == null), ...value.map((id) => ({ userId: Number(id), roleId: null, canEdit: false }))])
  const shareRoleValue = shares.filter((s) => s.roleId != null).map((s) => String(s.roleId))
  // Roles activos, más los ya elegidos aunque se hayan desactivado (para poder verlos y quitarlos).
  const roleOptions = (shareRoles.data ?? [])
    .filter((r) => r.id != null && (r.isActive !== false || shareRoleValue.includes(String(r.id))))
    .map((r) => ({ value: String(r.id), label: r.name ?? String(r.id) }))
  const setShareRoleValue = (value: string[]) =>
    setShares((prev) => [...prev.filter((s) => s.roleId == null), ...value.map((id) => ({ userId: null, roleId: Number(id), canEdit: false }))])

  const liveFilterJson = keepOriginalFilter ? (definition?.filterJson ?? null) : serializeFilterRows(filterRows)

  return (
    <Modal
      open
      size="lg"
      title={title}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t(kind === 'indicator' ? 'analytics.indicators.saveBtn' : 'analytics.charts.saveBtn')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onError={(p) => toast.error(p.title)}
        onSubmit={async (v) => {
          const descriptions = { es: v.descriptionEs, en: v.descriptionEn }
          const filterJson = liveFilterJson
          const body =
            kind === 'indicator' ? buildIndicatorRequest(v, descriptions, filterJson, shares) : buildChartRequest(v, descriptions, filterJson, shares)
          if (kind === 'indicator') await saveIndicator.mutateAsync({ id: definition?.id ?? null, body })
          else await saveChart.mutateAsync({ id: definition?.id ?? null, body })
          toast.success(t(isNew ? (kind === 'indicator' ? 'analytics.indicators.saved' : 'analytics.charts.saved') : kind === 'indicator' ? 'analytics.indicators.updated' : 'analytics.charts.updated', { name: v.name }))
          onClose()
        }}
      >
        <Field name="name" label={t(kind === 'indicator' ? 'analytics.indicators.nameLabel' : 'analytics.charts.nameLabel')} required>
          <TextInput placeholder={t(kind === 'indicator' ? 'analytics.indicators.namePh' : 'analytics.charts.namePh')} />
        </Field>
        <div className="r2">
          <Field name="descriptionEs" label={t('analytics.editor.descriptionEs')}>
            <TextArea rows={2} />
          </Field>
          <Field name="descriptionEn" label={t('analytics.editor.descriptionEn')}>
            <TextArea rows={2} />
          </Field>
        </div>
        <Field name="dataSource" label={t(kind === 'indicator' ? 'analytics.indicators.datasetLabel' : 'analytics.charts.datasetLabel')} required>
          <Select options={(dataSources.data ?? []).map((ds) => ({ value: ds.key ?? '', label: ds.label ?? ds.key ?? '' }))} placeholder={t('analytics.range.choose')} />
        </Field>
        <div className="r2">
          <Field name="aggregateFn" label={t(kind === 'indicator' ? 'analytics.indicators.fnLabel' : 'analytics.charts.metricFnLabel')} required>
            <Select options={aggregateFns.map((f) => ({ value: f.code, label: f.label }))} placeholder={t('analytics.range.choose')} />
          </Field>
          {aggregateFnValue !== 'COUNT' && (
            <Field name="field" label={t(kind === 'indicator' ? 'analytics.indicators.fieldLabel' : 'analytics.charts.metricFieldLabel')} required>
              <Select options={numericFieldOptions.map((f) => ({ value: f.key ?? '', label: f.label ?? f.key ?? '' }))} placeholder={t('analytics.range.choose')} />
            </Field>
          )}
        </div>
        {kind === 'chart' && (
          <>
            <Field name="groupByField" label={t('analytics.charts.groupByLabel')} help={t('analytics.charts.groupByHint')} required>
              <Select options={fieldOptions.map((f) => ({ value: f.key ?? '', label: f.label ?? f.key ?? '' }))} placeholder={t('analytics.range.choose')} />
            </Field>
            <Field name="chartType" label={t('analytics.charts.typeLabel')} help={t('analytics.charts.typeHint')} required>
              <Select options={chartTypes.map((c) => ({ value: c.code, label: c.label }))} placeholder={t('analytics.range.choose')} />
            </Field>
          </>
        )}

        {kind === 'indicator' && (
          <Field name="businessModule" label={t('analytics.editor.businessModule')} required>
            <Select options={businessModules.map((m) => ({ value: m.code, label: m.label }))} placeholder={t('analytics.range.choose')} />
          </Field>
        )}
        <Field name="isMoney" label={t('analytics.editor.isMoney')}>
          <Toggle />
        </Field>

        <Field name="visibility" label={t('analytics.indicators.visibilityLabel')} required>
          <Select options={visibilities.map((v) => ({ value: v.code, label: v.label }))} placeholder={t('analytics.range.choose')} />
        </Field>
        {visibilityValue === 'SHARED' && (
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
                  onChange={setShareUserValue}
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
                onChange={setShareRoleValue}
                placeholder={t('analytics.indicators.shareWithRolesPh')}
              />
            </div>
          </div>
        )}
        <p className="help">{t('analytics.indicators.visibilityHint')}</p>

        {dateRangeApplies ? (
          <>
            <Field name="dateRangeMode" label={t('analytics.indicators.dateRangeLabel')}>
              <Select options={dateModes.map((m) => ({ value: m.code, label: m.label }))} placeholder={t('analytics.range.choose')} />
            </Field>
            {dateRangeModeValue === CUSTOM_RANGE && (
              <div className="r2">
                <Field name="dateFrom" label={t('analytics.range.from')} required>
                  <DateInput />
                </Field>
                <Field name="dateTo" label={t('analytics.range.to')} required>
                  <DateInput />
                </Field>
              </div>
            )}
          </>
        ) : (
          <p className="help">{t('analytics.indicators.noDateField')}</p>
        )}

        <Field name="sortOrder" label={t('analytics.editor.sortOrder')} help={t('analytics.editor.sortOrderHint')}>
          <NumberInput />
        </Field>
        <Field name="showInPulse" label={t('analytics.indicators.showInPulsoLabel')}>
          <Toggle />
        </Field>
        <p className="help">{t('analytics.indicators.showInPulsoHint')}</p>

        <div className="ph2" style={{ padding: '16px 0 8px', border: 'none' }}>
          {t('analytics.editor.filtersTitle')}
        </div>
        <p className="help">{t('analytics.editor.filtersHint')}</p>
        {keepOriginalFilter && <p className="help">{t('analytics.editor.filterUnrepresentable')}</p>}
        {filterRows.map((row, idx) => (
          <FilterRowEditor
            key={idx}
            row={row}
            fields={fieldOptions}
            onChange={(patch) => updateFilterRow(idx, patch)}
            onRemove={() => removeFilterRow(idx)}
          />
        ))}
        <button type="button" className="btn sm" onClick={addFilterRow} disabled={fieldOptions.length === 0}>
          {t('analytics.editor.addFilter')}
        </button>

        {kind === 'chart' && (
          <div style={{ marginTop: 16 }}>
            <ChartPreview
              source={selectedSource}
              groupByField={groupByValue}
              aggregateFn={aggregateFnValue}
              field={fieldValue}
              chartType={chartTypeValue}
              dateRangeMode={dateRangeModeValue}
              dateFrom={dateFromValue}
              dateTo={dateToValue}
              filterJson={liveFilterJson}
              isMoney={isMoneyValue}
              name={nameValue}
            />
          </div>
        )}
      </Form>
    </Modal>
  )
}

function FilterRowEditor({
  row,
  fields,
  onChange,
  onRemove,
}: {
  row: FilterRow
  fields: { key?: string | null; label?: string | null }[]
  onChange: (patch: Partial<FilterRow>) => void
  onRemove: () => void
}) {
  const t = useT()
  const showValue = !NO_VALUE_OPS.has(row.op)
  const showSecondValue = RANGE_OPS.has(row.op)
  return (
    <div className="r3" style={{ alignItems: 'end' }}>
      <div className="f">
        <label>{t('analytics.editor.filterField')}</label>
        <select value={row.field} onChange={(e) => onChange({ field: e.target.value })}>
          {fields.map((f) => (
            <option key={f.key} value={f.key ?? ''}>
              {f.label ?? f.key}
            </option>
          ))}
        </select>
      </div>
      <div className="f">
        <label>{t('analytics.editor.filterOp')}</label>
        <select value={row.op} onChange={(e) => onChange({ op: e.target.value as FilterOp })}>
          {FILTER_OPS.map((op) => (
            <option key={op} value={op}>
              {t(`analytics.editor.filterOps.${op}`)}
            </option>
          ))}
        </select>
      </div>
      {showValue && (
        <div className="f">
          <label>{t('analytics.editor.filterValue')}</label>
          <div style={{ display: 'flex', gap: 6 }}>
            <input
              style={{ minWidth: 0, flex: 1 }}
              value={row.value}
              onChange={(e) => onChange({ value: e.target.value })}
              placeholder={LIST_OPS.has(row.op) ? t('analytics.editor.filterValueListPh') : undefined}
            />
            {showSecondValue && <input style={{ minWidth: 0, flex: 1 }} value={row.value2} onChange={(e) => onChange({ value2: e.target.value })} />}
          </div>
        </div>
      )}
      <button type="button" className="btn sm" onClick={onRemove} aria-label={t('analytics.editor.removeFilter')}>
        ✕
      </button>
    </div>
  )
}
