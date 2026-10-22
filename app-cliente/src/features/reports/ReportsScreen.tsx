// Informes del cliente: la lista de informes que ofrece su compañía, los filtros del informe escogido (fechas y búsqueda), la tabla con sus totales y «Exportar a Excel (CSV)».
// En escritorio la lista va a la izquierda y el informe a la derecha; en el celular, uno debajo del otro. Solo lectura y siempre de MI cliente.
import { useCallback, useMemo, useRef, useState } from 'react'
import { Pressable, ScrollView, Text, TextInput, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import type { Lang } from '../../kernel/i18n/i18n'
import { useT } from '../../kernel/i18n/useT'
import { PageHeader } from '../../kernel/ui/desk'
import { useIsWide } from '../../kernel/ui/layout'
import { Button, Card, Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useOffer } from '../capabilities'
import { formatDate, formatDateTime, formatMoney, formatQty } from '../format'
import { csvFileName, saveCsv, toCsv } from './csv'
import { availableReports, totalsOf, type CellValue, type ColType, type ReportDef, type ReportRow } from './reportDefs'
import { monthRange, PRESETS, type PresetId, rangeOf, todayDay } from './ranges'

const PREVIEW_ROWS = 200
const COL_WIDTH: Record<ColType, number> = { text: 140, date: 104, datetime: 150, int: 76, qty: 90, money: 104 }

export function formatCell(v: CellValue, type: ColType, lang: Lang): string {
  if (v === null || v === undefined || v === '') return type === 'text' ? '' : '—'
  if (type === 'date') return formatDate(String(v), lang)
  if (type === 'datetime') return formatDateTime(String(v), lang)
  if (type === 'money') return formatMoney(Number(v), lang)
  if (type === 'int' || type === 'qty') return formatQty(Number(v), lang)
  return String(v)
}

export function ReportsScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const wide = useIsWide()
  const offer = useOffer()
  const reports = useMemo(() => availableReports(offer), [offer])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const selected = reports.find((r) => r.id === selectedId) ?? null

  const groups = (['shipments', 'money', 'inventory'] as const).map((g) => ({ g, items: reports.filter((r) => r.group === g) })).filter((x) => x.items.length > 0)

  const list = (
    <View style={{ gap: 14, flexBasis: wide ? 300 : undefined, flexShrink: 0, width: wide ? 300 : '100%' }}>
      {groups.map(({ g, items }) => (
        <View key={g} style={{ gap: 6 }}>
          <Text style={{ fontSize: 12, fontWeight: '800', letterSpacing: 0.5, textTransform: 'uppercase', color: colors.muted }}>{t(`reports.groups.${g}`)}</Text>
          {items.map((r) => (
            <Pressable
              key={r.id}
              accessibilityRole="button"
              accessibilityState={{ selected: r.id === selectedId }}
              testID={`report-${r.id}`}
              onPress={() => setSelectedId(r.id)}
              style={{ padding: 12, borderRadius: 10, borderWidth: 1, borderColor: r.id === selectedId ? colors.flow : colors.line, backgroundColor: r.id === selectedId ? colors.neutralBg : colors.card, gap: 2 }}
            >
              <Text style={{ fontWeight: '700', color: colors.text }}>{t(`reports.name.${r.id}`)}</Text>
              <Text style={{ fontSize: 12, color: colors.muted }}>{t(`reports.desc.${r.id}`)}</Text>
            </Pressable>
          ))}
        </View>
      ))}
    </View>
  )

  return (
    <Screen wide>
      {wide ? <PageHeader title={t('reports.title')} subtitle={t('reports.subtitle')} /> : <><Title>{t('reports.title')}</Title><Muted>{t('reports.subtitle')}</Muted></>}
      {reports.length === 0 && <Notice tone="info">{t('reports.none')}</Notice>}
      <View style={{ flexDirection: wide ? 'row' : 'column', gap: 20, alignItems: 'flex-start' }}>
        {(wide || !selected) && list}
        {selected ? <ReportView key={selected.id} report={selected} onBack={wide ? undefined : () => setSelectedId(null)} /> : wide && reports.length > 0 ? <Muted>{t('reports.pick')}</Muted> : null}
      </View>
    </Screen>
  )
}

