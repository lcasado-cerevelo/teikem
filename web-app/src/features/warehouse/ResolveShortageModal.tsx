// Resolver el faltante de una línea de compra (`POST /purchase-orders/{publicId}/lines/{lineId}/resolve`). Lo comparten la
// pestaña Faltantes de la ficha de la orden y la pantalla 'Ajustes de inventario' (maqueta `ajustesAlmacen()`).
// Permisos: inventory.adjust (quien lo abre); REORDER exige además purchasing.manage y MANUAL_ADJUSTMENT el módulo
// WMS_LOTSERIAL: solo entonces se consultan posiciones —al abrir el BinPicker— y el seguimiento del producto, sin sacar
// al usuario ante un 403. Cerrar y Reordenar mandan la cantidad pendiente que se ve en pantalla: si otro usuario resolvió
// parte del faltante entretanto, el API responde 400 ('Cerrar y Reordenar resuelven el faltante completo (N)…') y el
// aviso sale arriba del formulario (el campo Cantidad no se pinta en esas acciones). Sin `rowVersion`: la protección real
// es el bloqueo de la orden y el pendiente recalculado bajo ese bloqueo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ModuleKeys, useCan, useModule } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Form, Modal, NumberInput, Select, TextArea, TextInput, toast } from '../../kernel/ui'
import { useProduct, useResolveShortage, type ShortageLineDto, type ShortageResolveResultDto } from './api'
import { decimalsOf, parseSerials } from './lineRules'
import { BinPickerInput } from './pickers'
import './warehouse.css'

export type ShortageAction = 'CLOSE' | 'REORDER' | 'MANUAL_ADJUSTMENT'

/** Lo mínimo de la orden que necesita el modal (lo cumplen `PurchaseOrderDto` y `PoShortageSummaryDto`). */
export interface ShortagePoRef {
  publicId?: string | null
  warehousePublicId?: string | null
  rowVersion?: string | null
}

export interface ResolveShortageModalProps {
  open: boolean
  onClose: () => void
  po: ShortagePoRef
  line: ShortageLineDto | null
  /** Acción elegida al abrir (por defecto CLOSE). */
  initialAction?: ShortageAction
  /** Valores precargados desde la fila (cantidad y notas del ajuste manual). */
  initial?: { quantity?: number | null; notes?: string }
  /** Con él, quien abre el modal muestra su propio aviso; sin él, el genérico 'Faltante resuelto.'. */
  onResolved?: (result: ShortageResolveResultDto) => void
}

