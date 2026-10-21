// «Envíos sin zona»: por qué siguen sin zona los envíos activos (2026-10-21, pedido de Luis). Agrupa por código postal y pueblo de la parada de entrega y dice el motivo de cada grupo:
// - NO_MATCH: ninguna zona lo cubre → «Agregar a una zona» (código postal o pueblo) y se asigna la zona a los envíos;
// - AMBIGUOUS: lo cubren varias zonas a la vez → quitar el duplicado de una de ellas (se ve cuáles);
// - NO_DATA: la parada no trae código postal ni pueblo → se corrige en la dirección del consignatario;
// - RESOLVABLE: ya hay una zona que lo cubre → solo falta «Asignar zona a los envíos».
// Lee `fleet.view`; agregar a una zona y asignar, `fleet.manage`.
import { useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Modal, Panel, Spinner, toast, type ChipTone, type DataColumn } from '../../kernel/ui'
import { IconRoute } from '../../kernel/ui/screenIcons'
import { useDispatchZones } from '../drivers/api'
import { useAddZoneMember, useUnresolvedZones, useZoneGroupMutations, type UnresolvedZoneRow } from './api'

/** El pueblo de una dirección «BARRIO, PUEBLO» (lo habitual en lo migrado): el último tramo; sin comas, el texto tal cual. Es lo que se agrega a la zona como municipio. */
export function municipalityOf(city: string | null | undefined): string {
  const parts = (city ?? '').split(',').map((p) => p.trim()).filter(Boolean)
  return parts.length > 1 ? parts[parts.length - 1] : (city ?? '').trim()
}

const REASON_TONE: Record<string, ChipTone> = { NO_MATCH: 'warn', AMBIGUOUS: 'fail', NO_DATA: 'cap', RESOLVABLE: 'deliv' }

