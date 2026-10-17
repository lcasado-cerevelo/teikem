// Contabilidad → COD (Lote 85, módulo COD, permiso cod.view): el cobro contra entrega y su reconciliación. Tres pestañas: las ÓRDENES con COD (lista y ficha con los cobros; «Registrar cobro» con
// cod.collect), RECONCILIAR (cod.reconcile: se suben las hojas escaneadas —el código de barras es el número de la factura— o se escriben los códigos; vista previa y cuadrar) y las CORRIDAS ya hechas.
import { useState } from 'react'
import { useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Tabs } from '../../kernel/ui'
import { CodOrderModal, CodOrdersTab } from './CodParts'
import { ReconcileTab, RunsTab } from './ReconcileParts'
import '../tax/tax.css'
import './cod.css'

type TabKey = 'orders' | 'reconcile' | 'runs'

export default function CodScreen() {
  const t = useT()
  const canReconcile = useCan('cod.reconcile')
  const [tab, setTab] = useState<TabKey>('orders')
  const [openId, setOpenId] = useState<string | null>(null)
  const tabs: { key: TabKey; label: string }[] = [
    { key: 'orders', label: t('cod.tabs.orders') },
    ...(canReconcile ? [{ key: 'reconcile' as const, label: t('cod.tabs.reconcile') }] : []),
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
      {tab === 'orders' ? <CodOrdersTab onOpen={setOpenId} /> : tab === 'reconcile' && canReconcile ? <ReconcileTab /> : <RunsTab />}
      {openId !== null && <CodOrderModal key={openId} id={openId} onClose={() => setOpenId(null)} />}
    </div>
  )
}
