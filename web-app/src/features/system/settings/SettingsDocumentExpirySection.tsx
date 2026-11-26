// Ajustes → Operación → Avisos de vencimiento (Lote 248, admin.tenant, módulo CATALOG): con cuántos días de anticipación avisa el programa que un documento del cliente (o un precio especial)
// se vence, si sigue avisando a diario después de vencido y si el aviso también le llega al cliente. Se guarda por PUT /tenant/settings. Las reglas de aviso (a quién y por qué canal) se ajustan
// en Sistema → Reglas de aviso, con los eventos «Documento del cliente por vencer» y «vencido».
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Can } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { Chip, Panel, toast } from '../../../kernel/ui'
import { IconClock } from '../../../kernel/ui/screenIcons'
import { useSaveTenantSettings, type TenantSettingsDto } from '../tenantSettingsApi'
import { DOC_EXPIRY_DEFAULT_THRESHOLDS, parseThreshold, sameThresholds, sortThresholds, thresholdsError } from './operations'

export function SettingsDocumentExpirySection({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const saved = sortThresholds(settings.docExpiryThresholdDays ?? DOC_EXPIRY_DEFAULT_THRESHOLDS)
  const [days, setDays] = useState<number[]>(saved)
  const [daily, setDaily] = useState(settings.docExpiryDailyAfterExpiry === true)
  const [client, setClient] = useState(settings.docExpiryNotifyClient === true)
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)

  // al cambiar lo guardado (otra pestaña, recarga) el borrador vuelve a lo guardado
  useEffect(() => {
    setDays(sortThresholds(settings.docExpiryThresholdDays ?? DOC_EXPIRY_DEFAULT_THRESHOLDS))
    setDaily(settings.docExpiryDailyAfterExpiry === true)
    setClient(settings.docExpiryNotifyClient === true)
  }, [settings.docExpiryThresholdDays, settings.docExpiryDailyAfterExpiry, settings.docExpiryNotifyClient])

  const dirty = !sameThresholds(days, saved) || daily !== (settings.docExpiryDailyAfterExpiry === true) || client !== (settings.docExpiryNotifyClient === true)

  const add = () => {
    setError(null)
    const n = parseThreshold(text)
    if (n === null) return setError(t('system.settings.docExpiry.errors.format'))
    if (days.includes(n)) return setError(t('system.settings.docExpiry.errors.repeated'))
    const next = sortThresholds([...days, n])
    const problem = thresholdsError(next)
    if (problem) return setError(t(`system.settings.docExpiry.errors.${problem}`))
    setDays(next)
    setText('')
  }

  const submit = async () => {
    setError(null)
    const problem = thresholdsError(days)
    if (problem) return setError(t(`system.settings.docExpiry.errors.${problem}`))
    try {
      await save.mutateAsync({ docExpiryThresholdDays: days, docExpiryDailyAfterExpiry: daily, docExpiryNotifyClient: client })
      toast.success(t('system.settings.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  const label = (n: number) => (n > 0 ? t('system.settings.docExpiry.before', { n }) : n === 0 ? t('system.settings.docExpiry.sameDay') : t('system.settings.docExpiry.after', { n: -n }))

  return (
    <Panel icon={<IconClock />} title={t('system.settings.docExpiry.title')} subtitle={t('system.settings.docExpiry.subtitle')}>
      <fieldset className="set-fs" disabled={!canEdit}>
        <div className="f">
          <span id="dxs-days-l" className="lbl">{t('system.settings.docExpiry.thresholds')}</span>
          <ul className="dxs-chips" aria-labelledby="dxs-days-l">
            {days.length === 0 && <li className="help">{t('system.settings.docExpiry.noThresholds')}</li>}
            {days.map((n) => (
              <li key={n}>
                <Chip tone={n > 0 ? 'warn' : n === 0 ? 'route' : 'fail'}>{label(n)}</Chip>
                {canEdit && <button type="button" className="btn sm" aria-label={t('system.settings.docExpiry.removeOf', { name: label(n) })} onClick={() => setDays(days.filter((d) => d !== n))}>{t('system.settings.docExpiry.remove')}</button>}
              </li>
            ))}
          </ul>
          <div className="dxs-add">
            <input id="dxs-add" inputMode="numeric" value={text} aria-label={t('system.settings.docExpiry.addLabel')} placeholder={t('system.settings.docExpiry.addPlaceholder')} onChange={(e) => setText(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add() } }} />
            <button type="button" className="btn sm" onClick={add}>{t('system.settings.docExpiry.add')}</button>
          </div>
          <p className="help">{t('system.settings.docExpiry.thresholdsHelp')}</p>
        </div>
        <label className="sw">
          <input type="checkbox" role="switch" checked={daily} onChange={(e) => setDaily(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('system.settings.docExpiry.daily')}</span>
        </label>
        <p className="help">{t('system.settings.docExpiry.dailyHelp')}</p>
        <label className="sw">
          <input type="checkbox" role="switch" checked={client} onChange={(e) => setClient(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('system.settings.docExpiry.client')}</span>
        </label>
        <p className="help">{t('system.settings.docExpiry.clientHelp')}</p>
        <Can perm="notifications.manage">
          <p className="help" data-testid="doc-expiry-rules-help">
            {t('system.settings.docExpiry.rulesHelp')} <Link to="/system/notifications">{t('system.settings.docExpiry.rulesLink')}</Link>
          </p>
        </Can>
      </fieldset>
      {error && <p className="ferr" role="alert">{error}</p>}
      {canEdit && (
        <div className="set-actions">
          <button type="button" className="btn" disabled={!dirty || save.isPending} onClick={() => { setDays(saved); setDaily(settings.docExpiryDailyAfterExpiry === true); setClient(settings.docExpiryNotifyClient === true); setError(null) }}>{t('system.settings.discard')}</button>
          <button type="button" className="btn flow" disabled={!dirty || save.isPending} onClick={() => void submit()}>{save.isPending ? t('system.settings.saving') : t('system.settings.save')}</button>
        </div>
      )}
    </Panel>
  )
}
