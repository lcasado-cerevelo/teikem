// Alta y cambio de una IP preferida (2026-10-12; docs/ip-preferida-mfa-diseno.md §5). La pantalla ayuda a escribir bien la IP o el rango sin saber qué es CIDR:
// «Una sola IP (recomendado)» o «Un rango de IPs», vista previa en vivo («Se guardará como 192.168.10.0/24 — 256 direcciones»), atajos de tamaño, corrección
// automática de bits de host y avisos (rango ancho, rango privado, no incluye su IP actual). El servidor valida y normaliza al guardar (manda él).
import { useMemo, useState } from 'react'
import { utcFromZonedInput, zonedInputFromUtc } from '../../../kernel/api/tenantZone'
import { applyProblemDetails } from '../../../kernel/api/client'
import { useLang, useT } from '../../../kernel/i18n/useT'
import { Modal } from '../../../kernel/ui'
import {
  useCreateTrustedNetwork,
  useUpdateTrustedNetwork,
  type TrustedNetworkDto,
  type TrustedNetworkRequest,
} from '../trustedNetworksApi'
import { cidrContains, formatCount, isPrivateRange, parseCidr, parseIp, PREFIX_SHORTCUTS, withPrefix, MIN_PREFIX } from './trustedNetworkCidr'

type Kind = 'single' | 'range'

export interface TrustedNetworkModalProps {
  open: boolean
  /** Entrada que se cambia; null = alta. */
  entry: TrustedNetworkDto | null
  /** Texto con el que se abre el alta (p. ej. «Agregar esta IP» pasa la IP actual). */
  initialCidr?: string
  /** IP desde la que se consulta (para avisar si el rango no la incluye). */
  currentIp: string | null | undefined
  onClose: () => void
}

/** Estado inicial del formulario según la entrada (o vacío). */
function initialState(entry: TrustedNetworkDto | null, initialCidr?: string) {
  const text = entry?.cidr ?? initialCidr ?? ''
  const parsed = parseCidr(text)
  const range = parsed.cidr ? !parsed.cidr.single : text.includes('/')
  const slash = text.indexOf('/')
  const address = slash >= 0 ? text.slice(0, slash) : text
  const prefix = parsed.cidr && !parsed.cidr.single ? parsed.cidr.prefix : 24
  return {
    name: entry?.name ?? '',
    kind: (range ? 'range' : 'single') as Kind,
    address,
    prefix,
    expires: entry?.expiresAtUtc ? zonedInputFromUtc(entry.expiresAtUtc).slice(0, 10) : '',
    note: entry?.note ?? '',
  }
}

export function TrustedNetworkModal(props: TrustedNetworkModalProps) {
  // se vuelve a montar al abrir (estado limpio por entrada)
  if (!props.open) return null
  return <ModalBody key={props.entry?.publicId ?? `new:${props.initialCidr ?? ''}`} {...props} />
}

