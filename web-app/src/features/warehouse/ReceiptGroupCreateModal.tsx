// Lote 183 (C4) — «Recibir juntos»: se eligen varios avisos de llegada esperando, DEL MISMO dueño y almacén, y se abre un recibo por cada uno bajo una recepción maestra (RCM-#####), con el mismo
// transporte y referencia. Cada documento conserva su recibo de siempre. Se arma aquí, al abrir: no se suman documentos después. Al elegir el primero, los de otro dueño o almacén se bloquean.
import { useId, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, Modal, Spinner, toast } from '../../kernel/ui'
import { useAsns, useCreateReceiptGroup, type AsnDto } from './api'
import { groupRequest, isReceivable, MIN_GROUP_DOCUMENTS, selectionBlock } from './receiptGroupView'

export function ReceiptGroupCreateModal({ onClose, onCreated }: { onClose: () => void; onCreated: (groupPublicId: string) => void }) {
  const t = useT()
  const create = useCreateReceiptGroup()
  const ids = { carrier: useId(), reference: useId(), note: useId() }
  const [chosen, setChosen] = useState<AsnDto[]>([])
  const [form, setForm] = useState({ carrier: '', reference: '', note: '' })
  const [error, setError] = useState<string | null>(null)
  const { data, isLoading } = useAsns({ status: ['EXPECTED'] })
  const asns = useMemo(() => (data ?? []).filter(isReceivable), [data])
  const enough = chosen.length >= MIN_GROUP_DOCUMENTS

  function toggle(a: AsnDto) {
    setError(null)
    setChosen((cur) => (cur.some((c) => c.id === a.id) ? cur.filter((c) => c.id !== a.id) : [...cur, a]))
  }

  async function submit() {
    setError(null)
    try {
      const g = await create.mutateAsync(groupRequest(chosen, form))
      toast.success(t('warehouse.receipts.groups.createdToast', { number: g.number ?? '' }))
      onCreated(g.publicId ?? '')
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.receipts.groups.createTitle')}
      onClose={onClose}
      dismissible={!create.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={create.isPending}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={!enough || create.isPending}>
            {create.isPending ? t('common.loading') : t('warehouse.receipts.groups.createSubmit', { n: chosen.length })}
          </button>
        </>
      }
    >
      <p className="help">{t('warehouse.receipts.groups.createHelp')}</p>
      {isLoading ? (
        <Spinner block />
      ) : asns.length === 0 ? (
        <p className="help">{t('warehouse.receipts.groups.createEmpty')}</p>
      ) : (
        <ul className="pgroup-pick">
          {asns.map((a) => {
            const isOn = chosen.some((c) => c.id === a.id)
            const block = selectionBlock(a, chosen)
            return (
              <li key={a.id}>
                <label className={block ? 'dim' : undefined} title={block ? t(`warehouse.receipts.groups.block.${block}`) : undefined}>
                  <input type="checkbox" checked={isOn} disabled={block !== null} onChange={() => toggle(a)} />
                  <span className="ref">{a.number}</span> <Chip tone="cap">{a.purchaseOrderNumber ? `${t('warehouse.receipts.groups.po')} ${a.purchaseOrderNumber}` : t('warehouse.receipts.groups.asn')}</Chip>
                  <span className="pgroup-client">{a.clientName ?? t('warehouse.receipts.groups.own')}</span>
                  <span className="help">{[a.warehouseCode, a.reference].filter(Boolean).join(' · ')}</span>
                </label>
              </li>
            )
          })}
        </ul>
      )}
      {!enough && chosen.length > 0 && <p className="help">{t('warehouse.receipts.groups.needTwo')}</p>}
      <div className="f">
        <label htmlFor={ids.carrier}>{t('warehouse.receipts.groups.carrier')}</label>
        <input id={ids.carrier} value={form.carrier} maxLength={80} autoComplete="off" onChange={(e) => setForm({ ...form, carrier: e.target.value })} />
      </div>
      <div className="f">
        <label htmlFor={ids.reference}>{t('warehouse.receipts.groups.reference')}</label>
        <input id={ids.reference} value={form.reference} maxLength={80} autoComplete="off" onChange={(e) => setForm({ ...form, reference: e.target.value })} />
      </div>
      <div className="f">
        <label htmlFor={ids.note}>{t('warehouse.receipts.groups.note')}</label>
        <input id={ids.note} value={form.note} maxLength={500} autoComplete="off" onChange={(e) => setForm({ ...form, note: e.target.value })} />
      </div>
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}
