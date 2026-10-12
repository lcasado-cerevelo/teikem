// Pestaña «Documentos por vencer» (maqueta `flota()`, panel «Documentos por vencer»; manual 04 §3): une documentos de vehículo, licencias y
// certificaciones vencidos o que vencen dentro de una ventana. Filtros multi «Documento» y «Entidad» + «Limpiar» (como la maqueta), más
// «Dentro de N días» (30 por omisión) e «Incluir vencidos». Lista completa (el API no pagina): `DataTable` ordena con `sortValue`.
// Un clic en la fila abre al vehículo (`?vehicle=`) o al chofer (`/catalog/drivers?driver=`).
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, FilterScope, Filters, matchesQ, Panel, QBox, SearchSelect, Spinner, type DataColumn } from '../../kernel/ui'
import { IconAlert } from '../../kernel/ui/icons'
import { useExpiringDocuments, type ExpiringDocument } from './api'
import { buildExpiringQuery, daysChipInfo, DEFAULT_WITHIN_DAYS, DOC_KIND_CERTIFICATION, DOC_KIND_LICENSE, MAX_WITHIN_DAYS, parseWithinDays } from './fleetRules'

const NO_ROWS: ExpiringDocument[] = []
const VEHICLE = 'VEHICLE'
const DRIVER = 'DRIVER'