function ReportView({ report, onBack }: { report: ReportDef; onBack?: () => void }) {
  const colors = useColors()
  const { t, lang } = useT()
  const initial = monthRange()
  const [from, setFrom] = useState(report.uses.dates ? initial.from : '')
  const [to, setTo] = useState(report.uses.dates ? initial.to : '')
  const [search, setSearch] = useState('')
  const [rows, setRows] = useState<ReportRow[] | null>(null)
  const [truncated, setTruncated] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)
  const seq = useRef(0)

  const run = useCallback(async () => {
    const mine = ++seq.current
    setLoading(true)
    setError(null)
    try {
      const res = await report.run({ from: from.trim(), to: to.trim(), search: search.trim() }, lang)
      if (mine !== seq.current) return
      setRows(res.rows)
      setTruncated(res.truncated)
    } catch (e) {
      if (mine === seq.current) setError(apiErrorMessage(e))
    } finally {
      if (mine === seq.current) setLoading(false)
    }
  }, [report, from, to, search, lang])

  const preset = (id: PresetId) => {
    const r = rangeOf(id)
    setFrom(r.from)
    setTo(r.to)
  }

  async function exportCsv() {
    if (!rows) return
    setExporting(true)
    try {
      await saveCsv(csvFileName(t(`reports.name.${report.id}`), todayDay()), toCsv(report.columns, report.columns.map((c) => t(c.title)), rows, t('reports.total')))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setExporting(false)
    }
  }

  const totals = rows ? totalsOf(report.columns, rows) : {}
  const input = (value: string, onChange: (v: string) => void, label: string, width: number, extra: { placeholder?: string; testID?: string } = {}) => (
    <View style={{ width, gap: 2 }}>
      <Text style={{ fontSize: 12, fontWeight: '600', color: colors.muted }}>{label}</Text>
      <TextInput accessibilityLabel={label} value={value} onChangeText={onChange} autoCapitalize="none" placeholderTextColor={colors.muted} onSubmitEditing={() => void run()} {...extra}
        style={{ minHeight: 40, borderRadius: 8, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.card, color: colors.text, paddingHorizontal: 8, fontSize: 14 }} />
    </View>
  )

  return (
    <View style={{ flex: 1, gap: 12, minWidth: 0, width: '100%' }} testID="report-view">
      {!!onBack && <Button title={t('reports.back')} variant="link" onPress={onBack} />}
      <View>
        <Text style={{ fontSize: 20, fontWeight: '800', color: colors.text }}>{t(`reports.name.${report.id}`)}</Text>
        <Text style={{ color: colors.muted }}>{t(`reports.desc.${report.id}`)}</Text>
      </View>

      <Card>
        <View style={{ flexDirection: 'row', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          {report.uses.dates && (
            <>
              {input(from, setFrom, t('reports.from'), 130, { placeholder: 'AAAA-MM-DD', testID: 'report-from' })}
              {input(to, setTo, t('reports.to'), 130, { placeholder: 'AAAA-MM-DD', testID: 'report-to' })}
            </>
          )}
          {report.uses.search && input(search, setSearch, t('reports.search'), 220, { testID: 'report-search' })}
          <Button title={t('reports.run')} onPress={() => void run()} busy={loading} testID="report-run" />
        </View>
        {report.uses.dates && (
          <View style={{ flexDirection: 'row', gap: 8, flexWrap: 'wrap', marginTop: 6 }}>
            {PRESETS.map((p) => (
              <Pressable key={p} onPress={() => preset(p)} accessibilityRole="button" testID={`report-preset-${p}`} style={{ paddingVertical: 4, paddingHorizontal: 10, borderRadius: 14, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.card }}>
                <Text style={{ fontSize: 12, color: colors.text, fontWeight: '600' }}>{t(`reports.preset.${p}`)}</Text>
              </Pressable>
            ))}
          </View>
        )}
      </Card>

      {!!error && <Notice>{error}</Notice>}
      {loading && <Spinner />}
      {!loading && rows === null && !error && <Muted>{t('reports.hint')}</Muted>}

      {!loading && rows !== null && (
        <>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <Text style={{ fontWeight: '700', color: colors.text }} testID="report-count">{t('reports.rows', { n: rows.length })}</Text>
            {rows.length > PREVIEW_ROWS && <Muted>{t('reports.preview', { n: PREVIEW_ROWS })}</Muted>}
            <View style={{ flex: 1 }} />
            <Button title={t('reports.export')} variant="secondary" onPress={() => void exportCsv()} busy={exporting} disabled={rows.length === 0} testID="report-export" />
          </View>
          {truncated && <Notice tone="warn">{t('reports.truncated')}</Notice>}
          {Object.keys(totals).length > 0 && rows.length > 0 && (
            <View style={{ flexDirection: 'row', gap: 10, flexWrap: 'wrap' }} testID="report-summary">
              {report.columns.filter((c) => c.total).map((c) => (
                <View key={c.key} style={{ backgroundColor: colors.card, borderWidth: 1, borderColor: colors.line, borderRadius: 10, paddingVertical: 8, paddingHorizontal: 14 }}>
                  <Text style={{ fontSize: 11, fontWeight: '700', letterSpacing: 0.4, textTransform: 'uppercase', color: colors.muted }}>{t(c.title)}</Text>
                  <Text style={{ fontSize: 18, fontWeight: '800', color: colors.text }}>{formatCell(totals[c.key], c.type, lang)}</Text>
                </View>
              ))}
            </View>
          )}
          {rows.length === 0 ? (
            <Muted>{t('reports.empty')}</Muted>
          ) : (
            <ScrollView horizontal style={{ borderWidth: 1, borderColor: colors.line, borderRadius: 12, backgroundColor: colors.card, alignSelf: 'flex-start', maxWidth: '100%' }}>
              <View>
                <View style={{ flexDirection: 'row', backgroundColor: colors.neutralBg, paddingVertical: 10 }}>
                  {report.columns.map((c) => (
                    <View key={c.key} style={{ width: COL_WIDTH[c.type], paddingHorizontal: 10, alignItems: c.type === 'text' || c.type === 'date' || c.type === 'datetime' ? 'flex-start' : 'flex-end' }}>
                      <Text numberOfLines={1} style={{ fontSize: 12, fontWeight: '700', letterSpacing: 0.4, textTransform: 'uppercase', color: colors.muted }}>{t(c.title)}</Text>
                    </View>
                  ))}
                </View>
                {rows.slice(0, PREVIEW_ROWS).map((r, i) => (
                  <View key={i} style={{ flexDirection: 'row', paddingVertical: 8, borderTopWidth: 1, borderTopColor: colors.line }}>
                    {report.columns.map((c) => (
                      <View key={c.key} style={{ width: COL_WIDTH[c.type], paddingHorizontal: 10, alignItems: c.type === 'text' || c.type === 'date' || c.type === 'datetime' ? 'flex-start' : 'flex-end' }}>
                        <Text numberOfLines={2} style={{ fontSize: 14, color: colors.text }}>{formatCell(r[c.key] ?? null, c.type, lang)}</Text>
                      </View>
                    ))}
                  </View>
                ))}
                {Object.keys(totals).length > 0 && (
                  <View style={{ flexDirection: 'row', paddingVertical: 10, borderTopWidth: 2, borderTopColor: colors.line, backgroundColor: colors.neutralBg }} testID="report-totals">
                    {report.columns.map((c, i) => (
                      <View key={c.key} style={{ width: COL_WIDTH[c.type], paddingHorizontal: 10, alignItems: c.total ? 'flex-end' : 'flex-start' }}>
                        <Text style={{ fontSize: 14, fontWeight: '800', color: colors.text }}>{i === 0 && !c.total ? t('reports.total') : c.total ? formatCell(totals[c.key], c.type, lang) : ''}</Text>
                      </View>
                    ))}
                  </View>
                )}
              </View>
            </ScrollView>
          )}
        </>
      )}
    </View>
  )
}
