// Lote F18 (Rentas F-R2) — "Registrar devolución" desde la ficha de una renta En renta (`POST /rentals/{publicId}/returns`,
// `rental.return`; manual 11 §5). Encabezado: fecha (hoy de la compañía por defecto, no futura ni anterior al inicio), motivo
// (catálogo RentalReturnReason; con "Otro" las notas son obligatorias), notas, costo de recogido estimado y su moneda (solo
// datos) y un destino común opcional (almacén + posición; vacío = cada equipo vuelve a su posición de origen; nunca la zona En
// renta; puede ser otro almacén). Por cada equipo pendiente (despachado y sin devolver): incluirlo (devolución parcial
// permitida), condición (RentalReturnCondition, Buena por defecto), posición de destino propia opcional, "¿Pasa por proceso?"
// (sí por defecto) y notas. La validación previa usa los mismos mensajes del servidor; los errores del servidor salen tal cual
// (bajo su campo o arriba del formulario) y el diálogo no se cierra.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useLookups, useTenantSettings } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Form, Modal, NumberInput, Select, TextArea, toast } from '../../kernel/ui'
import { BinPicker, WarehousePicker } from '../warehouse/pickers'
import { useCreateRentalReturn } from './api'
import type { RentalDto } from './rentalRules'
import {
  CONDITION_GOOD,
  initialReturnLines,
  isEarlyReturn,
  RENTAL_ZONE_TYPES,
  RETURN_CONDITION_DOMAIN,
  RETURN_LIMITS,
  RETURN_REASON_DOMAIN,
  returnBody,
  returnHeaderIssues,
  returnIssues,
  type ReturnHeaderDraft,
  type ReturnLineDraft,
} from './returnRules'

const FORM_ID = 'rental-return-form'

