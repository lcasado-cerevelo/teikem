// Lote 14 (P8) — modales de una línea del conteo elegido:
// - `CountQtyModal`: lo abre el escáner (o el botón "Series" de una línea SERIAL). NONE/LOT: "Cantidad contada" con el foco
//   puesto y Enter guarda (`PUT /cycle-counts/{id}/lines`, en la fila única de `useCountDrafts`); SERIAL: las series contadas
//   (una por renglón o separadas por coma; la serie escaneada ya viene agregada) o "No se encontró ninguna". Errores del
//   servidor tal cual, arriba del botón.
// - `AddFoundLineModal`: "Agregar lo encontrado" (`POST /cycle-counts/{id}/lines`): posición del almacén del conteo, producto,
//   lote (existente o nuevo) y lo contado; lo que antes hacía la ficha del conteo.
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useLang, useT } from '../../kernel/i18n'
import { Field, Form, Modal, NumberInput, Select, TextArea, TextInput, toast } from '../../kernel/ui'
import { productLabel, useCycleCountAction, useProductLots, type CycleCountDetailDto, type CycleCountLineDto } from './api'
import { countedText, parseCounted } from './countView'
import { countLineIssues, countLotIssue, formatNumber, parseSerials, remapProblemFields, type LineIssue } from './lineRules'
import { BinPickerInput, ProductPickerInput } from './pickers'
import { problemText } from './problemText'
import { QuantityCalculator } from './QuantityCalculator'
import { calcFromText, calcTotal, totalToText, type CalcState } from './quantityCalc'
import type { CountCaptureBody } from './useCountDrafts'

function useIssueText() {
  const t = useT()
  return useCallback((issue: LineIssue) => t(`warehouse.lineRules.${issue.code}`, issue.params), [t])
}

// =====================================================================================================================
// Cantidad (o series) de una línea
// =====================================================================================================================

export interface CountQtyModalProps {
  line: CycleCountLineDto
  /** Serie escaneada (se agrega a lo contado de una línea SERIAL). */
  scannedSerial?: string | null
  /** Sin permiso de ver lo esperado (a ciegas) no se muestra el "Esperado". */
  isBlind?: boolean
  /** Abre directo con la calculadora (filas × columnas × fondo + sueltas) en lugar del campo de cantidad. */
  startInCalculator?: boolean
  onSave: (body: CountCaptureBody) => Promise<unknown>
  onClose: () => void
}

