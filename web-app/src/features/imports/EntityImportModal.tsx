// Importar una entidad (productos, proveedores) con una plantilla (2026-10-21; motor genérico, dictado de Luis): elegir (o crear) la plantilla, elegir el archivo (CSV o .xlsx), «Revisar» (no guarda nada: dice
// por fila si está bien o por qué se rechaza) e «Importar» (crea las filas válidas). Solo CREA: lo que ya existe se rechaza, nunca se actualiza. Permiso: el de gestionar la entidad (`ENTITY_CONFIG`).
import { useCallback, useMemo, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, IconPlus, IconUpload, Modal, Spinner, toast } from '../../kernel/ui'
import { ImportTemplateEditorModal } from '../consignees/ImportTemplateEditorModal'
import { useEntityImportFields, useEntityImportTemplates, useImportEntity, useSaveEntityImportTemplate, type EntityImportResult } from './entityImportApi'
import { importCounts, rowErrorText, type ImportEntity } from './entityImportRules'

/** Formato estándar de cada entidad: lo habitual de su catálogo. */
export const STANDARD_COLUMNS: Record<ImportEntity, readonly string[]> = {
  PRODUCT: ['sku', 'name', 'brand', 'model', 'barcode', 'baseUom', 'trackingType', 'purchaseCost', 'salePrice'],
  SUPPLIER: ['name', 'contactName', 'phone', 'email', 'paymentTerm', 'notes'],
}

export function EntityImportModal({ entity, onClose }: { entity: ImportEntity; onClose: () => void }) {
  const t = useT()
  const fields = useEntityImportFields(entity)
  const templates = useEntityImportTemplates(entity, false)
  const saveTemplate = useSaveEntityImportTemplate(entity)
  const run = useImportEntity(entity)
  const fileRef = useRef<HTMLInputElement>(null)
  const [templateId, setTemplateId] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [result, setResult] = useState<EntityImportResult | null>(null)
  const [editing, setEditing] = useState(false)

  const list = useMemo(() => templates.data ?? [], [templates.data])
  const active = list.find((x) => x.publicId === templateId) ?? list[0]
  const busy = run.isPending || saveTemplate.isPending
  const fieldLabel = useCallback(
    (field: string) => {
      const key = `entityImport.fields.${entity}.${field}`
      const text = t(key)
      return text === key ? field : text
    },
    [t, entity],
  )

  const review = async (commit: boolean) => {
    if (!active?.publicId || !file) return
    try {
      setResult(await run.mutateAsync({ templatePublicId: active.publicId, file, commit }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const createStandard = async () => {
    try {
      const created = await saveTemplate.mutateAsync({
        action: 'create',
        body: { name: t('entityImport.standardName'), delimiter: ',', hasHeader: true, columns: STANDARD_COLUMNS[entity].map((field, i) => ({ position: i + 1, field })) },
      })
      setTemplateId(created.publicId ?? '')
      toast.success(t('orders.import.tplCreated', { name: created.name ?? '' }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  if (editing)
    return (
      <ImportTemplateEditorModal
        kind={entity}
        template={null}
        fields={fields.data ?? []}
        fieldLabel={fieldLabel}
        onClose={() => setEditing(false)}
        onSaved={(saved) => {
          setEditing(false)
          setTemplateId(saved.publicId ?? '')
          setResult(null)
        }}
      />
    )

  const counts = result ? importCounts(result.rows ?? []) : null
  const canReview = !!active?.publicId && !!file && !busy
  return (
    <Modal
      open
      size="lg"
      title={t('entityImport.title', { entity: t(`entityImport.entities.${entity}`) })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn sm" onClick={onClose} disabled={busy}>
            {result?.committed ? t('entityImport.close') : t('common.cancel')}
          </button>
          {!result?.committed && (
            <>
              <button type="button" className="btn sm" onClick={() => void review(false)} disabled={!canReview}>
                {t('entityImport.review')}
              </button>
              <button type="button" className="btn sm flow" onClick={() => void review(true)} disabled={!canReview || !result || (counts?.ok ?? 0) === 0}>
                {t('entityImport.confirm', { n: counts?.ok ?? 0 })}
              </button>
            </>
          )}
        </>
      }
    >
      <p className="help">{t('entityImport.help')}</p>
      <div className="f">
        <label htmlFor="pi-template">{t('entityImport.template')}</label>
        {templates.isPending ? (
          <Spinner />
        ) : list.length === 0 ? (
          <p className="help">{t('entityImport.noTemplates')}</p>
        ) : (
          <select
            id="pi-template"
            value={active?.publicId ?? ''}
            disabled={busy}
            onChange={(e) => {
              setTemplateId(e.target.value)
              setResult(null)
            }}
          >
            {list.map((x) => (
              <option key={x.publicId} value={x.publicId ?? ''}>
                {x.name}
              </option>
            ))}
          </select>
        )}
        <div className="cs-filerow">
          <button type="button" className="btn sm" onClick={() => setEditing(true)} disabled={busy || (fields.data ?? []).length === 0}>
            <IconPlus /> {t('entityImport.newTemplate')}
          </button>
          {list.length === 0 && (
            <button type="button" className="btn sm" onClick={() => void createStandard()} disabled={busy}>
              {t('entityImport.createStandard')}
            </button>
          )}
        </div>
      </div>
      <div className="f">
        <label htmlFor="pi-file">{t('entityImport.file')}</label>
        <div className="cs-filerow">
          <label className={active ? 'btn sm cs-pick' : 'btn sm cs-pick off'} htmlFor="pi-file">
            <IconUpload /> {file ? t('entityImport.changeFile') : t('entityImport.choose')}
          </label>
          <input
            id="pi-file"
            ref={fileRef}
            className="cs-fileinput"
            type="file"
            accept=".csv,.xlsx,text/csv"
            disabled={!active || busy}
            onChange={(e) => {
              const next = e.target.files?.[0] ?? null
              e.target.value = ''
              if (!next) return
              setFile(next)
              setResult(null)
            }}
          />
          {file && <span className="cs-filemeta">{file.name}</span>}
        </div>
      </div>
      {run.isPending && <Spinner block />}
      {result && counts && (
        <>
          <p data-testid="pi-summary" role="status">
            {result.committed
              ? t('entityImport.doneSummary', { created: result.created ?? 0, rejected: result.rejected ?? 0 })
              : t('entityImport.reviewSummary', { total: result.total ?? 0, valid: result.valid ?? 0, rejected: result.rejected ?? 0 })}
          </p>
          <div className="tblwrap">
            <table className="lst densetbl">
              <thead>
                <tr>
                  <th>{t('entityImport.col.row')}</th>
                  <th>{t(`entityImport.keyCol.${entity}`)}</th>
                  <th>{t(`entityImport.labelCol.${entity}`)}</th>
                  <th>{t('entityImport.col.status')}</th>
                  <th>{t('entityImport.col.detail')}</th>
                </tr>
              </thead>
              <tbody>
                {(result.rows ?? []).slice(0, 200).map((r) => (
                  <tr key={r.row} data-status={r.status}>
                    <td>{r.row}</td>
                    <td className="mono">{r.key}</td>
                    <td>{r.label}</td>
                    <td>
                      <Chip tone={r.status === 'ERROR' ? 'fail' : 'deliv'}>{t(`entityImport.status.${r.status}`)}</Chip>
                    </td>
                    <td>{rowErrorText(r.errors, fieldLabel)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {(result.rows ?? []).length > 200 && <p className="help">{t('entityImport.firstRows', { n: 200 })}</p>}
        </>
      )}
    </Modal>
  )
}
