// Mis facturas (Lote 119): la lista de las facturas del cliente de la sesión, solo lectura (no se marcan como pagadas desde aquí). Filtro por estatus y «Cargar más».
import { useRouter } from 'expo-router'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Pressable, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Card, Chip, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { formatDate, formatMoney } from '../format'
import { listInvoices, PAGE_SIZE, type PortalInvoiceListItemDto } from '../portalApi'
import { invoiceTone } from './invoiceRules'

const FILTERS = ['', 'ISSUED', 'OVERDUE', 'PAID'] as const

export function InvoicesScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const [status, setStatus] = useState<string>('')
  const [items, setItems] = useState<PortalInvoiceListItemDto[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const seq = useRef(0)

  const load = useCallback(async (skip: number) => {
    const mine = ++seq.current
    setLoading(true)
    setError(null)
    try {
      const page = await listInvoices(status, skip, PAGE_SIZE)
      if (mine !== seq.current) return
      setItems((prev) => (skip === 0 ? (page.items ?? []) : [...prev, ...(page.items ?? [])]))
      setTotal(page.total ?? 0)
    } catch (e) {
      if (mine === seq.current) setError(apiErrorMessage(e))
    } finally {
      if (mine === seq.current) setLoading(false)
    }
  }, [status])

  useEffect(() => {
    void load(0)
  }, [load])

  return (
    <Screen wide>
      <Title>{t('invoices.title')}</Title>
      <Muted>{t('invoices.readOnly')}</Muted>
      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }}>
        {FILTERS.map((f) => (
          <Pressable key={f || 'all'} onPress={() => setStatus(f)} accessibilityRole="button" accessibilityState={{ selected: status === f }} testID={`invoices-filter-${f || 'all'}`}
            style={{ paddingVertical: 6, paddingHorizontal: 12, borderRadius: 16, borderWidth: 1, borderColor: status === f ? colors.brand : colors.line, backgroundColor: status === f ? colors.brand : 'transparent' }}>
            <Text style={{ color: status === f ? colors.onBrand : colors.text, fontWeight: '600' }}>{t(`invoices.filter.${f || 'all'}`)}</Text>
          </Pressable>
        ))}
      </View>
      {!!error && (
        <>
          <Notice>{error}</Notice>
          <Button title={t('common.retry')} variant="secondary" onPress={() => void load(0)} />
        </>
      )}
      {!loading && !error && items.length === 0 && <Muted>{t('invoices.empty')}</Muted>}
      {items.map((i) => (
        <Card key={i.publicId} onPress={() => router.push({ pathname: '/invoice/[id]', params: { id: i.publicId as string } })}>
          <View style={{ flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
            <Text style={{ fontWeight: '700', color: colors.text }}>{i.number}</Text>
            <Chip label={i.statusLabel ?? i.status ?? ''} tone={invoiceTone(i.status)} />
          </View>
          <Text style={{ color: colors.text, fontSize: 18, fontWeight: '700' }}>{formatMoney(i.total, lang)}</Text>
          {(i.balance ?? 0) > 0 && i.status !== 'VOID' && <Muted>{t('invoices.balance', { amount: formatMoney(i.balance, lang) })}</Muted>}
          <Muted>{[i.issueDate ? t('invoices.issued', { date: formatDate(i.issueDate, lang) }) : '', i.dueDate ? t('invoices.due', { date: formatDate(i.dueDate, lang) }) : ''].filter(Boolean).join(' · ')}</Muted>
        </Card>
      ))}
      {loading && <Spinner />}
      {!loading && items.length < total && <Button title={t('orders.loadMore', { shown: items.length, total })} variant="secondary" onPress={() => void load(items.length)} />}
    </Screen>
  )
}
