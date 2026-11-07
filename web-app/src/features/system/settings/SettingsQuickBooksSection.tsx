// Ajustes → Operación → QuickBooks (Lote 225, plan §1.4): las cuentas y los ítems que lleva el archivo .iif de QuickBooks Desktop y el nombre en QuickBooks de cada término de pago.
// Leer: sales.quickbooks.view o admin.tenant; cambiar: admin.tenant. Un campo vacío vuelve al valor por omisión; un término sin nombre sale sin término en el archivo.
// La hora del aviso de la mañana es la del trabajo programado SALES_QUICKBOOKS_PENDING (Sistema → Trabajos programados).
import { useState } from 'react'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { IconDoc } from '../../../kernel/ui/screenIcons'
import { Panel, Spinner, toast } from '../../../kernel/ui'
import { useQuickBooksSettings, useSaveQuickBooksSettings } from '../../sales/quickBooksApi'
import {
  maxLengthOf,
  QB_ACCOUNT_FIELDS,
  QB_ITEM_FIELDS,
  QB_TERM_MAX,
  quickBooksDraftError,
  quickBooksDraftOf,
  quickBooksSettingsRequest,
  type QbTextField,
  type QuickBooksSettingsDraft,
} from '../../sales/quickBooksRules'
import '../../sales/quickbooks.css'

export function SettingsQuickBooksSection({ canEdit }: { canEdit: boolean }) {
  const t = useT()
  const settings = useQuickBooksSettings()
  const save = useSaveQuickBooksSettings()
  const [draft, setDraft] = useState<QuickBooksSettingsDraft>(() => quickBooksDraftOf(settings.data))
  const [error, setError] = useState<string | null>(null)

  // El borrador se rehace cuando llegan (o se guardan) los ajustes del servidor: estado derivado durante el render, sin efecto.
  const [loaded, setLoaded] = useState(settings.data)
  if (settings.data !== loaded) {
    setLoaded(settings.data)
    if (settings.data) setDraft(quickBooksDraftOf(settings.data))
  }

  const original = quickBooksDraftOf(settings.data)
  const body = settings.data ? quickBooksSettingsRequest(draft, original) : null
  const setField = (k: QbTextField, v: string) => {
    setError(null)
    setDraft((d) => ({ ...d, fields: { ...d.fields, [k]: v } }))
  }
  const setTerm = (code: string, v: string) => {
    setError(null)
    setDraft((d) => ({ ...d, terms: { ...d.terms, [code]: v } }))
  }

  async function submit() {
    const problem = quickBooksDraftError(draft)
    if (problem) return setError(t(`system.settings.quickbooks.errors.${problem}`))
    if (!body) return
    setError(null)
    try {
      await save.mutateAsync(body)
      toast.success(t('system.settings.quickbooks.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  const field = (k: QbTextField) => (
    <div className="f" key={k}>
      <label htmlFor={`set-qb-${k}`}>{t(`system.settings.quickbooks.fields.${k}`)}</label>
      <input id={`set-qb-${k}`} value={draft.fields[k]} maxLength={maxLengthOf(k)} onChange={(e) => setField(k, e.target.value)} autoComplete="off" />
    </div>
  )
  const terms = settings.data?.terms ?? []

  return (
    <Panel icon={<IconDoc />} title={t('system.settings.quickbooks.title')} subtitle={t('system.settings.quickbooks.subtitle')}>
      {settings.isPending ? (
        <Spinner block />
      ) : settings.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(settings.error).title}</p>
      ) : (
        <>
          <fieldset className="set-fs" disabled={!canEdit}>
            <h3 className="qb-sub">{t('system.settings.quickbooks.accounts')}</h3>
            <div className="r2">{QB_ACCOUNT_FIELDS.map(field)}</div>
            <p className="help">{t('system.settings.quickbooks.accountsHelp')}</p>
            <h3 className="qb-sub">{t('system.settings.quickbooks.items')}</h3>
            <div className="r2">{QB_ITEM_FIELDS.map(field)}</div>
            <p className="help">{t('system.settings.quickbooks.itemsHelp')}</p>
            <h3 className="qb-sub">{t('system.settings.quickbooks.terms')}</h3>
            {terms.length === 0 ? (
              <p className="help">{t('system.settings.quickbooks.noTerms')}</p>
            ) : (
              <table className="qb-terms">
                <thead>
                  <tr>
                    <th scope="col">{t('system.settings.quickbooks.termCol')}</th>
                    <th scope="col">{t('system.settings.quickbooks.termName')}</th>
                  </tr>
                </thead>
                <tbody>
                  {terms.map((x) => (
                    <tr key={x.code}>
                      <td>
                        {x.label || x.code} <span className="code mono">{x.code}</span>
                      </td>
                      <td>
                        <input
                          aria-label={t('system.settings.quickbooks.termNameOf', { term: x.label || x.code || '' })}
                          value={draft.terms[x.code ?? ''] ?? ''}
                          maxLength={QB_TERM_MAX}
                          onChange={(e) => setTerm(x.code ?? '', e.target.value)}
                          autoComplete="off"
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <p className="help">{t('system.settings.quickbooks.termsHelp')}</p>
            <p className="help">{t('system.settings.quickbooks.morningHelp')}</p>
          </fieldset>
          {error && <p className="ferr" role="alert">{error}</p>}
          {canEdit && (
            <div className="set-actions">
              <button type="button" className="btn" disabled={!body || save.isPending} onClick={() => (setError(null), setDraft(original))}>
                {t('system.settings.discard')}
              </button>
              <button type="button" className="btn flow" disabled={!body || save.isPending} onClick={() => void submit()}>
                {save.isPending ? t('system.settings.saving') : t('system.settings.save')}
              </button>
            </div>
          )}
        </>
      )}
    </Panel>
  )
}
