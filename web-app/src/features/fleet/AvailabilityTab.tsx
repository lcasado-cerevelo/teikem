// Pestaña «Disponibilidad» (manual 04 §4): para una fecha (hoy por omisión) dice si cada chofer y cada vehículo está disponible para
// despacho y por qué no. Los motivos que BLOQUEAN dejan el recurso «No disponible»; los que solo AVISAN no lo cambian. Solo lectura:
// lo que se arregla se arregla en la ficha del chofer o del vehículo.
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, FilterScope, IconTruck, IconUsers, matchesQ, Panel, QBox, Spinner, type DataColumn } from '../../kernel/ui'
import { useFleetAvailability, type AvailabilityIssue, type DriverAvailability, type VehicleAvailability } from './api'
import { issueText, sortedIssues } from './fleetRules'

const NO_DRIVERS: DriverAvailability[] = []
const NO_VEHICLES: VehicleAvailability[] = []

function Issues({ issues }: { issues: readonly AvailabilityIssue[] | null | undefined }) {
  const t = useT()
  const f = useFormat()
  const list = sortedIssues(issues)
  if (list.length === 0) return <span className="flt-issues-none">{t('fleet.avail.noIssues')}</span>
  return (
    <ul className="flt-issues">
      {list.map((i, n) => (
        <li key={`${i.code}-${n}`}>
          <Chip tone={i.blocking ? 'fail' : 'warn'}>{i.blocking ? t('fleet.avail.blocks') : t('fleet.avail.warns')}</Chip>
          <span>{issueText(i, t, f.date)}</span>
        </li>
      ))}
    </ul>
  )
}

function AvailableChip({ available }: { available: boolean | undefined }) {
  const t = useT()
  return available ? <Chip tone="deliv">{t('fleet.avail.available')}</Chip> : <Chip tone="fail">{t('fleet.avail.unavailable')}</Chip>
}

