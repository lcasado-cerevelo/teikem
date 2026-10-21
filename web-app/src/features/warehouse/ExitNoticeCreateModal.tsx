// Lote 144 — alta de un aviso de salida (`POST /api/v1/exit-notices`, exitnotice.create): Almacén, Cliente dueño (`ClientPickerInput`), Referencia (máx. 80), Destino (texto libre, máx. 200),
// Fecha requerida y Notas (máx. 500); las líneas en una rejilla (Producto del dueño con buscador y Cantidad > 0 con 3 decimales), hasta 200. El producto se busca solo entre los del cliente
// dueño (se elige primero). El servidor reserva en el acto lo que haya con la rotación de la compañía y deja el resto pendiente (nunca rechaza por falta de existencia). Sin precio.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useT } from '../../kernel/i18n'
import { ClientPickerInput, DateInput, Field, Form, IconTrash, Modal, NumberInput, TextArea, TextInput, toast } from '../../kernel/ui'
import { useExitNoticeMutations } from './exitNoticeApi'
import { EXIT_NOTICE_MAX_LINES, exitQuantityIssue, type ExitLineDraft } from './exitNoticeRules'
import { ProductPickerInput, WarehousePickerInput } from './pickers'

const EMPTY_LINE: ExitLineDraft = { productPublicId: null, quantity: null }

export function ExitNoticeCreateModal({ onClose, onCreated }: { onClose: () => void; onCreated?: (publicId: string) => void }) {
  const t = useT()
  const { create } = useExitNoticeMutations()
  const schema = useMemo(
    () =>
      z
        .object({
          warehousePublicId: z.string().nullable(),
          clientPublicId: z.string().nullable(),
          reference: z.string().max(80, t('warehouse.exitNotices.errors.referenceMax')),
          destination: z.string().max(200, t('warehouse.exitNotices.errors.destinationMax')),
          requiredDate: z.string(),
          notes: z.string().max(500, t('warehouse.exitNotices.errors.notesMax')),
          lines: z.array(z.object({ productPublicId: z.string().nullable(), quantity: z.number().nullable() })),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (!v.clientPublicId) ctx.addIssue({ code: 'custom', path: ['clientPublicId'], message: t('warehouse.exitNotices.errors.clientRequired') })
          if (v.lines.length > EXIT_NOTICE_MAX_LINES) ctx.addIssue({ code: 'custom', path: ['reference'], message: t('warehouse.exitNotices.errors.tooManyLines') })
          v.lines.forEach((l, i) => {
            if (!l.productPublicId) ctx.addIssue({ code: 'custom', path: ['lines', i, 'productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
            const issue = exitQuantityIssue(l.quantity)
            if (issue) ctx.addIssue({ code: 'custom', path: ['lines', i, 'quantity'], message: t(issue) })
          })
        }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { warehousePublicId: null, clientPublicId: null, reference: '', destination: '', requiredDate: '', notes: '', lines: [EMPTY_LINE] },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const clientPublicId = useWatch({ control: form.control, name: 'clientPublicId' })
  const formId = 'exit-notice-create'

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.exitNotices.new')}
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
          const created = await create.mutateAsync({
            warehousePublicId: v.warehousePublicId,
            clientPublicId: v.clientPublicId,
            reference: v.reference.trim() || null,
            destination: v.destination.trim() || null,
            requiredDate: v.requiredDate || null,
            notes: v.notes.trim() || null,
            lines: v.lines.map((l) => ({ productPublicId: l.productPublicId, quantity: l.quantity })),
          })
          const pending = (created.lines ?? []).filter((l) => (l.qtyBackordered ?? 0) > 0).length
          toast.success(pending > 0 ? t('warehouse.exitNotices.createdPending', { number: created.number ?? '', n: pending }) : t('warehouse.exitNotices.created', { number: created.number ?? '' }))
          onCreated?.(created.publicId ?? '')
          onClose()
        }}
      >
        <div className="r2">
          <Field name="warehousePublicId" label={t('warehouse.receipts.fields.warehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="clientPublicId" label={t('warehouse.exitNotices.fields.client')} required help={t('warehouse.exitNotices.fields.clientHelp')}>
            <ClientPickerInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="reference" label={t('warehouse.exitNotices.fields.reference')}>
            <TextInput maxLength={80} />
          </Field>
          <Field name="requiredDate" label={t('warehouse.exitNotices.fields.requiredDate')}>
            <DateInput />
          </Field>
        </div>
        <Field name="destination" label={t('warehouse.exitNotices.fields.destination')} help={t('warehouse.exitNotices.fields.destinationHelp')}>
          <TextInput maxLength={200} />
        </Field>
        <Field name="notes" label={t('warehouse.exitNotices.fields.notes')}>
          <TextArea rows={2} maxLength={500} />
        </Field>

        <p className="help">{t('warehouse.exitNotices.lines.title')}</p>
        <div className="rcp-asn-grid rcp-exit-grid" role="group" aria-label={t('warehouse.exitNotices.lines.title')}>
          <div className="rcp-asn-head" aria-hidden="true">
            <span>{t('warehouse.exitNotices.lines.product')}</span>
            <span>{t('warehouse.exitNotices.lines.quantity')}</span>
            <span />
          </div>
          {fields.map((f, i) => (
            <div key={f.id} className="rcp-asn-row">
              <Field name={`lines.${i}.productPublicId`} label={t('warehouse.exitNotices.lines.productOf', { n: i + 1 })} hideLabel required>
                <ProductPickerInput
                  showAvailable
                  ownerClientPublicId={clientPublicId}
                  disabled={!clientPublicId}
                  placeholder={clientPublicId ? undefined : t('warehouse.exitNotices.pickClientFirst')}
                />
              </Field>
              <Field name={`lines.${i}.quantity`} label={t('warehouse.exitNotices.lines.qtyOf', { n: i + 1 })} hideLabel required>
                <NumberInput min={0} step="0.001" />
              </Field>
              <button
                type="button"
                className="rowbtn danger rcp-asn-del"
                aria-label={t('warehouse.exitNotices.lines.remove', { n: i + 1 })}
                title={t('warehouse.exitNotices.lines.remove', { n: i + 1 })}
                disabled={fields.length <= 1}
                onClick={() => remove(i)}
              >
                <IconTrash />
              </button>
            </div>
          ))}
        </div>
        <button type="button" className="btn sm" disabled={fields.length >= EXIT_NOTICE_MAX_LINES} onClick={() => append(EMPTY_LINE)}>
          {t('warehouse.exitNotices.lines.add')}
        </button>
        <p className="help">{t('warehouse.exitNotices.createHelp')}</p>
      </Form>
    </Modal>
  )
}
