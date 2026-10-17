// Exportar a contabilidad (Lote 78): en la corrida aprobada o exportada, el selector de plantilla, «Configurar…» (formato, delimitador, fecha, encabezado, columnas y su orden) y
// «Archivo de exportación» (vista previa y descarga). Todo con billing.export; la vista previa no cambia nada y la descarga pasa la corrida de Aprobada a Exportada.
import { useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, Modal, Spinner, toast } from '../../kernel/ui'
import {
  useExportFields,
  useExportPreview,
  useExportRun,
  useExportTemplates,
  useTemplateMutations,
  type BillingRunDto,
  type ExportTemplateDto,
} from './billingApi'
import {
  availableFields,
  EXPORT_DATE_FORMATS,
  EXPORT_DELIMITERS,
  EXPORT_FORMATS,
  moveColumn,
  runCanExport,
  templateFormError,
  toTemplateRequest,
  type TemplateForm,
} from './billingRules'
import { downloadExportFile } from './exportFile'

function toForm(t: ExportTemplateDto): TemplateForm {
  return {
    name: t.name ?? '',
    format: t.format ?? 'CSV',
    delimiter: t.delimiter ?? ',',
    dateFormat: t.dateFormat ?? 'YYYY-MM-DD',
    includeHeader: t.includeHeader ?? true,
    columns: (t.columns ?? []).map((c) => ({ field: c.field ?? '', header: c.header ?? '' })),
  }
}

// ---------------------------------------------------------------- la sección de la corrida

export function ExportSection({ run }: { run: BillingRunDto }) {
  const t = useT()
  const f = useFormat()
  const templates = useExportTemplates()
  const [chosen, setChosen] = useState<string | null>(null)
  const [configuring, setConfiguring] = useState(false)
  const [previewing, setPreviewing] = useState(false)
  const list = templates.data ?? []
  const selected = chosen && list.some((x) => x.publicId === chosen) ? chosen : (list[0]?.publicId ?? null)
  const current = list.find((x) => x.publicId === selected) ?? null

  if (!runCanExport(run.status)) return null
  return (
    <Can perm="billing.export">
      <div className="exp-section">
        <h3>{t('billing.exp.title')}</h3>
        {run.exportedAtUtc && <p className="help">{t('billing.exp.exportedAt', { at: f.dateTime(run.exportedAtUtc) })}</p>}
        {templates.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(templates.error).title}</p>
        ) : templates.isPending ? (
          <Spinner />
        ) : (
          <div className="exp-row">
            <div className="f">
              <label htmlFor="exp-template">{t('billing.exp.template')}</label>
              <select id="exp-template" value={selected ?? ''} onChange={(e) => setChosen(e.target.value)}>
                {list.map((x) => (
                  <option key={x.publicId} value={x.publicId}>{x.name}</option>
                ))}
              </select>
            </div>
            <button type="button" className="btn" onClick={() => setConfiguring(true)} disabled={!current}>{t('billing.exp.configure')}</button>
            <button type="button" className="btn flow" onClick={() => setPreviewing(true)} disabled={!current}>{t('billing.exp.file')}</button>
          </div>
        )}
        {configuring && current && (
          <TemplateEditor
            current={current}
            canDeactivate={list.length > 1}
            onClose={() => setConfiguring(false)}
            onSaved={(id) => {
              setChosen(id)
              setConfiguring(false)
            }}
          />
        )}
        {previewing && current && <PreviewModal run={run} template={current} onClose={() => setPreviewing(false)} />}
      </div>
    </Can>
  )
}

// ---------------------------------------------------------------- vista previa y descarga

