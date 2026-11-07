// Lote 222: panel «Documentos del cliente» (permiso, licencia, SURI…): lista, subir y quitar. Ver: clients.read. Subir/quitar: clients.update, o clients.create_basic mientras el cliente es prospecto.
import { useRef, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, Panel, Spinner, toast } from '../../kernel/ui'
import { openClientFile, useClientDocuments, useDeleteClientDocument, useUploadClientDocument, type ClientDocumentDto } from './clientSalesApi'
import { isProspect, type ClientDetail } from './clientRules'

export function ClientDocumentsPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = client.publicId ?? ''
  const canUpdate = useCan('clients.update')
  const canBasic = useCan('clients.create_basic')
  const canWrite = canUpdate || (canBasic && isProspect(client))
  const docs = useClientDocuments(publicId)
  const upload = useUploadClientDocument(publicId)
  const remove = useDeleteClientDocument(publicId)
  const { data: types = [] } = useLookups('ClientDocumentType')
  const [type, setType] = useState('')
  const [notes, setNotes] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [removing, setRemoving] = useState<ClientDocumentDto | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const list = docs.data ?? []

  const send = async () => {
    setError(null)
    const file = fileRef.current?.files?.[0]
    if (!type) return setError(t('clients.documents.typeRequired'))
    if (!file) return setError(t('clients.documents.fileRequired'))
    try {
      await upload.mutateAsync({ file, documentType: type, notes })
      toast.success(t('clients.documents.uploaded'))
      setNotes('')
      if (fileRef.current) fileRef.current.value = ''
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  const open = async (d: ClientDocumentDto) => {
    try {
      await openClientFile(d.filePublicId ?? '')
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel title={t('clients.documents.title')} collapsible defaultCollapsed summary={t('clients.documents.summary', { n: list.length })}>
      {docs.isPending ? (
        <Spinner block />
      ) : docs.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(docs.error).title}</p>
      ) : list.length === 0 ? (
        <p className="help">{t('clients.documents.empty')}</p>
      ) : (
        <ul className="cl-docs" aria-label={t('clients.documents.title')}>
          {list.map((d) => (
            <li key={d.publicId}>
              <button type="button" className="btn sm" onClick={() => void open(d)}>{d.fileName}</button>{' '}
              <b>{d.documentTypeLabel ?? d.documentType}</b>
              <span className="help"> {[d.notes, d.createdByName, f.date(d.createdAtUtc)].filter(Boolean).join(' · ')}</span>
              {canWrite && (
                <button type="button" className="btn sm danger" onClick={() => setRemoving(d)} aria-label={t('clients.documents.removeOf', { name: d.fileName ?? '' })}>{t('clients.documents.remove')}</button>
              )}
            </li>
          ))}
        </ul>
      )}
      {canWrite && (
        <div className="cl-doc-new">
          <div className="r3">
            <div className="f">
              <label htmlFor="cl-doc-type">{t('clients.documents.type')}</label>
              <select id="cl-doc-type" value={type} onChange={(e) => setType(e.target.value)}>
                <option value="">{t('clients.documents.typePlaceholder')}</option>
                {types.map((o) => (
                  <option key={o.code} value={o.code}>{o.label}</option>
                ))}
              </select>
            </div>
            <div className="f">
              <label htmlFor="cl-doc-file">{t('clients.documents.file')}</label>
              <input id="cl-doc-file" type="file" ref={fileRef} />
            </div>
            <div className="f">
              <label htmlFor="cl-doc-notes">{t('clients.documents.notes')}</label>
              <input id="cl-doc-notes" value={notes} maxLength={300} autoComplete="off" onChange={(e) => setNotes(e.target.value)} />
            </div>
          </div>
          {error && <p className="ferr" role="alert">{error}</p>}
          <div className="form-acts">
            <button type="button" className="btn flow" disabled={upload.isPending} onClick={() => void send()}>{upload.isPending ? t('common.loading') : t('clients.documents.upload')}</button>
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
          await remove.mutateAsync(removing?.publicId ?? '')
          toast.success(t('clients.documents.removed'))
        }}
        onClose={() => setRemoving(null)}
      />
    </Panel>
  )
}
