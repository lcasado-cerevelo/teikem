// Pestaña «Servicios especiales»: tarifa por tipo de servicio especial del CLIENTE (se aplica a su contrato vigente). Alta con un
// tipo existente o «+ Nuevo tipo de servicio especial…» (el tipo queda para toda la compañía); «Editar» = versión nueva de la
// tarifa; «Quitar» = cerrar. Sin baja ni reactivación de tipos. Lectura contracts.read; escritura contracts.update, con el
// componente «Servicios especiales» encendido en el contrato vigente y `canEdit`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, type DataColumn, DateInput, EmptyState, Field, IconEdit, IconTrash, NumberInput, Select, Spinner, TextInput, toast, useElementWidth } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import { useContract, useSpecialMutations, useSpecialServices, useSpecialServiceTypes } from './contractApi'
import { buildSpecialCreate, defaultEffectiveDate, NEW_SPECIAL_TYPE, serverToday, SPECIAL_NAME_MAX, specialSchema, type SpecialService } from './contractRules'
import { FormModal } from './contractUi'
import type { ClientDetail } from './clientRules'

function SpecialModal({ clientPublicId, row, currency, open, onClose }: { clientPublicId: string; row: SpecialService | null; currency?: string | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const mutations = useSpecialMutations(clientPublicId)
  const { data: types = [] } = useSpecialServiceTypes()
  const editing = row !== null
  const schema = useMemo(
    () =>
      specialSchema(
        {
          typeRequired: t('clients.special.errors.typeRequired'),
          nameRequired: t('clients.special.errors.nameRequired'),
          nameMax: t('clients.special.errors.nameMax', { max: SPECIAL_NAME_MAX }),
          rateRequired: t('clients.rates.errors.rateRequired'),
          rateMin: t('clients.special.errors.rateMin'),
          dateRequired: t('clients.rates.errors.dateRequired'),
          dateBeforeToday: t('clients.rates.errors.dateBeforeToday'),
        },
        { needsType: !editing, minDate: editing ? serverToday() : undefined },
      ),
    [t, editing],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { typeId: '', newTypeName: '', rate: row?.rate ?? null, effectiveFrom: defaultEffectiveDate(tenantToday()) },
  })
  const typeId = useWatch({ control: form.control, name: 'typeId' })
  const options = [...types.map((x) => ({ value: String(x.id), label: x.name ?? '' })), { value: NEW_SPECIAL_TYPE, label: t('clients.special.newType') }]
  const creatingType = typeId === NEW_SPECIAL_TYPE

  return (
    <FormModal
      id="contract-special"
      title={editing ? t('clients.special.editTitle') : t('clients.special.addTitle')}
      open={open}
      onClose={onClose}
      form={form}
      onSubmit={async (v) => {
        if (editing) await mutations.update.mutateAsync({ id: row.id, body: { rate: v.rate ?? 0, effectiveFrom: v.effectiveFrom } })
        else await mutations.add.mutateAsync(buildSpecialCreate(v))
        toast.success(t('clients.special.saved'))
        onClose()
      }}
    >
      {editing ? (
        <>
          <p className="meta">{row.typeName}</p>
          <p className="help">{t('clients.rates.editHelp')}</p>
        </>
      ) : (
        <>
          <Field name="typeId" label={t('clients.special.service')} required>
            <Select options={options} placeholder={t('clients.rates.choose')} />
          </Field>
          {creatingType && (
            <Field name="newTypeName" label={t('clients.special.newTypeName')} help={t('clients.special.newTypeHelp')} required>
              <TextInput maxLength={SPECIAL_NAME_MAX} placeholder={t('clients.special.newTypePh')} />
            </Field>
          )}
        </>
      )}
      <div className="r2">
        <Field name="rate" label={currency ? `${t('clients.special.rate')} (${currency})` : t('clients.special.rate')} required>
          <NumberInput min={0} />
        </Field>
        <Field name="effectiveFrom" label={t('clients.rates.effectiveFrom')} help={editing ? t('clients.rates.effectiveHelpNew') : t('clients.rates.effectiveHelpAdd')} required>
          <DateInput min={editing ? serverToday() : undefined} />
        </Field>
      </div>
    </FormModal>
  )
}

