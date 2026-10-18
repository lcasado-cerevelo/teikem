// Entrada RÁPIDA de órdenes (Lote F-C3; maqueta `quickEditor`/`initQuick`): una rejilla de captura en la que cada Enter (o «Guardar fila»)
// crea UNA orden de 1 paquete y 1 pieza Sometida (Lote 106: al crear ya se cotiza y se mide el crédito; si se excede, el servidor responde 422).
// La fila guardada queda de solo lectura con su número y arriba aparece una fila nueva con el cliente y el servicio conservados. Barra:
// Cliente (obligatorio), Líneas guardadas y COD acumulado. Columnas: N.º de orden (solo si el cliente asigna sus números), Consignatario
// (directorio del cliente + compartidos, «+» con `locations.create`), Dirección · Pueblo, Paquete, COD, Servicio y Comentario.
// Los errores del servidor se muestran en la fila sin perder lo capturado. Lote F-G2: «Label al guardar» en la leyenda (como la maqueta):
// encendido, al guardar cada fila sale el label de cada pieza por la impresora de labels de la estación (lo imprime la lista: `onSaved`).
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { Link } from 'react-router-dom'
import { tenantToday } from '../../kernel/api/tenantZone'
import { lookupLabelOrCode, useLookups, useTenantSettings } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ClientPicker } from '../../kernel/ui'
import { useClient } from '../clients/api'
import { useSelectedConsignee } from '../consignees/api'
import { ConsigneePicker } from '../consignees/ConsigneePicker'
import { consigneeAddress } from '../consignees/consigneeRules'
import { useCreateOrder, type OrderDetailDto } from './api'
import {
  buildQuickRequest,
  emptyQuickRow,
  NOTES_MAX,
  nextQuickRow,
  quickCod,
  quickProblemOf,
  validateQuickRow,
  type QuickField,
  type QuickRowValues,
} from './orderQuickRules'
import { NUMBER_MAX } from './orderRules'
import './orders.css'

/** Una fila ya guardada: de solo lectura, con el número que le dio el servidor. */
interface SavedRow {
  key: number
  orderNumber: string
  publicId: string
  consignee: string
  address: string
  packageLabel: string
  cod: number
  serviceLabel: string
  notes: string
}

type RowErrors = Partial<Record<QuickField, string>>

/** Orden en que se recorren los campos de la fila (para llevar el foco al primer error). */
const FIELD_ORDER: QuickField[] = ['clientPublicId', 'orderNumber', 'consigneeId', 'packageType', 'cod', 'serviceType', 'notes']
const FIELD_IDS: Record<QuickField, string> = {
  clientPublicId: 'qk-client',
  orderNumber: 'qk-order',
  consigneeId: 'qk-cons',
  packageType: 'qk-pkg',
  cod: 'qk-cod',
  serviceType: 'qk-svc',
  notes: 'qk-note',
}

export interface LabelOnSaveSwitch {
  value: boolean
  onChange: (on: boolean) => void
}

export interface OrderQuickEntryProps {
  /** Cada orden guardada (para imprimir su label si «Label al guardar» está encendido). */
  onSaved?: (order: OrderDetailDto) => void
  /** Interruptor «Label al guardar» (sin él no se pinta). */
  labelOnSave?: LabelOnSaveSwitch
}

