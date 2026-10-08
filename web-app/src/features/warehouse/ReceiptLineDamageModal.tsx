// Daño declarado en una línea del recibo abierto (2026-10-08): «de lo recibido, vinieron N unidades dañadas». Cantidad dañada (no mayor que lo recibido), razón
// del catálogo (si es «Otra», se escribe) y qué se hace con ellas: dejarlas en una posición (vacía = la primera de cuarentena del almacén; si no hay, donde
// aterrice la línea) o desecharlas de una vez. Con PUT /receipts/{publicId}/lines/{lineId} (warehouse.receive). El reporte DAN-##### lo crea el servidor al
// confirmar el recibo. «Quitar daño» deja la línea sin unidades dañadas.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import type { components } from '../../kernel/api/schema'
import { useLookups } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { ComboSelectInput, Field, Form, Modal, NumberInput, TextInput, toast } from '../../kernel/ui'
import { productLabel, useSaveReceiptLine, type ReceiptDetailDto } from './api'
import { formatNumber } from './lineRules'
import { BinPickerInput } from './pickers'
import { SegInput } from './SegInput'

type LineDto = components['schemas']['ReceiptLineDto']

const M = 'warehouse.receipts.damage'

interface Values {
  quantity: number | null
  cause: string
  note: string
  disposition: 'PLACE' | 'DISCARD'
  binId: string
}

export interface ReceiptLineDamageModalProps {
  receipt: ReceiptDetailDto
  line: LineDto
  onClose: () => void
  onSaved?: (receipt: ReceiptDetailDto, line: LineDto | null) => void
}

export function ReceiptLineDamageModal({ receipt, line, onClose, onSaved }: ReceiptLineDamageModalProps) {
  const t = useT()
  const lang = useLang()
  const save = useSaveReceiptLine()
  const causes = useLookups('DamageCause')
  const publicId = receipt.header?.publicId ?? ''
  const received = line.receivedQty ?? 0
  const hasDamage = (line.damagedQty ?? 0) > 0
  const formId = `receipt-line-damage-${line.id ?? 0}`

  const schema = useMemo(
    () =>
      z
        .object({
          quantity: z.number(t(`${M}.errors.qtyInvalid`)).nullable().refine((v) => v !== null && v > 0, t(`${M}.errors.qtyPositive`)),
          cause: z.string().min(1, t(`${M}.errors.causeRequired`)),
          note: z.string().max(300, t(`${M}.errors.noteMax`)),
          disposition: z.enum(['PLACE', 'DISCARD']),
          binId: z.string(),
        })
        .superRefine((v, ctx) => {
          if (v.quantity !== null && v.quantity > received) ctx.addIssue({ code: 'custom', path: ['quantity'], message: t(`${M}.errors.qtyMax`, { max: formatNumber(received, lang) }) })
          if (v.cause === 'OTHER' && !v.note.trim()) ctx.addIssue({ code: 'custom', path: ['note'], message: t(`${M}.errors.noteRequired`) })
        }),
    [t, lang, received],
  )
  const form = useForm<Values>({
    resolver: zodResolver(schema) as never,
    defaultValues: {
      quantity: hasDamage ? (line.damagedQty ?? null) : null,
      cause: line.damageCauseCode ?? '',
      note: line.damageNote ?? '',
      disposition: line.damageDiscard ? 'DISCARD' : 'PLACE',
      binId: line.damageBinId != null ? String(line.damageBinId) : '',
    },
  })
  const [cause, disposition] = useWatch({ control: form.control, name: ['cause', 'disposition'] })
  const causeOptions = useMemo(() => (causes.data ?? []).map((c) => ({ value: c.code, label: c.label })), [causes.data])
  const dispositionOptions = [
    { value: 'PLACE', label: t(`${M}.place`) },
    { value: 'DISCARD', label: t(`${M}.discard`) },
  ]
  const busy = form.formState.isSubmitting

  async function persist(body: components['schemas']['ReceiptLineUpdateRequest'], doneKey: string) {
    const dto = await save.mutateAsync({ publicId, action: 'update', lineId: line.id ?? 0, body })
    toast.success(t(doneKey))
    onSaved?.(dto, (dto.lines ?? []).find((l) => l.id === line.id) ?? null)
    onClose()
  }

  return (
    <Modal
      open
      title={t(`${M}.title`, { product: productLabel({ sku: line.sku, name: line.productName }) })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          {hasDamage && (
            <button type="button" className="btn" disabled={busy} onClick={() => void persist({ clearDamage: true }, `${M}.cleared`)}>
              {t(`${M}.clear`)}
            </button>
          )}
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={(v) =>
          persist(
            {
              damagedQty: v.quantity,
              damageCause: v.cause,
              damageNote: v.note.trim() || null,
              damageBinId: v.disposition === 'PLACE' && v.binId ? Number(v.binId) : null,
              damageDiscard: v.disposition === 'DISCARD',
            },
            `${M}.saved`,
          )
        }
      >
        <p className="note rcp-capture-note">{t(`${M}.intro`, { received: formatNumber(received, lang) })}</p>
        <div className="r2">
          <Field name="quantity" label={t(`${M}.quantity`)} required>
            <NumberInput className="mono" step="0.001" min={0} />
          </Field>
          <Field name="cause" label={t(`${M}.cause`)} required>
            <ComboSelectInput options={causeOptions} loading={causes.isLoading} placeholder={t(`${M}.causePlaceholder`)} />
          </Field>
        </div>
        {cause === 'OTHER' && (
          <Field name="note" label={t(`${M}.note`)} required>
            <TextInput maxLength={300} />
          </Field>
        )}
        <Field name="disposition" label={t(`${M}.disposition`)} help={t(disposition === 'DISCARD' ? `${M}.discardHelp` : `${M}.placeHelp`)}>
          <SegInput label={t(`${M}.disposition`)} options={dispositionOptions} />
        </Field>
        {disposition === 'PLACE' && (
          <Field name="binId" label={t(`${M}.bin`)}>
            <BinPickerInput warehousePublicId={receipt.header?.warehousePublicId} placeholder={t(`${M}.binPlaceholder`)} />
          </Field>
        )}
      </Form>
    </Modal>
  )
}
