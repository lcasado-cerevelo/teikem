// Alta DETALLADA de una orden (Lote F-C1; maqueta `detailEditor`): paso 01 Cliente y servicio, 02 Consignatario y 03 Paquetes y COD
// (o Servicio especial) con el «Ticket de la orden» a la derecha. Dos formas de guardar: «Guardar borrador» (nace en Entrada) y
// «Guardar y confirmar» (`confirmNow`: cotiza, revisa el crédito y confirma en una transacción). Si el servidor responde 422
// `credit_exceeded` y el usuario tiene `orders.credit_override` se ofrece «Autorizar y confirmar» (reenvía con `overrideCredit`);
// 409 `duplicate_invoice_confirmable` abre «Factura repetida para este consignatario» (reenvía con `confirmDuplicateInvoice`).
// El precio NO se muestra al capturar. Entrega especial: servicio del catálogo del cliente, sin paquetes, COD ni chofer.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { get, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { Can, useCan, useModule } from '../../kernel/access'
import { tenantToday } from '../../kernel/api/tenantZone'
import { applyProblemDetails } from '../../kernel/api/problem'
import { lookupLabelOrCode, useLookups, useTenantSettings } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ClientPickerInput, DateInput, Field, Form, IconPlus, IconTrash, Modal, NumberInput, Select, TextInput, toast } from '../../kernel/ui'
import { IconTag } from '../../kernel/ui/screenIcons'
import { useClient } from '../clients/api'
import { useSpecialServices } from '../clients/contractApi'
import { useConsigneeContacts, useSelectedConsignee } from '../consignees/api'
import { ConsigneePickerInput } from '../consignees/ConsigneePicker'
import { pickPhone } from '../consignees/consigneeRules'
import { useCreateOrder, type OrderDetailDto } from './api'
import { CreditOverrideDialog } from './CreditOverrideDialog'
import {
  buildOrderRequest,
  duplicateInvoiceOf,
  emptyOrderValues,
  emptyPackage,
  isCreditExceeded,
  orderSchema,
  packageGroups,
  packageSummaryText,
  remapOrderProblem,
  type DuplicateInvoice,
  type OrderFormValues,
} from './orderRules'
import './orders.css'

type SaveMode = 'draft' | 'confirm'

/** Un envío que quedó esperando una decisión del usuario (factura repetida / crédito excedido). */
interface Pending {
  values: OrderFormValues
  confirmNow: boolean
  confirmDuplicateInvoice: boolean
}

export interface OrderEntryFormProps {
  /** La orden se creó (ficha devuelta por el servidor); `confirmed` = se guardó con «Guardar y confirmar». */
  onCreated: (order: OrderDetailDto, confirmed: boolean) => void
  /** Lote F-G2: interruptor «Label al guardar» (la lista imprime el label de la orden creada; sin él no se pinta). */
  labelOnSave?: { value: boolean; onChange: (on: boolean) => void }
}

