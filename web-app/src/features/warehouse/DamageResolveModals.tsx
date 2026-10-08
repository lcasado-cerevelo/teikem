// Resolver un daño que está en cuarentena (2026-10-08, `warehouse.damage`): DESECHARLO (sale del inventario con un ajuste Daño) o RECUPERARLO (vuelve a
// una posición de guardado). POST /api/v1/damage-reports/{id}/discard | /recover.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { ComboSelectInput, Field, Form, Modal, TextArea, toast } from '../../kernel/ui'
import { useResolveDamage, type DamageReportDto } from './api'
import { BinPickerInput } from './pickers'

export interface DamageResolveModalProps {
  /** null = cerrado. */
  damage: DamageReportDto | null
  action: 'discard' | 'recover'
  onClose: () => void
}

interface ResolveValues {
  toBinId: string
  /** Destino final al desecharlo (catálogo DamageFinalDestination): tirado, devuelto al proveedor, donado, vendido como saldo… */
  finalDestination: string
  notes: string
}

export function DamageResolveModal({ damage, action, onClose }: DamageResolveModalProps) {
  if (!damage) return null
  return <ResolveBody key={`${damage.id}-${action}`} damage={damage} action={action} onClose={onClose} />
}

function ResolveBody({ damage, action, onClose }: { damage: DamageReportDto; action: 'discard' | 'recover'; onClose: () => void }) {
  const t = useT()
  const resolve = useResolveDamage()
  const recover = action === 'recover'
  const destinations = useLookups('DamageFinalDestination')
  const destinationOptions = useMemo(() => (destinations.data ?? []).map((c) => ({ value: c.code, label: c.label })), [destinations.data])
  const schema = useMemo(
    () =>
      z.object({
        toBinId: z.string().refine((v) => !recover || v !== '', t('warehouse.damage.errors.toBinRequired')),
        finalDestination: z.string().refine((v) => recover || v !== '', t('warehouse.damage.errors.finalDestinationRequired')),
        notes: z.string().max(300, t('warehouse.damage.errors.notesMax')),
      }),
    [t, recover],
  )
  const form = useForm<ResolveValues>({ resolver: zodResolver(schema) as never, defaultValues: { toBinId: '', finalDestination: '', notes: '' } })
  const formId = 'damage-resolve'
  const busy = form.formState.isSubmitting
  const quantity = String(damage.quantity ?? '')

  return (
    <Modal
      open
      title={t(recover ? 'warehouse.damage.recoverTitle' : 'warehouse.damage.discardTitle', { code: damage.code ?? '' })}
      onClose={onClose}
      dismissible={!busy}
      size="sm"
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className={`btn ${recover ? 'flow' : 'danger'}`} disabled={busy}>
            {busy ? t('common.loading') : t(recover ? 'warehouse.damage.recover' : 'warehouse.damage.discard')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          await resolve.mutateAsync({
            action,
            id: damage.id ?? 0,
            body: { toBinId: recover ? Number(v.toBinId) : null, finalDestination: recover ? null : v.finalDestination, notes: v.notes.trim() || null },
          })
          toast.success(t(recover ? 'warehouse.damage.recovered' : 'warehouse.damage.discarded', { code: damage.code ?? '' }))
          onClose()
        }}
      >
        <p>
          {t(recover ? 'warehouse.damage.recoverBody' : 'warehouse.damage.discardBody', {
            qty: quantity,
            sku: damage.sku ?? '',
            bin: damage.quarantineBinCode ?? '',
          })}
        </p>
        {recover && (
          <Field name="toBinId" label={t('warehouse.damage.fields.toBin')} required help={t('warehouse.damage.toBinHelp')}>
            <BinPickerInput warehousePublicId={damage.warehousePublicId} excludeZoneTypeCodes={['QUARANTINE', 'STAGING', 'CROSSDOCK', 'RENTAL']} />
          </Field>
        )}
        {!recover && (
          <Field name="finalDestination" label={t('warehouse.damage.fields.finalDestination')} required help={t('warehouse.damage.finalDestinationHelp')}>
            <ComboSelectInput options={destinationOptions} loading={destinations.isLoading} placeholder={t('warehouse.damage.finalDestinationPlaceholder')} />
          </Field>
        )}
        <Field name="notes" label={t('warehouse.damage.fields.resolutionNotes')}>
          <TextArea rows={2} maxLength={300} />
        </Field>
      </Form>
    </Modal>
  )
}
