// Importar consignatarios (modal de la maqueta `renderConsigneeImportModalHtml`, flujo de dos pasos del servidor):
// plantilla primero (decide qué campo va en cada columna), cliente POR DEFECTO, archivo después → vista previa que valida el
// SERVIDOR (`validate-file`: errores y avisos por fila) → Importar (`confirm` de las válidas, con Idempotency-Key) o Cancelar
// (`discard` del lote ya validado) → resultado (creados, fallidos, omitidos). Sin plantilla el selector de archivo queda
// deshabilitado. Límite 5.000 filas / 2 MB. Ver/usar plantillas: `locations.read`; todo lo demás: `locations.create`.
// Lote F-B2: CSV (UTF-8) o Excel .xlsx (lo lee el servidor; con varias hojas se escoge la hoja), el archivo se revisa al elegirlo
// (`inspect-file`: hojas, encabezados y muestras, para el editor de plantillas), el cliente por defecto sale de la plantilla y
// «Recordar este cliente en la plantilla» lo guarda al importar; la fila que trae su propio cliente manda.
// Lote F-B3: la vista previa marca lo que el servidor dedujo del catálogo de códigos postales (pueblo, estado, ZIP: «del catálogo») y
// el «Código postal ajustado» (original tachado → el de entrega), dice qué variante de mapeo usó cada fila (columna Variante) y, si
// ninguna aplicó, muestra sus celdas; el resultado lista los consignatarios creados con datos completados o ajustados.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, IconCheck, IconGear, IconPlus, IconUpload, Modal, Spinner, toast } from '../../kernel/ui'
import { IconPower, IconRotateCcw } from '../../kernel/ui/actionIcons'
import {
  useClientOptions,
  useConfirmImport,
  useDiscardImport,
  useImportFields,
  useImportTemplates,
  useInspectImportFile,
  useSaveImportTemplate,
  useSetTemplateActive,
  useValidateImportFile,
} from './api'
import {
  CLIENT_CODE_FIELD,
  CLIENT_NAME_FIELD,
  exceedsImportSize,
  IMPORT_FILE_ACCEPT,
  MAX_IMPORT_ROWS,
  previewFields,
  rowHasProblems,
  rowProblems,
  SHARED,
  standardTemplateRequest,
  templateSummary,
  templateUsesField,
  type ImportFileInspection,
  type ImportPreview,
  type ImportResult,
  type ImportRow,
  type ImportTemplate,
} from './consigneeRules'
import { ConsigneeTemplateEditorModal } from './ConsigneeTemplateEditorModal'

/** Filas que se pintan en la vista previa (el lote completo vive en el servidor; hasta 5.000). */
const PREVIEW_ROWS = 200

export interface ConsigneeImportModalProps {
  /** Cliente con el que arranca el selector (el filtro de la pantalla; '' = sin cliente: manda el de la plantilla o compartidos). */
  defaultClient: string
  onClose: () => void
}

