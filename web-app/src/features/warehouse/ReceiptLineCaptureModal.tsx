// Lote 13 — "Lote y series" de una línea del recibo abierto (antes `LineModal` de la ficha del recibo, que ya no existe):
// lote con fabricación y vencimiento, números de serie y posición de recepción de la línea, con `PUT
// /receipts/{publicId}/lines/{lineId}` (warehouse.receive). En un producto por serie lo recibido es el número de series
// capturadas (el servidor lo fija igual si llegan series sin cantidad). Lo abre el ícono de la fila en `ReceiptLinesEditor`
// solo para productos LOT o SERIAL. Validación en el cliente con `receiptLineIssues` (mensajes exactos del manual 06); el
// error `line` del servidor (sin campo) va bajo el campo que lo causa.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'
import { useLang, useT } from '../../kernel/i18n'
import { DateInput, Field, Form, Modal, TextArea, TextInput, toast } from '../../kernel/ui'
import { productLabel, useSaveReceiptLine, type ReceiptDetailDto } from './api'
import { formatNumber, parseSerials, receiptLineIssues, remapProblemFields } from './lineRules'
import { BinPickerInput } from './pickers'

type LineDto = components['schemas']['ReceiptLineDto']

/** Tipos de zona donde se recibe (posición de recepción de la línea). */
const RECEIVING_ZONES = ['STAGING', 'CROSSDOCK'] as const

/**
 * El error `line` del servidor (ValidateCapture) no nombra campo: se pone bajo el que lo causa según el seguimiento
 * (SERIAL: cantidad no entera → series; LOT: series no admitidas).
 */
function captureFieldFor(tracking: string | null | undefined): string {
  return tracking === 'LOT' ? 'lot' : 'serialNumbers'
}

export interface ReceiptLineCaptureModalProps {
  receipt: ReceiptDetailDto
  line: LineDto
  onClose: () => void
  /** La ficha que devolvió el PUT (la rejilla pone la fila como quedó). */
  onSaved?: (receipt: ReceiptDetailDto, line: LineDto | null) => void
}

export function ReceiptLineCaptureModal({ receipt, line, onClose, onSaved }: ReceiptLineCaptureModalProps) {
  const t = useT()
  const lang = useLang()
  const save = useSaveReceiptLine()
  const header = receipt.header ?? {}
  const publicId = header.publicId ?? ''
  const tracking = line.trackingTypeCode ?? 'NONE'
  const sku = line.sku ?? ''

  const schema = useMemo(
    () =>
      z
        .object({
          lot: z.string(),
          lotManufactureDate: z.string(),
          lotExpiryDate: z.string(),
          serialNumbers: z.string(),
          stagingBinId: z.string(),
        })
        .superRefine((v, ctx) => {
          const serials = parseSerials(v.serialNumbers)
          const issues = receiptLineIssues({
            sku,
            trackingTypeCode: tracking,
            receivedQty: tracking === 'SERIAL' ? serials.length : (line.receivedQty ?? 0),
            lot: v.lot,
            lotManufactureDate: v.lotManufactureDate,
            lotExpiryDate: v.lotExpiryDate,
            serials,
          })
          for (const issue of issues) {
            const path = issue.field === 'receivedQty' ? 'serialNumbers' : issue.field
            ctx.addIssue({ code: 'custom', path: [path], message: t(`warehouse.lineRules.${issue.code}`, issue.params) })
          }
        }),
    [t, sku, tracking, line.receivedQty],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      lot: line.lotNumber ?? '',
      lotManufactureDate: '',
      lotExpiryDate: line.expiryDate ?? '',
      serialNumbers: (line.serialNumbers ?? []).join('\n'),
      stagingBinId: line.stagingBinId != null ? String(line.stagingBinId) : '',
    },
  })
  const serialText = useWatch({ control: form.control, name: 'serialNumbers' })
  const formId = `receipt-line-capture-${line.id ?? 0}`

  return (
    <Modal
      open
      title={t('warehouse.receipts.capture.title', { product: productLabel({ sku: line.sku, name: line.productName }) })}
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
          const serials = parseSerials(v.serialNumbers)
          const lotNumber = v.lot.trim()
          const lot = lotNumber ? { number: lotNumber, manufactureDate: v.lotManufactureDate || null, expiryDate: v.lotExpiryDate || null } : undefined
          const stagingBinId = v.stagingBinId ? Number(v.stagingBinId) : null
          let dto: ReceiptDetailDto
          try {
            dto = await save.mutateAsync({
              publicId,
              action: 'update',
              lineId: line.id ?? 0,
              body: {
                // SERIAL: lo recibido es el número de series (la lista completa; vacía la borra)
                receivedQty: tracking === 'SERIAL' ? serials.length : null,
                lot,
                clearLot: !lotNumber && line.lotId != null ? true : null,
                serialNumbers: tracking === 'SERIAL' ? serials : null,
                stagingBinId: stagingBinId !== (line.stagingBinId ?? null) ? stagingBinId : null,
              },
            })
          } catch (err) {
            throw remapProblemFields(err, (k) => {
              if (k === 'line') return captureFieldFor(tracking)
              if (k === 'receivedQty') return 'serialNumbers'
              return null
            })
          }
          toast.success(t('warehouse.receipts.capture.saved'))
          onSaved?.(dto, (dto.lines ?? []).find((l) => l.id === line.id) ?? null)
          onClose()
        }}
      >
        <p className="note rcp-capture-note">
          {t('warehouse.receipts.detail.expected')}: {line.expectedQty != null ? formatNumber(line.expectedQty, lang) : '—'} ·{' '}
          {t('warehouse.receipts.detail.received')}: {formatNumber(tracking === 'SERIAL' ? parseSerials(serialText).length : line.receivedQty, lang)} ·{' '}
          {t(`warehouse.receipts.tracking.${tracking}`)}
        </p>
        <div className="r3">
          <Field name="lot" label={t('warehouse.receipts.fields.lot')} required={tracking === 'LOT'}>
            <TextInput maxLength={60} />
          </Field>
          <Field name="lotManufactureDate" label={t('warehouse.receipts.fields.manufactureDate')}>
            <DateInput />
          </Field>
          <Field name="lotExpiryDate" label={t('warehouse.receipts.fields.expiryDate')}>
            <DateInput />
          </Field>
        </div>
        {tracking === 'SERIAL' && (
          <Field
            name="serialNumbers"
            label={t('warehouse.receipts.fields.serials')}
            help={`${t('warehouse.receipts.fields.serialsHelp')} ${t('warehouse.receipts.capture.serialQty', { count: parseSerials(serialText).length })}`}
            required
          >
            <TextArea rows={5} />
          </Field>
        )}
        <Field name="stagingBinId" label={t('warehouse.receipts.fields.stagingBin')}>
          <BinPickerInput warehousePublicId={header.warehousePublicId} zoneTypeCodes={RECEIVING_ZONES} placeholder={t('warehouse.receipts.defaultStaging')} />
        </Field>
      </Form>
    </Modal>
  )
}
