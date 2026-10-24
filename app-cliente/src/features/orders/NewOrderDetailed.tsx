// Entrada DETALLADA de un envío (2026-10-23, dictado de Luis: los mismos campos y la misma cara que «Nuevo envío» de la web del personal): paso 01 Servicio y fechas, 02 Consignatario
// (buscador con autocompletar y sus datos de solo lectura; «+ Nuevo» abre los campos), 03 Paquetes y COD, notas, y el «Ticket» con el resumen (sin precio) a la derecha en la web
// ancha o al final en el celular. Un solo «Guardar»: la orden nace Sometida. El cliente sale de la sesión. Es la ÚNICA forma de capturar en el celular (la rápida es de la web ancha).
// Consignatario que no existe: «+ Nuevo» abre ahí mismo sus campos; Enter lo graba en el servidor al momento, lo deja escogido y el foco sigue en el primer tipo de paquete (todo con el teclado).
import { useRouter } from 'expo-router'
import { useMemo, useRef, useState, type ReactNode } from 'react'
import { Text, View } from 'react-native'

import { ApiError, apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { DateInput } from '../../kernel/ui/DateInput'
import { useIsWide } from '../../kernel/ui/layout'
import { Select } from '../../kernel/ui/Select'
import { Button, Card, Field, Muted, Notice, useColors } from '../../kernel/ui/ui'
import { formatMoney } from '../format'
import { createConsignee, createOrder, type PortalOrderFormDto, type PortalPlaceDto } from '../portalApi'
import { ConsigneeCombo, type ConsigneeValue, emptyConsigneeValue, placeText } from './ConsigneeCombo'
import { missingConsigneeFields, NewConsigneeInline, type NewConsigneeHandle } from './NewConsigneeInline'
import { buildRequest, emptyDraft, emptyPackage, type ErrorKey, MAX_NOTES, MAX_PACKAGES, NUMBER_MAX, type OrderDraft, packageSummary, parseNumber, rulesOf, serverFieldKey, todayText, validateDraft } from './newOrderRules'

/** Rótulo + control (para los desplegables y las fechas, que no son un `Field`). */
function Labeled({ label, children, flex = 1, error, hint }: { label: string; children: ReactNode; flex?: number; error?: string | null; hint?: string }) {
  const c = useColors()
  return (
    <View style={{ gap: 4, flex, minWidth: 150 }}>
      <Text style={{ fontSize: 13, fontWeight: '600', color: c.text }}>{label}</Text>
      {children}
      {!!hint && !error && <Text style={{ fontSize: 12, color: c.muted }}>{hint}</Text>}
      {!!error && <Text style={{ fontSize: 12, color: c.danger }}>{error}</Text>}
    </View>
  )
}

/** Dato del consignatario elegido, de solo lectura (como la web del personal). */
function ReadOnly({ label, value, flex = 1 }: { label: string; value: string; flex?: number }) {
  const c = useColors()
  return (
    <View style={{ gap: 4, flex, minWidth: 120 }}>
      <Text style={{ fontSize: 13, fontWeight: '600', color: c.muted }}>{label}</Text>
      <View style={{ minHeight: 44, borderRadius: 10, borderWidth: 1, borderColor: c.line, backgroundColor: c.neutralBg, paddingHorizontal: 12, justifyContent: 'center' }}>
        <Text style={{ fontSize: 15, color: value ? c.text : c.muted }}>{value || '—'}</Text>
      </View>
    </View>
  )
}

function Section({ num, title, desc, children }: { num: string; title: string; desc: string; children: ReactNode }) {
  const c = useColors()
  return (
    <Card style={{ gap: 10 }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: 10 }}>
        <View style={{ width: 30, height: 30, borderRadius: 15, backgroundColor: c.brand, alignItems: 'center', justifyContent: 'center' }}>
          <Text style={{ color: c.onBrand, fontWeight: '800', fontSize: 12 }}>{num}</Text>
        </View>
        <Text style={{ fontWeight: '800', fontSize: 16, color: c.text }}>{title}</Text>
      </View>
      <Muted>{desc}</Muted>
      {children}
    </Card>
  )
}

