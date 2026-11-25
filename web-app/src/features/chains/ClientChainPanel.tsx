// Lote 245 — «Cadena» en la ficha del cliente (como «Venta»): muestra su cadena (o «Sin cadena»); con sales.chain la cambia o la quita
// (`PUT /client-chains/by-client/{id}`). Una cadena NO es una ruta: no cambia la ruta del cliente. Solo con el módulo Órdenes de venta
// y sales.chain|clients.read|sales.view (se protege solo: la ficha lo monta sin envoltorio).
import { useEffect, useState } from 'react'
import { Can, useAccess, useCan } from '../../kernel/access'
import { ModuleKeys } from '../../kernel/access/modules'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { Panel } from '../../kernel/ui/Panel'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { toast } from '../../kernel/ui/toast'
import type { ClientDetail } from '../clients/clientRules'
import { chainOrNone } from './chainRules'
import './chains.css'
import { CHAIN_MANAGE, CHAIN_VIEW, useChains, useSetClientChain } from './chainsApi'

export function ClientChainPanel({ client }: { client: ClientDetail }) {
  const { modules } = useAccess()
  const canView = useCan(CHAIN_VIEW)
  if (!modules.has(ModuleKeys.SalesOrders) || !canView) return null
  return <ClientChainBody client={client} />
}

function ClientChainBody({ client }: { client: ClientDetail }) {
  const t = useT()
  const canManage = useCan(CHAIN_MANAGE)
  const current = client.chainPublicId ?? ''
  const chains = useChains(false, '', canManage)
  const save = useSetClientChain(client.publicId ?? '')
  const [sel, setSel] = useState(current)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => setSel(current), [current])
  const options = (chains.data ?? []).filter((c) => c.isActive || c.publicId === current)
  const dirty = sel !== current
  const name = client.chainName

  const submit = async () => {
    setError(null)
    try {
      await save.mutateAsync(sel === '' ? null : sel)
      toast.success(t(sel === '' ? 'clients.chain.removed' : 'clients.chain.saved'))
    } catch (e) {
      const title = applyProblemDetails(e).title
      setError(title)
      toast.error(title)
    }
  }
  const id = `chn-client-${client.publicId}`
  return (
    <Panel icon={<IconLayers />} title={t('clients.chain.title')} collapsible defaultCollapsed summary={chainOrNone(name, t)}>
      <dl className="chn-facts">
        <div>
          <dt>{t('clients.chain.current')}</dt>
          <dd>
            {name ? name : <Chip tone="warn">{t('chains.noChain')}</Chip>}
            {name && client.chainIsActive === false && <Chip tone="warn">{t('chains.inactive')}</Chip>}
          </dd>
        </div>
      </dl>
      <Can perm={CHAIN_MANAGE}>
        <div className="chn-client-edit">
          <div className="f">
            <label htmlFor={id}>{t('clients.chain.pick')}</label>
            <select id={id} value={sel} onChange={(e) => setSel(e.target.value)}>
              <option value="">{t('chains.noChain')}</option>
              {options.map((c) => (
                <option key={c.publicId} value={c.publicId}>
                  {c.name}
                </option>
              ))}
            </select>
            <p className="help">{t('clients.chain.help')}</p>
          </div>
          <button type="button" className="btn flow" disabled={!dirty || save.isPending} onClick={() => void submit()}>
            {save.isPending ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
        {error && (
          <p className="ferr" role="alert">
            {error}
          </p>
        )}
      </Can>
    </Panel>
  )
}
