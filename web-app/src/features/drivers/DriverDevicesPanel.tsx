// Panel «Dispositivos» (manual 04 §2.6): los registra la app del chofer; aquí solo se listan (plataforma, versión, última conexión) y se
// pueden desactivar (`fleet.manage`). El token de notificaciones NUNCA llega al navegador: el servidor solo dice si existe.
import { useMemo, useRef, useState } from 'react'
import { useCan } from '../../kernel/access'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, IconPower, Panel, toast, useElementWidth, type DataColumn } from '../../kernel/ui'
import { IconLock } from '../../kernel/ui/screenIcons'
import { useDeactivateDevice, type DriverDetail, type DriverDevice } from './api'

export function DriverDevicesPanel({ driver }: { driver: DriverDetail }) {
  const t = useT()
  const f = useFormat()
  const canManage = useCan('fleet.manage') && driver.isTerminal !== true
  const deactivate = useDeactivateDevice(driver.publicId ?? '')
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const [removing, setRemoving] = useState<DriverDevice | null>(null)
  const rows = useMemo(() => driver.devices ?? [], [driver.devices])

  const columns = useMemo<DataColumn<DriverDevice>[]>(
    () => [
      { id: 'platform', header: t('drivers.devices.platform'), cell: (r) => <b>{r.platform || r.platformCode || '—'}</b>, sortValue: (r) => r.platform ?? '', card: 'title' },
      { id: 'version', header: t('drivers.devices.version'), cell: (r) => <span className="mono">{r.appVersion || '—'}</span>, sortValue: (r) => r.appVersion ?? '' },
      { id: 'seen', header: t('drivers.devices.lastSeen'), cell: (r) => (r.lastSeenUtc ? f.dateTime(r.lastSeenUtc) : '—'), sortValue: (r) => r.lastSeenUtc ?? '' },
      {
        id: 'push',
        header: t('drivers.devices.push'),
        cell: (r) => <Chip tone={r.hasPushToken ? 'deliv' : 'cap'}>{r.hasPushToken ? t('drivers.devices.pushOn') : t('drivers.devices.pushOff')}</Chip>,
        sortValue: (r) => (r.hasPushToken ? 1 : 0),
      },
    ],
    [t, f],
  )

  return (
    <Panel icon={<IconLock />} title={t('drivers.devices.title')} badge={rows.length}>
      <div ref={ref}>
        {rows.length === 0 ? (
          <p className="drv-empty">{t('drivers.devices.empty')}</p>
        ) : (
          <DataTable
            label={t('drivers.devices.title')}
            columns={columns}
            rows={rows}
            rowKey={(r) => String(r.id)}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 640}
            rowActions={[{ key: 'deactivate', label: t('drivers.devices.deactivate'), icon: <IconPower />, tone: 'danger', onClick: (r) => setRemoving(r), visible: (r) => canManage && r.isActive }]}
          />
        )}
      </div>
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('drivers.devices.deactivateTitle')}
        message={t('drivers.devices.deactivateBody', { platform: removing?.platform ?? '' })}
        confirmLabel={t('drivers.devices.deactivate')}
        onConfirm={async () => {
          if (!removing) return
          await deactivate.mutateAsync(removing.id)
          toast.success(t('drivers.devices.deactivated'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}
