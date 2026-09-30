// Pieza "Recolección y empaque" (Lote 13) — panel izquierdo "Recolección" de `PickBatchListScreen` (maqueta `picking()`;
// antes era el modal "Nueva recolección"). Solo con warehouse.pick (la pantalla no lo monta sin él).
// Almacén arriba (si la compañía tiene un solo almacén activo, ya elegido) y una rejilla de líneas en `DataTable`
// (`pagination={false}` y `exportable={false}`: filas con controles editables; tarjetas si el panel mide menos de 560 px):
// Producto (`ProductPickerInput` del almacén con existencia disponible y del mismo dueño que las demás filas; pista "N disp."),
// Cantidad, Posición (`BinPickerInput` con existencia; las posiciones FEFO del producto van primero y la primera se anuncia
// como "FEFO: …"; vacía = el servidor elige por FEFO), Lote (solo LOT/SERIAL) y Series (solo SERIAL: botón "Series (n)" que
// abre un modal), papelera. Al elegir producto en la última fila se agrega otra vacía (hasta 100 líneas); "Añadir línea"
// también agrega una. "Recolectar (bajar de inventario)" graba TODO en un solo POST /api/v1/pick-batches (sin las filas
// vacías; los errores del servidor vuelven a su fila) y "Limpiar" deja una fila vacía. Al grabar: toast, líneas limpias
// (se queda el almacén) y `onCollected` (la lista la resalta); no navega a la ficha. Lógica pura en `collectForm.ts`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { useController, useFieldArray, useForm, useFormContext, useWatch } from 'react-hook-form'
import { useLang, useT } from '../../kernel/i18n'
import {
  ComboSelectInput,
  DataTable,
  Field,
  Form,
  IconBasket,
  IconTrash,
  Modal,
  NumberInput,
  Panel,
  toast,
  useElementWidth,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { useCreatePickBatch, useInventoryBalances, useProductLots, useWarehouses, type PickBatchDto, type ProductListItemDto } from './api'
import {
  buildCollectBody,
  collectSchema,
  EMPTY_COLLECT_LINE,
  fefoAvailable,
  fefoBinSuggestions,
  fefoCandidates,
  isBlankLine,
  MAX_PICK_LINES,
  needsTrailingBlank,
  OWN,
  ownerFilterFor,
  remapCollectErrors,
  type CollectFormValues,
  type CollectLine,
} from './collectForm'
import { formatNumber, parseSerials, type LineIssue } from './lineRules'
import { BinPickerInput, ProductPickerInput, WarehousePickerInput } from './pickers'
import './warehouse.css'

/** Bajo este ancho del panel la rejilla pasa a tarjetas. */
const CARDS_BELOW_PX = 560
const FORM_ID = 'pick-batch-collect'

interface GridRow {
  key: string
  index: number
}

const isLotTracked = (code: string | null | undefined) => code === 'LOT' || code === 'SERIAL'

/** Saldos disponibles del producto en el almacén (misma consulta para la pista "N disp." y el FEFO de la posición). */
function useLineStock(warehousePublicId: string | null, productPublicId: string | null) {
  const q = useInventoryBalances(
    {
      warehousePublicIds: warehousePublicId ? [warehousePublicId] : undefined,
      productPublicIds: productPublicId ? [productPublicId] : undefined,
      onlyAvailable: true,
      take: 200,
    },
    { enabled: Boolean(warehousePublicId && productPublicId), handleAccessDenied: false },
  )
  // mientras llega la del producto nuevo, keepPreviousData mostraría la del anterior: se ignora
  return q.isPlaceholderData || !warehousePublicId || !productPublicId ? undefined : q.data?.items ?? undefined
}

// =====================================================================================================================
// Celdas de la rejilla (cada una lee su línea del formulario)
// =====================================================================================================================
function ProductCell({ index, warehousePublicId, onPicked }: { index: number; warehousePublicId: string | null; onPicked: (index: number, p: ProductListItemDto | null) => void }) {
  const t = useT()
  const lang = useLang()
  const lines = useWatch({ name: 'lines' }) as CollectLine[] | undefined
  const line = lines?.[index]
  const owner = ownerFilterFor(lines ?? [], index)
  const stock = useLineStock(warehousePublicId, line?.productPublicId ?? null)
  const available = line?.productPublicId ? (stock ? fefoAvailable(stock) : line.available) : null
  return (
    <Field
      name={`lines.${index}.productPublicId`}
      label={t('warehouse.pickBatches.collectPanel.lineProduct', { n: index + 1 })}
      hideLabel
      help={available != null ? t('warehouse.pickBatches.collectPanel.available', { qty: formatNumber(available, lang) }) : undefined}
    >
      <ProductPickerInput
        warehousePublicId={warehousePublicId}
        onlyAvailable
        ownOnly={owner.ownOnly}
        ownerClientPublicId={owner.ownerClientPublicId}
        disabled={!warehousePublicId}
        placeholder={warehousePublicId ? undefined : t('warehouse.pickBatches.pickWarehouseFirst')}
        onPicked={(p) => onPicked(index, p)}
      />
    </Field>
  )
}

function QuantityCell({ index }: { index: number }) {
  const t = useT()
  const tracking = useWatch({ name: `lines.${index}.trackingTypeCode` }) as string
  return (
    <Field name={`lines.${index}.quantity`} label={t('warehouse.pickBatches.collectPanel.lineQty', { n: index + 1 })} hideLabel>
      <NumberInput className="collect-qty" min={0} step={tracking === 'SERIAL' ? '1' : '0.001'} />
    </Field>
  )
}

function BinCell({ index, warehousePublicId }: { index: number; warehousePublicId: string | null }) {
  const t = useT()
  const productPublicId = useWatch({ name: `lines.${index}.productPublicId` }) as string | null
  const lotId = useWatch({ name: `lines.${index}.lotId` }) as string
  const binId = useWatch({ name: `lines.${index}.binId` }) as string
  const stock = useLineStock(warehousePublicId, productPublicId)
  const lot = lotId ? Number(lotId) : null
  const suggested = useMemo(() => (stock ? fefoBinSuggestions(stock, lot) : []), [stock, lot])
  const first = stock && !binId ? fefoCandidates(stock, lot)[0] : undefined
  const hint = first ? t('warehouse.pickBatches.collectPanel.fefoHint', { bin: [first.binCode, first.lotNumber].filter(Boolean).join(' · ') }) : undefined
  return (
    <Field name={`lines.${index}.binId`} label={t('warehouse.pickBatches.collectPanel.lineBin', { n: index + 1 })} hideLabel help={hint}>
      {/* vacío = el servidor elige por FEFO; solo posiciones con existencias; al cambiar de almacén se quita sola */}
      <BinPickerInput warehousePublicId={warehousePublicId} onlyWithStock suggestedBinIds={suggested} placeholder={t('warehouse.pickBatches.fefo')} />
    </Field>
  )
}

function LotCell({ index }: { index: number }) {
  const t = useT()
  const productPublicId = useWatch({ name: `lines.${index}.productPublicId` }) as string | null
  const tracking = useWatch({ name: `lines.${index}.trackingTypeCode` }) as string
  const tracked = isLotTracked(tracking)
  const lots = useProductLots(productPublicId, { enabled: tracked, handleAccessDenied: false })
  const options = useMemo(
    () =>
      (lots.data ?? [])
        .filter((l) => l.isActive !== false && (l.qtyOnHand ?? 0) > 0)
        .map((l) => ({ value: String(l.id), label: l.lotNumber ?? String(l.id), hint: l.expiryDate ?? undefined })),
    [lots.data],
  )
  if (!tracked) return <span className="collect-na">—</span>
  return (
    <Field name={`lines.${index}.lotId`} label={t('warehouse.pickBatches.collectPanel.lineLot', { n: index + 1 })} hideLabel>
      <ComboSelectInput options={options} loading={lots.isLoading} placeholder={t('warehouse.pickBatches.fefo')} />
    </Field>
  )
}

/** Botón "Series (n)" dentro de un <Field name="lines.i.serialNumbers">: abre un modal con las series (una por renglón). */
function SerialsButton({ index }: { index: number }) {
  const t = useT()
  const info = useFieldInfo('SerialsButton')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  const sku = useWatch({ name: `lines.${index}.sku` }) as string
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState('')
  const areaId = useId()
  const helpId = `${areaId}-help`
  const value = (field.value as string | undefined) ?? ''
  const n = parseSerials(value).length
  return (
    <>
      <button
        type="button"
        id={info.id}
        className="btn sm collect-serials"
        aria-invalid={info.invalid || undefined}
        aria-describedby={info.describedBy}
        onClick={() => {
          setDraft(value)
          setOpen(true)
        }}
      >
        {t('warehouse.pickBatches.collectPanel.serialsButton', { n })}
      </button>
      <Modal
        open={open}
        title={t('warehouse.pickBatches.collectPanel.serialsTitle', { n: index + 1, sku })}
        onClose={() => setOpen(false)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setOpen(false)}>
              {t('common.cancel')}
            </button>
            <button
              type="button"
              className="btn flow"
              onClick={() => {
                field.onChange(draft)
                field.onBlur()
                setOpen(false)
              }}
            >
              {t('common.done')}
            </button>
          </>
        }
      >
        <div className="f">
          <label htmlFor={areaId}>{t('warehouse.pickBatches.fields.serials')}</label>
          <textarea id={areaId} rows={8} value={draft} aria-describedby={helpId} onChange={(e) => setDraft(e.target.value)} />
          <p id={helpId} className="help">
            {t('warehouse.receipts.fields.serialsHelp')} {t('warehouse.pickBatches.collectPanel.serialsCount', { n: parseSerials(draft).length })}
          </p>
        </div>
      </Modal>
    </>
  )
}

