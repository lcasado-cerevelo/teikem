// «Llegadas y salidas» — `/warehouse/notices` (2026-10-20, Luis): los avisos de llegada y los avisos de salida en su propio ítem del menú (antes eran pestañas de Recibo).
// Pestañas en la URL: Avisos de llegada (`?tab=asns`, la primera) y Avisos de salida (`?tab=exit-notices`; `?notice=` abre uno). «Recibir» de un aviso de llegada lleva a Recibo con el aviso precargado.
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta); crear y operar tienen sus permisos dentro de cada pestaña.
import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Tabs } from '../../kernel/ui'
import type { AsnDto } from './api'
import { AsnsTab } from './AsnsTab'
import { ReceiptGroupCreateModal } from './ReceiptGroupCreateModal'
import { ExitNoticesTab } from './ExitNoticesTab'
import './warehouse.css'

type TabKey = 'asns' | 'exit-notices'

export default function NoticesScreen() {
  const t = useT()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const [creatingGroup, setCreatingGroup] = useState(false)
  const tab: TabKey = params.get('tab') === 'exit-notices' ? 'exit-notices' : 'asns'
  const setTab = (key: TabKey) => setParams(key === 'asns' ? {} : { tab: key }, { replace: true })
  const receiveAsn = (asn: AsnDto) => navigate('/warehouse/receipts', { state: { receiveAsn: asn } })

  return (
    <div className="wrap rcp">
      <div className="head">
        <div>
          <h1>{t('warehouse.notices.title')}</h1>
          <p>{t('warehouse.notices.subtitle')}</p>
        </div>
        <div className="act">
          {/* el mismo «Recibir juntos» de Recibo: abre la misma ventana y, al crear, lleva a la ficha en Recibo → Maestros */}
          <Can perm="warehouse.receive">
            <button type="button" className="btn" onClick={() => setCreatingGroup(true)}>
              {t('warehouse.receipts.groups.create')}
            </button>
          </Can>
        </div>
      </div>
      <div className="rcp-tabs">
        <Tabs<TabKey>
          label={t('warehouse.notices.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'asns', label: t('warehouse.receipts.tabAsns') },
            { key: 'exit-notices', label: t('warehouse.receipts.tabExitNotices') },
          ]}
        />
      </div>
      {tab === 'asns' && <AsnsTab onReceive={receiveAsn} />}
      {tab === 'exit-notices' && <ExitNoticesTab initialOpen={params.get('notice')} />}
      {creatingGroup && (
        <ReceiptGroupCreateModal onClose={() => setCreatingGroup(false)} onCreated={(id) => navigate(`/warehouse/receipts?tab=groups&group=${encodeURIComponent(id)}`)} />
      )}
    </div>
  )
}
