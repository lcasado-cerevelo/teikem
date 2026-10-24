// Lote 216 — la lupa de la estación (Luis): «buscar por el número de paquete lo que sea que se encuentre en la base de datos». Si escaneaste y no se encontró, aquí se ve por qué: cancelado, estatus final, dado de baja,
// reclamación, perdida o «Por verificar». Solo lee; no cambia nada ni cuenta el escaneo.
import { useState, type FormEvent } from 'react'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, Modal } from '../../kernel/ui'
import { IconSearch } from '../../kernel/ui/icons'
import { useScanLookup, type ScanLookupItem } from './scanApi'
import { flagTone } from './scanRules'

function Card({ item }: { item: ScanLookupItem }) {
  const t = useT()
  const f = useFormat()
  const rows: [string, string | null | undefined][] = [
    [t('scan.lookup.order'), item.orderNumber],
    [t('scan.lookup.waybill'), item.waybillNumber],
    [t('scan.lookup.status'), `${item.status}${item.orderActive ? '' : ` · ${t('scan.lookup.flag.ORDER_INACTIVE')}`}`],
    [t('scan.lookup.client'), item.clientName],
    [t('scan.lookup.consignee'), [item.consigneeName, item.city].filter(Boolean).join(' · ')],
    [t('scan.lookup.route'), item.tripCode ? [item.tripCode, item.tripStatus, item.driverName].filter(Boolean).join(' · ') : null],
    [t('scan.lookup.claim'), item.claimNumber ? [item.claimNumber, item.claimStatus].filter(Boolean).join(' · ') : null],
    [t('scan.lookup.pieces'), String(item.pieces)],
  ]
  return (
    <div className="scn-lk-card" data-testid="lookup-card">
      <div className="scn-lk-top">
        <span className="ref">{item.pieceNumber ?? item.orderNumber}</span>
        {item.pieceNumber == null && <Chip tone="disp">{t('scan.lookup.isOrder')}</Chip>}
      </div>
      {item.flags.length > 0 && (
        <div className="scn-lk-flags">
          {item.flags.map((fl) => <Chip key={fl} tone={flagTone(fl)}>{t(`scan.lookup.flag.${fl}`)}</Chip>)}
        </div>
      )}
      <dl className="scn-lk-grid">
        {rows.filter(([, v]) => v).map(([k, v]) => (
          <div key={k} style={{ display: 'contents' }}><dt>{k}</dt><dd>{v}</dd></div>
        ))}
      </dl>
      {item.scans.length > 0 && (
        <ul className="scn-lk-scans" aria-label={t('scan.lookup.scans')}>
          {item.scans.map((s, i) => <li key={i}>{`${t(`scan.action.${s.action}`)} · ${f.dateTime(s.scannedAtUtc)}${s.byName ? ` · ${s.byName}` : ''}`}</li>)}
        </ul>
      )}
    </div>
  )
}

export function ScanLookupModal({ initial, onClose }: { initial?: string; onClose: () => void }) {
  const t = useT()
  const [text, setText] = useState(initial ?? '')
  const [code, setCode] = useState<string | null>(initial?.trim() || null)
  const q = useScanLookup(code)
  const submit = (e: FormEvent) => {
    e.preventDefault()
    setCode(text.trim() || null)
  }
  return (
    <Modal open title={t('scan.lookup.title')} onClose={onClose}>
      <div className="pb">
        <p className="scn-note">{t('scan.lookup.help')}</p>
        <form className="scn-lookup-form" onSubmit={submit}>
          <input type="text" value={text} onChange={(e) => setText(e.target.value)} placeholder={t('scan.lookup.placeholder')} aria-label={t('scan.lookup.label')} autoComplete="off" spellCheck={false} autoFocus />
          <button type="submit" className="btn flow" disabled={!text.trim()}><IconSearch /> {t('scan.lookup.search')}</button>
        </form>
        {q.isFetching && <p>{t('common.loading')}</p>}
        {q.isError && <p role="alert">{t('scan.lookup.error')}</p>}
        {q.data && !q.isFetching && q.data.items.length === 0 && <p data-testid="lookup-none">{t('scan.lookup.none', { code: q.data.code })}</p>}
        {q.data && !q.isFetching && q.data.items.length > 0 && (
          <>
            {q.data.matchedBy && q.data.matchedBy !== 'PIECE' && <p className="scn-note">{t('scan.lookup.matchedOrder', { by: t(`scan.matchedBy.${q.data.matchedBy}`) })}</p>}
            {q.data.items.map((it, i) => <Card key={`${it.orderPublicId}-${it.pieceNumber ?? i}`} item={it} />)}
          </>
        )}
      </div>
    </Modal>
  )
}