function PreviewModal({ run, template, onClose }: { run: BillingRunDto; template: ExportTemplateDto; onClose: () => void }) {
  const t = useT()
  const preview = useExportPreview(run.id!, template.publicId ?? null)
  const exportRun = useExportRun()
  const [error, setError] = useState<string | null>(null)
  const data = preview.data
  const first = run.status === 'APPROVED'

  const download = async () => {
    setError(null)
    try {
      const file = await exportRun.mutateAsync({ runId: run.id!, templateId: template.publicId ?? null })
      downloadExportFile(file)
      toast.success(t(first ? 'billing.exp.exported' : 'billing.exp.downloaded', { id: run.id ?? 0 }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('billing.exp.previewTitle', { id: run.id ?? 0 })}
      onClose={onClose}
      dismissible={!exportRun.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={exportRun.isPending}>{t('billing.close')}</button>
          <button type="button" className="btn flow" onClick={() => void download()} disabled={!data || exportRun.isPending}>
            {exportRun.isPending ? t('common.loading') : t(first ? 'billing.exp.downloadMark' : 'billing.exp.download')}
          </button>
        </>
      }
    >
      <div className="pb">
        {preview.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(preview.error).title}</p>
        ) : !data ? (
          <Spinner block />
        ) : (
          <>
            <p className="help">{t('billing.exp.previewHelp', { file: data.fileName ?? '', rows: data.rowCount ?? 0, template: data.templateName ?? '' })}</p>
            {first && <p className="help">{t('billing.exp.markHelp')}</p>}
            <pre className="exp-pre" tabIndex={0} aria-label={t('billing.exp.fileText')}>{data.text}</pre>
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------- configurar una plantilla

function TemplateEditor({ current, canDeactivate, onClose, onSaved }: { current: ExportTemplateDto; canDeactivate: boolean; onClose: () => void; onSaved: (id: string) => void }) {
  const t = useT()
  const fields = useExportFields()
  const { create, update, deactivate } = useTemplateMutations()
  const [form, setForm] = useState<TemplateForm>(() => toForm(current))
  const [error, setError] = useState<string | null>(null)
  const [removing, setRemoving] = useState(false)
  const set = (patch: Partial<TemplateForm>) => setForm((x) => ({ ...x, ...patch }))
  const catalog = fields.data ?? []
  const labelOf = (code: string) => catalog.find((c) => c.code === code)?.label ?? code
  const defaultHeaderOf = (code: string) => catalog.find((c) => c.code === code)?.defaultHeader ?? ''
  const free = availableFields(catalog, form.columns)
  const busy = create.isPending || update.isPending || deactivate.isPending

  const validate = () => {
    const problem = templateFormError(form)
    if (problem) {
      setError(t(`billing.exp.errors.${problem}`))
      return false
    }
    setError(null)
    return true
  }
  const save = async (asNew: boolean) => {
    if (!validate()) return
    try {
      const saved = asNew ? await create.mutateAsync(toTemplateRequest(form)) : await update.mutateAsync({ id: current.publicId!, body: toTemplateRequest(form) })
      toast.success(t(asNew ? 'billing.exp.createdToast' : 'billing.exp.savedToast'))
      onSaved(saved.publicId!)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('billing.exp.configureTitle')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          {canDeactivate && (
            <button type="button" className="btn danger" onClick={() => setRemoving(true)} disabled={busy}>{t('billing.exp.deactivate')}</button>
          )}
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('billing.close')}</button>
          <button type="button" className="btn" onClick={() => void save(true)} disabled={busy}>{t('billing.exp.saveAsNew')}</button>
          <button type="button" className="btn flow" onClick={() => void save(false)} disabled={busy}>{busy ? t('common.loading') : t('billing.exp.save')}</button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t('billing.exp.configureHelp')}</p>
        <div className="f">
          <label htmlFor="tpl-name">{t('billing.exp.name')}</label>
          <input id="tpl-name" value={form.name} maxLength={80} autoComplete="off" onChange={(e) => set({ name: e.target.value })} />
        </div>
        <div className="exp-grid">
          <div className="f">
            <label htmlFor="tpl-format">{t('billing.exp.format')}</label>
            <select id="tpl-format" value={form.format} onChange={(e) => set({ format: e.target.value })}>
              {EXPORT_FORMATS.map((x) => (
                <option key={x} value={x}>{x}</option>
              ))}
            </select>
          </div>
          <div className="f">
            <label htmlFor="tpl-delimiter">{t('billing.exp.delimiter')}</label>
            <select id="tpl-delimiter" value={form.delimiter} onChange={(e) => set({ delimiter: e.target.value })}>
              {EXPORT_DELIMITERS.map((d) => (
                <option key={d.key} value={d.value}>{t(`billing.exp.delimiters.${d.key}`)}</option>
              ))}
            </select>
          </div>
          <div className="f">
            <label htmlFor="tpl-date">{t('billing.exp.dateFormat')}</label>
            <select id="tpl-date" value={form.dateFormat} onChange={(e) => set({ dateFormat: e.target.value })}>
              {EXPORT_DATE_FORMATS.map((x) => (
                <option key={x} value={x}>{x}</option>
              ))}
            </select>
          </div>
        </div>
        <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input type="checkbox" checked={form.includeHeader} onChange={(e) => set({ includeHeader: e.target.checked })} />
          {t('billing.exp.header')}
        </label>

        <h3 className="exp-h">{t('billing.exp.columns')}</h3>
        {fields.isPending ? (
          <Spinner />
        ) : (
          <>
            <ol className="exp-cols" aria-label={t('billing.exp.columns')}>
              {form.columns.map((c, i) => (
                <li key={c.field}>
                  <span className="exp-col-name">{labelOf(c.field)}</span>
                  <input
                    aria-label={t('billing.exp.headerOf', { field: labelOf(c.field) })}
                    value={c.header}
                    maxLength={60}
                    autoComplete="off"
                    placeholder={defaultHeaderOf(c.field)}
                    onChange={(e) => set({ columns: form.columns.map((x, j) => (j === i ? { ...x, header: e.target.value } : x)) })}
                  />
                  <button type="button" className="btn" aria-label={t('billing.exp.up', { field: labelOf(c.field) })} disabled={i === 0} onClick={() => set({ columns: moveColumn(form.columns, i, -1) })}>↑</button>
                  <button type="button" className="btn" aria-label={t('billing.exp.down', { field: labelOf(c.field) })} disabled={i === form.columns.length - 1} onClick={() => set({ columns: moveColumn(form.columns, i, 1) })}>↓</button>
                  <button type="button" className="btn danger" aria-label={t('billing.exp.remove', { field: labelOf(c.field) })} onClick={() => set({ columns: form.columns.filter((_, j) => j !== i) })}>✕</button>
                </li>
              ))}
            </ol>
            {free.length > 0 && (
              <div className="f">
                <label htmlFor="tpl-add">{t('billing.exp.addField')}</label>
                <select
                  id="tpl-add"
                  value=""
                  onChange={(e) => {
                    const code = e.target.value
                    if (code) set({ columns: [...form.columns, { field: code, header: '' }] })
                  }}
                >
                  <option value="">{t('billing.exp.chooseField')}</option>
                  {free.map((c) => (
                    <option key={c.code} value={c.code}>{c.label}</option>
                  ))}
                </select>
              </div>
            )}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
      <ConfirmDialog
        open={removing}
        title={t('billing.exp.deactivateTitle', { name: current.name ?? '' })}
        message={<p>{t('billing.exp.deactivateBody')}</p>}
        confirmLabel={t('billing.exp.deactivate')}
        tone="danger"
        onConfirm={async () => {
          await deactivate.mutateAsync(current.publicId!)
          toast.success(t('billing.exp.deactivatedToast'))
          onClose()
        }}
        onClose={() => setRemoving(false)}
      />
    </Modal>
  )
}
