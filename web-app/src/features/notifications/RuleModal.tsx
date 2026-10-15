// Modal de una regla de aviso (Lote 52): evento (solo al crear), nombre, activa, destinatarios (rol, usuario con buscador de nombre y correo,
// correo suelto, teléfono suelto) y, por canal, el interruptor y la plantilla en español e inglés con las {variables} del evento como
// fichas que se insertan. Los canales sin configurar no se pueden encender. «Enviar prueba» manda el texto GUARDADO a quien la pide.
import { useMemo, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLang, useT } from '../../kernel/i18n'
import { Modal, Spinner, toast } from '../../kernel/ui'
import { useRoles, useUsers } from '../system/api'
import {
  useNotificationChannels,
  useNotificationEvents,
  useRuleMutations,
  type NotificationRuleDto,
} from './notificationsApi'
import {
  buildRequest,
  CHANNELS,
  defaultChannels,
  emptyForm,
  formOfRule,
  insertVariable,
  isValidEmail,
  isValidPhone,
  MAX_BODY,
  MAX_SUBJECT,
  NEEDS_SUBJECT,
  recipientKey,
  validateForm,
  type ChannelForm,
  type RecipientForm,
  type RecipientKind,
  type RuleForm,
} from './ruleForm'
import { UserPicker, type PersonOption } from './UserPicker'

type TextField = 'subjectEs' | 'subjectEn' | 'bodyEs' | 'bodyEn'

