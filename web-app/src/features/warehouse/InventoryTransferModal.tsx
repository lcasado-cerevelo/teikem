// Pantalla C (Lote F6) — Modal de transferencia de inventario. `inventory.adjust`.
// POST /api/v1/inventory/transfers. 409 insufficient_stock: el título del servidor ya trae el mensaje exacto y
// `Form` lo muestra arriba solo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, Select, TextArea, toast } from '../../kernel/ui'
import { useInventoryTransfer, useWarehouseBins } from './api'
import { ProductPickerInput, WarehousePickerInput } from './pickers'

export interface InventoryTransferModalProps {
  open: boolean
  onClose: () => void
}

export function InventoryTransferModal({ open, onClose }: InventoryTransferModalProps) {
  const t = useT()
  const transfer = useInventoryTransfer()

  const schema = useMemo(
    () =>
      z
        .object({
          productPublicId: z.string().min(1, t('warehouse.inventory.transferModal.errors.productRequired')),
          fromWarehousePublicId: z.string().min(1, t('warehouse.inventory.transferModal.errors.fromWarehouseRequired')),
          fromBinId: z.string().min(1, t('warehouse.inventory.transferModal.errors.fromBinRequired')),
          toWarehousePublicId: z.string().min(1, t('warehouse.inventory.transferModal.errors.toWarehouseRequired')),
          toBinId: z.string().min(1, t('warehouse.inventory.transferModal.errors.toBinRequired')),
          quantity: z
            .number(t('warehouse.inventory.transferModal.errors.quantityInvalid'))
            .nullable()
            .superRefine((v, ctx) => {
              if (v === null || v <= 0) {
                ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.inventory.transferModal.errors.quantityPositive') })
              }
            }),
          notes: z.string(),
        })
        .refine((v) => v.fromBinId === '' || v.toBinId === '' || v.fromBinId !== v.toBinId, {
          path: ['toBinId'],
          message: t('warehouse.inventory.transferModal.errors.samePosition'),
        }),
    [t],
  )

  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      productPublicId: '',
      fromWarehousePublicId: '',
      fromBinId: '',
      toWarehousePublicId: '',
      toBinId: '',
      quantity: null as number | null,
      notes: '',
    },
  })
  const formId = 'inventory-transfer'
  const fromWarehousePublicId = form.watch('fromWarehousePublicId')
  const toWarehousePublicId = form.watch('toWarehousePublicId')
  const { data: fromBins = [] } = useWarehouseBins(fromWarehousePublicId || null, {}, { enabled: Boolean(fromWarehousePublicId) })
  const { data: toBins = [] } = useWarehouseBins(toWarehousePublicId || null, {}, { enabled: Boolean(toWarehousePublicId) })
  const fromBinOptions = useMemo(() => fromBins.filter((b) => b.isActive).map((b) => ({ value: String(b.id), label: b.code ?? '' })), [fromBins])
  const toBinOptions = useMemo(() => toBins.filter((b) => b.isActive).map((b) => ({ value: String(b.id), label: b.code ?? '' })), [toBins])

  useEffect(() => {
    form.setValue('fromBinId', '')
  }, [fromWarehousePublicId, form])
  useEffect(() => {
    form.setValue('toBinId', '')
  }, [toWarehousePublicId, form])

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.inventory.transferModal.title')}
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
          await transfer.mutateAsync({
            productPublicId: v.productPublicId,
            fromWarehousePublicId: v.fromWarehousePublicId || null,
            fromBinId: Number(v.fromBinId),
            toWarehousePublicId: v.toWarehousePublicId || null,
            toBinId: Number(v.toBinId),
            quantity: v.quantity,
            notes: v.notes || null,
          })
          toast.success(t('warehouse.inventory.transferModal.saved'))
          close()
        }}
      >
        <Field name="productPublicId" label={t('warehouse.inventory.transferModal.fields.product')} required>
          <ProductPickerInput />
        </Field>
        <div className="r2">
          <Field name="fromWarehousePublicId" label={t('warehouse.inventory.transferModal.fields.fromWarehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="fromBinId" label={t('warehouse.inventory.transferModal.fields.fromBin')} required>
            <Select options={fromBinOptions} disabled={!fromWarehousePublicId} placeholder={t('warehouse.products.fields.none')} />
          </Field>
        </div>
        <div className="r2">
          <Field name="toWarehousePublicId" label={t('warehouse.inventory.transferModal.fields.toWarehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="toBinId" label={t('warehouse.inventory.transferModal.fields.toBin')} required>
            <Select options={toBinOptions} disabled={!toWarehousePublicId} placeholder={t('warehouse.products.fields.none')} />
          </Field>
        </div>
        <Field name="quantity" label={t('warehouse.inventory.transferModal.fields.quantity')} required>
          <NumberInput step="0.001" />
        </Field>
        <Field name="notes" label={t('warehouse.inventory.transferModal.fields.notes')}>
          <TextArea rows={2} />
        </Field>
      </Form>
    </Modal>
  )
}
