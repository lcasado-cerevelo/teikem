// Lote 156: lo que el cliente crea desde su portal de almacén: producto nuevo, aviso de llegada y aviso de salida. Los formularios solo juntan los datos; las reglas y los mensajes son los del servidor
// (que valida igual que para el personal), y los errores se muestran tal cual. Todo se crea siempre a nombre de mi cliente.
import { useEffect, useState } from 'react'
import { Pressable, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Check, Field, Muted, Notice, Spinner, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import {
  createArrival,
  createExit,
  createProduct,
  listWarehouseOptions,
  listWarehouseProducts,
  type PortalProductDto,
} from '../portalApi'

/** Número escrito por la persona (acepta coma decimal); vacío = null. */
export function parseNumber(text: string): number | null {
  const t = text.trim().replace(',', '.')
  if (t === '') return null
  const n = Number(t)
  return Number.isFinite(n) ? n : null
}

function FormShell({ title, children, error, busy, onSave, onCancel, saveLabel }: { title: string; children: React.ReactNode; error: string | null; busy: boolean; onSave: () => void; onCancel: () => void; saveLabel: string }) {
  const { t } = useT()
  const colors = useColors()
  return (
    <Card>
      <Text style={{ fontWeight: '700', fontSize: 16, color: colors.text }}>{title}</Text>
      {children}
      {!!error && <Notice>{error}</Notice>}
      <Button title={saveLabel} onPress={onSave} busy={busy} disabled={busy} testID="create-save" />
      <Button title={t('common.cancel')} variant="secondary" onPress={onCancel} disabled={busy} />
    </Card>
  )
}

// ---------------------------------------------------------------- producto nuevo

export function NewProductForm({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const { t } = useT()
  const [sku, setSku] = useState('')
  const [name, setName] = useState('')
  const [barcode, setBarcode] = useState('')
  const [brand, setBrand] = useState('')
  const [cost, setCost] = useState('')
  const [lot, setLot] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  async function save() {
    setBusy(true)
    setError(null)
    try {
      await createProduct({ sku, name, barcode: barcode || null, brand: brand || null, purchaseCost: parseNumber(cost), trackingType: lot ? 'LOT' : 'NONE' })
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }
  return (
    <FormShell title={t('create.product.title')} error={error} busy={busy} onSave={() => void save()} onCancel={onCancel} saveLabel={t('create.save')}>
      <Field label={t('create.product.sku')} value={sku} onChangeText={setSku} autoCapitalize="characters" testID="create-sku" />
      <Field label={t('create.product.name')} value={name} onChangeText={setName} testID="create-name" />
      <Field label={t('create.product.barcode')} value={barcode} onChangeText={setBarcode} autoCapitalize="none" />
      <Field label={t('create.product.brand')} value={brand} onChangeText={setBrand} />
      <Field label={t('create.product.cost')} value={cost} onChangeText={setCost} keyboardType="decimal-pad" />
      <Muted>{t('create.product.costHelp')}</Muted>
      <Check label={t('create.product.lot')} help={t('create.product.lotHelp')} value={lot} onChange={setLot} />
    </FormShell>
  )
}

// ---------------------------------------------------------------- líneas (producto + cantidad)

export interface DraftLine {
  product: PortalProductDto | null
  qty: string
  lot: string
  expiry: string
  price: string
}
const emptyLine = (): DraftLine => ({ product: null, qty: '', lot: '', expiry: '', price: '' })

function ProductPicker({ value, onPick }: { value: PortalProductDto | null; onPick: (p: PortalProductDto | null) => void }) {
  const { t } = useT()
  const colors = useColors()
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  useEffect(() => {
    const id = setTimeout(() => setQuery(search.trim()), 300)
    return () => clearTimeout(id)
  }, [search])
  const found = useAsync(() => (value ? Promise.resolve(null) : listWarehouseProducts(query, 0, 6)), [query, value])
  if (value)
    return (
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <Text style={{ fontWeight: '700', color: colors.text, flexShrink: 1 }}>{value.sku} · {value.name}</Text>
        <Button title={t('create.change')} variant="link" onPress={() => onPick(null)} />
      </View>
    )
  return (
    <View style={{ gap: 6 }}>
      <Field label={t('create.line.product')} value={search} onChangeText={setSearch} placeholder={t('warehouse.searchProducts')} autoCapitalize="none" />
      {found.loading && <Spinner />}
      {(found.data?.items ?? []).map((p) => (
        <Pressable key={p.publicId} onPress={() => onPick(p)} accessibilityRole="button" style={{ padding: 10, borderRadius: 8, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.card }}>
          <Text style={{ color: colors.text }}>{p.sku} · {p.name}</Text>
        </Pressable>
      ))}
    </View>
  )
}

function LinesEditor({ lines, setLines, withLotAndPrice }: { lines: DraftLine[]; setLines: (l: DraftLine[]) => void; withLotAndPrice: boolean }) {
  const { t } = useT()
  const colors = useColors()
  const patch = (i: number, p: Partial<DraftLine>) => setLines(lines.map((l, n) => (n === i ? { ...l, ...p } : l)))
  return (
    <View style={{ gap: 10 }}>
      <Text style={{ fontWeight: '700', color: colors.text }}>{t('create.lines')}</Text>
      {lines.map((l, i) => (
        <View key={i} style={{ gap: 6, padding: 10, borderRadius: 10, borderWidth: 1, borderColor: colors.line }}>
          <ProductPicker value={l.product} onPick={(p) => patch(i, { product: p, lot: '', expiry: '' })} />
          <Field label={t('create.line.qty')} value={l.qty} onChangeText={(v) => patch(i, { qty: v })} keyboardType="decimal-pad" testID={`create-qty-${i}`} />
          {withLotAndPrice && l.product?.trackingType === 'LOT' && (
            <>
              <Field label={t('create.line.lot')} value={l.lot} onChangeText={(v) => patch(i, { lot: v })} autoCapitalize="characters" />
              {!!l.lot.trim() && <Field label={t('create.line.expiry')} value={l.expiry} onChangeText={(v) => patch(i, { expiry: v })} placeholder="2027-03-01" autoCapitalize="none" />}
            </>
          )}
          {withLotAndPrice && <Field label={t('create.line.price')} value={l.price} onChangeText={(v) => patch(i, { price: v })} keyboardType="decimal-pad" />}
          {lines.length > 1 && <Button title={t('create.line.remove')} variant="link" onPress={() => setLines(lines.filter((_, n) => n !== i))} />}
        </View>
      ))}
      <Button title={t('create.line.add')} variant="secondary" onPress={() => setLines([...lines, emptyLine()])} />
    </View>
  )
}

function WarehousePicker({ value, onChange }: { value: string | null; onChange: (id: string) => void }) {
  const { t } = useT()
  const colors = useColors()
  const options = useAsync(listWarehouseOptions, [])
  const list = options.data ?? []
  useEffect(() => {
    if (!value && list.length === 1) onChange(list[0].publicId as string)
  }, [list.length]) // eslint-disable-line react-hooks/exhaustive-deps
  if (options.loading && !options.data) return <Spinner />
  if (list.length <= 1) return list.length === 1 ? <Muted>{t('create.warehouse')}: {list[0].code} · {list[0].name}</Muted> : null
  return (
    <View style={{ gap: 6 }}>
      <Text style={{ fontWeight: '600', color: colors.text }}>{t('create.warehouse')}</Text>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }}>
        {list.map((w) => {
          const on = w.publicId === value
          return (
            <Pressable key={w.publicId} onPress={() => onChange(w.publicId as string)} accessibilityRole="button" accessibilityState={{ selected: on }} style={{ minHeight: 40, paddingHorizontal: 14, borderRadius: 999, justifyContent: 'center', borderWidth: 1, borderColor: on ? colors.flow : colors.line, backgroundColor: on ? colors.flow : colors.card }}>
              <Text style={{ fontWeight: '600', color: on ? colors.onFlow : colors.text }}>{w.code}</Text>
            </Pressable>
          )
        })}
      </View>
    </View>
  )
}

// ---------------------------------------------------------------- aviso de llegada

export function NewArrivalForm({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const { t } = useT()
  const [warehouse, setWarehouse] = useState<string | null>(null)
  const [reference, setReference] = useState('')
  const [date, setDate] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([emptyLine()])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  async function save() {
    setBusy(true)
    setError(null)
    try {
      await createArrival({
        warehousePublicId: warehouse,
        reference: reference || null,
        expectedDate: date.trim() || null,
        lines: lines.map((l) => ({ productPublicId: l.product?.publicId ?? null, expectedQty: parseNumber(l.qty), lotNumber: l.lot.trim() || null, expiryDate: l.lot.trim() ? l.expiry.trim() || null : null, unitPrice: parseNumber(l.price) })),
      })
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }
  return (
    <FormShell title={t('create.arrival.title')} error={error} busy={busy} onSave={() => void save()} onCancel={onCancel} saveLabel={t('create.save')}>
      <WarehousePicker value={warehouse} onChange={setWarehouse} />
      <Field label={t('create.reference')} value={reference} onChangeText={setReference} />
      <Field label={t('create.arrival.date')} value={date} onChangeText={setDate} placeholder="2026-12-31" autoCapitalize="none" />
      <LinesEditor lines={lines} setLines={setLines} withLotAndPrice />
      <Muted>{t('create.arrival.priceHelp')}</Muted>
    </FormShell>
  )
}

// ---------------------------------------------------------------- aviso de salida

export function NewExitForm({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const { t } = useT()
  const [warehouse, setWarehouse] = useState<string | null>(null)
  const [reference, setReference] = useState('')
  const [destination, setDestination] = useState('')
  const [date, setDate] = useState('')
  const [notes, setNotes] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([emptyLine()])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  async function save() {
    setBusy(true)
    setError(null)
    try {
      await createExit({
        warehousePublicId: warehouse,
        reference: reference || null,
        destination: destination || null,
        requiredDate: date.trim() || null,
        notes: notes || null,
        lines: lines.map((l) => ({ productPublicId: l.product?.publicId ?? null, quantity: parseNumber(l.qty) })),
      })
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }
  return (
    <FormShell title={t('create.exit.title')} error={error} busy={busy} onSave={() => void save()} onCancel={onCancel} saveLabel={t('create.save')}>
      <WarehousePicker value={warehouse} onChange={setWarehouse} />
      <Field label={t('create.reference')} value={reference} onChangeText={setReference} />
      <Field label={t('create.exit.destination')} value={destination} onChangeText={setDestination} />
      <Field label={t('create.exit.date')} value={date} onChangeText={setDate} placeholder="2026-12-31" autoCapitalize="none" />
      <Field label={t('create.exit.notes')} value={notes} onChangeText={setNotes} />
      <LinesEditor lines={lines} setLines={setLines} withLotAndPrice={false} />
      <Muted>{t('create.exit.help')}</Muted>
    </FormShell>
  )
}