export function RentalReturnModal({ rental, onClose }: { rental: RentalDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const r = rental.rental ?? {}
  const create = useCreateRentalReturn()
  const { data: settings } = useTenantSettings()
  const { data: reasons = [] } = useLookups(RETURN_REASON_DOMAIN)
  const { data: conditions = [] } = useLookups(RETURN_CONDITION_DOMAIN)
  const { data: currencies = [] } = useLookups('Currency')
  const today = tenantToday()

  const [lines, setLines] = useState<ReturnLineDraft[]>(() => initialReturnLines(rental))
  const [warehousePublicId, setWarehousePublicId] = useState<string | null>(r.warehousePublicId ?? null)
  const [toBinId, setToBinId] = useState<number | null>(null)
  const [linesError, setLinesError] = useState<string | null>(null)
  const [lineErrors, setLineErrors] = useState<Record<number, string>>({})

  const reasonOptions = useMemo(() => reasons.filter((x) => x.isEnabled).map((x) => ({ value: x.code, label: x.label })), [reasons])
  const conditionOptions = useMemo(() => {
    const list = conditions.filter((x) => x.isEnabled).map((x) => ({ value: x.code, label: x.label }))
    return list.length > 0 ? list : [{ value: CONDITION_GOOD, label: CONDITION_GOOD }]
  }, [conditions])
  const currencyOptions = useMemo(() => currencies.map((c) => ({ value: c.code, label: `${c.code} · ${c.label}` })), [currencies])

  const schema = useMemo(
    () =>
      z
        .object({
          returnedOn: z.string(),
          reason: z.string(),
          notes: z.string(),
          estimatedPickupCost: z.number(t('rentalReturns.errors.numberInvalid')).nullable(),
          transportCurrency: z.string(),
        })
        .superRefine((v, ctx) => {
          // solo el encabezado: los equipos se validan al enviar (viven fuera del formulario)
          const issues = returnHeaderIssues({ ...v, estimatedPickupCost: v.estimatedPickupCost ?? null }, { today, startDate: r.startDate })
          for (const [field, issue] of Object.entries(issues)) {
            if (issue) ctx.addIssue({ code: 'custom', path: [field], message: t(`rentalReturns.errors.${issue.code}`, issue.params) })
          }
        }),
    [t, today, r.startDate],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { returnedOn: today, reason: '', notes: '', estimatedPickupCost: null, transportCurrency: '' },
  })
  const returnedOn = useWatch({ control: form.control, name: 'returnedOn' })
  const reason = useWatch({ control: form.control, name: 'reason' })
  const submitting = form.formState.isSubmitting

  const included = lines.filter((l) => l.include).length
  const early = isEarlyReturn(returnedOn, r.pickupDate, today)
  const allChecked = included === lines.length

  const update = (i: number, patch: Partial<ReturnLineDraft>) => {
    setLines((cur) => cur.map((l, j) => (j === i ? { ...l, ...patch } : l)))
    setLinesError(null)
    if (patch.notes !== undefined) setLineErrors((cur) => ({ ...cur, [i]: '' }))
  }

  async function save(v: { returnedOn: string; reason: string; notes: string; estimatedPickupCost?: number | null; transportCurrency: string }) {
    const header: ReturnHeaderDraft = { ...v, estimatedPickupCost: v.estimatedPickupCost ?? null, toBinId }
    const issues = returnIssues(header, lines, { today, startDate: r.startDate })
    setLinesError(issues.fields.lines ? t(`rentalReturns.errors.${issues.fields.lines.code}`) : null)
    setLineErrors(Object.fromEntries(Object.entries(issues.lineNotes).map(([i, x]) => [Number(i), t(`rentalReturns.errors.${x.code}`)])))
    // el encabezado ya lo validó zod; aquí solo lo que vive fuera del formulario (los equipos)
    if (issues.fields.lines || Object.keys(issues.lineNotes).length > 0) return
    const dto = await create.mutateAsync({ publicId: r.publicId ?? '', body: returnBody(header, lines, rental.rowVersion) })
    const ret = dto.return ?? {}
    toast.success(
      t(dto.rentalStatusCode === 'RETURNED' ? 'rentalReturns.toast.createdClosed' : 'rentalReturns.toast.created', {
        number: ret.number ?? '',
        count: ret.units ?? included,
        rental: r.number ?? '',
      }),
    )
    onClose()
  }

  return (
    <Modal
      open
      size="lg"
      title={t('rentalReturns.form.title', { number: r.number ?? '' })}
      onClose={onClose}
      dismissible={!submitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={submitting}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={FORM_ID} className="btn flow" disabled={submitting || lines.length === 0}>
            {submitting ? t('common.loading') : t('rentalReturns.form.submit', { count: included })}
          </button>
        </>
      }
    >
      <Form id={FORM_ID} form={form} onSubmit={save}>
        <p className="ren-confirm">{t('rentalReturns.form.intro', { client: r.clientName ?? '', pickup: f.date(r.pickupDate) })}</p>
        <div className="r2">
          <Field name="returnedOn" label={t('rentalReturns.fields.returnedOn')} help={t('rentalReturns.form.returnedOnHelp')}>
            <DateInput max={today} min={r.startDate ?? undefined} />
          </Field>
          <Field name="reason" label={t('rentalReturns.fields.reason')} required>
            <Select options={reasonOptions} placeholder={t('rentalReturns.form.reasonPlaceholder')} />
          </Field>
        </div>
        {early && (
          <p className="note" role="status">
            {t('rentalReturns.form.earlyNote', { pickup: f.date(r.pickupDate) })}
          </p>
        )}
        <Field name="notes" label={t('rentalReturns.fields.notes')} required={reason === 'OTHER'} help={reason === 'OTHER' ? t('rentalReturns.form.notesOtherHelp') : undefined}>
          <TextArea rows={2} maxLength={RETURN_LIMITS.notes} />
        </Field>
        <div className="r2">
          <Field name="estimatedPickupCost" label={t('rentalReturns.fields.pickupCost')} help={t('rentalReturns.form.pickupCostHelp')}>
            <NumberInput step="0.01" min={0} />
          </Field>
          <Field name="transportCurrency" label={t('rentalReturns.fields.currency')}>
            <Select
              options={currencyOptions}
              placeholder={settings?.currencyCode ? t('rentals.rate.companyCurrencyCode', { code: settings.currencyCode }) : t('rentals.rate.companyCurrency')}
            />
          </Field>
        </div>

        <fieldset className="ren-rate">
          <legend>{t('rentalReturns.form.destination')}</legend>
          <p className="help">{t('rentalReturns.form.destinationHelp')}</p>
          <div className="r2">
            <div className="f">
              <label htmlFor="ret-warehouse">{t('rentalReturns.fields.toWarehouse')}</label>
              <WarehousePicker
                id="ret-warehouse"
                value={warehousePublicId}
                placeholder={null}
                onChange={(id) => {
                  setWarehousePublicId(id)
                  setToBinId(null)
                  setLines((cur) => cur.map((l) => ({ ...l, toBinId: null })))
                }}
              />
            </div>
            <div className="f">
              <label htmlFor="ret-bin">{t('rentalReturns.fields.toBinAll')}</label>
              <BinPicker
                id="ret-bin"
                warehousePublicId={warehousePublicId}
                value={toBinId}
                onChange={(id) => setToBinId(id)}
                excludeZoneTypeCodes={RENTAL_ZONE_TYPES}
                placeholder={t('rentalReturns.form.originBin')}
              />
            </div>
          </div>
        </fieldset>
      </Form>

      <section className="ren-equipment" aria-labelledby="ret-lines-h">
        <div className="ren-serials-bar">
          <h3 className="ren-h3" id="ret-lines-h">
            {t('rentalReturns.form.linesTitle', { included, total: lines.length })}
          </h3>
          {lines.length > 1 && (
            <button type="button" className="btn sm" onClick={() => setLines((cur) => cur.map((l) => ({ ...l, include: !allChecked })))}>
              {allChecked ? t('rentalReturns.form.none') : t('rentalReturns.form.all')}
            </button>
          )}
        </div>
        <p className="help">{t('rentalReturns.form.effects')}</p>
        {linesError && (
          <p className="ferr" role="alert">
            {linesError}
          </p>
        )}
        {lines.length === 0 && <p className="help">{t('rentalReturns.form.noPending')}</p>}
        <ul className="ren-ret-lines">
          {lines.map((l, i) => {
            const id = `ret-l${l.lineId}`
            return (
              <li key={l.lineId}>
                {/* los controles del primer legend siguen activos aunque el fieldset esté deshabilitado */}
                <fieldset className={l.include ? 'ren-ret-line' : 'ren-ret-line off'} disabled={!l.include || submitting}>
                  <legend>
                    <label className="ren-ret-include">
                      <input type="checkbox" checked={l.include} disabled={submitting} onChange={(e) => update(i, { include: e.target.checked })} />
                      <span className="mono">{l.serialNumber}</span>
                      <span className="ren-ret-product">
                        {l.sku} · {l.productName}
                      </span>
                    </label>
                  </legend>
                  <div className="ren-ret-grid">
                    <div className="f">
                      <label htmlFor={`${id}-cond`}>{t('rentalReturns.fields.condition')}</label>
                      <select id={`${id}-cond`} value={l.condition} onChange={(e) => update(i, { condition: e.target.value })}>
                        {conditionOptions.map((o) => (
                          <option key={o.value} value={o.value}>
                            {o.label}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="f">
                      <label htmlFor={`${id}-bin`}>{t('rentalReturns.fields.toBin')}</label>
                      <BinPicker
                        id={`${id}-bin`}
                        warehousePublicId={warehousePublicId}
                        value={l.toBinId}
                        onChange={(binId) => update(i, { toBinId: binId })}
                        excludeZoneTypeCodes={RENTAL_ZONE_TYPES}
                        disabled={!l.include || submitting}
                        placeholder={toBinId ? t('rentalReturns.form.commonBin') : t('rentalReturns.form.originBinOf', { bin: l.fromBinCode })}
                      />
                    </div>
                    <div className="f ren-ret-proc">
                      <label className="sw">
                        <input
                          type="checkbox"
                          role="switch"
                          aria-label={t('rentalReturns.form.requiresProcessOf', { serial: l.serialNumber })}
                          aria-describedby={`${id}-proc-h`}
                          checked={l.requiresProcess}
                          onChange={(e) => update(i, { requiresProcess: e.target.checked })}
                        />
                        <span className="tk" aria-hidden="true" />
                        {t('rentalReturns.fields.requiresProcess')}
                      </label>
                      <p className="help" id={`${id}-proc-h`}>
                        {l.requiresProcess ? t('rentalReturns.form.processYes') : t('rentalReturns.form.processNo')}
                      </p>
                    </div>
                    <div className="f">
                      <label htmlFor={`${id}-notes`}>{t('rentalReturns.fields.lineNotes')}</label>
                      <input
                        id={`${id}-notes`}
                        value={l.notes}
                        maxLength={RETURN_LIMITS.lineNotes}
                        aria-invalid={lineErrors[i] ? true : undefined}
                        aria-describedby={lineErrors[i] ? `${id}-notes-err` : undefined}
                        onChange={(e) => update(i, { notes: e.target.value })}
                      />
                      {lineErrors[i] && (
                        <p className="ferr" id={`${id}-notes-err`}>
                          {lineErrors[i]}
                        </p>
                      )}
                    </div>
                  </div>
                </fieldset>
              </li>
            )
          })}
        </ul>
      </section>
    </Modal>
  )
}
