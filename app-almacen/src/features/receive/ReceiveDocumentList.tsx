// Lote 161 — «¿Contra qué recibo?»: listado de los avisos de llegada y órdenes de compra pendientes del almacén (base local, funciona sin señal), con búsqueda PARCIAL
// (se escribe un pedazo de la referencia, número, cliente o proveedor). Tocar una fila empieza el recibo contra ese documento. Solo se ofrece lo que la compañía permite.
import { useMemo, useState } from 'react'
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { KeyboardInput } from '../../kernel/ui/KeyboardInput'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { listPendingDocs, type LocalDoc } from './localLookup'
import type { ReceiveOptions } from './receiptRequirement'

const PAGE = 30

export function ReceiveDocumentList({ warehousePublicId, options, onPick }: { warehousePublicId: string; options: ReceiveOptions; onPick: (doc: LocalDoc) => void }) {
  const { t } = useT()
  const kinds = [...(options.asn ? (['asn'] as const) : []), ...(options.purchaseOrder ? (['po'] as const) : [])]
  const [kind, setKind] = useState<'asn' | 'po'>(kinds[0] ?? 'asn')
  const [search, setSearch] = useState('')
  const [pages, setPages] = useState(1)
  const shownKind = kinds.includes(kind) ? kind : kinds[0]
  const limit = pages * PAGE
  const rows = useMemo(() => (shownKind ? listPendingDocs(warehousePublicId, shownKind, search, limit) : []), [warehousePublicId, shownKind, search, limit])
  if (!shownKind) return null
  return (
    <View style={styles.wrap} testID="receive-doc-list">
      <Text style={styles.title}>{t('receive.pendingDocs')}</Text>
      {kinds.length > 1 ? (
        <View style={styles.tabs}>
          {kinds.map((k) => (
            <Pressable
              key={k}
              accessibilityRole="tab"
              accessibilityState={{ selected: shownKind === k }}
              testID={`receive-docs-${k}`}
              onPress={() => {
                setKind(k)
                setPages(1)
              }}
              style={[styles.tab, shownKind === k && styles.tabOn]}
            >
              <Text style={styles.tabText}>{t(k === 'asn' ? 'receive.tabAsn' : 'receive.tabPo')}</Text>
            </Pressable>
          ))}
        </View>
      ) : null}
      <KeyboardInput
        value={search}
        onChangeText={(q) => {
          setSearch(q)
          setPages(1)
        }}
        autoCapitalize="none"
        autoCorrect={false}
        placeholder={t(shownKind === 'asn' ? 'receive.searchAsn' : 'receive.searchPo')}
        placeholderTextColor={colors.muted}
        style={styles.input}
        accessibilityLabel={t('receive.searchDocs')}
      />
      {rows.length === 0 ? <Text style={styles.help}>{t(search.trim() ? 'receive.noDocMatches' : 'receive.noPendingDocs')}</Text> : null}
      {rows.map((r) => (
        <Pressable key={`${r.doc.kind}:${r.doc.asnId ?? r.doc.purchaseOrderPublicId}`} accessibilityRole="button" accessibilityLabel={r.title} onPress={() => onPick(r.doc)} style={styles.row}>
          <Text style={styles.rowTitle}>{r.title}</Text>
          {r.subtitle ? <Text style={styles.help}>{r.subtitle}</Text> : null}
        </Pressable>
      ))}
      {rows.length >= limit ? <BigButton label={t('receive.moreDocs')} variant="secondary" onPress={() => setPages((n) => n + 1)} /> : null}
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.sm },
  title: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
  tabs: { flexDirection: 'row', gap: spacing.sm },
  tab: { flex: 1, minHeight: 48, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, alignItems: 'center', justifyContent: 'center' },
  tabOn: { borderColor: colors.brand, backgroundColor: colors.panelAlt },
  tabText: { color: colors.text, fontSize: fontSize.message, fontWeight: '700' },
  input: { minHeight: 56, borderWidth: 2, borderColor: colors.line, borderRadius: radius.md, paddingHorizontal: spacing.md, fontSize: 20, color: colors.text, backgroundColor: colors.panelAlt },
  row: { minHeight: 64, justifyContent: 'center', paddingHorizontal: spacing.md, paddingVertical: spacing.sm, backgroundColor: colors.panelAlt, borderRadius: radius.md },
  rowTitle: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '600' },
})
