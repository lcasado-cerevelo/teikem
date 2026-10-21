// Importar PRODUCTOS con una plantilla (2026-10-21; primera entidad del motor genérico, dictado de Luis): elegir (o crear) la plantilla, elegir el archivo (CSV o .xlsx), «Revisar» (no guarda nada: dice
// por fila si está bien o por qué se rechaza) e «Importar» (crea las filas válidas). Solo CREA: un SKU o código de barras que ya existe se rechaza, nunca se actualiza. inventory.manage.
import { useCallback, useMemo, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, IconPlus, IconUpload, Modal, Spinner, toast } from '../../kernel/ui'
import { ImportTemplateEditorModal } from '../consignees/ImportTemplateEditorModal'
import { useImportProducts, useProductImportFields, useProductImportTemplates, useSaveProductImportTemplate, type ProductImportResult } from './productImportApi'
import { importCounts, rowErrorText } from './productImportRules'

/** Formato estándar: lo habitual de un catálogo (SKU, nombre, marca, modelo, código de barras, unidad, seguimiento, costo y precio). */
export const PRODUCT_STANDARD_COLUMNS = ['sku', 'name', 'brand', 'model', 'barcode', 'baseUom', 'trackingType', 'purchaseCost', 'salePrice'] as const

export function ProductImportModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const fields = useProductImportFields()
  const templates = useProductImportTemplates(false)
  const saveTemplate = useSaveProductImportTemplate()
  const run = useImportProducts()
  const fileRef = useRef<HTMLInputElement>(null)
  const [templateId, setTemplateId] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [result, setResult] = useState<ProductImportResult | null>(null)
  const [editing, setEditing] = useState(false)

  const list = useMemo(() => templates.data ?? [], [templates.data])
  const active = list.find((x) => x.publicId === templateId) ?? list[0]
  const busy = run.isPending || saveTemplate.isPending
  const fieldLabel = useCallback(
    (field: string) => {
      const key = `products.import.fields.${field}`
      const text = t(key)
      return text === key ? field : text
    },
    [t],
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
        body: { name: t('products.import.standardName'), delimiter: ',', hasHeader: true, columns: PRODUCT_STANDARD_COLUMNS.map((field, i) => ({ position: i + 1, field })) },
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
        kind="PRODUCT"
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
      title={t('products.import.title')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn sm" onClick={onClose} disabled={busy}>
            {result?.committed ? t('products.import.close') : t('common.cancel')}
          </button>
          {!result?.committed && (
            <>
              <button type="button" className="btn sm" onClick={() => void review(false)} disabled={!canReview}>
                {t('products.import.review')}
              </button>
              <button type="button" className="btn sm flow" onClick={() => void review(true)} disabled={!canReview || !result || (counts?.ok ?? 0) === 0}>
                {t('products.import.confirm', { n: counts?.ok ?? 0 })}
              </button>
            </>
          )}
        </>
      }
    >
      <p className="help">{t('products.import.help')}</p>
      <div className="f">
        <label htmlFor="pi-template">{t('products.import.template')}</label>
        {templates.isPending ? (
          <Spinner />
        ) : list.length === 0 ? (
          <p className="help">{t('products.import.noTemplates')}</p>
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
            <IconPlus /> {t('products.import.newTemplate')}
          </button>
          {list.length === 0 && (
            <button type="button" className="btn sm" onClick={() => void createStandard()} disabled={busy}>
              {t('products.import.createStandard')}
            </button>
          )}
        </div>
      </div>
      <div className="f">
        <label htmlFor="pi-file">{t('products.import.file')}</label>
        <div className="cs-filerow">
          <label className={active ? 'btn sm cs-pick' : 'btn sm cs-pick off'} htmlFor="pi-file">
            <IconUpload /> {file ? t('products.import.changeFile') : t('products.import.choose')}
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
              ? t('products.import.doneSummary', { created: result.created ?? 0, rejected: result.rejected ?? 0 })
              : t('products.import.reviewSummary', { total: result.total ?? 0, valid: result.valid ?? 0, rejected: result.rejected ?? 0 })}
          </p>
          <div className="tblwrap">
            <table className="lst densetbl">
              <thead>
                <tr>
                  <th>{t('products.import.col.row')}</th>
                  <th>SKU</th>
                  <th>{t('products.import.col.name')}</th>
                  <th>{t('products.import.col.status')}</th>
                  <th>{t('products.import.col.detail')}</th>
                </tr>
              </thead>
              <tbody>
                {(result.rows ?? []).slice(0, 200).map((r) => (
                  <tr key={r.row} data-status={r.status}>
                    <td>{r.row}</td>
                    <td className="mono">{r.sku}</td>
                    <td>{r.name}</td>
                    <td>
                      <Chip tone={r.status === 'ERROR' ? 'fail' : 'deliv'}>{t(`products.import.status.${r.status}`)}</Chip>
                    </td>
                    <td>{rowErrorText(r.errors, fieldLabel)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {(result.rows ?? []).length > 200 && <p className="help">{t('products.import.firstRows', { n: 200 })}</p>}
        </>
      )}
    </Modal>
  )
}
