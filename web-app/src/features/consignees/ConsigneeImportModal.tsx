// Importar consignatarios desde CSV (modal de la maqueta `renderConsigneeImportModalHtml`, flujo de dos pasos del servidor):
// plantilla primero (decide qué campo va en cada columna), cliente dueño, archivo después → vista previa que valida el
// SERVIDOR (`validate-file`: errores y avisos por fila) → Importar (`confirm` de las válidas, con Idempotency-Key) o Cancelar
// (`discard` del lote ya validado) → resultado (creados, fallidos, omitidos). Sin plantilla el selector de archivo queda
// deshabilitado. Límite 5.000 filas / 2 MB y CSV en UTF-8. Ver/usar plantillas: `locations.read`; todo lo demás: `locations.create`.
import { useCallback, useMemo, useRef, useState } from 'react'
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
  useSaveImportTemplate,
  useSetTemplateActive,
  useValidateImportFile,
} from './api'
import {
  CLIENT_CODE_FIELD,
  CLIENT_NAME_FIELD,
  exceedsImportSize,
  MAX_IMPORT_ROWS,
  rowHasProblems,
  rowProblems,
  SHARED,
  standardTemplateRequest,
  templateSummary,
  type ImportPreview,
  type ImportResult,
  type ImportRow,
  type ImportTemplate,
} from './consigneeRules'
import { ImportTemplateEditorModal } from './ImportTemplateEditorModal'

/** Filas que se pintan en la vista previa (el lote completo vive en el servidor; hasta 5.000). */
const PREVIEW_ROWS = 200

export interface ConsigneeImportModalProps {
  /** Cliente dueño con el que arranca el selector (el filtro de la pantalla; '' = compartidos). */
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
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [problem, setProblem] = useState<{ title: string; messages: string[] } | null>(null)
  const [onlyProblems, setOnlyProblems] = useState(false)
  const [editor, setEditor] = useState<{ template: ImportTemplate | null } | null>(null)
  const [toggling, setToggling] = useState<ImportTemplate | null>(null)
  const requestRef = useRef(0)
  const keyRef = useRef<{ batch: string; key: string } | null>(null)

  const { data: clients = [] } = useClientOptions(false)
  const { data: fields = [] } = useImportFields()
  const templatesQuery = useImportTemplates(clientId || null, showInactive)
  const templates = useMemo(() => templatesQuery.data ?? [], [templatesQuery.data])
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
  const hasClientColumn = (activeTemplate?.columns ?? []).some((c) => c.field === CLIENT_CODE_FIELD || c.field === CLIENT_NAME_FIELD)
  const ownerOptions = useMemo(() => {
    const list = clients.map((c) => ({ value: c.publicId ?? '', label: c.name ?? c.code ?? '' }))
    // el cliente de arranque aunque no esté entre los activos
    if (clientId && !list.some((o) => o.value === clientId)) list.push({ value: clientId, label: clientId })
    return [{ value: SHARED, label: t('consignees.shared') }, ...list]
  }, [clients, clientId, t])

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

  const runValidate = useCallback(
    async (next: File | null, tpl: ImportTemplate | undefined, client: string, previous: ImportPreview | null) => {
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
        const dto = await validate.mutateAsync({ templatePublicId: tpl.publicId, clientPublicId: client || null, file: next })
        if (mine === requestRef.current) setPreview(dto)
      } catch (err) {
        if (mine !== requestRef.current) return
        const p = applyProblemDetails(err)
        const messages = Object.values(p.errors).flat().filter((m) => m !== p.title)
        setProblem({ title: p.title, messages })
      }
    },
    [dropBatch, validate, t],
  )

  const pickTemplate = (id: string) => {
    setTemplateId(id)
    const tpl = templates.find((x) => x.publicId === id)
    if (file) void runValidate(file, tpl, clientId, preview)
  }
  const pickClient = (id: string) => {
    setClientId(id)
    if (!file) return
    // una plantilla general o del cliente elegido sigue sirviendo: se valida otra vez con el nuevo dueño
    if (activeTemplate && (!activeTemplate.clientPublicId || activeTemplate.clientPublicId === id)) void runValidate(file, activeTemplate, id, preview)
    else {
      requestRef.current++
      setPreview(null)
      void dropBatch(preview)
    }
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
      const dto = await confirm.mutateAsync({ batchPublicId: preview.batchPublicId, idempotencyKey: keyRef.current.key })
      setResult(dto)
      toast.success(`${dto.created ?? 0} ${t('consignees.import.done')}`)
    } catch (err) {
      const p = applyProblemDetails(err)
      setProblem({ title: p.title, messages: Object.values(p.errors).flat().filter((m) => m !== p.title) })
    }
  }

  const rows = preview?.rows ?? []
  const shownRows = (onlyProblems ? rows.filter(rowHasProblems) : rows).slice(0, PREVIEW_ROWS)
  const invalidRows = (preview?.rowCount ?? 0) - (preview?.validRows ?? 0)
  const columnFields = useMemo(
    () =>
      [...(activeTemplate?.columns ?? [])]
        .sort((a, b) => (a.position ?? 0) - (b.position ?? 0))
        .map((c) => c.field ?? '')
        .filter(Boolean),
    [activeTemplate],
  )

  const createStandard = async () => {
    try {
      const created = await saveTemplate.mutateAsync({ action: 'create', body: standardTemplateRequest(t('consignees.import.standardName')) })
      setTemplateId(created.publicId ?? '')
      toast.success(t('consignees.import.tplCreated', { name: created.name ?? '' }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const busy = validate.isPending || confirm.isPending

  if (editor) {
    return (
      <ImportTemplateEditorModal
        template={editor.template}
        fields={fields}
        fieldLabel={fieldLabel}
        onClose={() => setEditor(null)}
        onSaved={(saved) => {
          setEditor(null)
          setTemplateId(saved.publicId ?? '')
          if (file) void runValidate(file, saved, clientId, preview)
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
          </div>

          {templatesQuery.isPending ? (
            <Spinner block />
          ) : activeTemplate ? (
            <div className="note cs-tplnote">
              <span>
                {t('consignees.import.mapping')}: {templateSummary(activeTemplate, fieldLabel) || t('consignees.import.ignore')}
              </span>
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
                accept=".csv,text/csv"
                disabled={!templateUsable || busy}
                onChange={(e) => {
                  const next = e.target.files?.[0] ?? null
                  e.target.value = ''
                  if (!next) return
                  setFile(next)
                  void runValidate(next, activeTemplate, clientId, preview)
                }}
              />
              {file && preview && (
                <span className="cs-filemeta">
                  {file.name} · {preview.rowCount ?? 0} {t('consignees.import.rowsFound')}
                </span>
              )}
              {file && !preview && validate.isPending && <Spinner />}
            </div>
            <p className="help">{t('consignees.import.limits', { rows: f.number(MAX_IMPORT_ROWS) })}</p>
            <p className="help">{t('consignees.import.utf8')}</p>
          </div>
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
                  {columnFields.map((f) => (
                    <th key={f}>{fieldLabel(f)}</th>
                  ))}
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
                      {columnFields.map((f) => (
                        <td key={f}>{r.values?.[f] || '—'}</td>
                      ))}
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
