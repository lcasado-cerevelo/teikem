// «Lista de valores» de un campo de selección (maqueta `camposScreen`): escoger una lista del catálogo o «Nueva lista…» (nombre y
// opciones separadas por coma). Los campos de lista no guardan opciones propias: apuntan a una lista del catálogo, y renombrar una
// opción actualiza todos los registros que ya la usan. «Nueva lista…» exige admin.catalogs; sin él no se pinta y se explica qué pedir.
import { useMemo, useState, type KeyboardEvent } from 'react'
import { useFormContext } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { buildListRequest, parseListOptions, type DefinitionFormValues } from '../../kernel/custom-fields'
import { useT } from '../../kernel/i18n'
import { ComboSelectInput, Field, toast } from '../../kernel/ui'
import { useCatalogDomains, useCreateList } from '../system/api'
import { listChoices } from './listChoices'

export function CustomListPicker({ ownOptionsText }: { ownOptionsText: { n: number; items: string } | null }) {
  const t = useT()
  const form = useFormContext<DefinitionFormValues>()
  const canCreate = useCan('admin.catalogs')
  const refEntity = form.watch('refEntity')
  const { data: domains } = useCatalogDomains()
  const { data: values = [] } = useLookups(refEntity || null)
  const create = useCreateList()
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [optionsText, setOptionsText] = useState('')
  const [error, setError] = useState<string | null>(null)

  const choices = useMemo(
    () => listChoices(domains, refEntity, { own: t('customFieldsAdmin.list.own'), system: t('customFieldsAdmin.list.system') }),
    [domains, refEntity, t],
  )

  const close = () => {
    setCreating(false)
    setName('')
    setOptionsText('')
    setError(null)
  }

  const submit = async () => {
    const parsed = parseListOptions(optionsText)
    if (name.trim() === '' || parsed.options.length === 0) return setError(t('customFieldsAdmin.list.need'))
    if (parsed.invalid.length > 0) return setError(t('customFieldsAdmin.list.invalid', { items: parsed.invalid.join(', ') }))
    setError(null)
    try {
      const domain = await create.mutateAsync(buildListRequest(name, parsed.options))
      if (domain.domainKey) form.setValue('refEntity', domain.domainKey, { shouldDirty: true, shouldValidate: true })
      toast.success(t('customFieldsAdmin.list.created', { name: name.trim() }))
      close()
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }

  // Enter dentro de «Nueva lista…» crea la lista; no envía el formulario del campo.
  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key !== 'Enter') return
    e.preventDefault()
    void submit()
  }

  const preview = values.map((v) => v.label)
  return (
    <>
      {creating ? (
        <div className="f" role="group" aria-label={t('customFieldsAdmin.list.newListTitle')}>
          <label>{t('customFieldsAdmin.list.newListTitle')}</label>
          <div className="r2" style={{ marginBottom: 8 }}>
            <input
              aria-label={t('customFieldsAdmin.list.name')}
              placeholder={t('customFieldsAdmin.list.name')}
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={onKeyDown}
            />
            <input
              aria-label={t('customFieldsAdmin.list.options')}
              placeholder={t('customFieldsAdmin.list.optionsPlaceholder')}
              value={optionsText}
              onChange={(e) => setOptionsText(e.target.value)}
              onKeyDown={onKeyDown}
            />
          </div>
          {error && (
            <p className="ferr" role="alert">
              {error}
            </p>
          )}
          <div className="cf-inline-actions">
            <button type="button" className="btn sm flow" onClick={() => void submit()} disabled={create.isPending}>
              {create.isPending ? t('customFieldsAdmin.list.creating') : t('customFieldsAdmin.list.create')}
            </button>
            <button type="button" className="btn sm" onClick={close} disabled={create.isPending}>
              {t('common.cancel')}
            </button>
          </div>
        </div>
      ) : (
        <Field name="refEntity" label={t('customFieldsAdmin.list.label')} required help={t('customFieldsAdmin.list.hint')}>
          <ComboSelectInput options={choices} placeholder={t('customFieldsAdmin.list.pick')} clearable={ownOptionsText !== null} />
        </Field>
      )}
      {!creating && (
        <div className="cf-list-extra">
          {canCreate ? (
            <button type="button" className="btn sm" onClick={() => setCreating(true)}>
              + {t('customFieldsAdmin.list.newList')}
            </button>
          ) : (
            <p className="help">{t('customFieldsAdmin.list.noPermission')}</p>
          )}
          {refEntity !== '' && (
            <span className="cf-list-values">{preview.length > 0 ? t('customFieldsAdmin.list.optionsOf', { items: preview.join(' · ') }) : t('customFieldsAdmin.list.empty')}</span>
          )}
        </div>
      )}
      {ownOptionsText && refEntity === '' && !creating && (
        <p className="note" role="note">
          {t('customFieldsAdmin.list.ownOptionsNote', ownOptionsText)}
        </p>
      )}
    </>
  )
}
