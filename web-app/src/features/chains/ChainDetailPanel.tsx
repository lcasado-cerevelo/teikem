// Lote 245 — ficha de la cadena: PANELES INDEPENDIENTES apilados (Datos · Clientes · Agregar por nombre · Venta). Lote 247 (Lote C): «Precios especiales» y «Ofertas»
// se agregan como paneles nuevos al final, sin rehacer ninguno de los anteriores.
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, EmptyState, Field, Form, Panel, Spinner, TextArea, TextInput, toast } from '../../kernel/ui'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { SpecialPricesPanel } from '../specialPrices/SpecialPricesPanel'
import { ChainBulkAssign } from './ChainBulkAssign'
import { ChainClientsPanel } from './ChainClientsPanel'
import { ChainOffersPanel } from './ChainOffersPanel'
import { ChainSalesSection } from './ChainSalesSection'
import { CHAIN_NAME_MAX, CHAIN_NOTES_MAX, chainSchema, updateChainRequest, type ChainFormValues } from './chainRules'
import { CHAIN_MANAGE, useChain, useSetChainActive, useUpdateChain, type ChainDetail } from './chainsApi'

export function ChainDetailPanel({ publicId }: { publicId: string }) {
  const t = useT()
  const q = useChain(publicId)
  if (q.isPending) {
    return (
      <Panel>
        <Spinner block />
      </Panel>
    )
  }
  if (q.isError || !q.data) {
    return (
      <Panel>
        <EmptyState icon={<IconLayers />} title={q.isError ? applyProblemDetails(q.error).title : t('chains.selectChain')} />
      </Panel>
    )
  }
  const chain = q.data
  return (
    <div className="chn-stack">
      <ChainDataPanel chain={chain} />
      <ChainClientsPanel chain={chain} />
      {chain.isActive && (
        <Can perm={CHAIN_MANAGE}>
          <ChainBulkAssign chain={chain} />
        </Can>
      )}
      <ChainSalesSection chain={chain} />
      <SpecialPricesPanel ownerKind="chain" ownerPublicId={publicId} ownerActive={chain.isActive !== false} />
      <ChainOffersPanel chainPublicId={publicId} />
    </div>
  )
}

/** Datos de la cadena: editar nombre y notas (con rowVersion; el 409 se muestra tal cual), desactivar y reactivar. */
function ChainDataPanel({ chain }: { chain: ChainDetail }) {
  const t = useT()
  const publicId = chain.publicId ?? ''
  const update = useUpdateChain(publicId)
  const setActive = useSetChainActive(publicId)
  const [confirm, setConfirm] = useState(false)
  const [activeError, setActiveError] = useState<string | null>(null)
  const schema = useMemo(
    () => chainSchema({ nameRequired: t('chains.errors.nameRequired'), nameMax: t('chains.errors.nameMax'), notesMax: t('chains.errors.notesMax') }),
    [t],
  )
  const values: ChainFormValues = { name: chain.name ?? '', notes: chain.notes ?? '' }
  const form = useForm<ChainFormValues>({ resolver: zodResolver(schema), defaultValues: values })
  // la ficha recargada (tras guardar, o tras un 409) vuelve a llenar el formulario
  useEffect(() => form.reset({ name: chain.name ?? '', notes: chain.notes ?? '' }), [chain.rowVersion, chain.name, chain.notes, form])

  const total = (chain.clientCount ?? 0) + (chain.inactiveClientCount ?? 0)
  const toggle = async (active: boolean) => {
    setActiveError(null)
    try {
      await setActive.mutateAsync(active)
      toast.success(t(active ? 'chains.reactivated' : 'chains.deactivated'))
    } catch (e) {
      // el 409 (clientes activos o nombre tomado) se muestra tal cual
      const title = applyProblemDetails(e).title
      setActiveError(title)
      toast.error(title)
    }
  }
  return (
    <Panel
      icon={<IconLayers />}
      title={chain.name}
      subtitle={t('chains.summary', { active: chain.clientCount ?? 0, inactive: chain.inactiveClientCount ?? 0, total })}
      actions={chain.isActive ? <Chip tone="cap">{t('chains.active')}</Chip> : <Chip tone="warn">{t('chains.inactive')}</Chip>}
    >
      <Can
        perm={CHAIN_MANAGE}
        fallback={
          <dl className="chn-facts">
            <div>
              <dt>{t('chains.fields.name')}</dt>
              <dd>{chain.name}</dd>
            </div>
            <div>
              <dt>{t('chains.fields.notes')}</dt>
              <dd>{chain.notes || '—'}</dd>
            </div>
          </dl>
        }
      >
        <Form
          form={form}
          onSubmit={async (v) => {
            await update.mutateAsync(updateChainRequest(v, chain.rowVersion))
            toast.success(t('chains.saved'))
          }}
          onError={(p) => {
            if (p.code === 'conflict') form.setError('name', { type: 'server', message: p.title })
          }}
        >
          <Field name="name" label={t('chains.fields.name')} required help={t('chains.fields.nameHelp')}>
            <TextInput maxLength={CHAIN_NAME_MAX} disabled={!chain.isActive} />
          </Field>
          <Field name="notes" label={t('chains.fields.notes')}>
            <TextArea maxLength={CHAIN_NOTES_MAX} rows={2} disabled={!chain.isActive} />
          </Field>
          <div className="chn-actions">
            {chain.isActive ? (
              <>
                <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
                  {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
                </button>
                <button type="button" className="btn" onClick={() => setConfirm(true)}>
                  {t('chains.deactivate')}
                </button>
              </>
            ) : (
              <button type="button" className="btn flow" onClick={() => void toggle(true)} disabled={setActive.isPending}>
                {t('chains.reactivate')}
              </button>
            )}
          </div>
        </Form>
      </Can>
      {activeError && (
        <p className="ferr" role="alert">
          {activeError}
        </p>
      )}
      <ConfirmDialog
        open={confirm}
        title={t('chains.deactivateTitle')}
        message={t('chains.deactivateMessage', { name: chain.name ?? '' })}
        confirmLabel={t('chains.deactivate')}
        tone="danger"
        onClose={() => setConfirm(false)}
        onConfirm={async () => {
          await setActive.mutateAsync(false)
          toast.success(t('chains.deactivated'))
        }}
      />
    </Panel>
  )
}
