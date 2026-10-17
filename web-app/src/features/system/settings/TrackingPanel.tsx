// Ajustes → General → «Seguimiento público» (Lote 103): la compañía enciende o apaga la página «¿Cómo va mi orden?» poniendo o quitando su identificador. El enlace que se comparte es <app del cliente>/track/<identificador>;
// con ?n=<conduce> abre ya con la consulta hecha (para los avisos). La página solo muestra el avance del paquete, nunca nombres, direcciones ni teléfonos.
import { useEffect, useState } from 'react'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { Chip, IconPin, Panel, Spinner, toast } from '../../../kernel/ui'
import { trackingLink, useSaveTracking, useTrackingSettings } from '../trackingApi'

export function TrackingPanel({ canEdit }: { canEdit: boolean }) {
  const t = useT()
  const tracking = useTrackingSettings()
  const save = useSaveTracking()
  const [slug, setSlug] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (tracking.data) setSlug(tracking.data.slug ?? '')
  }, [tracking.data])

  const enabled = !!tracking.data?.enabled
  const dirty = (tracking.data?.slug ?? '') !== slug.trim().toLowerCase()

  async function submit(value: string) {
    setError(null)
    try {
      await save.mutateAsync(value.trim().toLowerCase())
      toast.success(t(value.trim() ? 'system.settings.tracking.saved' : 'system.settings.tracking.off'))
    } catch (e) {
      const p = applyProblemDetails(e)
      setError(p.errors?.slug?.[0] ?? p.title)
    }
  }

  return (
    <Panel icon={<IconPin />} title={t('system.settings.tracking.title')} subtitle={t('system.settings.tracking.subtitle')}>
      {tracking.isPending ? (
        <Spinner block />
      ) : tracking.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(tracking.error).title}</p>
      ) : (
        <>
          <p className="set-d">
            <Chip tone={enabled ? 'deliv' : 'neutral'}>{enabled ? t('system.settings.tracking.on') : t('system.settings.tracking.disabled')}</Chip>
          </p>
          <div className="f">
            <label htmlFor="set-tracking-slug">{t('system.settings.tracking.slugLabel')}</label>
            <input id="set-tracking-slug" value={slug} disabled={!canEdit} maxLength={40} autoComplete="off" spellCheck={false} placeholder={t('system.settings.tracking.slugPlaceholder')} onChange={(e) => setSlug(e.target.value)} />
            <p className="help">{t('system.settings.tracking.slugHelp')}</p>
          </div>
          {error && <p className="ferr" role="alert">{error}</p>}
          {enabled && tracking.data?.slug && (
            <div className="f">
              <label htmlFor="set-tracking-link">{t('system.settings.tracking.linkLabel')}</label>
              <input id="set-tracking-link" className="mono" readOnly value={trackingLink(tracking.data.slug)} onFocus={(e) => e.currentTarget.select()} />
              <p className="help">{t('system.settings.tracking.linkHelp')}</p>
            </div>
          )}
          {canEdit && (
            <div className="set-actions">
              {enabled && (
                <button type="button" className="btn" disabled={save.isPending} onClick={() => void submit('')}>
                  {t('system.settings.tracking.turnOff')}
                </button>
              )}
              <button type="button" className="btn flow" disabled={!dirty || !slug.trim() || save.isPending} onClick={() => void submit(slug)}>
                {save.isPending ? t('system.settings.saving') : enabled ? t('system.settings.save') : t('system.settings.tracking.turnOn')}
              </button>
            </div>
          )}
        </>
      )}
    </Panel>
  )
}
