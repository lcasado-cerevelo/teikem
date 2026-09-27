// Diálogo "Mi rango de fecha" de una tarjeta de Pulso: modo del catálogo DateRangeMode y, si es personalizado,
// Desde/Hasta. Guarda la preferencia del usuario (no la definición) y Pulso se recalcula.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n/useT'
import { DateInput, Field, Form, Select } from '../../kernel/ui/Form'
import { Modal } from '../../kernel/ui/Modal'
import { toast } from '../../kernel/ui/toast'
import { useSetMyDateRange, type PulseItemKind } from './api'
import { CUSTOM_RANGE, customRangeDays } from './format'

export interface RangeModalProps {
  kind: PulseItemKind
  id: number
  name: string
  /** Rango actual que trae el DTO de Pulso. */
  dateRangeMode: string | null | undefined
  fromUtc: string | null | undefined
  toUtc: string | null | undefined
  onClose: () => void
}

export function RangeModal({ kind, id, name, dateRangeMode, fromUtc, toUtc, onClose }: RangeModalProps) {
  const t = useT()
  const save = useSetMyDateRange()
  const modes = useLookups('DateRangeMode').data
  const schema = useMemo(
    () =>
      z
        .object({ dateRangeMode: z.string().min(1, t('analytics.range.errors.modeRequired')), dateFrom: z.string(), dateTo: z.string() })
        .superRefine((v, ctx) => {
          if (v.dateRangeMode !== CUSTOM_RANGE) return
          if (!v.dateFrom) ctx.addIssue({ code: 'custom', path: ['dateFrom'], message: t('analytics.range.errors.fromRequired') })
          if (!v.dateTo) ctx.addIssue({ code: 'custom', path: ['dateTo'], message: t('analytics.range.errors.toRequired') })
          if (v.dateFrom && v.dateTo && v.dateFrom > v.dateTo)
            ctx.addIssue({ code: 'custom', path: ['dateFrom'], message: t('analytics.range.errors.fromAfterTo') })
        }),
    [t],
  )
  const initial = dateRangeMode === CUSTOM_RANGE ? customRangeDays(fromUtc, toUtc) : { from: '', to: '' }
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { dateRangeMode: dateRangeMode ?? '', dateFrom: initial.from, dateTo: initial.to },
  })
  const mode = useWatch({ control: form.control, name: 'dateRangeMode' })
  const options = useMemo(() => (modes ?? []).map((m) => ({ value: m.code, label: m.label })), [modes])
  const formId = `pulse-range-${kind}-${id}`

  return (
    <Modal
      open
      size="sm"
      title={t('analytics.range.title', { name })}
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
      <p className="note">{t('analytics.range.help')}</p>
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const custom = v.dateRangeMode === CUSTOM_RANGE
          await save.mutateAsync({
            kind,
            id,
            body: { dateRangeMode: v.dateRangeMode, dateFrom: custom ? v.dateFrom : null, dateTo: custom ? v.dateTo : null },
          })
          toast.success(t('analytics.range.saved'))
          onClose()
        }}
      >
        <Field name="dateRangeMode" label={t('analytics.range.mode')} required>
          <Select options={options} placeholder={t('analytics.range.choose')} />
        </Field>
        {mode === CUSTOM_RANGE && (
          <div className="r2">
            <Field name="dateFrom" label={t('analytics.range.from')} required>
              <DateInput />
            </Field>
            <Field name="dateTo" label={t('analytics.range.to')} required>
              <DateInput />
            </Field>
          </div>
        )}
      </Form>
    </Modal>
  )
}