export function ConsigneeImportModal({ defaultClient, onClose }: ConsigneeImportModalProps) {
  const t = useT()
  const f = useFormat()
  const canWrite = useCan('locations.create')
  const [templateId, setTemplateId] = useState('')
  const [showInactive, setShowInactive] = useState(false)
  const [clientId, setClientId] = useState(defaultClient)
  const [file, setFile] = useState<File | null>(null)
  const [inspection, setInspection] = useState<ImportFileInspection | null>(null)
  const [sheet, setSheet] = useState<string | null>(null)
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [problem, setProblem] = useState<{ title: string; messages: string[] } | null>(null)
  const [onlyProblems, setOnlyProblems] = useState(false)
  const [remember, setRemember] = useState(true)
  const [editor, setEditor] = useState<{ template: ImportTemplate | null } | null>(null)
  const [toggling, setToggling] = useState<ImportTemplate | null>(null)
  const requestRef = useRef(0)
  const keyRef = useRef<{ batch: string; key: string } | null>(null)
  // el cliente de la plantilla se aplica una vez al abrir (si la pantalla no trajo uno) y cada vez que se escoge una plantilla
  const appliedDefault = useRef(defaultClient !== '')

  const { data: clients = [] } = useClientOptions(false)
  const { data: fields = [] } = useImportFields()
  const templatesQuery = useImportTemplates(clientId || null, showInactive)
  const templates = useMemo(() => templatesQuery.data ?? [], [templatesQuery.data])
  const inspect = useInspectImportFile()
  const validate = useValidateImportFile()
  const confirm = useConfirmImport()
  const discard = useDiscardImport()
  const saveTemplate = useSaveImportTemplate()
  const setTemplateActive = useSetTemplateActive()

  const fieldLabel = useCallback(
    (field: string) => {
      const key = `consignees.import.fields.${field}`
      const text = t(key)
      return text === key ? field : text
    },
    [t],
  )

  // nunca empieza «sin plantilla» si hay alguna: la elegida o la primera activa
  const activeTemplate = templates.find((x) => x.publicId === templateId) ?? templates.find((x) => x.isActive !== false) ?? templates[0]
  const templateUsable = !!activeTemplate && activeTemplate.isActive !== false
  const hasClientColumn = templateUsesField(activeTemplate, CLIENT_CODE_FIELD, CLIENT_NAME_FIELD)
  const variants = activeTemplate?.variants ?? []
  const clientList = useMemo(() => clients.map((c) => ({ value: c.publicId ?? '', label: c.name ?? c.code ?? '' })), [clients])
  const ownerOptions = useMemo(() => {
    const list = [...clientList]
    // el cliente de arranque (o el de la plantilla) aunque no esté entre los activos
    if (clientId && !list.some((o) => o.value === clientId))
      list.push({ value: clientId, label: activeTemplate?.defaultClientPublicId === clientId ? (activeTemplate.defaultClientName ?? clientId) : clientId })
    return [{ value: SHARED, label: t('consignees.shared') }, ...list]
  }, [clientList, clientId, activeTemplate, t])
  const templateClient = activeTemplate?.defaultClientPublicId ?? ''
  const showRemember = canWrite && !!activeTemplate && templateUsable && clientId !== templateClient

  /** Descarta el lote anterior (si lo hay) sin molestar: el error de un descarte no importa. */
  const dropBatch = useCallback(
    async (batch: ImportPreview | null) => {
      if (batch?.batchPublicId && batch.status !== 'CONFIRMED') {
        try {
          await discard.mutateAsync(batch.batchPublicId)
        } catch {
          // ya estaba confirmado o descartado: nada que hacer
        }
      }
    },
    [discard],
  )

  /** Revisa el archivo (hojas, encabezados) y lo valida con la plantilla, el cliente por defecto y la hoja. */
  const runValidate = useCallback(
    async (next: File | null, tpl: ImportTemplate | undefined, client: string, previous: ImportPreview | null, wantedSheet: string | null) => {
      const mine = ++requestRef.current
      setProblem(null)
      setResult(null)
      setPreview(null)
      await dropBatch(previous)
      if (!next || !tpl?.publicId || tpl.isActive === false) return
      if (exceedsImportSize(next.size)) {
        setProblem({ title: t('consignees.import.limitExceeded'), messages: [] })
        return
      }
      try {
        const info = await inspect.mutateAsync({ file: next, delimiter: tpl.delimiter || ',', hasHeader: tpl.hasHeader ?? true, sheet: wantedSheet })
        if (mine !== requestRef.current) return
        setInspection(info)
        setSheet(info.sheet ?? null)
        const dto = await validate.mutateAsync({ templatePublicId: tpl.publicId, clientPublicId: client || null, file: next, sheet: info.sheet ?? null })
        if (mine === requestRef.current) setPreview(dto)
      } catch (err) {
        if (mine !== requestRef.current) return
        const p = applyProblemDetails(err)
        const messages = Object.values(p.errors).flat().filter((m) => m !== p.title)
        setProblem({ title: p.title, messages })
      }
    },
    [dropBatch, inspect, validate, t],
  )

  // al abrir: si la pantalla no trajo cliente, el de la plantilla que queda elegida
  useEffect(() => {
    if (appliedDefault.current || !activeTemplate) return
    appliedDefault.current = true
    if (!activeTemplate.defaultClientPublicId) return
    // se fija la plantilla: con el cliente cambia la lista (generales + las de ese cliente) y la primera podría ser otra
    setTemplateId(activeTemplate.publicId ?? '')
    setClientId(activeTemplate.defaultClientPublicId)
  }, [activeTemplate])

  const pickTemplate = (id: string) => {
    setTemplateId(id)
    const tpl = templates.find((x) => x.publicId === id)
    appliedDefault.current = true
    const client = tpl?.defaultClientPublicId ?? clientId
    if (client !== clientId) setClientId(client)
    if (file) void runValidate(file, tpl, client, preview, sheet)
  }
  const pickClient = (id: string) => {
    setClientId(id)
    if (!file) return
    // una plantilla general o del cliente elegido sigue sirviendo: se valida otra vez con el nuevo cliente
    if (activeTemplate && (!activeTemplate.clientPublicId || activeTemplate.clientPublicId === id)) void runValidate(file, activeTemplate, id, preview, sheet)
    else {
      requestRef.current++
      setPreview(null)
      void dropBatch(preview)
    }
  }
  const pickSheet = (name: string) => {
    setSheet(name)
    if (file) void runValidate(file, activeTemplate, clientId, preview, name)
  }

  const cancel = async () => {
    if (preview && !result) await dropBatch(preview)
    onClose()
  }

  const doConfirm = async () => {
    if (!preview?.batchPublicId) return
    // la misma clave en un reintento del mismo lote (el servidor no crea dos veces)
    if (keyRef.current?.batch !== preview.batchPublicId) keyRef.current = { batch: preview.batchPublicId, key: crypto.randomUUID() }
    try {
      const dto = await confirm.mutateAsync({ batchPublicId: preview.batchPublicId, idempotencyKey: keyRef.current.key, rememberClient: showRemember && remember })
      setResult(dto)
      toast.success(`${dto.created ?? 0} ${t('consignees.import.done')}`)
    } catch (err) {
      const p = applyProblemDetails(err)
      setProblem({ title: p.title, messages: Object.values(p.errors).flat().filter((m) => m !== p.title) })
    }
  }

  const rows = useMemo(() => preview?.rows ?? [], [preview])
  const shownRows = (onlyProblems ? rows.filter(rowHasProblems) : rows).slice(0, PREVIEW_ROWS)
  const invalidRows = (preview?.rowCount ?? 0) - (preview?.validRows ?? 0)
  const columnFields = useMemo(() => previewFields(activeTemplate, rows), [activeTemplate, rows])
  const hasVariantColumn = rows.some((r) => r.variant != null) || variants.length > 0
  /** Celda de la vista previa: el valor y, si salió del catálogo de códigos postales, su marca (o el ZIP original tachado). */
  const cell = (r: ImportRow, x: string) => {
    const raw = r.values?.[x]
    const text = x === 'contactPhone' && raw ? f.phone(raw) : raw || '—'
    if (x === 'postalCode' && r.originalPostalCode)
      return (
        <span className="cs-adjusted" title={t('consignees.import.zipAdjustedTitle', { from: r.originalPostalCode, to: raw ?? '' })}>
          <s>{r.originalPostalCode}</s> → <b>{text}</b> <Chip tone="warn">{t('consignees.import.zipAdjusted')}</Chip>
        </span>
      )
    if (r.inferred?.includes(x))
      return (
        <span className="cs-inferred" title={t('consignees.import.inferredTitle')}>
          {text} <small>{t('consignees.import.inferredTag')}</small>
        </span>
      )
    return text
  }
  const conditionText = (c: { position?: number; operator?: string | null; value?: string | null }) =>
    t('consignees.import.conditionSummary', {
      column: t('consignees.import.columnN', { n: c.position ?? 0 }),
      operator: t(`consignees.import.operators.${c.operator ?? 'EQUALS'}`),
      value: c.value ? ` «${c.value}»` : '',
    })
  const adjustedRows = (result?.rows ?? []).filter((r) => r.locationPublicId && ((r.inferred?.length ?? 0) > 0 || r.originalPostalCode))

  const createStandard = async () => {
    try {
      const created = await saveTemplate.mutateAsync({ action: 'create', body: standardTemplateRequest(t('consignees.import.standardName')) })
      setTemplateId(created.publicId ?? '')
      toast.success(t('consignees.import.tplCreated', { name: created.name ?? '' }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const busy = inspect.isPending || validate.isPending || confirm.isPending
  const sheets = inspection?.sheets ?? []

  if (editor) {
    return (
      <ConsigneeTemplateEditorModal
        template={editor.template}
        fields={fields}
        fieldLabel={fieldLabel}
        clients={clientList}
        inspection={inspection}
        fileName={file?.name ?? null}
        onClose={() => setEditor(null)}
        onSaved={(saved) => {
          setEditor(null)
          setTemplateId(saved.publicId ?? '')
          const client = saved.defaultClientPublicId ?? clientId
          if (client !== clientId) setClientId(client)
          if (file) void runValidate(file, saved, client, preview, sheet)
        }}
      />
    )
  }

  const rowStatus = (row: ImportRow) => {
    if (result) return row.skipped ? 'skipped' : row.locationPublicId ? 'created' : 'failed'
    return row.isValid ? ((row.warnings?.length ?? 0) > 0 ? 'warn' : 'ok') : 'bad'
  }
  const statusChip = (row: ImportRow) => {
    const s = rowStatus(row)
    const tone = s === 'ok' || s === 'created' ? 'deliv' : s === 'warn' ? 'warn' : s === 'skipped' ? 'cap' : 'fail'
    return <Chip tone={tone}>{t(`consignees.import.rowStatus.${s}`)}</Chip>
  }

  return (
    <>
    <Modal
      open
      size="md"
      title={t('consignees.import.title')}
      onClose={() => void cancel()}
      dismissible={!busy && toggling === null}
      footer={
        result ? (
          <button type="button" className="btn sm flow" onClick={onClose}>
            {t('consignees.import.close')}
          </button>
        ) : (
          <>
            <button type="button" className="btn sm" onClick={() => void cancel()} disabled={busy}>
              {t('consignees.import.cancel')}
            </button>
            <button type="button" className="btn sm flow" disabled={!canWrite || busy || !preview || (preview.validRows ?? 0) === 0} onClick={() => void doConfirm()}>
              <IconCheck /> {confirm.isPending ? t('common.loading') : t('consignees.import.do')}
            </button>
          </>
        )
      }
    >
      <p className="cs-importsub">{t('consignees.import.sub')}</p>

      {!result && (
        <>
          <div className="cs-tplrow">
            <div className="f">
              <label htmlFor="cs-tpl">{t('consignees.import.tplLabel')}</label>
              <select id="cs-tpl" value={activeTemplate?.publicId ?? ''} onChange={(e) => pickTemplate(e.target.value)} disabled={busy}>
                {templates.length === 0 && <option value="">{t('consignees.import.tplNone')}</option>}
                {templates.map((x) => (
                  <option key={x.publicId} value={x.publicId}>
                    {x.name}
                    {x.clientName ? ` · ${x.clientName}` : ''}
                    {x.isActive === false ? ` (${t('consignees.import.tplInactive')})` : ''}
                  </option>
                ))}
              </select>
            </div>
            <button type="button" className="btn sm" onClick={() => setEditor({ template: null })} disabled={busy}>
              <IconPlus /> {t('consignees.import.tplNewBtn')}
            </button>
            {activeTemplate && (
              <button type="button" className="btn sm" onClick={() => setEditor({ template: activeTemplate })} disabled={busy}>
                <IconGear /> {t('consignees.import.tplEditBtn')}
              </button>
            )}
          </div>

          <label className="sw cs-inactive-tpl">
            <input type="checkbox" role="switch" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('consignees.import.showInactiveTpl')}</span>
          </label>

          <div className="f">
            <label htmlFor="cs-owner">{t('consignees.import.clientLabel')}</label>
            <select id="cs-owner" value={clientId} onChange={(e) => pickClient(e.target.value)} disabled={busy}>
              {ownerOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
            <p className="help">{hasClientColumn ? t('consignees.import.clientColHint') : t('consignees.import.clientHint')}</p>
            {showRemember && (
              <label className="sw cs-remember">
                <input type="checkbox" role="switch" checked={remember} onChange={(e) => setRemember(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('consignees.import.rememberClient')}</span>
              </label>
            )}
          </div>

          {templatesQuery.isPending ? (
            <Spinner block />
          ) : activeTemplate ? (
            <div className="note cs-tplnote">
              {variants.length > 0 ? (
                <div className="cs-varsum">
                  <span>{t('consignees.import.variantsMapping', { n: variants.length })}</span>
                  <ol>
                    {variants.map((v, k) => (
                      <li key={k}>
                        <b>{v.name || t('consignees.import.variantN', { n: k + 1 })}</b>
                        {' — '}
                        {(v.conditions ?? []).length > 0 ? (v.conditions ?? []).map(conditionText).join(` ${t('consignees.import.and')} `) : t('consignees.import.conditionsNone')}
                        {': '}
                        {templateSummary({ columns: v.columns } as ImportTemplate, fieldLabel) || t('consignees.import.ignore')}
                      </li>
                    ))}
                  </ol>
                </div>
              ) : (
                <span>
                  {t('consignees.import.mapping')}: {templateSummary(activeTemplate, fieldLabel) || t('consignees.import.ignore')}
                </span>
              )}
              <button
                type="button"
                className="rowbtn"
                title={activeTemplate.isActive === false ? t('consignees.import.tplReactivate') : t('consignees.import.tplDeactivate')}
                aria-label={activeTemplate.isActive === false ? t('consignees.import.tplReactivate') : t('consignees.import.tplDeactivate')}
                onClick={() => setToggling(activeTemplate)}
                disabled={busy}
              >
                {activeTemplate.isActive === false ? <IconRotateCcw /> : <IconPower />}
              </button>
            </div>
          ) : (
            <div className="note cs-tplnote">
              <span>{t('consignees.import.tplNoneHint')}</span>
              <button type="button" className="btn sm" onClick={() => void createStandard()} disabled={saveTemplate.isPending}>
                {t('consignees.import.createStandard')}
              </button>
            </div>
          )}

          <div className="f">
            <label htmlFor="cs-file">{t('consignees.import.file')}</label>
            <div className="cs-filerow">
              <label className={templateUsable ? 'btn sm cs-pick' : 'btn sm cs-pick off'} htmlFor="cs-file">
                <IconUpload /> {file ? t('consignees.import.changeFile') : t('consignees.import.choose')}
              </label>
              <input
                id="cs-file"
                className="cs-fileinput"
                type="file"
                accept={IMPORT_FILE_ACCEPT}
                disabled={!templateUsable || busy}
                onChange={(e) => {
                  const next = e.target.files?.[0] ?? null
                  e.target.value = ''
                  if (!next) return
                  setFile(next)
                  setInspection(null)
                  setSheet(null)
                  void runValidate(next, activeTemplate, clientId, preview, null)
                }}
              />
              {file && preview && (
                <span className="cs-filemeta">
                  {file.name}
                  {inspection?.sheet ? ` · ${inspection.sheet}` : ''} · {preview.rowCount ?? 0} {t('consignees.import.rowsFound')}
                </span>
              )}
              {file && !preview && busy && <Spinner />}
            </div>
            <p className="help">{t('consignees.import.limits', { rows: f.number(MAX_IMPORT_ROWS) })}</p>
            <p className="help">{t('consignees.import.utf8')}</p>
          </div>

          {sheets.length > 1 && (
            <div className="f cs-sheet">
              <label htmlFor="cs-sheet">{t('consignees.import.sheet')}</label>
              <select id="cs-sheet" value={sheet ?? ''} onChange={(e) => pickSheet(e.target.value)} disabled={busy}>
                {sheets.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
              <p className="help">{t('consignees.import.sheetHint', { n: sheets.length })}</p>
            </div>
          )}
        </>
      )}

      {problem && (
        <div className="form-alert" role="alert">
          {problem.title}
          {problem.messages.length > 0 && (
            <ul>
              {problem.messages.map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      {result ? (
        <div className="cs-result" role="status">
          <h3 className="cs-sub">{t('consignees.import.resultTitle')}</h3>
          <div className="cs-stats">
            <span className="cs-stat ok">
              <b>{result.created ?? 0}</b> {t('consignees.import.created')}
            </span>
            <span className="cs-stat bad">
              <b>{result.failed ?? 0}</b> {t('consignees.import.failed')}
            </span>
            <span className="cs-stat">
              <b>{result.skipped ?? 0}</b> {t('consignees.import.skipped')}
            </span>
          </div>
          {adjustedRows.length > 0 && (
            <div className="cs-tablewrap">
              <p className="help">{t('consignees.import.adjustedTitle', { n: adjustedRows.length })}</p>
              <table className="lst densetbl" aria-label={t('consignees.import.adjustedTitle', { n: adjustedRows.length })}>
                <thead>
                  <tr>
                    <th>{t('consignees.import.line')}</th>
                    <th>{fieldLabel('name')}</th>
                    <th>{fieldLabel('postalCode')}</th>
                    <th>{t('consignees.import.remarks')}</th>
                  </tr>
                </thead>
                <tbody>
                  {adjustedRows.map((r) => (
                    <tr key={r.row}>
                      <td className="mono">{r.line ?? r.row}</td>
                      <td>{r.values?.name ?? '—'}</td>
                      <td>{cell(r, 'postalCode')}</td>
                      <td className="cs-remarks">{(r.warnings ?? []).join(' · ') || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {(result.rows ?? []).some((r) => !r.locationPublicId && !r.skipped) && (
            <div className="cs-tablewrap">
              <table className="lst densetbl">
                <thead>
                  <tr>
                    <th>{t('consignees.import.line')}</th>
                    <th>{t('consignees.import.rowState')}</th>
                    <th>{t('consignees.import.remarks')}</th>
                  </tr>
                </thead>
                <tbody>
                  {(result.rows ?? [])
                    .filter((r) => !r.locationPublicId && !r.skipped)
                    .map((r) => {
                      const p = rowProblems(r, fieldLabel)
                      return (
                        <tr key={r.row}>
                          <td className="mono">{r.line ?? r.row}</td>
                          <td>{statusChip(r)}</td>
                          <td className="cs-remarks">{[...p.errors, ...p.warnings].join(' · ') || '—'}</td>
                        </tr>
                      )
                    })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : preview ? (
        <div className="cs-preview">
          <div className="cs-prevhead">
            <b>{t('consignees.import.preview')}</b>
            <span className="cs-counts">
              <span className="cs-stat ok">
                <b>{preview.validRows ?? 0}</b> {t('consignees.import.valid')}
              </span>
              <span className="cs-stat bad">
                <b>{invalidRows}</b> {t('consignees.import.invalid')}
              </span>
            </span>
            {rows.some(rowHasProblems) && (
              <label className="sw cs-onlyprob">
                <input type="checkbox" role="switch" checked={onlyProblems} onChange={(e) => setOnlyProblems(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('consignees.import.onlyProblems')}</span>
              </label>
            )}
          </div>
          <div className="cs-tablewrap">
            <table className="lst densetbl">
              <thead>
                <tr>
                  <th>{t('consignees.import.line')}</th>
                  <th>{t('consignees.import.rowState')}</th>
                  {hasVariantColumn && <th>{t('consignees.import.variant')}</th>}
                  {columnFields.map((x) => (
                    <th key={x}>{fieldLabel(x)}</th>
                  ))}
                  <th>{t('consignees.import.owner')}</th>
                  <th>{t('consignees.import.remarks')}</th>
                </tr>
              </thead>
              <tbody>
                {shownRows.map((r) => {
                  const p = rowProblems(r, fieldLabel)
                  return (
                    <tr key={r.row} className={r.isValid ? undefined : 'cs-badrow'}>
                      <td className="mono">{r.line ?? r.row}</td>
                      <td>{statusChip(r)}</td>
                      {hasVariantColumn && <td className="cs-varcell">{r.variantName ?? (r.variant ? t('consignees.import.variantN', { n: r.variant }) : '—')}</td>}
                      {columnFields.map((x) => (
                        <td key={x}>{cell(r, x)}</td>
                      ))}
                      <td>{r.owner ? (r.owner.isShared ? t('consignees.shared') : (r.owner.clientName ?? '—')) : '—'}</td>
                      <td className="cs-remarks">
                        {p.errors.map((m) => (
                          <span key={m} className="cs-err">
                            {m}
                          </span>
                        ))}
                        {p.warnings.map((m) => (
                          <span key={m} className="cs-warn">
                            {m}
                          </span>
                        ))}
                        {(r.cells?.length ?? 0) > 0 && <span className="cs-cells">{t('consignees.import.rowCells', { cells: (r.cells ?? []).map((c) => c || '∅').join(' | ') })}</span>}
                        {p.errors.length + p.warnings.length === 0 && '—'}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          {rows.length > shownRows.length && <p className="help">{t('consignees.import.moreRows', { shown: shownRows.length, total: onlyProblems ? rows.filter(rowHasProblems).length : rows.length })}</p>}
        </div>
      ) : (
        !problem && (
          <div className="empty cs-noFile">
            <p>{t('consignees.import.noFile')}</p>
          </div>
        )
      )}

    </Modal>
    <ConfirmDialog
      open={toggling !== null}
      tone={toggling?.isActive === false ? 'flow' : 'danger'}
      title={toggling?.isActive === false ? t('consignees.import.tplReactivateTitle') : t('consignees.import.tplDeactivateTitle')}
      message={t(toggling?.isActive === false ? 'consignees.import.tplReactivateBody' : 'consignees.import.tplDeactivateBody', { name: toggling?.name ?? '' })}
      confirmLabel={toggling?.isActive === false ? t('consignees.import.tplReactivate') : t('consignees.import.tplDeactivate')}
      onConfirm={async () => {
        if (!toggling?.publicId) return
        const active = toggling.isActive === false
        await setTemplateActive.mutateAsync({ publicId: toggling.publicId, active })
        toast.success(t(active ? 'consignees.import.tplReactivated' : 'consignees.import.tplDeactivated', { name: toggling.name ?? '' }))
        if (!active) setTemplateId('')
      }}
      onClose={() => setToggling(null)}
    />
    </>
  )
}
