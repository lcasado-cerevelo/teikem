// Consignatario nuevo EN LÍNEA (Lote 213, dictado de Luis): al escribir un nombre que no existe en el combo del consignatario de una orden y
// elegir «+ Nuevo consignatario», este panel (sin modal) pide solo lo mínimo —Nombre, Dirección, Pueblo y, si hay permiso, Teléfono— y se
// maneja SIN ratón: Enter en cualquier campo graba (si falta algo lleva el foco al primer campo faltante, con el mensaje del esquema
// `consigneeSchema`), Escape cancela, Tab recorre. Al grabar crea el consignatario (mismo `useCreateConsignee` y `buildCreateRequest` del modal,
// tipo DELIVERY, país PR por omisión, dueño = el cliente de la orden) y su teléfono aparte (`useConsigneePhoneSync`); `onSaved` deja el nuevo
// elegido y mueve el foco. No es un `<form>`: en la captura detallada va DENTRO del formulario de la orden y los formularios no se anidan.
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { applyProblemDetails, normalizeField } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { toast } from '../../kernel/ui'
import { PostalLocalityPicker } from '../warehouse/PostalLocalityPicker'
import { localityCity } from '../warehouse/warehouseFilters'
import { useCreateConsignee } from './api'
import { consigneeMessagesOf } from './consigneeMessages'
import { buildCreateRequest, consigneeSchema, DEFAULT_COUNTRY, emptyConsigneeValues, LINE_MAX, NAME_MAX } from './consigneeRules'
import { useConsigneePhoneSync } from './useConsigneePhoneSync'

type FieldKey = 'name' | 'line1' | 'city' | 'phone'
/** Orden en que se recorren los campos (para llevar el foco al primer faltante). */
const ORDER: FieldKey[] = ['name', 'line1', 'city', 'phone']

interface Locality {
  city: string
  postalCode: string
  state: string
  country: string
}

const NO_LOCALITY: Locality = { city: '', postalCode: '', state: '', country: DEFAULT_COUNTRY }

/** Campo del formulario al que pertenece un error (el pueblo reúne ciudad, ZIP, estado y país). */
function fieldOf(path: string): FieldKey | null {
  const key = path === 'postalCode' || path === 'state' || path === 'country' ? 'city' : path
  return (ORDER as string[]).includes(key) ? (key as FieldKey) : null
}

export interface ConsigneeInlineFormProps {
  /** Cliente de la orden (publicId): el dueño del consignatario nuevo. */
  clientPublicId: string
  /** Lo escrito en el combo: el nombre con el que arranca. */
  initialName: string
  /** Prefijo de los id de los campos (`<prefijo>-name`, `-line1`, `-city`, `-phone`). */
  idPrefix: string
  /** Se grabó: publicId del consignatario nuevo (la pantalla lo elige, cierra el panel y mueve el foco). */
  onSaved: (publicId: string) => void
  /** Escape o «Cancelar». */
  onCancel: () => void
  /** Tab desde el último campo: la pantalla pasa el foco al siguiente campo de su fila (si no, sigue el orden natural). */
  onTabOut?: () => void
}

