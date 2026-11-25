// Lote 245 — alta de una cadena (sales.chain): nombre (único entre las activas, 409 si ya existe) y notas opcionales.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Field, Form, Modal, TextArea, TextInput, toast } from '../../kernel/ui'
import { CHAIN_NAME_MAX, CHAIN_NOTES_MAX, chainSchema, createChainRequest, type ChainFormValues } from './chainRules'
import { useCreateChain } from './chainsApi'

export function ChainCreateModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (publicId: string) => void }) {
  const t = useT()
  const create = useCreateChain()
  const schema = useMemo(
    () => chainSchema({ nameRequired: t('chains.errors.nameRequired'), nameMax: t('chains.errors.nameMax'), notesMax: t('chains.errors.notesMax') }),
    [t],
  )
  const form = useForm<ChainFormValues>({ resolver: zodResolver(schema), defaultValues: { name: '', notes: '' } })
  const formId = 'chain-create'
  const close = () => {
    form.reset({ name: '', notes: '' })
    onClose()
  }
  return (
    <Modal
      open={open}
      title={t('chains.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const created = await create.mutateAsync(createChainRequest(v))
          toast.success(t('chains.created'))
          close()
          if (created.publicId) onCreated(created.publicId)
        }}
        onError={(p) => {
          // el nombre repetido es un 409 sin campo: también se muestra bajo el nombre
          if (p.code === 'conflict') form.setError('name', { type: 'server', message: p.title })
        }}
      >
        <Field name="name" label={t('chains.fields.name')} required help={t('chains.fields.nameHelp')}>
          <TextInput maxLength={CHAIN_NAME_MAX} />
        </Field>
        <Field name="notes" label={t('chains.fields.notes')}>
          <TextArea maxLength={CHAIN_NOTES_MAX} rows={3} />
        </Field>
      </Form>
    </Modal>
  )
}
