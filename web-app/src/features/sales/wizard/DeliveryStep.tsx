// Lote 223 (P3) — paso 3 del pedido guiado: ENTREGA o RECOGIDO. Con entrega, a dónde (por omisión la dirección física del cliente; con permiso de consignatarios
// se puede escoger otra o crear una). Resumen calculado por el servidor al guardar el borrador: subtotal, IVU, cargo de envío con su porqué y total. «En
// consignación» solo con esa forma de pago. Fecha requerida opcional.
import { useCan } from '../../../kernel/access'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { DateField, Spinner } from '../../../kernel/ui'
import type { components } from '../../../kernel/api/schema'
import { ConsigneePicker } from '../../consignees/ConsigneePicker'
import type { SalesOrderDto } from '../salesApi'
import { StockInfo } from '../StockInfo'
import { lineStock, orderKitPieces, type StockInput } from '../stockRules'
import { DeliveryPriceNotices } from './PriceNotices'
import { addressLine, orderSummary, type DeliveryChoice, type WizardState } from './wizardRules'

type ClientDetailDto = components['schemas']['ClientDetailDto']

export interface DeliveryStepProps {
  state: WizardState
  onChange: (next: (s: WizardState) => WizardState) => void
  /** Cambió la entrega o el consignatario: hay que volver a calcular el envío (guarda el borrador). */
  onRecalc: (patch: Partial<WizardState>) => void
  client: ClientDetailDto | undefined
  saved: SalesOrderDto | null
  recalculating: boolean
  /** Lote 235: lo disponible y el almacén de la orden, para avisar en el resumen qué líneas quedan en backorder. */
  stock?: StockInput | null
  warehouseId?: string | null
}

