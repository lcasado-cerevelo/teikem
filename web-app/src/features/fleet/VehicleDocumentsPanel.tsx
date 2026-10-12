// Panel «Documentos del vehículo» de la ficha (manual 04 §1.5): registro, seguro, inspección y permiso. Alta, edición y baja (conserva el
// historial) con el chip de vencimiento compartido (`ExpiryChip`: Vigente / Por vencer / Vencido / Sin vencimiento / Reemplazado, de lo
// que calcula el servidor). No hay subida de archivos: el servidor no expone adjuntos. Lee `fleet.view`; escribe `fleet.manage`; un
// vehículo dado de baja solo se consulta.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, DateInput, ExpiryChip, Field, IconEdit, IconTrash, Panel, Select, TextInput, toast, useElementWidth, type DataColumn } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { lookupOptions } from '../clients/contractRules'
import { FormModal } from '../clients/contractUi'
import { useVehicleDocumentMutations, type VehicleDetail, type VehicleDocument } from './api'
import { buildDocumentRequest, documentSchema, documentValuesOf, emptyDocumentValues, hasDocumentChanges, type DocumentValues } from './fleetRules'

function DocumentModal({ vehicle, row, onClose }: { vehicle: VehicleDetail; row: VehicleDocument | null; onClose: () => void }) {
  const t = useT()
  const mutations = useVehicleDocumentMutations(vehicle.publicId ?? '')
  const { data: types = [] } = useLookups('VehicleDocType')
  const original = row ? documentValuesOf(row) : undefined
  const schema = useMemo(() => documentSchema(t), [t])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: original ?? emptyDocumentValues() })

  return (
    <FormModal
      id="vehicle-document"
      title={row ? t('fleet.docs.edit') : t('fleet.docs.add')}
      open
      size="md"
      onClose={onClose}
      form={form}
      onSubmit={async (v: DocumentValues) => {
        const body = buildDocumentRequest(v, original)
        if (row) {
          if (hasDocumentChanges(body)) await mutations.update.mutateAsync({ id: row.id, body })
        } else await mutations.add.mutateAsync(body)
        toast.success(t('fleet.docs.saved'))
        onClose()
      }}
    >
      <div className="r2">
        <Field name="docType" label={t('fleet.docs.type')} required>
          <Select options={lookupOptions(types, original?.docType)} placeholder={t('fleet.vehicle.choose')} />
        </Field>
        <Field name="docNumber" label={t('fleet.docs.number')}>
          <TextInput autoComplete="off" />
        </Field>
      </div>
      <div className="r2">
        <Field name="issuedDate" label={t('fleet.docs.issued')}>
          <DateInput />
        </Field>
        <Field name="expiryDate" label={t('fleet.docs.expiry')} help={t('fleet.docs.expiryHelp')}>
          <DateInput />
        </Field>
      </div>
    </FormModal>
  )
}

export function VehicleDocumentsPanel({ vehicle }: { vehicle: VehicleDetail }) {
  const t = useT()
  const f = useFormat()
  const canManage = useCan('fleet.manage') && vehicle.isTerminal !== true
  const mutations = useVehicleDocumentMutations(vehicle.publicId ?? '')
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const [editing, setEditing] = useState<{ row: VehicleDocument | null } | null>(null)
  const [removing, setRemoving] = useState<VehicleDocument | null>(null)
  const rows = useMemo(() => vehicle.documents ?? [], [vehicle.documents])

  const columns: DataColumn<VehicleDocument>[] = [
    { id: 'type', header: t('fleet.docs.type'), cell: (r) => <b>{r.docType ?? r.docTypeCode}</b>, sortValue: (r) => r.docType ?? r.docTypeCode ?? '', card: 'title' },
    { id: 'number', header: t('fleet.docs.number'), cell: (r) => <span className="mono">{r.docNumber || '—'}</span>, sortValue: (r) => r.docNumber ?? '' },
    { id: 'issued', header: t('fleet.docs.issued'), cell: (r) => (r.issuedDate ? f.date(r.issuedDate) : '—'), sortValue: (r) => r.issuedDate ?? '' },
    { id: 'expiry', header: t('fleet.docs.expiry'), cell: (r) => (r.expiryDate ? f.date(r.expiryDate) : '—'), sortValue: (r) => r.expiryDate ?? '' },
    {
      id: 'state',
      header: t('fleet.docs.state'),
      cell: (r) => <ExpiryChip state={r.expiryState} days={r.daysToExpiry} superseded={r.isSuperseded} />,
      sortValue: (r) => r.daysToExpiry ?? Number.MAX_SAFE_INTEGER,
    },
  ]

  return (
    <Panel icon={<IconDoc />} title={t('fleet.docs.title')}>
      <div ref={ref}>
        <div className="flt-doc-h">
          <span className="help">{t('fleet.docs.help')}</span>
          {canManage && (
            <button type="button" className="btn sm" onClick={() => setEditing({ row: null })}>
              {t('fleet.docs.add')}
            </button>
          )}
        </div>
        {rows.length === 0 ? (
          <p className="flt-empty">{t('fleet.docs.none')}</p>
        ) : (
          <DataTable
            label={t('fleet.docs.title')}
            columns={columns}
            rows={rows}
            rowKey={(r) => String(r.id)}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 560}
            rowClassName={(r) => (r.isSuperseded ? 'dim' : undefined)}
            rowActions={[
              { key: 'edit', label: t('fleet.edit'), icon: <IconEdit />, onClick: (r) => setEditing({ row: r }), visible: () => canManage },
              { key: 'remove', label: t('fleet.remove'), icon: <IconTrash />, tone: 'danger', onClick: (r) => setRemoving(r), visible: () => canManage },
            ]}
          />
        )}
      </div>
      {editing && <DocumentModal key={editing.row?.id ?? 'new'} vehicle={vehicle} row={editing.row} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('fleet.docs.removeTitle')}
        message={t('fleet.docs.removeBody', { type: removing?.docType ?? removing?.docTypeCode ?? '', number: removing?.docNumber || '—' })}
        confirmLabel={t('fleet.remove')}
        onConfirm={async () => {
          if (!removing) return
          await mutations.remove.mutateAsync(removing.id)
          toast.success(t('fleet.docs.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}