function AddToZoneModal({ row, onClose, onDone }: { row: UnresolvedZoneRow; onClose: () => void; onDone: (message: string) => void }) {
  const t = useT()
  const zones = useDispatchZones(false)
  const add = useAddZoneMember()
  const { assignOrders } = useZoneGroupMutations()
  const hasZip = !!row.postalCode
  const town = municipalityOf(row.city)
  const hasCity = town !== ''
  const [zoneId, setZoneId] = useState('')
  const [by, setBy] = useState<'POSTAL_CODE' | 'MUNICIPALITY'>(hasZip ? 'POSTAL_CODE' : 'MUNICIPALITY')
  const [error, setError] = useState<string | null>(null)
  const busy = add.isPending || assignOrders.isPending
  const value = by === 'POSTAL_CODE' ? (row.postalCode ?? '') : town

  const save = async () => {
    if (!zoneId) return setError(t('fleet.unresolved.zoneRequired'))
    setError(null)
    try {
      await add.mutateAsync({ zoneId: Number(zoneId), matchType: by, matchValue: value })
      const r = await assignOrders.mutateAsync(false)
      onDone(t('fleet.unresolved.added', { value, assigned: r.assigned ?? 0, unresolved: r.unresolved ?? 0 }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title ?? '')
    }
  }

  return (
    <Modal
      open
      size="sm"
      title={t('fleet.unresolved.addTitle', { place: [row.postalCode, row.city].filter(Boolean).join(' · ') })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>
            {busy ? t('common.loading') : t('fleet.unresolved.addAndAssign')}
          </button>
        </>
      }
    >
      <p className="help">{t('fleet.unresolved.addHelp', { orders: row.orders ?? 0 })}</p>
      <div className="f">
        <label htmlFor="uz-zone">{t('fleet.unresolved.zone')}</label>
        <select id="uz-zone" value={zoneId} onChange={(e) => setZoneId(e.target.value)} disabled={busy}>
          <option value="">{t('fleet.unresolved.pickZone')}</option>
          {(zones.data ?? []).map((z) => (
            <option key={z.id} value={z.id}>
              {z.code}
              {z.name ? ` · ${z.name}` : ''}
            </option>
          ))}
        </select>
      </div>
      {hasZip && hasCity && (
        <div className="f" role="radiogroup" aria-label={t('fleet.unresolved.by')}>
          <label className="sw">
            <input type="radio" name="uz-by" checked={by === 'POSTAL_CODE'} onChange={() => setBy('POSTAL_CODE')} disabled={busy} />
            <span>{t('fleet.unresolved.byZip', { value: row.postalCode ?? '' })}</span>
          </label>
          <label className="sw">
            <input type="radio" name="uz-by" checked={by === 'MUNICIPALITY'} onChange={() => setBy('MUNICIPALITY')} disabled={busy} />
            <span>{t('fleet.unresolved.byTown', { value: town })}</span>
          </label>
        </div>
      )}
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}

export function UnresolvedZonesPanel() {
  const t = useT()
  const canManage = useCan('fleet.manage')
  const data = useUnresolvedZones()
  const { assignOrders } = useZoneGroupMutations()
  const [adding, setAdding] = useState<UnresolvedZoneRow | null>(null)
  const [note, setNote] = useState<string | null>(null)
  const rows = useMemo(() => data.data?.rows ?? [], [data.data])
  const resolvable = rows.some((r) => r.reason === 'RESOLVABLE')

  const assign = async () => {
    try {
      const r = await assignOrders.mutateAsync(false)
      setNote(t('fleet.levels.assignResult', { examined: r.examined ?? 0, assigned: r.assigned ?? 0, unresolved: r.unresolved ?? 0 }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<UnresolvedZoneRow>[]>(
    () => [
      { id: 'zip', header: t('fleet.unresolved.col.zip'), cell: (r) => <span className="mono">{r.postalCode ?? '—'}</span>, sortValue: (r) => r.postalCode ?? '', card: 'title' },
      { id: 'city', header: t('fleet.unresolved.col.city'), cell: (r) => r.city ?? '—', sortValue: (r) => r.city ?? '' },
      { id: 'orders', header: t('fleet.unresolved.col.orders'), align: 'end', cell: (r) => <b>{r.orders}</b>, sortValue: (r) => r.orders ?? 0 },
      {
        id: 'reason',
        header: t('fleet.unresolved.col.reason'),
        cell: (r) => (
          <>
            <Chip tone={REASON_TONE[r.reason ?? ''] ?? 'neutral'}>{t(`fleet.unresolved.reason.${r.reason}`)}</Chip>
            {r.reason === 'AMBIGUOUS' && (r.candidates ?? []).length > 0 && <div className="help">{t('fleet.unresolved.candidates', { zones: (r.candidates ?? []).join(', ') })}</div>}
          </>
        ),
        sortValue: (r) => r.reason ?? '',
      },
    ],
    [t],
  )

  if (data.isPending || data.isError || (data.data?.totalOrders ?? 0) === 0) {
    // sin envíos sin zona (o sin permiso para verlos) el panel no estorba
    return data.isPending ? <Spinner block /> : null
  }

  return (
    <Panel
      flush
      icon={<IconRoute />}
      title={t('fleet.unresolved.title')}
      badge={data.data?.totalOrders}
      actions={
        canManage && resolvable ? (
          <button type="button" className="btn sm flow" disabled={assignOrders.isPending} onClick={() => void assign()}>
            {t('fleet.levels.assignOrders')}
          </button>
        ) : undefined
      }
    >
      <div className="pb" style={{ paddingBottom: 0 }}>
        <p className="help" style={{ marginTop: 0 }}>
          {t('fleet.unresolved.help', { orders: data.data?.totalOrders ?? 0, groups: data.data?.groups ?? 0 })}
        </p>
        {note && (
          <p className="help" role="status">
            {note}
          </p>
        )}
      </div>
      <DataTable
        label={t('fleet.unresolved.title')}
        columns={columns}
        rows={rows}
        rowKey={(r) => `${r.postalCode ?? ''}|${r.city ?? ''}`}
        defaultSort={{ id: 'orders', desc: true }}
        empty={t('fleet.unresolved.empty')}
        rowActions={
          canManage
            ? [{ key: 'add', label: t('fleet.unresolved.add'), onClick: (r) => setAdding(r), visible: (r) => r.reason === 'NO_MATCH' }]
            : []
        }
      />
      {adding && <AddToZoneModal row={adding} onClose={() => setAdding(null)} onDone={setNote} />}
    </Panel>
  )
}
