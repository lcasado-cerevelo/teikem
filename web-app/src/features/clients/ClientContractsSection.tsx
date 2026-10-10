// Sección «Contratos» de la ficha (F-A2): reemplaza al panel de solo lectura de F-A1. Pestañas Contrato · Tarifas · SLA ·
// Servicios especiales, con el selector de contrato (por omisión el vigente `currentContract`) y «Nuevo contrato»
// (contracts.create). Sin contracts.read la sección solo avisa y muestra el resumen de facturación que ya trae la ficha.
import { useId, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { EmptyState, Panel, Spinner, Tabs } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { useContract } from './contractApi'
import { ContractCreateModal } from './ContractCreateModal'
import { ContractRatesTab } from './ContractRatesTab'
import { ContractSlaTab } from './ContractSlaTab'
import { ContractSpecialTab } from './ContractSpecialTab'
import { ContractTab } from './ContractTab'
import { contractLabel, defaultContractId } from './contractRules'
import type { ClientDetail } from './clientRules'

type TabKey = 'contract' | 'rates' | 'sla' | 'special'

/** Lo que muestra cada pestaña que trabaja sobre UN contrato (Contrato, Tarifas, SLA): carga su ficha y la pasa. */
function ContractBody({ tab, contractId, clientPublicId }: { tab: Exclude<TabKey, 'special'>; contractId: string; clientPublicId: string }) {
  const { data: contract, isPending, error } = useContract(contractId)
  if (isPending) return <Spinner block />
  if (error || !contract) return <EmptyState icon={<IconDoc />} title={applyProblemDetails(error).title} />
  // `key`: al cambiar de contrato los formularios empiezan de cero (no arrastran lo escrito en el anterior)
  if (tab === 'contract') return <ContractTab key={contract.publicId} contract={contract} clientPublicId={clientPublicId} />
  if (tab === 'rates') return <ContractRatesTab key={contract.publicId} contract={contract} />
  return <ContractSlaTab key={contract.publicId} contract={contract} clientPublicId={clientPublicId} />
}

export function ClientContractsSection({ client }: { client: ClientDetail }) {
  const t = useT()
  const pickId = useId()
  const canRead = useCan('contracts.read')
  const contracts = client.contracts ?? []
  const clientPublicId = client.publicId ?? ''
  const [tab, setTab] = useState<TabKey>('contract')
  const [picked, setPicked] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const contractId = picked && contracts.some((c) => c.publicId === picked) ? picked : defaultContractId(client.currentContract?.publicId, contracts)

  if (!canRead) {
    return (
      <Panel icon={<IconDoc />} title={t('clients.contracts.title')} badge={contracts.length}>
        <p className="cl-billing">
          <span className="meta">{t('clients.contracts.billingLabel')}</span> <b>{client.billingSummary || '—'}</b>
        </p>
        <p className="note">{t('clients.contracts.noPermission')}</p>
      </Panel>
    )
  }

  const tabs: { key: TabKey; label: string }[] = [
    { key: 'contract', label: t('clients.contracts.tabs.contract') },
    { key: 'rates', label: t('clients.contracts.tabs.rates') },
    { key: 'sla', label: t('clients.contracts.tabs.sla') },
    { key: 'special', label: t('clients.contracts.tabs.special') },
  ]

  return (
    <Panel
      icon={<IconDoc />}
      title={t('clients.contracts.title')}
      badge={contracts.length}
      actions={
        <Can perm="contracts.create">
          <button type="button" className="btn sm flow" onClick={() => setCreating(true)}>
            {t('clients.contracts.new')}
          </button>
        </Can>
      }
    >
      <p className="cl-billing">
        <span className="meta">{t('clients.contracts.billingLabel')}</span> <b>{client.billingSummary || '—'}</b>
      </p>
      <Tabs tabs={tabs} value={tab} onChange={(k: TabKey) => setTab(k)} label={t('clients.contracts.tabsLabel')} />

      {tab === 'special' ? (
        <ContractSpecialTab client={client} />
      ) : !contractId ? (
        <EmptyState icon={<IconDoc />} title={t('clients.contracts.empty')} />
      ) : (
        <>
          <div className="f cl-ctr-pick">
            <label htmlFor={pickId}>{t('clients.contracts.pick')}</label>
            <select id={pickId} value={contractId} onChange={(e) => setPicked(e.target.value)}>
              {contracts.map((c) => (
                <option key={c.publicId} value={c.publicId}>
                  {contractLabel(c)} — {c.statusLabel ?? c.status}
                  {c.isCurrent ? ` (${t('clients.contracts.current')})` : ''}
                </option>
              ))}
            </select>
          </div>
          <ContractBody tab={tab} contractId={contractId} clientPublicId={clientPublicId} />
        </>
      )}

      <ContractCreateModal
        client={client}
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(c) => {
          setPicked(c.publicId ?? null)
          setTab('contract')
          setCreating(false)
        }}
      />
    </Panel>
  )
}
