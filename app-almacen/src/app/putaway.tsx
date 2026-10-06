import { useCallback, useState } from 'react'
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text, View } from 'react-native'
import { useFocusEffect, useRouter } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'
import { useT } from '../kernel/i18n/useT'
import { ApiError } from '../kernel/api/client'
import { BigButton } from '../kernel/ui/BigButton'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import {
  completeTask,
  distributeTask,
  fetchOpenPutawayTasks,
  fetchPutawaySuggestions,
  findBinByCode,
  startTask,
  type PutawaySuggestion,
} from '../features/putaway/putawayApi'
import { addDistBin, chunkAt, distSummary, exceedsCapacity, isRestBin, parsePerBin, sortTasksMineFirst, type DistBin, type PutawayTask } from '../features/putaway/putawayLogic'
import { useFormat } from '../kernel/format/useFormat'
import { ScanMessage } from '../kernel/ui/ScanMessage'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { StickyAlert } from '../kernel/ui/StickyAlert'

/** Pantalla 4 (docs/mobile/app-almacen-plan.md §2): lista de tareas PUTAWAY (mías primero), escanear la posición
 *  destino y completar. Necesita señal (docs/lote8A-app-decisiones.md, segunda entrega): la tarea es de todo el
 *  almacén, no solo de este aparato, así que cada acción es una llamada directa, sin cola. */
