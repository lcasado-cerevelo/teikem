// Ajustes → Marca (maqueta `tenantBrandPanelHtml`): 13 temas predefinidos o colores propios (operación, dinero y tono base),
// con la validación de contraste WCAG en los dos modos y de separación de matiz (`brandChecks`, kernel/ui/brandTheme). La app
// se ve con el borrador mientras se edita (`setBrandPreview`); "Guardar" lo deja en `Tenant.BrandingJson` y "Descartar" vuelve
// a lo guardado. Los logos por compañía quedan pendientes de backend (no hay dónde guardar los archivos).
import { useEffect, useMemo, useState } from 'react'
import { useLang, useT } from '../../../kernel/i18n'
import {
  BRAND_PRESETS,
  brandChecks,
  brandColors,
  DEFAULT_BRAND,
  IconTag,
  isValidHex,
  normalizeHex,
  Panel,
  parseBranding,
  serializeBranding,
  setBrandPreview,
  toast,
  type BrandSettings,
} from '../../../kernel/ui'
import { IconAlert, IconCheck } from '../../../kernel/ui/icons'
import { problemText } from '../../warehouse/problemText'
import { useSaveTenantSettings, type TenantSettingsDto } from '../tenantSettingsApi'

type ColorKey = 'flow' | 'money' | 'neutral'

function sameBrand(a: BrandSettings, b: BrandSettings): boolean {
  return JSON.stringify(a) === JSON.stringify(b)
}

function ColorField({ k, value, disabled, onChange }: { k: ColorKey; value: string; disabled: boolean; onChange: (hex: string) => void }) {
  const t = useT()
  // el padre la monta con `key={value}`: un color elegido con el selector reinicia el texto
  const [text, setText] = useState(value)
  const [error, setError] = useState(false)
  const label = t(`system.settings.brand.${k}Label`)
  const id = `brand-${k}`
  return (
    <div className="f" style={{ margin: 0 }}>
      <label htmlFor={id}>{label}</label>
      <div className="set-color">
        <input type="color" aria-label={label} value={value} disabled={disabled} onChange={(e) => onChange(normalizeHex(e.target.value))} />
        <input
          id={id}
          className="mono"
          value={text}
          disabled={disabled}
          maxLength={7}
          aria-label={t('system.settings.brand.hexLabel', { name: label })}
          aria-invalid={error || undefined}
          aria-describedby={`${id}-help`}
          onChange={(e) => setText(e.target.value)}
          onBlur={() => {
            if (isValidHex(text)) {
              setError(false)
              onChange(normalizeHex(text))
            } else setError(true)
          }}
        />
      </div>
      {error ? (
        <p id={`${id}-help`} className="ferr" role="alert">
          {t('system.settings.brand.hexInvalid')}
        </p>
      ) : (
        <p id={`${id}-help`} className="set-d" style={{ marginTop: 4 }}>
          {t(`system.settings.brand.${k}Hint`)}
        </p>
      )}
    </div>
  )
}

