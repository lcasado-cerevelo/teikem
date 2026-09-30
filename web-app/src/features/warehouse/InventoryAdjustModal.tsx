// Pantalla C (Lote F6) — Modal de ajuste de inventario. `inventory.adjust`.
// POST /api/v1/inventory/adjustments. 409 insufficient_stock: el título del servidor ya trae el mensaje exacto
// ("Inventario insuficiente de {sku} en {bin}: disponible {x}, solicitado {y}.") y `Form` lo muestra arriba solo.
// Nota obligatoria (decisión del 2026-09-30, "todo ajuste manual exige nota"): misma regla y mensaje que el bloque de ajuste
// de ProductEditorModal (`adjustNotesSchema`); el 400 del API en `errors.notes` queda bajo el campo Notas.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, Select, TextArea, TextInput, toast } from '../../kernel/ui'
import { selectableAdjustmentReasons } from './adjustmentReasons'
import { useInventoryAdjustment, type ProductListItemDto } from './api'
import { BinPickerInput, ProductPickerInput, WarehousePickerInput } from './pickers'
import { ADJUST_NOTES_MAX, adjustNotesSchema, adjustQuantitySchema } from './productRules'

export interface InventoryAdjustModalProps {
  open: boolean
  onClose: () => void
}

export function InventoryAdjustModal({ open, onClose }: InventoryAdjustModalProps) {
  const t = useT()
  const adjust = useInventoryAdjustment()
  const { data: reasons = [] } = useLookups('AdjustmentReason')
  const reasonOptions = useMemo(
    () => selectableAdjustmentReasons(reasons).map((r) => ({ value: r.code, label: r.label })),
    [reasons],
  )
  const [product, setProduct] = useState<ProductListItemDto | null>(null)

  const schema = useMemo(
    () =>
      z.object({
        productPublicId: z.string().min(1, t('warehouse.inventory.adjustModal.errors.productRequired')),
        warehousePublicId: z.string().min(1, t('warehouse.inventory.adjustModal.errors.warehouseRequired')),
        binId: z.string().min(1, t('warehouse.inventory.adjustModal.errors.binRequired')),
        quantity: adjustQuantitySchema(t),
        reason: z.string().min(1, t('warehouse.inventory.adjustModal.errors.reasonRequired')),
        notes: adjustNotesSchema(t),
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
            notes: v.notes,
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
            <BinPickerInput warehousePublicId={warehousePublicId} />
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
        <Field name="notes" label={t('warehouse.inventory.adjustModal.fields.notes')} required>
          <TextArea rows={2} maxLength={ADJUST_NOTES_MAX} placeholder={t('warehouse.products.editor.adjustNotesPlaceholder')} />
        </Field>
      </Form>
    </Modal>
  )
}
