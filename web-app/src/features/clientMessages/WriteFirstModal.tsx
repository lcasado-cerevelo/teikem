// «Escribirle a una persona» (ficha del cliente): elige a una persona del cliente y escribe el primer mensaje; abre (o reutiliza) su
// conversación y lleva a /portal/messages con ella abierta. Hay una conversación POR PERSONA del cliente.
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal, Spinner } from '../../kernel/ui'
import { useClientPeople, useMessageMutations } from './api'
import { charsLeft, MESSAGE_MAX, validateMessage } from './rules'

export function WriteFirstModal({ clientPublicId, open, onClose }: { clientPublicId: string; open: boolean; onClose: () => void }) {
  const t = useT()
  const navigate = useNavigate()
  const { data: people = [], isPending } = useClientPeople(clientPublicId, open)
  const { start } = useMessageMutations()
  const [personId, setPersonId] = useState('')
  const [body, setBody] = useState('')
  const [errors, setErrors] = useState<{ person?: string; body?: string; server?: string }>({})

  const submit = async () => {
    const next: typeof errors = {}
    if (!personId) next.person = t('clientMessages.errors.personRequired')
    const problem = validateMessage(body)
    if (problem) next.body = t(problem === 'empty' ? 'clientMessages.errors.empty' : 'clientMessages.errors.tooLong')
    setErrors(next)
    if (next.person || next.body) return
    try {
      const conv = await start.mutateAsync({ clientPublicId, personId: Number(personId), body: body.trim() })
      onClose()
      navigate(`/portal/messages?conversation=${conv.publicId ?? ''}`)
    } catch (e) {
      const p = applyProblemDetails(e)
      setErrors({ body: p.errors.body?.[0], person: p.errors.personId?.[0], server: p.title })
    }
  }

  const left = charsLeft(body)
  return (
    <Modal
      open={open}
      title={t('clientMessages.writeFirst.title')}
      size="sm"
      onClose={onClose}
      dismissible={!start.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={start.isPending}>
            {t('common.cancel')}
          </button>
          <button type="submit" form="cm-first" className="btn flow" disabled={start.isPending}>
            {start.isPending ? t('common.loading') : t('clientMessages.send')}
          </button>
        </>
      }
    >
      <form
        id="cm-first"
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
      >
        <p className="help">{t('clientMessages.writeFirst.help')}</p>
        {isPending ? (
          <Spinner block />
        ) : people.length === 0 ? (
          <p className="note">{t('clientMessages.writeFirst.noPeople')}</p>
        ) : (
          <>
            <div className="f">
              <label htmlFor="cm-first-person">{t('clientMessages.writeFirst.person')}</label>
              <select id="cm-first-person" value={personId} onChange={(e) => setPersonId(e.target.value)} aria-invalid={errors.person ? true : undefined}>
                <option value="">{t('clientMessages.writeFirst.choosePerson')}</option>
                {people.map((p) => (
                  <option key={p.personId} value={String(p.personId)}>
                    {[p.fullName, p.email].filter(Boolean).join(' · ')}
                  </option>
                ))}
              </select>
              {errors.person && <p className="ferr">{errors.person}</p>}
            </div>
            <div className="f">
              <label htmlFor="cm-first-body">{t('clientMessages.writeFirst.message')}</label>
              <textarea
                id="cm-first-body"
                className="cm-first-body"
                rows={4}
                value={body}
                onChange={(e) => setBody(e.target.value)}
                aria-invalid={errors.body ? true : undefined}
              />
              <span className={left < 0 ? 'meta cm-left over' : 'meta cm-left'}>{t('clientMessages.charsLeft', { n: left, max: MESSAGE_MAX })}</span>
              {errors.body && <p className="ferr">{errors.body}</p>}
            </div>
            {errors.server && errors.server !== errors.body && errors.server !== errors.person && <p className="ferr" role="alert">{errors.server}</p>}
          </>
        )}
      </form>
    </Modal>
  )
}
