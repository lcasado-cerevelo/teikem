// Piezas compartidas por las pestañas del contrato (F-A2): opciones de catálogo, errores del servidor renombrados y
// el modal de formulario con el pie Cancelar / Guardar.
import type { ReactNode } from 'react'
import type { UseFormReturn, FieldValues } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { Form, Modal } from '../../kernel/ui'

/** Modal con un formulario: el botón «Guardar» del pie envía el `<form>` por su id; el título y los errores del servidor salen arriba. */
export function FormModal<TIn extends FieldValues, TOut extends FieldValues = TIn>({
  id,
  title,
  open,
  onClose,
  form,
  onSubmit,
  size = 'sm',
  saveLabel,
  children,
}: {
  id: string
  title: string
  open: boolean
  onClose: () => void
  form: UseFormReturn<TIn, unknown, TOut>
  onSubmit: (values: TOut) => Promise<unknown>
  size?: 'sm' | 'md'
  saveLabel?: string
  children: ReactNode
}) {
  const t = useT()
  return (
    <Modal
      open={open}
      title={title}
      size={size}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={id} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : (saveLabel ?? t('ui.form.save'))}
          </button>
        </>
      }
    >
      <Form id={id} form={form} onSubmit={onSubmit}>
        {children}
      </Form>
    </Modal>
  )
}
