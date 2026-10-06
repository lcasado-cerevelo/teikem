// Lote F17 (Rentas F-R1) — diálogos de la ficha de una renta: programar, despachar y cancelar (confirmación con comentario
// opcional que va al historial), extender (nueva fecha, motivo y tarifa opcional; `rental.extend`), tarifa de un equipo,
// agregar equipos (selector por serie) y quitar un equipo. Todos muestran el error del servidor tal cual y no se cierran
// (p. ej. 422 'Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.' o el 400
// 'La nueva fecha de recogido debe ser posterior a la actual ({fecha}).' bajo su campo).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useTenantSettings } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n'
import { DateInput, Field, Form, Modal, SearchMultiSelect, TextArea, toast } from '../../kernel/ui'
import { problemText } from '../warehouse/problemText'
import { useRentalAction } from './api'
import { EquipmentPicker } from './EquipmentPicker'
import { RateFields } from './RateFields'
import {
  activeLines,
  addDays,
  EMPTY_RATE,
  equipmentLines,
  extendBody,
  extensionIssues,
  rateDraftOf,
  rateIssue,
  RENTAL_LIMITS,
  serialKey,
  type PickedEquipment,
  type RateDraft,
  type RentalDto,
  type RentalLineDto,
} from './rentalRules'

// =====================================================================================================================
// Programar / Despachar / Cancelar
// =====================================================================================================================

export type StatusActionKind = 'schedule' | 'dispatch' | 'cancel'

export function RentalStatusActionModal({ kind, rental, onClose }: { kind: StatusActionKind; rental: RentalDto; onClose: () => void }) {
  const t = useT()
  const action = useRentalAction()
  const r = rental.rental ?? {}
  const units = activeLines(rental).length
  const schema = useMemo(() => z.object({ comment: z.string().max(RENTAL_LIMITS.comment, t('rentals.errors.commentTooLong')) }), [t])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { comment: '' } })
  const submitting = form.formState.isSubmitting
  const formId = `rental-${kind}-form`
  const params = { number: r.number ?? '', units, warehouse: r.warehouseCode ?? '' }
  const body =
    kind === 'cancel'
      ? t(r.statusCode === 'SCHEDULED' ? 'rentals.confirm.cancelBodyScheduled' : 'rentals.confirm.cancelBodyDraft', params)
      : t(`rentals.confirm.${kind}Body`, params)

  return (
    <Modal
      open
      title={t(`rentals.confirm.${kind}Title`, params)}
      onClose={onClose}
      dismissible={!submitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={submitting}>
            {t('rentals.confirm.back')}
          </button>
          <button type="submit" form={formId} className={kind === 'cancel' ? 'btn danger' : 'btn flow'} disabled={submitting}>
            {submitting ? t('common.loading') : t(`rentals.actions.${kind}`)}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          await action.mutateAsync({ action: kind, publicId: r.publicId ?? '', body: { comment: v.comment.trim() || null, rowVersion: rental.rowVersion ?? null } })
          toast.success(t(`rentals.toast.${kind}`, params))
          onClose()
        }}
      >
        <p className="ren-confirm">{body}</p>
        <Field name="comment" label={t('rentals.confirm.comment')}>
          <TextArea rows={2} maxLength={RENTAL_LIMITS.comment} />
        </Field>
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Extender
// =====================================================================================================================

