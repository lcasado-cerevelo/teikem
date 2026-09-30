// Transferencia de inventario (Lote F6; Lote 14: orden origen → ítem → destino y lote/series, hallazgo 3). `inventory.adjust`.
// POST /api/v1/inventory/transfers. Orden de captura: almacén de origen → posición de origen → ítem (lo disponible en esa
// posición: producto + lote, `ComboSelect` sobre los saldos con `onlyAvailable`) → series (productos SERIAL: las disponibles
// en la posición; la cantidad = número de series) → almacén de destino (por defecto el de origen) → posición de destino →
// cantidad (tope: lo disponible del ítem) → nota. El lote viaja como `lotId` (el del saldo elegido) y las series en
// `serialNumbers`. 409 insufficient_stock: el título del servidor ya trae el mensaje exacto y `Form` lo muestra arriba solo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useLang, useT } from '../../kernel/i18n'
import { ComboSelectInput, Field, Form, Modal, NumberInput, TextArea, toast } from '../../kernel/ui'
import { SerialsPickInput } from './adjustControls'
import { useInventoryBalances, useInventoryTransfer, useProduct, useProductSerials, type BalanceDto } from './api'
import { formatNumber } from './lineRules'
import { parseTransferItemKey, serialsAt, transferItemKey, transferItemOptions } from './movementForms'
import { BinPickerInput, WarehousePickerInput } from './pickers'
import { decimals } from './productRules'

export interface InventoryTransferModalProps {
  open: boolean
  onClose: () => void
}

const M = 'warehouse.inventory.transferModal.errors'
const NO_BALANCES: BalanceDto[] = []

interface TransferValues {
  fromWarehousePublicId: string | null
  fromBinId: string
  /** Ítem de la posición de origen (`transferItemKey`: producto + lote). */
  item: string
  serials: string[]
  toWarehousePublicId: string | null
  toBinId: string
  quantity: number | null
  notes: string
}

/** Modal "Transferencia de inventario". Se monta limpio cada vez que se abre. */
export function InventoryTransferModal({ open, onClose }: InventoryTransferModalProps) {
  if (!open) return null
  return <TransferModalBody onClose={onClose} />
}