function ModalBody({ entry, initialCidr, currentIp, onClose }: TrustedNetworkModalProps) {
  const t = useT()
  const lang = useLang()
  const create = useCreateTrustedNetwork()
  const update = useUpdateTrustedNetwork()
  const init = useMemo(() => initialState(entry, initialCidr), [entry, initialCidr])
  const [name, setName] = useState(init.name)
  const [kind, setKind] = useState<Kind>(init.kind)
  const [address, setAddress] = useState(init.address)
  const [prefix, setPrefix] = useState(init.prefix)
  const [expires, setExpires] = useState(init.expires)
  const [note, setNote] = useState(init.note)
  const [confirmed, setConfirmed] = useState(false)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const busy = create.isPending || update.isPending

  // `address` es lo escrito, tal cual (puede traer «/NN»). Texto que se interpreta: lo escrito si trae «/NN» o es «Una sola IP»; en «Rango» sin «/NN», la IP con
  // el tamaño escogido. El tamaño que muestra la lista sale de lo escrito si trae «/NN».
  const typedSlash = address.includes('/')
  const typedPrefix = typedSlash ? Number(address.split('/')[1]) : NaN
  const shownPrefix = Number.isInteger(typedPrefix) ? typedPrefix : prefix
  const text = kind === 'range' && !typedSlash ? withPrefix(address, prefix) : address.trim()
  const parsed = useMemo(() => (text ? parseCidr(text) : null), [text])
  const cidr = parsed?.cidr
  const current = useMemo(() => (currentIp ? parseIp(currentIp) : null), [currentIp])
  // cambiar la IP o el rango exige confirmar de nuevo; sin cambio (solo nombre, nota…) no
  const needsConfirm = entry === null || (cidr !== undefined && cidr.text !== entry.cidr)

  const warnings: string[] = []
  if (cidr) {
    if (cidr.adjusted) warnings.push(t('system.audit.trusted.modal.adjusted', { text: cidr.text }))
    if (isPrivateRange(cidr)) warnings.push(t('system.audit.trusted.modal.private'))
    if (!cidr.single && cidr.family === 4 && cidr.prefix <= 20) warnings.push(t('system.audit.trusted.modal.wide', { n: formatCount(cidr.count, lang) }))
    if (current && !cidrContains(cidr, current)) warnings.push(t('system.audit.trusted.modal.notIncludesCurrent', { ip: currentIp ?? '' }))
  }

  const onAddress = (value: string) => {
    setAddress(value)
    // escribir o pegar «192.168.10.0/24» es un rango: pasa a «Un rango de IPs» sin tocar lo escrito
    if (value.includes('/') && kind === 'single') setKind('range')
  }
  // el tamaño escogido (lista o atajo) reescribe el «/NN» si ya estaba escrito
  const onSize = (p: number) => {
    setPrefix(p)
    if (typedSlash) setAddress(withPrefix(address, p))
  }

  async function submit() {
    const next: Record<string, string> = {}
    if (!name.trim()) next.name = t('system.audit.trusted.errors.nameRequired')
    if (!cidr) next.cidr = parsed?.error ? t(`system.audit.trusted.errors.${parsed.error}`) : t('system.audit.trusted.errors.required')
    if (needsConfirm && !confirmed) next.confirm = t('system.audit.trusted.errors.confirm')
    setErrors(next)
    setFormError(null)
    if (Object.keys(next).length > 0 || !cidr) return

    const expiresUtc = expires ? utcFromZonedInput(`${expires}T23:59`) : null
    try {
      if (entry === null) {
        const body: TrustedNetworkRequest = {
          name: name.trim(), cidr: cidr.text, expiresAtUtc: expiresUtc, note: note.trim() || null, confirmInternalOnly: true,
        }
        await create.mutateAsync(body)
      } else {
        const body: TrustedNetworkRequest = {
          name: name.trim(),
          cidr: needsConfirm ? cidr.text : null,
          confirmInternalOnly: needsConfirm ? true : null,
          note: note.trim(),
          // el vencimiento solo se manda si cambió (una entrada ya vencida se puede renombrar sin tocarlo)
          ...(expires === init.expires ? {} : expiresUtc ? { expiresAtUtc: expiresUtc } : { clearExpiry: true }),
        }
        await update.mutateAsync({ publicId: entry.publicId ?? '', body })
      }
      onClose()
    } catch (err) {
      const problem = applyProblemDetails(err)
      const server: Record<string, string> = {}
      for (const [field, messages] of Object.entries(problem.errors)) {
        const key = field === 'confirmInternalOnly' ? 'confirm' : field === 'expiresAtUtc' ? 'expires' : field
        server[key] = messages.join(' ')
      }
      setErrors(server)
      if (Object.keys(server).length === 0) setFormError(problem.title)
    }
  }

  const prefixOptions = useMemo(
    () =>
      Array.from({ length: 32 - MIN_PREFIX[4] + 1 }, (_, i) => 32 - i).map((p) => ({ prefix: p, count: 2 ** (32 - p) })),
    [],
  )

  return (
    <Modal
      open
      title={entry ? t('system.audit.trusted.modal.editTitle') : t('system.audit.trusted.modal.addTitle')}
      onClose={onClose}
      size="md"
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={busy}>
            {busy ? t('system.audit.policy.saving') : t('system.audit.trusted.modal.save')}
          </button>
        </>
      }
    >
      <div className="trn-form">
        {formError && (
          <div className="alert" role="alert">
            {formError}
          </div>
        )}
        <div className="f">
          <label htmlFor="trn-name">{t('system.audit.trusted.modal.name')}</label>
          <input id="trn-name" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} aria-invalid={Boolean(errors.name)} placeholder={t('system.audit.trusted.modal.namePlaceholder')} />
          {errors.name && <p className="ferr" role="alert">{errors.name}</p>}
        </div>

        <fieldset className="trn-kind" aria-label={t('system.audit.trusted.modal.kindLabel')}>
          <div className="seg" role="radiogroup">
            <button type="button" role="radio" aria-checked={kind === 'single'} className={kind === 'single' ? 'on' : ''} onClick={() => setKind('single')}>
              {t('system.audit.trusted.modal.kindSingle')}
            </button>
            <button type="button" role="radio" aria-checked={kind === 'range'} className={kind === 'range' ? 'on' : ''} onClick={() => setKind('range')}>
              {t('system.audit.trusted.modal.kindRange')}
            </button>
          </div>
        </fieldset>

        <div className="trn-addr">
          <div className="f">
            <label htmlFor="trn-ip">{kind === 'single' ? t('system.audit.trusted.modal.ip') : t('system.audit.trusted.modal.network')}</label>
            <input id="trn-ip" className="mono" value={address} onChange={(e) => onAddress(e.target.value)} aria-invalid={Boolean(errors.cidr)} placeholder={kind === 'single' ? '203.0.113.10' : '192.168.10.0'} inputMode="text" autoComplete="off" spellCheck={false} />
          </div>
          {kind === 'range' && (
            <div className="f">
              <label htmlFor="trn-prefix">{t('system.audit.trusted.modal.size')}</label>
              <select id="trn-prefix" value={shownPrefix} onChange={(e) => onSize(Number(e.target.value))}>
                {prefixOptions.map((o) => (
                  <option key={o.prefix} value={o.prefix}>
                    {`/${o.prefix} — ${formatCount(BigInt(o.count), lang)}`}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>
        {kind === 'range' && (
          <>
            <p className="set-d">{t('system.audit.trusted.modal.rangeHelp')}</p>
            <div className="trn-shortcuts" role="group" aria-label={t('system.audit.trusted.modal.shortcuts')}>
              {PREFIX_SHORTCUTS.filter((s) => s.prefix !== 32).map((s) => (
                <button key={s.prefix} type="button" className={`btn sm${shownPrefix === s.prefix ? ' flow' : ''}`} onClick={() => onSize(s.prefix)} title={t('system.audit.trusted.modal.shortcutTitle', { n: formatCount(BigInt(s.count), lang) })}>
                  {`/${s.prefix}`}
                </button>
              ))}
            </div>
          </>
        )}

        {parsed?.error && <p className="ferr" role="alert">{t(`system.audit.trusted.errors.${parsed.error}`)}</p>}
        {errors.cidr && !parsed?.error && <p className="ferr" role="alert">{errors.cidr}</p>}
        {cidr && (
          <p className="trn-preview" role="status">
            {cidr.single
              ? t('system.audit.trusted.modal.previewSingle', { text: cidr.text })
              : t('system.audit.trusted.modal.previewRange', { text: cidr.text, n: formatCount(cidr.count, lang), first: cidr.first, last: cidr.last })}
          </p>
        )}
        {warnings.map((w) => (
          <p key={w} className="trn-warn" role="note">
            {w}
          </p>
        ))}

        <div className="r2">
          <div className="f">
            <label htmlFor="trn-exp">{t('system.audit.trusted.modal.expires')}</label>
            <input id="trn-exp" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} />
            <p className="set-d">{t('system.audit.trusted.modal.expiresHelp')}</p>
            {errors.expires && <p className="ferr" role="alert">{errors.expires}</p>}
          </div>
          <div className="f">
            <label htmlFor="trn-note">{t('system.audit.trusted.modal.note')}</label>
            <input id="trn-note" value={note} maxLength={300} onChange={(e) => setNote(e.target.value)} placeholder={t('system.audit.trusted.modal.notePlaceholder')} />
            {errors.note && <p className="ferr" role="alert">{errors.note}</p>}
          </div>
        </div>

        {needsConfirm && (
          <div className="f trn-confirm">
            <label className="trn-check">
              <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} aria-invalid={Boolean(errors.confirm)} />
              <span>{t('system.audit.trusted.modal.confirm')}</span>
            </label>
            {errors.confirm && <p className="ferr" role="alert">{errors.confirm}</p>}
          </div>
        )}
      </div>
    </Modal>
  )
}
