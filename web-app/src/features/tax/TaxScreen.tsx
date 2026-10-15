// Contabilidad → Impuestos (Lote 56, permiso tax.manage, módulo TAX): tasas con vigencia y componentes, reglas de la compañía, categoría de impuesto de
// los productos, exenciones de clientes y un simulador de cálculo. Todo es configuración: el contador define las cifras, el programa no trae ninguna.
import { useState } from 'react'
import { useT } from '../../kernel/i18n'
import { Tabs } from '../../kernel/ui'
import { ExemptionsTab } from './ExemptionsTab'
import { ProductsTab } from './ProductsTab'
import { RatesTab } from './RatesTab'
import { RulesTab } from './RulesTab'
import { SimulatorTab } from './SimulatorTab'
import './tax.css'

type TabKey = 'rates' | 'rules' | 'products' | 'exemptions' | 'simulator'

export default function TaxScreen() {
  const t = useT()
  const [tab, setTab] = useState<TabKey>('rates')
  return (
    <div className="tax-screen">
      <div className="head">
        <div>
          <h1>{t('tax.title')}</h1>
          <p>{t('tax.subtitle')}</p>
        </div>
      </div>
      <Tabs<TabKey>
        label={t('tax.tabs.label')}
        value={tab}
        onChange={setTab}
        tabs={[
          { key: 'rates', label: t('tax.tabs.rates') },
          { key: 'rules', label: t('tax.tabs.rules') },
          { key: 'products', label: t('tax.tabs.products') },
          { key: 'exemptions', label: t('tax.tabs.exemptions') },
          { key: 'simulator', label: t('tax.tabs.simulator') },
        ]}
      />
      {tab === 'rates' && <RatesTab />}
      {tab === 'rules' && <RulesTab />}
      {tab === 'products' && <ProductsTab />}
      {tab === 'exemptions' && <ExemptionsTab />}
      {tab === 'simulator' && <SimulatorTab />}
    </div>
  )
}
