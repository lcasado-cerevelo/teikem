// Reportar un daño (2026-10-08, `warehouse.damage`): algo que LLEGÓ DAÑADO en un recibo (esas unidades no entran como buenas) o se DAÑÓ en el almacén
// (ya está en inventario). Qué hacer con ello: mandarlo a CUARENTENA o DESECHARLO de una vez. La causa (vino así / accidente en el camino / en el
// almacén / otra) es solo informativa. POST /api/v1/damage-reports: 409 insufficient_stock y 422 (serie, o almacén sin posición de cuarentena) llegan
// con su mensaje exacto en el título y `Form` los muestra arriba.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { ComboSelectInput, Field, Form, Modal, NumberInput, TextArea, TextInput, toast } from '../../kernel/ui'
import { useProductLots, useReceipts, useReportDamage, type DamageReportDto } from './api'
import { damageBody, defaultCause, type DamageFormValues, type DamageOrigin } from './damageForm'
import { formatNumber } from './lineRules'
import { BinPickerInput, ProductPickerInput, WarehousePickerInput } from './pickers'
import { SegInput } from './SegInput'

const M = 'warehouse.damage.errors'

export interface DamageReportModalProps {
  open: boolean
  onClose: () => void
  /** Desde la ficha de un recibo: el recibo y su almacén ya vienen puestos (origen «Llegó dañado»). */
  receipt?: { publicId: string; number: string; warehousePublicId: string }
  onReported?: (damage: DamageReportDto) => void
}

/** Modal «Reportar daño». Se monta limpio cada vez que se abre. */
export function DamageReportModal({ open, onClose, receipt, onReported }: DamageReportModalProps) {
  if (!open) return null
  return <DamageModalBody onClose={onClose} receipt={receipt} onReported={onReported} />
}

