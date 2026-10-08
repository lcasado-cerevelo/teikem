import { useCallback, useEffect, useState } from 'react'
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import { useFocusEffect, useRouter } from 'expo-router'

import { clearUserSession, lockSession } from '../kernel/auth/session'
import { useSession } from '../kernel/auth/useSession'
import { useMyPermissions } from '../kernel/auth/permissions'
import { getOpenReceipt } from '../features/receive/localLookup'
import { getOpenPick } from '../features/dispatch/localPick'
import { getOpenCount } from '../features/count/localCount'
import { getOpenCountHints, refreshOpenCountHints } from '../features/count/openCountHints'
import { useT } from '../kernel/i18n/useT'
import {
  getCachedWarehouseOptions,
  refreshWarehouseOptions,
  selectActiveWarehouse,
  useActiveWarehouse,
  type WarehouseOption,
} from '../kernel/warehouse/activeWarehouse'
import { runSync, useAutoSync, useLastSync, usePendingCount } from '../kernel/sync/engine'
import { syncStatusKey } from '../kernel/sync/syncStatus'
import { BigButton } from '../kernel/ui/BigButton'
import { WarehousePickerModal } from '../kernel/ui/WarehousePickerModal'
import { colors, fontSize, spacing } from '../kernel/ui/theme'

type OpenKind = 'receive' | 'dispatch' | 'count'

/** Pantalla 2 (docs/mobile/app-almacen-plan.md §2): botones de las acciones + estado de sincronización.
 *  Un documento en curso (recibo, despacho o conteo) bloquea las demás acciones (decisión de Luis, 2026-09-28: uno a
 *  la vez por aparato): el botón del documento abierto lo retoma (cada pantalla nunca empieza uno nuevo mientras haya
 *  uno abierto, ver localLookup.ts/localPick.ts/localCount.ts) y los demás avisan en vez de navegar.
 *  docs/mobile/mejoras-ux-zebra.md §4: acciones en DOS columnas, botones altos (88 dp, icono arriba y texto debajo), y la
 *  pantalla desplazable por si el aparato es más corto; Sincronizar y Cerrar sesión debajo. */
