// Importar órdenes desde CSV (Lote F-C3; mismo flujo que la importación de consignatarios, manual 03 §7): plantilla → cliente → archivo →
// vista previa que valida el SERVIDOR (`validate-file`: consignatario existente / por crear, errores y avisos por fila) → Importar
// (`confirm` de las válidas, con las casillas «Confirmar las órdenes al importar», «Autorizar crédito excedido» y «Aceptar facturas
// repetidas», e `Idempotency-Key` estable por lote y opciones) o descartar el lote → resultado (creadas, fallidas, omitidas; número de
// orden o error por fila). Validar/confirmar/descartar: `orders.create`; plantillas: `orders.edit`; módulo LTL_GROUND (lo aplica la ruta).
import { useCallback, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, IconCheck, IconGear, IconPlus, IconUpload, Modal, Spinner, toast } from '../../kernel/ui'
import { IconPower, IconRotateCcw } from '../../kernel/ui/actionIcons'
import { useClientOptions } from '../consignees/api'
import { ImportTemplateEditorModal } from '../consignees/ImportTemplateEditorModal'
import { exceedsImportSize, MAX_IMPORT_ROWS, rowHasProblems, rowProblems, templateSummary, type ImportTemplate } from '../consignees/consigneeRules'
import '../consignees/consignees.css'
import {
  useConfirmOrderImport,
  useDiscardOrderImport,
  useOrderImportFields,
  useOrderImportTemplates,
  useSaveOrderImportTemplate,
  useSetOrderTemplateActive,
  useValidateOrderImport,
} from './importApi'
import {
  buildConfirmRequest,
  confirmSignature,
  DEFAULT_IMPORT_OPTIONS,
  failedRows,
  orderStandardTemplateRequest,
  rowStatus,
  type ImportOptions,
  type OrderImportPreview,
  type OrderImportResult,
  type OrderImportRow,
} from './orderImportRules'
import './orders.css'

/** Filas que se pintan en la vista previa y en el resultado (el lote completo, hasta 5.000, vive en el servidor). */
const PREVIEW_ROWS = 200

export interface OrderImportModalProps {
  /** Cliente con el que arranca el selector ('' = ninguno). */
  defaultClient?: string
  onClose: () => void
}

