// Nueva orden (Lote 105): solo lo básico —consignatario (uno de los suyos o uno nuevo), recogida, paquetes, COD, factura propia si el cliente la numera, fecha pedida y notas—. La orden entra directo al flujo normal. El cliente
// sale de la sesión; quien solo puede consultar ve un aviso en lugar del formulario.
import { useRouter } from 'expo-router'
import { useEffect, useMemo, useState } from 'react'
import { Pressable, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { StoreKeys, storeGet, storeSet } from '../../kernel/storage'
import { PageHeader } from '../../kernel/ui/desk'
import { useIsWide } from '../../kernel/ui/layout'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Field, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { createOrder, getOrderForm, type PortalOrderFormDto, type PortalPlaceDto } from '../portalApi'
import { NewOrderQuick } from './NewOrderQuick'
import { buildRequest, emptyConsignee, emptyDraft, emptyPackage, MAX_NOTES, MAX_PACKAGES, type ErrorKey, type OrderDraft, validateDraft } from './newOrderRules'

function Choice({ selected, title, detail, onPress, testID }: { selected: boolean; title: string; detail?: string; onPress: () => void; testID?: string }) {
  const colors = useColors()
  return (
    <Pressable accessibilityRole="radio" accessibilityState={{ selected }} onPress={onPress} testID={testID} style={{ borderWidth: 1, borderColor: selected ? colors.flow : colors.line, backgroundColor: selected ? colors.card : colors.bg, borderRadius: 10, padding: 10, gap: 2 }}>
      <Text style={{ fontWeight: '700', color: colors.text }}>{(selected ? '● ' : '○ ') + title}</Text>
      {!!detail && <Text style={{ color: colors.muted, fontSize: 12 }}>{detail}</Text>}
    </Pressable>
  )
}

const place = (p: PortalPlaceDto) => [p.line1, p.city, p.state].filter(Boolean).join(', ')

export function NewOrderScreen() {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const [form, setForm] = useState<PortalOrderFormDto | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState<OrderDraft>(emptyDraft)
  const [shown, setShown] = useState<ErrorKey[]>([])
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const wide = useIsWide()
  // «Rápida» (la rejilla de captura) o «Detallada» (la tarjeta con todos los campos); se recuerda. En escritorio sale la rápida; en el celular, la detallada.
  const [mode, setModeState] = useState<'quick' | 'detailed' | null>(null)
  useEffect(() => {
    void storeGet(StoreKeys.newOrderMode).then((v) => setModeState(v === 'quick' || v === 'detailed' ? v : null))
  }, [])
  const effectiveMode: 'quick' | 'detailed' = mode === 'quick' || mode === 'detailed' ? mode : wide ? 'quick' : 'detailed'
  const setMode = (m: 'quick' | 'detailed') => {
    setModeState(m)
    void storeSet(StoreKeys.newOrderMode, m)
  }

  useEffect(() => {
    getOrderForm().then(setForm).catch((e) => setLoadError(apiErrorMessage(e)))
  }, [])

  const consignees = useMemo(() => {
    const q = filter.trim().toLowerCase()
    const all = form?.consignees ?? []
    return q ? all.filter((c) => `${c.name} ${c.line1} ${c.city}`.toLowerCase().includes(q)) : all
  }, [form, filter])

  if (loadError) return <Screen><Notice>{loadError}</Notice></Screen>
  if (!form) return <Screen><Spinner /></Screen>
  if (!form.canCreate) {
    return (
      <Screen>
        <Title>{t('newOrder.title')}</Title>
        <Notice tone="info">{t('newOrder.readOnly')}</Notice>
      </Screen>
    )
  }

  const modeSwitch = wide ? (
    <View style={{ flexDirection: 'row', gap: 8 }}>
      {(['quick', 'detailed'] as const).map((m) => (
        <Pressable key={m} accessibilityRole="button" accessibilityState={{ selected: effectiveMode === m }} testID={`neworder-mode-${m}`} onPress={() => setMode(m)}
          style={{ paddingVertical: 6, paddingHorizontal: 14, borderRadius: 18, borderWidth: 1, borderColor: effectiveMode === m ? colors.brand : colors.line, backgroundColor: effectiveMode === m ? colors.brand : colors.card }}>
          <Text style={{ color: effectiveMode === m ? colors.onBrand : colors.text, fontWeight: '600' }}>{t(`newOrder.mode.${m}`)}</Text>
        </Pressable>
      ))}
    </View>
  ) : null

  if (wide && effectiveMode === 'quick') {
    return (
      <Screen wide>
        <PageHeader title={t('newOrder.title')} subtitle={t('newOrder.quick.subtitle')}>{modeSwitch}</PageHeader>
        <NewOrderQuick form={form} onOpen={(id) => router.push({ pathname: '/order/[id]', params: { id } })} />
      </Screen>
    )
  }

  const set = (patch: Partial<OrderDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const err = (k: ErrorKey) => (shown.includes(k) ? t(`newOrder.errors.${k}`, { max: k === 'packages' ? MAX_PACKAGES : MAX_NOTES }) : null)
  const setPackage = (i: number, patch: Partial<OrderDraft['packages'][number]>) => set({ packages: draft.packages.map((p, j) => (j === i ? { ...p, ...patch } : p)) })
  const setNew = (patch: Partial<ReturnType<typeof emptyConsignee>>) => set({ newConsignee: { ...(draft.newConsignee ?? emptyConsignee()), ...patch } })

  async function submit() {
    const errors = validateDraft(draft)
    setShown(errors)
    if (errors.length > 0) return
    setSending(true)
    setError(null)
    try {
      const created = await createOrder(buildRequest(draft, !!form?.clientTypesInvoiceNumber))
      router.replace({ pathname: '/order/[id]', params: { id: created.publicId as string } })
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setSending(false)
    }
  }

  const usingNew = draft.consigneeId === null && draft.newConsignee !== null
  return (
    <Screen>
      {wide ? <PageHeader title={t('newOrder.title')} subtitle={t('newOrder.help')}>{modeSwitch}</PageHeader> : <><Title>{t('newOrder.title')}</Title><Muted>{t('newOrder.help')}</Muted></>}

      <Text style={{ fontWeight: '700', color: colors.text }}>{t('newOrder.consignee')}</Text>
      {(form.consignees ?? []).length > 6 && <Field label={t('newOrder.filter')} value={filter} onChangeText={setFilter} autoCapitalize="none" testID="neworder-filter" />}
      {consignees.map((c) => (
        <Choice key={c.publicId} selected={draft.consigneeId === c.publicId} title={c.name ?? ''} detail={place(c)} onPress={() => set({ consigneeId: c.publicId as string, newConsignee: null })} testID={`consignee-${c.publicId}`} />
      ))}
      <Choice selected={usingNew} title={t('newOrder.newConsignee')} onPress={() => set({ consigneeId: null, newConsignee: draft.newConsignee ?? emptyConsignee() })} testID="consignee-new" />
      {usingNew && (
        <Card>
          <Field label={t('newOrder.name')} value={draft.newConsignee!.name} onChangeText={(v) => setNew({ name: v })} testID="new-name" />
          <Field label={t('newOrder.line1')} value={draft.newConsignee!.line1} onChangeText={(v) => setNew({ line1: v })} testID="new-line1" />
          <Field label={t('newOrder.line2')} value={draft.newConsignee!.line2} onChangeText={(v) => setNew({ line2: v })} />
          <Field label={t('newOrder.city')} value={draft.newConsignee!.city} onChangeText={(v) => setNew({ city: v })} testID="new-city" />
          <Field label={t('newOrder.state')} value={draft.newConsignee!.state} onChangeText={(v) => setNew({ state: v })} />
          <Field label={t('newOrder.postalCode')} value={draft.newConsignee!.postalCode} onChangeText={(v) => setNew({ postalCode: v })} keyboardType="number-pad" />
        </Card>
      )}
      {!!err('consignee') && <Notice>{err('consignee')}</Notice>}

      {(form.pickups ?? []).length > 0 && (
        <>
          <Text style={{ fontWeight: '700', color: colors.text }}>{t('newOrder.pickup')}</Text>
          <Choice selected={draft.pickupId === null} title={t('newOrder.pickupDefault')} onPress={() => set({ pickupId: null })} />
          {(form.pickups ?? []).map((p) => (
            <Choice key={p.publicId} selected={draft.pickupId === p.publicId} title={p.name ?? ''} detail={place(p)} onPress={() => set({ pickupId: p.publicId as string })} />
          ))}
        </>
      )}

      <Text style={{ fontWeight: '700', color: colors.text }}>{t('newOrder.packages')}</Text>
      {draft.packages.map((p, i) => (
        <Card key={i}>
          <Field label={t('newOrder.description')} value={p.description} onChangeText={(v) => setPackage(i, { description: v })} testID={`pkg-desc-${i}`} />
          <Field label={t('newOrder.pieces')} value={p.pieces} onChangeText={(v) => setPackage(i, { pieces: v })} keyboardType="number-pad" testID={`pkg-pieces-${i}`} />
          <Field label={t('newOrder.weight')} value={p.weightKg} onChangeText={(v) => setPackage(i, { weightKg: v })} keyboardType="decimal-pad" testID={`pkg-weight-${i}`} />
          {draft.packages.length > 1 && <Button title={t('newOrder.removePackage')} variant="link" onPress={() => set({ packages: draft.packages.filter((_, j) => j !== i) })} />}
        </Card>
      ))}
      {draft.packages.length < MAX_PACKAGES && <Button title={t('newOrder.addPackage')} variant="secondary" onPress={() => set({ packages: [...draft.packages, emptyPackage()] })} testID="pkg-add" />}
      {(['packages', 'pieces', 'weight'] as const).map((k) => (err(k) ? <Notice key={k}>{err(k)}</Notice> : null))}

      <Field label={t('newOrder.cod')} value={draft.codAmount} onChangeText={(v) => set({ codAmount: v })} keyboardType="decimal-pad" error={err('cod')} testID="order-cod" />
      {!!form.clientTypesInvoiceNumber && <Field label={t('newOrder.invoice')} value={draft.invoiceNumber} onChangeText={(v) => set({ invoiceNumber: v })} autoCapitalize="characters" testID="order-invoice" />}
      <Field label={t('newOrder.requestedDate')} value={draft.requestedDate} onChangeText={(v) => set({ requestedDate: v })} placeholder="AAAA-MM-DD" autoCapitalize="none" error={err('date')} testID="order-date" />
      <Field label={t('newOrder.notes')} value={draft.notes} onChangeText={(v) => set({ notes: v })} multiline maxLength={MAX_NOTES + 50} error={err('notes')} testID="order-notes" />

      {!!error && <Notice>{error}</Notice>}
      <View style={{ gap: 8 }}>
        <Button title={t('newOrder.submit')} onPress={() => void submit()} busy={sending} testID="neworder-submit" />
        <Button title={t('common.cancel')} variant="secondary" onPress={() => router.back()} />
      </View>
    </Screen>
  )
}
