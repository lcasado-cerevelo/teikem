// Captura RÁPIDA de envíos (2026-10-23, dictado de Luis: igual que «Entrada rápida» de la web del personal y TODO con el teclado, nada de ratón): la rejilla de la web ancha donde cada
// fila es un envío de 1 paquete y 1 pieza. Columnas (SIEMPRE las mismas que en el personal): Fecha, N.º de envío y N.º de factura (se escriben solo si el cliente asigna sus números; si no, salen
// apagados con «(automático)», como en el personal), Consignatario (autocompletar), Dirección · Pueblo,
// Tipo de paquete, COD, Servicio y Comentario. Tab recorre los campos; Enter guarda desde cualquier campo (en el consignatario con la lista abierta, Enter escoge); la fila guardada
// queda de solo lectura debajo con su número, y la nueva trae la fecha, el servicio y el tipo de paquete de la anterior con el foco en la fecha. Los errores salen en la fila sin perder lo escrito.
// Consignatario que no existe: «+ Nuevo consignatario» abre debajo de la fila sus campos (nombre, dirección, pueblo, estado, código postal y teléfono); Enter lo GRABA en el servidor al momento,
// lo deja escogido y el foco sigue en el Paquete de la misma fila; Enter en cualquier otro campo con el bloque abierto lo graba y guarda también el envío. Sin ratón en ningún paso.
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Pressable, Text, TextInput, View, type TextInputProps } from 'react-native'