export function RuleModal({ rule, onClose }: { rule: NotificationRuleDto | null; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const creating = rule === null
  const events = useNotificationEvents()
  const channels = useNotificationChannels()
  const roles = useRoles()
  const users = useUsers()
  const { create, update, test } = useRuleMutations()
  const [form, setForm] = useState<RuleForm>(() => (rule ? formOfRule(rule) : emptyForm()))
  const [errors, setErrors] = useState<{ name?: string; eventCode?: string }>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [kind, setKind] = useState<RecipientKind>('ROLE')
  const [loose, setLoose] = useState('')
  const [looseError, setLooseError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const focused = useRef<{ el: HTMLInputElement | HTMLTextAreaElement; channel: string; field: TextField } | null>(null)

  const event = events.data?.find((e) => e.code === form.eventCode)
  const configured = useMemo(() => new Map((channels.data ?? []).map((c) => [c.channel, !!c.isConfigured])), [channels.data])
  const people = useMemo<PersonOption[]>(
    () => (users.data ?? []).filter((u) => u.isActive && u.id != null).map((u) => ({ id: u.id!, name: u.fullName || u.email || `#${u.id}`, email: u.email ?? '' })),
    [users.data],
  )
  const taken = useMemo(() => new Set(form.recipients.map(recipientKey)), [form.recipients])
  const usedUsers = useMemo(() => new Set(form.recipients.filter((r) => r.kind === 'USER').map((r) => r.userId!)), [form.recipients])
  const eventName = (code: string) => {
    const e = events.data?.find((x) => x.code === code)
    return (lang === 'en' ? e?.nameEn : e?.name) ?? code
  }

  const setChannel = (channel: string, patch: Partial<ChannelForm>) => setForm((f) => ({ ...f, channels: f.channels.map((c) => (c.channel === channel ? { ...c, ...patch } : c)) }))

  const addRecipient = (r: RecipientForm) => {
    if (taken.has(recipientKey(r))) return
    setForm((f) => ({ ...f, recipients: [...f.recipients, r] }))
  }

  const addLoose = () => {
    const v = loose.trim()
    if (kind === 'EMAIL' && !isValidEmail(v)) return setLooseError(t('notifications.rules.errors.emailInvalid', { value: v }))
    if (kind === 'PHONE' && !isValidPhone(v)) return setLooseError(t('notifications.rules.errors.phoneInvalid', { value: v }))
    setLooseError(null)
    addRecipient({ kind, value: v, label: v })
    setLoose('')
  }

  const pickEvent = (code: string) => {
    const e = events.data?.find((x) => x.code === code)
    setForm((f) => ({ ...f, eventCode: code, channels: defaultChannels(e) }))
  }

  const insert = (variable: string) => {
    const cur = focused.current
    if (!cur) return
    const field = cur.field
    const channel = form.channels.find((c) => c.channel === cur.channel)
    if (!channel) return
    const { text, caret } = insertVariable(channel[field], variable, cur.el.selectionStart, cur.el.selectionEnd)
    setChannel(cur.channel, { [field]: text })
    requestAnimationFrame(() => {
      cur.el.focus()
      cur.el.setSelectionRange(caret, caret)
    })
  }

  const save = async () => {
    const found = validateForm(form, creating, t)
    setErrors(found)
    setServerError(null)
    if (found.name || found.eventCode) return
    setSaving(true)
    try {
      const body = buildRequest(form, rule)
      const saved = rule ? await update.mutateAsync({ publicId: rule.publicId!, body }) : await create.mutateAsync(body)
      toast.success(t(creating ? 'notifications.rules.created' : 'notifications.rules.saved', { name: saved.name ?? form.name.trim() }))
      onClose()
    } catch (e) {
      setServerError(applyProblemDetails(e).title)
    } finally {
      setSaving(false)
    }
  }

  const sendTest = async (channel: string) => {
    try {
      const r = await test.mutateAsync({ publicId: rule!.publicId!, channel })
      const name = t(`notifications.rules.channel.${channel}`)
      if (r.status === 'SENT') toast.success(t('notifications.rules.form.testSent', { channel: name }))
      else if (r.status === 'SKIPPED') toast.info(t('notifications.rules.form.testSkipped', { channel: name, why: r.message ?? '' }))
      else toast.error(t('notifications.rules.form.testFailed', { channel: name, why: r.message ?? '' }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const field = (c: ChannelForm, name: TextField, label: string, multiline: boolean) => {
    const max = name.startsWith('subject') ? MAX_SUBJECT : MAX_BODY
    const common = {
      id: `nr-${c.channel}-${name}`,
      value: c[name],
      maxLength: max,
      onFocus: (e: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement>) => {
        focused.current = { el: e.currentTarget, channel: c.channel, field: name }
      },
      onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setChannel(c.channel, { [name]: e.target.value }),
    }
    return (
      <div className="f" key={name}>
        <label htmlFor={common.id}>{label}</label>
        {multiline ? <textarea rows={3} {...common} /> : <input type="text" {...common} />}
      </div>
    )
  }

  const loading = events.isPending || channels.isPending

  return (
    <Modal
      open
      size="lg"
      title={creating ? t('notifications.rules.new') : t('notifications.rules.edit')}
      onClose={onClose}
      dismissible={!saving}
      footer={
        <>
          <button type="button" className="btn" disabled={saving} onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={saving || loading} onClick={() => void save()}>
            {saving ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <div className="pb nr-form">
        {loading ? (
          <Spinner block />
        ) : (
          <>
            <div className="r2">
              <div className="f">
                <label htmlFor="nr-event">{t('notifications.rules.form.event')}</label>
                {creating ? (
                  <select id="nr-event" value={form.eventCode} onChange={(e) => pickEvent(e.target.value)} aria-invalid={!!errors.eventCode}>
                    <option value="">—</option>
                    {(events.data ?? []).map((e) => (
                      <option key={e.code} value={e.code}>
                        {lang === 'en' ? e.nameEn : e.name}
                      </option>
                    ))}
                  </select>
                ) : (
                  <input id="nr-event" value={eventName(form.eventCode)} disabled readOnly />
                )}
                <p className="help">{t('notifications.rules.form.eventHelp')}</p>
                {errors.eventCode && (
                  <p className="ferr" role="alert">
                    {errors.eventCode}
                  </p>
                )}
              </div>
              <div className="f">
                <label htmlFor="nr-name">{t('notifications.rules.form.name')}</label>
                <input id="nr-name" value={form.name} maxLength={100} autoComplete="off" aria-invalid={!!errors.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} />
                {errors.name && (
                  <p className="ferr" role="alert">
                    {errors.name}
                  </p>
                )}
              </div>
            </div>
            <div className="f">
              <label className="sw">
                <input type="checkbox" role="switch" checked={form.isActive} onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))} />
                <span className="tk" aria-hidden="true" />
                <span>{t('notifications.rules.form.activeText')}</span>
              </label>
              <p className="help">{t('notifications.rules.form.activeHelp')}</p>
            </div>

            <fieldset className="nr-box">
              <legend>{t('notifications.rules.form.recipients')}</legend>
              {form.recipients.length === 0 ? (
                <p className="help">{t('notifications.rules.form.noRecipients')}</p>
              ) : (
                <ul className="nr-chips">
                  {form.recipients.map((r) => (
                    <li key={recipientKey(r)} className="nr-chip">
                      <span className="nr-kind">{t(`notifications.rules.form.kind${r.kind}`)}</span> {r.label}
                      <button
                        type="button"
                        aria-label={t('notifications.rules.form.remove', { name: r.label })}
                        onClick={() => setForm((f) => ({ ...f, recipients: f.recipients.filter((x) => recipientKey(x) !== recipientKey(r)) }))}
                      >
                        ✕
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              <div className="nr-add">
                <div className="f nr-kindsel">
                  <label htmlFor="nr-kind">{t('notifications.rules.form.kind')}</label>
                  <select
                    id="nr-kind"
                    value={kind}
                    onChange={(e) => {
                      setKind(e.target.value as RecipientKind)
                      setLoose('')
                      setLooseError(null)
                    }}
                  >
                    {(['ROLE', 'USER', 'EMAIL', 'PHONE'] as const).map((k) => (
                      <option key={k} value={k}>
                        {t(`notifications.rules.form.kind${k}`)}
                      </option>
                    ))}
                  </select>
                </div>
                {kind === 'ROLE' && (
                  <div className="f">
                    <label htmlFor="nr-role">{t('notifications.rules.form.kindROLE')}</label>
                    <select
                      id="nr-role"
                      value=""
                      onChange={(e) => {
                        const r = (roles.data ?? []).find((x) => String(x.id) === e.target.value)
                        if (r) addRecipient({ kind: 'ROLE', roleId: r.id, label: r.name ?? '' })
                      }}
                    >
                      <option value="">{t('notifications.rules.form.pickRole')}</option>
                      {(roles.data ?? [])
                        .filter((r) => r.isActive !== false && !r.isTemplate && !taken.has(`R${r.id}`))
                        .map((r) => (
                          <option key={r.id} value={String(r.id)}>
                            {r.name}
                          </option>
                        ))}
                    </select>
                  </div>
                )}
                {kind === 'USER' && (
                  <div className="f">
                    <label htmlFor="nr-user-label">{t('notifications.rules.form.kindUSER')}</label>
                    <UserPicker
                      label={t('notifications.rules.form.pickUser')}
                      placeholder={t('notifications.rules.form.pickUser')}
                      people={people}
                      exclude={usedUsers}
                      onPick={(p) => addRecipient({ kind: 'USER', userId: p.id, label: `${p.name} (${p.email})` })}
                    />
                  </div>
                )}
                {(kind === 'EMAIL' || kind === 'PHONE') && (
                  <div className="f">
                    <label htmlFor="nr-loose">{t(`notifications.rules.form.kind${kind}`)}</label>
                    <div className="nr-row">
                      <input
                        id="nr-loose"
                        type={kind === 'EMAIL' ? 'email' : 'tel'}
                        value={loose}
                        placeholder={t(kind === 'EMAIL' ? 'notifications.rules.form.emailPh' : 'notifications.rules.form.phonePh')}
                        autoComplete="off"
                        onChange={(e) => setLoose(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') {
                            e.preventDefault()
                            addLoose()
                          }
                        }}
                      />
                      <button type="button" className="btn" onClick={addLoose}>
                        {t('notifications.rules.form.addRecipient')}
                      </button>
                    </div>
                    {looseError && (
                      <p className="ferr" role="alert">
                        {looseError}
                      </p>
                    )}
                  </div>
                )}
              </div>
            </fieldset>

            <fieldset className="nr-box">
              <legend>{t('notifications.rules.form.channels')}</legend>
              {event && event.variables && event.variables.length > 0 && (
                <p className="nr-vars">
                  {t('notifications.rules.form.variables')}{' '}
                  {event.variables.map((v) => (
                    <button
                      key={v.name}
                      type="button"
                      className="nr-var"
                      title={`${lang === 'en' ? v.labelEn : v.label} — ${v.sample}`}
                      aria-label={t('notifications.rules.form.insert', { variable: `{${v.name}}` })}
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => insert(v.name!)}
                    >{`{${v.name}}`}</button>
                  ))}
                </p>
              )}
              {CHANNELS.map((code) => {
                const c = form.channels.find((x) => x.channel === code)!
                const ok = configured.get(code) ?? false
                const name = t(`notifications.rules.channel.${code}`)
                return (
                  <div key={code} className="nr-channel" data-channel={code}>
                    <div className="nr-chhead">
                      <label className="sw">
                        <input
                          type="checkbox"
                          role="switch"
                          aria-label={t('notifications.rules.form.channelOn', { channel: name })}
                          checked={c.enabled && ok}
                          disabled={!ok}
                          onChange={(e) => setChannel(code, { enabled: e.target.checked })}
                        />
                        <span className="tk" aria-hidden="true" />
                        <b>{name}</b>
                      </label>
                      {!ok && <span className="help">{t('notifications.rules.form.notConfigured')}</span>}
                      {ok && !creating && (
                        <button type="button" className="btn sm" disabled={test.isPending} onClick={() => void sendTest(code)}>
                          {t('notifications.rules.form.test')}
                        </button>
                      )}
                    </div>
                    {ok && (
                      <div className="nr-texts">
                        {NEEDS_SUBJECT.includes(code) && (
                          <div className="r2">
                            {field(c, 'subjectEs', t('notifications.rules.form.subjectEs'), false)}
                            {field(c, 'subjectEn', t('notifications.rules.form.subjectEn'), false)}
                          </div>
                        )}
                        <div className="r2">
                          {field(c, 'bodyEs', t('notifications.rules.form.bodyEs'), true)}
                          {field(c, 'bodyEn', t('notifications.rules.form.bodyEn'), true)}
                        </div>
                      </div>
                    )}
                  </div>
                )
              })}
              {!creating && <p className="help">{t('notifications.rules.form.testNeedsSave')}</p>}
            </fieldset>
            {serverError && (
              <p className="ferr" role="alert">
                {serverError}
              </p>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}