function SerialsCell({ index }: { index: number }) {
  const t = useT()
  const tracking = useWatch({ name: `lines.${index}.trackingTypeCode` }) as string
  if (tracking !== 'SERIAL') return <span className="collect-na">—</span>
  return (
    <Field name={`lines.${index}.serialNumbers`} label={t('warehouse.pickBatches.collectPanel.lineSerials', { n: index + 1 })} hideLabel>
      <SerialsButton index={index} />
    </Field>
  )
}

// =====================================================================================================================
// Panel
// =====================================================================================================================
export interface CollectPanelProps {
  /** Recolección recién creada (la lista la resalta). */
  onCollected?: (batch: PickBatchDto) => void
}

export function CollectPanel({ onCollected }: CollectPanelProps) {
  const t = useT()
  const create = useCreatePickBatch()
  const boxRef = useRef<HTMLDivElement>(null)
  const width = useElementWidth(boxRef)
  const issueText = useCallback((i: LineIssue) => t(`warehouse.lineRules.${i.code}`, i.params), [t])
  const schema = useMemo(() => collectSchema(t, issueText), [t, issueText])
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { warehousePublicId: null, lines: [EMPTY_COLLECT_LINE] } as CollectFormValues,
  })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })
  const warehousePublicId = useWatch({ control: form.control, name: 'warehousePublicId' })
  const lines = useWatch({ control: form.control, name: 'lines' })

  // un solo almacén activo en la compañía: ya elegido (misma consulta que el selector)
  const warehouses = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  useEffect(() => {
    const only = warehouses.data?.length === 1 ? warehouses.data[0].publicId : undefined
    if (only && !form.getValues('warehousePublicId')) form.setValue('warehousePublicId', only)
  }, [warehouses.data, form])

  const addTrailingBlank = useCallback(() => {
    if (needsTrailingBlank(form.getValues('lines'))) append({ ...EMPTY_COLLECT_LINE }, { shouldFocus: false })
  }, [form, append])

  const onPicked = useCallback(
    (index: number, p: ProductListItemDto | null) => {
      const base = `lines.${index}` as const
      form.setValue(`${base}.sku`, p?.sku ?? '')
      form.setValue(`${base}.trackingTypeCode`, p?.trackingTypeCode ?? '')
      form.setValue(`${base}.owner`, p ? (p.ownerClientPublicId ?? OWN) : '')
      form.setValue(`${base}.available`, p?.qtyAvailable ?? null)
      form.setValue(`${base}.lotId`, '')
      form.setValue(`${base}.serialNumbers`, '')
      if (p && form.getValues(`${base}.quantity`) == null) form.setValue(`${base}.quantity`, 1)
      if (p) addTrailingBlank()
    },
    [form, addTrailingBlank],
  )

  const removeLine = useCallback(
    (index: number) => {
      if (form.getValues('lines').length <= 1) form.setValue('lines.0', { ...EMPTY_COLLECT_LINE })
      else remove(index)
      addTrailingBlank()
    },
    [form, remove, addTrailingBlank],
  )

  const clearAll = () => {
    form.reset({ warehousePublicId: form.getValues('warehousePublicId'), lines: [{ ...EMPTY_COLLECT_LINE }] })
  }

  const showLot = (lines ?? []).some((l) => isLotTracked(l?.trackingTypeCode))
  const showSerials = (lines ?? []).some((l) => l?.trackingTypeCode === 'SERIAL')
  const rows = useMemo<GridRow[]>(() => fields.map((f, i) => ({ key: f.id, index: i })), [fields])

  const columns = useMemo<DataColumn<GridRow>[]>(() => {
    // rejilla de captura: sin orden por columna (reordenar movería las filas que se están llenando)
    const cols: DataColumn<GridRow>[] = [
      {
        id: 'product',
        header: t('warehouse.pickBatches.fields.product'),
        cell: (r) => <ProductCell index={r.index} warehousePublicId={warehousePublicId} onPicked={onPicked} />,
        card: 'title',
      },
      { id: 'quantity', header: t('warehouse.pickBatches.fields.quantity'), cell: (r) => <QuantityCell index={r.index} /> },
      { id: 'bin', header: t('warehouse.pickBatches.fields.bin'), cell: (r) => <BinCell index={r.index} warehousePublicId={warehousePublicId} /> },
    ]
    if (showLot) cols.push({ id: 'lot', header: t('warehouse.pickBatches.fields.lot'), cell: (r) => <LotCell index={r.index} /> })
    if (showSerials) cols.push({ id: 'serials', header: t('warehouse.pickBatches.collectPanel.serials'), cell: (r) => <SerialsCell index={r.index} /> })
    return cols
  }, [t, warehousePublicId, onPicked, showLot, showSerials])

  const rowActions = useMemo<RowAction<GridRow>[]>(
    () => [
      {
        key: 'remove',
        label: t('warehouse.pickBatches.collectPanel.removeLine'),
        icon: <IconTrash />,
        tone: 'danger',
        // la fila vacía del final no se quita (siempre hay una para seguir capturando)
        visible: (r) => !(r.index === rows.length - 1 && lines?.[r.index] !== undefined && isBlankLine(lines[r.index])),
        onClick: (r) => removeLine(r.index),
      },
    ],
    [t, rows.length, lines, removeLine],
  )

  const submitting = form.formState.isSubmitting

  return (
    <div ref={boxRef} className="collect-panel">
      <Panel flush icon={<IconBasket />} title={t('warehouse.pickBatches.collectPanel.title')}>
        <Form
          id={FORM_ID}
          form={form}
          onSubmit={async (v) => {
            const { body, indexMap } = buildCollectBody(v)
            let created: PickBatchDto
            try {
              created = await create.mutateAsync(body)
            } catch (err) {
              throw remapCollectErrors(err, indexMap)
            }
            toast.success(t('warehouse.pickBatches.collected', { number: created.number ?? '' }))
            form.reset({ warehousePublicId: v.warehousePublicId, lines: [{ ...EMPTY_COLLECT_LINE }] })
            onCollected?.(created)
          }}
        >
          <div className="collect-top">
            <Field name="warehousePublicId" label={t('warehouse.pickBatches.fields.warehouse')} required>
              <WarehousePickerInput />
            </Field>
            <p className="note collect-note">{t('warehouse.pickBatches.linesHelp')}</p>
          </div>
          <div className="collect-grid">
            <DataTable
              label={t('warehouse.pickBatches.collectPanel.linesLabel')}
              columns={columns}
              rows={rows}
              rowKey={(r) => r.key}
              pagination={false}
              exportable={false}
              forceCards={width > 0 && width < CARDS_BELOW_PX}
              rowActions={rowActions}
            />
          </div>
          <div className="collect-foot">
            <button
              type="button"
              className="btn sm"
              disabled={fields.length >= MAX_PICK_LINES || submitting}
              onClick={() => append({ ...EMPTY_COLLECT_LINE })}
            >
              {t('warehouse.pickBatches.collectPanel.addLine')}
            </button>
            {fields.length >= MAX_PICK_LINES && <span className="help">{t('warehouse.pickBatches.collectPanel.maxLines')}</span>}
            <span className="collect-foot-act">
              <button type="button" className="btn" onClick={clearAll} disabled={submitting}>
                {t('warehouse.pickBatches.collectPanel.clear')}
              </button>
              <button type="submit" className="btn flow" disabled={submitting}>
                {submitting ? t('common.loading') : t('warehouse.pickBatches.collectPanel.submit')}
              </button>
            </span>
          </div>
        </Form>
      </Panel>
    </div>
  )
}
