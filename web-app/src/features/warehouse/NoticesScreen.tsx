// «Llegadas y salidas» — `/warehouse/notices` (2026-10-20, Luis): los avisos de llegada y los avisos de salida en su propio ítem del menú (antes eran pestañas de Recibo).
// Pestañas en la URL: Avisos de llegada (`?tab=asns`, la primera) y Avisos de salida (`?tab=exit-notices`; `?notice=` abre uno). «Recibir» de un aviso de llegada lleva a Recibo con el aviso precargado.
// Lectura: inventory.view + WMS_LOTSERIAL (por la ruta); crear y operar tienen sus permisos dentro de cada pestaña.
import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Tabs } from '../../kernel/ui'
import { useAsns, usePickDocuments, usePurchaseOrders, type AsnDto } from './api'
import { AsnsTab } from './AsnsTab'
import { PickGroupCollectModal } from './PickGroupCollectModal'
import { isGroupable } from './pickGroupView'
import { ReceiptGroupCreateModal } from './ReceiptGroupCreateModal'
import { isPoReceivable, isReceivable, MIN_GROUP_DOCUMENTS } from './receiptGroupView'
import { ExitNoticesTab } from './ExitNoticesTab'
import './warehouse.css'

type TabKey = 'asns' | 'exit-notices'

export default function NoticesScreen() {
  const t = useT()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const [creatingGroup, setCreatingGroup] = useState(false)
  const [collecting, setCollecting] = useState(false)
  const tab: TabKey = params.get('tab') === 'exit-notices' ? 'exit-notices' : 'asns'
  const setTab = (key: TabKey) => setParams(key === 'asns' ? {} : { tab: key }, { replace: true })
  // «Juntos» solo se habilita con al menos dos documentos que se puedan juntar EN ESA pestaña: llegadas = avisos esperando + órdenes de compra por recibir; salidas = avisos de salida + órdenes de venta con algo reservado
  const asnQuery = useAsns({ status: ['EXPECTED'] })
  const poQuery = usePurchaseOrders({ status: ['SENT', 'PARTIAL'] })
  const arrivals = useMemo(
    () => (asnQuery.data ?? []).filter(isReceivable).length + (poQuery.data?.items ?? []).filter((p) => isPoReceivable(p.statusCode)).length,
    [asnQuery.data, poQuery.data],
  )
  const pickDocs = usePickDocuments({ pendingOnly: true, take: 200 })
  const departures = useMemo(() => (pickDocs.data?.items ?? []).filter(isGroupable).length, [pickDocs.data])
  const receiveAsn = (asn: AsnDto) => navigate('/warehouse/receipts', { state: { receiveAsn: asn } })

  return (
    <div className="wrap rcp">
      <div className="head">
        <div>
          <h1>{t('warehouse.notices.title')}</h1>
          <p>{t('warehouse.notices.subtitle')}</p>
        </div>
        <div className="act">
          {/* «Juntos» sigue a la pestaña: en llegadas, Recibir juntos (recepción maestra); en salidas, Recolectar juntos (recolección maestra). Deshabilitado hasta que haya dos documentos que juntar. */}
          {tab === 'asns' ? (
            <Can perm="warehouse.receive">
              <button
                type="button"
                className="btn"
                disabled={arrivals < MIN_GROUP_DOCUMENTS}
                title={arrivals < MIN_GROUP_DOCUMENTS ? t('warehouse.notices.joinNeedTwoArrivals') : undefined}
                onClick={() => setCreatingGroup(true)}
              >
                {t('warehouse.receipts.groups.create')}
              </button>
            </Can>
          ) : (
            <Can perm="warehouse.pick">
              <button
                type="button"
                className="btn"
                disabled={departures < MIN_GROUP_DOCUMENTS}
                title={departures < MIN_GROUP_DOCUMENTS ? t('warehouse.notices.joinNeedTwoDepartures') : undefined}
                onClick={() => setCollecting(true)}
              >
                {t('warehouse.pickBatches.groups.collectJoin')}
              </button>
            </Can>
          )}
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
      {collecting && <PickGroupCollectModal onClose={() => setCollecting(false)} onCollected={() => navigate('/warehouse/pick-batches?view=groups')} />}
      {creatingGroup && (
        <ReceiptGroupCreateModal onClose={() => setCreatingGroup(false)} onCreated={(id) => navigate(`/warehouse/receipts?tab=groups&group=${encodeURIComponent(id)}`)} />
      )}
    </div>
  )
}
