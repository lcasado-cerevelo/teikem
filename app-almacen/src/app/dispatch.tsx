import { useMemo, useState } from 'react'
import { ActivityIndicator, Alert, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError } from '../kernel/api/client'
import { useSession } from '../kernel/auth/useSession'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { useT } from '../kernel/i18n/useT'
import { runSync } from '../kernel/sync/engine'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField } from '../kernel/ui/ScanField'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { fetchConsigneesForClient, resolveBinCodes, submitCollectAndPack } from '../features/dispatch/dispatchApi'
import { addLocalPickLine, discardLocalPick, getOpenPick, removeLocalPickLine, startLocalPick } from '../features/dispatch/localPick'
import {
  buildPickLine,
  canAddPickLine,
  type ConsigneeChoice,
  DISPATCH_ADD_ON_BIN_SCAN,
  newPickLineDraft,
  type PickLineDraft,
} from '../features/dispatch/dispatchLogic'

type Step = { name: 'scan' } | { name: 'consignee' } | { name: 'error'; message: string }

/** Pantalla 5 (docs/mobile/app-almacen-plan.md §2): recolectar (producto, cantidad, posición) sin señal; empacar
 *  (elegir consignatario y confirmar) necesita señal un momento y luego se manda por la cola. Solo clientes 3PL por
 *  ahora (docs/lote8A-app-decisiones.md): el dueño del producto ya viene sincronizado; inventario propio se completa
 *  en la web. */