export function OrderQuickEntry({ onSaved, labelOnSave }: OrderQuickEntryProps = {}) {
  const t = useT()
  const f = useFormat()
  const create = useCreateOrder()
  const { data: settings } = useTenantSettings()
  const { data: serviceTypes = [] } = useLookups('ServiceType')
  const { data: packageTypes = [] } = useLookups('PackageType')
  const [clientId, setClientId] = useState<string | null>(null)
  const [raw, setDraft] = useState<QuickRowValues>(() => emptyQuickRow())
  const [errors, setErrors] = useState<RowErrors>({})
  const [rowErrors, setRowErrors] = useState<{ title: string; others: string[] } | null>(null)
  const [saved, setSaved] = useState<SavedRow[]>([])
  const [rowSeq, setRowSeq] = useState(0)
  const [saving, setSaving] = useState(false)
  // misma clave solo para reintentar el MISMO envío que no llegó a responderse (red / 5xx); un 4xx ya quedó guardado en el servidor
  const attemptRef = useRef<{ sig: string; key: string } | null>(null)
  const focusRef = useRef<QuickField | 'save' | null>(null)

  // ---- Cliente: quién asigna el número de orden ----
  const { data: client } = useClient(clientId, true)
  const orderNumberEditable = client?.numberSettings?.clientAssignsOrderNumber === true

  // ---- Predeterminados de la compañía ----
  const defaultService = useMemo(() => serviceTypes.find((s) => s.code.toLowerCase() === (settings?.defaultServiceType ?? '').toLowerCase())?.code ?? '', [serviceTypes, settings])
  const defaultPackage = useMemo(() => packageTypes.find((p) => p.code.toLowerCase() === (settings?.defaultPackageType ?? '').toLowerCase())?.code ?? '', [packageTypes, settings])
  // lo que aún no se eligió toma el predeterminado de la compañía (derivado al pintar, no copiado al estado)
  const draft = useMemo<QuickRowValues>(() => ({ ...raw, serviceType: raw.serviceType || defaultService, packageType: raw.packageType || defaultPackage }), [raw, defaultService, defaultPackage])

  // ---- Consignatario elegido en la fila activa (dirección · pueblo de solo lectura) ----
  const consignee = useSelectedConsignee(clientId, draft.consigneeId)

  // el foco se lleva al campo con error (o al primero de la fila nueva) cuando ya está pintado
  useEffect(() => {
    const field = focusRef.current
    if (!field) return
    focusRef.current = null
    document.getElementById(field === 'save' ? 'qk-save' : FIELD_IDS[field])?.focus()
  })

  const messages = useMemo(
    () => ({
      clientRequired: t('orders.entry.errors.clientRequired'),
      consigneeRequired: t('orders.entry.errors.consigneeRequired'),
      packageTypeRequired: t('orders.entry.errors.packageTypeRequired'),
      serviceRequired: t('orders.entry.errors.serviceRequired'),
      codInvalid: t('orders.quick.errors.codInvalid'),
      codNegative: t('orders.entry.errors.codNegative'),
      numberMax: t('orders.entry.errors.numberMax'),
    }),
    [t],
  )

  function change(patch: Partial<QuickRowValues>, field?: QuickField) {
    setDraft((d) => ({ ...d, ...patch }))
    if (field && errors[field]) setErrors((e) => ({ ...e, [field]: undefined }))
  }

  function pickClient(id: string | null) {
    setClientId(id)
    // lo que dependía del cliente anterior se limpia
    setDraft((d) => ({ ...d, consigneeId: '', orderNumber: '' }))
    setErrors({})
    setRowErrors(null)
    attemptRef.current = null
  }

  function firstError(errs: RowErrors): QuickField | null {
    return FIELD_ORDER.find((k) => errs[k]) ?? null
  }

  async function save() {
    if (saving) return
    const errs = validateQuickRow(clientId, draft, orderNumberEditable, messages)
    setRowErrors(null)
    if (Object.keys(errs).length > 0) {
      setErrors(errs)
      focusRef.current = firstError(errs)
      return
    }
    setErrors({})
    const body = buildQuickRequest(clientId ?? '', draft, { orderNumberEditable, today: tenantToday() })
    const sig = JSON.stringify(body)
    if (attemptRef.current?.sig !== sig) attemptRef.current = { sig, key: crypto.randomUUID() }
    setSaving(true)
    try {
      const order = await create.mutateAsync({ body, idempotencyKey: attemptRef.current.key })
      attemptRef.current = null
      const cod = quickCod(draft.cod)
      setSaved((rows) => [
        {
          key: order.id ?? rows.length + 1,
          orderNumber: order.orderNumber ?? '',
          publicId: order.publicId ?? '',
          consignee: consignee?.name ?? '',
          address: consignee ? consigneeAddress(consignee) : '',
          packageLabel: lookupLabelOrCode(draft.packageType, packageTypes) ?? draft.packageType,
          cod: cod != null && !Number.isNaN(cod) && cod > 0 ? cod : 0,
          serviceLabel: lookupLabelOrCode(draft.serviceType, serviceTypes) ?? draft.serviceType,
          notes: draft.notes.trim(),
        },
        ...rows,
      ])
      onSaved?.(order)
      setDraft((d) => nextQuickRow(d, { packageType: defaultPackage }))
      setRowSeq((n) => n + 1)
      focusRef.current = orderNumberEditable ? 'orderNumber' : 'consigneeId'
    } catch (err) {
      const p = quickProblemOf(err)
      // un 4xx ya lo respondió el servidor: el siguiente intento es otro envío; un corte de red se reintenta con la misma clave
      if (!p.retryable) attemptRef.current = null
      setErrors(p.fields)
      setRowErrors({ title: p.title, others: p.others })
      focusRef.current = firstError(p.fields) ?? 'save'
    } finally {
      setSaving(false)
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLTableRowElement>) {
    // Enter guarda la fila; si una lista abierta ya lo usó para elegir una opción (preventDefault), no. Los botones y el modal de
    // «+ consignatario» (un portal que sube por el árbol de React) no la guardan.
    if (e.key !== 'Enter' || e.defaultPrevented) return
    if (!e.currentTarget.contains(e.target as Node)) return
    if ((e.target as HTMLElement).tagName === 'BUTTON') return
    e.preventDefault()
    void save()
  }

  const totalCod = saved.reduce((sum, r) => sum + r.cod, 0)
  const serviceOptions = serviceTypes.map((s) => ({ value: s.code, label: s.label }))
  const packageOptions = packageTypes.map((p) => ({ value: p.code, label: p.label }))
  const errId = (field: QuickField) => `${FIELD_IDS[field]}-err`
  const cellError = (field: QuickField) =>
    errors[field] ? (
      <p className="ferr qk-err" id={errId(field)} role="alert">
        {errors[field]}
      </p>
    ) : null
  const aria = (field: QuickField) => ({ 'aria-invalid': errors[field] ? true : undefined, 'aria-describedby': errors[field] ? errId(field) : undefined })

  return (
    <div className="qk">
      <div className="burst">
        <div className="t qk-client">
          <small>{t('orders.quick.client')}</small>
          <div className="f">
            <ClientPicker
              id={FIELD_IDS.clientPublicId}
              aria-label={t('orders.quick.client')}
              value={clientId}
              onChange={(id) => pickClient(id)}
              invalid={!!errors.clientPublicId}
              aria-describedby={errors.clientPublicId ? errId('clientPublicId') : undefined}
            />
          </div>
          {cellError('clientPublicId')}
        </div>
        <div className="t">
          <small>{t('orders.quick.lines')}</small>
          <b data-testid="qk-lines">{saved.length}</b>
        </div>
        <div className="t cod">
          <small>{t('orders.quick.codTotal')}</small>
          <b data-testid="qk-cod">{f.money(totalCod)}</b>
        </div>
        <div className="leg">
          <span className="k">
            <kbd>Tab</kbd> {t('orders.quick.keyField')}
          </span>
          <span className="k">
            <kbd>Enter</kbd> {t('orders.quick.keySave')}
          </span>
          {labelOnSave && (
            <label className="sw qk-autolabel" title={t('orders.entry.labelOnSaveNote')}>
              <input type="checkbox" role="switch" checked={labelOnSave.value} onChange={(e) => labelOnSave.onChange(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('orders.entry.labelOnSave')}</span>
            </label>
          )}
        </div>
      </div>

      <div className="panel qk-panel">
        <table className="qg">
          <thead>
            <tr>
              <th className="qk-c-ord">{t('orders.quick.cols.orderNumber')}</th>
              <th className="qk-c-cons">{t('orders.quick.cols.consignee')}</th>
              <th className="qk-c-addr">{t('orders.quick.cols.address')}</th>
              <th className="qk-c-pkg">{t('orders.quick.cols.package')}</th>
              <th className="qk-c-cod">{t('orders.quick.cols.cod')}</th>
              <th className="qk-c-svc">{t('orders.quick.cols.service')}</th>
              <th className="qk-c-note">{t('orders.quick.cols.comment')}</th>
              <th className="qk-c-act">
                <span className="sr-only">{t('orders.quick.cols.actions')}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr key={rowSeq} className="qk-active" onKeyDown={onKeyDown}>
              <td data-label={t('orders.quick.cols.orderNumber')}>
                <input
                  id={FIELD_IDS.orderNumber}
                  className="mono"
                  aria-label={t('orders.quick.cols.orderNumber')}
                  value={draft.orderNumber}
                  maxLength={NUMBER_MAX}
                  disabled={!orderNumberEditable || saving}
                  placeholder={orderNumberEditable ? t('orders.quick.cols.orderNumber') : t('orders.entry.auto')}
                  onChange={(e) => change({ orderNumber: e.target.value }, 'orderNumber')}
                  {...aria('orderNumber')}
                />
                {cellError('orderNumber')}
              </td>
              <td data-label={t('orders.quick.cols.consignee')}>
                <ConsigneePicker
                  id={FIELD_IDS.consigneeId}
                  aria-label={t('orders.quick.cols.consignee')}
                  clearable={false}
                  clientPublicId={clientId}
                  value={draft.consigneeId}
                  onChange={(v) => change({ consigneeId: v }, 'consigneeId')}
                  invalid={!!errors.consigneeId}
                  aria-describedby={errors.consigneeId ? errId('consigneeId') : undefined}
                />
                {cellError('consigneeId')}
              </td>
              <td data-label={t('orders.quick.cols.address')}>
                <span className="qk-addr">{consignee ? consigneeAddress(consignee) || '—' : '—'}</span>
              </td>
              <td data-label={t('orders.quick.cols.package')}>
                <select
                  id={FIELD_IDS.packageType}
                  aria-label={t('orders.quick.cols.package')}
                  value={draft.packageType}
                  disabled={saving}
                  onChange={(e) => change({ packageType: e.target.value }, 'packageType')}
                  {...aria('packageType')}
                >
                  <option value="" disabled>
                    {t('orders.quick.choosePackage')}
                  </option>
                  {packageOptions.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </select>
                {cellError('packageType')}
              </td>
              <td data-label={t('orders.quick.cols.cod')}>
                <input
                  id={FIELD_IDS.cod}
                  className="mono qk-r"
                  aria-label={t('orders.quick.cols.cod')}
                  inputMode="decimal"
                  value={draft.cod}
                  disabled={saving}
                  placeholder="0.00"
                  onChange={(e) => change({ cod: e.target.value }, 'cod')}
                  {...aria('cod')}
                />
                {cellError('cod')}
              </td>
              <td data-label={t('orders.quick.cols.service')}>
                <select
                  id={FIELD_IDS.serviceType}
                  aria-label={t('orders.quick.cols.service')}
                  value={draft.serviceType}
                  disabled={saving}
                  onChange={(e) => change({ serviceType: e.target.value }, 'serviceType')}
                  {...aria('serviceType')}
                >
                  <option value="" disabled>
                    {t('orders.quick.chooseService')}
                  </option>
                  {serviceOptions.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </select>
                {cellError('serviceType')}
              </td>
              <td data-label={t('orders.quick.cols.comment')}>
                <input
                  id={FIELD_IDS.notes}
                  aria-label={t('orders.quick.cols.comment')}
                  value={draft.notes}
                  maxLength={NOTES_MAX}
                  disabled={saving}
                  placeholder={t('orders.quick.cols.comment')}
                  onChange={(e) => change({ notes: e.target.value }, 'notes')}
                  {...aria('notes')}
                />
                {cellError('notes')}
              </td>
              <td className="qk-act">
                <button id="qk-save" type="button" className="btn sm flow" disabled={saving} onClick={() => void save()}>
                  {saving ? t('orders.entry.saving') : t('orders.quick.saveRow')}
                </button>
              </td>
            </tr>
            {rowErrors && (
              <tr className="qk-rowerr">
                <td colSpan={8}>
                  <div className="form-alert" role="alert">
                    {rowErrors.title}
                    {rowErrors.others.length > 0 && (
                      <ul>
                        {rowErrors.others.map((m) => (
                          <li key={m}>{m}</li>
                        ))}
                      </ul>
                    )}
                  </div>
                </td>
              </tr>
            )}
            {saved.map((r) => (
              <tr key={r.key} className="saved">
                <td data-label={t('orders.quick.cols.orderNumber')}>
                  <Link to={`/orders/${r.publicId}`} className="ref" title={t('orders.list.open')}>
                    {r.orderNumber}
                  </Link>
                </td>
                <td data-label={t('orders.quick.cols.consignee')}>{r.consignee || '—'}</td>
                <td data-label={t('orders.quick.cols.address')}>
                  <span className="qk-addr">{r.address || '—'}</span>
                </td>
                <td data-label={t('orders.quick.cols.package')}>{r.packageLabel}</td>
                <td data-label={t('orders.quick.cols.cod')} className="qk-r mono">
                  {r.cod > 0 ? f.money(r.cod) : '—'}
                </td>
                <td data-label={t('orders.quick.cols.service')}>{r.serviceLabel}</td>
                <td data-label={t('orders.quick.cols.comment')}>{r.notes || '—'}</td>
                <td className="qk-act">
                  <span className="chip s-deliv">{t('orders.quick.saved')}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="qk-foot" role="status">
          {t('orders.quick.captured', { count: saved.length })}
        </div>
      </div>
      <p className="help qk-help">{t('orders.quick.help')}</p>
    </div>
  )
}