export function ContractSpecialTab({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const clientPublicId = client.publicId ?? ''
  const canUpdate = useCan('contracts.update')
  const [history, setHistory] = useState(false)
  const { data, isPending, error } = useSpecialServices(clientPublicId, history)
  const current = useContract(data?.contractPublicId ?? null)
  const mutations = useSpecialMutations(clientPublicId)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const [modal, setModal] = useState<{ row: SpecialService | null } | null>(null)
  const [removing, setRemoving] = useState<SpecialService | null>(null)

  const currency = current.data?.currency
  const rows = useMemo(() => data?.items ?? [], [data?.items])
  const columns = useMemo<DataColumn<SpecialService>[]>(
    () => [
      { id: 'service', header: t('clients.special.service'), cell: (r) => <b>{r.typeName}</b>, sortValue: (r) => r.typeName, card: 'title' },
      { id: 'rate', header: t('clients.special.rate'), align: 'end', cell: (r) => <span className="mono">{f.money(r.rate, { currency, unitPrice: true })}</span>, sortValue: (r) => r.rate },
      { id: 'from', header: t('clients.rates.from'), cell: (r) => f.date(r.effectiveFrom), sortValue: (r) => r.effectiveFrom },
      ...(history ? [{ id: 'to', header: t('clients.rates.to'), cell: (r: SpecialService) => (r.effectiveTo ? f.date(r.effectiveTo) : '—'), sortValue: (r: SpecialService) => r.effectiveTo }] : []),
    ],
    [t, f, history, currency],
  )

  if (isPending) return <Spinner block />
  if (error) return <EmptyState icon={<IconCash />} title={applyProblemDetails(error).title} />

  const hasContract = !!data?.contractPublicId
  const enabled = data?.componentEnabled ?? false
  const canWrite = canUpdate && hasContract && enabled && (current.data?.canEdit ?? false)

  return (
    <div className="cl-ctr-tab" ref={ref}>
      <div className="cl-rates-bar">
        <label className="sw">
          <input type="checkbox" role="switch" checked={history} onChange={(e) => setHistory(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('clients.rates.showHistory')}</span>
        </label>
        {canWrite && (
          <button type="button" className="btn sm flow" onClick={() => setModal({ row: null })}>
            {t('clients.special.add')}
          </button>
        )}
      </div>
      <p className="meta">{t('clients.special.note')}</p>
      {!hasContract ? (
        <p className="note cl-ctr-note">{t('clients.special.noContract')}</p>
      ) : (
        !enabled && <p className="note cl-ctr-note">{t('clients.special.off')}</p>
      )}
      {rows.length === 0 ? (
        <EmptyState icon={<IconCash />} title={t('clients.special.empty')} />
      ) : (
        <DataTable
          label={t('clients.special.title')}
          columns={columns}
          rows={rows}
          rowKey={(r) => String(r.id)}
          pagination={false}
          exportable={false}
          forceCards={width > 0 && width < 640}
          rowClassName={(r) => (r.isCurrent ? undefined : 'dim')}
          rowActions={[
            { key: 'edit', label: t('clients.edit'), icon: <IconEdit />, onClick: (r) => setModal({ row: r }), visible: (r) => canWrite && !r.effectiveTo },
            { key: 'remove', label: t('clients.remove'), icon: <IconTrash />, tone: 'danger', onClick: (r) => setRemoving(r), visible: (r) => canWrite && !r.effectiveTo },
          ]}
        />
      )}

      {modal && <SpecialModal key={modal.row?.id ?? 'new'} clientPublicId={clientPublicId} row={modal.row} currency={currency} open onClose={() => setModal(null)} />}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('clients.special.removeTitle')}
        message={t('clients.special.removeBody', { name: removing?.typeName ?? '' })}
        confirmLabel={t('clients.remove')}
        onConfirm={async () => {
          if (!removing) return
          await mutations.close.mutateAsync({ id: removing.id })
          toast.success(t('clients.special.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </div>
  )
}
