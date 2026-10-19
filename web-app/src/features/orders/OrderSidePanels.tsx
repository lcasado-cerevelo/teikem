// Paneles de la columna derecha del expediente de la orden (Lote F-C2; maqueta `record()`): COD (solo lectura), Cotización (solo con
// `orders.view_price`), Chofer asignado, Campos personalizados (TRANSPORT_ORDER) y Contactos (ContactPoint de TRANSPORT_ORDER).
import { useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { CustomFieldsForm, useCustomFieldDefinitions, useSaveCustomFields } from '../../kernel/custom-fields'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { lookupLabelOrCode, useLookups } from '../../kernel/catalogs'
import { Chip, Form, Panel, toast } from '../../kernel/ui'
import { IconCash, IconCheckin, IconTag, IconUsers } from '../../kernel/ui/screenIcons'
import { contactKeys, useContactPoints } from '../contacts/api'
import { ContactPointsPanel } from '../contacts/ContactPointsPanel'
import { useDeliveryProof, useOrderQuote, type OrderDetailDto } from './api'

export const ORDER_ENTITY_TYPE = 'TRANSPORT_ORDER'

const kv = (label: string, value: ReactNode, className?: string) => (
  <div className="od-kv">
    <span className="od-k">{label}</span>
    <b className={className}>{value}</b>
  </div>
)

/** COD: solo lectura. El tipo de cobro (efectivo, cheque…) se registra al entregar, no al capturar la orden. */
export function CodPanel({ order }: { order: OrderDetailDto }) {
  const t = useT()
  const f = useFormat()
  const hasCod = !!order.codAmount && order.codAmount > 0
  return (
    <Panel icon={<IconCash />} title={t('orders.detail.cod.title')}>
      {kv(t('orders.detail.cod.amount'), hasCod ? f.money(order.codAmount ?? 0, { currency: order.currency }) : '—', 'cash')}
      {hasCod && kv(t('orders.detail.cod.type'), order.codType || t('orders.detail.cod.typePending'))}
      {hasCod && kv(t('orders.detail.cod.state'), order.codStatusLabel ? <Chip tone="cod">{order.codStatusLabel}</Chip> : '—')}
      {hasCod && <p className="d od-note">{t('orders.detail.cod.note')}</p>}
      {kv(
        t('orders.detail.cod.invoiceProcessed'),
        order.invoiceProcessedAtUtc
          ? `${f.dateTime(order.invoiceProcessedAtUtc)}${order.invoiceProcessedByName ? ` · ${order.invoiceProcessedByName}` : ''}`
          : t('orders.detail.cod.invoiceNotProcessed'),
      )}
    </Panel>
  )
}

/**
 * Cotización: el monto congelado y, al pulsar «Cotizar», la vista previa del servidor (líneas, despacho, COD, total y crédito) sin guardar
 * nada. Solo existe con `orders.view_price` (lo decide quien la monta); el 403 nunca llega a la pantalla porque ni se pide.
 */
export function QuotePanel({ order }: { order: OrderDetailDto }) {
  const t = useT()
  const f = useFormat()
  const [requested, setRequested] = useState(false)
  const quote = useOrderQuote(order.publicId, requested)
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const { data: packageTypes = [] } = useLookups('PackageType')
  const money = (n: number | null | undefined) => (n == null ? '—' : f.money(n, { currency: order.currency }))
  const q = quote.data
  const credit = q?.credit

  return (
    <Panel
      icon={<IconCash />}
      title={t('orders.detail.quote.title')}
      actions={
        <button
          type="button"
          className="btn sm"
          disabled={quote.isFetching}
          onClick={() => {
            if (requested) void quote.refetch()
            else setRequested(true)
          }}
        >
          {quote.isFetching ? t('common.loading') : t('orders.detail.quote.run')}
        </button>
      }
    >
      {kv(t('orders.detail.quote.quoted'), money(order.quotedAmount), 'cash')}
      <p className="d od-note">{order.quotedAmount != null && order.quotedAtUtc ? t('orders.detail.quote.quotedAt', { date: f.dateTime(order.quotedAtUtc) }) : t('orders.detail.quote.notQuoted')}</p>
      {quote.isError && (
        <p className="ferr" role="alert">
          {applyProblemDetails(quote.error).title}
        </p>
      )}
      {q && (
        <div className="od-quote" aria-live="polite">
          <p className="od-sect">{t('orders.detail.quote.preview')}</p>
          {q.isSpecialDelivery ? (
            kv(q.specialServiceName ?? t('orders.entry.specialTitle'), money(q.total))
          ) : (
            (q.lines ?? []).map((l, i) => (
              <div className="od-kv" key={i}>
                <span className="od-k">{`${lookupLabelOrCode(l.serviceType, serviceTypes) ?? ''} · ${lookupLabelOrCode(l.packageType, packageTypes) ?? ''} ×${l.pieces ?? 0}`}</span>
                <b className="mono">{money(l.lineTotal)}</b>
              </div>
            ))
          )}
          {!q.isSpecialDelivery && kv(t('orders.detail.quote.dispatch'), money(q.dispatchFee))}
          {!q.isSpecialDelivery && kv(t('orders.detail.quote.codFee'), money(q.codFee))}
          <div className="od-kv od-tot">
            <span className="od-k">{t('orders.detail.quote.total')}</span>
            <b className="cash">{money(q.total)}</b>
          </div>
          {credit && (
            <>
              <p className="od-sect">{t('orders.detail.quote.credit')}</p>
              {kv(t('orders.detail.quote.creditLimit'), credit.creditLimit == null ? t('orders.detail.quote.noLimit') : money(credit.creditLimit))}
              {kv(t('orders.detail.quote.pending'), money(credit.pendingBalance))}
              {kv(t('orders.detail.quote.thisOrder'), money(credit.orderTotal))}
              {credit.creditLimit != null && kv(t('orders.detail.quote.available'), money(credit.available))}
              {credit.exceeds ? <Chip tone="fail">{t('orders.detail.quote.exceeds')}</Chip> : credit.creditLimit != null ? <Chip tone="deliv">{t('orders.detail.quote.withinLimit')}</Chip> : null}
            </>
          )}
          <p className="d od-note">{t('orders.detail.quote.note')}</p>
        </div>
      )}
    </Panel>
  )
}

/** Chofer y viaje asignados (solo identidad, nunca montos). Se pinta en una entrega especial o cuando la orden ya tiene un viaje. */
export function DriverPanel({ order }: { order: OrderDetailDto }) {
  const t = useT()
  if (!order.isSpecialDelivery && !order.assignedTripCode && !order.assignedDriverPublicId) return null
  const driver = [order.assignedDriverCode, order.assignedDriverName].filter(Boolean).join(' · ')
  return (
    <Panel icon={<IconUsers />} title={t('orders.detail.driver.title')}>
      {driver || order.assignedTripCode ? (
        <>
          {kv(t('orders.detail.driver.driver'), driver || '—')}
          {order.assignedTripCode && kv(t('orders.detail.driver.trip'), order.assignedTripCode, 'mono')}
        </>
      ) : (
        <p className="note">{t('orders.detail.driver.none')}</p>
      )}
    </Panel>
  )
}

interface CustomValues {
  customFields: Record<string, unknown>
}

/** Campos personalizados de la orden: se guardan con su propio botón (PUT de valores). Sin módulo o sin definiciones no se pinta. */
export function OrderCustomFieldsPanel({ order }: { order: OrderDetailDto }) {
  const t = useT()
  const canEdit = useCan('orders.edit')
  const definitions = useCustomFieldDefinitions(ORDER_ENTITY_TYPE)
  const { save } = useSaveCustomFields(ORDER_ENTITY_TYPE)
  const form = useForm<CustomValues>({ defaultValues: { customFields: {} } })
  if (!definitions.data || definitions.data.length === 0) return null

  return (
    <Panel icon={<IconTag />} title={t('orders.detail.custom.title')}>
      <Form
        form={form}
        onSubmit={async () => {
          const problem = await save(order.id ?? 0, form)
          // los errores por campo quedan bajo cada campo; el título va al aviso
          if (problem) {
            toast.error(problem.title)
            return
          }
          toast.success(t('orders.detail.custom.saved'))
        }}
      >
        <CustomFieldsForm entityType={ORDER_ENTITY_TYPE} entityId={order.id ?? 0} form={form} disabled={!canEdit} />
        <Can perm="orders.edit">
          <div className="form-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
            </button>
          </div>
        </Can>
      </Form>
    </Panel>
  )
}

const WRITE_PERMS = ['contacts.manage', 'orders.edit']

/** Contactos de la orden (quién recibe, teléfono, correo): lista con alta, edición y baja; el rótulo es el `label` del contacto. */
export function OrderContactsPanel({ order }: { order: OrderDetailDto }) {
  const queryClient = useQueryClient()
  const contacts = useContactPoints(ORDER_ENTITY_TYPE, order.id)
  // sin permiso para leer contactos (403) no hay nada que mostrar ni que editar
  if (contacts.isError) return null
  return (
    <ContactPointsPanel
      ownerEntity={ORDER_ENTITY_TYPE}
      ownerId={order.id ?? 0}
      points={contacts.data ?? []}
      perms={WRITE_PERMS}
      idPrefix="order"
      emptyKey="orders.detail.contacts.empty"
      removeBodyKey="orders.detail.contacts.removeBody"
      onChanged={() => queryClient.invalidateQueries({ queryKey: contactKeys.of(ORDER_ENTITY_TYPE, order.id ?? 0) })}
    />
  )
}

/**
 * Comprobante de entrega (Lote 118): solo cuando la orden está Entregada y ya tiene comprobante. Dice quién recibió, cuándo, quién lo registró y que la firma es AUTOMÁTICA del usuario (el consignatario no firmó),
 * y el estado de la ubicación (se busca aparte con Google; si no se logró, queda sin ubicación).
 */
export function DeliveryProofPanel({ order }: { order: OrderDetailDto }) {
  const t = useT()
  const f = useFormat()
  const proof = useDeliveryProof(order.publicId, (order.status ?? '').toUpperCase() === 'DELIVERED')
  const p = proof.data
  if (!p) return null
  const geo =
    p.latitude != null && p.longitude != null
      ? `${p.latitude.toFixed(5)}, ${p.longitude.toFixed(5)}${p.geoStatus === 'FROM_STOP' ? ` · ${t('orders.detail.proof.fromStop')}` : ''}`
      : t(`orders.detail.proof.geo.${p.geoStatus}`)
  return (
    <Panel icon={<IconCheckin />} title={t('orders.detail.proof.title')}>
      <div data-testid="delivery-proof">
        {kv(t('orders.detail.proof.recipient'), p.recipientName)}
        {kv(t('orders.detail.proof.at'), f.dateTime(p.deliveredAtUtc))}
        {p.notes && kv(t('orders.detail.proof.notes'), p.notes)}
        {kv(t('orders.detail.proof.registeredBy'), p.registeredByName ?? '—')}
        {kv(t('orders.detail.proof.location'), geo)}
        <p className="od-note"><b>{t('orders.detail.proof.signature')}:</b> {p.signatureKind === 'USER' ? t('orders.detail.proof.signedByUser', { name: p.signedByName ?? '—' }) : t('orders.detail.proof.signedByRecipient')}</p>
        {p.signatureKind === 'USER' && <p className="d od-note">{t('orders.detail.proof.userSignatureNote')}</p>}
      </div>
    </Panel>
  )
}
