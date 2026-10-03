// Ajustes → Región y formatos (maqueta `tenantRegionPanelsHtml`): la región carga de una vez la zona, la moneda y todos los
// formatos (juego de `GET /tenant/format-options`); después cada valor se cambia por separado ("Personalizada" + "Restaurar
// valores de la región"). Los separadores de miles y decimal nunca chocan: si el elegido choca, el otro se intercambia solo.
// La vista previa usa los valores del formulario (sin guardar); al guardar, el PUT lleva el juego completo y toda la app
// cambia sin recargar. Los 400 del servidor (`errors.<campo>`) quedan bajo su campo.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import {
  formatDate,
  formatDateLong,
  formatDayMonth,
  formatMoney,
  formatNumber,
  formatPhone,
  formatTime,
  regionDefaults,
  todayIso,
  toFormatSettings,
  utcNowText,
  type FormatSettings,
} from '../../../kernel/format'
import { useLang, useT } from '../../../kernel/i18n'
import { Chip, Field, Form, IconCash, IconClip, IconClock, IconGrid, IconPhoneFormat, IconPin, Panel, Select, TextInput, toast } from '../../../kernel/ui'
import { IconSearch } from '../../../kernel/ui/icons'
import {
  allowedLists,
  CURRENCIES,
  formIsCustom,
  PHONE_MASKS,
  previewSettings,
  regionCodes,
  regionRequestBody,
  TIME_ZONES,
  toRegionForm,
  withCurrent,
  type RegionFormValues,
} from '../regionForm'
import { useFormatOptions, useSaveTenantSettings, type TenantSettingsDto } from '../tenantSettingsApi'

const SEP_KEY: Record<string, string> = { ',': 'comma', '.': 'period', ' ': 'space' }

/** "Ahora", que avanza cada 15 s (vista previa de la hora y de "hoy"). */
function useNow(ms = 15_000): Date {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), ms)
    return () => clearInterval(id)
  }, [ms])
  return now
}

function Preview({ settings }: { settings: FormatSettings }) {
  const t = useT()
  const lang = useLang()
  const now = useNow()
  const today = todayIso(now, settings)
  const rows: [string, string][] = [
    ['pvToday', formatDate(today, settings)],
    ['pvNowUtc', utcNowText(now)],
    ['pvShort', formatDate(today, settings)],
    ['pvDayMonth', formatDayMonth(today, settings)],
    ['pvLong', formatDateLong(now, lang, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }, settings)],
    ['pvTime', formatTime(now, lang, {}, settings)],
    ['pvMoney', formatMoney(1234567.5, lang, {}, settings)],
    ['pvNeg', formatMoney(-12, lang, {}, settings)],
    ['pvNum', formatNumber(61023.125, { minimumFractionDigits: 3, maximumFractionDigits: 3 }, settings)],
    ['pvPhone', formatPhone('7875550142', settings)],
  ]
  return (
    <Panel className="set-preview" icon={<IconSearch />} title={t('system.settings.region.previewTitle')} subtitle={t('system.settings.region.previewHint')}>
      <dl aria-label={t('system.settings.region.previewTitle')} data-testid="format-preview">
        {rows.map(([k, v]) => (
          <div key={k} className="set-pv">
            <dt>{t(`system.settings.region.${k}`)}</dt>
            <dd data-pv={k}>{v}</dd>
          </div>
        ))}
      </dl>
    </Panel>
  )
}

