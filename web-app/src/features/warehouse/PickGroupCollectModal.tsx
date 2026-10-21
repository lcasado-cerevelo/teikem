// Lote 177 — «Recolectar juntos»: se eligen varios documentos (avisos de salida y/o órdenes de venta) con algo reservado por sacar, DEL MISMO cliente, y se recolecta todo junto
// (cada documento hace su despacho de siempre; la maestra los reúne y se empaca con UN envío). Se arma aquí, al recolectar: no se suman documentos después. Al elegir el primero, los de
// otro cliente se bloquean (una maestra es de un solo cliente y un solo destino). Manual 06 «Recolección maestra».
import { useId, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, Modal, Spinner, toast } from '../../kernel/ui'
import { useCollectPickGroup, usePickDocuments, type PickDocumentDto } from './api'
import { docKey, groupRequest, isGroupable, MIN_GROUP_DOCUMENTS, selectionBlock } from './pickGroupView'

export function PickGroupCollectModal({ onClose, onCollected }: { onClose: () => void; onCollected: (groupPublicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const collect = useCollectPickGroup()
  const noteId = useId()
  const [chosen, setChosen] = useState<PickDocumentDto[]>([])
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const { data, isLoading } = usePickDocuments({ pendingOnly: true, take: 200 })
  const docs = useMemo(() => (data?.items ?? []).filter(isGroupable), [data])
  const enough = chosen.length >= MIN_GROUP_DOCUMENTS

  function toggle(d: PickDocumentDto) {
    setError(null)
    setChosen((cur) => (cur.some((c) => docKey(c) === docKey(d)) ? cur.filter((c) => docKey(c) !== docKey(d)) : [...cur, d]))
  }

  async function submit() {
    setError(null)
    try {
      const g = await collect.mutateAsync(groupRequest(chosen, note))
      toast.success(t('warehouse.pickBatches.groups.collectedToast', { number: g.number ?? '' }))
      onCollected(g.publicId ?? '')
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.pickBatches.groups.collectTitle')}
      onClose={onClose}
      dismissible={!collect.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={collect.isPending}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={!enough || collect.isPending}>
            {collect.isPending ? t('common.loading') : t('warehouse.pickBatches.groups.collectSubmit', { n: chosen.length })}
          </button>
        </>
      }
    >
      <p className="help">{t('warehouse.pickBatches.groups.collectHelp')}</p>
      {isLoading ? (
        <Spinner block />
      ) : docs.length === 0 ? (
        <p className="help">{t('warehouse.pickBatches.groups.collectEmpty')}</p>
      ) : (
        <ul className="pgroup-pick">
          {docs.map((d) => {
            const isOn = chosen.some((c) => docKey(c) === docKey(d))
            const block = selectionBlock(d, chosen)
            return (
              <li key={docKey(d)}>
                <label className={block ? 'dim' : undefined} title={block ? t(`warehouse.pickBatches.groups.block.${block}`) : undefined}>
                  <input type="checkbox" checked={isOn} disabled={block !== null} onChange={() => toggle(d)} />
                  <span className="ref">{d.number}</span> <Chip tone="cap">{t(`warehouse.pickBatches.docs.kinds.${d.kind}`)}</Chip>
                  <span className="pgroup-client">{d.clientName ?? '—'}</span>
                  <span className="help">{t('warehouse.pickBatches.docs.toCollect', { n: f.number(d.reserved ?? 0) })}</span>
                </label>
              </li>
            )
          })}
        </ul>
      )}
      <div className="f pgroup-note">
        <label htmlFor={noteId}>{t('warehouse.pickBatches.groups.note')}</label>
        <input id={noteId} type="text" value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
      </div>
      {!enough && chosen.length > 0 && <p className="help">{t('warehouse.pickBatches.groups.needTwo')}</p>}
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}
