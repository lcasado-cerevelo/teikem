// Panel «Avisos al chofer»: la oficina le escribe un aviso a este chofer. Le llega a la bandeja de su app (y como aviso emergente en el teléfono si Firebase está configurado).
// Permiso trips.dispatch. El servidor valida título (obligatorio, 120) y texto (1000).
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Field, Form, IconTag, Panel, TextArea, TextInput, toast } from '../../kernel/ui'
import { useSendDriverNotice, type DriverDetail } from './api'

export const NOTICE_TITLE_MAX = 120
export const NOTICE_BODY_MAX = 1000

export function DriverNoticePanel({ driver }: { driver: DriverDetail }) {
  const t = useT()
  const send = useSendDriverNotice()
  const schema = z.object({
    title: z.string().trim().min(1, t('drivers.notice.titleRequired')).max(NOTICE_TITLE_MAX, t('drivers.notice.titleMax', { max: NOTICE_TITLE_MAX })),
    body: z.string().trim().max(NOTICE_BODY_MAX, t('drivers.notice.bodyMax', { max: NOTICE_BODY_MAX })),
  })
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { title: '', body: '' } })
  if (driver.isTerminal === true) return null

  return (
    <Can perm="trips.dispatch">
      <Panel icon={<IconTag />} title={t('drivers.notice.title')}>
        <Form
          form={form}
          onSubmit={async (v) => {
            await send.mutateAsync({ driverPublicId: driver.publicId, title: v.title, body: v.body || undefined })
            form.reset({ title: '', body: '' })
            toast.success(t('drivers.notice.sent'))
          }}
        >
          <p className="help">{t('drivers.notice.help')}</p>
          <Field name="title" label={t('drivers.notice.fieldTitle')}>
            <TextInput maxLength={NOTICE_TITLE_MAX} autoComplete="off" />
          </Field>
          <Field name="body" label={t('drivers.notice.fieldBody')}>
            <TextArea maxLength={NOTICE_BODY_MAX} rows={4} />
          </Field>
          <div className="form-acts">
            <button type="submit" className="btn flow" disabled={form.formState.isSubmitting}>
              {form.formState.isSubmitting ? t('common.loading') : t('drivers.notice.send')}
            </button>
          </div>
        </Form>
      </Panel>
    </Can>
  )
}
