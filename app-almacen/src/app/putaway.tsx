import { useCallback, useState } from 'react'
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from 'react-native'
import { useFocusEffect, useRouter } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { ApiError } from '../kernel/api/client'
import { BigButton } from '../kernel/ui/BigButton'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { completeTask, fetchOpenPutawayTasks, fetchPutawaySuggestions, findBinByCode, startTask, type PutawaySuggestion } from '../features/putaway/putawayApi'
import { sortTasksMineFirst, type PutawayTask } from '../features/putaway/putawayLogic'

/** Pantalla 4 (docs/mobile/app-almacen-plan.md §2): lista de tareas PUTAWAY (mías primero), escanear la posición
 *  destino y completar. Necesita señal (docs/lote8A-app-decisiones.md, segunda entrega): la tarea es de todo el
 *  almacén, no solo de este aparato, así que cada acción es una llamada directa, sin cola. */
export default function PutawayScreen() {
  const { t } = useT()
  const router = useRouter()
  const { device, session } = useSession()
  const warehousePublicId = device?.defaultWarehousePublicId ?? null

  const [tasks, setTasks] = useState<PutawayTask[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  const [selected, setSelected] = useState<PutawayTask | null>(null)
  const [suggestions, setSuggestions] = useState<PutawaySuggestion[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [scanError, setScanError] = useState<string | null>(null)

  const load = useCallback(() => {
    if (!warehousePublicId) return
    setListError(null)
    fetchOpenPutawayTasks(warehousePublicId)
      .then(setTasks)
      .catch((err: unknown) => setListError(err instanceof ApiError ? err.title : t('errors.network')))
  }, [warehousePublicId, t])

  useFocusEffect(load)

  async function openTask(task: PutawayTask) {
    setBusy(true)
    setScanError(null)
    try {
      await startTask(task.id)
      const found = await fetchPutawaySuggestions(task.id)
      setSuggestions(found)
      setSelected(task)
    } catch (err) {
      setListError(err instanceof ApiError ? err.title : t('errors.network'))
    } finally {
      setBusy(false)
    }
  }

  async function scanDestination(code: string) {
    if (!selected || !warehousePublicId) return
    setBusy(true)
    try {
      const bin = await findBinByCode(warehousePublicId, code)
      if (!bin) {
        setScanError(t('putaway.binNotFound'))
        vibrateError()
        return
      }
      await completeTask(selected.id, bin.id, selected.quantity)
      vibrateOk()
      setSelected(null)
      setSuggestions(null)
      setScanError(null)
      load()
    } catch (err) {
      setScanError(err instanceof ApiError ? err.title : t('errors.network'))
      vibrateError()
    } finally {
      setBusy(false)
    }
  }

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  if (selected) {
    return (
      <View style={styles.fill}>
        <Text style={styles.title}>{selected.sku}</Text>
        <Text style={styles.help}>{selected.productName}</Text>
        {suggestions && suggestions.length > 0 ? (
          <View style={styles.suggestion}>
            <Text style={styles.suggestionLabel}>{t('putaway.suggestedBin')}</Text>
            <Text style={styles.suggestionBin}>{suggestions[0].binCode}</Text>
          </View>
        ) : null}
        <ScanField label={t('putaway.scanDestLabel')} error={scanError} onSubmit={scanDestination} />
        {busy ? <ActivityIndicator color={colors.brand} /> : null}
        <BigButton
          label={t('common.cancel')}
          variant="secondary"
          onPress={() => {
            setSelected(null)
            setSuggestions(null)
          }}
        />
      </View>
    )
  }

  return (
    <View style={styles.fill}>
      <Text style={styles.title}>{t('home.putaway')}</Text>
      {listError ? (
        <View style={styles.center}>
          <Text style={styles.error}>{listError}</Text>
          <BigButton label={t('common.retry')} onPress={load} />
        </View>
      ) : tasks === null ? (
        <View style={styles.center}>
          <ActivityIndicator size="large" color={colors.brand} />
        </View>
      ) : tasks.length === 0 ? (
        <Text style={styles.help}>{t('putaway.empty')}</Text>
      ) : (
        <FlatList
          data={sortTasksMineFirst(tasks, session?.userId ?? -1)}
          keyExtractor={(item) => String(item.id)}
          renderItem={({ item }) => (
            <Pressable accessibilityRole="button" onPress={() => openTask(item)} style={styles.row} disabled={busy}>
              <View style={styles.rowTexts}>
                <Text style={styles.rowTitle}>{item.sku}</Text>
                <Text style={styles.help}>{item.productName}</Text>
              </View>
              <Text style={styles.rowQty}>{item.quantity ?? ''}</Text>
            </Pressable>
          )}
        />
      )}
      <BigButton label={t('common.back')} variant="danger" onPress={() => router.back()} />
    </View>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: 20, fontWeight: '700' },
  help: { color: colors.muted, fontSize: 13 },
  error: { color: colors.error, fontSize: 15, textAlign: 'center' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    minHeight: 64,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.panelAlt,
    borderRadius: 12,
    marginBottom: spacing.sm,
  },
  rowTexts: { gap: 2 },
  rowTitle: { color: colors.text, fontSize: 17, fontWeight: '600' },
  rowQty: { color: colors.brand, fontSize: 18, fontWeight: '700' },
  suggestion: { alignItems: 'center', gap: spacing.xs, paddingVertical: spacing.md },
  suggestionLabel: { color: colors.muted, fontSize: 14 },
  suggestionBin: { color: colors.brand, fontSize: 36, fontWeight: '800' },
})
