// Contabilidad → Liquidación a choferes (Lote 96, módulo LTL_GROUND, permiso driverpay.view): lo que se le paga a cada chofer por sus entregas (con intentos) y viajes. Pestañas: las LIQUIDACIONES
// (lista, «Nueva liquidación» con vista previa y la ficha con el flujo generar → revisar → aprobar → exportar) y los INTENTOS de entrega (la fuente de verdad del pago).
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useT } from '../../kernel/i18n'
import { Tabs } from '../../kernel/ui'
import { AttemptsTab } from './AttemptParts'
import { NewSettlementModal, SettlementModal, SettlementsTab } from './SettlementParts'
import '../tax/tax.css'
import './driverpay.css'

type TabKey = 'settlements' | 'attempts'

export default function DriverPayScreen() {
  const t = useT()
  const [params] = useSearchParams()
  const [tab, setTab] = useState<TabKey>(() => (params.get('tab') === 'attempts' ? 'attempts' : 'settlements'))
  const [openId, setOpenId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  return (
    <div className="pay-screen">
      <div className="head">
        <div>
          <h1>{t('pay.screenTitle')}</h1>
          <p>{t('pay.subtitle')}</p>
        </div>
      </div>
      <Tabs<TabKey>
        label={t('pay.tabs.label')}
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'settlements', label: t('pay.tabs.settlements') },
          { key: 'attempts', label: t('pay.tabs.attempts') },
        ]}
      />
      {tab === 'settlements' ? <SettlementsTab onOpen={setOpenId} onNew={() => setCreating(true)} /> : <AttemptsTab />}
      {creating && (
        <NewSettlementModal
          onClose={() => setCreating(false)}
          onCreated={(id) => {
            setCreating(false)
            setTab('settlements')
            setOpenId(id)
          }}
        />
      )}
      {openId !== null && <SettlementModal key={openId} id={openId} onClose={() => setOpenId(null)} />}
    </div>
  )
}
