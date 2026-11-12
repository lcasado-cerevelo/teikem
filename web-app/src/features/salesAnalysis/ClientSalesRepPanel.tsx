// Lote 241 — «Representante de ventas» en la ficha del cliente: su representante y la ruta (de `ClientDetailDto`). Con
// `sales.analysis.manage` se cambia aquí (`PUT /sales-analysis/clients/{id}/rep`). Solo se pinta con el módulo Órdenes de venta y
// `sales.analysis.view|manage` (se protege solo: la ficha lo monta sin envoltorio).
import { useEffect, useState } from 'react'
import { useAccess, useCan } from '../../kernel/access'
import { ModuleKeys } from '../../kernel/access/modules'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { Panel } from '../../kernel/ui/Panel'
import { IconRoute } from '../../kernel/ui/screenIcons'
import { toast } from '../../kernel/ui/toast'
import type { ClientDetail } from '../clients/clientRules'
import { useSalesReps, useSetClientRep } from './salesAnalysisApi'
import { SALES_ANALYSIS_MANAGE, SALES_ANALYSIS_VIEW } from './salesAnalysisRules'

export function ClientSalesRepPanel({ client }: { client: ClientDetail }) {
  const { modules } = useAccess()
  const canView = useCan(SALES_ANALYSIS_VIEW)
  if (!modules.has(ModuleKeys.SalesOrders) || !canView) return null
  return <ClientSalesRepBody client={client} />
}

function ClientSalesRepBody({ client }: { client: ClientDetail }) {
  const t = useT()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const reps = useSalesReps()
  const set = useSetClientRep(client.publicId)
  const current = client.salesRepId ? String(client.salesRepId) : ''
  const [value, setValue] = useState(current)
  useEffect(() => setValue(current), [current])
  const save = async () => {
    try {
      const r = await set.mutateAsync({ clientId: client.id ?? 0, repId: value ? Number(value) : null })
      toast.success(t('salesAnalysis.client.saved', { rep: r.repName ?? t('salesAnalysis.noRep') }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const options = (reps.data ?? []).filter((r) => r.isActive || String(r.id) === current)
  const selectId = `cl-rep-${client.publicId}`
  return (
    <Panel
      icon={<IconRoute />}
      title={t('salesAnalysis.client.title')}
      collapsible
      defaultCollapsed
      summary={client.salesRepName ? `${client.salesRepName}${client.salesRouteName ? ` · ${client.salesRouteName}` : ''}` : t('salesAnalysis.noRep')}
    >
      <dl className="kx-facts">
        <div>
          <dt>{t('salesAnalysis.col.rep')}</dt>
          <dd>{client.salesRepName ?? <Chip tone="warn">{t('salesAnalysis.noRep')}</Chip>}</dd>
        </div>
        <div>
          <dt>{t('salesAnalysis.col.route')}</dt>
          <dd>{client.salesRouteName ?? '—'}</dd>
        </div>
      </dl>
      {canManage && (
        <div className="filters" style={{ marginTop: 10 }}>
          <div className="f">
            <label htmlFor={selectId}>{t('salesAnalysis.client.change')}</label>
            <select id={selectId} value={value} onChange={(e) => setValue(e.target.value)}>
              <option value="">{t('salesAnalysis.noRep')}</option>
              {options.map((r) => (
                <option key={r.id} value={String(r.id)}>{r.name}{r.routeName ? ` · ${r.routeName}` : ''}</option>
              ))}
            </select>
          </div>
          <div className="f" style={{ flex: '0 0 auto', justifyContent: 'flex-end' }}>
            <button type="button" className="btn flow" disabled={value === current || set.isPending} onClick={() => void save()}>
              {t('salesAnalysis.save')}
            </button>
          </div>
        </div>
      )}
    </Panel>
  )
}