function DamageModalBody({ onClose, receipt, onReported }: Omit<DamageReportModalProps, 'open'>) {
  const t = useT()
  const lang = useLang()
  const report = useReportDamage()
  const causes = useLookups('DamageCause')
  const [tracking, setTracking] = useState('')
  const initialOrigin: DamageOrigin = receipt ? 'RECEIPT' : 'WAREHOUSE'

  const schema = useMemo(
    () =>
      z
        .object({
          origin: z.enum(['WAREHOUSE', 'RECEIPT']),
          warehousePublicId: z.string().nullable().refine(Boolean, t(`${M}.warehouseRequired`)),
          receiptPublicId: z.string(),
          productPublicId: z.string().nullable().refine(Boolean, t(`${M}.productRequired`)),
          fromBinId: z.string(),
          lotId: z.string(),
          lotNumber: z.string(),
          quantity: z.number(t(`${M}.quantityInvalid`)).nullable().refine((v) => v !== null && v > 0, t(`${M}.quantityPositive`)),
          cause: z.string().min(1, t(`${M}.causeRequired`)),
          disposition: z.enum(['QUARANTINE', 'DISCARD'], t(`${M}.dispositionRequired`)),
          notes: z.string().max(300, t(`${M}.notesMax`)),
        })
        .superRefine((v, c) => {
          if (v.origin === 'WAREHOUSE' && !v.fromBinId) c.addIssue({ code: 'custom', path: ['fromBinId'], message: t(`${M}.fromBinRequired`) })
          if (v.origin === 'RECEIPT' && !v.receiptPublicId) c.addIssue({ code: 'custom', path: ['receiptPublicId'], message: t(`${M}.receiptRequired`) })
          if (tracking === 'LOT' && !v.lotId && !(v.origin === 'RECEIPT' && v.disposition === 'QUARANTINE' && v.lotNumber.trim()))
            c.addIssue({ code: 'custom', path: ['lotId'], message: t(`${M}.lotRequired`) })
        }),
    [t, tracking],
  )
  const form = useForm<DamageFormValues>({
    resolver: zodResolver(schema) as never,
    defaultValues: {
      origin: initialOrigin,
      warehousePublicId: receipt?.warehousePublicId ?? null,
      receiptPublicId: receipt?.publicId ?? '',
      productPublicId: null,
      fromBinId: '',
      lotId: '',
      lotNumber: '',
      quantity: null,
      cause: defaultCause(initialOrigin),
      disposition: '',
      notes: '',
    },
  })
  const formId = 'damage-report'
  const [origin, warehousePublicId, productPublicId, disposition] = useWatch({
    control: form.control,
    name: ['origin', 'warehousePublicId', 'productPublicId', 'disposition'],
  })

  const receiptsQ = useReceipts({ warehousePublicId: warehousePublicId ?? undefined, take: 100 }, { enabled: origin === 'RECEIPT' && !receipt && Boolean(warehousePublicId), handleAccessDenied: false })
  const receiptOptions = useMemo(
    () => (receiptsQ.data?.items ?? []).map((r) => ({ value: r.publicId ?? '', label: [r.number, r.origin, r.senderName].filter(Boolean).join(' · ') })),
    [receiptsQ.data],
  )
  const lotsQ = useProductLots(productPublicId, { enabled: tracking === 'LOT', handleAccessDenied: false })
  const lotOptions = useMemo(
    () => (lotsQ.data ?? []).map((l) => ({ value: String(l.id ?? ''), label: `${l.lotNumber ?? ''} · ${formatNumber(l.qtyOnHand ?? 0, lang)}` })),
    [lotsQ.data, lang],
  )
  const causeOptions = useMemo(() => (causes.data ?? []).map((c) => ({ value: c.code, label: c.label })), [causes.data])
  const busy = form.formState.isSubmitting

  const originOptions = [
    { value: 'WAREHOUSE', label: t('warehouse.damage.originWarehouse') },
    { value: 'RECEIPT', label: t('warehouse.damage.originReceipt') },
  ]
  const dispositionOptions = [
    { value: 'QUARANTINE', label: t('warehouse.damage.toQuarantine') },
    { value: 'DISCARD', label: t('warehouse.damage.toDiscard') },
  ]

  return (
    <Modal
      open
      title={t('warehouse.damage.reportTitle')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('warehouse.damage.reportSubmit')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const dto = await report.mutateAsync(damageBody(v, tracking))
          toast.success(t(v.disposition === 'QUARANTINE' ? 'warehouse.damage.reportedQuarantine' : 'warehouse.damage.reportedDiscard', { code: dto.code ?? '' }))
          onReported?.(dto)
          onClose()
        }}
      >
        {!receipt && (
          <Field name="origin" label={t('warehouse.damage.fields.origin')} required>
            <SegInput
              label={t('warehouse.damage.fields.origin')}
              options={originOptions}
              onPicked={(o) => {
                form.setValue('cause', defaultCause(o as DamageOrigin))
                form.setValue('fromBinId', '')
                form.setValue('receiptPublicId', '')
              }}
            />
          </Field>
        )}
        {receipt && <p className="help">{t('warehouse.damage.fromReceipt', { number: receipt.number })}</p>}
        <p className="help">{t(origin === 'RECEIPT' ? 'warehouse.damage.originReceiptHelp' : 'warehouse.damage.originWarehouseHelp')}</p>
        {!receipt && (
          <Field name="warehousePublicId" label={t('warehouse.damage.fields.warehouse')} required>
            <WarehousePickerInput />
          </Field>
        )}
        {origin === 'RECEIPT' && !receipt && (
          <Field name="receiptPublicId" label={t('warehouse.damage.fields.receipt')} required>
            <ComboSelectInput options={receiptOptions} loading={receiptsQ.isLoading} placeholder={t('warehouse.damage.receiptPlaceholder')} />
          </Field>
        )}
        <Field name="productPublicId" label={t('warehouse.damage.fields.product')} required>
          <ProductPickerInput
            onPicked={(p) => {
              setTracking(p?.trackingTypeCode ?? '')
              form.setValue('lotId', '')
              form.setValue('lotNumber', '')
            }}
          />
        </Field>
        {origin === 'WAREHOUSE' && (
          <Field name="fromBinId" label={t('warehouse.damage.fields.fromBin')} required>
            <BinPickerInput warehousePublicId={warehousePublicId} />
          </Field>
        )}
        {tracking === 'LOT' && (
          <div className="r2">
            <Field name="lotId" label={t('warehouse.damage.fields.lot')} required={!(origin === 'RECEIPT' && disposition === 'QUARANTINE')}>
              <ComboSelectInput options={lotOptions} loading={lotsQ.isLoading} placeholder={t('warehouse.damage.lotPlaceholder')} />
            </Field>
            {origin === 'RECEIPT' && disposition === 'QUARANTINE' && (
              <Field name="lotNumber" label={t('warehouse.damage.fields.newLot')} help={t('warehouse.damage.newLotHelp')}>
                <TextInput maxLength={60} />
              </Field>
            )}
          </div>
        )}
        <div className="r2">
          <Field name="quantity" label={t('warehouse.damage.fields.quantity')} required>
            <NumberInput className="mono" step="0.001" min={0} />
          </Field>
          <Field name="cause" label={t('warehouse.damage.fields.cause')} required help={t('warehouse.damage.causeHelp')}>
            <ComboSelectInput options={causeOptions} loading={causes.isLoading} />
          </Field>
        </div>
        <Field name="disposition" label={t('warehouse.damage.fields.disposition')} required help={t(disposition === 'DISCARD' ? 'warehouse.damage.discardHelp' : 'warehouse.damage.quarantineHelp')}>
          <SegInput label={t('warehouse.damage.fields.disposition')} options={dispositionOptions} />
        </Field>
        <Field name="notes" label={t('warehouse.damage.fields.notes')}>
          <TextArea rows={2} maxLength={300} />
        </Field>
      </Form>
    </Modal>
  )
}
