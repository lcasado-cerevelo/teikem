// Lote 157: el mismo formulario edita un aviso esperado (`PATCH /api/v1/asns/{id}`; el cliente no cambia). El lote y su vencimiento solo salen en las líneas de productos por lote.
// Lote 13 — alta de un aviso de llegada (ASN) de un cliente 3PL (`POST /api/v1/asns`, warehouse.receive), extraído de la
// lista de Recibo: Almacén, Cliente dueño (`ClientPickerInput`, buscador), Referencia (máx. 80) y Llegada esperada; las
// líneas en una misma rejilla (Producto del dueño con buscador, Cantidad esperada > 0 con 3 decimales, Lote), hasta 200.
// El producto se busca solo entre los del cliente dueño (se elige primero). Mensajes exactos del manual 06 §4.
import { zodResolver } from '@hookform/resolvers/zod'
import { Fragment, useMemo } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useT } from '../../kernel/i18n'
import { ClientPickerInput, DateInput, Field, Form, IconTrash, Modal, NumberInput, TextInput, toast } from '../../kernel/ui'
import { useSaveAsn, type AsnDto } from './api'
import { decimalsOf } from './lineRules'
import { ProductPickerInput, WarehousePickerInput } from './pickers'
import { preloadCost, priceDiffers, ProductPriceNote } from './ProductPriceNote'

const MAX_LINES = 200

interface AsnLineForm {
  productPublicId: string | null
  expectedQty: number | null
  lotNumber: string
  /** Vencimiento del lote que llega (solo productos por lote, junto al número de lote). */
  expiryDate: string
  /** El producto es por lote: solo entonces se piden lote y vencimiento. */
  isLot: boolean
  /** Lote 147: precio unitario opcional (alimenta la capa de costo al recibir). */
  unitPrice: number | null
  /** Costo que hoy tiene el producto (precarga el precio y avisa si el escrito es distinto). */
  storedCost: number | null
  /** «Actualizar el precio del producto con este» (solo con inventory.manage). */
  updatePrice: boolean
}
const EMPTY_ASN_LINE: AsnLineForm = { productPublicId: null, expectedQty: null, lotNumber: '', expiryDate: '', isLot: false, unitPrice: null, storedCost: null, updatePrice: false }

