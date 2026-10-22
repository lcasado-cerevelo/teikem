// Captura RÁPIDA de envíos (2026-10-22): la rejilla de la web de escritorio por donde la persona entra los datos. Cada Enter (o «Guardar fila») crea UN envío; la fila guardada queda de solo
// lectura con su número y debajo sale la fila nueva con la fecha de la anterior. El consignatario se escribe y se escoge de la lista (Enter toma el primero; «+ Nuevo» lo crea en la misma fila).
// Los errores del servidor salen en la fila sin perder lo escrito. Es lo mismo que «Entrada rápida» de la aplicación del personal.
import { useRef, useState } from 'react'
import { Pressable, Text, TextInput, View, type TextInputProps } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Chip, Notice, useColors } from '../../kernel/ui/ui'
import { formatMoney } from '../format'
import { createOrder, type PortalOrderFormDto, type PortalPlaceDto } from '../portalApi'
import { MAX_NOTES } from './newOrderRules'
import { emptyConsignee, emptyQuickRow, firstErrorField, matchConsignees, nextQuickRow, QUICK_FIELDS, quickRequest, type QuickField, type QuickRow, validateQuickRow } from './quickRules'

interface SavedRow {
  key: number
  publicId: string
  waybill: string
  consignee: string
  description: string
  date: string
  pieces: string
  cod: number
}

const W = { date: 124, invoice: 120, pieces: 70, weight: 84, cod: 100, action: 140 } as const

