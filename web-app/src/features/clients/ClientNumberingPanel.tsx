// Panel «Numeración»: quién asigna el número de orden y de factura (el cliente o Teikem, automático) y los tres patrones
// (orden, factura, paquete) con «Ejemplo» en vivo (GET /clients/number-format/preview con pausa entre teclas; mientras no
// responde, los *Preview de la ficha o el cálculo local). Patrón vacío = el del sistema. PATCH /number-settings.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Field, Form, Panel, Select, TextInput, toast } from '../../kernel/ui'
import { IconTag } from '../../kernel/ui/screenIcons'
import { useNumberPreview, useUpdateNumberSettings } from './api'
import {
  buildNumberingRequest,
  effectivePattern,
  localPreview,
  numberingSchema,
  numberingValuesOf,
  PATTERN_DEFAULTS,
  PATTERN_MAX,
  type ClientDetail,
  type NumberingValues,
  type PatternIssue,
  type PatternKind,
} from './clientRules'

const PREVIEW_DELAY_MS = 300

/** Valor que se mantiene `delay` ms sin cambiar (pausa entre teclas antes de pedir el ejemplo al servidor). */
function useSettled(value: string, delay: number): string {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setSettled(value), delay)
    return () => clearTimeout(id)
  }, [value, delay])
  return settled
}

const KIND_FIELD: Record<PatternKind, 'orderPattern' | 'invoicePattern' | 'packagePattern'> = {
  order: 'orderPattern',
  invoice: 'invoicePattern',
  package: 'packagePattern',
}

/** Texto bajo un patrón: «Ejemplo: AX-00001» o el motivo (400) por el que no se puede resolver. */
function PatternExample({ kind, client }: { kind: PatternKind; client: ClientDetail }) {
  const t = useT()
  const raw = String(useWatch<NumberingValues>({ name: KIND_FIELD[kind] }) ?? '').trim()
  const settled = useSettled(raw, PREVIEW_DELAY_MS)
  const preview = useNumberPreview(settled, settled !== '')
  const n = client.numberSettings
  const saved = { order: n?.orderNumberPreview, invoice: n?.invoiceNumberPreview, package: n?.packageNumberPreview }[kind]
  const savedPattern = { order: n?.orderNumberFormat, invoice: n?.invoiceNumberFormat, package: n?.packageNumberFormat }[kind]?.trim() ?? ''

  let example: string | null
  let error: string | null = null
  if (raw === '') example = localPreview(kind, '')
  else if (settled === raw && preview.isError) {
    const p = applyProblemDetails(preview.error)
    error = Object.values(p.errors).flat()[0] ?? p.title
    example = null
  } else if (settled === raw && preview.data?.value) example = preview.data.value
  else example = raw === savedPattern && saved ? saved : localPreview(kind, raw)

  return (
    <p className="meta cl-example" aria-live="polite">
      {error ? (
        <span className="ferr">{error}</span>
      ) : (
        <>
          {t('clients.numbering.example')}: <span className="mono">{example ?? '—'}</span>
        </>
      )}
    </p>
  )
}

export function ClientNumberingPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const canEdit = useCan('clients.update')
  const publicId = client.publicId ?? ''
  const save = useUpdateNumberSettings(publicId)
  const original = useMemo(() => numberingValuesOf(client), [client])

  const patternMessage = useMemo(
    () => (issue: PatternIssue) =>
      issue.code === 'badChar' ? t('clients.errors.patternBadChar', { ch: issue.ch }) : t(`clients.errors.pattern.${issue.code}`, { max: PATTERN_MAX }),
    [t],
  )
  const schema = useMemo(() => numberingSchema(patternMessage), [patternMessage])
  const form = useForm({ resolver: zodResolver(schema), values: original, resetOptions: { keepDirtyValues: true } })

  const assignerOptions = [
    { value: 'client', label: t('clients.numbering.byClient') },
    { value: 'teikem', label: t('clients.numbering.byTeikem') },
  ]
  const patternField = (kind: PatternKind, name: keyof NumberingValues, label: string) => (
    <div className="cl-pattern" key={kind}>
      <Field name={name} label={label}>
        <TextInput maxLength={PATTERN_MAX} placeholder={effectivePattern(kind, '')} spellCheck={false} autoCapitalize="off" />
      </Field>
      <PatternExample kind={kind} client={client} />
    </div>
  )

  return (
    <Panel icon={<IconTag />} title={t('clients.numbering.title')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          const updated = await save.mutateAsync(buildNumberingRequest(v))
          form.reset(numberingValuesOf(updated))
          toast.success(t('clients.saved'))
        }}
      >
        <fieldset className="cl-fs" disabled={!canEdit}>
          <div className="r2">
            <Field name="orderAssigner" label={t('clients.numbering.orderBy')}>
              <Select options={assignerOptions} />
            </Field>
            <Field name="invoiceAssigner" label={t('clients.numbering.invoiceBy')}>
              <Select options={assignerOptions} />
            </Field>
          </div>
          <p className="help">{t('clients.numbering.hint')}</p>
          <div className="r3 cl-patterns">
            {patternField('order', 'orderPattern', t('clients.numbering.order'))}
            {patternField('invoice', 'invoicePattern', t('clients.numbering.invoice'))}
            {patternField('package', 'packagePattern', t('clients.numbering.package'))}
          </div>
          <p className="help">{t('clients.numbering.emptyHelp', { order: PATTERN_DEFAULTS.order, invoice: PATTERN_DEFAULTS.invoice, package: PATTERN_DEFAULTS.package })}</p>
        </fieldset>
        <Can perm="clients.update">
          <div className="form-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
              {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
            </button>
          </div>
        </Can>
      </Form>
    </Panel>
  )
}
