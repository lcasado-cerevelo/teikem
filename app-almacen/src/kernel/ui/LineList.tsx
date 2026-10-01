import { Pressable, StyleSheet, Text, View } from 'react-native'

import { colors, radius, spacing } from './theme'

export interface LineListItem {
  id: string | number
  title: string
  subtitle?: string
}

export interface LineListProps {
  items: LineListItem[]
  onRemove?: (id: LineListItem['id']) => void
  removeLabel: string
  /** Editar la línea (p. ej. corregir la cantidad contada). Con Editar y Quitar se pintan como íconos ✎ y ✕. */
  onEdit?: (id: LineListItem['id']) => void
  editLabel?: string
  emptyLabel?: string
}

/** Lista de líneas capturadas (Recibir, Conteo, Despacho): una fila por línea, botón grande para quitarla. */
export function LineList({ items, onRemove, removeLabel, emptyLabel, onEdit, editLabel }: LineListProps) {
  const icons = Boolean(onEdit)
  if (items.length === 0) return emptyLabel ? <Text style={styles.empty}>{emptyLabel}</Text> : null
  // lista simple (no FlatList): son pocas líneas y la pantalla ya desplaza con su ScrollView; una FlatList anidada en un
  // ScrollView da el aviso "VirtualizedLists should never be nested inside plain ScrollViews"
  return (
    <View>
      {items.map((item) => (
        <View key={String(item.id)} style={styles.row}>
          <View style={styles.texts}>
            <Text style={styles.title}>{item.title}</Text>
            {item.subtitle ? <Text style={styles.subtitle}>{item.subtitle}</Text> : null}
          </View>
          {onEdit ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`${editLabel ?? ''} ${item.title}`.trim()}
              onPress={() => onEdit(item.id)}
              style={[styles.removeBtn, styles.iconBtn, styles.editBtn]}
            >
              <Text style={styles.iconLabel}>✎</Text>
            </Pressable>
          ) : null}
          {onRemove ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`${removeLabel} ${item.title}`}
              onPress={() => onRemove(item.id)}
              style={[styles.removeBtn, icons && styles.iconBtn]}
            >
              <Text style={icons ? [styles.iconLabel, styles.iconRemove] : styles.removeLabel}>{icons ? '✕' : removeLabel}</Text>
            </Pressable>
          ) : null}
        </View>
      ))}
    </View>
  )
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.panelAlt,
    borderRadius: radius.md,
    marginBottom: spacing.sm,
  },
  texts: { flex: 1, gap: 2 },
  title: { color: colors.text, fontSize: 16, fontWeight: '600' },
  subtitle: { color: colors.muted, fontSize: 13 },
  removeBtn: { paddingVertical: spacing.sm, paddingHorizontal: spacing.md },
  removeLabel: { color: colors.error, fontWeight: '700' },
  empty: { color: colors.muted, fontSize: 14, textAlign: 'center', paddingVertical: spacing.lg },
  iconBtn: {
    minWidth: 48,
    minHeight: 44,
    paddingHorizontal: 0,
    alignItems: 'center',
    justifyContent: 'center',
    marginLeft: spacing.sm,
    borderWidth: 1,
    borderColor: colors.line,
    borderRadius: radius.sm,
  },
  editBtn: { borderColor: colors.brand },
  iconLabel: { color: colors.text, fontSize: 22, fontWeight: '700' },
  iconRemove: { color: colors.error },
})
