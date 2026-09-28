import { FlatList, Pressable, StyleSheet, Text, View } from 'react-native'

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
  emptyLabel?: string
}

/** Lista de líneas capturadas (Recibir, Conteo, Despacho): una fila por línea, botón grande para quitarla. */
export function LineList({ items, onRemove, removeLabel, emptyLabel }: LineListProps) {
  if (items.length === 0) return emptyLabel ? <Text style={styles.empty}>{emptyLabel}</Text> : null
  return (
    <FlatList
      data={items}
      keyExtractor={(item) => String(item.id)}
      renderItem={({ item }) => (
        <View style={styles.row}>
          <View style={styles.texts}>
            <Text style={styles.title}>{item.title}</Text>
            {item.subtitle ? <Text style={styles.subtitle}>{item.subtitle}</Text> : null}
          </View>
          {onRemove ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`${removeLabel} ${item.title}`}
              onPress={() => onRemove(item.id)}
              style={styles.removeBtn}
            >
              <Text style={styles.removeLabel}>{removeLabel}</Text>
            </Pressable>
          ) : null}
        </View>
      )}
    />
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
})
