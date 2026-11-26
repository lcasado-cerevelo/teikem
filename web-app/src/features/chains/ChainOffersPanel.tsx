// Lote 247 (Lote C) — «Ofertas» en la ficha de la cadena, en solo lectura: las ofertas activas y no vencidas que le aplican (para todos, incluyéndola, o
// excluyendo a otros sin excluirla a ella), de `GET /api/v1/offers?chainPublicId=`. Con sales.view y el módulo Órdenes de venta (se protege solo).
import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { useAccess, useCan } from '../../kernel/access'
import { ModuleKeys } from '../../kernel/access/modules'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Panel, Spinner, type DataColumn } from '../../kernel/ui'
import { IconPercent } from '../../kernel/ui'
import { useOfferList, type OfferListItemDto } from '../warehouse/offerApi'
import { offerAudienceKey, offerIsOpenEnded } from '../warehouse/offerRules'
import './chains.css'

export function ChainOffersPanel({ chainPublicId }: { chainPublicId: string }) {
  const { modules } = useAccess()
  const canView = useCan('sales.view|sales.pricelist')
  if (!modules.has(ModuleKeys.SalesOrders) || !canView || !chainPublicId) return null
  return <ChainOffersBody chainPublicId={chainPublicId} />
}

function ChainOffersBody({ chainPublicId }: { chainPublicId: string }) {
  const t = useT()
  const f = useFormat()
  const q = useOfferList({ chainPublicId })
  const columns = useMemo<DataColumn<OfferListItemDto>[]>(
    () => [
      { id: 'name', header: t('chains.offers.name'), cell: (o) => <b className="chn-name">{o.name}</b>, sortValue: (o) => o.name ?? '', exportValue: (o) => o.name ?? '', card: 'title' },
      {
        id: 'validity',
        header: t('chains.offers.validity'),
        cell: (o) => (offerIsOpenEnded(o) ? t('chains.offers.fromOpen', { from: f.date(o.validFrom) }) : t('chains.offers.fromTo', { from: f.date(o.validFrom), to: f.date(o.validTo) })),
        sortValue: (o) => o.validFrom ?? '',
        exportValue: (o) => `${o.validFrom ?? ''} ${o.validTo ?? ''}`.trim(),
      },
      {
        id: 'applies',
        header: t('chains.offers.applies'),
        cell: (o) => <Chip tone={offerAudienceKey(o) === 'ALL' ? 'neutral' : 'cap'}>{t(`chains.offers.audience.${offerAudienceKey(o)}`)}</Chip>,
        sortValue: (o) => offerAudienceKey(o),
        exportValue: (o) => t(`chains.offers.audience.${offerAudienceKey(o)}`),
      },
      { id: 'items', header: t('chains.offers.items'), cell: (o) => f.number(o.itemCount ?? 0), sortValue: (o) => o.itemCount ?? 0, align: 'end' },
    ],
    [t, f],
  )
  return (
    <Panel icon={<IconPercent />} title={t('chains.offers.title')} badge={q.data?.length} subtitle={t('chains.offers.subtitle')} flush collapsible storageKey="chain.offers">
      {q.isPending ? (
        <Spinner block />
      ) : q.isError ? (
        <p className="pb ferr" role="alert">
          {applyProblemDetails(q.error).title}
        </p>
      ) : (
        <>
          <DataTable
            label={t('chains.offers.title')}
            columns={columns}
            rows={q.data ?? []}
            rowKey={(o) => o.publicId ?? ''}
            pageSize={10}
            empty={<EmptyState title={t('chains.offers.empty')} body={t('chains.offers.emptyBody')} />}
          />
          <p className="help chn-offers-link">
            <Link to="/money/prices">{t('chains.offers.open')}</Link>
          </p>
        </>
      )}
    </Panel>
  )
}
