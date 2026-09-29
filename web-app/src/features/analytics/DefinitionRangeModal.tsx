// "Rango" de la tarjeta en Indicadores/Gráficos (Lote F8a P3): mismo diálogo y mismo `PUT .../my-date-range` que
// `RangeModal` de Pulso, pero las fechas iniciales vienen ya en 'YYYY-MM-DD' (`effectiveDateFrom`/`effectiveDateTo`
// del DTO de la lista) — sin el ajuste de `toUtc` exclusivo que usa Pulso con `IndicatorValueDto`/`ChartDatum`
// (`fromUtc`/`toUtc`, marca de tiempo UTC). Dos componentes chicos y sin estado compartido antes que una prop que
// cambie el comportamiento de `RangeModal` según de dónde se abra.
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
import { CUSTOM_RANGE } from './format'

export interface DefinitionRangeModalProps {
  kind: PulseItemKind
  id: number
  name: string
  dateRangeMode: string | null | undefined
  dateFrom: string | null | undefined
  dateTo: string | null | undefined
  onClose: () => void
}

export function DefinitionRangeModal({ kind, id, name, dateRangeMode, dateFrom, dateTo, onClose }: DefinitionRangeModalProps) {
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
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { dateRangeMode: dateRangeMode ?? '', dateFrom: dateFrom ?? '', dateTo: dateTo ?? '' },
  })
  const mode = useWatch({ control: form.control, name: 'dateRangeMode' })
  const options = useMemo(() => (modes ?? []).map((m) => ({ value: m.code, label: m.label })), [modes])
  const formId = `def-range-${kind}-${id}`

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
