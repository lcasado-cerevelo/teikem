// Lote 222: panel «Documentos del cliente» (permiso, licencia, SURI…). Ver: clients.read. Subir/editar/quitar: clients.update, o clients.create_basic mientras el cliente es prospecto.
// Lote 248: cada documento lleva número y vigencia («desde» y «hasta»; «hasta» obligatorio si el tipo lleva vigencia) con su estado (Vigente, Por vencer, Vencido, Renovado, Falta «hasta»).
// Un documento de un tipo «de impuestos» pregunta «¿Aplicar la exención de IVU?» al subirlo (tax.manage y módulo TAX) y abre la captura de la exención con ese documento.
// Un documento que respalda una exención no se quita (primero se da de baja la exención) y cambiar su número o vigencia exige tax.manage.
import { useMemo, useRef, useState } from 'react'
import { ModuleKeys, useCan, useModule } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, DateField, Modal, Panel, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconCheckCircle, IconEdit, IconTrash } from '../../kernel/ui/actionIcons'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { ExemptionCaptureModal } from '../tax/ExemptionCaptureModal'
import { openClientFile, useClientDocuments, useDeleteClientDocument, useUploadClientDocument, type ClientDocumentDto } from './clientSalesApi'
import { documentError, documentFieldsError, expiryStateKey, expiryTone, validityRange, type DocumentTypeMarks } from './documentRules'
import { useClientDocumentTypes, useUpdateClientDocument } from './documentsApi'
import { isProspect, type ClientDetail } from './clientRules'

const NO_MARKS: DocumentTypeMarks = { requiresValidTo: false, isTax: false }

/** Días que faltan (o pasaron) como texto corto: «12 días», «hoy», «hace 3 días». */
function useDaysText() {
  const t = useT()
  return (days: number | null | undefined) => (days == null ? '—' : days === 0 ? t('clients.documents.today') : days > 0 ? t('clients.documents.inDays', { n: days }) : t('clients.documents.daysAgo', { n: -days }))
}

