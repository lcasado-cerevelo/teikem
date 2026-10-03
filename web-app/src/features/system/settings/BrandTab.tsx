// Ajustes → Marca (maqueta `tenantBrandPanelHtml`): 13 temas predefinidos o colores propios (operación, dinero y tono base),
// con la validación de contraste WCAG en los dos modos y de separación de matiz (`brandChecks`, kernel/ui/brandTheme). La app
// se ve con el borrador mientras se edita (`setBrandPreview`); "Guardar" lo deja en `Tenant.BrandingJson` y "Descartar" vuelve
// a lo guardado. El servidor repite las validaciones (BrandingRules) y su 400 se muestra junto a los colores. Los cuatro logos
// (lockup y marca cuadrada, cada uno con su variante para fondo oscuro) se suben y se quitan al instante por
// `/tenant/brand/logos`; los errores 400/413/415 del servidor salen junto a la ranura. Sin admin.tenant todo es solo lectura.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useLang, useT } from '../../../kernel/i18n'
import {
  BRAND_PRESETS,
  brandChecks,
  brandColors,
  DEFAULT_BRAND,
  IconTag,
  isInvertedSlot,
  isValidHex,
  LOGO_ACCEPT,
  LOGO_MAX_BYTES,
  LOGO_SLOTS,
  normalizeHex,
  Panel,
  parseBranding,
  serializeBranding,
  setBrandPreview,
  toast,
  useBrandLogoList,
  useCompanyLogos,
  type BrandLogoDto,
  type BrandSettings,
  type LogoSlot,
} from '../../../kernel/ui'
import { IconAlert, IconCheck } from '../../../kernel/ui/icons'
import { problemText } from '../../warehouse/problemText'
import { useRemoveBrandLogo, useSaveTenantSettings, useUploadBrandLogo, type TenantSettingsDto } from '../tenantSettingsApi'

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

/** Tipo y tamaño legibles de un logo guardado: "PNG · 12 KB". */
function logoInfo(l: BrandLogoDto | undefined): string {
  if (!l) return ''
  const kind = l.contentType === 'image/svg+xml' ? 'SVG' : l.contentType === 'image/jpeg' ? 'JPG' : l.contentType === 'image/webp' ? 'WebP' : 'PNG'
  return `${kind} · ${Math.max(1, Math.round((l.sizeBytes ?? 0) / 1024))} KB`
}

/** Una ranura: vista previa sobre fondo claro u oscuro según corresponda, subir/reemplazar y quitar; el error del servidor, debajo. */
function LogoSlotCard({ slot, logo, url, canEdit }: { slot: LogoSlot; logo: BrandLogoDto | undefined; url: string | undefined; canEdit: boolean }) {
  const t = useT()
  const upload = useUploadBrandLogo()
  const remove = useRemoveBrandLogo()
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | null>(null)
  const busy = upload.isPending || remove.isPending
  const name = t(`system.settings.brand.slots.${slot}`)
  const errId = `logo-${slot}-err`

  const choose = async (file: File | undefined) => {
    if (!file) return
    setError(null)
    // el servidor es quien decide (contenido real); el tope de tamaño se avisa antes de mandar un archivo enorme
    if (file.size > LOGO_MAX_BYTES) {
      setError(t('system.settings.brand.logoTooBig'))
      return
    }
    try {
      await upload.mutateAsync({ slot, file })
      toast.success(t('system.settings.brand.logoUploaded'))
    } catch (err) {
      setError(problemText(err))
    }
  }
  const clear = async () => {
    setError(null)
    try {
      await remove.mutateAsync(slot)
      toast.success(t('system.settings.brand.logoRemoved'))
    } catch (err) {
      setError(problemText(err))
    }
  }

  return (
    <div className="set-logo" data-testid={`logo-${slot}`}>
      <b className="set-logo-name">{name}</b>
      <div className={isInvertedSlot(slot) ? 'set-logo-prev dark' : 'set-logo-prev'}>
        {url ? <img src={url} alt={t('system.settings.brand.logoPreview', { name })} /> : <span>{t('system.settings.brand.logoNone')}</span>}
      </div>
      <p className="set-d">{t(`system.settings.brand.slotHints.${slot}`)}</p>
      {logo && <p className="set-sub mono">{logoInfo(logo)}</p>}
      {canEdit && (
        <div className="set-logo-actions">
          <input
            ref={input}
            type="file"
            accept={LOGO_ACCEPT}
            hidden
            aria-label={t('system.settings.brand.logoFile', { name })}
            aria-describedby={error ? errId : undefined}
            onChange={(e) => {
              void choose(e.target.files?.[0])
              e.target.value = ''
            }}
          />
          <button type="button" className="btn sm" disabled={busy} onClick={() => input.current?.click()}>
            {logo ? t('system.settings.brand.logoReplace') : t('system.settings.brand.logoUpload')}
          </button>
          {logo && (
            <button type="button" className="btn sm" disabled={busy} aria-label={`${t('system.settings.brand.logoRemove')} ${name}`} onClick={() => void clear()}>
              {t('system.settings.brand.logoRemove')}
            </button>
          )}
        </div>
      )}
      {error && (
        <p id={errId} className="ferr" role="alert">
          {error}
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
  const [saveError, setSaveError] = useState<string | null>(null)
  const logoList = useBrandLogoList()
  const company = useCompanyLogos()
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
    setSaveError(null)
    try {
      await save.mutateAsync({ brandingJson: serializeBranding(draft) })
      toast.success(t('system.settings.brand.saved'))
    } catch (err) {
      // el 400 del servidor (BrandingRules) sale junto a los colores, con su mensaje exacto
      const message = problemText(err)
      setSaveError(message)
      toast.error(message)
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
      {saveError && (
        <p className="ferr" role="alert" data-testid="brand-save-error" style={{ margin: '-8px 0 16px' }}>
          {saveError}
        </p>
      )}

      <h3 className="set-h3">{t('system.settings.brand.logoTitle')}</h3>
      <p className="set-d" style={{ marginBottom: 10 }}>
        {t('system.settings.brand.logoHint')}
      </p>
      <div className="set-logos">
        {LOGO_SLOTS.map((slot) => (
          <LogoSlotCard key={slot} slot={slot} logo={logoList.data?.find((l) => l.slot === slot)} url={company.urls[slot]} canEdit={canEdit} />
        ))}
      </div>

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