export function ExpiringDocumentsTab() {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const { data: vehicleDocTypes = [] } = useLookups('VehicleDocType')
  const [docTypes, setDocTypes] = useState<string[]>([])
  const [entities, setEntities] = useState<string[]>([])
  const [daysText, setDaysText] = useState(String(DEFAULT_WITHIN_DAYS))
  // la ventana que se pide es la última válida: un valor a medias o fuera de rango no cambia la consulta
  const [within, setWithin] = useState(DEFAULT_WITHIN_DAYS)
  const [includeExpired, setIncludeExpired] = useState(true)
  const [q, setQ] = useState('')
  const daysInvalid = parseWithinDays(daysText) === null

  const query = useMemo(() => buildExpiringQuery({ withinDays: within, includeExpired, docTypes, entities }), [within, includeExpired, docTypes, entities])
  const list = useExpiringDocuments(query)
  // el buscador filtra lo cargado (entidad, documento y número), sin acentos ni mayúsculas
  const rows = useMemo(() => (list.data ?? NO_ROWS).filter((r) => matchesQ(q, r.ownerCode, r.ownerName, r.documentType, r.documentTypeCode, r.docNumber)), [list.data, q])

  const docOptions = useMemo(
    () => [
      ...vehicleDocTypes.map((o) => ({ value: o.code, label: o.label })),
      { value: DOC_KIND_LICENSE, label: t('fleet.docs.license') },
      { value: DOC_KIND_CERTIFICATION, label: t('fleet.docs.certification') },
    ],
    [vehicleDocTypes, t],
  )
  const entityOptions = useMemo(
    () => [
      { value: VEHICLE, label: t('fleet.docs.entityVehicle') },
      { value: DRIVER, label: t('fleet.docs.entityDriver') },
    ],
    [t],
  )

  const clear = () => {
    setDocTypes([])
    setEntities([])
    setDaysText(String(DEFAULT_WITHIN_DAYS))
    setWithin(DEFAULT_WITHIN_DAYS)
    setIncludeExpired(true)
    setQ('')
  }

  const open = (r: ExpiringDocument) => {
    if (r.ownerKind === DRIVER) navigate(`/catalog/drivers?driver=${r.ownerPublicId}`)
    else navigate(`/catalog/fleet?vehicle=${r.ownerPublicId}`)
  }

  const columns = useMemo<DataColumn<ExpiringDocument>[]>(
    () => [
      {
        id: 'entity',
        header: t('fleet.docs.entity'),
        cell: (r) => (
          <>
            <span className="ref">{r.ownerCode}</span> {r.ownerName}
            <span className="tag flt-tag">{r.ownerKind === DRIVER ? t('fleet.docs.entityDriver') : t('fleet.docs.entityVehicle')}</span>
          </>
        ),
        exportValue: (r) => `${r.ownerCode ?? ''} ${r.ownerName ?? ''} (${r.ownerKind === DRIVER ? t('fleet.docs.entityDriver') : t('fleet.docs.entityVehicle')})`,
        sortValue: (r) => `${r.ownerCode ?? ''} ${r.ownerName ?? ''}`,
        card: 'title',
      },
      {
        id: 'docType',
        header: t('fleet.docs.document'),
        cell: (r) => (
          <>
            {r.documentType ?? r.documentTypeCode}
            {r.docNumber ? <span className="meta mono"> · {r.docNumber}</span> : null}
          </>
        ),
        sortValue: (r) => r.documentType ?? r.documentTypeCode ?? '',
      },
      { id: 'expiry', header: t('fleet.docs.expires'), cell: (r) => <span className="mono">{f.date(r.expiryDate)}</span>, sortValue: (r) => r.expiryDate },
      {
        id: 'days',
        header: t('fleet.docs.daysLeft'),
        align: 'end',
        cell: (r) => {
          const info = daysChipInfo(r.daysToExpiry)
          return (
            <Chip tone={info.tone} title={r.daysToExpiry < 0 ? t('fleet.docs.expiredTitle', { days: Math.abs(r.daysToExpiry) }) : undefined}>
              {r.daysToExpiry < 0 ? t('fleet.docs.expiredChip') : t(info.key, info.params)}
            </Chip>
          )
        },
        exportValue: (r) => r.daysToExpiry,
        sortValue: (r) => r.daysToExpiry,
      },
    ],
    [t, f],
  )

  return (
    <FilterScope>
      <Panel flush icon={<IconAlert />} title={t('fleet.docs.dueTitle')} badge={list.isPending ? undefined : rows.length}>
        <div className="pb" style={{ paddingBottom: 0 }}>
          <div className="flt-bar">
            <QBox value={q} onChange={setQ} placeholder={t('ui.qbox.placeholder')} />
          </div>
          <Filters onClear={clear}>
            <SearchSelect label={t('fleet.docs.document')} options={docOptions} value={docTypes} onChange={setDocTypes} placeholder={t('fleet.docs.all')} />
            <SearchSelect label={t('fleet.docs.entity')} options={entityOptions} value={entities} onChange={setEntities} placeholder={t('fleet.docs.all')} />
            <div className="f">
              <label htmlFor="flt-within">{t('fleet.docs.within')}</label>
              <input
                id="flt-within"
                className="mono"
                type="number"
                inputMode="numeric"
                min={0}
                max={MAX_WITHIN_DAYS}
                step={1}
                value={daysText}
                aria-invalid={daysInvalid || undefined}
                onChange={(e) => {
                  setDaysText(e.target.value)
                  const n = parseWithinDays(e.target.value)
                  if (n !== null) setWithin(n)
                }}
              />
              {daysInvalid && (
                <p className="ferr" role="alert">
                  {t('fleet.docs.withinInvalid', { max: MAX_WITHIN_DAYS })}
                </p>
              )}
            </div>
            <label className="sw">
              <input type="checkbox" role="switch" checked={includeExpired} onChange={(e) => setIncludeExpired(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('fleet.docs.includeExpired')}</span>
            </label>
          </Filters>
        </div>
        <div className="pb" style={{ paddingTop: 10 }}>
          {list.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('fleet.docs.dueTitle')}
              columns={columns}
              rows={rows}
              rowKey={(r) => r.rowKey ?? `${r.ownerKind}-${r.ownerPublicId}-${r.documentTypeCode}-${r.expiryDate}`}
              defaultSort={{ id: 'expiry', desc: false }}
              onRowClick={open}
              empty={t('fleet.docs.noneDue')}
            />
          )}
        </div>
      </Panel>
    </FilterScope>
  )
}