function TransferModalBody({ onClose }: { onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const transfer = useInventoryTransfer()
  // lo que decide las reglas se guarda en estado al elegir (posición de origen e ítem) para armar el esquema con ello
  const [fromBin, setFromBin] = useState<number | null>(null)
  const [item, setItem] = useState('')

  const balancesQ = useInventoryBalances(
    { binIds: fromBin != null ? [fromBin] : undefined, onlyAvailable: true, take: 200 },
    { enabled: fromBin != null, handleAccessDenied: false },
  )
  const balances = fromBin != null ? (balancesQ.data?.items ?? NO_BALANCES) : NO_BALANCES
  const itemOptions = useMemo(
    () =>
      transferItemOptions(balances, {
        lot: (lot) => t('warehouse.inventory.transferModal.lot', { lot }),
        available: (qty) => t('warehouse.inventory.transferModal.available', { qty: formatNumber(qty, lang) }),
      }),
    [balances, t, lang],
  )
  const picked = parseTransferItemKey(item)
  const pickedBalance = picked ? balances.find((b) => transferItemKey(b) === item) : undefined
  const productQ = useProduct(picked?.productPublicId ?? null, { handleAccessDenied: false })
  const tracking = productQ.data?.product?.trackingTypeCode ?? ''
  const available = pickedBalance ? (pickedBalance.qtyAvailable ?? 0) : null
  const serialsQ = useProductSerials(picked?.productPublicId ?? null, { status: 'AVAILABLE' }, { enabled: tracking === 'SERIAL', handleAccessDenied: false })
  const serialOptions = useMemo(() => serialsAt(serialsQ.data ?? [], fromBin, picked?.lotId ?? null), [serialsQ.data, fromBin, picked?.lotId])

  const schema = useMemo(() => {
    const exceeds = (qty: number) => t(`${M}.exceedsAvailable`, { qty: formatNumber(qty, lang) })
    return z
      .object({
        fromWarehousePublicId: z
          .string()
          .nullable()
          .refine((v) => Boolean(v), t(`${M}.fromWarehouseRequired`)),
        fromBinId: z.string().min(1, t(`${M}.fromBinRequired`)),
        item: z.string().min(1, t(`${M}.itemRequired`)),
        serials: z.array(z.string()).superRefine((v, c) => {
          if (tracking !== 'SERIAL') return
          if (v.length === 0) c.addIssue({ code: 'custom', message: t(`${M}.serialsRequired`) })
          else if (available != null && v.length > available) c.addIssue({ code: 'custom', message: exceeds(available) })
        }),
        toWarehousePublicId: z
          .string()
          .nullable()
          .refine((v) => Boolean(v), t(`${M}.toWarehouseRequired`)),
        toBinId: z.string().min(1, t(`${M}.toBinRequired`)),
        quantity: z.number(t(`${M}.quantityInvalid`)).nullable().superRefine((v, c) => {
          if (tracking === 'SERIAL') return
          if (v === null || v <= 0) c.addIssue({ code: 'custom', message: t(`${M}.quantityPositive`) })
          else if (decimals(v) > 3) c.addIssue({ code: 'custom', message: t('warehouse.inventory.adjustModal.errors.quantityDecimals') })
          else if (available != null && v > available) c.addIssue({ code: 'custom', message: exceeds(available) })
        }),
        notes: z.string().max(300, t('warehouse.products.errors.adjustNotesMax')),
      })
      .refine((v) => v.fromBinId === '' || v.toBinId === '' || v.fromBinId !== v.toBinId, {
        path: ['toBinId'],
        message: t(`${M}.samePosition`),
      })
  }, [t, lang, tracking, available])

  const form = useForm<TransferValues>({
    resolver: zodResolver(schema) as never,
    defaultValues: {
      fromWarehousePublicId: null,
      fromBinId: '',
      item: '',
      serials: [],
      toWarehousePublicId: null,
      toBinId: '',
      quantity: null,
      notes: '',
    },
  })
  const formId = 'inventory-transfer'
  const [fromWarehousePublicId, serialsChosen, toWarehousePublicId] = useWatch({
    control: form.control,
    name: ['fromWarehousePublicId', 'serials', 'toWarehousePublicId'],
  })

  // el destino arranca en el almacén de origen (se puede cambiar)
  useEffect(() => {
    if (fromWarehousePublicId && !form.getValues('toWarehousePublicId')) form.setValue('toWarehousePublicId', fromWarehousePublicId)
  }, [fromWarehousePublicId, form])

  const busy = form.formState.isSubmitting
  const availableHelp = available != null ? t('warehouse.inventory.transferModal.availableHelp', { qty: formatNumber(available, lang) }) : undefined

  return (
    <Modal
      open
      title={t('warehouse.inventory.transferModal.title')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('warehouse.inventory.transferModal.submit')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const it = parseTransferItemKey(v.item)
          const serials = tracking === 'SERIAL' ? v.serials : []
          await transfer.mutateAsync({
            productPublicId: it?.productPublicId ?? null,
            fromWarehousePublicId: v.fromWarehousePublicId,
            fromBinId: Number(v.fromBinId),
            toWarehousePublicId: v.toWarehousePublicId,
            toBinId: Number(v.toBinId),
            quantity: tracking === 'SERIAL' ? serials.length : v.quantity,
            lotId: it?.lotId ?? undefined,
            serialNumbers: serials.length > 0 ? serials : undefined,
            notes: v.notes.trim() || null,
          })
          toast.success(t('warehouse.inventory.transferModal.saved'))
          onClose()
        }}
      >
        <div className="r2">
          <Field name="fromWarehousePublicId" label={t('warehouse.inventory.transferModal.fields.fromWarehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="fromBinId" label={t('warehouse.inventory.transferModal.fields.fromBin')} required>
            {/* posiciones del almacén ORIGEN */}
            <BinPickerInput
              warehousePublicId={fromWarehousePublicId}
              onlyWithStock
              onPicked={(b) => {
                // otra posición de origen: el ítem y las series elegidos ya no aplican
                setFromBin(b?.id ?? null)
                setItem('')
                form.setValue('item', '')
                form.setValue('serials', [])
              }}
            />
          </Field>
        </div>
        <Field name="item" label={t('warehouse.inventory.transferModal.fields.item')} required help={fromBin == null ? t('warehouse.inventory.transferModal.itemHelp') : undefined}>
          <ComboSelectInput
            options={itemOptions}
            loading={fromBin != null && balancesQ.isLoading}
            disabled={fromBin == null}
            placeholder={t('warehouse.inventory.transferModal.itemPlaceholder')}
            onPicked={(o) => {
              setItem(o?.value ?? '')
              form.setValue('serials', [])
            }}
          />
        </Field>
        {tracking === 'SERIAL' && (
          <Field name="serials" label={t('warehouse.inventory.transferModal.fields.serials')} required>
            <SerialsPickInput serials={serialOptions} placeholder={t('warehouse.inventory.adjustModal.serialsPlaceholder')} />
          </Field>
        )}
        <div className="r2">
          <Field name="toWarehousePublicId" label={t('warehouse.inventory.transferModal.fields.toWarehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="toBinId" label={t('warehouse.inventory.transferModal.fields.toBin')} required>
            {/* posiciones del almacén DESTINO (independiente del origen) */}
            <BinPickerInput warehousePublicId={toWarehousePublicId} />
          </Field>
        </div>
        {tracking === 'SERIAL' ? (
          <div className="f">
            <label htmlFor="transfer-serial-count">{t('warehouse.inventory.transferModal.fields.quantity')}</label>
            <output id="transfer-serial-count" className="mono">
              {t('warehouse.inventory.adjustModal.serialCount', { count: serialsChosen.length })}
            </output>
          </div>
        ) : (
          <Field name="quantity" label={t('warehouse.inventory.transferModal.fields.quantity')} required help={availableHelp}>
            <NumberInput className="mono" step="0.001" min={0} />
          </Field>
        )}
        <Field name="notes" label={t('warehouse.inventory.transferModal.fields.notes')}>
          <TextArea rows={2} maxLength={300} />
        </Field>
      </Form>
    </Modal>
  )
}