export function NewOrderDetailed({ form, onCreated, onConsigneeCreated }: { form: PortalOrderFormDto; onCreated?: (publicId: string) => void; onConsigneeCreated?: (place: PortalPlaceDto) => void }) {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const wide = useIsWide()
  const rules = useMemo(() => rulesOf(form), [form])
  const consignees: PortalPlaceDto[] = form.consignees ?? []
  const [draft, setDraft] = useState<OrderDraft>(() => emptyDraft({ serviceType: rules.defaultServiceType, packageType: rules.defaultPackageType }))
  const [pick, setPick] = useState<ConsigneeValue>(emptyConsigneeValue)
  const [shown, setShown] = useState<ErrorKey[]>([])
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [blockBusy, setBlockBusy] = useState(false)
  const [blockError, setBlockError] = useState<string | null>(null)
  const blockRef = useRef<NewConsigneeHandle>(null)
  const consigneeRef = useRef<{ focus: () => void } | null>(null)
  const firstPackageRef = useRef<{ focus: () => void } | null>(null)

  const services = (form.serviceTypes ?? []).map((s) => ({ value: s.code ?? '', label: s.label ?? s.code ?? '' }))
  const packageTypes = (form.packageTypes ?? []).map((s) => ({ value: s.code ?? '', label: s.label ?? s.code ?? '' }))
  const labelOf = (code: string) => packageTypes.find((p) => p.value === code)?.label ?? ''
  const serviceLabel = services.find((s) => s.value === draft.serviceType)?.label ?? '—'
  const selected = consignees.find((c) => c.publicId === draft.consigneeId) ?? null
  const usingNew = draft.consigneeId === null && draft.newConsignee !== null
  const today = todayText()

  const set = (patch: Partial<OrderDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const err = (k: ErrorKey) => (shown.includes(k) ? t(`newOrder.errors.${k}`, { max: k === 'packages' ? MAX_PACKAGES : k === 'notes' ? MAX_NOTES : NUMBER_MAX }) : null)
  const setPackage = (i: number, patch: Partial<OrderDraft['packages'][number]>) => set({ packages: draft.packages.map((p, j) => (j === i ? { ...p, ...patch } : p)) })
  const onPick = (v: ConsigneeValue) => {
    setPick(v)
    setBlockError(null)
    set({ consigneeId: v.consigneeId || null, newConsignee: v.newConsignee })
  }
  /** El foco sigue en lo que viene después del consignatario: el primer tipo de paquete (o el combo si no hay catálogo de paquetes). */
  const focusNext = () => setTimeout(() => (firstPackageRef.current ?? consigneeRef.current)?.focus(), 0)

  /** Graba el consignatario nuevo en el servidor (al momento) y lo deja escogido; null si falta algo o el servidor lo rechazó (el error sale en el bloque). */
  async function confirmConsignee(): Promise<PortalPlaceDto | null> {
    const n = draft.newConsignee
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

  async function submit() {
    setError(null)
    // con el bloque del consignatario nuevo abierto: se graba primero (y si falta algo o falla, ahí queda el foco)
    let current = draft
    if (draft.newConsignee) {
      setSending(true)
      const place = await confirmConsignee()
      setSending(false)
      if (!place) return
      current = { ...draft, consigneeId: place.publicId as string, newConsignee: null }
    }
    const errors = validateDraft(current, rules)
    setShown(errors)
    if (errors.length > 0) return
    setSending(true)
    try {
      const created = await createOrder(buildRequest(current, rules))
      const id = created.publicId as string
      if (onCreated) onCreated(id)
      else router.replace({ pathname: '/order/[id]', params: { id } })
    } catch (e) {
      setError(apiErrorMessage(e))
      if (e instanceof ApiError) setShown(Object.keys(e.errors ?? {}).map(serverFieldKey).filter((k): k is ErrorKey => k !== null))
    } finally {
      setSending(false)
    }
  }

  const cod = parseNumber(draft.codAmount)
  const ticket = (
    <Card style={{ gap: 6, borderStyle: 'dashed' }} >
      <Text style={{ fontWeight: '800', color: colors.text }}>{t('newOrder.ticket')}</Text>
      {([
        [t('newOrder.service'), serviceLabel],
        [t('newOrder.ticketConsignee'), usingNew ? draft.newConsignee?.name || '—' : selected?.name ?? '—'],
        [t('newOrder.town'), usingNew ? draft.newConsignee?.city || '—' : selected?.city ?? '—'],
        [t('newOrder.ticketPackages'), packageSummary(draft.packages, labelOf) || '—'],
      ] as const).map(([k, v]) => (
        <View key={k} style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 12 }}>
          <Muted>{k}</Muted>
          <Text style={{ color: colors.text, fontWeight: '700', flexShrink: 1, textAlign: 'right' }}>{v}</Text>
        </View>
      ))}
      <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: 12, borderTopWidth: 1, borderTopColor: colors.line, paddingTop: 6 }}>
        <Text style={{ color: colors.text, fontWeight: '700' }}>{t('newOrder.ticketCod')}</Text>
        <Text style={{ color: colors.text, fontWeight: '800', fontSize: 18 }} testID="ticket-cod">{formatMoney(cod && !Number.isNaN(cod) && cod > 0 ? cod : 0, lang)}</Text>
      </View>
      <Muted>{t('newOrder.ticketNote')}</Muted>
      <Button title={t('newOrder.save')} onPress={() => void submit()} busy={sending} testID="neworder-submit" />
      <Button title={t('common.cancel')} variant="secondary" onPress={() => router.back()} />
    </Card>
  )

  return (
    <View style={{ flexDirection: wide ? 'row' : 'column', gap: 16, alignItems: 'flex-start' }} testID="new-order-detailed">
      <View style={{ flex: 1, gap: 12, width: '100%', minWidth: 0 }}>
        <Section num="01" title={t('newOrder.s1')} desc={t('newOrder.s1d')}>
          <View style={{ flexDirection: 'row', gap: 10, flexWrap: 'wrap' }}>
            {services.length > 0 && (
              <Labeled label={t('newOrder.service')} flex={2} error={err('service')}>
                <Select value={draft.serviceType} options={services} onChange={(v) => set({ serviceType: v })} placeholder={t('newOrder.chooseService')} accessibilityLabel={t('newOrder.service')} invalid={!!err('service')} testID="order-service" />
              </Labeled>
            )}
            <Labeled label={t('newOrder.readyDate')} error={err('date')}>
              <DateInput value={draft.readyDate} min={today} onChange={(v) => set({ readyDate: v })} accessibilityLabel={t('newOrder.readyDate')} invalid={!!err('date')} testID="order-date" />
            </Labeled>
            <Labeled label={t('newOrder.dueDate')} error={err('dueDate')}>
              <DateInput value={draft.dueDate} min={draft.readyDate || today} onChange={(v) => set({ dueDate: v })} accessibilityLabel={t('newOrder.dueDate')} invalid={!!err('dueDate')} testID="order-due" />
            </Labeled>
          </View>
          <View style={{ flexDirection: 'row', gap: 10, flexWrap: 'wrap' }}>
            <View style={{ flex: 1, minWidth: 150 }}>
              {rules.assignsOrderNumber ? (
                <Field label={t('newOrder.orderNumber')} value={draft.orderNumber} onChangeText={(v) => set({ orderNumber: v })} autoCapitalize="characters" maxLength={NUMBER_MAX} error={err('orderNumber')} testID="order-number" />
              ) : (
                <ReadOnly label={`${t('newOrder.orderNumber')} ${t('newOrder.auto')}`} value={t('newOrder.autoPlaceholder')} />
              )}
            </View>
            <View style={{ flex: 1, minWidth: 150 }}>
              {rules.typesInvoice ? (
                <Field label={t('newOrder.invoice')} value={draft.invoiceNumber} onChangeText={(v) => set({ invoiceNumber: v })} autoCapitalize="characters" maxLength={NUMBER_MAX} error={err('invoice')} testID="order-invoice" />
              ) : (
                <ReadOnly label={`${t('newOrder.invoice')} ${t('newOrder.auto')}`} value={t('newOrder.autoPlaceholder')} />
              )}
            </View>
          </View>
        </Section>

        <Section num="02" title={t('newOrder.s2')} desc={t('newOrder.s2d')}>
          <View style={{ flexDirection: 'row', gap: 10, flexWrap: 'wrap' }}>
            <Labeled label={t('newOrder.consignee')} flex={2} error={err('consignee')} hint={t('newOrder.consigneeHelp')}>
              <ConsigneeCombo consignees={consignees} value={pick} onChange={onPick} onPicked={focusNext} invalid={!!err('consignee')} inputRef={(el) => { consigneeRef.current = el }} testID="consignee" />
            </Labeled>
            {!usingNew && <ReadOnly label={t('newOrder.consigneeNumber')} value={selected?.code ?? ''} />}
          </View>
          {usingNew ? (
            <NewConsigneeInline
              ref={blockRef}
              value={draft.newConsignee!}
              onChange={(v) => onPick({ ...pick, newConsignee: v })}
              onConfirm={() => void confirmConsignee().then((p) => { if (p) focusNext() })}
              onCancel={() => { onPick({ consigneeId: '', query: draft.newConsignee?.name ?? '', newConsignee: null }); setTimeout(() => consigneeRef.current?.focus(), 0) }}
              busy={blockBusy}
              error={blockError}
              testPrefix="new"
              testID="new-consignee-block"
            />
          ) : (
            <>
              <ReadOnly label={t('newOrder.address')} value={selected ? [selected.line1, selected.line2].filter(Boolean).join(', ') : ''} />
              <View style={{ flexDirection: 'row', gap: 10, flexWrap: 'wrap' }}>
                <ReadOnly label={t('newOrder.town')} value={selected?.city ?? ''} />
                <ReadOnly label={t('newOrder.zip')} value={selected?.postalCode ?? ''} />
                <ReadOnly label={t('newOrder.phone')} value={selected?.phone ?? ''} />
              </View>
            </>
          )}
          {(form.pickups ?? []).length > 0 && (
            <Labeled label={t('newOrder.pickup')}>
              <Select
                value={draft.pickupId ?? ''}
                options={[{ value: '', label: t('newOrder.pickupDefault') }, ...(form.pickups ?? []).map((p) => ({ value: p.publicId as string, label: `${p.name ?? ''} · ${placeText(p)}` }))]}
                onChange={(v) => set({ pickupId: v || null })}
                accessibilityLabel={t('newOrder.pickup')}
                testID="order-pickup"
              />
            </Labeled>
          )}
        </Section>

        <Section num="03" title={t('newOrder.s3')} desc={t('newOrder.s3d')}>
          {draft.packages.map((p, i) => (
            <View key={i} style={{ flexDirection: 'row', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end', paddingBottom: 8, borderBottomWidth: draft.packages.length > 1 ? 1 : 0, borderBottomColor: colors.line }}>
              {packageTypes.length > 0 && (
                <Labeled label={t('newOrder.packageType')} flex={2} error={i === 0 ? err('packageType') : null}>
                  <Select value={p.packageType} options={packageTypes} onChange={(v) => setPackage(i, { packageType: v })} placeholder={t('newOrder.choosePackageType')} accessibilityLabel={t('newOrder.packageType')} invalid={!!err('packageType') && !p.packageType} inputRef={i === 0 ? (el) => { firstPackageRef.current = el } : undefined} testID={`pkg-type-${i}`} />
                </Labeled>
              )}
              <View style={{ flex: 3, minWidth: 160 }}><Field label={t('newOrder.description')} value={p.description} onChangeText={(v) => setPackage(i, { description: v })} testID={`pkg-desc-${i}`} /></View>
              <View style={{ flex: 1, minWidth: 80 }}><Field label={t('newOrder.pieces')} value={p.pieces} onChangeText={(v) => setPackage(i, { pieces: v })} keyboardType="number-pad" error={i === 0 ? err('pieces') : null} testID={`pkg-pieces-${i}`} /></View>
              <View style={{ flex: 1, minWidth: 90 }}><Field label={t('newOrder.weight')} value={p.weightKg} onChangeText={(v) => setPackage(i, { weightKg: v })} keyboardType="decimal-pad" error={i === 0 ? err('weight') : null} testID={`pkg-weight-${i}`} /></View>
              {draft.packages.length > 1 && <Button title={t('newOrder.removePackage')} variant="link" onPress={() => set({ packages: draft.packages.filter((_, j) => j !== i) })} />}
            </View>
          ))}
          {!!err('packages') && <Notice>{err('packages')}</Notice>}
          {draft.packages.length < MAX_PACKAGES && <Button title={t('newOrder.addPackage')} variant="secondary" onPress={() => set({ packages: [...draft.packages, emptyPackage(rules.defaultPackageType)] })} testID="pkg-add" />}
          <View style={{ maxWidth: 260 }}>
            <Field label={t('newOrder.cod')} value={draft.codAmount} onChangeText={(v) => set({ codAmount: v })} keyboardType="decimal-pad" placeholder="0.00" error={err('cod')} testID="order-cod" />
          </View>
          <Muted>{t('newOrder.codNote')}</Muted>
        </Section>

        <Card>
          <Field label={t('newOrder.notes')} value={draft.notes} onChangeText={(v) => set({ notes: v })} multiline maxLength={MAX_NOTES + 50} error={err('notes')} testID="order-notes" />
        </Card>
        {!!error && <Notice>{error}</Notice>}
        {!wide && ticket}
      </View>
      {wide && <View style={{ width: 300 }}>{ticket}</View>}
    </View>
  )
}
