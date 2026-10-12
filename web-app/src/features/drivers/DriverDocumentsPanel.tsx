// Panel «Licencias y certificaciones» de la ficha (manual 04 §2.5): alta, edición y baja (conserva historial) con el chip de
// vencimiento compartido (`ExpiryChip`: Vigente / Por vencer / Vencida / Sin vencimiento / Reemplazada, de lo que calcula el
// servidor). Lee `fleet.view`; escribe `fleet.manage`; un chofer eliminado solo se consulta.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, DateInput, ExpiryChip, Field, IconEdit, IconTrash, Panel, Select, TextInput, toast, useElementWidth, type DataColumn } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { lookupOptions, renameErrors, rethrowRenamed } from '../clients/contractRules'
import { FormModal } from '../clients/contractUi'
import { useCertificationMutations, useLicenseMutations, type DriverCertification, type DriverDetail, type DriverLicense } from './api'
import {
  buildCertificationRequest,
  buildLicenseRequest,
  certificationValuesOf,
  documentSchema,
  emptyDocumentValues,
  hasDocumentChanges,
  licenseValuesOf,
  type DocumentValues,
} from './driverRules'

type Kind = 'license' | 'certification'
type Row = DriverLicense | DriverCertification

const isLicense = (r: Row): r is DriverLicense => 'licenseNumber' in r

function DocumentModal({ driver, kind, row, onClose }: { driver: DriverDetail; kind: Kind; row: Row | null; onClose: () => void }) {
  const t = useT()
  const publicId = driver.publicId ?? ''
  const licenses = useLicenseMutations(publicId)
  const certs = useCertificationMutations(publicId)
  const { data: classes = [] } = useLookups('LicenseClass')
  const { data: certTypes = [] } = useLookups('CertificationType')
  const original = row ? (isLicense(row) ? licenseValuesOf(row) : certificationValuesOf(row)) : undefined
  const schema = useMemo(() => documentSchema(t, kind), [t, kind])
  const form = useForm({ resolver: zodResolver(schema), defaultValues: original ?? emptyDocumentValues() })
  const typeOptions = lookupOptions(kind === 'license' ? classes : certTypes, original?.type)
  const names = { licenseClass: 'type', certType: 'type', licenseNumber: 'number', certNumber: 'number' }

  const title = kind === 'license' ? (row ? t('drivers.docs.editLicense') : t('drivers.docs.addLicense')) : row ? t('drivers.docs.editCert') : t('drivers.docs.addCert')
  return (
    <FormModal
      id={`driver-${kind}`}
      title={title}
      open
      size="md"
      onClose={onClose}
      form={form}
      onSubmit={async (v: DocumentValues) => {
        try {
          if (kind === 'license') {
            const body = buildLicenseRequest(v, original)
            if (row) {
              if (hasDocumentChanges(body)) await licenses.update.mutateAsync({ id: row.id, body })
            } else await licenses.add.mutateAsync(body)
          } else {
            const body = buildCertificationRequest(v, original)
            if (row) {
              if (hasDocumentChanges(body)) await certs.update.mutateAsync({ id: row.id, body })
            } else await certs.add.mutateAsync(body)
          }
        } catch (err) {
          // el servidor nombra los campos licenseClass / licenseNumber / certType / certNumber; el formulario usa type / number
          rethrowRenamed(err, (e) => renameErrors(e, names))
        }
        toast.success(t('drivers.docs.saved'))
        onClose()
      }}
    >
      <div className="r2">
        <Field name="type" label={kind === 'license' ? t('drivers.docs.licenseClass') : t('drivers.docs.certType')} required>
          <Select options={typeOptions} placeholder={t('drivers.choose')} />
        </Field>
        <Field name="number" label={kind === 'license' ? t('drivers.docs.licenseNumber') : t('drivers.docs.certNumber')} required={kind === 'license'}>
          <TextInput autoComplete="off" />
        </Field>
      </div>
      <div className="r2">
        <Field name="issuedDate" label={t('drivers.docs.issued')}>
          <DateInput />
        </Field>
        <Field name="expiryDate" label={t('drivers.docs.expiry')} help={t('drivers.docs.expiryHelp')}>
          <DateInput />
        </Field>
      </div>
    </FormModal>
  )
}

