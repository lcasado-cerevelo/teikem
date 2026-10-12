// Lote F-D2 — Catálogo → Flota y mantenimiento (maqueta `flota()`; manual 04 §1–§4): cabecera con «Nuevo vehículo», nota de la maqueta, las
// tarjetas resumen («Vehículos» activos y «Documentos por vencer» a 30 días) y las pestañas Vehículos, Documentos por vencer, Zonas de
// despacho y Disponibilidad. Pestaña en `?tab=` (la primera = sin parámetro); vehículo elegido en `?vehicle=`. El mantenimiento
// preventivo, las órdenes de trabajo y el combustible (F-D3) se agregarán como pestañas y tarjetas de esta misma pantalla.
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { IconDoc, IconPlus, IconTruck, Tabs } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import { useExpiringDocuments, useVehicleList } from './api'
import { AvailabilityTab } from './AvailabilityTab'
import { DispatchZonesTab } from './DispatchZonesTab'
import { ExpiringDocumentsTab } from './ExpiringDocumentsTab'
import { DEFAULT_WITHIN_DAYS, tabOf, type FleetTab } from './fleetRules'
import { VehicleCreateModal } from './VehicleCreateModal'
import { VehiclesTab } from './VehiclesTab'
import './fleet.css'

export default function FleetScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [creating, setCreating] = useState(false)
  // cada alta vuelve a montar la lista con los filtros limpios, para que el vehículo nuevo se vea
  const [created, setCreated] = useState(0)
  const tab = tabOf(params.get('tab'))

  // tarjetas: solo lo de este lote (F-D3 suma las de mantenimiento y órdenes de trabajo)
  const vehicles = useVehicleList({ includeInactive: false })
  const due = useExpiringDocuments({ withinDays: DEFAULT_WITHIN_DAYS, includeExpired: true })

  const goTab = (key: FleetTab) => {
    const next = new URLSearchParams(params)
    if (key === 'vehicles') next.delete('tab')
    else next.set('tab', key)
    setParams(next, { replace: true })
  }

  const tabs = [
    { key: 'vehicles' as const, label: t('fleet.tabs.vehicles') },
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
      </div>

      <div className="flt-tabs">
        <Tabs tabs={tabs} value={tab} onChange={goTab} label={t('fleet.tabs.label')} />
      </div>

      {tab === 'vehicles' && <VehiclesTab key={created} />}
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