export function ResolveShortageModal({ open, onClose, po, line, initialAction, initial, onResolved }: ResolveShortageModalProps) {
  const t = useT()
  const resolve = useResolveShortage()
  const canManage = useCan('purchasing.manage')
  const hasLotSerial = useModule(ModuleKeys.WmsLotSerial)
  const pending = line?.qtyPending ?? 0

  const actionOptions = useMemo(() => {
    const opts = [{ value: 'CLOSE', label: t('warehouse.purchaseOrders.shortages.modal.actions.close') }]
    if (canManage) opts.push({ value: 'REORDER', label: t('warehouse.purchaseOrders.shortages.modal.actions.reorder') })
    if (hasLotSerial) opts.push({ value: 'MANUAL_ADJUSTMENT', label: t('warehouse.purchaseOrders.shortages.modal.actions.manualAdjustment') })
    return opts
  }, [t, canManage, hasLotSerial])

  const schema = useMemo(
    () =>
      z
        .object({
          action: z.string().min(1),
          quantity: z.number().nullable(),
          reason: z.string(),
          binId: z.string(),
          notes: z.string(),
          lot: z.string(),
          lotExpiry: z.string(),
          serialNumbers: z.string(),
        })
        .superRefine((v, ctx) => {
          if (v.action !== 'MANUAL_ADJUSTMENT') return
          if (v.quantity === null || v.quantity <= 0) {
            ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.shortages.errors.quantityPositive'), path: ['quantity'] })
          } else {
            if (decimalsOf(v.quantity) > 3) {
              ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.shortages.errors.quantityDecimals'), path: ['quantity'] })
            }
            if (v.quantity > pending) {
              ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.shortages.errors.quantityExceeds'), path: ['quantity'] })
            }
          }
          if (!v.binId) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.purchaseOrders.shortages.errors.binRequired'), path: ['binId'] })
          if (v.lot.trim().length > 60) ctx.addIssue({ code: z.ZodIssueCode.custom, message: t('warehouse.lineRules.lotTooLong'), path: ['lot'] })
        }),
    [t, pending],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      action: initialAction ?? 'CLOSE',
      quantity: initial?.quantity ?? (null as number | null),
      reason: 'PO_SHORTAGE',
      binId: '',
      notes: initial?.notes ?? '',
      lot: '',
      lotExpiry: '',
      serialNumbers: '',
    },
  })
  const action = form.watch('action')
  const formId = 'shortage-resolve'
  const isManual = action === 'MANUAL_ADJUSTMENT' && hasLotSerial

  // Solo el ajuste manual usa posiciones (BinPicker: consulta al abrirlo, sin sacar de la pantalla ante un 403) y el
  // seguimiento del producto (WMS_LOTSERIAL). CLOSE/REORDER no consultan nada de WMS (la ruta es de compras).
  const product = useProduct(line?.productPublicId ?? null, { enabled: open && isManual, handleAccessDenied: false })
  const tracking = (product.data?.product?.trackingTypeCode ?? '').toUpperCase()
  const sku = line?.sku ?? ''

  /** Réplica de AdjustmentRules.ValidateTracking (LOT exige lote; SERIAL, cantidad entera y una serie por unidad). */
  function trackingErrors(v: { quantity?: number | null; lot: string; serialNumbers: string }): boolean {
    let failed = false
    const qty = v.quantity ?? 0
    const serials = parseSerials(v.serialNumbers)
    if (tracking === 'LOT' && !v.lot.trim()) {
      form.setError('lot', { message: t('warehouse.purchaseOrders.shortages.errors.lotRequired', { sku }) })
      failed = true
    }
    if (tracking === 'SERIAL') {
      if (!Number.isInteger(qty)) {
        form.setError('quantity', { message: t('warehouse.purchaseOrders.shortages.errors.serialInteger') })
        failed = true
      } else if (serials.length === 0) {
        form.setError('serialNumbers', { message: t('warehouse.purchaseOrders.shortages.errors.serialsRequired', { sku }) })
        failed = true
      } else if (serials.length !== qty) {
        form.setError('serialNumbers', {
          message: t('warehouse.purchaseOrders.shortages.errors.serialCountMismatch', { count: serials.length, qty: String(qty) }),
        })
        failed = true
      }
    }
    return failed
  }

  const close = () => {
    form.reset()
    onClose()
  }
  if (!line) return null

  return (
    <Modal
      open={open}
      title={t('warehouse.purchaseOrders.shortages.modal.title')}
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
          const manual = v.action === 'MANUAL_ADJUSTMENT'
          if (manual && trackingErrors(v)) return
          const serials = parseSerials(v.serialNumbers)
          const result = await resolve.mutateAsync({
            publicId: po.publicId ?? '',
            lineId: line.purchaseOrderLineId ?? 0,
            body: {
              action: v.action,
              // Cerrar/Reordenar: el pendiente que se ve; si cambió en el servidor, 400 en vez de actuar sobre datos viejos
              quantity: manual ? v.quantity : pending,
              reason: manual ? v.reason || 'PO_SHORTAGE' : null,
              notes: v.notes || null,
              binId: manual && v.binId ? Number(v.binId) : null,
              // lote (existente por número o nuevo) y series: solo en el ajuste manual (el API los rechaza con otras acciones)
              lot: manual && v.lot.trim() ? { number: v.lot.trim(), expiryDate: v.lotExpiry || null } : undefined,
              serialNumbers: manual && serials.length > 0 ? serials : null,
              rowVersion: po.rowVersion ?? null,
            },
          })
          if (onResolved) onResolved(result)
          else toast.success(t('warehouse.purchaseOrders.shortages.resolved'))
          close()
        }}
      >
        <p className="note rsm-context">
          {t('warehouse.purchaseOrders.shortages.modal.context', { sku, product: line.productName ?? '', qty: String(pending) })}
        </p>
        <Field name="action" label={t('warehouse.purchaseOrders.shortages.modal.action')} required>
          <Select options={actionOptions} />
        </Field>
        {action === 'MANUAL_ADJUSTMENT' && (
          <>
            <Field
              name="quantity"
              label={t('warehouse.purchaseOrders.shortages.modal.quantity')}
              required
              help={t('warehouse.purchaseOrders.shortages.modal.pendingHelp', { qty: String(pending) })}
            >
              <NumberInput step="0.001" />
            </Field>
            <Field name="reason" label={t('warehouse.purchaseOrders.shortages.modal.reason')} required>
              <Select
                options={[
                  { value: 'PO_SHORTAGE', label: t('warehouse.purchaseOrders.shortages.modal.reasons.poShortage') },
                  { value: 'FOUND', label: t('warehouse.purchaseOrders.shortages.modal.reasons.found') },
                ]}
              />
            </Field>
            <Field name="binId" label={t('warehouse.purchaseOrders.shortages.modal.bin')} required>
              <BinPickerInput warehousePublicId={po.warehousePublicId} />
            </Field>
            {tracking === 'LOT' && (
              <div className="r2">
                <Field name="lot" label={t('warehouse.purchaseOrders.shortages.modal.lot')} required help={t('warehouse.purchaseOrders.shortages.modal.lotHelp')}>
                  <TextInput maxLength={60} />
                </Field>
                <Field name="lotExpiry" label={t('warehouse.purchaseOrders.shortages.modal.lotExpiry')}>
                  <DateInput />
                </Field>
              </div>
            )}
            {tracking === 'SERIAL' && (
              <Field
                name="serialNumbers"
                label={t('warehouse.purchaseOrders.shortages.modal.serialNumbers')}
                required
                help={t('warehouse.purchaseOrders.shortages.modal.serialNumbersHelp')}
              >
                <TextArea rows={4} />
              </Field>
            )}
          </>
        )}
        <Field name="notes" label={t('warehouse.purchaseOrders.shortages.modal.notes')}>
          <TextArea rows={2} />
        </Field>
      </Form>
    </Modal>
  )
}
