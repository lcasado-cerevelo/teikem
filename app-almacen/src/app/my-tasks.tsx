// Lote 172 (B1) — «Mis tareas»: lo que el supervisor le asignó a este usuario (avisos de llegada por recibir, avisos de salida por despachar y acomodos), listo para tocar y trabajar. Se lee de la base local
// (funciona sin señal); los acomodos asignados se piden en línea y quedan guardados. Lo que no tiene dueño aparece aparte con «Tomar». La asignación ordena el trabajo, no lo bloquea.
import { useCallback, useState } from 'react'
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import { useFocusEffect, useRouter } from 'expo-router'

import { useSession } from '../kernel/auth/useSession'
import { apiErrorMessage, toApiError } from '../kernel/api/client'
import { useFormat } from '../kernel/format/useFormat'
import { useT } from '../kernel/i18n/useT'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { BigButton } from '../kernel/ui/BigButton'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { getOpenCount } from '../features/count/localCount'
import { getOpenPick } from '../features/dispatch/localPick'
import { getOpenReceipt } from '../features/receive/localLookup'
import { fetchMyPutaway, getCachedMyPutaway, takeAsn, takeExitNotice, type MyPutawayTask } from '../features/myTasks/myTasksApi'
import {
  asnOpenCode,
  listAsnTasks,
  listExitTasks,
  markTakenLocally,
  type MyAsnTask,
  type MyExitTask,
} from '../features/myTasks/myTasksLogic'

interface Lists {
  asnMine: MyAsnTask[]
  exitMine: MyExitTask[]
  asnFree: MyAsnTask[]
  exitFree: MyExitTask[]
}

const EMPTY: Lists = { asnMine: [], exitMine: [], asnFree: [], exitFree: [] }

