// Ajustes → General → «Devolución» (Lote 113): con cuántos intentos fallidos se SUGIERE devolver una orden (por omisión 2; 0 = sin sugerencia). Solo una sugerencia y una marca: nunca bloquea.
import { useEffect, useState } from 'react'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { IconBox, Panel, Spinner, toast } from '../../../kernel/ui'
import { useReturnSettings, useSaveReturnSettings } from '../returnSettingsApi'

export function ReturnSuggestPanel({ canEdit }: { canEdit: boolean }) {
  const t = useT()
  const settings = useReturnSettings()
  const save = useSaveReturnSettings()
  const [value, setValue] = useState('2')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (settings.data) setValue(String(settings.data.suggestAfterAttempts ?? 2))
  }, [settings.data])

  const n = Number(value)
  const valid = value.trim() !== '' && Number.isInteger(n) && n >= 0 && n <= 20
  const dirty = settings.data !== undefined && String(settings.data.suggestAfterAttempts) !== value.trim()

  async function submit() {
    if (!valid) return setError(t('system.settings.returns.invalid'))
    setError(null)
    try {
      await save.mutateAsync(n)
      toast.success(t('system.settings.returns.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel icon={<IconBox />} title={t('system.settings.returns.title')} subtitle={t('system.settings.returns.subtitle')}>
      {settings.isPending ? (
        <Spinner block />
      ) : settings.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(settings.error).title}</p>
      ) : (
        <>
          <div className="f">
            <label htmlFor="set-return-after">{t('system.settings.returns.label')}</label>
            <input id="set-return-after" type="number" min={0} max={20} inputMode="numeric" value={value} disabled={!canEdit} onChange={(e) => (setError(null), setValue(e.target.value))} />
            <p className="help">{t('system.settings.returns.help')}</p>
          </div>
          {error && <p className="ferr" role="alert">{error}</p>}
          {canEdit && (
            <div className="set-actions">
              <button type="button" className="btn flow" disabled={!dirty || save.isPending} onClick={() => void submit()}>
                {save.isPending ? t('system.settings.saving') : t('system.settings.save')}
              </button>
            </div>
          )}
        </>
      )}
    </Panel>
  )
}