export function AsnCreateModal({ onClose, asn }: { onClose: () => void; asn?: AsnDto }) {
  const t = useT()
  const editing = asn != null
  const save = useSaveAsn()
  const schema = useMemo(
    () =>
      z
        .object({
          warehousePublicId: z.string().nullable(),
          clientPublicId: z.string().nullable(),
          reference: z.string().max(80, t('warehouse.asns.errors.referenceMax')),
          expectedDate: z.string(),
          lines: z.array(
            z.object({
              productPublicId: z.string().nullable(),
              expectedQty: z.number().nullable(),
              lotNumber: z.string().max(60, t('warehouse.lineRules.lotTooLong')),
              expiryDate: z.string(),
              isLot: z.boolean(),
              unitPrice: z.number().nullable(),
              storedCost: z.number().nullable(),
              updatePrice: z.boolean(),
            }),
          ),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (!v.clientPublicId) ctx.addIssue({ code: 'custom', path: ['clientPublicId'], message: t('warehouse.asns.errors.clientRequired') })
          if (v.lines.length === 0) ctx.addIssue({ code: 'custom', path: ['reference'], message: t('warehouse.receipts.errors.linesRequired') })
          if (v.lines.length > MAX_LINES) ctx.addIssue({ code: 'custom', path: ['reference'], message: t('warehouse.asns.errors.tooManyLines') })
          v.lines.forEach((l, i) => {
            if (!l.productPublicId) ctx.addIssue({ code: 'custom', path: ['lines', i, 'productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
            if (l.expectedQty == null || l.expectedQty <= 0)
              ctx.addIssue({ code: 'custom', path: ['lines', i, 'expectedQty'], message: t('warehouse.asns.errors.expectedQtyPositive') })
            else if (decimalsOf(l.expectedQty) > 3) ctx.addIssue({ code: 'custom', path: ['lines', i, 'expectedQty'], message: t('warehouse.lineRules.qtyDecimals') })
            if (l.unitPrice != null && (l.unitPrice < 0 || decimalsOf(l.unitPrice) > 4)) ctx.addIssue({ code: 'custom', path: ['lines', i, 'unitPrice'], message: t('warehouse.asns.errors.unitPrice') })
          })
        }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: asn
      ? {
          warehousePublicId: asn.warehousePublicId ?? null,
          clientPublicId: asn.clientPublicId ?? null,
          reference: asn.reference ?? '',
          expectedDate: asn.expectedDate ?? '',
          lines: (asn.lines ?? []).map((l) => ({
            productPublicId: l.productPublicId ?? null,
            expectedQty: l.expectedQty ?? null,
            lotNumber: l.lotNumber ?? '',
            expiryDate: l.expiryDate ?? '',
            isLot: l.trackingType === 'LOT',
            unitPrice: l.unitPrice ?? null,
            storedCost: null,
            updatePrice: false,
          })),
        }
      : { warehousePublicId: null, clientPublicId: null, reference: '', expectedDate: '', lines: [EMPTY_ASN_LINE] },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const clientPublicId = useWatch({ control: form.control, name: 'clientPublicId' })
  const watched = useWatch({ control: form.control, name: 'lines' })
  const formId = 'asn-create'

  return (
    <Modal
      open
      size="lg"
      title={editing ? t('warehouse.asns.edit') : t('warehouse.asns.new')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const lines = v.lines.map((l) => ({
            productPublicId: l.productPublicId,
            expectedQty: l.expectedQty,
            lotNumber: l.isLot ? l.lotNumber.trim() || null : null,
            expiryDate: l.isLot && l.lotNumber.trim() ? l.expiryDate || null : null,
            unitPrice: l.unitPrice,
            updateProductPrice: l.updatePrice && priceDiffers(l.unitPrice, l.storedCost) ? true : null,
          }))
          if (asn) await save.mutateAsync({ action: 'update', id: asn.id as number, body: { warehousePublicId: v.warehousePublicId, reference: v.reference.trim(), expectedDate: v.expectedDate || null, lines } })
          else await save.mutateAsync({ action: 'create', body: { warehousePublicId: v.warehousePublicId, clientPublicId: v.clientPublicId, reference: v.reference.trim() || null, expectedDate: v.expectedDate || null, lines } })
          toast.success(t(editing ? 'warehouse.asns.updated' : 'warehouse.asns.created'))
          onClose()
        }}
      >
        <div className="r2">
          <Field name="warehousePublicId" label={t('warehouse.receipts.fields.warehouse')} required>
            <WarehousePickerInput />
          </Field>
          {editing ? (
            <div className="f">
              <label>{t('warehouse.asns.fields.client')}</label>
              <p>{asn?.clientName}</p>
            </div>
          ) : (
            <Field name="clientPublicId" label={t('warehouse.asns.fields.client')} required>
              <ClientPickerInput />
            </Field>
          )}
        </div>
        <div className="r2">
          <Field name="reference" label={t('warehouse.asns.fields.reference')}>
            <TextInput maxLength={80} />
          </Field>
          <Field name="expectedDate" label={t('warehouse.asns.fields.expectedDate')}>
            <DateInput />
          </Field>
        </div>

        <p className="help">{t('warehouse.asns.lines.title')}</p>
        {/* una sola rejilla: encabezados arriba y una fila por línea (a 480 px cada campo con su etiqueta, uno debajo del otro) */}
        <div className="rcp-asn-grid rcp-asn-price" role="group" aria-label={t('warehouse.asns.lines.title')}>
          <div className="rcp-asn-head" aria-hidden="true">
            <span>{t('warehouse.asns.lines.product')}</span>
            <span>{t('warehouse.asns.lines.expectedQty')}</span>
            <span>{t('warehouse.asns.lines.lotAndExpiry')}</span>
            <span>{t('warehouse.asns.lines.unitPrice')}</span>
            <span />
          </div>
          {fields.map((f, i) => (
            <Fragment key={f.id}>
            <div className="rcp-asn-row">
              <Field name={`lines.${i}.productPublicId`} label={t('warehouse.asns.lines.productOf', { n: i + 1 })} hideLabel required>
                <ProductPickerInput
                  onPicked={(p) => {
                    form.setValue(`lines.${i}.isLot`, p?.trackingTypeCode === 'LOT')
                    if (p?.trackingTypeCode !== 'LOT') {
                      form.setValue(`lines.${i}.lotNumber`, '')
                      form.setValue(`lines.${i}.expiryDate`, '')
                    }
                    // precarga el precio con el costo del producto (si no se escribió otro) y recuerda el guardado para avisar si difiere
                    form.setValue(`lines.${i}.storedCost`, p?.purchaseCost ?? null)
                    form.setValue(`lines.${i}.updatePrice`, false)
                    if (preloadCost(p?.purchaseCost) != null && form.getValues(`lines.${i}.unitPrice`) == null) form.setValue(`lines.${i}.unitPrice`, preloadCost(p?.purchaseCost))
                  }}
                  ownerClientPublicId={clientPublicId}
                  disabled={!clientPublicId}
                  placeholder={clientPublicId ? undefined : t('warehouse.asns.pickClientFirst')}
                />
              </Field>
              <Field name={`lines.${i}.expectedQty`} label={t('warehouse.asns.lines.qtyOf', { n: i + 1 })} hideLabel required>
                <NumberInput min={0} step="0.001" />
              </Field>
              {watched?.[i]?.isLot ? (
                <div>
                  <Field name={`lines.${i}.lotNumber`} label={t('warehouse.asns.lines.lotOf', { n: i + 1 })} hideLabel>
                    <TextInput maxLength={60} placeholder={t('warehouse.asns.lines.lot')} />
                  </Field>
                  <Field name={`lines.${i}.expiryDate`} label={t('warehouse.asns.lines.expiryOf', { n: i + 1 })} hideLabel>
                    <DateInput />
                  </Field>
                </div>
              ) : (
                <span aria-hidden="true" />
              )}
              <Field name={`lines.${i}.unitPrice`} label={t('warehouse.asns.lines.unitPriceOf', { n: i + 1 })} hideLabel>
                <NumberInput min={0} step="0.0001" />
              </Field>
              <button
                type="button"
                className="rowbtn danger rcp-asn-del"
                aria-label={t('warehouse.asns.lines.remove', { n: i + 1 })}
                title={t('warehouse.asns.lines.remove', { n: i + 1 })}
                disabled={fields.length <= 1}
                onClick={() => remove(i)}
              >
                <IconTrash />
              </button>
            </div>
            <ProductPriceNote
              entered={watched?.[i]?.unitPrice}
              stored={watched?.[i]?.storedCost}
              checked={watched?.[i]?.updatePrice ?? false}
              onToggle={(v) => form.setValue(`lines.${i}.updatePrice`, v)}
            />
            </Fragment>
          ))}
        </div>
        <button type="button" className="btn sm" disabled={fields.length >= MAX_LINES} onClick={() => append(EMPTY_ASN_LINE)}>
          {t('warehouse.asns.lines.add')}
        </button>
      </Form>
    </Modal>
  )
}
