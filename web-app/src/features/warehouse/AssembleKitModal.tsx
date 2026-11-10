// Lote 232 — «Armar kits» de un kit ARMADO (`POST /products/{id}/assembled-kit/assemblies`, `inventory.adjust`). Dos pasos en el mismo modal:
// 1) cantidad (entera, 1 a 100,000), almacén y posición donde quedan los kits («se pueden armar N» del servidor para ese almacén) y, por pieza, de
//    dónde sale: por omisión la sugerida por la rotación del servidor (rank 1 de GET /inventory/exit-options) o una posición/lote escogidos;
// 2) resumen antes de confirmar: piezas que salen (por kit, total y de dónde), kits que entran y la nota del costo (se calcula al armar).
// Los errores del servidor salen tal cual (p. ej. «El lote A2 de A solo alcanza para 2 kit(s)…») en el aviso de arriba del formulario.
// Armar saca las piezas del inventario y mete los kits: no queda nada «reservado».
import { zodResolver } from '@hookform/resolvers/zod'
import { useId, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, Spinner, TextArea, toast } from '../../kernel/ui'
import { useWarehouseBins } from './api'
import { useAssembleKit, useAssembledKit, usePiecesExitOptions } from './assembledKitApi'
import {
  AUTO_CHOICE,
  KIT_EXCLUDED_ZONE_TYPES,
  assembleRequest,
  assembleSummary,
  choiceKey,
  exitOptionsOf,
  kitQuantityProblem,
  type StockExitOptionDto,
} from './assembledKitRules'
import { BinPickerInput, WarehousePickerInput } from './pickers'
import './assembledKit.css'

const A = 'warehouse.products.assembled'
const NOTES_MAX = 300
const NO_CHOICES: Record<string, string> = {}

export interface AssembleKitModalProps {
  open: boolean
  publicId: string
  sku: string
  /** Almacén y posición con que arranca (los por defecto del producto). */
  defaultWarehousePublicId?: string | null
  defaultBinId?: number | null
  onClose: () => void
}

export function AssembleKitModal(props: AssembleKitModalProps) {
  if (!props.open) return null
  return <AssembleKitDialog {...props} />
}

