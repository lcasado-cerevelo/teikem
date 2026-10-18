// Lote 111b — «Confirmar y cerrar el conteo»: antes de cerrar se dice qué va a pasar: las piezas que no se escanearon (quedarán «Por verificar») y, si aparecieron piezas perdidas o reclamadas, la orden nueva que se crea con ellas.
import { useT } from '../../kernel/i18n'
import { Modal } from '../../kernel/ui'
import type { components } from '../../kernel/api/schema'

type Preview = components['schemas']['ScanClosePreviewDto']

export function ScanReappearedConfirm({ preview, busy, onConfirm, onCancel }: { preview: Preview; busy: boolean; onConfirm: () => void; onCancel: () => void }) {
  const t = useT()
  const missing = preview.missingNumbers ?? []
  const reappeared = preview.reappearedNumbers ?? []
  return (
    <Modal
      open
      title={t('scan.reappeared.title', { order: preview.orderNumber ?? '' })}
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
        {missing.length > 0 && (
          <>
            <p>{t('scan.reappeared.missing', { count: missing.length })}</p>
            <ul data-testid="missing-pieces">
              {missing.map((p) => (
                <li key={p}>{p}</li>
              ))}
            </ul>
          </>
        )}
        {reappeared.length > 0 && (
          <>
            <p>{t('scan.reappeared.body', { count: reappeared.length, order: preview.orderNumber ?? '' })}</p>
            <p>
              <b>{t('scan.reappeared.client')}:</b> {preview.clientName} · <b>{t('scan.reappeared.consignee')}:</b> {preview.consigneeName ?? '—'} · <b>{t('scan.reappeared.status')}:</b> {preview.newOrderStatusLabel}
            </p>
            <ul data-testid="reappeared-pieces">
              {reappeared.map((p) => (
                <li key={p}>{p}</li>
              ))}
            </ul>
          </>
        )}
      </div>
    </Modal>
  )
}
