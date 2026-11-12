// Lote 239 — Importar órdenes de compra desde un archivo (pantalla «Órdenes de compra», purchasing.manage). Un solo importador con origen: archivo genérico (CSV/Excel) o QuickBooks.
// Una orden de compra dice «se compró» y NO mueve inventario (eso lo hace el recibo). «Revisar» no guarda nada: dice por orden si está lista, bloqueada (falta un producto o un proveedor,
// con el motivo), repetida (ya se importó) u omitida; «Importar» crea las listas, en borrador o ya enviadas. Lo que falta se puede bajar como lista para darlo de alta.
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useRef, useState } from 'react'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, IconUpload, Modal, Spinner, toast } from '../../kernel/ui'
import { useWarehouses, warehouseLabel } from './api'
import '../consignees/consignees.css'

type ImportResult = components['schemas']['PurchaseOrderImportResultDto']
type ImportDoc = components['schemas']['PurchaseOrderImportDocDto']

type ImportRequest = { file: File; source: 'GENERIC' | 'QUICKBOOKS'; status: 'DRAFT' | 'SENT'; warehousePublicId: string; commit: boolean }

/** `POST /api/v1/purchase-orders/import` (multipart): vista previa (`commit` false) o creación (true). */
function useImportPurchaseOrders() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: ImportRequest) => {
      const form = new FormData()
      form.append('source', v.source)
      form.append('status', v.status)
      if (v.warehousePublicId) form.append('warehousePublicId', v.warehousePublicId)
      form.append('commit', v.commit ? 'true' : 'false')
      form.append('file', v.file)
      return unwrap(api.POST('/api/v1/purchase-orders/import', { body: {} as never, bodySerializer: () => form })) as Promise<ImportResult>
    },
    onSuccess: (r) => {
      if (r.committed) void qc.invalidateQueries({ queryKey: ['/api/v1/purchase-orders'] })
    },
  })
}

const csvCell = (v: string | number | null | undefined) => `"${String(v ?? '').replace(/"/g, '""')}"`

/** La lista de lo que falta (productos y proveedores) como CSV: el SKU, la descripción, la cantidad y el último costo, listos para darlos de alta. */
function missingCsv(result: ImportResult): string {
  const lines = ['SKU,Description,Qty,Cost,Orders']
  for (const m of result.missingProducts ?? []) lines.push([m.sku, m.description, m.qty, m.cost, m.orders].map(csvCell).join(','))
  for (const s of result.missingSuppliers ?? []) lines.push(['SUPPLIER', s, '', '', ''].map(csvCell).join(','))
  return lines.join('\r\n')
}

