// Ajuste de inventario (Lote F6; Lote 14 D11: "Subir" / "Bajar" + cantidad POSITIVA). `inventory.adjust`.
// POST /api/v1/inventory/adjustments: el API sigue recibiendo la cantidad con signo (`adjustmentBody` la arma: subir +,
// bajar −). Motivo con buscador (`ComboSelectInput`) filtrado por dirección (`reasonsForDirection`: Encontrado solo al
// subir; Daño, Pérdida y Vencido solo al bajar). Pista "Disponible en la posición: N" (saldos de la posición) y, al bajar,
// tope 'No puede bajar más de lo disponible en la posición ({qty}).'. Rastreo: LOT al subir = número de lote (nuevo o
// existente), al bajar = lote con saldo en la posición; SERIAL al subir = series nuevas, al bajar = series disponibles en la
// posición (la cantidad = número de series). Posición: al subir, cualquiera del almacén; al BAJAR, solo las posiciones donde
// el producto tiene disponible (cada una con su disponible, por código; deshabilitada sin producto; una que ya no aplica al
// cambiar producto, almacén o dirección se quita sola). Nota obligatoria (`adjustNotesSchema`, mensaje exacto del API).
// 409 insufficient_stock: el título del servidor ya trae el mensaje exacto y `Form` lo muestra arriba solo.
// `AdjustmentFields` (con `useAdjustmentForm` de adjustmentForm.ts) lo comparten este modal (Transferencias y ajustes y
// Kárdex) y el bloque "Ajustar inventario" de la ficha del producto (ProductEditorModal), que fija el producto.
import { useMemo } from 'react'
import { useWatch } from 'react-hook-form'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { ComboSelectInput, Field, Form, Modal, NumberInput, TextArea, TextInput, toast } from '../../kernel/ui'
import { AdjustDirectionInput, SerialsPickInput } from './adjustControls'
import { useAdjustmentForm, useSubmitAdjustment, type AdjustInitial, type AdjustmentFormState } from './adjustmentForm'
import { reasonAllowed, reasonsForDirection, type AdjustDirection } from './adjustmentReasons'
import { formatDate, formatNumber } from './lineRules'
import { adjustMagnitude, lotOptions, type AdjustFormValues } from './movementForms'
import { BinPickerInput, ProductPickerInput, WarehousePickerInput, type BinPickerOption } from './pickers'
import { ADJUST_NOTES_MAX } from './productRules'

/**
 * Campos del ajuste dentro de un <Form form={state.form}>, en el orden Subir/Bajar → Producto → Almacén · Posición →
 * Cantidad · Motivo → Lote o series → Nota. Con `state.fixedProduct` el producto no se elige (ficha del producto).
 */