function EditDocumentModal({ clientPublicId, doc, marks, onClose }: { clientPublicId: string; doc: ClientDocumentDto; marks: DocumentTypeMarks; onClose: () => void }) {
  const t = useT()
  const update = useUpdateClientDocument(clientPublicId)
  const [number, setNumber] = useState(doc.documentNumber ?? '')
  const [from, setFrom] = useState(doc.validFrom ?? '')
  const [to, setTo] = useState(doc.validTo ?? '')
  const [notes, setNotes] = useState(doc.notes ?? '')
  const [error, setError] = useState<string | null>(null)
  const busy = update.isPending
  const requires = marks.requiresValidTo || doc.requiresValidTo === true

  const save = async () => {
    setError(null)
    const problem = documentFieldsError({ number, from, to }, { ...marks, requiresValidTo: requires })
    if (problem) return setError(t(`clients.documents.errors.${problem}`))
    try {
      await update.mutateAsync({
        documentPublicId: doc.publicId ?? '',
        body: {
          documentNumber: number.trim(),
          validFrom: from || undefined,
          validTo: to || undefined,
          clearValidFrom: !from && !!doc.validFrom,
          clearValidTo: !to && !!doc.validTo,
          notes: notes.trim(),
          rowVersion: doc.rowVersion,
        },
      })
      toast.success(t('clients.documents.updated'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('clients.documents.editTitle', { type: doc.documentTypeLabel ?? doc.documentType ?? '' })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>{busy ? t('common.loading') : t('clients.documents.save')}</button>
        </>
      }
    >
      <div className="pb cdoc-form">
        {doc.backsExemption && <p className="cdoc-note" role="status">{t('clients.documents.backsHelp')}</p>}
        <div className="f">
          <label htmlFor="cdoc-e-number">{t('clients.documents.number')}</label>
          <input id="cdoc-e-number" value={number} maxLength={60} autoComplete="off" onChange={(e) => setNumber(e.target.value)} />
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="cdoc-e-from">{t('clients.documents.from')}</label>
            <DateField id="cdoc-e-from" value={from} onChange={setFrom} />
          </div>
          <div className="f">
            <label htmlFor="cdoc-e-to">{requires ? t('clients.documents.toRequired') : t('clients.documents.to')}</label>
            <DateField id="cdoc-e-to" value={to} onChange={setTo} />
          </div>
        </div>
        <div className="f">
          <label htmlFor="cdoc-e-notes">{t('clients.documents.notes')}</label>
          <input id="cdoc-e-notes" value={notes} maxLength={300} autoComplete="off" onChange={(e) => setNotes(e.target.value)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

export function ClientDocumentsPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const daysText = useDaysText()
  const publicId = client.publicId ?? ''
  const canUpdate = useCan('clients.update')
  const canBasic = useCan('clients.create_basic')
  const canTax = useCan('tax.manage')
  const taxOn = useModule(ModuleKeys.Tax)
  const canWrite = canUpdate || (canBasic && isProspect(client))
  const canExempt = canTax && taxOn
  const docs = useClientDocuments(publicId)
  const upload = useUploadClientDocument(publicId)
  const remove = useDeleteClientDocument(publicId)
  const types = useClientDocumentTypes()
  const [type, setType] = useState('')
  const [notes, setNotes] = useState('')
  const [number, setNumber] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [hasFile, setHasFile] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [removing, setRemoving] = useState<ClientDocumentDto | null>(null)
  const [editing, setEditing] = useState<ClientDocumentDto | null>(null)
  const [asking, setAsking] = useState<ClientDocumentDto | null>(null)
  const [exempting, setExempting] = useState<ClientDocumentDto | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const list = docs.data ?? []
  const marksOf = (code: string | null | undefined) => (types.data ?? []).find((x) => x.code === code)?.marks ?? NO_MARKS
  const chosen = marksOf(type)

  const send = async () => {
    setError(null)
    const file = fileRef.current?.files?.[0]
    const problem = documentError({ type, number, from, to }, !!file, chosen)
    if (problem) return setError(t(`clients.documents.errors.${problem}`))
    try {
      const doc = await upload.mutateAsync({ file: file!, documentType: type, notes, documentNumber: number, validFrom: from, validTo: to })
      toast.success(t('clients.documents.uploaded'))
      setNotes('')
      setNumber('')
      setFrom('')
      setTo('')
      setHasFile(false)
      if (fileRef.current) fileRef.current.value = ''
      if (doc.suggestExemption && canExempt) setAsking(doc)
    } catch (e) {
      const p = applyProblemDetails(e)
      setError([p.title, ...Object.values(p.errors).flat().filter((m) => m !== p.title)].join(' '))
    }
  }

  const open = async (d: ClientDocumentDto) => {
    try {
      await openClientFile(d.filePublicId ?? '')
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  // panel angosto (la ficha comparte el ancho con la lista): pocas columnas y dos líneas por fila, sin recortes
  const columns = useMemo<DataColumn<ClientDocumentDto>[]>(
    () => [
      {
        id: 'type',
        header: t('clients.documents.type'),
        cell: (d) => (
          <span className="cdoc-type" title={[d.fileName, d.notes, d.createdByName].filter(Boolean).join(' · ') || undefined}>
            <b className="cdoc-type-text">{d.documentTypeLabel ?? d.documentType}</b>
            {d.backsExemption ? <Chip tone="cod" title={t('clients.documents.backsHelp')}>{t('clients.documents.backs')}</Chip> : <span className="help cdoc-sub">{d.fileName}</span>}
          </span>
        ),
        sortValue: (d) => d.documentTypeLabel ?? d.documentType ?? '',
        exportValue: (d) => d.documentTypeLabel ?? d.documentType ?? '',
        card: 'title',
      },
      { id: 'number', header: t('clients.documents.number'), cell: (d) => <span className="cdoc-nowrap">{d.documentNumber ?? '—'}</span>, sortValue: (d) => d.documentNumber ?? '', exportValue: (d) => d.documentNumber ?? '' },
      {
        id: 'validity',
        header: t('clients.documents.validity'),
        cell: (d) => (
          <span className="cdoc-type">
            <span className="cdoc-nowrap">{validityRange(d.validFrom, d.validTo, f.date)}</span>
            <span className="help cdoc-sub">{daysText(d.daysToExpiry)}</span>
          </span>
        ),
        sortValue: (d) => d.validTo ?? '9999-12-31',
        exportValue: (d) => validityRange(d.validFrom, d.validTo, f.date),
      },
      {
        id: 'state',
        header: t('clients.documents.state'),
        cell: (d) => (
          <Chip tone={expiryTone(d.expiryState)}>{t(expiryStateKey(d.expiryState))}</Chip>
        ),
        sortValue: (d) => d.expiryState ?? '',
        exportValue: (d) => t(expiryStateKey(d.expiryState)),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f],
  )

  return (
    <Panel title={t('clients.documents.title')} collapsible defaultCollapsed summary={t('clients.documents.summary', { n: list.length })}>
      {docs.isPending ? (
        <Spinner block />
      ) : docs.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(docs.error).title}</p>
      ) : list.length === 0 ? (
        <p className="help">{t('clients.documents.empty')}</p>
      ) : (
        <DataTable
          label={t('clients.documents.title')}
          columns={columns}
          rows={list}
          rowKey={(d) => d.publicId ?? ''}
          pagination={false}
          exportable={false}
          dense
          defaultSort={{ id: 'validity', desc: false }}
          rowActions={[
            { key: 'open', label: t('clients.documents.openFile'), icon: <IconDoc />, onClick: (d) => void open(d) },
            { key: 'exempt', label: t('clients.documents.applyExemption'), icon: <IconCheckCircle />, onClick: (d) => setExempting(d), visible: (d) => canExempt && d.isTax === true && !d.backsExemption },
            { key: 'edit', label: t('clients.documents.edit'), icon: <IconEdit />, onClick: (d) => setEditing(d), visible: () => canWrite },
            { key: 'remove', label: t('clients.documents.remove'), icon: <IconTrash />, tone: 'danger', onClick: (d) => setRemoving(d), visible: () => canWrite },
          ]}
        />
      )}
      {canWrite && (
        <div className="cl-doc-new">
          <div className="r3">
            <div className="f">
              <label htmlFor="cl-doc-type">{t('clients.documents.type')}</label>
              <select id="cl-doc-type" value={type} onChange={(e) => setType(e.target.value)}>
                <option value="">{t('clients.documents.typePlaceholder')}</option>
                {(types.data ?? []).map((o) => (
                  <option key={o.code} value={o.code}>{o.label}</option>
                ))}
              </select>
            </div>
            <div className="f">
              <label htmlFor="cl-doc-file">{t('clients.documents.file')}</label>
              <input id="cl-doc-file" type="file" ref={fileRef} onChange={(e) => setHasFile((e.target.files?.length ?? 0) > 0)} />
            </div>
            <div className="f">
              <label htmlFor="cl-doc-number">{t('clients.documents.number')}</label>
              <input id="cl-doc-number" value={number} maxLength={60} autoComplete="off" onChange={(e) => setNumber(e.target.value)} />
            </div>
          </div>
          <div className="r3">
            <div className="f">
              <label htmlFor="cl-doc-from">{t('clients.documents.from')}</label>
              <DateField id="cl-doc-from" value={from} onChange={setFrom} />
            </div>
            <div className="f">
              <label htmlFor="cl-doc-to">{chosen.requiresValidTo ? t('clients.documents.toRequired') : t('clients.documents.to')}</label>
              <DateField id="cl-doc-to" value={to} onChange={setTo} />
              {chosen.requiresValidTo && <p className="help">{t('clients.documents.toHelp')}</p>}
            </div>
            <div className="f">
              <label htmlFor="cl-doc-notes">{t('clients.documents.notes')}</label>
              <input id="cl-doc-notes" value={notes} maxLength={300} autoComplete="off" onChange={(e) => setNotes(e.target.value)} />
            </div>
          </div>
          {error && <p className="ferr" role="alert">{error}</p>}
          <div className="form-acts">
            <button type="button" className="btn flow" disabled={upload.isPending || (hasFile && !type)} onClick={() => void send()}>{upload.isPending ? t('common.loading') : t('clients.documents.upload')}</button>
          </div>
        </div>
      )}
      <ConfirmDialog
        open={removing !== null}
        tone="danger"
        title={t('clients.documents.removeTitle')}
        message={t('clients.documents.removeBody', { name: removing?.fileName ?? '' })}
        confirmLabel={t('clients.documents.remove')}
        onConfirm={async () => {
          // un 409 («respalda una exención vigente») se muestra dentro del diálogo, que no se cierra
          await remove.mutateAsync(removing?.publicId ?? '')
          toast.success(t('clients.documents.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
      <ConfirmDialog
        open={asking !== null}
        title={t('clients.documents.askExemptionTitle')}
        message={t('clients.documents.askExemptionBody', { type: asking?.documentTypeLabel ?? asking?.documentType ?? '' })}
        confirmLabel={t('clients.documents.askExemptionYes')}
        onConfirm={() => {
          setExempting(asking)
        }}
        onClose={() => setAsking(null)}
      />
      {editing && <EditDocumentModal key={editing.publicId} clientPublicId={publicId} doc={editing} marks={marksOf(editing.documentType)} onClose={() => setEditing(null)} />}
      {exempting && <ExemptionCaptureModal clientPublicId={publicId} clientName={client.name} document={exempting} onClose={() => setExempting(null)} />}
    </Panel>
  )
}
