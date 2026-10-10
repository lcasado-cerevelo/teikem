// 2026-10-10 (Luis): en Ubicaciones, cuando una posición tiene más de un producto la columna dice «N productos»; ese texto es un enlace que
// abre esta ventana con lo que hay en la posición (producto, lote, en mano y disponible). Lee `GET /inventory/balances?binIds=` (inventory.view).
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import { useLang, useT } from '../../kernel/i18n'
import { Modal, Spinner } from '../../kernel/ui'
import { formatNumber } from './lineRules'

export interface BinContentsModalProps {
  warehousePublicId: string
  bin: { id: number; code: string }
  onClose: () => void
}

export function BinContentsModal({ warehousePublicId, bin, onClose }: BinContentsModalProps) {
  const t = useT()
  const lang = useLang()
  const q = useQuery({
    queryKey: ['bin-contents', warehousePublicId, bin.id],
    queryFn: () =>
      unwrap(api.GET('/api/v1/inventory/balances', { params: { query: { warehousePublicIds: [warehousePublicId], binIds: [bin.id], skip: 0, take: 200 } } })),
  })
  const rows = q.data?.items ?? []
  const fmt = (n: number | undefined) => formatNumber(n ?? 0, lang)
  return (
    <Modal open title={t('warehouse.locations.contents.title', { bin: bin.code })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('warehouse.locations.contents.close')}</button>}>
      {q.isLoading && <Spinner />}
      {q.error && (
        <p className="note ferr" role="alert">
          {q.error.message || t('errors.generic')}
        </p>
      )}
      {!q.isLoading && !q.error && rows.length === 0 && <p className="note">{t('warehouse.locations.contents.empty')}</p>}
      {rows.length > 0 && (
        <div className="tbl-wrap">
          <table className="tbl" aria-label={t('warehouse.locations.contents.title', { bin: bin.code })}>
            <thead>
              <tr>
                <th>{t('warehouse.locations.contents.sku')}</th>
                <th>{t('warehouse.locations.contents.product')}</th>
                <th>{t('warehouse.locations.contents.lot')}</th>
                <th className="num">{t('warehouse.locations.contents.onHand')}</th>
                <th className="num">{t('warehouse.locations.contents.available')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td className="ref">{r.sku}</td>
                  <td>{r.productName}</td>
                  <td>{r.lotNumber ?? '—'}</td>
                  <td className="num mono">{fmt(r.qtyOnHand)}</td>
                  <td className="num mono">{fmt(r.qtyAvailable)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  )
}
