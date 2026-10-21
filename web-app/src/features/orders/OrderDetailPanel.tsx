// Panel «Detalle» del expediente de la orden (Lote F-C2; maqueta `recordDetailPanelHtml`): cliente y servicio, consignatario con pueblo
// y ZIP, paquetes, COD y comentario. Con `canEditCargo` (y `orders.edit`) los mismos campos se editan AHÍ MISMO con un solo «Guardar
// cambios» (PATCH solo con lo cambiado y el `rowVersion` de la ficha); fuera de ese estatus se muestran como referencia. El cliente,
// el N.º de orden, la factura y el empaque nunca se editan; una entrega especial no edita paquetes ni COD (el servidor responde 400).
import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { useMemo, useState, type ReactNode } from 'react'
import { get, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { StatusChip, useLookups, useStatuses } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DateInput, Field, Form, IconCheck, IconPlus, IconTrash, NumberInput, Panel, Select, TextArea, TextInput, toast } from '../../kernel/ui'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { useSelectedConsignee } from '../consignees/api'
import { ConsigneePickerInput } from '../consignees/ConsigneePicker'
import { orderKeys, useUpdateOrder, type OrderDetailDto } from './api'
import {
  buildOrderPatch,
  duplicateInvoiceOf,
  emptyPackage,
  hasOrderChanges,
  isConcurrencyConflict,
  orderEditSchema,
  orderEditValues,
  packageGroups,
  packageSummaryText,
  pieceNumbersText,
  DESCRIPTION_MAX,
  remapOrderProblem,
  type DuplicateInvoice,
  type OrderEditValues,
} from './orderRules'
import './orders.css'

export type LockReason = 'advanced' | 'permission'

export interface OrderDetailPanelProps {
  order: OrderDetailDto
  /** `capabilities.canEditCargo` con `orders.edit`: los campos se editan. */
  editable: boolean
  /** Por qué no se edita (el texto de ayuda cambia). */
  lockReason?: LockReason
}

/** Campo de solo lectura de la maqueta (`ro`). */
function ReadField({ label, children, mono = false }: { label: string; children: ReactNode; mono?: boolean }) {
  return (
    <div className="f">
      <label>{label}</label>
      <p className={mono ? 'od-ro mono' : 'od-ro'}>{children || '—'}</p>
    </div>
  )
}

type Stop = OrderDetailDto['pickup']

/** Una parada (recogida o entrega): dirección de la orden tal como se guardó, ventana y estatus. Solo lectura. */
function StopCard({ title, stop }: { title: string; stop: Stop | undefined }) {
  const t = useT()
  const f = useFormat()
  if (!stop) return null
  const address = [[stop.line1, stop.line2].filter(Boolean).join(', '), stop.city, [stop.state, stop.postalCode].filter(Boolean).join(' ')].filter(Boolean).join(', ')
  return (
    <div className="od-stop">
      <div className="od-stopt">
        <b>{title}</b>
        {stop.status && <StatusChip domain="StopStatus" code={stop.status} label={stop.statusLabel} />}
      </div>
      <div>{stop.name}</div>
      <div className="od-muted">{address}</div>
      {(stop.windowStartUtc || stop.windowEndUtc) && (
        <div className="od-muted">
          {t('orders.detail.window')}: {f.dateTime(stop.windowStartUtc)} — {f.dateTime(stop.windowEndUtc)}
        </div>
      )}
      {stop.notes && <div className="od-muted">{stop.notes}</div>}
    </div>
  )
}

function StopsBlock({ order }: { order: OrderDetailDto }) {
  const t = useT()
  if (!order.pickup && !order.delivery) return null
  return (
    <>
      <div className="od-sub">
        <span className="od-subt">{t('orders.detail.stops')}</span>
      </div>
      <div className="od-stops">
        <StopCard title={t('orders.detail.pickupStop')} stop={order.pickup} />
        <StopCard title={t('orders.detail.deliveryStop')} stop={order.delivery} />
      </div>
    </>
  )
}

