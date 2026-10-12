// Lotes F-D2 y F-D3 — Catálogo → Flota y mantenimiento (maqueta `flota()`; manual 04 §1–§7): cabecera con «Nuevo vehículo», nota de la maqueta,
// las cuatro tarjetas resumen de la maqueta («Vehículos» activos, «Documentos por vencer» a 30 días, «Mantenimiento preventivo» vencidos y
// «Órdenes de trabajo» abiertas; cada una lleva a su pestaña) y las pestañas Vehículos, Mantenimiento, Órdenes de trabajo, Combustible,
// Documentos por vencer, Zonas de despacho y Disponibilidad. Pestaña en `?tab=` (la primera = sin parámetro); vehículo elegido en
// `?vehicle=`; orden de trabajo abierta en `?workOrder=`.
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { IconClip, IconDoc, IconGear, IconPlus, IconTruck, Tabs } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import { useExpiringDocuments, useVehicleList } from './api'
import { AvailabilityTab } from './AvailabilityTab'
import { DispatchZonesTab } from './DispatchZonesTab'
import { ExpiringDocumentsTab } from './ExpiringDocumentsTab'
import { DEFAULT_WITHIN_DAYS, tabOf, type FleetTab } from './fleetRules'
import { FuelTab } from './FuelTab'
import { MaintenanceTab } from './MaintenanceTab'
import { useDue, useWorkOrderList } from './maintenanceApi'
import { DueStates, OPEN_WORK_ORDER_STATUSES } from './maintenanceRules'
import { VehicleCreateModal } from './VehicleCreateModal'
import { VehiclesTab } from './VehiclesTab'
import { WorkOrdersTab } from './WorkOrdersTab'
import './fleet.css'

const OVERDUE_QUERY = { status: [DueStates.Overdue] }
const OPEN_ORDERS_QUERY = { status: [...OPEN_WORK_ORDER_STATUSES] }

export default function FleetScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [creating, setCreating] = useState(false)
  // cada alta vuelve a montar la lista con los filtros limpios, para que el vehículo nuevo se vea
  const [created, setCreated] = useState(0)
  const tab = tabOf(params.get('tab'))

  // tarjetas resumen (cada una lleva a su pestaña)
  const vehicles = useVehicleList({ includeInactive: false })
  const due = useExpiringDocuments({ withinDays: DEFAULT_WITHIN_DAYS, includeExpired: true })
  const overdue = useDue(OVERDUE_QUERY)
  const openOrders = useWorkOrderList(OPEN_ORDERS_QUERY)

  const goTab = (key: FleetTab) => {
    const next = new URLSearchParams(params)
    if (key === 'vehicles') next.delete('tab')
    else next.set('tab', key)
    // la orden abierta es de la pestaña «Órdenes de trabajo»
    if (key !== 'workOrders') next.delete('workOrder')
    setParams(next, { replace: true })
  }

  const tabs = [
    { key: 'vehicles' as const, label: t('fleet.tabs.vehicles') },
    { key: 'maintenance' as const, label: t('fleet.tabs.maintenance') },
    { key: 'workOrders' as const, label: t('fleet.tabs.workOrders') },
    { key: 'fuel' as const, label: t('fleet.tabs.fuel') },
    { key: 'expiring' as const, label: t('fleet.tabs.expiring') },
    { key: 'zones' as const, label: t('fleet.tabs.zones') },
    { key: 'availability' as const, label: t('fleet.tabs.availability') },
  ]

  return (
    <div className="wrap flt">
      <div className="head">
        <div>
          <h1>{t('fleet.title')}</h1>
          <p>{t('fleet.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="fleet.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              <IconPlus /> {t('fleet.vehicle.new')}
            </button>
          </Can>
        </div>
      </div>
      <div className="note flt-note">
        <IconDoc /> {t('fleet.note')}
      </div>

      <div className="flt-river">
        <button type="button" className="flt-node flow" onClick={() => goTab('vehicles')}>
          <span className="ph">
            <IconTruck /> {t('fleet.cards.vehicles')}
          </span>
          <span className="big">{vehicles.data ? vehicles.data.length : '—'}</span>
        </button>
        <button type="button" className="flt-node" onClick={() => goTab('expiring')}>
          <span className="ph">
            <IconAlert /> {t('fleet.cards.docsDue', { days: DEFAULT_WITHIN_DAYS })}
          </span>
          <span className="big">{due.data ? due.data.length : '—'}</span>
        </button>
        <button type="button" className={`flt-node${overdue.data && overdue.data.length > 0 ? ' warn' : ''}`} onClick={() => goTab('maintenance')}>
          <span className="ph">
            <IconGear /> {t('fleet.cards.maintOverdue')}
          </span>
          <span className="big">{overdue.data ? overdue.data.length : '—'}</span>
        </button>
        <button type="button" className="flt-node" onClick={() => goTab('workOrders')}>
          <span className="ph">
            <IconClip /> {t('fleet.cards.openOrders')}
          </span>
          <span className="big">{openOrders.data ? openOrders.data.length : '—'}</span>
        </button>
      </div>

      <div className="flt-tabs">
        <Tabs tabs={tabs} value={tab} onChange={goTab} label={t('fleet.tabs.label')} />
      </div>

      {tab === 'vehicles' && <VehiclesTab key={created} />}
      {tab === 'maintenance' && <MaintenanceTab />}
      {tab === 'workOrders' && <WorkOrdersTab />}
      {tab === 'fuel' && <FuelTab />}
      {tab === 'expiring' && <ExpiringDocumentsTab />}
      {tab === 'zones' && <DispatchZonesTab />}
      {tab === 'availability' && <AvailabilityTab />}

      <VehicleCreateModal
        open={creating}
        onClose={() => setCreating(false)}
        onSaved={(publicId) => {
          setCreated((n) => n + 1)
          const next = new URLSearchParams(params)
          next.delete('tab')
          next.set('vehicle', publicId)
          setParams(next, { replace: true })
        }}
      />
    </div>
  )
}