export function AdjustmentFields({ state, idPrefix = 'adj' }: { state: AdjustmentFormState; idPrefix?: string }) {
  const t = useT()
  const lang = useLang()
  const { form, tracking, direction, warehousePublicId, balances, available, serials, downBins, downBinsLoading, fixedProduct, picked } = state
  const productPublicId = useWatch({ control: form.control, name: 'productPublicId' })
  const reasonsQ = useLookups('AdjustmentReason')
  const reasonOptions = useMemo(
    () => reasonsForDirection(reasonsQ.data ?? [], direction as AdjustDirection | '').map((r) => ({ value: r.code, label: r.label })),
    [reasonsQ.data, direction],
  )
  const lots = useMemo(
    () =>
      lotOptions(balances, (qty, expiry) =>
        [t('warehouse.inventory.adjustModal.lotAvailable', { qty: formatNumber(qty, lang) }), expiry ? t('warehouse.inventory.adjustModal.lotExpiry', { date: formatDate(expiry, lang) }) : '']
          .filter(Boolean)
          .join(' · '),
      ),
    [balances, t, lang],
  )
  // al bajar: solo donde hay disponible del producto, con su disponible
  const binOptions = useMemo<BinPickerOption[] | undefined>(
    () =>
      downBins?.map((b) => ({
        id: b.binId,
        code: b.binCode,
        zoneCode: b.zoneCode,
        zoneTypeCode: b.zoneTypeCode,
        hint: t('ui.binPicker.available', { qty: formatNumber(b.qtyAvailable, lang) }),
      })),
    [downBins, t, lang],
  )
  const noProductToLower = direction === 'down' && !productPublicId
  const values = useWatch({ control: form.control }) as AdjustFormValues
  const serialCount = tracking === 'SERIAL' ? adjustMagnitude({ ...values, serials: values.serials ?? [] }, tracking) : 0
  const availableHelp = available != null ? t('warehouse.inventory.adjustModal.availableInBin', { qty: formatNumber(available, lang) }) : undefined

  const onDirection = (d: AdjustDirection) => {
    picked.direction(d)
    // un motivo que no vale para la nueva dirección se quita; lote y series dependen de la dirección
    if (!reasonAllowed(form.getValues('reason'), d)) form.setValue('reason', '')
    form.setValue('lotId', '')
    form.setValue('lotNumber', '')
    form.setValue('serials', [])
    form.setValue('serialText', '')
    picked.lot(null)
  }

  return (
    <>
      <Field name="direction" label={t('warehouse.inventory.adjustModal.fields.direction')} required>
        <AdjustDirectionInput label={t('warehouse.inventory.adjustModal.fields.direction')} onPicked={onDirection} />
      </Field>
      {!fixedProduct && (
        <Field name="productPublicId" label={t('warehouse.inventory.adjustModal.fields.product')} required>
          <ProductPickerInput
            onPicked={(p) => {
              picked.product(p)
              form.setValue('lotId', '')
              form.setValue('serials', [])
            }}
          />
        </Field>
      )}
      <div className="r2">
        <Field name="warehousePublicId" label={t('warehouse.inventory.adjustModal.fields.warehouse')} required>
          <WarehousePickerInput />
        </Field>
        <Field name="binId" label={t('warehouse.inventory.adjustModal.fields.bin')} required help={tracking === 'SERIAL' ? availableHelp : undefined}>
          <BinPickerInput
            warehousePublicId={warehousePublicId}
            options={binOptions}
            optionsLoading={downBinsLoading}
            disabled={noProductToLower}
            placeholder={noProductToLower ? t('ui.binPicker.pickProductFirst') : undefined}
            onPicked={(b) => {
              picked.bin(b)
              form.setValue('lotId', '')
              form.setValue('serials', [])
            }}
          />
        </Field>
      </div>
      <div className="r2">
        {tracking === 'SERIAL' ? (
          <div className="f">
            <label htmlFor={`${idPrefix}-serial-count`}>{t('warehouse.inventory.adjustModal.fields.quantity')}</label>
            <output id={`${idPrefix}-serial-count`} className="mono">
              {t('warehouse.inventory.adjustModal.serialCount', { count: serialCount })}
            </output>
          </div>
        ) : (
          <Field name="quantity" label={t('warehouse.inventory.adjustModal.fields.quantity')} required help={availableHelp}>
            <NumberInput className="mono" step="0.001" min={0} />
          </Field>
        )}
        <Field name="reason" label={t('warehouse.inventory.adjustModal.fields.reason')} required>
          <ComboSelectInput options={reasonOptions} loading={reasonsQ.isLoading} placeholder={t('warehouse.inventory.adjustModal.reasonPlaceholder')} />
        </Field>
      </div>
      {tracking === 'LOT' && direction === 'up' && (
        <Field name="lotNumber" label={t('warehouse.inventory.adjustModal.fields.lotNumber')} required>
          <TextInput maxLength={60} />
        </Field>
      )}
      {tracking === 'LOT' && direction === 'down' && (
        <Field name="lotId" label={t('warehouse.inventory.adjustModal.fields.lot')} required>
          <ComboSelectInput options={lots} placeholder={t('warehouse.inventory.adjustModal.lotPlaceholder')} onPicked={picked.lot} />
        </Field>
      )}
      {tracking === 'SERIAL' && direction === 'up' && (
        <Field name="serialText" label={t('warehouse.inventory.adjustModal.fields.serialNumbers')} required>
          <TextArea rows={3} />
        </Field>
      )}
      {tracking === 'SERIAL' && direction === 'down' && (
        <Field name="serials" label={t('warehouse.inventory.adjustModal.fields.serialsOut')} required>
          <SerialsPickInput serials={serials} placeholder={t('warehouse.inventory.adjustModal.serialsPlaceholder')} />
        </Field>
      )}
      <Field name="notes" label={t('warehouse.inventory.adjustModal.fields.notes')} required>
        <TextArea rows={2} maxLength={ADJUST_NOTES_MAX} placeholder={t('warehouse.products.editor.adjustNotesPlaceholder')} />
      </Field>
    </>
  )
}

export interface InventoryAdjustModalProps {
  open: boolean
  onClose: () => void
  /** Valores iniciales (producto, almacén, posición, dirección). */
  initial?: AdjustInitial
}

/** Modal "Ajuste de inventario". Se monta limpio cada vez que se abre. */
export function InventoryAdjustModal({ open, onClose, initial }: InventoryAdjustModalProps) {
  if (!open) return null
  return <AdjustModalBody onClose={onClose} initial={initial} />
}

function AdjustModalBody({ onClose, initial }: { onClose: () => void; initial?: AdjustInitial }) {
  const t = useT()
  const lang = useLang()
  const state = useAdjustmentForm({ initial })
  const submit = useSubmitAdjustment()
  const { form, tracking } = state
  const formId = 'inventory-adjust'
  const busy = form.formState.isSubmitting

  return (
    <Modal
      open
      title={t('warehouse.inventory.adjustModal.title')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('warehouse.inventory.adjustModal.submit')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const q = await submit(v, tracking)
          toast.success(t('warehouse.inventory.adjustModal.savedQty', { qty: `${q > 0 ? '+' : ''}${formatNumber(q, lang)}` }))
          onClose()
        }}
      >
        <AdjustmentFields state={state} />
      </Form>
    </Modal>
  )
}