import { ApiError, apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { DateInput } from '../../kernel/ui/DateInput'
import { Select } from '../../kernel/ui/Select'
import { Button, Chip, Notice, useColors } from '../../kernel/ui/ui'
import { formatDate, formatMoney } from '../format'
import { createConsignee, createOrder, type PortalOrderFormDto, type PortalPlaceDto } from '../portalApi'
import { ConsigneeCombo, type ConsigneeValue, placeText } from './ConsigneeCombo'
import { missingConsigneeFields, NewConsigneeInline, type NewConsigneeHandle } from './NewConsigneeInline'
import { type ErrorKey, MAX_NOTES, NUMBER_MAX, rulesOf, serverFieldKey, todayText } from './newOrderRules'
import { emptyQuickRow, FIELD_OF_ERROR, type DayRange, filterSavedEntries, firstErrorField, nextQuickRow, QUICK_FIELDS, type QuickField, type QuickRow, quickRequest, setRangeEdge, summarizeSaved, validateQuickRow, widenRange } from './quickRules'

interface SavedRow {
  key: number
  publicId: string
  waybill: string
  orderNumber: string
  invoice: string
  date: string
  consignee: string
  address: string
  packageLabel: string
  cod: number
  serviceLabel: string
  notes: string
}

// anchos fijos y mínimos de los flexibles: suman ~1150 px, así que la fila cabe completa desde ventanas de ~1230 px (con las columnas que tiene también el personal)
const W = { date: 132, orderNumber: 108, invoice: 122, packageType: 124, cod: 84, serviceType: 124, action: 126 } as const
type Focusable = { focus: () => void } | null

export function NewOrderQuick({ form, onOpen, onConsigneeCreated }: { form: PortalOrderFormDto; onOpen?: (publicId: string) => void; onConsigneeCreated?: (place: PortalPlaceDto) => void }) {
  const colors = useColors()
  const { t, lang } = useT()
  const rules = useMemo(() => rulesOf(form), [form])
  const consignees: PortalPlaceDto[] = form.consignees ?? []
  const services = (form.serviceTypes ?? []).map((s) => ({ value: s.code ?? '', label: s.label ?? s.code ?? '' }))
  const packageTypes = (form.packageTypes ?? []).map((s) => ({ value: s.code ?? '', label: s.label ?? s.code ?? '' }))
  const [row, setRow] = useState<QuickRow>(() => emptyQuickRow(todayText(), { serviceType: rules.defaultServiceType, packageType: rules.defaultPackageType }))
  const [pick, setPick] = useState<ConsigneeValue>({ consigneeId: '', query: '', newConsignee: null })
  const [saved, setSaved] = useState<SavedRow[]>([])
  // la tabla de lo capturado se filtra por UN día (hoy al abrir) y por una búsqueda libre; un envío con otra fecha se guarda pero no sale ahí
  const [savedRange, setSavedRange] = useState<DayRange>(() => ({ from: todayText(), to: todayText() }))
  const [savedQ, setSavedQ] = useState('')
  const [errors, setErrors] = useState<ErrorKey[]>([])
  const [message, setMessage] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [blockBusy, setBlockBusy] = useState(false)
  const [blockError, setBlockError] = useState<string | null>(null)
  const refs = useRef<Partial<Record<QuickField, Focusable>>>({})
  const blockRef = useRef<NewConsigneeHandle>(null)
  const counter = useRef(0)
  const today = todayText()

  const fields = QUICK_FIELDS.filter((f) => (f !== 'orderNumber' || rules.assignsOrderNumber) && (f !== 'invoice' || rules.typesInvoice) && (f !== 'packageType' || packageTypes.length > 0) && (f !== 'serviceType' || services.length > 0))
  const focus = (f: QuickField | null) => f && setTimeout(() => refs.current[f]?.focus(), 0)
  // mientras se guarda, los campos están deshabilitados y no aceptan el foco: lo pedido durante el guardado se da cuando termina
  const pendingFocus = useRef<QuickField | null>(null)
  const focusWhenIdle = (f: QuickField | null) => { pendingFocus.current = f }
  useEffect(() => {
    if (saving || !pendingFocus.current) return
    const f = pendingFocus.current
    pendingFocus.current = null
    setTimeout(() => refs.current[f]?.focus(), 0)
  }, [saving])
  const focusAfter = (f: QuickField) => focus(fields[fields.indexOf(f) + 1] ?? null)
  const set = (patch: Partial<QuickRow>) => setRow((r) => ({ ...r, ...patch }))
  const onPick = (v: ConsigneeValue) => {
    setPick(v)
    setBlockError(null)
    set({ consigneeQuery: v.query, consigneeId: v.consigneeId, newConsignee: v.newConsignee })
  }
  const selected = consignees.find((c) => c.publicId === row.consigneeId) ?? null

  /** Graba el consignatario nuevo en el servidor (al momento), lo deja escogido y avisa a la pantalla para que lo sume a su lista. null si falta algo o el servidor lo rechazó (el error sale en el bloque). */
  async function confirmConsignee(): Promise<PortalPlaceDto | null> {
    const n = row.newConsignee
    if (!n) return null
    if (missingConsigneeFields(n).length > 0) {
      blockRef.current?.focusFirstMissing()
      return null
    }
    setBlockBusy(true)
    setBlockError(null)
    try {
      const place = await createConsignee({ name: n.name.trim(), line1: n.line1.trim(), line2: n.line2.trim() || null, city: n.city.trim(), state: n.state.trim() || null, postalCode: n.postalCode.trim() || null, phone: n.phone.trim() || null })
      onConsigneeCreated?.(place)
      onPick({ consigneeId: place.publicId as string, query: place.name ?? n.name, newConsignee: null })
      return place
    } catch (e) {
      setBlockError(apiErrorMessage(e))
      return null
    } finally {
      setBlockBusy(false)
    }
  }

  async function save() {
    if (saving || blockBusy) return
    // con el bloque del consignatario nuevo abierto: se graba primero (y si falta algo o falla, ahí queda el foco)
    let current = row
    let created: PortalPlaceDto | null = null
    if (row.newConsignee) {
      setSaving(true)
      created = await confirmConsignee()
      setSaving(false)
      if (!created) return
      current = { ...row, consigneeId: created.publicId as string, consigneeQuery: created.name ?? '', newConsignee: null }
    }
    const errs = validateQuickRow(current, rules)
    setErrors(errs)
    setMessage(errs.length > 0 ? errs.map((k) => t(`newOrder.errors.${k}`, { max: k === 'notes' ? MAX_NOTES : NUMBER_MAX })).join(' ') : null)
    if (errs.length > 0) return focus(firstErrorField(errs))
    setSaving(true)
    try {
      const order = await createOrder(quickRequest(current, rules))
      const place = created ?? selected
      const cod = Number(current.cod.replace(',', '.')) || 0
      setSavedRange((r) => widenRange(r, current.date))   // lo que se captura siempre se ve: el rango se ensancha a la fecha más vieja o más reciente
      setSaved((s) => [{
        key: ++counter.current, publicId: order.publicId as string, waybill: order.waybillNumber || '', orderNumber: order.orderNumber || '', invoice: (order.clientInvoiceNumber || current.invoice).trim(), date: current.date, consignee: place?.name ?? current.consigneeQuery,
        address: place ? placeText(place) : '', packageLabel: packageTypes.find((p) => p.value === current.packageType)?.label ?? current.packageType, cod, serviceLabel: services.find((p) => p.value === current.serviceType)?.label ?? current.serviceType, notes: current.notes.trim(),
      }, ...s])
      setRow((r) => nextQuickRow(r))
      setPick({ consigneeId: '', query: '', newConsignee: null })
      setErrors([])
      focusWhenIdle('date')
    } catch (e) {
      setMessage(apiErrorMessage(e))
      if (e instanceof ApiError) {
        const keys = Object.keys(e.errors ?? {}).map(serverFieldKey).filter((k): k is ErrorKey => k !== null)
        setErrors(keys)
        focusWhenIdle(firstErrorField(keys))
      }
    } finally {
      setSaving(false)
    }
  }

  const bad = (f: QuickField) => errors.some((k) => FIELD_OF_ERROR[k] === f)
  const input = (f: QuickField, value: string, onChange: (v: string) => void, extra: TextInputProps = {}) => (
    <TextInput
      ref={(el) => { refs.current[f] = el }}
      accessibilityLabel={t(`newOrder.quick.col.${f}`)}
      testID={`quick-${f}`}
      value={value}
      onChangeText={onChange}
      onSubmitEditing={() => void save()}
      blurOnSubmit={false}
      editable={!saving}
      placeholderTextColor={colors.muted}
      style={{ minHeight: 40, borderRadius: 8, borderWidth: 1, borderColor: bad(f) ? colors.danger : colors.line, backgroundColor: extra.editable === false ? colors.neutralBg : colors.card, color: colors.text, paddingHorizontal: 8, fontSize: 14, opacity: extra.editable === false ? 0.7 : 1 }}
      {...extra}
    />
  )
  const col = (child: ReactNode, width?: number, flex = 1, minWidth = 120) => <View style={{ width, flex: width ? undefined : flex, minWidth: width ? undefined : minWidth, paddingHorizontal: 4 }}>{child}</View>
  const head = (label: string, width?: number, flex = 1, minWidth = 120) => col(<Text numberOfLines={1} style={{ fontSize: 12, fontWeight: '700', letterSpacing: 0.4, textTransform: 'uppercase', color: colors.muted }}>{label}</Text>, width, flex, minWidth)
  const text = (v: string, bold?: boolean, muted?: boolean) => <Text numberOfLines={1} style={{ fontSize: 14, fontWeight: bold ? '700' : '400', color: muted ? colors.muted : colors.text }}>{v || '—'}</Text>
  const kbd = (key: string, what: string) => (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 4 }}>
      <Text style={{ fontSize: 12, fontWeight: '700', color: colors.text, borderWidth: 1, borderColor: colors.line, borderRadius: 4, paddingHorizontal: 5, backgroundColor: colors.card }}>{key}</Text>
      <Text style={{ fontSize: 12, color: colors.muted }}>{what}</Text>
    </View>
  )

  const codTotal = saved.reduce((n, s) => n + s.cod, 0)
  const visibleSaved = filterSavedEntries(saved, savedRange, savedQ, (s) => [s.waybill, s.orderNumber, s.invoice, s.consignee, s.address, s.packageLabel, s.serviceLabel, s.cod > 0 ? s.cod : null, s.cod > 0 ? formatMoney(s.cod, lang) : null, s.notes, formatDate(s.date, lang)])
  const summary = summarizeSaved(visibleSaved)
  const nc = row.newConsignee
  const offDate = row.date !== today

  return (
    <View style={{ gap: 10 }} testID="new-order-quick">
      <View style={{ flexDirection: 'row', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
        <Chip label={t('newOrder.quick.savedCount', { n: saved.length })} tone="info" />
        {codTotal > 0 && <Chip label={t('newOrder.quick.codTotal', { amount: formatMoney(codTotal, lang) })} tone="neutral" />}
        {kbd('Tab', t('newOrder.quick.keyField'))}
        {kbd('Enter', t('newOrder.quick.keySave'))}
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
          <View style={{ width: 14, height: 10, borderRadius: 3, backgroundColor: colors.warnBg, borderWidth: 1, borderColor: colors.warn }} />
          <Text style={{ fontSize: 12, color: colors.muted }}>{t('newOrder.quick.offDateLegend')}</Text>
        </View>
        <Text style={{ color: colors.muted, fontSize: 13, flexShrink: 1 }}>{t('newOrder.quick.hint')}</Text>
      </View>

      <View style={{ backgroundColor: colors.card, borderRadius: 12, borderWidth: 1, borderColor: colors.line, padding: 10, gap: 8 }}>
        <View style={{ flexDirection: 'row' }}>
          {head(t('newOrder.quick.col.date'), W.date)}
          {head(t('newOrder.quick.col.orderNumber'), W.orderNumber)}
          {head(t('newOrder.quick.col.invoice'), W.invoice)}
          {head(t('newOrder.quick.col.consignee'), undefined, 2, 170)}
          {head(t('newOrder.quick.col.address'), undefined, 1.5, 110)}
          {packageTypes.length > 0 && head(t('newOrder.quick.col.packageType'), W.packageType)}
          {head(t('newOrder.quick.col.cod'), W.cod)}
          {services.length > 0 && head(t('newOrder.quick.col.serviceType'), W.serviceType)}
          {head(t('newOrder.quick.col.notes'), undefined, 1, 90)}
          {head('', W.action)}
        </View>

        <View style={{ flexDirection: 'row', alignItems: 'flex-start', zIndex: 10, backgroundColor: offDate ? colors.warnBg : 'transparent', borderRadius: 8, paddingVertical: 2 }} testID="quick-row">
          {col(<DateInput value={row.date} min={today} onChange={(v) => set({ date: v })} accessibilityLabel={t('newOrder.quick.col.date')} compact invalid={bad('date')} disabled={saving} onEnter={() => void save()} inputRef={(el) => { refs.current.date = el }} testID="quick-date" />, W.date)}
          {col(input('orderNumber', row.orderNumber, (v) => set({ orderNumber: v }), { autoCapitalize: 'characters', maxLength: NUMBER_MAX, editable: rules.assignsOrderNumber && !saving, focusable: rules.assignsOrderNumber, placeholder: rules.assignsOrderNumber ? t('newOrder.quick.col.orderNumber') : t('newOrder.auto') }), W.orderNumber)}
          {col(input('invoice', row.invoice, (v) => set({ invoice: v }), { autoCapitalize: 'characters', maxLength: NUMBER_MAX, editable: rules.typesInvoice && !saving, focusable: rules.typesInvoice, placeholder: rules.typesInvoice ? t('newOrder.quick.col.invoice') : t('newOrder.auto') }), W.invoice)}
          {col(<ConsigneeCombo consignees={consignees} value={pick} onChange={onPick} onPicked={() => focusAfter('consignee')} onEnter={() => void save()} compact invalid={bad('consignee')} disabled={saving} inputRef={(el) => { refs.current.consignee = el }} testID="quick-consignee" />, undefined, 2, 170)}
          {col(<View style={{ minHeight: 40, justifyContent: 'center' }}><Text numberOfLines={2} style={{ fontSize: 13, color: colors.muted }} testID="quick-address">{nc ? [nc.line1, nc.city].filter(Boolean).join(', ') || '—' : selected ? placeText(selected) || '—' : '—'}</Text></View>, undefined, 1.5, 110)}
          {packageTypes.length > 0 && col(<Select value={row.packageType} options={packageTypes} onChange={(v) => set({ packageType: v })} placeholder={t('newOrder.choosePackageType')} accessibilityLabel={t('newOrder.quick.col.packageType')} compact invalid={bad('packageType')} disabled={saving} onEnter={() => void save()} inputRef={(el) => { refs.current.packageType = el }} testID="quick-packageType" />, W.packageType)}
          {col(input('cod', row.cod, (v) => set({ cod: v }), { keyboardType: 'decimal-pad', placeholder: '0.00' }), W.cod)}
          {services.length > 0 && col(<Select value={row.serviceType} options={services} onChange={(v) => set({ serviceType: v })} placeholder={t('newOrder.chooseService')} accessibilityLabel={t('newOrder.quick.col.serviceType')} compact invalid={bad('serviceType')} disabled={saving} onEnter={() => void save()} inputRef={(el) => { refs.current.serviceType = el }} testID="quick-serviceType" />, W.serviceType)}
          {col(input('notes', row.notes, (v) => set({ notes: v }), { maxLength: MAX_NOTES + 50 }), undefined, 1, 90)}
          {col(<Button title={t('newOrder.quick.saveRow')} onPress={() => void save()} busy={saving} testID="quick-save" />, W.action)}
        </View>

        {!!nc && (
          <NewConsigneeInline
            ref={blockRef}
            value={nc}
            onChange={(v) => { setPick({ ...pick, newConsignee: v }); set({ newConsignee: v }) }}
            onConfirm={() => void confirmConsignee().then((p) => { if (p) focusAfter('consignee') })}
            onCancel={() => { onPick({ consigneeId: '', query: nc.name, newConsignee: null }); focus('consignee') }}
            busy={blockBusy}
            error={blockError}
            testPrefix="quick-new"
            testID="quick-new-block"
          />
        )}

        {!!message && <Notice>{message}</Notice>}

        {saved.length > 0 && (
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12, flexWrap: 'wrap', paddingTop: 6, borderTopWidth: 1, borderTopColor: colors.line }} testID="quick-filters">
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
              <Text style={{ fontSize: 12.5, color: colors.muted }}>{t('newOrder.quick.filters.from')}</Text>
              <View style={{ width: 150 }}>
                <DateInput value={savedRange.from} onChange={(v) => setSavedRange((r) => setRangeEdge(r, 'from', v))} accessibilityLabel={t('newOrder.quick.filters.from')} compact testID="quick-saved-from" />
              </View>
            </View>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
              <Text style={{ fontSize: 12.5, color: colors.muted }}>{t('newOrder.quick.filters.to')}</Text>
              <View style={{ width: 150 }}>
                <DateInput value={savedRange.to} onChange={(v) => setSavedRange((r) => setRangeEdge(r, 'to', v))} accessibilityLabel={t('newOrder.quick.filters.to')} compact testID="quick-saved-to" />
              </View>
            </View>
            <TextInput
              accessibilityLabel={t('newOrder.quick.filters.searchLabel')}
              testID="quick-saved-search"
              value={savedQ}
              onChangeText={setSavedQ}
              placeholder={t('newOrder.quick.filters.search')}
              placeholderTextColor={colors.muted}
              autoCapitalize="none"
              style={{ flexGrow: 1, flexBasis: 220, maxWidth: 420, minHeight: 40, borderRadius: 8, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.card, color: colors.text, paddingHorizontal: 10, fontSize: 14 }}
            />
            <Text style={{ marginLeft: 'auto', fontSize: 12.5, color: colors.muted }} testID="quick-shown">{t('newOrder.quick.filters.shown', { shown: visibleSaved.length, total: saved.length })}</Text>
          </View>
        )}
        {saved.length > 0 && visibleSaved.length === 0 && <Text style={{ fontSize: 13, color: colors.muted, paddingVertical: 8 }} testID="quick-none">{t('newOrder.quick.filters.none')}</Text>}
        {visibleSaved.map((s) => (
          <Pressable key={s.key} testID={`quick-saved-${s.key}`} accessibilityRole="link" onPress={() => onOpen?.(s.publicId)} style={{ flexDirection: 'row', alignItems: 'center', minHeight: 36, backgroundColor: s.date !== today ? colors.warnBg : colors.okBg, borderRadius: 8 }}>
            {col(text(formatDate(s.date, lang), false, true), W.date)}
            {col(text(s.orderNumber, true), W.orderNumber)}
            {col(text(s.invoice), W.invoice)}
            {col(text([s.waybill, s.consignee].filter(Boolean).join(' · '), true), undefined, 2, 170)}
            {col(text(s.address, false, true), undefined, 1.5, 110)}
            {packageTypes.length > 0 && col(text(s.packageLabel), W.packageType)}
            {col(text(s.cod ? formatMoney(s.cod, lang) : '—', false, true), W.cod)}
            {services.length > 0 && col(text(s.serviceLabel), W.serviceType)}
            {col(text(s.notes, false, true), undefined, 1, 90)}
            {col(<Text style={{ color: colors.ok, fontWeight: '700' }}>✓ {t('newOrder.quick.saved')}</Text>, W.action)}
          </Pressable>
        ))}
        {saved.length === 0 ? (
          <Text style={{ fontSize: 12, color: colors.muted }}>{t('newOrder.quick.captured', { n: 0 })}</Text>
        ) : (
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }} testID="quick-summary">
            <Text style={{ fontSize: 13, fontWeight: '700', color: colors.text }}>{t('newOrder.quick.summary.title')}</Text>
            {summary.lines.length === 0 ? (
              <Text style={{ fontSize: 13, color: colors.muted }}>{t('newOrder.quick.summary.none')}</Text>
            ) : (
              summary.lines.map((l) => <Chip key={`${l.packageLabel}|${l.serviceLabel}`} label={t('newOrder.quick.summary.line', { count: l.count, package: l.packageLabel, service: l.serviceLabel })} tone="neutral" />)
            )}
            <Text style={{ fontSize: 13, color: colors.text }} testID="quick-summary-packages">{t('newOrder.quick.summary.packages', { n: summary.total })}</Text>
            <Text style={{ fontSize: 13, color: colors.text }} testID="quick-summary-invoices">{t('newOrder.quick.summary.invoices', { n: summary.invoices })}</Text>
            <Text style={{ fontSize: 13, fontWeight: '700', color: colors.text }} testID="quick-summary-cod">{t('newOrder.quick.summary.cod', { amount: formatMoney(summary.cod, lang) })}</Text>
          </View>
        )}
      </View>
      <Text style={{ fontSize: 12, color: colors.muted }}>{t('newOrder.quick.help')}</Text>
    </View>
  )
}