export function RentalExtendModal({ rental, onClose }: { rental: RentalDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const action = useRentalAction()
  const { data: settings } = useTenantSettings()
  const r = rental.rental ?? {}
  const current = r.pickupDate ?? ''
  const lines = activeLines(rental)
  const [rate, setRate] = useState<RateDraft>(EMPTY_RATE)
  const [rateError, setRateError] = useState<{ field: 'frequency' | 'amount'; message: string } | null>(null)
  const [lineIds, setLineIds] = useState<string[]>(() => lines.map((l) => String(l.id)))
  const [linesError, setLinesError] = useState<string | null>(null)
  const lineOptions = useMemo(() => lines.map((l) => ({ value: String(l.id), label: `${l.serialNumber} · ${l.sku}` })), [lines])

  const schema = useMemo(
    () =>
      z
        .object({ newPickupDate: z.string(), reason: z.string() })
        .superRefine((v, ctx) => {
          const issues = extensionIssues({ currentPickupDate: current, newPickupDate: v.newPickupDate, reason: v.reason })
          if (issues.newPickupDate)
            ctx.addIssue({ code: 'custom', path: ['newPickupDate'], message: t(`rentals.errors.${issues.newPickupDate.code}`, issues.newPickupDate.params) })
          if (issues.reason) ctx.addIssue({ code: 'custom', path: ['reason'], message: t(`rentals.errors.${issues.reason.code}`) })
        }),
    [t, current],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { newPickupDate: '', reason: '' } })
  const submitting = form.formState.isSubmitting

  return (
    <Modal
      open
      size="lg"
      title={t('rentals.extend.title', { number: r.number ?? '' })}
      onClose={onClose}
      dismissible={!submitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={submitting}>
            {t('common.cancel')}
          </button>
          <button type="submit" form="rental-extend-form" className="btn flow" disabled={submitting}>
            {submitting ? t('common.loading') : t('rentals.extend.submit')}
          </button>
        </>
      }
    >
      <Form
        id="rental-extend-form"
        form={form}
        onSubmit={async (v) => {
          const problem = rateIssue(rate)
          if (problem) {
            setRateError({ field: problem.field, message: t(`rentals.errors.${problem.issue.code}`) })
            return
          }
          if (rate.frequency && lineIds.length === 0) {
            setLinesError(t('rentals.errors.noRateLines'))
            return
          }
          const body = extendBody(rental, { newPickupDate: v.newPickupDate, reason: v.reason, rate, lineIds: lineIds.map(Number) })
          await action.mutateAsync({ action: 'extend', publicId: r.publicId ?? '', body })
          toast.success(t('rentals.toast.extended', { number: r.number ?? '', date: f.date(v.newPickupDate) }))
          onClose()
        }}
      >
        <p className="ren-confirm">
          {t('rentals.extend.current', { date: f.date(current), original: f.date(r.originalPickupDate) })}
        </p>
        <div className="r2">
          <Field name="newPickupDate" label={t('rentals.extend.newPickupDate')} required help={t('rentals.extend.newPickupHelp')}>
            <DateInput min={current ? addDays(current, 1) : undefined} />
          </Field>
        </div>
        <Field name="reason" label={t('rentals.extend.reason')} required>
          <TextArea rows={2} maxLength={RENTAL_LIMITS.reason} />
        </Field>
        <fieldset className="ren-rate">
          <legend>{t('rentals.extend.rateSection')}</legend>
          <p className="help">{t('rentals.extend.rateHelp')}</p>
          <RateFields
            value={rate}
            onChange={(next) => {
              setRate(next)
              setRateError(null)
            }}
            error={rateError}
            defaultCurrency={settings?.currencyCode ?? ''}
          />
          {rate.frequency && lines.length > 0 && (
            <div className="f">
              <label id="rental-extend-lines-label" htmlFor="rental-extend-lines">
                {t('rentals.extend.applyTo')}
              </label>
              <SearchMultiSelect
                id="rental-extend-lines"
                options={lineOptions}
                value={lineIds}
                onChange={(v) => {
                  setLineIds(v)
                  setLinesError(null)
                }}
                labelledBy="rental-extend-lines-label"
                invalid={Boolean(linesError)}
              />
              {linesError && (
                <p className="ferr" role="alert">
                  {linesError}
                </p>
              )}
            </div>
          )}
        </fieldset>
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Tarifa de un equipo (antes del despacho)
// =====================================================================================================================

export function RentalRateModal({ rental, line, onClose }: { rental: RentalDto; line: RentalLineDto; onClose: () => void }) {
  const t = useT()
  const action = useRentalAction()
  const { data: settings } = useTenantSettings()
  const [rate, setRate] = useState<RateDraft>(() => rateDraftOf(line.rate))
  const [error, setError] = useState<{ field: 'frequency' | 'amount'; message: string } | null>(null)
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function save() {
    // aquí la tarifa es obligatoria (se corrige la vigente): sin frecuencia, el mensaje del servidor
    const problem = rateIssue(rate.frequency ? rate : { ...rate, frequency: '', amount: rate.amount ?? 0 })
    if (problem) {
      setError({ field: problem.field, message: t(`rentals.errors.${problem.issue.code}`) })
      return
    }
    setSaving(true)
    setAlert(null)
    try {
      await action.mutateAsync({
        action: 'setRate',
        publicId: rental.rental?.publicId ?? '',
        lineId: line.id ?? 0,
        body: { frequency: rate.frequency, amount: rate.amount, currency: rate.currency || null, rowVersion: rental.rowVersion ?? null },
      })
      toast.success(t('rentals.toast.rateSaved', { serial: line.serialNumber ?? '' }))
      onClose()
    } catch (err) {
      const p = applyProblemDetails(err)
      const freq = p.errors.frequency?.[0]
      const amount = p.errors.amount?.[0]
      if (freq) setError({ field: 'frequency', message: freq })
      else if (amount) setError({ field: 'amount', message: amount })
      else setAlert(problemText(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      title={t('rentals.rate.title', { serial: line.serialNumber ?? '' })}
      onClose={onClose}
      dismissible={!saving}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={saving}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={saving}>
            {saving ? t('common.loading') : t('rentals.rate.save')}
          </button>
        </>
      }
    >
      {alert && (
        <div className="form-alert" role="alert">
          {alert}
        </div>
      )}
      <p className="help">{t('rentals.rate.help')}</p>
      <RateFields
        value={rate}
        onChange={(next) => {
          setRate(next)
          setError(null)
        }}
        error={error}
        defaultCurrency={settings?.currencyCode ?? ''}
        required
      />
    </Modal>
  )
}

// =====================================================================================================================
// Agregar equipos (ficha, Borrador o Programada)
// =====================================================================================================================

export function AddEquipmentModal({ rental, onClose }: { rental: RentalDto; onClose: () => void }) {
  const t = useT()
  const action = useRentalAction()
  const r = rental.rental ?? {}
  const [alert, setAlert] = useState<string | null>(null)
  const excluded = useMemo(() => new Set(activeLines(rental).map((l) => serialKey(l.serialNumber))), [rental])

  async function add(items: PickedEquipment[]) {
    setAlert(null)
    try {
      for (const body of equipmentLines(items)) {
        await action.mutateAsync({ action: 'addLines', publicId: r.publicId ?? '', body })
      }
      toast.success(t('rentals.toast.linesAdded', { count: items.length }))
    } catch (err) {
      setAlert(problemText(err))
      throw err
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('rentals.picker.modalTitle', { number: r.number ?? '' })}
      onClose={onClose}
      dismissible={!action.isPending}
      footer={
        <button type="button" className="btn" onClick={onClose} disabled={action.isPending}>
          {t('common.done')}
        </button>
      }
    >
      {alert && (
        <div className="form-alert" role="alert">
          {alert}
        </div>
      )}
      {r.statusCode === 'SCHEDULED' && <p className="note">{t('rentals.picker.scheduledNote')}</p>}
      <EquipmentPicker warehousePublicId={r.warehousePublicId} warehouseCode={r.warehouseCode ?? ''} excluded={excluded} onAdd={add} busy={action.isPending} />
    </Modal>
  )
}