/** Campo de solo lectura (datos del consignatario elegido). */
function ReadOnlyField({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  const id = useId()
  return (
    <div className="f">
      <label htmlFor={id}>{label}</label>
      <input id={id} className={mono ? 'mono' : undefined} value={value} readOnly disabled />
    </div>
  )
}

export function OrderEntryForm({ onCreated, labelOnSave }: OrderEntryFormProps) {
  const t = useT()
  const f = useFormat()
  const canOverride = useCan('orders.credit_override')
  const canRead = useCan('contracts.read')
  const catalogOn = useModule('CATALOG')
  const canContracts = canRead && catalogOn
  const create = useCreateOrder()
  const { data: settings } = useTenantSettings()
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const { data: packageTypes = [] } = useLookups('PackageType')
  const modeRef = useRef<SaveMode>('draft')
  const [savingMode, setSavingMode] = useState<SaveMode>('draft')
  const [dup, setDup] = useState<{ info: DuplicateInvoice; pending: Pending } | null>(null)
  const [dupError, setDupError] = useState<string | null>(null)
  const [dupBusy, setDupBusy] = useState(false)
  const [credit, setCredit] = useState<{ message: string; pending: Pending } | null>(null)
  const [creditDenied, setCreditDenied] = useState(false)

  const schema = useMemo(
    () =>
      orderSchema({
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
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: emptyOrderValues(tenantToday()) })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'packages' })
  const clientId = useWatch({ control: form.control, name: 'clientPublicId' })
  const isSpecial = useWatch({ control: form.control, name: 'isSpecial' })
  const serviceType = useWatch({ control: form.control, name: 'serviceType' })
  const consigneeId = useWatch({ control: form.control, name: 'consigneeLocationPublicId' })
  const specialServiceId = useWatch({ control: form.control, name: 'specialServiceId' })
  const packagesValue = useWatch({ control: form.control, name: 'packages' })
  const codAmount = useWatch({ control: form.control, name: 'codAmount' })

  // ---- Datos del cliente: nombre (ticket) y quién asigna los números ----
  const { data: client } = useClient(clientId, true)
  const invoiceEditable = client?.numberSettings?.clientAssignsInvoiceNumber === true
  const orderNumberEditable = client ? (client.numberSettings?.clientAssignsOrderNumber ?? true) : false

  // ---- Consignatario elegido (campos de solo lectura) ----
  const consignee = useSelectedConsignee(clientId, consigneeId)
  const contacts = useConsigneeContacts(consignee?.id ?? null)
  const phone = pickPhone(contacts.data)

  // ---- Servicios especiales del cliente ----
  const specials = useSpecialServices(clientId ?? '', false, canContracts && !!clientId && isSpecial)
  const specialOptions = useMemo(
    () =>
      specials.data?.componentEnabled === false
        ? []
        : (specials.data?.items ?? [])
            .filter((s) => s.isCurrent !== false)
            .map((s) => ({ value: String(s.id), label: `${s.typeName ?? ''} · ${f.money(s.rate ?? 0)}`, name: s.typeName ?? '' })),
    [specials.data, f],
  )

  // ---- Predeterminados de la compañía: servicio y tipo de paquete ----
  const defaultService = useMemo(() => serviceTypes.find((s) => s.code.toLowerCase() === (settings?.defaultServiceType ?? '').toLowerCase())?.code ?? '', [serviceTypes, settings])
  const defaultPackage = useMemo(() => packageTypes.find((p) => p.code.toLowerCase() === (settings?.defaultPackageType ?? '').toLowerCase())?.code ?? '', [packageTypes, settings])
  useEffect(() => {
    if (defaultService && !form.getValues('serviceType')) form.setValue('serviceType', defaultService)
  }, [defaultService, form])
  useEffect(() => {
    if (!defaultPackage) return
    form.getValues('packages').forEach((p, i) => {
      if (!p.packageType) form.setValue(`packages.${i}.packageType`, defaultPackage)
    })
  }, [defaultPackage, form])

  // al cambiar de cliente, lo que dependía del anterior se limpia
  const prevClient = useRef(clientId)
  useEffect(() => {
    if (prevClient.current === clientId) return
    prevClient.current = clientId
    form.setValue('consigneeLocationPublicId', '')
    form.setValue('specialServiceId', '')
    form.setValue('clientInvoiceNumber', '')
  }, [clientId, form])

  const serviceOptions = useMemo(() => serviceTypes.map((s) => ({ value: s.code, label: s.label })), [serviceTypes])
  const packageOptions = useMemo(() => packageTypes.map((p) => ({ value: p.code, label: p.label })), [packageTypes])

  // ---- Envío ----
  async function send(p: Pending, extra: { overrideCredit?: boolean } = {}): Promise<void> {
    const body = buildOrderRequest(p.values, {
      confirmNow: p.confirmNow,
      overrideCredit: extra.overrideCredit,
      confirmDuplicateInvoice: p.confirmDuplicateInvoice,
      invoiceEditable,
      orderNumberEditable,
    })
    const order = await create.mutateAsync({ body, idempotencyKey: crypto.randomUUID() })
    toast.success(t('orders.entry.created', { number: order.orderNumber ?? '' }))
    onCreated(order, p.confirmNow)
  }

  /** Decide qué hacer con un error del envío: abrir un diálogo o devolverlo (con los campos renombrados) para que el formulario lo muestre. */
  function route(err: unknown, p: Pending): 'handled' | unknown {
    const d = duplicateInvoiceOf(err)
    if (d?.confirmable) {
      setDupError(null)
      setDup({ info: d, pending: { ...p, confirmDuplicateInvoice: false } })
      return 'handled'
    }
    if (p.confirmNow && isCreditExceeded(err)) {
      if (canOverride) {
        setCredit({ message: err.title, pending: p })
        return 'handled'
      }
      setCreditDenied(true)
    }
    return remapOrderProblem(err)
  }

  async function submit(values: OrderFormValues) {
    setCreditDenied(false)
    const pending: Pending = { values, confirmNow: modeRef.current === 'confirm', confirmDuplicateInvoice: false }
    try {
      await send(pending)
    } catch (err) {
      const out = route(err, pending)
      if (out !== 'handled') throw out
    }
  }

  async function createAnyway() {
    if (!dup) return
    setDupBusy(true)
    setDupError(null)
    const pending: Pending = { ...dup.pending, confirmDuplicateInvoice: true }
    try {
      await send(pending)
      setDup(null)
    } catch (err) {
      if (pending.confirmNow && isCreditExceeded(err) && canOverride) {
        setDup(null)
        setCredit({ message: err.title, pending })
      } else {
        setDupError(applyProblemDetails(err).title)
      }
    } finally {
      setDupBusy(false)
    }
  }

  // ---- Ticket (sin precio) ----
  const serviceLabel = lookupLabelOrCode(serviceType, serviceTypes) ?? '—'
  const groups = packageGroups({
    packages: (packagesValue ?? [])
      .filter((p) => p.packageType)
      .map((p) => ({ packageType: p.packageType, packageTypeLabel: lookupLabelOrCode(p.packageType, packageTypes), description: p.description, pieces: p.pieces ?? 0 })),
    packagesSummary: null,
    totalPieces: 0,
  })
  const hasLines = (packagesValue ?? []).some((p) => p.packageType)
  const specialName = specialOptions.find((o) => o.value === specialServiceId)?.name
  const busy = form.formState.isSubmitting || create.isPending
  const pkgError = get(form.formState.errors, 'packages') as { message?: string; root?: { message?: string } } | undefined

  return (
    <>
      <Form
        id="order-entry-form"
        form={form}
        className="oe-detail"
        onSubmit={submit}
        onError={(p) => toast.error(p.title)}
      >
        <div className="oe-stepper" aria-hidden="true">
          <div className="st on">1</div>
          <div className="ln" />
          <div className="st">2</div>
          <div className="ln" />
          <div className="st">3</div>
        </div>

        <div className="oe-main" role="group" aria-label={t('orders.entry.steps')}>
          <section className="docsec">
            <h3>
              <span className="num">01</span> {t('orders.entry.s1')}
            </h3>
            <p className="d">{t('orders.entry.s1d')}</p>
            <div className="r2">
              <Field name="clientPublicId" label={t('orders.entry.client')} required>
                <ClientPickerInput />
              </Field>
              <Field name="serviceType" label={t('orders.entry.service')} required>
                <Select options={serviceOptions} placeholder={t('orders.entry.chooseService')} />
              </Field>
            </div>
            <div className="r3">
              <Field name="readyDate" label={t('orders.entry.ready')}>
                <DateInput />
              </Field>
              <Field name="dueDate" label={t('orders.entry.due')}>
                <DateInput />
              </Field>
              <Field
                name="clientInvoiceNumber"
                label={
                  <>
                    {t('orders.entry.invoice')}
                    {!invoiceEditable && <span className="oe-auto"> {t('orders.entry.auto')}</span>}
                  </>
                }
              >
                <TextInput className="mono" maxLength={40} disabled={!invoiceEditable} placeholder={invoiceEditable ? undefined : t('orders.entry.autoPlaceholder')} />
              </Field>
            </div>
          </section>

          <section className="docsec">
            <h3>
              <span className="num">02</span> {t('orders.entry.s2')}
            </h3>
            <p className="d">{t('orders.entry.s2d')}</p>
            <div className="r2">
              <Field
                name="consigneeLocationPublicId"
                label={t('orders.entry.consigneeName')}
                required
                help={consignee ? (consignee.allowDupInvoice ? t('orders.entry.dupAllowed') : t('orders.entry.dupNotAllowed')) : t('orders.entry.consigneeHelp')}
              >
                <ConsigneePickerInput clientPublicId={clientId} />
              </Field>
              <ReadOnlyField label={t('orders.entry.consigneeNumber')} value={consignee?.code ?? ''} mono />
            </div>
            <ReadOnlyField label={t('orders.entry.address')} value={consignee ? [consignee.line1, consignee.line2].filter(Boolean).join(', ') : ''} />
            <div className="r3">
              <ReadOnlyField label={t('orders.entry.town')} value={consignee?.city ?? ''} />
              <ReadOnlyField label={t('orders.entry.zip')} value={consignee?.postalCode ?? ''} mono />
              <ReadOnlyField label={t('orders.entry.phone')} value={phone?.value ? f.phone(phone.value) : ''} mono />
            </div>
          </section>

          <section className="docsec oe-special">
            <label className="sw">
              <input type="checkbox" role="switch" disabled={!canContracts} {...form.register('isSpecial')} />
              <span className="tk" aria-hidden="true" />
              <span>{t('orders.entry.special')}</span>
            </label>
            <p className="d">{canContracts ? t('orders.entry.specialHint') : t('orders.entry.specialNoPermission')}</p>
          </section>

          <section className="docsec">
            {isSpecial ? (
              <>
                <h3>
                  <span className="num">03</span> {t('orders.entry.specialTitle')}
                </h3>
                <p className="d">{t('orders.entry.specialDesc')}</p>
                <div className="r2">
                  <Field
                    name="orderNumber"
                    label={
                      <>
                        {t('orders.entry.orderNumber')}
                        {!orderNumberEditable && <span className="oe-auto"> {t('orders.entry.auto')}</span>}
                      </>
                    }
                  >
                    <TextInput className="mono" maxLength={40} disabled={!orderNumberEditable} placeholder={orderNumberEditable ? undefined : t('orders.entry.autoPlaceholder')} />
                  </Field>
                  <Field
                    name="specialServiceId"
                    label={t('orders.entry.specialTitle')}
                    required
                    help={
                      !clientId
                        ? t('orders.entry.specialNeedClient')
                        : specials.data?.componentEnabled === false
                          ? t('orders.entry.specialComponentOff')
                          : undefined
                    }
                  >
                    <Select
                      options={specialOptions}
                      disabled={specialOptions.length === 0}
                      placeholder={specialOptions.length === 0 && clientId && !specials.isLoading ? t('orders.entry.specialNone') : t('orders.entry.specialChoose')}
                    />
                  </Field>
                </div>
              </>
            ) : (
              <>
                <h3>
                  <span className="num">03</span> {t('orders.entry.s3')}
                </h3>
                <p className="d">{t('orders.entry.s3d')}</p>
                {fields.map((row, i) => (
                  <div key={row.id} className="r3 oe-pkg">
                    <Field name={`packages.${i}.packageType`} label={t('orders.entry.packageType')} required>
                      <Select options={packageOptions} placeholder={t('orders.entry.choosePackageType')} />
                    </Field>
                    <Field name={`packages.${i}.description`} label={t('orders.entry.description')}>
                      <TextInput maxLength={200} placeholder={t('orders.entry.descriptionPlaceholder')} />
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
                <button type="button" className="btn sm oe-addpkg" onClick={() => append(emptyPackage(defaultPackage))}>
                  <IconPlus /> {t('orders.entry.addPackage')}
                </button>
                <div className="oe-cod">
                  <Field name="codAmount" label={t('orders.entry.codAmount')}>
                    <NumberInput className="mono" min={0} step="0.01" placeholder="0.00" />
                  </Field>
                </div>
                <p className="d oe-codnote">{t('orders.entry.codNote')}</p>
              </>
            )}
          </section>
        </div>

        <aside className="ticket" aria-label={t('orders.entry.ticket')}>
          <h4>
            <IconTag /> {t('orders.entry.ticket')}
          </h4>
          <div className="zig" />
          <div className="row">
            <span>{t('orders.entry.client')}</span>
            <b>{client?.name || '—'}</b>
          </div>
          <div className="row">
            <span>{t('orders.entry.service')}</span>
            <b>{serviceLabel}</b>
          </div>
          <div className="row">
            <span>{t('orders.entry.ticketConsignee')}</span>
            <b>{consignee?.name || '—'}</b>
          </div>
          <div className="row">
            <span>{t('orders.entry.town')}</span>
            <b>{consignee?.city || '—'}</b>
          </div>
          {isSpecial ? (
            <div className="row tot">
              <span>{t('orders.entry.ticketSpecial')}</span>
              <b>{specialName || '—'}</b>
            </div>
          ) : (
            <>
              <div className="row">
                <span>{t('orders.entry.ticketPackages')}</span>
                <b>{hasLines ? packageSummaryText(groups) : '—'}</b>
              </div>
              <div className="row tot">
                <span>{t('orders.entry.ticketCod')}</span>
                <b>{f.money(codAmount && codAmount > 0 ? codAmount : 0)}</b>
              </div>
            </>
          )}
          <div className="zig" />
          {labelOnSave && (
            <label className="sw oe-autolabel">
              <input type="checkbox" role="switch" checked={labelOnSave.value} onChange={(e) => labelOnSave.onChange(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('orders.entry.labelOnSave')}</span>
            </label>
          )}
          <Can perm="orders.create">
            <div className="oe-save">
              <button type="submit" className="btn" disabled={busy} onClick={() => {
                modeRef.current = 'draft'
                setSavingMode('draft')
              }}>
                {busy && savingMode === 'draft' ? t('orders.entry.saving') : t('orders.entry.saveDraft')}
              </button>
              <button type="submit" className="btn flow" disabled={busy} onClick={() => {
                modeRef.current = 'confirm'
                setSavingMode('confirm')
              }}>
                {busy && savingMode === 'confirm' ? t('orders.entry.saving') : t('orders.entry.saveConfirm')}
              </button>
            </div>
          </Can>
          {creditDenied && (
            <p className="note oe-denied" role="status">
              {t('orders.entry.credit.denied')}
            </p>
          )}
          <div className="note oe-ticketnote">{t('orders.entry.ticketNote')}</div>
          {labelOnSave?.value && <div className="note oe-ticketnote">{t('orders.entry.labelOnSaveNote')}</div>}
        </aside>
      </Form>

      <Modal
        open={dup !== null}
        size="sm"
        title={t('orders.entry.dup.title')}
        onClose={() => {
          setDup(null)
          toast.info(t('orders.entry.dup.cancelled'))
        }}
        dismissible={!dupBusy}
        footer={
          <>
            <button
              type="button"
              className="btn"
              disabled={dupBusy}
              onClick={() => {
                setDup(null)
                toast.info(t('orders.entry.dup.cancelled'))
              }}
            >
              {t('orders.entry.dup.dont')}
            </button>
            <button type="button" className="btn flow" disabled={dupBusy} onClick={() => void createAnyway()}>
              {dupBusy ? t('common.loading') : t('orders.entry.dup.create')}
            </button>
          </>
        }
      >
        {dup && (
          <>
            {dupError && (
              <div className="form-alert" role="alert">
                {dupError}
              </div>
            )}
            <p>
              {t(dup.info.existingOrderNumber ? 'orders.entry.dup.body' : 'orders.entry.dup.bodyNoOrder', {
                consignee: consignee?.name ?? '',
                order: dup.info.existingOrderNumber,
                invoice: dup.pending.values.clientInvoiceNumber.trim(),
              })}
            </p>
            {dup.info.existingOrderPublicId && (
              <p>
                <Link to={`/orders/${dup.info.existingOrderPublicId}`} target="_blank" rel="noreferrer">
                  {t('orders.entry.dup.view', { order: dup.info.existingOrderNumber })}
                </Link>
              </p>
            )}
          </>
        )}
      </Modal>

      <CreditOverrideDialog
        open={credit !== null}
        message={credit?.message}
        note={t('orders.entry.credit.note')}
        onAuthorize={async () => {
          if (credit) await send({ ...credit.pending, confirmNow: true }, { overrideCredit: true })
        }}
        onClose={() => setCredit(null)}
      />
    </>
  )
}