export function NewOrderQuick({ form, onOpen }: { form: PortalOrderFormDto; onOpen?: (publicId: string) => void }) {
  const colors = useColors()
  const { t, lang } = useT()
  const hasInvoice = !!form.clientTypesInvoiceNumber
  const consignees: PortalPlaceDto[] = form.consignees ?? []
  const [row, setRow] = useState<QuickRow>(() => emptyQuickRow())
  const [saved, setSaved] = useState<SavedRow[]>([])
  const [errors, setErrors] = useState<ReturnType<typeof validateQuickRow>>([])
  const [serverError, setServerError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [suggestOpen, setSuggestOpen] = useState(false)
  const [hi, setHi] = useState(0)
  const refs = useRef<Partial<Record<QuickField, TextInput | null>>>({})
  const counter = useRef(0)

  const fields = QUICK_FIELDS.filter((f) => f !== 'invoice' || hasInvoice)
  const focus = (f: QuickField | null) => f && setTimeout(() => refs.current[f]?.focus(), 0)
  const focusNext = (f: QuickField) => focus(fields[fields.indexOf(f) + 1] ?? null)
  const set = (patch: Partial<QuickRow>) => setRow((r) => ({ ...r, ...patch }))

  const matches = !row.consigneeId && !row.newConsignee ? matchConsignees(consignees, row.consigneeQuery) : []
  const showList = suggestOpen && !row.consigneeId && !row.newConsignee && row.consigneeQuery.trim().length > 0
  const choose = (c: PortalPlaceDto) => {
    set({ consigneeId: c.publicId as string, consigneeQuery: c.name ?? '', newConsignee: null })
    setSuggestOpen(false)
    focusNext('consignee')
  }
  const startNew = () => {
    set({ consigneeId: '', newConsignee: { ...emptyConsignee(), name: row.consigneeQuery.trim() } })
    setSuggestOpen(false)
  }

  async function save() {
    if (saving) return
    const errs = validateQuickRow(row)
    setErrors(errs)
    setServerError(null)
    if (errs.length > 0) return focus(firstErrorField(errs))
    setSaving(true)
    try {
      const created = await createOrder(quickRequest(row, hasInvoice))
      const name = row.newConsignee ? row.newConsignee.name : (consignees.find((c) => c.publicId === row.consigneeId)?.name ?? row.consigneeQuery)
      const cod = Number(row.cod.replace(',', '.')) || 0
      setSaved((s) => [...s, { key: ++counter.current, publicId: created.publicId as string, waybill: created.waybillNumber || created.orderNumber || '', consignee: name, description: row.description.trim(), date: row.date, pieces: row.pieces, cod }])
      setRow((r) => nextQuickRow(r))
      setErrors([])
      focus('date')
    } catch (e) {
      setServerError(apiErrorMessage(e))
    } finally {
      setSaving(false)
    }
  }

  const err = (f: QuickField) => errors.some((k) => firstErrorField([k]) === f)
  const input = (f: QuickField, value: string, onChange: (v: string) => void, extra: TextInputProps = {}) => (
    <TextInput
      ref={(el) => { refs.current[f] = el }}
      accessibilityLabel={t(`newOrder.quick.col.${f}`)}
      testID={`quick-${f}`}
      value={value}
      onChangeText={onChange}
      onSubmitEditing={() => (f === fields[fields.length - 1] ? void save() : focusNext(f))}
      blurOnSubmit={false}
      placeholderTextColor={colors.muted}
      style={{ minHeight: 40, borderRadius: 8, borderWidth: 1, borderColor: err(f) ? colors.danger : colors.line, backgroundColor: colors.card, color: colors.text, paddingHorizontal: 8, fontSize: 14 }}
      {...extra}
    />
  )
  const head = (label: string, width?: number, flex = 1) => (
    <View style={{ width, flex: width ? undefined : flex, minWidth: width ? undefined : 120, paddingHorizontal: 4 }}>
      <Text numberOfLines={1} style={{ fontSize: 12, fontWeight: '700', letterSpacing: 0.4, textTransform: 'uppercase', color: colors.muted }}>{label}</Text>
    </View>
  )
  const cell = (child: React.ReactNode, width?: number, flex = 1) => <View style={{ width, flex: width ? undefined : flex, minWidth: width ? undefined : 120, paddingHorizontal: 4 }}>{child}</View>
  const text = (v: string, bold?: boolean, muted?: boolean) => <Text numberOfLines={1} style={{ fontSize: 14, fontWeight: bold ? '700' : '400', color: muted ? colors.muted : colors.text }}>{v}</Text>

  const codTotal = saved.reduce((n, s) => n + s.cod, 0)
  const nc = row.newConsignee

  return (
    <View style={{ gap: 10 }} testID="new-order-quick">
      <View style={{ flexDirection: 'row', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
        <Chip label={t('newOrder.quick.savedCount', { n: saved.length })} tone="info" />
        {codTotal > 0 && <Chip label={t('newOrder.quick.codTotal', { amount: formatMoney(codTotal, lang) })} tone="neutral" />}
        <Text style={{ color: colors.muted, fontSize: 13, flexShrink: 1 }}>{t('newOrder.quick.hint')}</Text>
      </View>

      <View style={{ backgroundColor: colors.card, borderRadius: 12, borderWidth: 1, borderColor: colors.line, padding: 10, gap: 8 }}>
        <View style={{ flexDirection: 'row' }}>
          {head(t('newOrder.quick.col.date'), W.date)}
          {hasInvoice && head(t('newOrder.quick.col.invoice'), W.invoice)}
          {head(t('newOrder.quick.col.consignee'), undefined, 2)}
          {head(t('newOrder.quick.col.description'))}
          {head(t('newOrder.quick.col.pieces'), W.pieces)}
          {head(t('newOrder.quick.col.weight'), W.weight)}
          {head(t('newOrder.quick.col.cod'), W.cod)}
          {head(t('newOrder.quick.col.notes'))}
          {head('', W.action)}
        </View>

        {saved.map((s) => (
          <Pressable key={s.key} testID={`quick-saved-${s.key}`} accessibilityRole="link" onPress={() => onOpen?.(s.publicId)} style={{ flexDirection: 'row', alignItems: 'center', minHeight: 36, backgroundColor: colors.okBg, borderRadius: 8 }}>
            {cell(text(s.date, false, true), W.date)}
            {hasInvoice && cell(null, W.invoice)}
            {cell(text(s.consignee, true), undefined, 2)}
            {cell(text([s.waybill, s.description].filter(Boolean).join(' · '), true))}
            {cell(text(s.pieces), W.pieces)}
            {cell(null, W.weight)}
            {cell(text(s.cod ? formatMoney(s.cod, lang) : '—', false, true), W.cod)}
            {cell(null)}
            {cell(<Text style={{ color: colors.ok, fontWeight: '700' }}>✓ {t('newOrder.quick.saved')}</Text>, W.action)}
          </Pressable>
        ))}

        <View style={{ flexDirection: 'row', alignItems: 'flex-start', zIndex: 10 }}>
          {cell(input('date', row.date, (v) => set({ date: v }), { placeholder: 'AAAA-MM-DD', autoCapitalize: 'none' }), W.date)}
          {hasInvoice && cell(input('invoice', row.invoice, (v) => set({ invoice: v }), { autoCapitalize: 'characters' }), W.invoice)}
          {cell(
            <View style={{ zIndex: 20 }}>
              {input('consignee', row.consigneeQuery, (v) => { set({ consigneeQuery: v, consigneeId: '', newConsignee: null }); setSuggestOpen(true); setHi(0) }, {
                autoCapitalize: 'none',
                placeholder: t('newOrder.quick.consigneePlaceholder'),
                onFocus: () => setSuggestOpen(true),
                onSubmitEditing: () => {
                  if (showList && matches.length > 0) choose(matches[Math.min(hi, matches.length - 1)])
                  else if (showList) startNew()
                  else focusNext('consignee')
                },
                onKeyPress: (e) => {
                  const k = (e.nativeEvent as { key?: string }).key
                  if (k === 'ArrowDown') setHi((h) => Math.min(h + 1, Math.max(matches.length - 1, 0)))
                  if (k === 'ArrowUp') setHi((h) => Math.max(h - 1, 0))
                  if (k === 'Escape') setSuggestOpen(false)
                },
              })}
              {showList && (
                <View style={{ position: 'absolute', top: 42, left: 0, right: 0, backgroundColor: colors.card, borderWidth: 1, borderColor: colors.line, borderRadius: 8, zIndex: 50, overflow: 'hidden' }}>
                  {matches.map((c, i) => (
                    <Pressable key={c.publicId} onPress={() => choose(c)} accessibilityRole="button" testID={`quick-suggest-${i}`} style={{ padding: 8, backgroundColor: i === hi ? colors.neutralBg : colors.card }}>
                      <Text style={{ fontWeight: '700', color: colors.text }}>{c.name}</Text>
                      <Text style={{ fontSize: 12, color: colors.muted }}>{[c.line1, c.city].filter(Boolean).join(', ')}</Text>
                    </Pressable>
                  ))}
                  <Pressable onPress={startNew} accessibilityRole="button" testID="quick-new-consignee" style={{ padding: 8, backgroundColor: matches.length === 0 ? colors.neutralBg : colors.card }}>
                    <Text style={{ fontWeight: '700', color: colors.link }}>{t('newOrder.quick.newConsignee', { name: row.consigneeQuery.trim() })}</Text>
                  </Pressable>
                </View>
              )}
            </View>,
            undefined,
            2,
          )}
          {cell(input('description', row.description, (v) => set({ description: v })))}
          {cell(input('pieces', row.pieces, (v) => set({ pieces: v }), { keyboardType: 'number-pad' }), W.pieces)}
          {cell(input('weight', row.weightKg, (v) => set({ weightKg: v }), { keyboardType: 'decimal-pad' }), W.weight)}
          {cell(input('cod', row.cod, (v) => set({ cod: v }), { keyboardType: 'decimal-pad' }), W.cod)}
          {cell(input('notes', row.notes, (v) => set({ notes: v }), { maxLength: MAX_NOTES + 50 }))}
          {cell(<Button title={t('newOrder.quick.saveRow')} onPress={() => void save()} busy={saving} testID="quick-save" />, W.action)}
        </View>

        {!!nc && (
          <View style={{ flexDirection: 'row', gap: 8, flexWrap: 'wrap', alignItems: 'flex-start', backgroundColor: colors.neutralBg, borderRadius: 8, padding: 8 }} testID="quick-new-block">
            <Text style={{ fontWeight: '700', color: colors.text, width: '100%' }}>{t('newOrder.quick.newBlock')}</Text>
            {([['name', 'name', 200], ['line1', 'line1', 260], ['city', 'city', 150], ['state', 'state', 100], ['postalCode', 'postalCode', 100]] as const).map(([k, label, w]) => (
              <View key={k} style={{ width: w, gap: 2 }}>
                <Text style={{ fontSize: 12, color: colors.muted }}>{t(`newOrder.${label}`)}</Text>
                <TextInput accessibilityLabel={t(`newOrder.${label}`)} testID={`quick-new-${k}`} value={nc[k]} onChangeText={(v) => set({ newConsignee: { ...nc, [k]: v } })} placeholderTextColor={colors.muted}
                  style={{ minHeight: 38, borderRadius: 8, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.card, color: colors.text, paddingHorizontal: 8, fontSize: 14 }} />
              </View>
            ))}
            <Button title={t('common.cancel')} variant="link" onPress={() => set({ newConsignee: null })} />
          </View>
        )}

        {errors.length > 0 && <Notice>{errors.map((k) => t(`newOrder.errors.${k}`, { max: 50 })).join(' ')}</Notice>}
        {!!serverError && <Notice>{serverError}</Notice>}
      </View>
    </View>
  )
}
