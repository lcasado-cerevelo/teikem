// Panel «Contratos» (SOLO LECTURA en F-A1): los contratos del cliente (`contracts` de la ficha) y su resumen de facturación.
// Contratos, tarifas, COD y servicios especiales se administran en el bloque siguiente (F-A2).
import { useMemo, useRef } from 'react'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, type DataColumn, EmptyState, Panel, useElementWidth } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { contractName, type ClientDetail } from './clientRules'

type Contract = NonNullable<ClientDetail['contracts']>[number]
const CONTRACT_STATUS_DOMAIN = 'ContractStatus'
const NO_CONTRACTS: Contract[] = []

export function ClientContractsPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const contracts = client.contracts ?? NO_CONTRACTS

  const columns = useMemo<DataColumn<Contract>[]>(
    () => [
      {
        id: 'contract',
        header: t('clients.contracts.contract'),
        cell: (c) => (
          <span>
            <b>{contractName(c)}</b> {c.isCurrent && <Chip tone="route">{t('clients.contracts.current')}</Chip>}
          </span>
        ),
        sortValue: (c) => c.contractNumber ?? c.title,
        exportValue: (c) => contractName(c),
        card: 'title',
      },
      {
        id: 'status',
        header: t('clients.contracts.status'),
        cell: (c) => <StatusChip domain={CONTRACT_STATUS_DOMAIN} code={c.status} label={c.statusLabel} />,
        sortValue: (c) => c.statusLabel ?? c.status,
      },
      {
        id: 'validity',
        header: t('clients.contracts.validity'),
        cell: (c) => (c.endDate ? t('clients.contracts.range', { from: f.date(c.startDate), to: f.date(c.endDate) }) : t('clients.contracts.since', { from: f.date(c.startDate) })),
        sortValue: (c) => c.startDate,
      },
    ],
    [t, f],
  )

  return (
    <Panel flush icon={<IconDoc />} title={t('clients.contracts.title')} badge={contracts.length}>
      <div className="pb cl-billing">
        <span className="meta">{t('clients.contracts.billing')}</span> <b>{client.billingSummary || '—'}</b>
      </div>
      <div ref={ref}>
        {contracts.length === 0 ? (
          <EmptyState icon={<IconDoc />} title={t('clients.contracts.empty')} />
        ) : (
          <DataTable
            label={t('clients.contracts.title')}
            columns={columns}
            rows={contracts}
            rowKey={(c) => c.publicId}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 640}
            rowClassName={(c) => (c.isActive ? undefined : 'dim')}
          />
        )}
      </div>
      <p className="note cl-note">{t('clients.contracts.note')}</p>
    </Panel>
  )
}