export default function DispatchScreen() {
  const { t } = useT()
  const router = useRouter()
  const { device } = useSession()
  const warehousePublicId = device?.defaultWarehousePublicId ?? null
  const [tick, setTick] = useState(0)
  const [scanError, setScanError] = useState<string | null>(null)
  const [draft, setDraft] = useState<PickLineDraft | null>(null)
  const [packing, setPacking] = useState<Step>({ name: 'scan' })
  const [consignees, setConsignees] = useState<ConsigneeChoice[] | null>(null)
  const [pieces, setPieces] = useState('1')
  const [busy, setBusy] = useState(false)
  // aviso verde de la última línea agregada (se queda hasta la siguiente lectura)
  const [notice, setNotice] = useState<string | null>(null)

  // tick fuerza releer la base local tras cada mutación; getOpenPick() no usa tick.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const openPick = useMemo(() => getOpenPick(), [tick])
  const refresh = () => setTick((n) => n + 1)

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  function scanProduct(code: string) {
    setNotice(null)
    const product = findProductByCode(code)
    if (!product) {
      setScanError(t('dispatch.productNotFound'))
      vibrateError()
      return
    }
    if (!product.ownerClientPublicId) {
      setScanError(t('dispatch.ownedInventoryBlocked'))
      vibrateError()
      return
    }
    if (openPick?.clientPublicId && openPick.clientPublicId !== product.ownerClientPublicId) {
      setScanError(t('dispatch.ownedInventoryBlocked'))
      vibrateError()
      return
    }
    if (!openPick) startLocalPick(warehousePublicId!, { publicId: product.ownerClientPublicId, name: product.ownerName ?? '' })
    setScanError(null)
    setDraft(newPickLineDraft(product))
    vibrateOk()
    refresh()
  }

  function addLine(line: PickLineDraft) {
    if (!openPick || !canAddPickLine(line)) return
    const built = buildPickLine(line)
    addLocalPickLine(openPick.id, built)
    setDraft(null)
    setNotice(t('dispatch.lineAdded', { qty: built.quantity, sku: line.sku, bin: built.fromBinCode }))
    vibrateOk()
    refresh()
  }

  function addCurrentLine() {
    if (draft) addLine(draft)
  }

  /** Posición de donde sale: con la cantidad ya válida, la lectura agrega la línea (DISPATCH_ADD_ON_BIN_SCAN). */
  function scanFromBin(code: string) {
    if (!draft) return
    const next = { ...draft, fromBinCode: code }
    if (DISPATCH_ADD_ON_BIN_SCAN && canAddPickLine(next)) {
      addLine(next)
      return
    }
    setDraft(next)
  }

  function cancelDispatch() {
    Alert.alert(t('dispatch.cancelConfirmTitle'), t('dispatch.cancelConfirmBody'), [
      { text: t('common.no'), style: 'cancel' },
      {
        text: t('dispatch.cancelDispatch'),
        style: 'destructive',
        onPress: () => {
          discardLocalPick()
          setDraft(null)
          setPacking({ name: 'scan' })
          refresh()
        },
      },
    ])
  }

  async function startPacking() {
    if (!openPick || openPick.lineRows.length === 0) return
    setBusy(true)
    try {
      const resolved = await resolveBinCodes(warehousePublicId!, openPick.lineRows)
      if (resolved.notFound.length > 0) {
        setPacking({ name: 'error', message: t('putaway.binNotFound') + ' ' + resolved.notFound.join(', ') })
        return
      }
      const rows = await fetchConsigneesForClient(openPick.clientPublicId!)
      setConsignees(rows)
      setPacking({ name: 'consignee' })
    } catch (err) {
      setPacking({ name: 'error', message: err instanceof ApiError ? err.title : t('dispatch.consigneesError') })
    } finally {
      setBusy(false)
    }
  }

  async function confirmPack(consignee: ConsigneeChoice) {
    if (!openPick) return
    setBusy(true)
    try {
      const resolved = await resolveBinCodes(warehousePublicId!, openPick.lineRows)
      if (resolved.notFound.length > 0) {
        setPacking({ name: 'error', message: t('putaway.binNotFound') + ' ' + resolved.notFound.join(', ') })
        return
      }
      const n = Math.max(1, Math.round(Number(pieces) || 1))
      await submitCollectAndPack(warehousePublicId!, openPick.clientPublicId!, consignee.publicId, n, resolved.lines)
      discardLocalPick()
      void runSync()
      router.replace('/home')
    } catch (err) {
      setPacking({ name: 'error', message: err instanceof ApiError ? err.title : t('errors.generic') })
    } finally {
      setBusy(false)
    }
  }

  // Sin despacho abierto o capturando líneas.
  if (!openPick || draft) {
    return (
      <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
        <Text style={styles.title}>{t('dispatch.title')}</Text>
        {draft ? (
          <>
            <Text style={styles.help}>{draft.productName}</Text>
            <View style={styles.field}>
              <Text style={styles.label}>{t('dispatch.qtyLabel')}</Text>
              <TextInput
                value={draft.qtyText}
                onChangeText={(v) => setDraft((d) => (d ? { ...d, qtyText: v } : d))}
                keyboardType="decimal-pad"
                style={styles.input}
                accessibilityLabel={t('dispatch.qtyLabel')}
              />
            </View>
            <ScanField
              label={t('dispatch.fromBinLabel')}
              help={t('dispatch.fromBinHelp')}
              onSubmit={scanFromBin}
            />
            {draft.fromBinCode ? <Text style={styles.help}>{draft.fromBinCode}</Text> : null}
            <View style={styles.row}>
              <BigButton label={t('common.cancel')} variant="secondary" onPress={() => setDraft(null)} />
              <BigButton label={t('receive.addLine')} onPress={addCurrentLine} disabled={!canAddPickLine(draft)} />
            </View>
          </>
        ) : (
          <>
            {/* Sin despacho abierto todavía (draft es null aquí): nada que perder, "Volver" sale directo a Inicio.
                2026-10-01 (Luis): abajo, pegado al borde inferior (`marginTop: auto`). */}
            <ScanField label={t('dispatch.scanProductLabel')} help={t('dispatch.scanProductHelp')} error={scanError} notice={notice} onSubmit={scanProduct} />
            <View style={styles.bottom}>
              <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
            </View>
          </>
        )}
      </ScrollView>
    )
  }

  // Empacando: resolviendo posiciones y eligiendo consignatario.
  if (packing.name === 'consignee' || packing.name === 'error') {
    return (
      <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
        <Text style={styles.title}>{t('dispatch.chooseConsignee')}</Text>
        <View style={styles.field}>
          <Text style={styles.label}>{t('dispatch.piecesLabel')}</Text>
          <TextInput value={pieces} onChangeText={setPieces} keyboardType="number-pad" style={styles.input} accessibilityLabel={t('dispatch.piecesLabel')} />
        </View>
        {packing.name === 'error' ? (
          <Text style={styles.error}>{packing.message}</Text>
        ) : busy ? (
          <ActivityIndicator color={colors.brand} />
        ) : consignees && consignees.length === 0 ? (
          <Text style={styles.help}>{t('dispatch.noConsignees')}</Text>
        ) : (
          (consignees ?? []).map((c) => <BigButton key={c.publicId} label={c.label} onPress={() => confirmPack(c)} />)
        )}
        <BigButton label={t('common.back')} variant="secondary" onPress={() => setPacking({ name: 'scan' })} disabled={busy} />
      </ScrollView>
    )
  }

  // Despacho abierto: viendo lo recolectado. ScrollView (no View, mismo motivo que receive.tsx paso 2): el
  // ScanField se reenfoca tras cada línea y, con el teclado abierto, tapaba Empacar/Cancelar.
  return (
    <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
      <Text style={styles.title}>{openPick.clientName}</Text>
      <ScanField label={t('dispatch.scanProductLabel')} help={t('dispatch.scanProductHelp')} error={scanError} notice={notice} onSubmit={scanProduct} />
      <Text style={styles.label}>{t('dispatch.linesTitle')}</Text>
      <LineList
        items={openPick.lineRows.map((l) => ({ id: l.id, title: t('dispatch.lineQty', { qty: l.quantity, sku: l.sku }), subtitle: l.fromBinCode }))}
        onRemove={(id) => {
          removeLocalPickLine(Number(id))
          refresh()
        }}
        removeLabel={t('common.remove')}
        emptyLabel={t('dispatch.linesTitle')}
      />
      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      <BigButton label={t('dispatch.packButton')} onPress={startPacking} disabled={openPick.lineRows.length === 0 || busy} />
      <Text style={styles.help}>{t('dispatch.packHelp')}</Text>
      <BigButton label={t('dispatch.cancelDispatch')} variant="danger" onPress={cancelDispatch} />
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
  field: { gap: spacing.xs },
  // Pega el botón al borde inferior cuando el contenido es corto (el contenedor del ScrollView crece: flexGrow 1).
  bottom: { marginTop: 'auto' },
  row: { flexDirection: 'row', gap: spacing.md },
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
})
