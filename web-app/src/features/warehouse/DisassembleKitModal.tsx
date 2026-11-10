// Lote 232 — «Desarmar» kits de UN armado concreto del historial (`POST /products/{id}/assembled-kit/assemblies/{assemblyId}/disassemble`,
// `inventory.adjust`). Cantidad (entera; hasta lo que queda del armado; parcial permitido), almacén donde están los kits (por omisión el del armado)
// y posición donde vuelven las piezas (vacía = cada una a la posición de donde salió; obligatoria si los kits están en otro almacén). El resumen
// (kits que salen y piezas que vuelven, cada una a su mismo lote) se ve antes de confirmar. Solo se desarman kits disponibles: lo reservado por una
// orden o ya despachado lo rechaza el servidor con su mensaje, que se muestra tal cual.
import { zodResolver } from '@hookform/resolvers/zod'
import { useId, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, TextArea, toast } from '../../kernel/ui'
import { useDisassembleKit } from './assembledKitApi'
import { KIT_EXCLUDED_ZONE_TYPES, disassembleRequest, disassembleReturns, kitQuantityProblem, type KitAssemblyDto } from './assembledKitRules'
import { BinPickerInput, WarehousePickerInput } from './pickers'
import './assembledKit.css'

const A = 'warehouse.products.assembled'
const NOTES_MAX = 300

export interface DisassembleKitModalProps {
  publicId: string
  /** El armado del historial (null = cerrado). */
  assembly: KitAssemblyDto | null
  onClose: () => void
}

export function DisassembleKitModal({ publicId, assembly, onClose }: DisassembleKitModalProps) {
  if (!assembly) return null
  return <DisassembleKitDialog key={assembly.publicId} publicId={publicId} assembly={assembly} onClose={onClose} />
}

function DisassembleKitDialog({ publicId, assembly, onClose }: { publicId: string; assembly: KitAssemblyDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const formId = useId()
  const disassemble = useDisassembleKit()
  const remaining = assembly.remaining ?? 0
  const number = assembly.number ?? ''
  const [toBinCode, setToBinCode] = useState<string | null>(null)

  const schema = useMemo(
    () =>
      z
        .object({
          quantity: z.number(t(`${A}.err.kitQtyInvalid`)).nullable(),
          warehousePublicId: z.string().nullable(),
          toBinId: z.string(),
          notes: z.string().max(NOTES_MAX, t(`${A}.err.notesMax`)),
        })
        .superRefine((v, ctx) => {
          const qp = kitQuantityProblem(v.quantity)
          if (qp) ctx.addIssue({ code: 'custom', path: ['quantity'], message: t(`${A}.err.${qp}`) })
          else if ((v.quantity ?? 0) > remaining)
            ctx.addIssue({ code: 'custom', path: ['quantity'], message: t(`${A}.err.remainingExceeded`, { number, remaining: f.number(remaining), qty: f.number(v.quantity ?? 0) }) })
          const otherWarehouse = Boolean(v.warehousePublicId) && v.warehousePublicId !== assembly.warehousePublicId
          if (otherWarehouse && !v.toBinId) ctx.addIssue({ code: 'custom', path: ['toBinId'], message: t(`${A}.err.returnBinRequired`) })
        }),
    [t, f, remaining, number, assembly.warehousePublicId],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { quantity: remaining > 0 ? remaining : null, warehousePublicId: assembly.warehousePublicId ?? null, toBinId: '', notes: '' },
  })
  const [quantity, warehousePublicId, toBinId] = useWatch({ control: form.control, name: ['quantity', 'warehousePublicId', 'toBinId'] })
  // lo escrito (si es un entero válido) va en el botón; el resumen nunca pasa de lo que queda del armado
  const typed = quantity != null && quantity > 0 && Number.isInteger(quantity) ? quantity : 0
  const kits = Math.min(typed, remaining)
  const returns = disassembleReturns(assembly, kits, toBinId ? toBinCode : null)
  const submitting = form.formState.isSubmitting

  async function submit(v: z.output<typeof schema>) {
    const result = await disassemble.mutateAsync({
      publicId,
      assemblyPublicId: assembly.publicId ?? '',
      body: disassembleRequest({ quantity: v.quantity, warehousePublicId: v.warehousePublicId, assemblyWarehousePublicId: assembly.warehousePublicId, toBinId: v.toBinId, notes: v.notes }),
    })
    toast.success(t(`${A}.disassembled`, { number: result.disassembly?.number ?? '', n: f.number(result.disassembly?.quantity ?? v.quantity ?? 0) }))
    onClose()
  }

  const footer = (
    <div className="ak-actions">
      <button type="button" className="btn" onClick={onClose} disabled={submitting}>
        {t('common.cancel')}
      </button>
      <button type="submit" form={formId} className="btn flow" disabled={submitting || remaining <= 0}>
        {submitting ? t('common.loading') : t(`${A}.confirmDisassemble`, { n: f.number(typed) })}
      </button>
    </div>
  )

  return (
    <Modal open title={t(`${A}.disassembleTitle`, { number })} size="lg" onClose={onClose} dismissible={!submitting} footer={footer}>
      <Form id={formId} form={form} onSubmit={submit}>
        <p className="ak-explain">{t(`${A}.disassembleExplain`)}</p>
        <p className="ak-can">
          <strong>{t(`${A}.remainingInfo`, { n: f.number(remaining) })}</strong>
          {assembly.kitLotNumber && <> · {t(`${A}.col.lot`)}: <span className="ref">{assembly.kitLotNumber}</span></>}
        </p>
        <div className="ak-grid">
          <Field name="quantity" label={t(`${A}.quantity`)} required>
            <NumberInput className="mono" min={1} max={remaining} step={1} />
          </Field>
          <Field name="warehousePublicId" label={t(`${A}.kitsWarehouse`)}>
            <WarehousePickerInput placeholder={null} />
          </Field>
          <Field name="toBinId" label={t(`${A}.returnBin`)}>
            <BinPickerInput
              warehousePublicId={warehousePublicId}
              excludeZoneTypeCodes={KIT_EXCLUDED_ZONE_TYPES}
              placeholder={t(`${A}.returnBinPlaceholder`)}
              onPicked={(b) => setToBinCode(b?.code ?? null)}
            />
          </Field>
        </div>
        <section aria-labelledby={`${formId}-sum`}>
          <h3 id={`${formId}-sum`} className="ak-sub">
            {t(`${A}.summaryTitle`)}
          </h3>
          <p className="ak-can">{t(`${A}.summaryKitsOut`, { n: f.number(kits) })}</p>
          <p className="ak-sub">{t(`${A}.summaryBack`)}</p>
          <table className="ak-sum">
            <thead>
              <tr>
                <th scope="col">{t(`${A}.colPiece`)}</th>
                <th scope="col">{t(`${A}.col.lot`)}</th>
                <th scope="col" className="num">
                  {t(`${A}.colBack`)}
                </th>
                <th scope="col">{t(`${A}.colTo`)}</th>
              </tr>
            </thead>
            <tbody>
              {returns.map((r, i) => (
                <tr key={`${r.productPublicId}-${i}`}>
                  <td>
                    <span className="ref">{r.sku}</span>
                    <span className="sub">{r.name}</span>
                  </td>
                  <td>{r.lotNumber ?? '—'}</td>
                  <td className="num">{f.number(r.quantity)}</td>
                  <td>{r.binCode || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
        <Field name="notes" label={t(`${A}.notes`)}>
          <TextArea rows={2} maxLength={NOTES_MAX} />
        </Field>
      </Form>
    </Modal>
  )
}
