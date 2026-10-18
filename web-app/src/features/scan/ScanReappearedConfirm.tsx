// Lote 111b — «esto es lo que va a pasar»: al cerrar el conteo de una orden con piezas que aparecieron (estaban perdidas o reclamadas) se crea UNA orden nueva con ellas; la web de staff lo confirma antes.
import { useT } from '../../kernel/i18n'
import { Modal } from '../../kernel/ui'
import type { components } from '../../kernel/api/schema'

type Preview = components['schemas']['ScanReappearedPreviewDto']

export function ScanReappearedConfirm({ preview, busy, onConfirm, onCancel }: { preview: Preview; busy: boolean; onConfirm: () => void; onCancel: () => void }) {
  const t = useT()
  const pieces = preview.pieceNumbers ?? []
  return (
    <Modal
      open
      title={t('scan.reappeared.title')}
      onClose={onCancel}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>{t('scan.reappeared.cancel')}</button>
          <button type="button" className="btn flow" onClick={onConfirm} disabled={busy}>{busy ? t('common.loading') : t('scan.reappeared.confirm')}</button>
        </>
      }
    >
      <div className="pb">
        <p>{t('scan.reappeared.body', { count: pieces.length, order: preview.orderNumber ?? '' })}</p>
        <p>
          <b>{t('scan.reappeared.client')}:</b> {preview.clientName} · <b>{t('scan.reappeared.consignee')}:</b> {preview.consigneeName ?? '—'} · <b>{t('scan.reappeared.status')}:</b> {preview.statusLabel}
        </p>
        <ul data-testid="reappeared-pieces">
          {pieces.map((p) => (
            <li key={p}>{p}</li>
          ))}
        </ul>
      </div>
    </Modal>
  )
}