export function CountQtyModal({ line, scannedSerial, isBlind, startInCalculator, onSave, onClose }: CountQtyModalProps) {
  const t = useT()
  const lang = useLang()
  const issueText = useIssueText()
  const formId = useId()
  const serial = line.trackingTypeCode === 'SERIAL'
  const [qty, setQty] = useState(() => countedText(line.countedQty))
  // calculadora de cantidad: el total llena la cantidad a medida que se escribe; al volver a «Cantidad directa» queda ese número
  const [calc, setCalc] = useState<CalcState | null>(() => (startInCalculator && !line.trackingTypeCode?.includes('SERIAL') ? calcFromText(countedText(line.countedQty)) : null))
  const [serials, setSerials] = useState(() => {
    const current = [...(line.countedSerials ?? [])]
    if (scannedSerial && !current.some((s) => s.toLowerCase() === scannedSerial.toLowerCase())) current.push(scannedSerial)
    return current.join('\n')
  })
  const [nothingFound, setNothingFound] = useState(serial && line.countedQty === 0 && (line.countedSerials ?? []).length === 0)
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e?: FormEvent) => {
    e?.preventDefault()
    if (busy) return
    setError(null)
    setFieldError(null)
    let body: CountCaptureBody
    if (serial) {
      const list = nothingFound ? [] : parseSerials(serials)
      const issue = countLineIssues({ sku: line.sku ?? '', trackingTypeCode: 'SERIAL', countedQty: null, serials: list })[0]
      if (issue) return setFieldError(issueText(issue))
      body = { countedQty: null, serialNumbers: nothingFound ? [] : list.length > 0 ? list : null }
    } else {
      const n = parseCounted(qty)
      if (n !== null && Number.isNaN(n)) return setFieldError(t('warehouse.receipts.lines.invalidNumber'))
      const issue = countLineIssues({ sku: line.sku ?? '', trackingTypeCode: line.trackingTypeCode, countedQty: n, serials: [] })[0]
      if (issue) return setFieldError(issueText(issue))
      body = { countedQty: n, serialNumbers: null }
    }
    setBusy(true)
    try {
      await onSave(body)
      toast.success(t('warehouse.cycleCounts.qty.saved'))
      onClose()
    } catch (err) {
      setError(problemText(err))
    } finally {
      setBusy(false)
    }
  }

  const fieldId = `${formId}-f`
  const helpId = `${formId}-h`
  const errId = `${formId}-e`
  return (
    <Modal
      open
      size="sm"
      title={t('warehouse.cycleCounts.qty.title', { product: productLabel({ sku: line.sku, name: line.productName }) })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={busy}>
            {busy ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} noValidate>
        <p className="note cc-qty-where">
          {t('warehouse.cycleCounts.qty.where', { bin: line.binCode ?? '—', lot: line.lotNumber ?? '—' })}
          {!isBlind && line.systemQty != null && <> · {t('warehouse.cycleCounts.qty.expected', { qty: formatNumber(line.systemQty, lang) })}</>}
        </p>
        {serial ? (
          <>
            <div className="f">
              <label htmlFor={fieldId}>{t('warehouse.cycleCounts.qty.serials')}</label>
              <textarea
                id={fieldId}
                rows={6}
                value={serials}
                readOnly={nothingFound}
                aria-invalid={fieldError ? true : undefined}
                aria-describedby={[helpId, fieldError ? errId : ''].filter(Boolean).join(' ')}
                onChange={(e) => setSerials(e.target.value)}
              />
              <p className="help" id={helpId}>
                {isBlind
                  ? t('warehouse.receipts.fields.serialsHelp')
                  : t('warehouse.cycleCounts.qty.expectedSerials', {
                      count: line.expectedSerials?.length ?? 0,
                      list: (line.expectedSerials ?? []).slice(0, 20).join(', ') || '—',
                    })}
              </p>
            </div>
            <div className="f">
              <label className="sw">
                <input type="checkbox" role="switch" checked={nothingFound} onChange={(e) => setNothingFound(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('warehouse.cycleCounts.qty.nothingFound')}</span>
              </label>
            </div>
          </>
        ) : (
          <div className="f">
            <label htmlFor={fieldId}>{t('warehouse.cycleCounts.qty.counted')}</label>
            {calc ? (
              <>
                <QuantityCalculator
                  state={calc}
                  onChange={(next) => {
                    setCalc(next)
                    setQty(totalToText(calcTotal(next).total))
                  }}
                  onEnter={() => void submit()}
                />
                <button type="button" className="btn sm cc-calc-btn" onClick={() => setCalc(null)}>
                  {t('warehouse.cycleCounts.calc.direct')}
                </button>
              </>
            ) : (
            <>
            <input
              id={fieldId}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              className="cc-qty-big"
              value={qty}
              aria-invalid={fieldError ? true : undefined}
              aria-describedby={[helpId, fieldError ? errId : ''].filter(Boolean).join(' ')}
              onChange={(e) => setQty(e.target.value)}
              onFocus={(e) => e.currentTarget.select()}
              onKeyDown={(e) => {
                // Enter guarda (el lector de código de barras también manda Enter)
                if (e.key === 'Enter') {
                  e.preventDefault()
                  void submit()
                }
              }}
            />
            <button type="button" className="btn sm cc-calc-btn" onClick={() => setCalc(calcFromText(qty))}>
              {t('warehouse.cycleCounts.calc.open')}
            </button>
            </>
            )}
            <p className="help" id={helpId}>
              {t('warehouse.cycleCounts.qty.countedHelp')}
            </p>
          </div>
        )}
        {fieldError && (
          <p className="ferr" id={errId} role="alert">
            {fieldError}
          </p>
        )}
        {error && (
          <p className="ferr" role="alert">
            {error}
          </p>
        )}
      </form>
    </Modal>
  )
}

// =====================================================================================================================
// Agregar lo encontrado (POST /{id}/lines): una línea que no tenía foto
// =====================================================================================================================

export interface AddFoundProduct {
  publicId: string
  sku: string
  trackingTypeCode: string
}

/** Lote 24: la posición es OPCIONAL; sin ella el servidor usa la única posición donde el sistema tiene el producto (si hay varias o
 *  ninguna, responde 400 en `binId` con el mensaje exacto y se elige aquí). `initialProduct` = el producto que se escaneó. */
export function AddFoundLineModal({ detail, onClose, initialProduct }: { detail: CycleCountDetailDto; onClose: () => void; initialProduct?: AddFoundProduct | null }) {
  const t = useT()
  const issueText = useIssueText()
  const action = useCycleCountAction()
  const schema = useMemo(
    () =>
      z
        .object({
          binId: z.string(),
          productPublicId: z.string().nullable(),
          sku: z.string(),
          trackingTypeCode: z.string(),
          lotId: z.string(),
          lotNumber: z.string().max(60, t('warehouse.lineRules.lotTooLong')),
          countedQty: z.number().nullable(),
          serialNumbers: z.string(),
        })
        .superRefine((v, ctx) => {
          if (!v.productPublicId) ctx.addIssue({ code: 'custom', path: ['productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
          if (v.lotId && v.lotNumber.trim()) ctx.addIssue({ code: 'custom', path: ['lotNumber'], message: t('warehouse.cycleCounts.errors.lotAmbiguous') })
          const lotIssue = countLotIssue(v.trackingTypeCode, v.sku, Boolean(v.lotId || v.lotNumber.trim()))
          if (lotIssue) ctx.addIssue({ code: 'custom', path: ['lotNumber'], message: issueText(lotIssue) })
          const serial = v.trackingTypeCode === 'SERIAL'
          for (const issue of countLineIssues({ sku: v.sku, trackingTypeCode: v.trackingTypeCode, countedQty: serial ? null : v.countedQty, serials: parseSerials(v.serialNumbers) })) {
            ctx.addIssue({ code: 'custom', path: [issue.field], message: issueText(issue) })
          }
          if (!serial && v.countedQty == null) ctx.addIssue({ code: 'custom', path: ['countedQty'], message: t('warehouse.cycleCounts.errors.countedRequired') })
        }),
    [t, issueText],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      binId: '',
      productPublicId: (initialProduct?.publicId ?? null) as string | null,
      sku: initialProduct?.sku ?? '',
      trackingTypeCode: initialProduct?.trackingTypeCode ?? '',
      lotId: '',
      lotNumber: '',
      countedQty: null as number | null,
      serialNumbers: '',
    },
  })
  const productPublicId = useWatch({ control: form.control, name: 'productPublicId' })
  const tracking = useWatch({ control: form.control, name: 'trackingTypeCode' })
  const lots = useProductLots(productPublicId, { enabled: tracking === 'LOT' || tracking === 'SERIAL', handleAccessDenied: false })
  const lotOptions = useMemo(
    () => (lots.data ?? []).filter((l) => l.isActive !== false).map((l) => ({ value: String(l.id), label: [l.lotNumber, l.expiryDate].filter(Boolean).join(' · ') })),
    [lots.data],
  )
  const formId = 'cycle-count-add-line'

  return (
    <Modal
      open
      title={t('warehouse.cycleCounts.detail.addLine')}
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
      <p className="note cc-qty-where">{t('warehouse.cycleCounts.detail.addLineHelp')}</p>
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const serials = parseSerials(v.serialNumbers)
          const lotNumber = v.lotNumber.trim()
          try {
            await action.mutateAsync({
              id: detail.count?.id ?? 0,
              action: 'addLine',
              body: {
                binId: v.binId ? Number(v.binId) : null,
                productPublicId: v.productPublicId,
                lotId: v.lotId ? Number(v.lotId) : null,
                lot: lotNumber ? { number: lotNumber } : undefined,
                countedQty: v.trackingTypeCode === 'SERIAL' ? null : v.countedQty,
                serialNumbers: v.trackingTypeCode === 'SERIAL' ? serials : null,
              },
            })
          } catch (err) {
            throw remapProblemFields(err, (k) => (k === 'lot' || k.startsWith('lot.') ? 'lotNumber' : null))
          }
          toast.success(t('warehouse.cycleCounts.detail.lineAdded'))
          onClose()
        }}
      >
        <div className="r2">
          <Field name="binId" label={t('warehouse.cycleCounts.detail.bin')} help={t('warehouse.cycleCounts.detail.binOptionalHelp')}>
            <BinPickerInput warehousePublicId={detail.count?.warehousePublicId} />
          </Field>
          <Field name="productPublicId" label={t('warehouse.receipts.fields.product')} required>
            <ProductPickerInput
              onPicked={(p) => {
                form.setValue('sku', p?.sku ?? '')
                form.setValue('trackingTypeCode', p?.trackingTypeCode ?? '')
                form.setValue('lotId', '')
              }}
            />
          </Field>
        </div>
        {(tracking === 'LOT' || tracking === 'SERIAL') && (
          <div className="r2">
            <Field name="lotId" label={t('warehouse.cycleCounts.detail.existingLot')}>
              <Select options={lotOptions} placeholder="—" />
            </Field>
            <Field name="lotNumber" label={t('warehouse.cycleCounts.detail.newLot')} required={tracking === 'LOT'}>
              <TextInput maxLength={60} />
            </Field>
          </div>
        )}
        {tracking === 'SERIAL' ? (
          <Field name="serialNumbers" label={t('warehouse.cycleCounts.qty.serials')} help={t('warehouse.receipts.fields.serialsHelp')}>
            <TextArea rows={4} />
          </Field>
        ) : (
          <Field name="countedQty" label={t('warehouse.cycleCounts.qty.counted')} required>
            <NumberInput min={0} step="0.001" />
          </Field>
        )}
      </Form>
    </Modal>
  )
}