export default function MyTasksScreen() {
  const { t } = useT()
  const f = useFormat()
  const router = useRouter()
  const { session } = useSession()
  const warehousePublicId = useActiveWarehouse().publicId
  const userId = session?.userId ?? null
  const [lists, setLists] = useState<Lists>(EMPTY)
  const [putaway, setPutaway] = useState<MyPutawayTask[]>([])
  const [online, setOnline] = useState(true)
  const [showFree, setShowFree] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)

  const reload = useCallback(() => {
    if (!warehousePublicId || userId == null) return
    setLists({
      asnMine: listAsnTasks(warehousePublicId, userId, 'mine'),
      exitMine: listExitTasks(warehousePublicId, userId, 'mine'),
      asnFree: listAsnTasks(warehousePublicId, userId, 'unassigned'),
      exitFree: listExitTasks(warehousePublicId, userId, 'unassigned'),
    })
  }, [warehousePublicId, userId])

  useFocusEffect(
    useCallback(() => {
      reload()
      if (!warehousePublicId || userId == null) return undefined
      setPutaway(getCachedMyPutaway(warehousePublicId, userId))
      let alive = true
      void fetchMyPutaway(warehousePublicId, userId).then(
        (list) => {
          if (!alive) return
          setPutaway(list)
          setOnline(true)
        },
        () => {
          if (alive) setOnline(false)
        },
      )
      return () => {
        alive = false
      }
    }, [reload, warehousePublicId, userId]),
  )

  /** Un documento en curso (recibo, despacho o conteo) bloquea abrir otro: se avisa cuál en vez de navegar (decisión de Luis, 2026-09-28). */
  function blockedBy(ownKind?: 'receive'): string | null {
    if (getOpenReceipt() !== null) return ownKind === 'receive' ? null : 'lock.receiveInProgress'
    if (getOpenPick() !== null) return 'lock.dispatchInProgress'
    if (getOpenCount() !== null) return 'lock.countInProgress'
    return null
  }

  function openAsn(a: MyAsnTask) {
    const blocked = blockedBy('receive')
    if (blocked) return Alert.alert(t(blocked))
    router.push({ pathname: '/receive', params: { doc: asnOpenCode(a) } })
  }

  function openExit(o: MyExitTask) {
    const blocked = blockedBy()
    if (blocked) return Alert.alert(t(blocked))
    router.push({ pathname: '/exit-dispatch', params: { notice: o.number } })
  }

  async function take(key: string, run: () => Promise<void>, done: () => void) {
    setBusy(key)
    try {
      await run()
      done()
      reload()
    } catch (e) {
      Alert.alert(apiErrorMessage(toApiError(e)))
    } finally {
      setBusy(null)
    }
  }

  const total = lists.asnMine.length + lists.exitMine.length + putaway.length
  const free = lists.asnFree.length + lists.exitFree.length

  return (
    <ScrollView contentContainerStyle={styles.fill} testID="my-tasks">
      <Text style={styles.title}>{t('myTasks.title')}</Text>
      {!online ? <Text style={styles.help}>{t('myTasks.offline')}</Text> : null}
      {total === 0 ? <Text style={styles.help} testID="my-tasks-empty">{t('myTasks.empty')}</Text> : null}

      {lists.asnMine.length > 0 ? <Text style={styles.section}>{t('myTasks.receive', { count: lists.asnMine.length })}</Text> : null}
      {lists.asnMine.map((a) => (
        <Pressable key={`a${a.id}`} accessibilityRole="button" onPress={() => openAsn(a)} style={({ pressed }) => [styles.card, pressed && styles.cardPressed]} testID={`my-asn-${a.id}`}>
          <Text style={styles.cardTitle}>{[a.number || `#${a.id}`, a.reference].filter(Boolean).join(' · ')}</Text>
          <Text style={styles.help}>{[a.clientName, a.expectedDate ? t('myTasks.expected', { date: f.date(a.expectedDate) }) : null].filter(Boolean).join(' · ')}</Text>
        </Pressable>
      ))}

      {lists.exitMine.length > 0 ? <Text style={styles.section}>{t('myTasks.dispatch', { count: lists.exitMine.length })}</Text> : null}
      {lists.exitMine.map((o) => (
        <Pressable key={`o${o.id}`} accessibilityRole="button" onPress={() => openExit(o)} style={({ pressed }) => [styles.card, pressed && styles.cardPressed]} testID={`my-exit-${o.number}`}>
          <Text style={styles.cardTitle}>{o.number}</Text>
          <Text style={styles.help}>
            {[o.clientName, o.destination ?? o.reference, o.requiredDate ? t('myTasks.required', { date: f.date(o.requiredDate) }) : null].filter(Boolean).join(' · ')}
          </Text>
        </Pressable>
      ))}

      {putaway.length > 0 ? <Text style={styles.section}>{t('myTasks.putaway', { count: putaway.length })}</Text> : null}
      {putaway.map((p) => (
        <Pressable key={`p${p.id}`} accessibilityRole="button" onPress={() => router.push('/putaway')} style={({ pressed }) => [styles.card, pressed && styles.cardPressed]} testID={`my-putaway-${p.id}`}>
          <Text style={styles.cardTitle}>{p.sku}{p.quantity != null ? ` × ${f.qty(p.quantity)}` : ''}</Text>
          <Text style={styles.help}>{[p.productName, p.toBinCode ? t('myTasks.toBin', { bin: p.toBinCode }) : null].filter(Boolean).join(' · ')}</Text>
        </Pressable>
      ))}

      {free > 0 ? (
        <BigButton
          label={showFree ? t('myTasks.hideFree') : t('myTasks.showFree', { count: free })}
          variant="secondary"
          onPress={() => setShowFree((v) => !v)}
          testID="my-tasks-free-toggle"
        />
      ) : null}
      {showFree ? <Text style={styles.help}>{t('myTasks.freeHelp')}</Text> : null}
      {showFree
        ? lists.asnFree.map((a) => (
            <View key={`fa${a.id}`} style={styles.card}>
              <Text style={styles.cardTitle}>{[a.number || `#${a.id}`, a.reference].filter(Boolean).join(' · ')}</Text>
              <Text style={styles.help}>{[t('myTasks.kindReceive'), a.clientName].filter(Boolean).join(' · ')}</Text>
              <BigButton
                label={t('myTasks.take')}
                loading={busy === `fa${a.id}`}
                onPress={() => void take(`fa${a.id}`, () => takeAsn(a.id), () => userId != null && markTakenLocally('asn', a.id, userId))}
                testID={`take-asn-${a.id}`}
              />
            </View>
          ))
        : null}
      {showFree
        ? lists.exitFree.map((o) => (
            <View key={`fo${o.id}`} style={styles.card}>
              <Text style={styles.cardTitle}>{o.number}</Text>
              <Text style={styles.help}>{[t('myTasks.kindDispatch'), o.clientName].filter(Boolean).join(' · ')}</Text>
              <BigButton
                label={t('myTasks.take')}
                loading={busy === `fo${o.id}`}
                onPress={() => void take(`fo${o.id}`, () => takeExitNotice(o.publicId), () => userId != null && markTakenLocally('exit', o.id, userId))}
                testID={`take-exit-${o.number}`}
              />
            </View>
          ))
        : null}

      <View style={styles.bottom}>
        <BigButton label={t('common.back')} variant="secondary" onPress={() => router.replace('/home')} />
      </View>
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  section: { color: colors.text, fontSize: fontSize.label, fontWeight: '700', marginTop: spacing.sm },
  help: { color: colors.muted, fontSize: fontSize.message },
  card: { gap: spacing.xs, padding: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.panelAlt },
  cardPressed: { backgroundColor: colors.panel },
  cardTitle: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  bottom: { marginTop: 'auto' },
})