export default function PutawayScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const { device, session } = useSession()
  const warehousePublicId = device?.defaultWarehousePublicId ?? null

  const [tasks, setTasks] = useState<PutawayTask[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  const [selected, setSelected] = useState<PutawayTask | null>(null)
  const [suggestions, setSuggestions] = useState<PutawaySuggestion[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [scanError, setScanError] = useState<string | null>(null)
  // aviso verde del último acomodo hecho, en la lista (se queda hasta abrir otra tarea)
  const [notice, setNotice] = useState<string | null>(null)
  // Reparto por posición: con cantidad por posición, cada escaneo suma una posición y "Confirmar reparto" las manda todas juntas.
  const [perBinText, setPerBinText] = useState('')
  const [distBins, setDistBins] = useState<DistBin[]>([])
  const perBin = parsePerBin(perBinText)
  const pending = selected?.quantity ?? 0
  // La posición que recibe solo lo que quedaba (la décima de 185 de 20): alerta fija, cerrable, mientras esa posición esté en el reparto.
  const [closedAlertBin, setClosedAlertBin] = useState<number | null>(null)
  const restAt = perBin > 0 ? distBins.findIndex((_, i) => isRestBin(pending, perBin, i)) : -1
  const restBin = restAt >= 0 ? distBins[restAt] : null

  function closeTask() {
    setSelected(null)
    setSuggestions(null)
    setPerBinText('')
    setDistBins([])
    setClosedAlertBin(null)
    setScanError(null)
  }

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
    setNotice(null)
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
      if (perBin > 0) {
        // Modo reparto: se acumula la posición; nada se manda hasta "Confirmar reparto".
        const added = addDistBin(distBins, { id: bin.id, code, free: bin.freeQty ?? null }, pending, perBin)
        if (!added.ok) {
          setScanError(
            added.reason === 'duplicate'
              ? t('putaway.binRepeated')
              : t('putaway.noRoomLeft', { total: f.qty(pending) }),
          )
          vibrateError()
          return
        }
        setDistBins(added.bins)
        setScanError(null)
        vibrateOk()
        return
      }
      await completeTask(selected.id, bin.id, selected.quantity)
      vibrateOk()
      setNotice(t('putaway.done', { sku: selected.sku, bin: code }))
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

  async function confirmDistribution() {
    if (!selected || distBins.length === 0 || perBin <= 0) return
    setBusy(true)
    try {
      await distributeTask(selected.id, perBin, distBins.map((b) => b.id))
      const { total, left } = distSummary(pending, perBin, distBins.length)
      vibrateOk()
      setNotice(t('putaway.distributed', { sku: selected.sku, qty: f.qty(total), count: distBins.length, left: f.qty(left) }))
      closeTask()
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
    // ScrollView: con las letras grandes y la posición sugerida, en un aparato corto el botón quedaba fuera de la pantalla
    return (
      <KeyboardScreen
        contentContainerStyle={styles.scroll}
        banner={
          restBin && closedAlertBin !== restBin.id ? (
            <StickyAlert
              message={t('putaway.restAlert', { bin: restBin.code, qty: f.qty(chunkAt(pending, perBin, restAt)), per: f.qty(perBin) })}
              onClose={() => setClosedAlertBin(restBin.id)}
            />
          ) : null
        }
      >
        <Text style={styles.title}>{selected.sku}</Text>
        <Text style={styles.help}>{selected.productName}</Text>
        {suggestions && suggestions.length > 0 ? (
          <View style={styles.suggestion}>
            <Text style={styles.suggestionLabel}>{t('putaway.suggestedBin')}</Text>
            <Text style={styles.suggestionBin}>{suggestions[0].binCode}</Text>
          </View>
        ) : null}
        {selected.quantity != null ? <Text style={styles.help}>{t('putaway.pendingQty', { qty: f.qty(selected.quantity) })}</Text> : null}
        <View style={styles.field}>
          <Text style={styles.label}>{t('putaway.perBinLabel')}</Text>
          <KeyboardInput
            value={perBinText}
            onChangeText={(v) => {
              setPerBinText(v)
              setDistBins([])
            }}
            keyboardType="decimal-pad"
            style={styles.input}
            accessibilityLabel={t('putaway.perBinLabel')}
          />
          <Text style={styles.help}>{t(perBin > 0 ? 'putaway.perBinHelpOn' : 'putaway.perBinHelp')}</Text>
        </View>
        <ScanField label={t(perBin > 0 ? 'putaway.scanNextLabel' : 'putaway.scanDestLabel')} error={scanError} onSubmit={scanDestination} pick="bin" />
        {perBin > 0 && distBins.length > 0 ? (
          <View style={styles.field}>
            {distBins.map((b, i) => (
              <View key={b.id} style={styles.distRow}>
                <View style={styles.distText}>
                  <Text style={styles.rowTitle}>{t('putaway.distLine', { bin: b.code, qty: f.qty(chunkAt(pending, perBin, i)) })}</Text>
                  {exceedsCapacity(b.free, chunkAt(pending, perBin, i)) ? (
                    <Text style={styles.capWarn}>{t('putaway.capacityWarn', { free: f.qty(b.free ?? 0), qty: f.qty(chunkAt(pending, perBin, i)) })}</Text>
                  ) : null}
                </View>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={t('putaway.removeBin', { bin: b.code })}
                  onPress={() => setDistBins((bins) => bins.filter((x) => x.id !== b.id))}
                  style={styles.distRemove}
                  hitSlop={8}
                >
                  <Text style={styles.distRemoveLabel}>✕</Text>
                </Pressable>
              </View>
            ))}
            <Text style={styles.help}>
              {t('putaway.distTotal', {
                total: f.qty(distSummary(pending, perBin, distBins.length).total),
                left: f.qty(distSummary(pending, perBin, distBins.length).left),
              })}
            </Text>
            <View style={styles.row2}>
              <BigButton label={t('putaway.confirmDist')} fullWidth={false} loading={busy} onPress={confirmDistribution} />
            </View>
          </View>
        ) : null}
        {busy ? <ActivityIndicator color={colors.brand} /> : null}
        <BigButton label={t('common.cancel')} variant="secondary" onPress={closeTask} />
      </KeyboardScreen>
    )
  }

  return (
    <View style={styles.fill}>
      <Text style={styles.title}>{t('home.putaway')}</Text>
      <ScanMessage tone="ok" message={notice} />
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
              <Text style={styles.rowQty}>{item.quantity != null ? f.qty(item.quantity) : ''}</Text>
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
  scroll: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message, textAlign: 'center' },
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
  rowTitle: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '600' },
  rowQty: { color: colors.brand, fontSize: 18, fontWeight: '700' },
  field: { gap: spacing.xs },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  input: {
    minHeight: 56,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: 12,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
  distRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  distText: { flex: 1, gap: 2 },
  capWarn: { color: colors.warn, fontSize: fontSize.listSubtitle, fontWeight: '700' },
  distRemove: { minWidth: 48, minHeight: 48, alignItems: 'center', justifyContent: 'center', borderRadius: 12, backgroundColor: colors.panelAlt },
  distRemoveLabel: { color: colors.error, fontSize: 22, fontWeight: '700' },
  row2: { flexDirection: 'row', gap: spacing.md, flexWrap: 'wrap' },
  suggestion: { alignItems: 'center', gap: spacing.xs, paddingVertical: spacing.md },
  suggestionLabel: { color: colors.muted, fontSize: fontSize.message },
  suggestionBin: { color: colors.brand, fontSize: 36, fontWeight: '800' },
})