export default function HomeScreen() {
  const { t } = useT()
  const router = useRouter()
  const { session, device } = useSession()
  const activeWarehouse = useActiveWarehouse()
  const pending = usePendingCount()
  const lastSync = useLastSync()
  useAutoSync()
  const permissions = useMyPermissions()
  const canReportDamage = permissions?.includes('warehouse.damage') ?? false

  const [openKind, setOpenKind] = useState<OpenKind | null>(null)
  const userId = session?.userId
  const warehouseId = activeWarehouse.publicId
  useFocusEffect(
    useCallback(() => {
      // Documento abierto en el aparato o, desde 2026-10-07, un conteo que este usuario dejó abierto en el servidor (guardó y siguió después):
      // en ambos casos solo se puede retomar ese documento.
      const compute = () => {
        if (getOpenReceipt() !== null) setOpenKind('receive')
        else if (getOpenPick() !== null) setOpenKind('dispatch')
        else if (getOpenCount() !== null || getOpenCountHints(warehouseId, userId).length > 0) setOpenKind('count')
        else setOpenKind(null)
      }
      compute()
      let alive = true
      if (warehouseId && userId != null) {
        void refreshOpenCountHints(warehouseId, userId).then(() => {
          if (alive) compute()
        })
      }
      return () => {
        alive = false
      }
    }, [warehouseId, userId]),
  )

  // 2026-10-07 (varios almacenes): la lista de almacenes de la compañía se baja al entrar (queda guardada para elegir sin señal).
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>(() => getCachedWarehouseOptions())
  const [pickingWarehouse, setPickingWarehouse] = useState(false)
  const deviceId = device?.devicePublicId
  useEffect(() => {
    let alive = true
    void refreshWarehouseOptions().then((list) => {
      if (alive) setWarehouses(list)
    })
    return () => {
      alive = false
    }
  }, [deviceId])

  /** Cambiar de almacén no se hace con un recibo, despacho o conteo abierto: ese documento es de su almacén. */
  function changeWarehouse() {
    if (openKind) {
      Alert.alert(t('lock.warehouseInProgress'))
      return
    }
    setPickingWarehouse(true)
  }

  function pickWarehouse(option: WarehouseOption) {
    setPickingWarehouse(false)
    if (option.publicId === activeWarehouse.publicId) return
    void selectActiveWarehouse(option).then(() => {
      Alert.alert(t('warehousePick.changed', { name: option.name }))
      void runSync() // baja las posiciones y existencias del almacén nuevo
    })
  }

  /** Navega a `path`, salvo que haya un documento distinto abierto (avisa cuál en vez de navegar). `ownKind` es el
   *  tipo de documento que esa pantalla retoma (undefined si no maneja ninguno, como Acomodar o Consultar). */
  function go(path: '/receive' | '/putaway' | '/dispatch' | '/count' | '/lookup' | '/damage', ownKind?: OpenKind) {
    if (openKind && openKind !== ownKind) {
      Alert.alert(t(`lock.${openKind}InProgress`))
      return
    }
    router.push(path)
  }

  const syncLabel = t(syncStatusKey(pending, Boolean(lastSync?.error)), { count: pending })

  return (
    <ScrollView contentContainerStyle={styles.fill} testID="home-scroll">
      <View style={styles.header}>
        <Text style={styles.title}>{t('home.title')}</Text>
        {session ? (
          // Nombre del usuario y, a la derecha, un candado pequeño (bloqueo rápido): sin ocupar una fila más.
          <View style={styles.userRow}>
            <Text style={styles.userName} numberOfLines={1}>{session.fullName}</Text>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t('home.lock')}
              hitSlop={8}
              onPress={() => {
                lockSession()
                router.replace('/lock')
              }}
              style={styles.lockBtn}
            >
              <Text style={styles.lockIcon}>🔒</Text>
              <Text style={styles.lockLabel}>{t('home.lock')}</Text>
            </Pressable>
          </View>
        ) : null}
      </View>

      {activeWarehouse.publicId ? (
        <View style={styles.warehouseRow} testID="home-warehouse">
          <Text style={styles.warehouseText} numberOfLines={1}>
            {t('home.warehouseLabel', { name: activeWarehouse.name ?? t('home.warehouseNone') })}
          </Text>
          {warehouses.length > 1 ? (
            <Pressable accessibilityRole="button" accessibilityLabel={t('home.changeWarehouse')} hitSlop={8} onPress={changeWarehouse} style={styles.lockBtn}>
              <Text style={styles.lockLabel}>{t('home.changeWarehouse')}</Text>
            </Pressable>
          ) : null}
        </View>
      ) : null}

      <View style={styles.grid} testID="home-grid">
        <View style={styles.cell}>
          <BigButton layout="tile" label={t('home.receive')} icon="📥" onPress={() => go('/receive', 'receive')} />
        </View>
        <View style={styles.cell}>
          <BigButton layout="tile" label={t('home.putaway')} icon="📦" variant="secondary" onPress={() => go('/putaway')} />
        </View>
        <View style={styles.cell}>
          <BigButton layout="tile" label={t('home.dispatch')} icon="🚚" variant="secondary" onPress={() => go('/dispatch', 'dispatch')} />
        </View>
        <View style={styles.cell}>
          <BigButton layout="tile" label={t('home.count')} icon="🔢" variant="secondary" onPress={() => go('/count', 'count')} />
        </View>
        <View style={styles.cell}>
          <BigButton layout="tile" label={t('home.lookup')} icon="🔎" variant="secondary" onPress={() => go('/lookup')} />
        </View>
        {/* 2026-10-08: solo con el permiso warehouse.damage (sin saberlo, no se ofrece) */}
        {canReportDamage ? (
          <View style={styles.cell}>
            <BigButton layout="tile" label={t('home.damage')} icon="💥" variant="secondary" onPress={() => go('/damage')} testID="home-damage" />
          </View>
        ) : null}
      </View>

      <View style={styles.syncBar}>
        <Pressable accessibilityRole="button" onPress={() => router.push('/sync')}>
          <Text style={[styles.syncText, lastSync?.error && styles.syncError]}>{syncLabel}</Text>
        </Pressable>
        <BigButton label={t('home.syncNow')} variant="secondary" onPress={() => void runSync()} />
      </View>

      <WarehousePickerModal
        visible={pickingWarehouse}
        options={warehouses}
        currentPublicId={activeWarehouse.publicId}
        defaultPublicId={device?.defaultWarehousePublicId ?? null}
        onSelect={pickWarehouse}
        onClose={() => setPickingWarehouse(false)}
      />

      <BigButton label={t('home.signOut')} variant="secondary" onPress={() => void clearUserSession().then(() => router.replace('/login'))} />
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.lg },
  header: { gap: spacing.xs },
  title: { color: colors.text, fontSize: 24, fontWeight: '700' },
  userRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  userName: { color: colors.muted, fontSize: fontSize.message, flexShrink: 1 },
  lockBtn: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, minHeight: 44, paddingHorizontal: spacing.md, borderRadius: 22, backgroundColor: colors.panelAlt },
  lockIcon: { fontSize: 16 },
  lockLabel: { color: colors.text, fontSize: 16, fontWeight: '600' },
  warehouseRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  warehouseText: { color: colors.text, fontSize: fontSize.message, fontWeight: '600', flexShrink: 1 },
  // dos columnas: cada celda ocupa la mitad (menos el espacio entre ellas); la quinta, sola, toma todo el ancho
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  cell: { flexBasis: '45%', flexGrow: 1 },
  syncBar: { gap: spacing.sm },
  syncText: { color: colors.muted, fontSize: fontSize.message, textAlign: 'center' },
  syncError: { color: colors.error },
})
