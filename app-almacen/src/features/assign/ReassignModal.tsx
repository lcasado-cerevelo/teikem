// Lote 199 — ventana para reasignar: las personas de la compañía y «Sin asignar». Pide señal (la reasignación va al servidor en el acto).
import { useEffect, useState } from 'react'
import { ActivityIndicator, FlatList, Modal, Pressable, StyleSheet, Text, View } from 'react-native'

import { apiErrorMessage, isNetworkError, toApiError } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { fetchAssignees, getCachedAssignees, type Assignee } from './assignApi'

export function ReassignModal({
  visible,
  title,
  currentUserId,
  onPick,
  onClose,
}: {
  visible: boolean
  title: string
  currentUserId: number | null
  /** Hace la reasignación (lanza si falla: el mensaje se muestra aquí). */
  onPick: (userId: number | null) => Promise<void>
  onClose: () => void
}) {
  const { t } = useT()
  const [people, setPeople] = useState<Assignee[]>(getCachedAssignees())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!visible) return undefined
    let alive = true
    setError(null)
    void fetchAssignees().then((list) => {
      if (alive) setPeople(list)
    })
    return () => {
      alive = false
    }
  }, [visible])

  async function choose(userId: number | null) {
    setBusy(true)
    setError(null)
    try {
      await onPick(userId)
      onClose()
    } catch (e) {
      const err = toApiError(e)
      setError(isNetworkError(err) ? t('reassign.needSignal') : apiErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  const rows: Array<{ id: number | null; name: string }> = [{ id: null, name: t('reassign.unassigned') }, ...people.filter((p) => p.id !== currentUserId)]

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose} transparent>
      <View style={styles.backdrop}>
        <View style={styles.sheet} testID="reassign-modal">
          <Text style={styles.title}>{title}</Text>
          {error ? <Text style={styles.error}>{error}</Text> : null}
          {busy ? <ActivityIndicator color={colors.brand} /> : null}
          <FlatList
            data={rows}
            keyExtractor={(r) => String(r.id)}
            renderItem={({ item }) => (
              <Pressable accessibilityRole="button" accessibilityLabel={item.name} disabled={busy} onPress={() => void choose(item.id)} style={styles.row} testID={`reassign-${item.id ?? 'none'}`}>
                <Text style={styles.rowText}>{item.name}</Text>
              </Pressable>
            )}
          />
          <BigButton label={t('common.cancel')} variant="secondary" onPress={onClose} />
        </View>
      </View>
    </Modal>
  )
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'flex-end' },
  sheet: { maxHeight: '80%', backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md, borderTopLeftRadius: radius.md, borderTopRightRadius: radius.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  error: { color: colors.error, fontSize: fontSize.message },
  row: { minHeight: 56, justifyContent: 'center', paddingHorizontal: spacing.md, backgroundColor: colors.panelAlt, borderRadius: radius.md, marginBottom: spacing.sm },
  rowText: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '600' },
})
