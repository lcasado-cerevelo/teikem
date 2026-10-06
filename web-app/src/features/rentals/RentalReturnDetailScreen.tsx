// Lote F18 (Rentas F-R2) — ficha de una devolución de renta. `/warehouse/rental-returns/:publicId` (`rental.view` +
// RENTAL_EQUIPMENT por la ruta; manual 11 §5). Encabezado con el número DRN, el cliente, el motivo y "Anticipada"; enlace a la
// renta de origen (devolución ↔ renta); datos (fecha, motivo, equipos, procesos abiertos, estatus de la renta después de la
// devolución, costo de recogido, envío de recogido vacío, quién y cuándo, notas) y los equipos devueltos con su condición, el
// destino (almacén · posición), si pasan por proceso y el estatus de su proceso (enlace a la cola de proceso con su serie).
import type { ReactNode } from 'react'
import { useMemo } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Panel, Spinner, type DataColumn } from '../../kernel/ui'
import { IconDoc, IconWarehouse } from '../../kernel/ui/screenIcons'
import { useRentalReturn } from './api'
import { RENTAL_STATUS_DOMAIN } from './rentalRules'
import { PROCESS_STATUS_DOMAIN, type RentalReturnDto, type RentalReturnLineDto } from './returnRules'
import '../warehouse/warehouse.css'
import './rentals.css'

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  )
}

export default function RentalReturnDetailScreen() {
  const t = useT()
  const { publicId = '' } = useParams()
  const { data, isLoading, error } = useRentalReturn(publicId)
  if (isLoading) return <Spinner block />
  if (error || !data?.return) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('rentalReturns.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/rental-returns">
            {t('rentalReturns.back')}
          </Link>
        }
      />
    )
  }
  return <ReturnDetail dto={data} />
}