/** Se vuelve a montar (key) cuando cambia la marca guardada: el borrador arranca de ella. */
export function BrandTab({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  return <BrandEditor key={settings.brandingJson ?? ''} settings={settings} canEdit={canEdit} />
}

function BrandEditor({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const lang = useLang()
  const save = useSaveTenantSettings()
  const saved = useMemo(() => parseBranding(settings.brandingJson), [settings.brandingJson])
  const [draft, setDraft] = useState<BrandSettings>(saved)
  const dirty = !sameBrand(draft, saved)
  const checks = brandChecks(draft)
  const pass = checks.every((c) => c.pass)
  const colors = brandColors(draft)

  // vista previa en toda la app mientras hay cambios; al salir de la pestaña, vuelve lo guardado
  useEffect(() => {
    setBrandPreview(dirty ? draft : null)
  }, [dirty, draft])
  useEffect(() => () => setBrandPreview(null), [])

  const choosePreset = (id: string) => {
    const p = BRAND_PRESETS.find((x) => x.id === id)
    if (!p) return
    setDraft((d) => ({ preset: id, useCustom: false, custom: { ...d.custom, flow: p.flow, money: p.money } }))
  }
  const setColor = (k: ColorKey, hex: string) => setDraft((d) => ({ ...d, useCustom: true, custom: { ...d.custom, [k]: hex } }))

  const submit = async () => {
    if (!pass) {
      toast.error(t('system.settings.brand.blocked'))
      return
    }
    try {
      await save.mutateAsync({ brandingJson: serializeBranding(draft, settings.brandingJson) })
      toast.success(t('system.settings.brand.saved'))
    } catch (err) {
      toast.error(problemText(err))
    }
  }

  const swatch = (hex: string) => <span className="set-swatch" style={{ background: hex }} aria-hidden="true" />
  const hue = checks.find((c) => c.type === 'hue')

  return (
    <Panel
      icon={<IconTag />}
      title={t('system.settings.brand.brandTitle')}
      actions={
        canEdit ? (
          <button
            type="button"
            className="btn sm"
            onClick={() => {
              setDraft(DEFAULT_BRAND)
              toast.info(t('system.settings.brand.brandReset'))
            }}
          >
            {t('system.settings.brand.brandResetBtn')}
          </button>
        ) : undefined
      }
    >
      <p className="set-d" style={{ marginBottom: 12 }}>
        {t('system.settings.brand.brandHint')}
      </p>
      <h3 className="set-h3">{t('system.settings.brand.presetsTitle')}</h3>
      <div className="set-presets" role="group" aria-label={t('system.settings.brand.presetsTitle')}>
        {BRAND_PRESETS.map((p) => {
          const on = !draft.useCustom && draft.preset === p.id
          return (
            <button key={p.id} type="button" className={on ? 'btn sm flow' : 'btn sm'} aria-pressed={on} disabled={!canEdit} onClick={() => choosePreset(p.id)}>
              {swatch(p.flow)}
              {swatch(p.money)} {lang === 'en' ? p.nameEn : p.nameEs}
            </button>
          )
        })}
      </div>

      <label className="sw" style={{ marginBottom: 10 }}>
        <input type="checkbox" role="switch" checked={draft.useCustom} disabled={!canEdit} onChange={(e) => setDraft((d) => ({ ...d, useCustom: e.target.checked }))} />
        <span className="tk" aria-hidden="true" />
        <span>{t('system.settings.brand.customTitle')}</span>
      </label>
      {draft.useCustom && (
        <div className="r3" style={{ margin: '10px 0' }}>
          <ColorField key={`flow-${colors.flow}`} k="flow" value={colors.flow} disabled={!canEdit} onChange={(h) => setColor('flow', h)} />
          <ColorField key={`money-${colors.money}`} k="money" value={colors.money} disabled={!canEdit} onChange={(h) => setColor('money', h)} />
          <ColorField key={`neutral-${draft.custom.neutral}`} k="neutral" value={draft.custom.neutral} disabled={!canEdit} onChange={(h) => setColor('neutral', h)} />
        </div>
      )}

      <div className={`note set-checks ${pass ? 'ok' : 'bad'}`} role="status" data-testid="brand-checks">
        <b>
          {pass ? <IconCheck /> : <IconAlert />} {pass ? t('system.settings.brand.checksOk') : t('system.settings.brand.checksWarn')}
        </b>
        {checks.map((c) =>
          c.type === 'contrast' ? (
            <div key={`${c.key}-${c.mode}`} className={c.pass ? undefined : 'fail'}>
              {c.pass ? '✓' : '✕'} {t(`system.settings.brand.acc.${c.key}`)} · {c.mode === 'dark' ? t('system.settings.brand.modeDark') : t('system.settings.brand.modeLight')} —{' '}
              {t('system.settings.brand.contrastWord')} {c.ratio}:1 {c.pass ? '' : `(${t('system.settings.brand.contrastMin')} ${c.min}:1)`}
            </div>
          ) : null,
        )}
        {hue && (
          <div className={hue.pass ? undefined : 'fail'}>
            {hue.pass ? '✓' : '✕'} {t('system.settings.brand.hueWord')} {hue.distance}° {hue.pass ? '' : `(${t('system.settings.brand.hueMin')} ${hue.min}°)`}
          </div>
        )}
        <div>{t('system.settings.brand.serverNote')}</div>
      </div>

      <h3 className="set-h3">{t('system.settings.brand.logoTitle')}</h3>
      <p className="set-d">{t('system.settings.brand.logoPending')}</p>

      {canEdit && (
        <div className="set-actions">
          {dirty && <p className="set-d">{t('system.settings.brand.previewNote')}</p>}
          <button type="button" className="btn" disabled={!dirty || save.isPending} onClick={() => setDraft(saved)}>
            {t('system.settings.discard')}
          </button>
          <button type="button" className="btn flow" disabled={!dirty || save.isPending} onClick={() => void submit()}>
            {save.isPending ? t('system.settings.saving') : t('system.settings.save')}
          </button>
        </div>
      )}
    </Panel>
  )
}
