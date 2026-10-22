// Lote 161 — «¿Contra qué recibo?»: listado de los avisos de llegada y órdenes de compra pendientes del almacén (base local, funciona sin señal), con búsqueda PARCIAL
// (se escribe un pedazo de la referencia, número, cliente o proveedor). Tocar una fila empieza el recibo contra ese documento. Solo se ofrece lo que la compañía permite.
import { useMemo, useState } from 'react'
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { KeyboardInput } from '../../kernel/ui/KeyboardInput'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { SmallButton } from '../../kernel/ui/SmallButton'
import { useAssignAccess, useAssignees } from '../assign/assignScope'
import { assigneeName, reassign } from '../assign/assignApi'
import { ReassignModal } from '../assign/ReassignModal'
import { listPendingDocs, type LocalDoc } from './localLookup'
import type { ReceiveOptions } from './receiptRequirement'

const PAGE = 30

const keyOf = (d: LocalDoc) => `${d.kind}:${d.asnId ?? d.purchaseOrderPublicId}`

export function ReceiveDocumentList({
  warehousePublicId,
  options,
  userId,
  onPick,
  onPickMany,
}: {
  warehousePublicId: string
  options: ReceiveOptions
  userId: number | null
  onPick: (doc: LocalDoc) => void
  /** Lote 198: recibir varios seguidos (los marcados, en el orden de la lista). */
  onPickMany: (docs: LocalDoc[]) => void
}) {
  const { t } = useT()
  const kinds = [...(options.asn ? (['asn'] as const) : []), ...(options.purchaseOrder ? (['po'] as const) : [])]
  const [kind, setKind] = useState<'asn' | 'po'>(kinds[0] ?? 'asn')
  const [search, setSearch] = useState('')
  const [pages, setPages] = useState(1)
  const [selecting, setSelecting] = useState(false)
  const [showFree, setShowFree] = useState(false)
  const [showOthers, setShowOthers] = useState(false)
  const [tick, setTick] = useState(0)
  const [toReassign, setToReassign] = useState<{ doc: LocalDoc; title: string; current: number | null } | null>(null)
  const access = useAssignAccess()
  const people = useAssignees(access.viewAll)
  const [selected, setSelected] = useState<Record<string, LocalDoc>>({})
  const shownKind = kinds.includes(kind) ? kind : kinds[0]
  const limit = pages * PAGE
  const rows = useMemo(() => (shownKind ? listPendingDocs(warehousePublicId, shownKind, search, limit, userId) : []), [warehousePublicId, shownKind, search, limit, userId, tick])
  // Lote 198: primero lo asignado a esta persona; lo que nadie tiene asignado se ve con la opción de abajo (y lo de otras personas no se ofrece)
  const freeCount = rows.filter((r) => r.assignedToUserId == null).length
  const othersCount = access.viewAll ? rows.filter((r) => r.assignedToUserId != null && r.assignedToUserId !== userId).length : 0
  const shown = rows.filter(
    (r) => (r.assignedToUserId != null && r.assignedToUserId === userId) || (showFree && r.assignedToUserId == null) || (access.viewAll && showOthers && r.assignedToUserId != null && r.assignedToUserId !== userId),
  )
  const chosen = Object.values(selected)
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
      {shown.length === 0 ? <Text style={styles.help}>{t(search.trim() ? 'receive.noDocMatches' : showFree || freeCount === 0 ? 'receive.noPendingDocs' : 'receive.noneMine')}</Text> : null}
      <BigButton
        label={selecting ? t('receive.selectCancel') : t('receive.selectMany')}
        variant="secondary"
        testID="receive-select-many"
        onPress={() => {
          setSelecting((v) => !v)
          setSelected({})
        }}
      />
      {shown.map((r) => {
        const k = keyOf(r.doc)
        const otherOwner = r.assignedToUserId != null && r.assignedToUserId !== userId
        const mine = r.assignedToUserId != null && r.assignedToUserId === userId
        const on = selected[k] !== undefined
        return (
          <Pressable
            key={k}
            accessibilityRole="button"
            accessibilityLabel={r.title}
            accessibilityState={{ selected: on }}
            testID={`receive-doc-${k}`}
            onPress={() => {
              if (!selecting) return onPick(r.doc)
              setSelected((cur) => {
                const next = { ...cur }
                if (next[k]) delete next[k]
                else next[k] = r.doc
                return next
              })
            }}
            style={[styles.row, on && styles.rowOn]}
          >
            <Text style={styles.rowTitle}>{selecting ? `${on ? '☑' : '☐'} ` : ''}{r.title}</Text>
            {r.subtitle ? <Text style={styles.help}>{r.subtitle}</Text> : null}
            <Text style={mine ? styles.mine : styles.help}>
              {mine ? t('receive.assignedToYou') : otherOwner ? t('reassign.assignedTo', { name: assigneeName(r.assignedToUserId, people) }) : t('receive.unassigned')}
            </Text>
            {access.reassign ? (
              <SmallButton
                label={t('reassign.button')}
                testID={`receive-reassign-${k}`}
                onPress={() => setToReassign({ doc: r.doc, title: r.title, current: r.assignedToUserId })}
              />
            ) : null}
          </Pressable>
        )
      })}
      {freeCount > 0 ? (
        <BigButton
          label={showFree ? t('myTasks.hideFree') : t('myTasks.showFree', { count: freeCount })}
          variant="secondary"
          testID="receive-free-toggle"
          onPress={() => setShowFree((v) => !v)}
        />
      ) : null}
      {othersCount > 0 ? (
        <BigButton
          label={showOthers ? t('reassign.hideOthers') : t('reassign.showOthers', { count: othersCount })}
          variant="secondary"
          testID="receive-others-toggle"
          onPress={() => setShowOthers((v) => !v)}
        />
      ) : null}
      <ReassignModal
        visible={toReassign !== null}
        title={t('reassign.title', { what: toReassign?.title ?? '' })}
        currentUserId={toReassign?.current ?? null}
        onClose={() => setToReassign(null)}
        onPick={async (uid) => {
          if (!toReassign) return
          const d = toReassign.doc
          await reassign(d.kind, d.kind === 'asn' ? (d.asnId as number) : (d.purchaseOrderPublicId as string), uid)
          setTick((n) => n + 1)
        }}
      />
      {selecting && chosen.length > 0 ? (
        <BigButton
          label={t('receive.receiveSelected', { count: chosen.length })}
          testID="receive-selected-go"
          onPress={() => onPickMany(shown.map((r) => r.doc).filter((d) => selected[keyOf(d)] !== undefined))}
        />
      ) : null}
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
  rowOn: { borderWidth: 2, borderColor: colors.brand },
  rowDim: { opacity: 0.45 },
  mine: { color: colors.brand, fontSize: fontSize.message, fontWeight: '600' },
  rowTitle: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '600' },
})
