// Lote 13 — alta de un aviso de llegada (ASN) de un cliente 3PL (`POST /api/v1/asns`, warehouse.receive), extraído de la
// lista de Recibo: Almacén, Cliente dueño (`ClientPickerInput`, buscador), Referencia (máx. 80) y Llegada esperada; las
// líneas en una misma rejilla (Producto del dueño con buscador, Cantidad esperada > 0 con 3 decimales, Lote), hasta 200.
// El producto se busca solo entre los del cliente dueño (se elige primero). Mensajes exactos del manual 06 §4.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useT } from '../../kernel/i18n'
import { ClientPickerInput, DateInput, Field, Form, IconTrash, Modal, NumberInput, TextInput, toast } from '../../kernel/ui'
import { useSaveAsn } from './api'
import { decimalsOf } from './lineRules'
import { ProductPickerInput, WarehousePickerInput } from './pickers'

const MAX_LINES = 200

interface AsnLineForm {
  productPublicId: string | null
  expectedQty: number | null
  lotNumber: string
}
const EMPTY_ASN_LINE: AsnLineForm = { productPublicId: null, expectedQty: null, lotNumber: '' }

export function AsnCreateModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const save = useSaveAsn()
  const schema = useMemo(
    () =>
      z
        .object({
          warehousePublicId: z.string().nullable(),
          clientPublicId: z.string().nullable(),
          reference: z.string().max(80, t('warehouse.asns.errors.referenceMax')),
          expectedDate: z.string(),
          lines: z.array(
            z.object({
              productPublicId: z.string().nullable(),
              expectedQty: z.number().nullable(),
              lotNumber: z.string().max(60, t('warehouse.lineRules.lotTooLong')),
            }),
          ),
        })
        .superRefine((v, ctx) => {
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t('warehouse.receipts.errors.warehouseRequired') })
          if (!v.clientPublicId) ctx.addIssue({ code: 'custom', path: ['clientPublicId'], message: t('warehouse.asns.errors.clientRequired') })
          if (v.lines.length === 0) ctx.addIssue({ code: 'custom', path: ['reference'], message: t('warehouse.receipts.errors.linesRequired') })
          if (v.lines.length > MAX_LINES) ctx.addIssue({ code: 'custom', path: ['reference'], message: t('warehouse.asns.errors.tooManyLines') })
          v.lines.forEach((l, i) => {
            if (!l.productPublicId) ctx.addIssue({ code: 'custom', path: ['lines', i, 'productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
            if (l.expectedQty == null || l.expectedQty <= 0)
              ctx.addIssue({ code: 'custom', path: ['lines', i, 'expectedQty'], message: t('warehouse.asns.errors.expectedQtyPositive') })
            else if (decimalsOf(l.expectedQty) > 3) ctx.addIssue({ code: 'custom', path: ['lines', i, 'expectedQty'], message: t('warehouse.lineRules.qtyDecimals') })
          })
        }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { warehousePublicId: null, clientPublicId: null, reference: '', expectedDate: '', lines: [EMPTY_ASN_LINE] },
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const clientPublicId = useWatch({ control: form.control, name: 'clientPublicId' })
  const formId = 'asn-create'

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.asns.new')}
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
          await save.mutateAsync({
            action: 'create',
            body: {
              warehousePublicId: v.warehousePublicId,
              clientPublicId: v.clientPublicId,
              reference: v.reference.trim() || null,
              expectedDate: v.expectedDate || null,
              lines: v.lines.map((l) => ({ productPublicId: l.productPublicId, expectedQty: l.expectedQty, lotNumber: l.lotNumber.trim() || null })),
            },
          })
          toast.success(t('warehouse.asns.created'))
          onClose()
        }}
      >
        <div className="r2">
          <Field name="warehousePublicId" label={t('warehouse.receipts.fields.warehouse')} required>
            <WarehousePickerInput />
          </Field>
          <Field name="clientPublicId" label={t('warehouse.asns.fields.client')} required>
            <ClientPickerInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="reference" label={t('warehouse.asns.fields.reference')}>
            <TextInput maxLength={80} />
          </Field>
          <Field name="expectedDate" label={t('warehouse.asns.fields.expectedDate')}>
            <DateInput />
          </Field>
        </div>

        <p className="help">{t('warehouse.asns.lines.title')}</p>
        {/* una sola rejilla: encabezados arriba y una fila por línea (a 480 px cada campo con su etiqueta, uno debajo del otro) */}
        <div className="rcp-asn-grid" role="group" aria-label={t('warehouse.asns.lines.title')}>
          <div className="rcp-asn-head" aria-hidden="true">
            <span>{t('warehouse.asns.lines.product')}</span>
            <span>{t('warehouse.asns.lines.expectedQty')}</span>
            <span>{t('warehouse.asns.lines.lot')}</span>
            <span />
          </div>
          {fields.map((f, i) => (
            <div key={f.id} className="rcp-asn-row">
              <Field name={`lines.${i}.productPublicId`} label={t('warehouse.asns.lines.productOf', { n: i + 1 })} hideLabel required>
                <ProductPickerInput
                  ownerClientPublicId={clientPublicId}
                  disabled={!clientPublicId}
                  placeholder={clientPublicId ? undefined : t('warehouse.asns.pickClientFirst')}
                />
              </Field>
              <Field name={`lines.${i}.expectedQty`} label={t('warehouse.asns.lines.qtyOf', { n: i + 1 })} hideLabel required>
                <NumberInput min={0} step="0.001" />
              </Field>
              <Field name={`lines.${i}.lotNumber`} label={t('warehouse.asns.lines.lotOf', { n: i + 1 })} hideLabel>
                <TextInput maxLength={60} />
              </Field>
              <button
                type="button"
                className="rowbtn danger rcp-asn-del"
                aria-label={t('warehouse.asns.lines.remove', { n: i + 1 })}
                title={t('warehouse.asns.lines.remove', { n: i + 1 })}
                disabled={fields.length <= 1}
                onClick={() => remove(i)}
              >
                <IconTrash />
              </button>
            </div>
          ))}
        </div>
        <button type="button" className="btn sm" disabled={fields.length >= MAX_LINES} onClick={() => append(EMPTY_ASN_LINE)}>
          {t('warehouse.asns.lines.add')}
        </button>
      </Form>
    </Modal>
  )
}