function download(name: string, text: string) {
  const url = URL.createObjectURL(new Blob(['﻿' + text], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  URL.revokeObjectURL(url)
}

const TONE: Record<string, 'deliv' | 'fail' | 'neutral' | 'warn'> = { READY: 'deliv', CREATED: 'deliv', BLOCKED: 'fail', ERROR: 'fail', DUPLICATE: 'warn', SKIPPED: 'neutral' }

export function PurchaseOrderImportModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const run = useImportPurchaseOrders()
  const fileRef = useRef<HTMLInputElement>(null)
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const [source, setSource] = useState<'GENERIC' | 'QUICKBOOKS'>('QUICKBOOKS')
  const [status, setStatus] = useState<'DRAFT' | 'SENT'>('DRAFT')
  const [warehouse, setWarehouse] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)

  const busy = run.isPending
  const needsWarehouse = warehouses.length > 1
  const warehouseId = needsWarehouse ? warehouse || warehouses[0]?.publicId || '' : ''
  const canRun = !!file && !busy

  const review = async (commit: boolean) => {
    if (!file) return
    try {
      const r = await run.mutateAsync({ file, source, status, warehousePublicId: warehouseId, commit })
      setResult(r)
      if (commit) toast.success(t('poImport.doneToast', { n: r.created ?? 0 }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const docs = useMemo<ImportDoc[]>(() => result?.documents ?? [], [result])
  const hasMissing = (result?.missingProducts?.length ?? 0) + (result?.missingSuppliers?.length ?? 0) > 0

  return (
    <Modal
      open
      size="lg"
      title={t('poImport.title')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn sm" onClick={onClose} disabled={busy}>{result?.committed ? t('poImport.close') : t('common.cancel')}</button>
          {!result?.committed && (
            <>
              <button type="button" className="btn sm" onClick={() => void review(false)} disabled={!canRun}>{t('poImport.review')}</button>
              <button type="button" className="btn sm flow" onClick={() => void review(true)} disabled={!canRun || !result || (result.ready ?? 0) === 0}>
                {t('poImport.confirm', { n: result?.ready ?? 0 })}
              </button>
            </>
          )}
        </>
      }
    >
      <p className="help">{t('poImport.help')}</p>
      <div className="f">
        <label htmlFor="poi-source">{t('poImport.source')}</label>
        <select id="poi-source" value={source} disabled={busy} onChange={(e) => { setSource(e.target.value as 'GENERIC' | 'QUICKBOOKS'); setResult(null) }}>
          <option value="QUICKBOOKS">{t('poImport.sourceQuickBooks')}</option>
          <option value="GENERIC">{t('poImport.sourceGeneric')}</option>
        </select>
        <p className="help">{source === 'QUICKBOOKS' ? t('poImport.helpQuickBooks') : t('poImport.helpGeneric')}</p>
      </div>
      <div className="f">
        <label htmlFor="poi-status">{t('poImport.status')}</label>
        <select id="poi-status" value={status} disabled={busy} onChange={(e) => { setStatus(e.target.value as 'DRAFT' | 'SENT'); setResult(null) }}>
          <option value="DRAFT">{t('poImport.statusDraft')}</option>
          <option value="SENT">{t('poImport.statusSent')}</option>
        </select>
        <p className="help">{t('poImport.statusHelp')}</p>
      </div>
      {needsWarehouse && (
        <div className="f">
          <label htmlFor="poi-warehouse">{t('poImport.warehouse')}</label>
          <select id="poi-warehouse" value={warehouseId} disabled={busy} onChange={(e) => { setWarehouse(e.target.value); setResult(null) }}>
            {warehouses.map((w) => <option key={w.publicId} value={w.publicId ?? ''}>{warehouseLabel(w)}</option>)}
          </select>
        </div>
      )}
      <div className="f">
        <label htmlFor="poi-file">{t('poImport.file')}</label>
        <div className="cs-filerow">
          <label className="btn sm cs-pick" htmlFor="poi-file"><IconUpload /> {file ? t('poImport.changeFile') : t('poImport.choose')}</label>
          <input
            id="poi-file"
            ref={fileRef}
            className="cs-fileinput"
            type="file"
            accept=".csv,.xlsx,text/csv"
            disabled={busy}
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
      {busy && <Spinner block />}
      {result && (
        <>
          <p data-testid="poi-summary" role="status">
            {result.committed
              ? t('poImport.doneSummary', { created: result.created ?? 0, blocked: result.blocked ?? 0, duplicates: result.duplicates ?? 0, skipped: result.skipped ?? 0 })
              : t('poImport.reviewSummary', { total: result.totalDocuments ?? 0, ready: result.ready ?? 0, blocked: result.blocked ?? 0, duplicates: result.duplicates ?? 0, skipped: result.skipped ?? 0 })}
          </p>
          {(result.blocked ?? 0) > 0 && <p className="ferr" role="alert">{t('poImport.blockedNote', { n: result.blocked ?? 0 })}</p>}
          {hasMissing && (
            <div className="act" style={{ justifyContent: 'flex-start', marginBottom: 8 }}>
              <button type="button" className="btn sm" onClick={() => download('missing-products.csv', missingCsv(result))}>{t('poImport.downloadMissing')}</button>
            </div>
          )}
          <div className="tblwrap">
            <table className="lst densetbl">
              <thead>
                <tr>
                  <th>{t('poImport.col.vendor')}</th>
                  <th>{t('poImport.col.number')}</th>
                  <th>{t('poImport.col.date')}</th>
                  <th>{t('poImport.col.lines')}</th>
                  <th>{t('poImport.col.status')}</th>
                  <th>{t('poImport.col.detail')}</th>
                </tr>
              </thead>
              <tbody>
                {docs.slice(0, 300).map((d) => (
                  <tr key={d.key} data-status={d.status}>
                    <td>{d.vendor}</td>
                    <td className="mono">{d.purchaseOrderNumber ?? d.number}</td>
                    <td>{d.date}</td>
                    <td>{d.lines}</td>
                    <td><Chip tone={TONE[d.status ?? ''] ?? 'neutral'}>{t(`poImport.docStatus.${d.status}`)}</Chip></td>
                    <td>
                      {(d.issues ?? []).filter((i) => i.blocking || d.status === 'SKIPPED' || d.status === 'DUPLICATE').map((i, n) => <div key={n} className={i.blocking ? 'ferr' : 'help'}>{i.message}</div>)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {docs.length > 300 && <p className="help">{t('poImport.firstRows', { n: 300 })}</p>}
        </>
      )}
    </Modal>
  )
}