export function OrderDetailPanel({ order, editable, lockReason = 'advanced' }: OrderDetailPanelProps) {
  const t = useT()
  const f = useFormat()
  const queryClient = useQueryClient()
  const update = useUpdateOrder()
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const { data: packageTypes = [] } = useLookups('PackageType')
  const { data: priorities = [] } = useLookups('OrderPriority')
  const { data: statuses = [] } = useStatuses('OrderStatus', { includeDisabled: true })
  const [conflict, setConflict] = useState<string | null>(null)
  const [dup, setDup] = useState<{ info: DuplicateInvoice; values: OrderEditValues } | null>(null)

  const schema = useMemo(
    () =>
      orderEditSchema(
        {
          clientRequired: t('orders.entry.errors.clientRequired'),
          serviceRequired: t('orders.entry.errors.serviceRequired'),
          consigneeRequired: t('orders.entry.errors.consigneeRequired'),
          packagesRequired: t('orders.entry.errors.packagesRequired'),
          packageTypeRequired: t('orders.entry.errors.packageTypeRequired'),
          pieces: t('orders.entry.errors.pieces'),
          codNegative: t('orders.entry.errors.codNegative'),
          specialRequired: t('orders.entry.errors.specialRequired'),
          numberMax: t('orders.entry.errors.numberMax'),
          descriptionMax: t('orders.entry.errors.descriptionMax'),
          dueBeforeReady: t('orders.entry.errors.dueBeforeReady'),
          dateRequired: t('orders.detail.errors.dateRequired'),
        },
        { hadReady: !!order.requestedDate, hadDue: !!order.promisedDate, isSpecial: order.isSpecialDelivery === true },
      ),
    [t, order.requestedDate, order.promisedDate, order.isSpecialDelivery],
  )
  const values = useMemo(() => orderEditValues(order), [order])
  // `values` vuelve a llenar el formulario cuando la ficha se relee; lo que el usuario ya cambió se conserva (p. ej. tras un 409)
  const form = useForm({ resolver: zodResolver(schema), values, resetOptions: { keepDirtyValues: true } })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'packages' })
  const watched = useWatch({ control: form.control }) as OrderEditValues
  const live = useMemo<OrderEditValues>(() => ({ ...values, ...watched, packages: watched?.packages ?? values.packages }), [values, watched])
  const pkgError = get(form.formState.errors, 'packages') as { message?: string; root?: { message?: string } } | undefined

  // opciones de catálogo; el valor guardado se conserva aunque el catálogo ya no lo ofrezca
  const serviceOptions = useMemo(() => {
    const opts = serviceTypes.map((s) => ({ value: s.code, label: s.label }))
    if (order.serviceType && !opts.some((o) => o.value.toLowerCase() === order.serviceType?.toLowerCase())) opts.push({ value: order.serviceType, label: order.serviceTypeLabel || order.serviceType })
    return opts
  }, [serviceTypes, order.serviceType, order.serviceTypeLabel])
  const packageOptions = useMemo(() => {
    const opts = packageTypes.map((p) => ({ value: p.code, label: p.label }))
    for (const l of order.packages ?? []) {
      if (l.packageType && !opts.some((o) => o.value.toLowerCase() === l.packageType?.toLowerCase())) opts.push({ value: l.packageType, label: l.packageTypeLabel || l.packageType })
    }
    return opts
  }, [packageTypes, order.packages])
  const priorityOptions = useMemo(() => {
    const opts = priorities.map((p) => ({ value: p.code, label: p.label }))
    const current = (order.priority ?? '').toUpperCase()
    if (current && !opts.some((o) => o.value.toUpperCase() === current)) opts.push({ value: current, label: current })
    return opts
  }, [priorities, order.priority])

  // pueblo y ZIP salen de la dirección elegida (del snapshot de la parada si no se cambió el consignatario)
  const consigneeId = live.consigneeLocationPublicId
  const consigneeChanged = consigneeId !== (order.delivery?.locationPublicId ?? '')
  const picked = useSelectedConsignee(editable ? (order.clientPublicId ?? null) : null, editable && consigneeChanged ? consigneeId : null)
  const town = consigneeChanged ? picked?.city : order.delivery?.city
  const zip = consigneeChanged ? picked?.postalCode : order.delivery?.postalCode

  const initialLabel = statuses.find((s) => s.isInitial)?.label ?? t('orders.detail.initialFallback')
  const hint = editable
    ? t('orders.detail.editableHint', { status: order.statusLabel || order.status || '' })
    : lockReason === 'permission'
      ? t('orders.detail.lockedPermissionHint')
      : t('orders.detail.lockedHint', { status: initialLabel })

  async function save(v: OrderEditValues, extra: { confirmDuplicateInvoice?: boolean } = {}) {
    setConflict(null)
    const body = buildOrderPatch(order, v, extra)
    if (!hasOrderChanges(body)) return
    try {
      const saved = await update.mutateAsync({ publicId: order.publicId ?? '', body })
      toast.success(t('orders.detail.saved'))
      form.reset(orderEditValues(saved), { keepDirtyValues: false })
    } catch (err) {
      // factura repetida al cambiar de consignatario: se puede guardar de todos modos si el consignatario lo admite
      const d = duplicateInvoiceOf(err)
      if (d?.confirmable) {
        setDup({ info: d, values: v })
        return
      }
      // otro usuario modificó la orden: el texto del servidor, se relee la ficha y se conserva lo escrito
      if (isConcurrencyConflict(err)) {
        setConflict(err.title)
        toast.error(err.title)
        void queryClient.invalidateQueries({ queryKey: orderKeys.detail })
        return
      }
      throw remapOrderProblem(err)
    }
  }

  const groups = packageGroups(order)
  // las piezas reales del envío (el total que guarda el servidor); sin él, la suma de las líneas
  const pieces = order.totalPieces ?? (order.packages ?? []).reduce((sum, p) => sum + (p.pieces ?? 0), 0)

  return (
    <Panel
      icon={<IconLayers />}
      title={t('orders.detail.panel')}
      actions={
        editable ? (
          <button type="submit" form="order-edit-form" className="btn sm flow" disabled={!form.formState.isDirty || form.formState.isSubmitting}>
            <IconCheck /> {form.formState.isSubmitting ? t('orders.entry.saving') : t('orders.detail.saveChanges')}
          </button>
        ) : undefined
      }
    >
      <p className="d od-hint">{hint}</p>
      {conflict && (
        <div className="form-alert" role="alert">
          {conflict}
        </div>
      )}
      <Form id="order-edit-form" form={form} onSubmit={(v) => save(v as OrderEditValues)} onError={(p) => toast.error(p.title)}>
        <div className="r2">
          <ReadField label={t('orders.entry.client')}>{order.clientName}</ReadField>
          {editable ? (
            <Field name="serviceType" label={t('orders.entry.service')} required>
              <Select options={serviceOptions} placeholder={t('orders.entry.chooseService')} />
            </Field>
          ) : (
            <ReadField label={t('orders.entry.service')}>{order.serviceTypeLabel || order.serviceType}</ReadField>
          )}
        </div>

        {editable ? (
          <Field name="consigneeLocationPublicId" label={t('orders.entry.ticketConsignee')} required help={t('orders.detail.consigneeHelp')}>
            <ConsigneePickerInput clientPublicId={order.clientPublicId ?? null} />
          </Field>
        ) : (
          <ReadField label={t('orders.entry.ticketConsignee')}>{order.delivery?.name}</ReadField>
        )}
        <div className="r3">
          <ReadField label={t('orders.entry.town')}>{town}</ReadField>
          <ReadField label={t('orders.entry.zip')} mono>
            {zip}
          </ReadField>
          <ReadField label={t('orders.detail.zone')}>{order.zoneCode ? `${order.zoneCode}${order.zoneName ? ` · ${order.zoneName}` : ''}${order.zoneGroupName ? ` (${order.zoneGroupName})` : ''}` : t('orders.detail.noZone')}</ReadField>
          {editable ? (
            <Field name="priority" label={t('orders.detail.priority')}>
              <Select options={priorityOptions} placeholder={t('orders.detail.noPriority')} />
            </Field>
          ) : (
            <ReadField label={t('orders.detail.priority')}>{priorities.find((p) => p.code.toUpperCase() === (order.priority ?? '').toUpperCase())?.label ?? order.priority}</ReadField>
          )}
        </div>
        <div className="r3">
          {editable ? (
            <>
              <Field name="readyDate" label={t('orders.entry.ready')}>
                <DateInput />
              </Field>
              <Field name="dueDate" label={t('orders.entry.due')}>
                <DateInput />
              </Field>
            </>
          ) : (
            <>
              <ReadField label={t('orders.entry.ready')}>{order.requestedDate ? f.date(order.requestedDate.slice(0, 10)) : ''}</ReadField>
              <ReadField label={t('orders.entry.due')}>{order.promisedDate ? f.date(order.promisedDate.slice(0, 10)) : ''}</ReadField>
            </>
          )}
          <ReadField label={t('orders.detail.pickup')}>{order.pickup ? [order.pickup.name, order.pickup.city].filter(Boolean).join(' · ') : ''}</ReadField>
        </div>
        <div className="od-facts">
          <ReadField label={t('orders.entry.orderNumber')} mono>
            {order.orderNumber}
          </ReadField>
          <ReadField label={t('orders.entry.invoice')} mono>
            {order.clientInvoiceNumber}
          </ReadField>
          <ReadField label={t('orders.detail.waybill')} mono>
            {order.waybillNumber}
          </ReadField>
        </div>
        {(order.references?.length ?? 0) > 0 && (
          <ReadField label={t('orders.detail.references')}>
            {(order.references ?? []).map((r) => `${r.refTypeLabel || r.refType}: ${r.value}`).join(' · ')}
          </ReadField>
        )}

        {order.isSpecialDelivery ? (
          <ReadField label={t('orders.entry.specialTitle')}>{order.specialServiceName}</ReadField>
        ) : (
          <>
            <div className="od-sub">
              <span className="od-subt">{t('orders.detail.packagesTitle')}</span>
              <span className="od-subr">{t('orders.detail.piecesCount', { count: pieces })}</span>
            </div>
            {editable ? (
              <>
                {fields.map((row, i) => (
                  <div key={row.id} className="r3 oe-pkg">
                    <Field name={`packages.${i}.packageType`} label={t('orders.entry.packageType')} required>
                      <Select options={packageOptions} placeholder={t('orders.entry.choosePackageType')} />
                    </Field>
                    <Field name={`packages.${i}.description`} label={t('orders.entry.description')}>
                      <TextInput maxLength={DESCRIPTION_MAX} placeholder={t('orders.entry.descriptionPlaceholder')} />
                    </Field>
                    <div className="oe-pcs">
                      <Field name={`packages.${i}.pieces`} label={t('orders.entry.pieces')} required>
                        <NumberInput className="mono" min={1} step="1" />
                      </Field>
                      <button
                        type="button"
                        className="rowbtn"
                        disabled={fields.length <= 1}
                        onClick={() => remove(i)}
                        aria-label={t('orders.entry.removePackage', { n: i + 1 })}
                        title={t('orders.entry.removePackage', { n: i + 1 })}
                      >
                        <IconTrash />
                      </button>
                    </div>
                  </div>
                ))}
                {(pkgError?.message || pkgError?.root?.message) && (
                  <p className="ferr" role="alert">
                    {pkgError.message ?? pkgError.root?.message}
                  </p>
                )}
                <button type="button" className="btn sm oe-addpkg" onClick={() => append(emptyPackage(packageTypes[0]?.code ?? ''))}>
                  <IconPlus /> {t('orders.entry.addPackage')}
                </button>
              </>
            ) : (order.packages?.length ?? 0) === 0 ? (
              <p className="note">{t('orders.detail.noPackages')}</p>
            ) : (
              <div className="od-tbl">
                <table className="lst densetbl">
                  <thead>
                    <tr>
                      <th>{t('orders.entry.packageType')}</th>
                      <th>{t('orders.entry.description')}</th>
                      <th>{t('orders.detail.packageNumbers')}</th>
                      <th className="n">{t('orders.entry.pieces')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(order.packages ?? []).map((p) => (
                      <tr key={p.id}>
                        <td>{p.packageTypeLabel || p.packageType || '—'}</td>
                        <td className="od-muted od-longtext">{p.description || '—'}</td>
                        <td className="mono od-small">{pieceNumbersText(p.pieceNumbers ?? (p.packageNumber ? [p.packageNumber] : []))}</td>
                        <td className="n mono">{p.pieces}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <p className="note od-sum">{packageSummaryText(groups)}</p>
              </div>
            )}
          </>
        )}

        <div className="r2 od-last">
          {order.isSpecialDelivery ? null : editable ? (
            <Field name="codAmount" label={t('orders.entry.codAmount')} help={t('orders.entry.codNote')}>
              <NumberInput className="mono" min={0} step="0.01" placeholder="0.00" />
            </Field>
          ) : (
            <ReadField label={t('orders.entry.codAmount')}>{order.codAmount ? f.money(order.codAmount) : ''}</ReadField>
          )}
          {editable ? (
            <Field name="notes" label={t('orders.detail.comment')}>
              <TextArea rows={2} />
            </Field>
          ) : (
            <ReadField label={t('orders.detail.comment')}>{order.notes}</ReadField>
          )}
        </div>
      </Form>

      <StopsBlock order={order} />

      <ConfirmDialog
        open={dup !== null}
        title={t('orders.entry.dup.title')}
        message={
          dup && (
            <>
              <p>{dup.info.message}</p>
              {dup.info.existingOrderPublicId && (
                <p>
                  <Link to={`/orders/${dup.info.existingOrderPublicId}`} target="_blank" rel="noreferrer">
                    {t('orders.entry.dup.view', { order: dup.info.existingOrderNumber })}
                  </Link>
                </p>
              )}
            </>
          )
        }
        confirmLabel={t('orders.detail.dupSave')}
        onConfirm={async () => {
          if (dup) await save(dup.values, { confirmDuplicateInvoice: true })
        }}
        onClose={() => setDup(null)}
      />
    </Panel>
  )
}