export function DriverDocumentsPanel({ driver }: { driver: DriverDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = driver.publicId ?? ''
  const canManage = useCan('fleet.manage') && driver.isTerminal !== true
  const licenses = useLicenseMutations(publicId)
  const certs = useCertificationMutations(publicId)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const [editing, setEditing] = useState<{ kind: Kind; row: Row | null } | null>(null)
  const [removing, setRemoving] = useState<{ kind: Kind; row: Row } | null>(null)

  const licenseRows = useMemo(() => driver.licenses ?? [], [driver.licenses])
  const certRows = useMemo(() => driver.certifications ?? [], [driver.certifications])

  const columns = (kind: Kind): DataColumn<Row>[] => [
    {
      id: 'type',
      header: kind === 'license' ? t('drivers.docs.licenseClass') : t('drivers.docs.certType'),
      cell: (r) => <b>{isLicense(r) ? (r.licenseClass ?? r.licenseClassCode) : (r.certType ?? r.certTypeCode)}</b>,
      sortValue: (r) => (isLicense(r) ? (r.licenseClass ?? '') : (r.certType ?? '')),
      card: 'title',
    },
    {
      id: 'number',
      header: kind === 'license' ? t('drivers.docs.licenseNumber') : t('drivers.docs.certNumber'),
      cell: (r) => <span className="mono">{(isLicense(r) ? r.licenseNumber : r.certNumber) || '—'}</span>,
      sortValue: (r) => (isLicense(r) ? r.licenseNumber : r.certNumber) ?? '',
    },
    { id: 'issued', header: t('drivers.docs.issued'), cell: (r) => (r.issuedDate ? f.date(r.issuedDate) : '—'), sortValue: (r) => r.issuedDate ?? '' },
    { id: 'expiry', header: t('drivers.docs.expiry'), cell: (r) => (r.expiryDate ? f.date(r.expiryDate) : '—'), sortValue: (r) => r.expiryDate ?? '' },
    {
      id: 'state',
      header: t('drivers.docs.state'),
      cell: (r) => <ExpiryChip state={r.expiryState} days={r.daysToExpiry} superseded={r.isSuperseded} />,
      sortValue: (r) => r.daysToExpiry ?? Number.MAX_SAFE_INTEGER,
    },
  ]

  const section = (kind: Kind, rows: Row[]) => {
    const label = kind === 'license' ? t('drivers.docs.licenses') : t('drivers.docs.certs')
    return (
      <section className="drv-doc" aria-label={label}>
        <div className="drv-doc-h">
          <h3>{label}</h3>
          {canManage && (
            <button type="button" className="btn sm" onClick={() => setEditing({ kind, row: null })}>
              {kind === 'license' ? t('drivers.docs.addLicense') : t('drivers.docs.addCert')}
            </button>
          )}
        </div>
        {rows.length === 0 ? (
          <p className="drv-empty">{kind === 'license' ? t('drivers.docs.noLicenses') : t('drivers.docs.noCerts')}</p>
        ) : (
          <DataTable
            label={label}
            columns={columns(kind)}
            rows={rows}
            rowKey={(r) => String(r.id)}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 640}
            rowClassName={(r) => (r.isSuperseded ? 'dim' : undefined)}
            rowActions={[
              { key: 'edit', label: t('drivers.edit'), icon: <IconEdit />, onClick: (r) => setEditing({ kind, row: r }), visible: () => canManage },
              { key: 'remove', label: t('drivers.remove'), icon: <IconTrash />, tone: 'danger', onClick: (r) => setRemoving({ kind, row: r }), visible: () => canManage },
            ]}
          />
        )}
      </section>
    )
  }

  const removingNumber = removing ? ((isLicense(removing.row) ? removing.row.licenseNumber : removing.row.certNumber) ?? '') : ''
  const removingType = removing ? (isLicense(removing.row) ? removing.row.licenseClass : removing.row.certType) : ''

  return (
    <Panel icon={<IconDoc />} title={t('drivers.docs.title')}>
      <div ref={ref}>
        {section('license', licenseRows)}
        {section('certification', certRows)}
      </div>
      {editing && <DocumentModal key={`${editing.kind}-${editing.row?.id ?? 'new'}`} driver={driver} kind={editing.kind} row={editing.row} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={removing?.kind === 'license' ? t('drivers.docs.removeLicenseTitle') : t('drivers.docs.removeCertTitle')}
        message={t(removing?.kind === 'license' ? 'drivers.docs.removeLicenseBody' : 'drivers.docs.removeCertBody', { type: removingType ?? '', number: removingNumber || '—' })}
        confirmLabel={t('drivers.remove')}
        onConfirm={async () => {
          if (!removing) return
          if (removing.kind === 'license') await licenses.remove.mutateAsync(removing.row.id)
          else await certs.remove.mutateAsync(removing.row.id)
          toast.success(t('drivers.docs.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}