export function AvailabilityTab() {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const [date, setDate] = useState(() => tenantToday())
  const [onlyAvailable, setOnlyAvailable] = useState(false)
  const [q, setQ] = useState('')
  const { data: vehicleTypes = [] } = useLookups('VehicleType')
  // una fecha vaciada a medias no se pide: rige la última completa
  const [asked, setAsked] = useState(date)
  const list = useFleetAvailability(asked, onlyAvailable)
  // el buscador filtra lo cargado en las dos tablas (código, nombre o placa), sin acentos ni mayúsculas
  const drivers = useMemo(() => (list.data?.drivers ?? NO_DRIVERS).filter((d) => matchesQ(q, d.code, d.fullName, d.zoneCode)), [list.data, q])
  const vehicles = useMemo(() => (list.data?.vehicles ?? NO_VEHICLES).filter((v) => matchesQ(q, v.code, v.plateNumber)), [list.data, q])

  const driverColumns = useMemo<DataColumn<DriverAvailability>[]>(
    () => [
      { id: 'code', header: t('fleet.avail.code'), cell: (d) => <span className="ref">{d.code}</span>, sortValue: (d) => d.code ?? '' },
      { id: 'name', header: t('fleet.avail.driver'), cell: (d) => <b>{d.fullName}</b>, sortValue: (d) => d.fullName ?? '', card: 'title' },
      { id: 'zone', header: t('fleet.avail.zone'), cell: (d) => d.zoneCode || '—', sortValue: (d) => d.zoneCode ?? '' },
      {
        id: 'state',
        header: t('fleet.avail.state'),
        cell: (d) => <AvailableChip available={d.available} />,
        exportValue: (d) => (d.available ? t('fleet.avail.available') : t('fleet.avail.unavailable')),
        sortValue: (d) => (d.available ? 1 : 0),
      },
      {
        id: 'issues',
        header: t('fleet.avail.reasons'),
        cell: (d) => <Issues issues={d.issues} />,
        exportValue: (d) => sortedIssues(d.issues).map((i) => issueText(i, t, f.date)).join(' | '),
        sortValue: (d) => (d.issues ?? []).filter((i) => i.blocking).length * 100 + (d.issues ?? []).length,
      },
    ],
    [t, f],
  )

  const vehicleColumns = useMemo<DataColumn<VehicleAvailability>[]>(() => {
    const typeLabel = (code: string | null | undefined) => vehicleTypes.find((o) => o.code === code)?.label ?? code ?? '—'
    return [
      { id: 'code', header: t('fleet.avail.code'), cell: (v) => <span className="ref">{v.code}</span>, sortValue: (v) => v.code ?? '', card: 'title' },
      { id: 'plate', header: t('fleet.avail.plate'), cell: (v) => <span className="mono">{v.plateNumber || '—'}</span>, sortValue: (v) => v.plateNumber ?? '' },
      { id: 'type', header: t('fleet.avail.type'), cell: (v) => typeLabel(v.vehicleTypeCode), sortValue: (v) => typeLabel(v.vehicleTypeCode) },
      {
        id: 'state',
        header: t('fleet.avail.state'),
        cell: (v) => <AvailableChip available={v.available} />,
        exportValue: (v) => (v.available ? t('fleet.avail.available') : t('fleet.avail.unavailable')),
        sortValue: (v) => (v.available ? 1 : 0),
      },
      {
        id: 'issues',
        header: t('fleet.avail.reasons'),
        cell: (v) => <Issues issues={v.issues} />,
        exportValue: (v) => sortedIssues(v.issues).map((i) => issueText(i, t, f.date)).join(' | '),
        sortValue: (v) => (v.issues ?? []).filter((i) => i.blocking).length * 100 + (v.issues ?? []).length,
      },
    ]
  }, [t, f, vehicleTypes])

  return (
    <FilterScope>
      <div className="flt-stack">
        <Panel icon={<IconUsers />} title={t('fleet.avail.title')} subtitle={t('fleet.avail.help')}>
          <div className="flt-bar">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
            <div className="f flt-days">
              <label htmlFor="flt-av-date">{t('fleet.avail.date')}</label>
              <input
                id="flt-av-date"
                type="date"
                value={date}
                onChange={(e) => {
                  setDate(e.target.value)
                  if (e.target.value) setAsked(e.target.value)
                }}
              />
            </div>
            <label className="sw">
              <input type="checkbox" role="switch" checked={onlyAvailable} onChange={(e) => setOnlyAvailable(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('fleet.avail.onlyAvailable')}</span>
            </label>
          </div>
          <p className="help">
            <Chip tone="fail">{t('fleet.avail.blocks')}</Chip> {t('fleet.avail.blocksHelp')} <Chip tone="warn">{t('fleet.avail.warns')}</Chip> {t('fleet.avail.warnsHelp')}
          </p>
          {list.isError && (
            <p className="ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          )}
        </Panel>

        {list.isPending ? (
          <Spinner block />
        ) : (
          <>
            <Panel flush icon={<IconUsers />} title={t('fleet.avail.drivers')} badge={drivers.length}>
              <div className="pb">
                <DataTable
                  label={t('fleet.avail.drivers')}
                  columns={driverColumns}
                  rows={drivers}
                  rowKey={(d) => String(d.id)}
                  defaultSort={{ id: 'code', desc: false }}
                  onRowClick={(d) => navigate(`/catalog/drivers?driver=${d.publicId}`)}
                  empty={t('fleet.avail.noDrivers')}
                />
              </div>
            </Panel>
            <Panel flush icon={<IconTruck />} title={t('fleet.avail.vehicles')} badge={vehicles.length}>
              <div className="pb">
                <DataTable
                  label={t('fleet.avail.vehicles')}
                  columns={vehicleColumns}
                  rows={vehicles}
                  rowKey={(v) => String(v.id)}
                  defaultSort={{ id: 'code', desc: false }}
                  onRowClick={(v) => navigate(`/catalog/fleet?vehicle=${v.publicId}`)}
                  empty={t('fleet.avail.noVehicles')}
                />
              </div>
            </Panel>
          </>
        )}
      </div>
    </FilterScope>
  )
}