function AssembleKitDialog({ publicId, sku, defaultWarehousePublicId, defaultBinId, onClose }: AssembleKitModalProps) {
  const t = useT()
  const f = useFormat()
  const formId = useId()
  const kit = useAssembledKit(publicId)
  const assemble = useAssembleKit()
  const [step, setStep] = useState<'form' | 'review'>('form')
  // de dónde sale cada pieza, por almacén: en otro almacén (otras posiciones y lotes) lo escogido ya no aplica
  const [picked, setPicked] = useState<{ warehouse: string | null; map: Record<string, string> }>({ warehouse: null, map: {} })

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
          if (!v.warehousePublicId) ctx.addIssue({ code: 'custom', path: ['warehousePublicId'], message: t(`${A}.err.warehouseRequired`) })
          if (!v.toBinId) ctx.addIssue({ code: 'custom', path: ['toBinId'], message: t(`${A}.err.toBinRequired`) })
        }),
    [t],
  )
  const firstAvailable = kit.data?.availability?.find((a) => (a.canAssemble ?? 0) > 0)?.warehousePublicId
  const singleWarehouse = kit.data?.availability?.length === 1 ? kit.data.availability[0].warehousePublicId : undefined
  const initialWarehouse = defaultWarehousePublicId ?? firstAvailable ?? singleWarehouse ?? null
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      quantity: null as number | null,
      warehousePublicId: initialWarehouse,
      toBinId: initialWarehouse && initialWarehouse === defaultWarehousePublicId && defaultBinId != null ? String(defaultBinId) : '',
      notes: '',
    },
    resetOptions: { keepDirtyValues: true },
  })
  const [quantity, warehousePublicId, toBinId] = useWatch({ control: form.control, name: ['quantity', 'warehousePublicId', 'toBinId'] })
  const [pickedBinCode, setPickedBinCode] = useState<{ id: string; code: string } | null>(null)
  // código de la posición destino para el resumen: la escogida en el buscador o, si llegó por omisión, la de la consulta por id
  const needBin = Boolean(warehousePublicId && toBinId) && pickedBinCode?.id !== toBinId
  const binQuery = useWarehouseBins(warehousePublicId, { binIds: needBin ? [Number(toBinId)] : undefined, take: 1 }, { enabled: needBin, handleAccessDenied: false })
  const toBinCode = pickedBinCode?.id === toBinId ? pickedBinCode.code : (binQuery.data?.items?.[0]?.code ?? null)
  const choices = picked.warehouse === warehousePublicId ? picked.map : NO_CHOICES
  const setChoice = (productPublicId: string, key: string) => setPicked({ warehouse: warehousePublicId, map: { ...choices, [productPublicId]: key } })
  const pieces = useMemo(() => kit.data?.pieces ?? [], [kit.data])
  const exit = usePiecesExitOptions(
    warehousePublicId,
    pieces.map((p) => p.productPublicId ?? '').filter(Boolean),
    { handleAccessDenied: false },
  )
  const rows: StockExitOptionDto[] = exit.data?.items ?? []
  const availability = kit.data?.availability?.find((a) => a.warehousePublicId === warehousePublicId)
  const submitting = form.formState.isSubmitting

  async function submit(v: z.output<typeof schema>) {
    if (step === 'form') {
      setStep('review')
      return
    }
    const result = await assemble.mutateAsync({
      publicId,
      body: assembleRequest({ quantity: v.quantity, toBinId: v.toBinId, warehousePublicId: v.warehousePublicId, notes: v.notes, choices }),
    })
    const a = result.assembly
    const vars = { number: a?.number ?? '', n: f.number(a?.quantity ?? v.quantity ?? 0), bin: a?.toBinCode ?? toBinCode ?? '' }
    toast.success(a?.totalCost != null ? t(`${A}.assembledCost`, { ...vars, cost: f.money(a.totalCost) }) : t(`${A}.assembled`, vars))
    onClose()
  }

  const title = t(`${A}.assembleTitle`, { sku })
  if (kit.isLoading) {
    return (
      <Modal open title={title} size="lg" onClose={onClose}>
        <Spinner block />
      </Modal>
    )
  }

  const kits = quantity ?? 0
  const summary = assembleSummary(pieces, kits, rows, choices)
  const footer = (
    <div className="ak-actions">
      {step === 'review' ? (
        <button type="button" className="btn" onClick={() => setStep('form')} disabled={submitting}>
          {t(`${A}.back`)}
        </button>
      ) : (
        <button type="button" className="btn" onClick={onClose} disabled={submitting}>
          {t('common.cancel')}
        </button>
      )}
      <button type="submit" form={formId} className="btn flow" disabled={submitting}>
        {submitting ? t('common.loading') : step === 'review' ? t(`${A}.confirm`, { n: f.number(kits) }) : t(`${A}.review`)}
      </button>
    </div>
  )

  return (
    <Modal open title={title} size="lg" onClose={onClose} dismissible={!submitting} footer={footer}>
      <Form id={formId} form={form} onSubmit={submit}>
        <p className="ak-explain">{t(`${A}.assembleExplain`)}</p>
        {step === 'form' ? (
          <>
            <div className="ak-grid">
              <Field name="quantity" label={t(`${A}.quantity`)} required>
                <NumberInput className="mono" min={1} step={1} />
              </Field>
              <Field name="warehousePublicId" label={t(`${A}.warehouse`)} required>
                <WarehousePickerInput placeholder={null} />
              </Field>
              <Field name="toBinId" label={t(`${A}.toBin`)} required>
                <BinPickerInput warehousePublicId={warehousePublicId} excludeZoneTypeCodes={KIT_EXCLUDED_ZONE_TYPES} onPicked={(b) => setPickedBinCode(b && b.id != null ? { id: String(b.id), code: b.code ?? '' } : null)} />
              </Field>
            </div>
            {availability && (
              <p className="ak-can" role="status">
                <strong>{t(`${A}.canAssemble`, { n: f.number(availability.canAssemble ?? 0) })}</strong>
                {(availability.canAssembleTotal ?? 0) > (availability.canAssemble ?? 0) && <> {t(`${A}.canAssembleTotal`, { n: f.number(availability.canAssembleTotal ?? 0) })}</>}
              </p>
            )}
            <h3 className="ak-sub">{t(`${A}.piecesFrom`)}</h3>
            <p className="help">{t(`${A}.piecesFromHelp`)}</p>
            <div className="ak-pieces">
              {pieces.map((p) => {
                const id = p.productPublicId ?? ''
                const options = exitOptionsOf(rows, id)
                const selectId = `${formId}-from-${id}`
                return (
                  <div className="f" key={id}>
                    <label htmlFor={selectId}>
                      {t(`${A}.pieceFrom`, { sku: p.sku ?? '' })} <span className="pe-faint">({p.name} × {f.number(p.quantity ?? 0)})</span>
                    </label>
                    <select
                      id={selectId}
                      value={choices[id] ?? AUTO_CHOICE}
                      disabled={!warehousePublicId}
                      onChange={(e) => setChoice(id, e.target.value)}
                    >
                      <option value={AUTO_CHOICE}>
                        {!warehousePublicId
                          ? t(`${A}.chooseWarehouse`)
                          : options.length === 0
                            ? exit.isLoading
                              ? t('common.loading')
                              : t(`${A}.noStock`)
                            : t(`${A}.autoWith`, { where: optionText(options[0], t, f) })}
                      </option>
                      {options.map((o) => (
                        <option key={choiceKey(o.binId, o.lotId)} value={choiceKey(o.binId, o.lotId)}>
                          {optionText(o, t, f)}
                        </option>
                      ))}
                    </select>
                  </div>
                )
              })}
            </div>
            <Field name="notes" label={t(`${A}.notes`)}>
              <TextArea rows={2} maxLength={NOTES_MAX} />
            </Field>
          </>
        ) : (
          <section aria-labelledby={`${formId}-sum`}>
            <h3 id={`${formId}-sum`} className="ak-sub">
              {t(`${A}.summaryTitle`)}
            </h3>
            <p className="ak-sub">{t(`${A}.summaryOut`)}</p>
            <table className="ak-sum">
              <thead>
                <tr>
                  <th scope="col">{t(`${A}.colPiece`)}</th>
                  <th scope="col" className="num ak-hide-sm">
                    {t(`${A}.colPerKit`)}
                  </th>
                  <th scope="col" className="num">
                    {t(`${A}.colTotal`)}
                  </th>
                  <th scope="col">{t(`${A}.colFrom`)}</th>
                </tr>
              </thead>
              <tbody>
                {summary.map((r) => (
                  <tr key={r.productPublicId}>
                    <td>
                      <span className="ref">{r.sku}</span>
                      <span className="sub">{r.name}</span>
                    </td>
                    <td className="num ak-hide-sm">{f.number(r.qtyPerKit)}</td>
                    <td className="num">{f.number(r.total)}</td>
                    <td>
                      {r.binCode ? (r.lotNumber ? t(`${A}.where`, { bin: r.binCode, lot: r.lotNumber }) : r.binCode) : '—'}
                      <span className="sub">{t(r.suggested ? `${A}.suggested` : `${A}.chosen`)}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="ak-can">
              <strong>{t(`${A}.summaryIn`, { n: f.number(kits), bin: toBinCode ?? toBinId })}</strong>
            </p>
            <p className="help">{t(`${A}.summaryCost`)}</p>
          </section>
        )}
      </Form>
    </Modal>
  )
}

/** «A-01 · lote L1 · vence 2027-01-31 · disp. 8» */
function optionText(o: StockExitOptionDto, t: ReturnType<typeof useT>, f: ReturnType<typeof useFormat>): string {
  return [
    o.lotNumber ? t(`${A}.where`, { bin: o.binCode ?? '', lot: o.lotNumber }) : (o.binCode ?? ''),
    o.expiryDate ? t(`${A}.expires`, { date: f.date(o.expiryDate) }) : null,
    t(`${A}.available`, { qty: f.number(o.available ?? 0) }),
  ]
    .filter(Boolean)
    .join(' · ')
}
