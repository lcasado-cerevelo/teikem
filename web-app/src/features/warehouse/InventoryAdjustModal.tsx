// Pantalla C (Lote F6) — Modal de ajuste de inventario. `inventory.adjust`.
// POST /api/v1/inventory/adjustments. 409 insufficient_stock: el título del servidor ya trae el mensaje exacto
// ("Inventario insuficiente de {sku} en {bin}: disponible {x}, solicitado {y}.") y `Form` lo muestra arriba solo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, Select, TextArea, TextInput, toast } from '../../kernel/ui'
import { useInventoryAdjustment, useWarehouseBins, type ProductListItemDto } from './api'
import { ProductPickerInput, WarehousePickerInput } from './pickers'

// Motivos reservados al sistema: los genera el propio proceso (recepción, conteo, reversa de recolección), nunca a mano.
const SYSTEM_RESERVED_REASONS = new Set(['RECEIPT_VARIANCE', 'COUNT_VARIANCE', 'PICK_BATCH_REVERSAL'])

function decimals(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

export interface InventoryAdjustModalProps {
  open: boolean
  onClose: () => void
}

export function InventoryAdjustModal({ open, onClose }: InventoryAdjustModalProps) {
  const t = useT()
  const adjust = useInventoryAdjustment()
  const { data: reasons = [] } = useLookups('AdjustmentReason')
  const reasonOptions = useMemo(
    () => reasons.filter((r) => !SYSTEM_RESERVED_REASONS.has(r.code)).map((r) => ({ value: r.code, label: r.label })),
    [reasons],
  )
  const [product, setProduct] = useState<ProductListItemDto | null>(null)

  const schema = useMemo(
    () =>
      z.object({
        productPublicId: z.string().min(1, t('warehouse.inventory.adjustModal.errors.productRequired')),
        warehousePublicId: z.string().min(1, t('warehouse.inventory.adjustModal.errors.warehouseRequired')),
        binId: z.string().min(1, t('warehouse.inventory.adjustModal.errors.binRequired')),
        quantity: z
          .number(t('warehouse.inventory.adjustModal.errors.quantityInvalid'))
          .nullable()
          .superRefine((v, ctx) => {
            if (v === null) {
              ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.inventory.adjustModal.errors.quantityInvalid') })
              return
            }
            if (v === 0) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.inventory.adjustModal.errors.quantityZero') })
            else if (decimals(v) > 3) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.inventory.adjustModal.errors.quantityDecimals') })
          }),
        reason: z.string().min(1, t('warehouse.inventory.adjustModal.errors.reasonRequired')),
        notes: z.string(),
        lotNumber: z.string(),
        serialNumbers: z.string(),
      }),
    [t],
  )

  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      productPublicId: '',
      warehousePublicId: '',
      binId: '',
      quantity: null as number | null,
      reason: '',
      notes: '',
      lotNumber: '',
      serialNumbers: '',
    },
  })
  const formId = 'inventory-adjust'
  const warehousePublicId = form.watch('warehousePublicId')
  const { data: bins = [] } = useWarehouseBins(warehousePublicId || null, {}, { enabled: Boolean(warehousePublicId) })
  const binOptions = useMemo(() => bins.filter((b) => b.isActive).map((b) => ({ value: String(b.id), label: b.code ?? '' })), [bins])

  useEffect(() => {
    // el almacén cambió: la posición elegida ya no aplica
    form.setValue('binId', '')
  }, [warehousePublicId, form])

  const trackingType = product?.trackingTypeCode ?? ''

  const close = () => {
    form.reset()
    setProduct(null)
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.inventory.adjustModal.title')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
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
          const serialNumbers = v.serialNumbers
            .split(/\r?\n/)
            .map((s) => s.trim())
            .filter(Boolean)
          await adjust.mutateAsync({
            productPublicId: v.productPublicId,
            warehousePublicId: v.warehousePublicId,
            binId: Number(v.binId),
            quantity: v.quantity,
            reason: v.reason,
            notes: v.notes || null,
            lot: trackingType === 'LOT' && v.lotNumber ? { number: v.lotNumber } : undefined,
            serialNumbers: trackingType === 'SERIAL' && serialNumbers.length > 0 ? serialNumbers : undefined,
          })
          toast.success(t('warehouse.inventory.adjustModal.saved'))
          close()
        }}
      >
        <Field name="productPublicId" label={t('warehouse.inventory.adjustModal.fields.product')} required>
          <ProductPickerInput onPicked={setProduct} />
        </Field>
        <div className="r2">
          <Field name="warehousePublicId" label={t('warehouse.inventory.adjustModal.fields.warehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="binId" label={t('warehouse.inventory.adjustModal.fields.bin')} required>
            <Select options={binOptions} disabled={!warehousePublicId} placeholder={t('warehouse.products.fields.none')} />
          </Field>
        </div>
        <div className="r2">
          <Field name="quantity" label={t('warehouse.inventory.adjustModal.fields.quantity')} required>
            <NumberInput step="0.001" />
          </Field>
          <Field name="reason" label={t('warehouse.inventory.adjustModal.fields.reason')} required>
            <Select options={reasonOptions} placeholder={t('warehouse.products.fields.none')} />
          </Field>
        </div>
        {trackingType === 'LOT' && (
          <Field name="lotNumber" label={t('warehouse.inventory.adjustModal.fields.lotNumber')}>
            <TextInput maxLength={60} />
          </Field>
        )}
        {trackingType === 'SERIAL' && (
          <Field name="serialNumbers" label={t('warehouse.inventory.adjustModal.fields.serialNumbers')}>
            <TextArea rows={4} />
          </Field>
        )}
        <Field name="notes" label={t('warehouse.inventory.adjustModal.fields.notes')}>
          <TextArea rows={2} />
        </Field>
      </Form>
    </Modal>
  )
}