export function DeliveryStep({ state, onChange, onRecalc, client, saved, recalculating, stock, warehouseId }: DeliveryStepProps) {
  const t = useT()
  const f = useFormat()
  const canPickOther = useCan('locations.read')
  const physical = client?.physicalAddress?.isActive === false ? null : client?.physicalAddress ?? null
  const usingPhysical = !!physical?.publicId && state.consigneePublicId === physical.publicId
  const summary = orderSummary(saved)
  // Lote 236: un kit de oferta se cuenta con sus piezas reales (las de la orden si se cambiaron)
  const piecesOf = (l: (typeof state.cart)[number]) => orderKitPieces(l.components, saved?.lines?.find((x) => x.productPublicId === l.productPublicId))
  const short = stock && warehouseId ? state.cart.filter((l) => (lineStock(stock, l.productPublicId, warehouseId, l.quantity, piecesOf(l))?.backorder ?? 0) > 0) : []
  const mode = (m: DeliveryChoice) => onRecalc({ deliveryMode: m, consigneePublicId: m === 'DELIVERY' ? state.consigneePublicId ?? physical?.publicId ?? null : state.consigneePublicId })

  return (
    <section className="wz-step" aria-labelledby="wz-step-title">
      <h2 id="wz-step-title">{t('salesWizard.delivery.title')}</h2>
      <fieldset className="wz-fieldset wz-choice">
        <legend>{t('salesWizard.delivery.how')}</legend>
        {(['DELIVERY', 'PICKUP'] as const).map((m) => (
          <label key={m} className={`wz-radio${state.deliveryMode === m ? ' on' : ''}`}>
            <input type="radio" name="wz-mode" value={m} checked={state.deliveryMode === m} onChange={() => mode(m)} />
            <span>
              <b>{t(`salesWizard.delivery.${m}`)}</b>
              <span className="help">{t(`salesWizard.delivery.${m}Help`)}</span>
            </span>
          </label>
        ))}
      </fieldset>

      {state.deliveryMode === 'DELIVERY' && (
        <fieldset className="wz-fieldset wz-choice">
          <legend>{t('salesWizard.delivery.where')}</legend>
          {physical?.publicId ? (
            <label className={`wz-radio${usingPhysical ? ' on' : ''}`}>
              <input type="radio" name="wz-where" checked={usingPhysical} onChange={() => onRecalc({ consigneePublicId: physical.publicId ?? null })} />
              <span>
                <b>{t('salesWizard.delivery.physical')}</b>
                <span className="help">{addressLine(physical)}</span>
              </span>
            </label>
          ) : (
            <p className="help">{t('salesWizard.delivery.noPhysical')}</p>
          )}
          {canPickOther && (
            <div className="f">
              <label htmlFor="wz-cons">{t('salesWizard.delivery.other')}</label>
              <ConsigneePicker
                id="wz-cons"
                clientPublicId={state.clientPublicId}
                value={usingPhysical ? '' : state.consigneePublicId ?? ''}
                onChange={(id) => onRecalc({ consigneePublicId: id || physical?.publicId || null })}
              />
              <p className="help">{t('salesWizard.delivery.otherHelp')}</p>
            </div>
          )}
        </fieldset>
      )}

      {state.paymentMethod === 'CONSIGNMENT' && (
        <label className="sw">
          <input type="checkbox" role="switch" checked={state.isConsignment} onChange={(e) => onChange((s) => ({ ...s, isConsignment: e.target.checked }))} />
          <span className="tk" aria-hidden="true" />
          <span>{t('salesWizard.delivery.consignment')}</span>
        </label>
      )}

      <div className="f wz-date">
        <label htmlFor="wz-req">{t('salesWizard.delivery.requiredDate')}</label>
        <DateField id="wz-req" value={state.requiredDate} onChange={(v) => onChange((s) => ({ ...s, requiredDate: v }))} />
        <p className="help">{t('salesWizard.delivery.requiredDateHelp')}</p>
      </div>

      <section className="wz-summary" aria-labelledby="wz-summary-title" aria-busy={recalculating || undefined}>
        <h3 id="wz-summary-title">
          {t('salesWizard.summary.title')} {recalculating && <Spinner label={t('salesWizard.summary.calculating')} />}
        </h3>
        <dl>
          <div><dt>{t('salesWizard.summary.subtotal')}</dt><dd className="mono">{f.money(summary.subtotal)}</dd></div>
          <div><dt>{t('salesWizard.summary.tax')}</dt><dd className="mono">{f.money(summary.tax)}</dd></div>
          <div>
            <dt>{t('salesWizard.summary.shipping')}</dt>
            <dd>
              <span className="mono">{summary.shipping > 0 ? f.money(summary.shipping) : t('salesWizard.summary.noShipping')}</span>
              {summary.reasonLabel && <span className="help wz-reason">{t('salesWizard.summary.why', { reason: summary.reasonLabel })}</span>}
            </dd>
          </div>
          <div className="wz-total"><dt>{t('salesWizard.summary.total')}</dt><dd className="mono">{f.money(summary.total)}</dd></div>
        </dl>
        <p className="help">{t('salesWizard.summary.help')}</p>
        {/* Lote 235: lo que pasa de lo disponible queda marcado (no bloquea): el pedido se puede enviar y eso queda en backorder */}
        {short.length > 0 && (
          <>
            <ul className="sales-backorders" aria-label={t('sales.stock.backorderTitle')}>
              {short.map((l) => (
                <li key={l.productPublicId}>
                  <b><span className="mono">{l.sku}</span> · {l.name}</b>
                  <StockInfo stock={stock} warehousePublicId={warehouseId} productPublicId={l.productPublicId} quantity={l.quantity} pieces={piecesOf(l)} onlyBackorder />
                </li>
              ))}
            </ul>
            <p className="help">{t('sales.stock.backorderHelp')}</p>
          </>
        )}
      </section>
      {/* Lote 229: avisos de precio (dos ofertas a la vez, oferta más cara que la lista general) de lo guardado (`linePrices`) y del catálogo */}
      <DeliveryPriceNotices cart={state.cart} clientPublicId={state.clientPublicId} saved={saved} />
    </section>
  )
}