export function ConsigneeInlineForm({ clientPublicId, initialName, idPrefix, onSaved, onCancel, onTabOut }: ConsigneeInlineFormProps) {
  const t = useT()
  const f = useFormat()
  const create = useCreateConsignee()
  const { canPhone, sync: syncPhone } = useConsigneePhoneSync()
  const [name, setName] = useState(initialName)
  const [line1, setLine1] = useState('')
  const [locality, setLocality] = useState<Locality>(NO_LOCALITY)
  const [phone, setPhone] = useState('')
  const [errors, setErrors] = useState<Partial<Record<FieldKey, string>>>({})
  const [alert, setAlert] = useState<{ title: string; messages: string[] } | null>(null)
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false)
  const lastField: FieldKey = canPhone ? 'phone' : 'city'

  const schema = useMemo(() => consigneeSchema(consigneeMessagesOf(t), f.isValidPhoneFor), [t, f])
  const idOf = (k: FieldKey) => `${idPrefix}-${k}`
  const focusField = (k: FieldKey) => document.getElementById(idOf(k))?.focus()

  // al abrirse, el foco cae en el primer campo vacío (normalmente Dirección: el nombre ya viene escrito)
  useEffect(() => {
    focusField(initialName.trim() ? 'line1' : 'name')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function submit() {
    if (savingRef.current) return
    setAlert(null)
    const values = {
      ...emptyConsigneeValues(clientPublicId),
      name,
      line1,
      city: locality.city,
      state: locality.state,
      postalCode: locality.postalCode,
      country: locality.country || DEFAULT_COUNTRY,
      phone: canPhone ? phone : '',
    }
    const parsed = schema.safeParse(values)
    if (!parsed.success) {
      const next: Partial<Record<FieldKey, string>> = {}
      for (const issue of parsed.error.issues) {
        const key = fieldOf(String(issue.path[0]))
        if (key && !next[key]) next[key] = issue.message
      }
      setErrors(next)
      const first = ORDER.find((k) => next[k])
      if (first) focusField(first)
      return
    }
    setErrors({})
    savingRef.current = true
    setSaving(true)
    try {
      const saved = await create.mutateAsync(buildCreateRequest(parsed.data))
      const phoneProblem = canPhone && phone.trim() ? await syncPhone(saved.id, { phone, countryCallingCode: '' }, undefined) : null
      if (phoneProblem) toast.error(t('consignees.phoneNotSaved', { reason: phoneProblem }))
      else toast.success(t('consignees.created', { name: saved.name ?? '' }))
      savingRef.current = false
      setSaving(false)
      if (saved.publicId) onSaved(saved.publicId)
    } catch (err) {
      // el servidor rechazó: el error sale aquí mismo y lo escrito se conserva
      savingRef.current = false
      setSaving(false)
      const p = applyProblemDetails(err)
      const next: Partial<Record<FieldKey, string>> = {}
      const rest: string[] = []
      for (const [field, messages] of Object.entries(p.errors)) {
        const key = fieldOf(normalizeField(field))
        if (key && !next[key]) next[key] = messages.join(' ')
        else rest.push(...messages)
      }
      setErrors(next)
      setAlert({ title: p.title, messages: rest })
      const first = ORDER.find((k) => next[k])
      if (first) focusField(first)
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'Enter' && !e.defaultPrevented) {
      // un botón conserva su Enter; en cualquier otro campo Enter graba (y no deja que el formulario de la orden se envíe)
      if ((e.target as HTMLElement).tagName === 'BUTTON') return
      e.preventDefault()
      e.stopPropagation()
      void submit()
    } else if (e.key === 'Escape' && !e.defaultPrevented) {
      e.preventDefault()
      e.stopPropagation()
      onCancel()
    } else if (e.key === 'Tab' && !e.shiftKey && onTabOut && (e.target as HTMLElement).id === idOf(lastField)) {
      e.preventDefault()
      onTabOut()
    }
  }

  const err = (k: FieldKey) =>
    errors[k] ? (
      <p className="ferr" id={`${idOf(k)}-err`} role="alert">
        {errors[k]}
      </p>
    ) : null
  const aria = (k: FieldKey) => ({ 'aria-invalid': errors[k] ? true : undefined, 'aria-describedby': errors[k] ? `${idOf(k)}-err` : undefined })
  const req = (
    <span className="req" aria-hidden="true">
      *
    </span>
  )

  return (
    <div className="cs-inline" role="group" aria-label={t('consignees.inline.title')} aria-busy={saving || undefined} onKeyDown={onKeyDown} data-testid="consignee-inline">
      <h4>{t('consignees.inline.title')}</h4>
      {alert && (
        <div className="form-alert" role="alert">
          {alert.title}
          {alert.messages.length > 0 && (
            <ul>
              {alert.messages.map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      <div className="r2">
        <div className="f">
          <label htmlFor={idOf('name')}>
            {t('consignees.name')}
            {req}
          </label>
          <input id={idOf('name')} value={name} maxLength={NAME_MAX} autoComplete="off" aria-required="true" onChange={(e) => setName(e.target.value)} {...aria('name')} />
          {err('name')}
        </div>
        <div className="f">
          <label htmlFor={idOf('line1')}>
            {t('consignees.address')}
            {req}
          </label>
          <input id={idOf('line1')} value={line1} maxLength={LINE_MAX} autoComplete="off" aria-required="true" onChange={(e) => setLine1(e.target.value)} {...aria('line1')} />
          {err('line1')}
        </div>
      </div>
      <div className="r2">
        <div className="f">
          <label htmlFor={idOf('city')}>
            {t('consignees.town')}
            {req}
          </label>
          <PostalLocalityPicker
            id={idOf('city')}
            zipClass="DELIVERY"
            city={locality.city}
            postalCode={locality.postalCode}
            required
            onChange={(l) => {
              setLocality(l ? { city: localityCity(l), postalCode: l.postalCode ?? '', state: l.state ?? '', country: l.countryCode || DEFAULT_COUNTRY } : NO_LOCALITY)
              setErrors((e) => ({ ...e, city: undefined }))
              // elegido el pueblo, el cursor pasa al teléfono (si lo hay): así no se cae en la ✕ del selector y Enter ahí graba
              if (l && canPhone) focusField('phone')
            }}
            {...aria('city')}
          />
          {err('city')}
        </div>
        {canPhone && (
          <div className="f">
            <label htmlFor={idOf('phone')}>{t('consignees.phone')}</label>
            <input
              id={idOf('phone')}
              type="tel"
              inputMode="numeric"
              autoComplete="off"
              maxLength={40}
              value={phone}
              placeholder={f.phonePlaceholder()}
              onChange={(e) => setPhone(f.phoneInput(e.target.value))}
              {...aria('phone')}
            />
            {err('phone')}
          </div>
        )}
      </div>
      <div className="cs-inline-foot">
        <span className="help">{t('consignees.inline.keys')}</span>
        <span className="cs-inline-btns">
          <button type="button" className="btn sm" onClick={onCancel} disabled={saving}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn sm flow" onClick={() => void submit()} disabled={saving}>
            {saving ? t('common.loading') : t('ui.form.save')}
          </button>
        </span>
      </div>
    </div>
  )
}
