// Contabilidad → COD (Lote 85, módulo COD, permiso cod.view): el cobro contra entrega, su reconciliación y la remesa al cliente. Pestañas: las ÓRDENES con COD (lista y ficha con los cobros; «Registrar cobro» con
// cod.collect), RECONCILIAR (cod.reconcile: se suben las hojas escaneadas —el código de barras es el número de la factura— o se escriben los códigos; vista previa y cuadrar) y las CORRIDAS ya hechas.
import { useState } from 'react'
import { useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Tabs } from '../../kernel/ui'
import { CodOrderModal, CodOrdersTab } from './CodParts'
import { ReconcileTab, RunsTab } from './ReconcileParts'
import { NewRemittanceModal, RemittanceModal, RemittancesTab } from './RemittanceParts'
import '../tax/tax.css'
import './cod.css'

type TabKey = 'orders' | 'reconcile' | 'remittances' | 'runs'

export default function CodScreen() {
  const t = useT()
  const canReconcile = useCan('cod.reconcile')
  const [tab, setTab] = useState<TabKey>('orders')
  const [openId, setOpenId] = useState<string | null>(null)
  const [remittanceId, setRemittanceId] = useState<string | null>(null)
  const [newRemittance, setNewRemittance] = useState(false)
  const tabs: { key: TabKey; label: string }[] = [
    { key: 'orders', label: t('cod.tabs.orders') },
    ...(canReconcile ? [{ key: 'reconcile' as const, label: t('cod.tabs.reconcile') }] : []),
    { key: 'remittances', label: t('cod.tabs.remittances') },
    { key: 'runs', label: t('cod.tabs.runs') },
  ]
  return (
    <div className="cod-screen">
      <div className="head">
        <div>
          <h1>{t('cod.title')}</h1>
          <p>{t('cod.subtitle')}</p>
        </div>
      </div>
      <Tabs<TabKey> label={t('cod.tabs.label')} value={tab} onChange={setTab} tabs={tabs} />
      {tab === 'orders' ? (
        <CodOrdersTab onOpen={setOpenId} />
      ) : tab === 'reconcile' && canReconcile ? (
        <ReconcileTab />
      ) : tab === 'remittances' ? (
        <RemittancesTab onOpen={setRemittanceId} onNew={() => setNewRemittance(true)} />
      ) : (
        <RunsTab />
      )}
      {openId !== null && <CodOrderModal key={openId} id={openId} onClose={() => setOpenId(null)} />}
      {newRemittance && (
        <NewRemittanceModal
          onClose={() => setNewRemittance(false)}
          onCreated={(id) => {
            setNewRemittance(false)
            setTab('remittances')
            setRemittanceId(id)
          }}
        />
      )}
      {remittanceId !== null && <RemittanceModal key={remittanceId} id={remittanceId} onClose={() => setRemittanceId(null)} />}
    </div>
  )
}