export function RegionTab({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const { data: options } = useFormatOptions()
  const saved = useMemo(() => toFormatSettings(settings), [settings])
  const form = useForm<RegionFormValues>({ values: toRegionForm(saved) })
  const values = useWatch({ control: form.control }) as RegionFormValues
  const lists = allowedLists(options)
  const regions = regionCodes(options)
  const draftRegion = regionDefaults(values.regionCode ?? saved.regionCode, options)
  const custom = formIsCustom(values, draftRegion)
  const preview = useMemo(() => previewSettings(values, saved), [values, saved])
  const dirty = form.formState.isDirty
  // separadores: si el elegido choca con el otro, el otro se intercambia solo (setFmt de la maqueta). Se compara con los
  // valores anteriores para saber cuál tocó el usuario; al cargar una región completa los dos llegan juntos y no chocan.
  const { setValue } = form
  const prevSeps = useRef({ th: values.thousandsSeparator, dec: values.decimalSeparator })
  useEffect(() => {
    const prev = prevSeps.current
    const th = values.thousandsSeparator
    const dec = values.decimalSeparator
    prevSeps.current = { th, dec }
    if (th !== dec) return
    if (th !== prev.th) setValue('decimalSeparator', th === ',' ? '.' : ',', { shouldDirty: true })
    else if (dec !== prev.dec) setValue('thousandsSeparator', dec === ',' ? '.' : ',', { shouldDirty: true })
    else return
    toast.info(t('system.settings.region.sepSwapped'))
  }, [values.thousandsSeparator, values.decimalSeparator, setValue, t])

  /** Carga el juego completo de una región (botón de región o "Restaurar valores de la región"). */
  const applyRegion = (code: string) => {
    const r = regionDefaults(code, options)
    if (!r) return
    const next = toRegionForm(r)
    for (const [k, v] of Object.entries(next) as [keyof RegionFormValues, string][]) {
      form.setValue(k, v, { shouldDirty: true })
    }
    form.clearErrors()
    toast.info(t('system.settings.region.regionApplied'))
  }

  const opt = (value: string | number, label: string) => ({ value: String(value), label })
  const tzOptions = withCurrent(TIME_ZONES, values.timeZoneId ?? '').map((z) => {
    const key = `system.settings.region.tz.${z}`
    const label = t(key)
    return opt(z, label === key ? String(z) : label)
  })
  const curOptions = withCurrent(CURRENCIES, values.currencyCode ?? '').map((c) => {
    const key = `system.settings.region.currencies.${c}`
    const label = t(key)
    return opt(c, label === key ? String(c) : label)
  })
  const sepLabel = (s: string) => t(`system.settings.region.seps.${SEP_KEY[s] ?? 'comma'}`)

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        await save.mutateAsync(regionRequestBody(v))
        toast.success(t('system.settings.saved'))
      }}
    >
      <Panel
        icon={<IconPin />}
        title={t('system.settings.region.regionTitle')}
        actions={
          custom ? (
            <>
              <Chip tone="cap">{t('system.settings.region.regionCustom')}</Chip>
              {canEdit && (
                <button type="button" className="btn sm" onClick={() => applyRegion(values.regionCode)}>
                  {t('system.settings.region.regionReset')}
                </button>
              )}
            </>
          ) : undefined
        }
      >
        <div className="set-regions" role="group" aria-label={t('system.settings.region.regionTitle')}>
          {regions.map((code) => {
            const key = `system.settings.region.regions.${code}`
            const label = t(key) === key ? code : t(key)
            const on = values.regionCode === code
            return (
              <button
                key={code}
                type="button"
                className={on ? 'btn sm flow' : 'btn sm'}
                aria-pressed={on}
                disabled={!canEdit}
                onClick={() => applyRegion(code)}
              >
                {label}
              </button>
            )
          })}
        </div>
        <p className="set-d">{t('system.settings.region.regionHint')}</p>
      </Panel>

      <fieldset className="set-fs" disabled={!canEdit}>
        <div className="set-cols region set-gap">
          <div className="set-stack">
            <Panel icon={<IconClock />} title={t('system.settings.region.tzLabel')}>
              <Field name="timeZoneId" label={t('system.settings.region.tzLabel')} help={t('system.settings.region.tzHint')}>
                <Select options={tzOptions} />
              </Field>
            </Panel>

            <Panel icon={<IconCash />} title={t('system.settings.region.currencyTitle')}>
              <div className="r3">
                <Field name="currencyCode" label={t('system.settings.region.curLabel')}>
                  <Select options={curOptions} />
                </Field>
                <Field name="currencySymbol" label={t('system.settings.region.symLabel')}>
                  <TextInput maxLength={3} className="mono" />
                </Field>
                <Field name="currencyDecimals" label={t('system.settings.region.decLabel')}>
                  <Select options={withCurrent(lists.currencyDecimals, values.currencyDecimals ?? '').map((d) => opt(d, String(d)))} />
                </Field>
              </div>
              <div style={{ maxWidth: 300 }}>
                <Field name="currencySymbolPosition" label={t('system.settings.region.symPosLabel')}>
                  <Select
                    options={withCurrent(lists.currencySymbolPositions, values.currencySymbolPosition ?? '').map((p) =>
                      opt(p, p === 'A' ? t('system.settings.region.symAfter') : p === 'B' ? t('system.settings.region.symBefore') : String(p)),
                    )}
                  />
                </Field>
              </div>
              <p className="set-d">{t('system.settings.region.curHint')}</p>
            </Panel>

            <Panel icon={<IconClip />} title={t('system.settings.region.datetimeTitle')}>
              <div className="r3">
                <Field name="dateOrder" label={t('system.settings.region.dateFmtLabel')}>
                  <Select
                    options={withCurrent(lists.dateOrders, values.dateOrder ?? '').map((o) => {
                      const key = `system.settings.region.dateOrders.${o}`
                      return opt(o, t(key) === key ? String(o) : t(key))
                    })}
                  />
                </Field>
                <Field name="dateSeparator" label={t('system.settings.region.dateSepLabel')}>
                  <Select options={withCurrent(lists.dateSeparators, values.dateSeparator ?? '').map((s) => opt(s, String(s)))} />
                </Field>
                <Field name="timeFormat" label={t('system.settings.region.timeFmtLabel')}>
                  <Select
                    options={withCurrent(lists.timeFormats, values.timeFormat ?? '').map((h) =>
                      opt(h, String(h) === '24' ? t('system.settings.region.time24') : t('system.settings.region.time12')),
                    )}
                  />
                </Field>
              </div>
              <div style={{ maxWidth: 300 }}>
                <Field name="weekStartDay" label={t('system.settings.region.weekStartLabel')}>
                  <Select
                    options={withCurrent(lists.weekStartDays, values.weekStartDay ?? '').map((d) =>
                      opt(d, String(d) === '1' ? t('system.settings.region.mon') : t('system.settings.region.sun')),
                    )}
                  />
                </Field>
              </div>
            </Panel>

            <div className="set-cols">
              <Panel icon={<IconGrid />} title={t('system.settings.region.numbersTitle')}>
                <div className="r2">
                  <Field name="thousandsSeparator" label={t('system.settings.region.thouLabel')}>
                    <Select options={withCurrent(lists.thousandsSeparators, values.thousandsSeparator ?? '').map((s) => opt(s, sepLabel(String(s))))} />
                  </Field>
                  <Field name="decimalSeparator" label={t('system.settings.region.decSepLabel')}>
                    <Select options={withCurrent(lists.decimalSeparators, values.decimalSeparator ?? '').map((s) => opt(s, sepLabel(String(s))))} />
                  </Field>
                </div>
              </Panel>
              <Panel icon={<IconPhoneFormat />} title={t('system.settings.region.phoneTitle')}>
                <div className="r2">
                  <Field name="phoneCountryCode" label={t('system.settings.region.phoneCcLabel')}>
                    <TextInput maxLength={5} className="mono" inputMode="tel" />
                  </Field>
                  <Field name="phoneMask" label={t('system.settings.region.phoneMaskLabel')}>
                    <Select options={withCurrent(PHONE_MASKS, values.phoneMask ?? '').map((m) => opt(m, String(m).replace(/#/g, '0')))} />
                  </Field>
                </div>
                <p className="set-d">{t('system.settings.region.phoneHint')}</p>
              </Panel>
            </div>
          </div>
          <Preview settings={preview} />
        </div>
      </fieldset>

      {canEdit && (
        <div className="set-actions">
          {dirty && <p className="set-d">{t('system.settings.unsaved')}</p>}
          <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
            {t('system.settings.discard')}
          </button>
          <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
          </button>
        </div>
      )}
    </Form>
  )
}
