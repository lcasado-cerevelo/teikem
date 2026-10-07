// Repartir una línea de un recibo DIRECTO a posición en varias posiciones (pedido del dueño 2026-10-07; en la app ya existía): cantidad por posición, las
// posiciones una por una, la del resto con alerta fija, ✕ para quitar, aviso de cupo sin bloquear. Solo recibos sin documento (ciego o devolución: con aviso u orden
// de compra cada línea entra a una sola posición) y productos sin lote ni serie. Al guardar: la línea toma la primera posición y su cantidad; las demás posiciones
// son líneas nuevas; lo que no cupo en posiciones llenas queda en una línea SIN posición (se elige en la rejilla; Confirmar la exige). Son llamadas de línea una tras otra:
// si alguna falla se avisa cuáles quedaron hechas.
import { useState } from 'react'
import { useLang, useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { useSaveReceiptLine, type ReceiptDetailDto } from './api'
import { formatNumber } from './lineRules'
import { problemText } from './problemText'
import { parseQtyText, type LineRow } from './receiptLineEdit'
import { TARGET_EXCLUDED_ZONE_TYPES } from './receivingMode'
import { SplitBinsEditor } from './SplitBinsEditor'
import { chunkAt, parsePerBin, splitSummary, type SplitBin } from './splitPlan'

export function ReceiptSplitModal({ receipt, row, onClose }: { receipt: ReceiptDetailDto; row: LineRow; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const save = useSaveReceiptLine()
  const publicId = receipt.header?.publicId ?? ''
  const total = parseQtyText(row.received) ?? 0
  const hadExpected = (parseQtyText(row.expected) ?? 0) > 0
  const [perBinText, setPerBinText] = useState('')
  const [bins, setBins] = useState<SplitBin[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const perBin = parsePerBin(perBinText)
  const ready = perBin > 0 && bins.length > 0

  const submit = async () => {
    if (!ready || row.lineId === null) return
    setBusy(true)
    setError(null)
    let done = 0
    try {
      const productPublicId = row.productPublicId
      // 1) la línea existente toma la primera posición y su cantidad (el esperado se ajusta si lo había: sin diferencia falsa)
      const first = chunkAt(total, perBin, 0)
      await save.mutateAsync({
        publicId,
        action: 'update',
        lineId: row.lineId,
        body: { receivedQty: first, ...(hadExpected ? { expectedQty: first } : {}), targetBinId: bins[0].id },
      })
      done++
      // 2) una línea nueva por cada posición que sigue
      for (let i = 1; i < bins.length; i++) {
        const qty = chunkAt(total, perBin, i)
        await save.mutateAsync({
          publicId,
          action: 'add',
          body: { productPublicId, receivedQty: qty, expectedQty: hadExpected ? qty : null, targetBinId: bins[i].id },
        })
        done++
      }
      // 3) lo que no cupo queda en una línea sin posición, para elegirla en la rejilla
      const { left } = splitSummary(total, perBin, bins.length)
      if (left > 0) {
        await save.mutateAsync({ publicId, action: 'add', body: { productPublicId, receivedQty: left, expectedQty: hadExpected ? left : null } })
        done++
      }
      toast.success(t('warehouse.split.receiptSaved', { count: done }))
      onClose()
    } catch (err) {
      setError(t('warehouse.split.receiptFailed', { done, message: problemText(err) }))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('warehouse.split.receiptTitle', { product: row.sku || row.productName || '' })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={busy || !ready}>
            {busy ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <p className="note">{t('warehouse.split.receiptIntro', { qty: formatNumber(total, lang) })}</p>
      <SplitBinsEditor
        total={total}
        perBinText={perBinText}
        onPerBinChange={setPerBinText}
        bins={bins}
        onBinsChange={setBins}
        warehousePublicId={receipt.header?.warehousePublicId}
        excludeZoneTypeCodes={TARGET_EXCLUDED_ZONE_TYPES}
      />
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}
