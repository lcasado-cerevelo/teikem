// Lote 244 — «Venta» en la ficha del cliente: su ruta de venta (propia o heredada del representante), el titular y los apoyos de esa ruta, el vendedor
// preferido (con un aviso legible si ya no es persona activa de la ruta; no bloquea nada), el vendedor propuesto y el representante heredado (solo lectura).
// Con `sales.analysis.manage` se cambian la ruta propia y el preferido (`PUT /sales-analysis/clients/{id}/sales`, con rowVersion).
// Solo se pinta con el módulo Órdenes de venta y `sales.analysis.view|manage` (se protege solo: la ficha lo monta sin envoltorio).
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
import { useClientSales, useRoutePeople, useSalesRoutes, useSaveClientSales } from './salesAnalysisApi'
import { SALES_ANALYSIS_MANAGE, SALES_ANALYSIS_VIEW } from './salesAnalysisRules'
import './clientSales.css'

export function ClientSalesPanel({ client }: { client: ClientDetail }) {
  const { modules } = useAccess()
  const canView = useCan(SALES_ANALYSIS_VIEW)
  if (!modules.has(ModuleKeys.SalesOrders) || !canView) return null
  return <ClientSalesBody client={client} />
}

function ClientSalesBody({ client }: { client: ClientDetail }) {
  const t = useT()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const sales = useClientSales(client.id)
  const routes = useSalesRoutes()
  const save = useSaveClientSales(client.publicId)
  const data = sales.data
  const directRoute = data?.directRouteId != null ? String(data.directRouteId) : ''
  const currentPref = data?.preferred?.userId != null ? String(data.preferred.userId) : ''
  const [routeSel, setRouteSel] = useState(directRoute)
  const [prefSel, setPrefSel] = useState(currentPref)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => setRouteSel(directRoute), [directRoute])
  useEffect(() => setPrefSel(currentPref), [currentPref])
  // las personas de la ruta que quedaría (la propia elegida o, sin ella, la efectiva actual)
  const targetRoute = routeSel ? Number(routeSel) : (data?.routeId ?? null)
  const people = useRoutePeople(canManage ? targetRoute : null)

  if (sales.isPending) return null
  if (sales.isError || !data) return null

  const candidates = [people.data?.primary, ...(people.data?.supports ?? [])].filter((p) => p && p.isCurrent && !p.isVoided && p.userIsActiveMember)
  const seen = new Set<number>()
  const prefOptions = candidates.filter((p) => (p?.userId != null && !seen.has(p.userId) ? (seen.add(p.userId), true) : false))
  const routeOptions = (routes.data ?? []).filter((r) => r.isActive || String(r.id) === directRoute)
  const dirty = routeSel !== directRoute || prefSel !== currentPref
  const noRouteAfter = !routeSel && data.routeId == null

  const submit = async () => {
    setError(null)
    try {
      await save.mutateAsync({
        clientId: data.clientId ?? client.id ?? 0,
        routeId: routeSel ? Number(routeSel) : null,
        clearRoute: !routeSel && directRoute !== '',
        preferredUserId: prefSel ? Number(prefSel) : null,
        clearPreferred: !prefSel && currentPref !== '',
        rowVersion: data.rowVersion,
      })
      toast.success(t('clients.sales.saved'))
    } catch (e) {
      const title = applyProblemDetails(e).title
      setError(title)
      toast.error(title)
    }
  }

  const summary = [data.routeName ?? t('clients.sales.noRoute'), data.preferred?.name].filter(Boolean).join(' · ')
  const routeId = `sa-csales-route-${client.publicId}`
  const prefId = `sa-csales-pref-${client.publicId}`
  const repText = data.repName ?? t('clients.sales.noRep')
  return (
    <Panel icon={<IconRoute />} title={t('clients.sales.sectionTitle')} collapsible defaultCollapsed summary={summary}>
      <dl className="sa-csales-facts">
        <div>
          <dt>{t('clients.sales.route')}</dt>
          <dd className="sa-csales-val">
            {data.routeName ?? <Chip tone="warn">{t('clients.sales.noRoute')}</Chip>}
            {data.routeName && <span className="sa-csales-sub">{data.routeFromRep ? t('clients.sales.routeFromRep', { rep: data.repName ?? '' }) : t('clients.sales.routeOwn')}</span>}
          </dd>
        </div>
        <div>
          <dt>{t('clients.sales.primary')}</dt>
          <dd className="sa-csales-val">{data.primary?.name ?? '—'}</dd>
        </div>
        <div>
          <dt>{t('clients.sales.supports')}</dt>
          <dd className="sa-csales-val">{(data.supports ?? []).length > 0 ? (data.supports ?? []).map((p) => p.name).join(', ') : data.routeId == null ? '—' : t('clients.sales.noPeople')}</dd>
        </div>
        <div>
          <dt>{t('clients.sales.preferred')}</dt>
          <dd className="sa-csales-val">
            {data.preferred?.name ?? t('clients.sales.noPreferred')}
            {data.preferred && data.preferred.isActive === false && <Chip tone="warn">{t('clients.sales.inactive')}</Chip>}
          </dd>
        </div>
        <div>
          <dt>{t('clients.sales.proposed')}</dt>
          <dd className="sa-csales-val">
            {data.proposedName ?? t('clients.sales.proposedNone')}
            {data.proposedName && data.proposedKind && <span className="sa-csales-sub">{t(`clients.sales.kind${data.proposedKind}`)}</span>}
          </dd>
        </div>
        <div>
          <dt>{t('clients.sales.rep')}</dt>
          <dd className="sa-csales-val">{repText}{data.repRouteName ? <span className="sa-csales-sub">{data.repRouteName}</span> : null}</dd>
        </div>
      </dl>
      {data.preferredOutOfRoute && (
        <p className="sa-csales-warn" role="status">
          <b>{t('clients.sales.preferredOutOfRoute')}</b> {t('clients.sales.preferredOutOfRouteHelp')}
        </p>
      )}
      {canManage && (
        <div className="sa-csales-edit">
          <div className="f">
            <label htmlFor={routeId}>{t('clients.sales.route')}</label>
            <select id={routeId} value={routeSel} onChange={(e) => setRouteSel(e.target.value)}>
              <option value="">{t('clients.sales.pickRoute')}</option>
              {routeOptions.map((r) => (
                <option key={r.id} value={String(r.id)}>{r.name}</option>
              ))}
            </select>
            <p className="help">{t('clients.sales.routeHelp')}</p>
          </div>
          <div className="f">
            <label htmlFor={prefId}>{t('clients.sales.preferred')}</label>
            <select id={prefId} value={prefSel} disabled={noRouteAfter} onChange={(e) => setPrefSel(e.target.value)}>
              <option value="">{t('clients.sales.noPreferred')}</option>
              {currentPref && !prefOptions.some((p) => String(p?.userId) === currentPref) && <option value={currentPref}>{data.preferred?.name}</option>}
              {prefOptions.map((p) => (
                <option key={p?.userId} value={String(p?.userId)}>{p?.userName}</option>
              ))}
            </select>
            <p className="help">{noRouteAfter ? t('clients.sales.preferredNeedsRoute') : t('clients.sales.preferredHelp')}</p>
          </div>
          <div className="f sa-csales-act">
            <button type="button" className="btn flow" disabled={!dirty || save.isPending} onClick={() => void submit()}>
              {t('clients.sales.save')}
            </button>
          </div>
          {error && <p className="ferr" role="alert">{error}</p>}
        </div>
      )}
    </Panel>
  )
}