function ReturnDetail({ dto }: { dto: RentalReturnDto }) {
  const t = useT()
  const f = useFormat()
  const x = dto.return ?? {}
  const lines = dto.lines ?? []

  const columns = useMemo<DataColumn<RentalReturnLineDto>[]>(
    () => [
      { id: 'serial', header: t('rentals.lines.serial'), cell: (l) => <span className="mono">{l.serialNumber}</span>, sortValue: (l) => l.serialNumber, card: 'title' },
      { id: 'sku', header: t('rentals.lines.sku'), cell: (l) => <span className="ref">{l.sku}</span>, sortValue: (l) => l.sku },
      { id: 'product', header: t('rentals.lines.product'), cell: (l) => l.productName, sortValue: (l) => l.productName },
      {
        id: 'condition',
        header: t('rentalReturns.fields.condition'),
        cell: (l) => <Chip tone={l.conditionCode === 'GOOD' ? 'deliv' : 'warn'}>{l.condition ?? l.conditionCode}</Chip>,
        sortValue: (l) => l.condition ?? l.conditionCode,
        exportValue: (l) => l.condition ?? l.conditionCode ?? '',
      },
      {
        id: 'destination',
        header: t('rentalReturns.fields.destination'),
        cell: (l) => [l.toWarehouseCode, l.toBinCode].filter(Boolean).join(' · '),
        sortValue: (l) => `${l.toWarehouseCode ?? ''} ${l.toBinCode ?? ''}`,
      },
      {
        id: 'process',
        header: t('rentalReturns.fields.process'),
        cell: (l) =>
          l.requiresProcess && l.processId ? (
            <span className="ren-links">
              <StatusChip domain={PROCESS_STATUS_DOMAIN} code={l.processStatusCode} label={l.processStatus} />
              <Link to={`/warehouse/rental-processes?search=${encodeURIComponent(l.serialNumber ?? '')}&open=all`} onClick={(e) => e.stopPropagation()}>
                {t('rentalReturns.detail.openProcess')}
              </Link>
            </span>
          ) : (
            t('rentalReturns.detail.noProcess')
          ),
        sortValue: (l) => (l.requiresProcess ? (l.processStatus ?? l.processStatusCode ?? '') : ''),
        exportValue: (l) => (l.requiresProcess ? (l.processStatus ?? l.processStatusCode ?? '') : t('rentalReturns.detail.noProcess')),
      },
      { id: 'notes', header: t('rentalReturns.fields.lineNotes'), cell: (l) => l.notes ?? '', sortValue: (l) => l.notes, card: 'hidden' },
    ],
    [t],
  )

  const pickupCost = dto.estimatedPickupCost != null ? f.money(dto.estimatedPickupCost, { currency: dto.transportCurrencyCode ?? undefined }) : '—'

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{x.number}</span> · {x.clientName}
          </h1>
          <p className="ren-headline">
            <Chip tone="neutral">{x.reason ?? x.reasonCode}</Chip>
            {x.isEarly ? <Chip tone="warn">{t('rentalReturns.earlyChip')}</Chip> : <Chip tone="deliv">{t('rentalReturns.onTime')}</Chip>}
            <span>
              {t('rentalReturns.fields.returnedOn')}: {f.date(x.returnedOn)}
            </span>
          </p>
        </div>
        <div className="act ren-act">
          <Link className="btn" to="/warehouse/rental-returns">
            {t('rentalReturns.back')}
          </Link>
          <Link className="btn" to={`/warehouse/rentals/${x.rentalPublicId}`}>
            {t('rentalReturns.detail.rentalLink', { number: x.rentalNumber ?? '' })}
          </Link>
        </div>
      </div>

      <Panel icon={<IconWarehouse />} title={t('rentalReturns.detail.data')}>
        <dl className="kx-facts">
          <Fact label={t('rentalReturns.fields.rental')}>
            <Link className="ref" to={`/warehouse/rentals/${x.rentalPublicId}`}>
              {x.rentalNumber}
            </Link>
          </Fact>
          <Fact label={t('rentalReturns.fields.client')}>{x.clientName}</Fact>
          <Fact label={t('rentalReturns.fields.returnedOn')}>{f.date(x.returnedOn)}</Fact>
          <Fact label={t('rentalReturns.fields.reason')}>{x.reason ?? x.reasonCode}</Fact>
          <Fact label={t('rentalReturns.columns.early')}>{x.isEarly ? t('rentalReturns.yes') : t('rentalReturns.no')}</Fact>
          <Fact label={t('rentalReturns.columns.units')}>{f.number(x.units ?? 0)}</Fact>
          <Fact label={t('rentalReturns.columns.openProcesses')}>{f.number(x.openProcesses ?? 0)}</Fact>
          <Fact label={t('rentalReturns.fields.rentalStatus')}>
            {dto.rentalStatusCode ? <StatusChip domain={RENTAL_STATUS_DOMAIN} code={dto.rentalStatusCode} /> : '—'}
          </Fact>
          <Fact label={t('rentalReturns.fields.pickupCost')}>{pickupCost}</Fact>
          <Fact label={t('rentalReturns.fields.pickupShipment')}>{dto.pickupShipmentId ? `#${dto.pickupShipmentId}` : t('rentals.detail.noShipment')}</Fact>
          <Fact label={t('rentalReturns.fields.createdBy')}>{dto.createdByName || '—'}</Fact>
          <Fact label={t('rentalReturns.columns.createdAt')}>{f.dateTime(x.createdAtUtc)}</Fact>
        </dl>
        {dto.notes && (
          <>
            <h3 className="ren-h3">{t('rentalReturns.fields.notes')}</h3>
            <p className="ren-notes">{dto.notes}</p>
          </>
        )}
      </Panel>

      <Panel flush icon={<IconDoc />} title={t('rentalReturns.detail.lines')} badge={lines.length}>
        <DataTable
          label={t('rentalReturns.detail.lines')}
          columns={columns}
          rows={lines}
          rowKey={(l) => l.id ?? 0}
          exportFileName={`${t('rentalReturns.detail.lines')} ${x.number ?? ''}`}
          empty={<EmptyState title={t('rentalReturns.detail.noLines')} />}
        />
      </Panel>
    </div>
  )
}