export function OrderImportModal({ defaultClient = '', onClose }: OrderImportModalProps) {
  const t = useT()
  const f = useFormat()
  const canImport = useCan('orders.create')
  const canTemplates = useCan('orders.edit')
  const canOverride = useCan('orders.credit_override')
  const [templateId, setTemplateId] = useState('')
  const [showInactive, setShowInactive] = useState(false)
  const [clientId, setClientId] = useState(defaultClient)
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<OrderImportPreview | null>(null)
  const [result, setResult] = useState<OrderImportResult | null>(null)
  const [problem, setProblem] = useState<{ title: string; messages: string[] } | null>(null)
  const [onlyProblems, setOnlyProblems] = useState(false)
  const [options, setOptions] = useState<ImportOptions>(DEFAULT_IMPORT_OPTIONS)
  const [editor, setEditor] = useState<{ template: ImportTemplate | null } | null>(null)
  const [toggling, setToggling] = useState<ImportTemplate | null>(null)
  const requestRef = useRef(0)
  const keyRef = useRef<{ sig: string; key: string } | null>(null)

  const { data: clients = [] } = useClientOptions(false)
  const { data: fields = [] } = useOrderImportFields(canTemplates)
  const templatesQuery = useOrderImportTemplates(clientId, showInactive, canTemplates)
  const templates = useMemo(() => (canTemplates ? (templatesQuery.data ?? []) : []), [canTemplates, templatesQuery.data])
  const validate = useValidateOrderImport()
  const confirm = useConfirmOrderImport()
  const discard = useDiscardOrderImport()
  const saveTemplate = useSaveOrderImportTemplate()
  const setTemplateActive = useSetOrderTemplateActive()

  const fieldLabel = useCallback(
    (field: string) => {
      const key = `orders.import.fields.${field}`
      const text = t(key)
      return text === key ? field : text
    },
    [t],
  )

  // nunca empieza «sin plantilla» si hay alguna: la elegida o la primera activa
  const activeTemplate = templates.find((x) => x.publicId === templateId) ?? templates.find((x) => x.isActive !== false) ?? templates[0]
  const templateUsable = !!activeTemplate && activeTemplate.isActive !== false
  const clientOptions = useMemo(() => {
    const list = clients.map((c) => ({ value: c.publicId ?? '', label: [c.code, c.name].filter(Boolean).join(' · ') }))
    // el cliente de arranque aunque no esté entre los activos
    if (clientId && !list.some((o) => o.value === clientId)) list.push({ value: clientId, label: clientId })
    return list
  }, [clients, clientId])

  /** Descarta el lote anterior (si lo hay) sin molestar: el error de un descarte no importa. */
  const dropBatch = useCallback(
    async (batch: OrderImportPreview | null) => {
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
    async (next: File | null, tpl: ImportTemplate | undefined, client: string, previous: OrderImportPreview | null) => {
      const mine = ++requestRef.current
      setProblem(null)
      setResult(null)
      setPreview(null)
      await dropBatch(previous)
      if (!next || !tpl?.publicId || tpl.isActive === false || !client) return
      if (exceedsImportSize(next.size)) {
        setProblem({ title: t('orders.import.limitExceeded'), messages: [] })
        return
      }
      try {
        const dto = await validate.mutateAsync({ templatePublicId: tpl.publicId, clientPublicId: client, file: next })
        if (mine === requestRef.current) setPreview(dto)
      } catch (err) {
        if (mine !== requestRef.current) return
        const p = applyProblemDetails(err)
        setProblem({ title: p.title, messages: Object.values(p.errors).flat().filter((m) => m !== p.title) })
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
    // una plantilla general o del cliente elegido sigue sirviendo: se valida otra vez con el nuevo cliente
    if (id && activeTemplate && (!activeTemplate.clientPublicId || activeTemplate.clientPublicId === id)) void runValidate(file, activeTemplate, id, preview)
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

  /** «Descartar»: cierra el lote validado sin crear nada y vuelve a pedir un archivo (el modal sigue abierto). */
  const discardBatch = async () => {
    requestRef.current++
    const batch = preview
    setPreview(null)
    setFile(null)
    setProblem(null)
    if (!batch?.batchPublicId) return
    try {
      await discard.mutateAsync(batch.batchPublicId)
      toast.info(t('orders.import.discarded'))
    } catch (err) {
      setProblem({ title: applyProblemDetails(err).title, messages: [] })
    }
  }

  const body = buildConfirmRequest(options, canOverride)
  const doConfirm = async () => {
    if (!preview?.batchPublicId) return
    // la misma clave en un reintento de la misma confirmación (el servidor no crea dos veces); otras opciones son otra operación
    const sig = confirmSignature(preview.batchPublicId, body)
    if (keyRef.current?.sig !== sig) keyRef.current = { sig, key: crypto.randomUUID() }
    try {
      const dto = await confirm.mutateAsync({ batchPublicId: preview.batchPublicId, body, idempotencyKey: keyRef.current.key })
      setResult(dto)
      toast.success(t('orders.import.done', { count: dto.created ?? 0 }))
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
      const created = await saveTemplate.mutateAsync({ action: 'create', body: orderStandardTemplateRequest(t('orders.import.standardName')) })
      setTemplateId(created.publicId ?? '')
      toast.success(t('orders.import.tplCreated', { name: created.name ?? '' }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const busy = validate.isPending || confirm.isPending || discard.isPending

  if (editor) {
    return (
      <ImportTemplateEditorModal
        kind="ORDER"
        template={editor.template}
        fields={fields}
        fieldLabel={fieldLabel}
        onClose={() => setEditor(null)}
        onSaved={(saved) => {
          setEditor(null)
          setTemplateId(saved.publicId ?? '')
          if (file && clientId) void runValidate(file, saved, clientId, preview)
        }}
      />
    )
  }

  const statusChip = (row: OrderImportRow) => {
    const s = rowStatus(row, result !== null)
    const tone = s === 'ok' || s === 'created' ? 'deliv' : s === 'warn' ? 'warn' : s === 'skipped' ? 'cap' : 'fail'
    return <Chip tone={tone}>{t(`orders.import.rowStatus.${s}`)}</Chip>
  }
  const consigneeCell = (row: OrderImportRow) => {
    const c = row.consignee
    if (!c?.action) return '—'
    return t(c.action === 'EXISTING' ? 'orders.import.consigneeExisting' : 'orders.import.consigneeNew', { name: c.name ?? '' })
  }
  const resultRows = (result?.rows ?? []).slice(0, PREVIEW_ROWS)

  return (
    <>
      <Modal
        open
        size="lg"
        title={t('orders.import.title')}
        onClose={() => void cancel()}
        dismissible={!busy && toggling === null}
        footer={
          result ? (
            <button type="button" className="btn sm flow" onClick={onClose}>
              {t('orders.import.close')}
            </button>
          ) : (
            <>
              <button type="button" className="btn sm" onClick={() => void cancel()} disabled={busy}>
                {t('orders.import.cancel')}
              </button>
              {preview && (
                <button type="button" className="btn sm" onClick={() => void discardBatch()} disabled={busy}>
                  {t('orders.import.discard')}
                </button>
              )}
              <button type="button" className="btn sm flow" disabled={!canImport || busy || !preview || (preview.validRows ?? 0) === 0} onClick={() => void doConfirm()}>
                <IconCheck /> {confirm.isPending ? t('common.loading') : t('orders.import.do')}
              </button>
            </>
          )
        }
      >
        <p className="cs-importsub">{t('orders.import.sub')}</p>

        {!result && (
          <>
            {canTemplates ? (
              <>
                <div className="cs-tplrow">
                  <div className="f">
                    <label htmlFor="oi-tpl">{t('orders.import.tplLabel')}</label>
                    <select id="oi-tpl" value={activeTemplate?.publicId ?? ''} onChange={(e) => pickTemplate(e.target.value)} disabled={busy}>
                      {templates.length === 0 && <option value="">{t('orders.import.tplNone')}</option>}
                      {templates.map((x) => (
                        <option key={x.publicId} value={x.publicId}>
                          {x.name}
                          {x.clientName ? ` · ${x.clientName}` : ''}
                          {x.isActive === false ? ` (${t('orders.import.tplInactive')})` : ''}
                        </option>
                      ))}
                    </select>
                  </div>
                  <button type="button" className="btn sm" onClick={() => setEditor({ template: null })} disabled={busy}>
                    <IconPlus /> {t('orders.import.tplNewBtn')}
                  </button>
                  {activeTemplate && (
                    <button type="button" className="btn sm" onClick={() => setEditor({ template: activeTemplate })} disabled={busy}>
                      <IconGear /> {t('orders.import.tplEditBtn')}
                    </button>
                  )}
                </div>

                <label className="sw cs-inactive-tpl">
                  <input type="checkbox" role="switch" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
                  <span className="tk" aria-hidden="true" />
                  <span>{t('orders.import.showInactiveTpl')}</span>
                </label>
              </>
            ) : (
              <div className="note cs-tplnote">
                <span>{t('orders.import.noTemplatePermission')}</span>
              </div>
            )}

            <div className="f">
              <label htmlFor="oi-client">{t('orders.import.clientLabel')}</label>
              <select id="oi-client" value={clientId} onChange={(e) => pickClient(e.target.value)} disabled={busy}>
                <option value="">{t('orders.import.clientPlaceholder')}</option>
                {clientOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
              <p className="help">{t('orders.import.clientHint')}</p>
            </div>

            {canTemplates &&
              (templatesQuery.isPending ? (
                <Spinner block />
              ) : activeTemplate ? (
                <div className="note cs-tplnote">
                  <span>
                    {t('orders.import.mapping')}: {templateSummary(activeTemplate, fieldLabel) || t('orders.import.ignore')}
                  </span>
                  <button
                    type="button"
                    className="rowbtn"
                    title={activeTemplate.isActive === false ? t('orders.import.tplReactivate') : t('orders.import.tplDeactivate')}
                    aria-label={activeTemplate.isActive === false ? t('orders.import.tplReactivate') : t('orders.import.tplDeactivate')}
                    onClick={() => setToggling(activeTemplate)}
                    disabled={busy}
                  >
                    {activeTemplate.isActive === false ? <IconRotateCcw /> : <IconPower />}
                  </button>
                </div>
              ) : (
                <div className="note cs-tplnote">
                  <span>{t('orders.import.tplNoneHint')}</span>
                  <button type="button" className="btn sm" onClick={() => void createStandard()} disabled={saveTemplate.isPending}>
                    {t('orders.import.createStandard')}
                  </button>
                </div>
              ))}

            <div className="f">
              <label htmlFor="oi-file">{t('orders.import.file')}</label>
              <div className="cs-filerow">
                <label className={templateUsable && clientId ? 'btn sm cs-pick' : 'btn sm cs-pick off'} htmlFor="oi-file">
                  <IconUpload /> {file ? t('orders.import.changeFile') : t('orders.import.choose')}
                </label>
                <input
                  id="oi-file"
                  className="cs-fileinput"
                  type="file"
                  accept=".csv,text/csv"
                  disabled={!templateUsable || !clientId || busy}
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
                    {file.name} · {preview.rowCount ?? 0} {t('orders.import.rowsFound')}
                  </span>
                )}
                {file && !preview && validate.isPending && <Spinner />}
              </div>
              {!clientId && <p className="help">{t('orders.import.needClient')}</p>}
              <p className="help">{t('orders.import.limits', { rows: f.number(MAX_IMPORT_ROWS) })}</p>
              <p className="help">{t('orders.import.utf8')}</p>
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
            <h3 className="cs-sub">{t('orders.import.resultTitle')}</h3>
            <div className="cs-stats">
              <span className="cs-stat ok">
                <b>{result.created ?? 0}</b> {t('orders.import.created')}
              </span>
              <span className="cs-stat bad">
                <b>{result.failed ?? 0}</b> {t('orders.import.failed')}
              </span>
              <span className="cs-stat">
                <b>{result.skipped ?? 0}</b> {t('orders.import.skipped')}
              </span>
            </div>
            {resultRows.length > 0 && (
              <div className="cs-tablewrap">
                <table className="lst densetbl">
                  <thead>
                    <tr>
                      <th>{t('orders.import.line')}</th>
                      <th>{t('orders.import.rowState')}</th>
                      <th>{t('orders.import.orderNumber')}</th>
                      <th>{t('orders.import.remarks')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {resultRows.map((r) => (
                      <tr key={r.row} className={failedRows([r]).length > 0 ? 'cs-badrow' : undefined}>
                        <td className="mono">{r.line ?? r.row}</td>
                        <td>{statusChip(r)}</td>
                        <td className="mono">
                          {r.orderPublicId ? (
                            <Link to={`/orders/${r.orderPublicId}`} className="ref">
                              {r.orderNumber}
                            </Link>
                          ) : (
                            '—'
                          )}
                        </td>
                        <td className="cs-remarks">
                          {r.error ? <span className="cs-err">{r.error}</span> : r.orderPublicId ? (r.orderStatus ? <StatusChip domain="OrderStatus" code={r.orderStatus} /> : '—') : '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        ) : preview ? (
          <div className="cs-preview">
            <div className="cs-prevhead">
              <b>{t('orders.import.preview')}</b>
              <span className="cs-counts">
                <span className="cs-stat ok">
                  <b>{preview.validRows ?? 0}</b> {t('orders.import.valid')}
                </span>
                <span className="cs-stat bad">
                  <b>{invalidRows}</b> {t('orders.import.invalid')}
                </span>
              </span>
              {rows.some(rowHasProblems) && (
                <label className="sw cs-onlyprob">
                  <input type="checkbox" role="switch" checked={onlyProblems} onChange={(e) => setOnlyProblems(e.target.checked)} />
                  <span className="tk" aria-hidden="true" />
                  <span>{t('orders.import.onlyProblems')}</span>
                </label>
              )}
            </div>
            <div className="cs-tablewrap">
              <table className="lst densetbl">
                <thead>
                  <tr>
                    <th>{t('orders.import.line')}</th>
                    <th>{t('orders.import.rowState')}</th>
                    {columnFields.map((c) => (
                      <th key={c}>{fieldLabel(c)}</th>
                    ))}
                    <th>{t('orders.import.consignee')}</th>
                    <th>{t('orders.import.remarks')}</th>
                  </tr>
                </thead>
                <tbody>
                  {shownRows.map((r) => {
                    const p = rowProblems(r, fieldLabel)
                    return (
                      <tr key={r.row} className={r.isValid ? undefined : 'cs-badrow'}>
                        <td className="mono">{r.line ?? r.row}</td>
                        <td>{statusChip(r)}</td>
                        {columnFields.map((c) => (
                          <td key={c}>{r.values?.[c] || '—'}</td>
                        ))}
                        <td>{consigneeCell(r)}</td>
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
            {rows.length > shownRows.length && <p className="help">{t('orders.import.moreRows', { shown: shownRows.length, total: onlyProblems ? rows.filter(rowHasProblems).length : rows.length })}</p>}

            <fieldset className="oi-options" disabled={!canImport || busy}>
              <legend>{t('orders.import.optionsTitle')}</legend>
              {canOverride && (
                <>
                  <label className="sw">
                    <input
                      type="checkbox"
                      role="switch"
                      checked={options.overrideCredit}
                      onChange={(e) => setOptions((o) => ({ ...o, overrideCredit: e.target.checked }))}
                    />
                    <span className="tk" aria-hidden="true" />
                    <span>{t('orders.import.optOverride')}</span>
                  </label>
                  <p className="help">{t('orders.import.optOverrideHelp')}</p>
                </>
              )}
              <label className="sw">
                <input
                  type="checkbox"
                  role="switch"
                  checked={options.confirmDuplicateInvoice}
                  onChange={(e) => setOptions((o) => ({ ...o, confirmDuplicateInvoice: e.target.checked }))}
                />
                <span className="tk" aria-hidden="true" />
                <span>{t('orders.import.optDuplicate')}</span>
              </label>
              <p className="help">{t('orders.import.optDuplicateHelp')}</p>
            </fieldset>
          </div>
        ) : (
          !problem && (
            <div className="empty cs-noFile">
              <p>{t('orders.import.noFile')}</p>
            </div>
          )
        )}
      </Modal>
      <ConfirmDialog
        open={toggling !== null}
        tone={toggling?.isActive === false ? 'flow' : 'danger'}
        title={toggling?.isActive === false ? t('orders.import.tplReactivateTitle') : t('orders.import.tplDeactivateTitle')}
        message={t(toggling?.isActive === false ? 'orders.import.tplReactivateBody' : 'orders.import.tplDeactivateBody', { name: toggling?.name ?? '' })}
        confirmLabel={toggling?.isActive === false ? t('orders.import.tplReactivate') : t('orders.import.tplDeactivate')}
        onConfirm={async () => {
          if (!toggling?.publicId) return
          const active = toggling.isActive === false
          await setTemplateActive.mutateAsync({ publicId: toggling.publicId, active })
          toast.success(t(active ? 'orders.import.tplReactivated' : 'orders.import.tplDeactivated', { name: toggling.name ?? '' }))
          if (!active) setTemplateId('')
        }}
        onClose={() => setToggling(null)}
      />
    </>
  )
}
