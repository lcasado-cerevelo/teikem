import { useEffect, useMemo, useState } from 'react'
import { ActivityIndicator, Alert, Pressable, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError, apiErrorMessage, isNetworkError } from '../kernel/api/client'
import { useSession } from '../kernel/auth/useSession'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { getKv, KvKeys, setKv } from '../kernel/db/kv'
import { findBinByCode } from '../kernel/warehouse/binLookup'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { useT } from '../kernel/i18n/useT'
import { runSync } from '../kernel/sync/engine'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { ScanField, type ScanPrefill } from '../kernel/ui/ScanField'
import { ScanMessage } from '../kernel/ui/ScanMessage'
import { colors, fontSize, radius, spacing, touchTarget } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import {
  cancelCountOnline,
  enqueueFinishCount,
  fetchExpectedLines,
  startCountOnline,
  startOpenCountOnline,
} from '../features/count/countApi'
import { hasAnyCountedQty, matchExpectedLine, parseQty, productCountBlocker, remainingExpectedLines, type ExpectedLine } from '../features/count/countLogic'
import {
  addExtraLine,
  captureExpectedLine,
  discardLocalCount,
  getCapturedLines,
  getOpenCount,
  getProductCountRows,
  removeLocalCountLine,
  updateLocalCountLineQty,
  startLocalCount,
  startLocalOpenCount,
  toCapturedEntries,
  toProductEntries,
  type CountMode,
  type CountProduct,
} from '../features/count/localCount'
import { OpenCountView } from '../features/count/OpenCountView'
import { ProductCountView } from '../features/count/ProductCountView'
import { QuantityField } from '../features/count/QuantityField'
import { checkCountLine, checkStateOf, forgetChecks, isClosedState, isCountOff, markCountOff, rememberCheck, revealMessage } from '../features/count/lineCheck'

type Draft = { line: ExpectedLine | null; productPublicId: string; sku: string; productName: string; qtyText: string }
/** Corrección de la cantidad de una línea ya contada (sin volver a escanear). */
type Edit = { id: number; sku: string; productName: string; systemQty: number | null; qtyText: string }

/** Pantalla 6 (docs/mobile/app-almacen-plan.md §2): escanear la posición reclama el conteo en línea (posición
 *  compartida, igual que Acomodar); de ahí en adelante capturar lo encontrado y terminar van por la cola de salida.
 *  Un conteo a la vez por aparato (docs/lote8A-app-decisiones.md).
 *  Lote A4: dos caminos, "Por posición" (como antes) y "Por producto" (docs/conteo-por-producto-diseno.md): se escanea el
 *  producto, se abre el conteo en línea y se lista una fila por posición (y lote) con su espacio de cantidad
 *  (features/count/ProductCountView.tsx). */
export default function CountScreen() {
  const { t } = useT()
  const router = useRouter()
  const { device } = useSession()
  const activeWarehouse = useActiveWarehouse()
  const warehousePublicId = activeWarehouse.publicId
  const [tick, setTick] = useState(0)
  const [binError, setBinError] = useState<string | null>(null)
  const [scanError, setScanError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [expectedLines, setExpectedLines] = useState<ExpectedLine[] | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [edit, setEdit] = useState<Edit | null>(null)
  const [retryTick, setRetryTick] = useState(0)
  // docs/mobile/mejoras-ux-zebra.md §3: tocar un producto de "Lo que se espera aquí" lo pone en el campo del producto
  // (sin enviarlo: se confirma con Aceptar). Se limpia al enviar, para que no vuelva a aparecer al regresar a este paso.
  const [prefill, setPrefill] = useState<ScanPrefill | null>(null)
  // Lote A4: forma de contar elegida (se recuerda en el aparato; la primera vez, por posición).
  const [entryMode, setEntryMode] = useState<CountMode>(() => (getKv(KvKeys.countEntryMode) === 'PRODUCT' ? 'PRODUCT' : 'BIN'))
  const [productError, setProductError] = useState<string | null>(null)
  // Lote 24: el producto cuyo escaneo abrió el conteo abierto; la vista lo procesa al montar
  const [pendingProduct, setPendingProduct] = useState<CountProduct | null>(null)
  // Tarea 25 (conteo informado al capturar): resultado de verificar la última cantidad aceptada contra lo esperado (se quita al escanear otro producto).
  const [reveal, setReveal] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null)

  // tick fuerza releer la base local tras cada mutación; getOpenCount() no usa tick.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const openCount = useMemo(() => getOpenCount(), [tick])
  const refresh = () => setTick((n) => n + 1)

  // El conteo en curso solo guarda el id localmente; si se cerró y reabrió la app hay que volver a pedir las líneas
  // esperadas (ya de todas formas necesita señal para terminar). expectedLines === null es la señal de "cargando".
  useEffect(() => {
    // el conteo por producto guarda todas sus líneas en la base local al abrirse: no se vuelven a pedir
    if (!openCount || openCount.mode !== 'BIN' || expectedLines !== null) return
    let cancelled = false
    fetchExpectedLines(openCount.countId)
      .then((lines) => {
        if (!cancelled) setExpectedLines(lines)
      })
      .catch((err: unknown) => {
        if (!cancelled) setScanError(err instanceof ApiError ? err.title : t('errors.network'))
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openCount?.id, retryTick])

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  async function scanBin(code: string) {
    setBinError(null)
    setBusy(true)
    try {
      const bin = await findBinByCode(warehousePublicId!, code)
      if (!bin) {
        // Lote A4: si lo escaneado es un producto, se dice cómo contarlo así (en vez de solo "no hay posición")
        setBinError(findProductByCode(code) ? t('count.binLooksLikeProduct') : t('count.binNotFound'))
        vibrateError()
        return
      }
      const started = await startCountOnline(warehousePublicId!, bin.id)
      startLocalCount(warehousePublicId!, bin, { countId: started.countId, isBlind: started.isBlind })
      setExpectedLines(started.expectedLines)
      vibrateOk()
      refresh()
    } catch (err) {
      setBinError(err instanceof ApiError ? err.title : t('count.startError'))
      vibrateError()
    } finally {
      setBusy(false)
    }
  }

  function chooseMode(mode: CountMode) {
    setEntryMode(mode)
    setKv(KvKeys.countEntryMode, mode)
    setBinError(null)
    setProductError(null)
  }

  /** Lote 24 — "Por producto": el primer producto escaneado abre UN conteo (vacío, en línea) y de ahí en adelante se van agregando
   *  productos a ese mismo conteo (features/count/OpenCountView.tsx). */
  async function scanProductToCount(code: string) {
    setProductError(null)
    const product = findProductByCode(code)
    if (!product) {
      setProductError(t('count.productNotFound'))
      vibrateError()
      return
    }
    if (productCountBlocker(product) === 'serial') {
      // no se crea ningún conteo: no se podría terminar desde la app (no hay captura de series)
      setProductError(t('count.serialNotSupported'))
      vibrateError()
      return
    }
    setBusy(true)
    try {
      const started = await startOpenCountOnline(warehousePublicId!)
      startLocalOpenCount(warehousePublicId!, started)
      setPendingProduct({ publicId: product.publicId, sku: product.sku, name: product.name, trackingTypeCode: product.trackingTypeCode })
      vibrateOk()
      refresh()
    } catch (err) {
      if (isNetworkError(err)) setProductError(t('count.productStartError'))
      else if (err instanceof ApiError) setProductError(apiErrorMessage(err))
      else setProductError(t('count.productStartError'))
      vibrateError()
    } finally {
      setBusy(false)
    }
  }

  function scanProduct(code: string) {
    setPrefill(null)
    setReveal(null)
    if (!openCount || expectedLines === null) return
    const product = findProductByCode(code)
    if (!product) {
      setScanError(t('count.productNotFound'))
      vibrateError()
      return
    }
    const capturedLineIds = new Set(getCapturedLines(openCount.id).map((r) => r.lineId).filter((id): id is number => id != null))
    const line = matchExpectedLine(expectedLines, capturedLineIds, product.publicId)
    setScanError(null)
    setDraft({ line, productPublicId: product.publicId, sku: product.sku, productName: product.name, qtyText: '' })
  }

  async function addFound() {
    if (!draft || !openCount) return
    const qty = parseQty(draft.qtyText)
    if (qty === null) return
    // Conteo informado al capturar: con una línea esperada del servidor y un contador (conteo a ciegas), la cantidad aceptada se verifica con señal;
    // sin señal o sin permiso se captura como siempre. RECOUNT pide volver a contar sin decir lo esperado y no captura todavía.
    if (draft.line && draft.line.lineId > 0 && openCount.isBlind && !isCountOff(openCount.countId)) {
      setBusy(true)
      const outcome = await checkCountLine(openCount.countId, draft.line.lineId, qty)
      setBusy(false)
      if (outcome.kind === 'off') markCountOff(openCount.countId)
      else if (outcome.kind === 'rejected') {
        setReveal({ tone: 'error', text: outcome.message })
        vibrateError()
        return
      } else if (outcome.kind === 'result') {
        rememberCheck(draft.line.lineId, outcome.result.state)
        const m = revealMessage(outcome.result)
        const text = t(m.key, m.params)
        if (outcome.result.state === 'RECOUNT') {
          setReveal({ tone: 'error', text })
          setDraft({ ...draft, qtyText: '' })
          vibrateError()
          return
        }
        captureExpectedLine(openCount.id, draft.line, qty)
        setReveal({ tone: m.tone === 'ok' ? 'ok' : 'error', text })
        setDraft(null)
        if (m.tone === 'ok') vibrateOk()
        else vibrateError()
        refresh()
        return
      }
    }
    if (draft.line) {
      captureExpectedLine(openCount.id, draft.line, qty)
    } else {
      addExtraLine(openCount.id, { publicId: draft.productPublicId, sku: draft.sku, name: draft.productName }, qty)
    }
    setDraft(null)
    vibrateOk()
    refresh()
  }

  function cancelCount() {
    Alert.alert(t('count.cancelConfirmTitle'), t('count.cancelConfirmBody'), [
      { text: t('common.no'), style: 'cancel' },
      {
        text: t('count.cancelCount'),
        style: 'destructive',
        onPress: async () => {
          if (!openCount) return
          setBusy(true)
          try {
            await cancelCountOnline(openCount.countId)
            discardLocalCount()
            forgetChecks()
            setDraft(null)
            setExpectedLines(null)
            refresh()
            router.replace('/home')
          } catch (err) {
            setScanError(err instanceof ApiError ? err.title : t('count.cancelError'))
          } finally {
            setBusy(false)
          }
        },
      },
    ])
  }

  function finishBin() {
    if (!openCount) return
    const captured = toCapturedEntries(getCapturedLines(openCount.id))
    if (captured.length === 0) return
    enqueueFinishCount(openCount.countId, captured)
    discardLocalCount()
    forgetChecks()
    void runSync()
    router.replace('/home')
  }

  /** Conteo por producto: todas las filas (las en blanco como 0) viajan en un lote, seguido del cierre (cola de salida). */
  function finishProduct() {
    if (!openCount) return
    const rows = getProductCountRows(openCount.id)
    // la vista ya avisa; esto evita encolar un conteo vacío o todo en blanco (decisión del dueño 4) si se llegara aquí igual
    if (rows.length === 0 || !hasAnyCountedQty(rows.map((r) => r.countedQty))) return
    enqueueFinishCount(openCount.countId, toProductEntries(rows))
    discardLocalCount()
    forgetChecks()
    vibrateOk()
    void runSync()
    router.replace('/home')
  }

  // Sin conteo abierto: elegir cómo contar y escanear la posición o el producto. Nada que perder aquí, así que "Volver" sale
  // directo a Inicio.
  if (!openCount) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{t('count.title')}</Text>
        <Text style={styles.label}>{t('count.modeLabel')}</Text>
        <View style={styles.modes} accessibilityRole="radiogroup">
          {(['BIN', 'PRODUCT'] as const).map((mode) => {
            const selected = entryMode === mode
            return (
              <Pressable
                key={mode}
                accessibilityRole="radio"
                accessibilityState={{ selected, checked: selected }}
                accessibilityLabel={t(mode === 'BIN' ? 'count.modeBin' : 'count.modeProduct')}
                onPress={() => chooseMode(mode)}
                disabled={busy}
                style={[styles.mode, selected && styles.modeOn]}
              >
                <Text style={[styles.modeLabel, selected && styles.modeLabelOn]}>{t(mode === 'BIN' ? 'count.modeBin' : 'count.modeProduct')}</Text>
              </Pressable>
            )
          })}
        </View>
        {entryMode === 'BIN' ? (
          <ScanField key="bin" label={t('count.scanBinLabel')} error={binError} onSubmit={scanBin} pick="bin" />
        ) : (
          <>
            <ScanField
              key="product"
              label={t('count.scanProductToCountLabel')}
              help={t('count.scanProductToCountHelp')}
              error={productError}
              onSubmit={(code) => void scanProductToCount(code)}
              pick="product"
            />
            {productError ? <BigButton label={t('count.switchToBin')} variant="secondary" onPress={() => chooseMode('BIN')} /> : null}
          </>
        )}
        {busy ? <ActivityIndicator color={colors.brand} /> : null}
        {/* 2026-10-01 (Luis): al final de todo lo que hay en pantalla */}
        <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} disabled={busy} />
      </KeyboardScreen>
    )
  }

  // Lote 24: conteo abierto con varios productos (recién abierto o retomado tras cerrar la app: sus líneas están en la base local).
  if (openCount.mode === 'OPEN') {
    return (
      <OpenCountView
        openCount={openCount}
        busy={busy}
        onConfirm={finishProduct}
        onCancelCount={cancelCount}
        initialProduct={pendingProduct}
        error={scanError}
      />
    )
  }

  // Conteo por producto de antes del Lote 24 (un producto en todas sus posiciones), retomado desde la base local.
  if (openCount.mode === 'PRODUCT' && openCount.product) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <ProductCountView openCount={openCount} busy={busy} onConfirm={finishProduct} onCancelCount={cancelCount} error={scanError} />
        {busy ? <ActivityIndicator color={colors.brand} /> : null}
      </KeyboardScreen>
    )
  }

  // Conteo abierto pero todavía sin las líneas esperadas en memoria (recién reabrió la app).
  if (expectedLines === null) {
    return (
      <View style={styles.fill}>
        <Text style={styles.title}>{openCount.binCode}</Text>
        {scanError ? (
          <>
            <Text style={styles.error}>{scanError}</Text>
            <BigButton
              label={t('common.retry')}
              onPress={() => {
                setScanError(null)
                setRetryTick((n) => n + 1)
              }}
            />
          </>
        ) : (
          <ActivityIndicator size="large" color={colors.brand} />
        )}
      </View>
    )
  }

  // Capturando la cantidad encontrada de un producto ya escaneado.
  if (draft) {
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{draft.productName}</Text>
        <Text style={styles.help}>{draft.sku}</Text>
        <ScanMessage message={reveal?.text} tone={reveal?.tone ?? 'error'} />
        {draft.line?.systemQty != null ? <Text style={styles.help}>{t('count.expectedQtyLabel', { qty: draft.line.systemQty })}</Text> : null}
        <View style={styles.field}>
          <Text style={styles.label}>{t('count.foundQtyLabel')}</Text>
          <QuantityField
            value={draft.qtyText}
            onChangeText={(v) => setDraft((d) => (d ? { ...d, qtyText: v } : d))}
            style={styles.input}
            accessibilityLabel={t('count.foundQtyLabel')}
            autoFocus
          />
        </View>
        <View style={styles.row}>
          <BigButton
            label={t('common.cancel')}
            variant="secondary"
            onPress={() => {
              setReveal(null)
              setDraft(null)
            }}
          />
          <BigButton label={t('count.addFound')} onPress={() => void addFound()} disabled={parseQty(draft.qtyText) === null || busy} />
        </View>
      </KeyboardScreen>
    )
  }

  const capturedRows = getCapturedLines(openCount.id)

  // Corrigiendo la cantidad de una línea ya contada: paso aparte (como capturar), así el lector no escribe en el escaneo.
  if (edit) {
    const qty = parseQty(edit.qtyText)
    return (
      <KeyboardScreen contentContainerStyle={styles.fill}>
        <Text style={styles.title}>{edit.productName}</Text>
        <Text style={styles.help}>{edit.sku}</Text>
        {!openCount.isBlind && edit.systemQty != null ? <Text style={styles.help}>{t('count.expectedQtyLabel', { qty: edit.systemQty })}</Text> : null}
        <View style={styles.field}>
          <Text style={styles.label}>{t('count.editTitle')}</Text>
          <QuantityField
            value={edit.qtyText}
            onChangeText={(v) => setEdit((e) => (e ? { ...e, qtyText: v } : e))}
            style={styles.input}
            accessibilityLabel={t('count.editTitle')}
            autoFocus
            selectTextOnFocus
          />
        </View>
        <View style={styles.row}>
          <BigButton label={t('common.cancel')} variant="secondary" onPress={() => setEdit(null)} />
          <BigButton
            label={t('common.save')}
            disabled={qty === null}
            onPress={() => {
              if (qty === null) return
              updateLocalCountLineQty(edit.id, qty)
              setEdit(null)
              refresh()
            }}
          />
        </View>
      </KeyboardScreen>
    )
  }
  const capturedLineIds = new Set(capturedRows.map((r) => r.lineId).filter((id): id is number => id != null))
  const remaining = remainingExpectedLines(expectedLines, capturedLineIds)

  // Conteo abierto: escaneando productos y viendo lo ya capturado.
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{openCount.binCode}</Text>
      {openCount.isBlind ? <Text style={styles.help}>{t('count.blindNotice')}</Text> : null}
      <ScanField label={t('count.scanProductLabel')} error={scanError} onSubmit={scanProduct} prefill={prefill} pick="product" />
      <ScanMessage message={reveal?.text} tone={reveal?.tone ?? 'error'} />

      {/* lo contado y los botones van justo debajo del escaneo (pedido del dueño): con muchas líneas esperadas quedaban
          al final de la lista y había que desplazarse para terminar o cancelar */}
      <Text style={styles.label}>{t('count.foundQtyLabel')}</Text>
      <LineList
        items={capturedRows.map((r) => ({ id: r.id, title: t('count.foundLineTitle', { name: r.productName, qty: r.countedQty }), subtitle: r.sku }))}
        onRemove={(id) => {
          // una línea ya verificada (coincidió o ya se recontó) no se quita ni se cambia: el servidor solo aceptaría la cifra verificada
          if (isClosedState(checkStateOf(capturedRows.find((r) => r.id === Number(id))?.lineId))) {
            setReveal({ tone: 'error', text: t('count.lineChecked') })
            return
          }
          removeLocalCountLine(Number(id))
          refresh()
        }}
        removeLabel={t('common.remove')}
        onEdit={(id) => {
          const row = capturedRows.find((r) => r.id === Number(id))
          if (row && isClosedState(checkStateOf(row.lineId))) {
            setReveal({ tone: 'error', text: t('count.lineChecked') })
            return
          }
          if (row) setEdit({ id: row.id, sku: row.sku, productName: row.productName, systemQty: row.systemQty, qtyText: String(row.countedQty) })
        }}
        editLabel={t('common.edit')}
        emptyLabel={t('count.emptyExpected')}
      />

      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      <BigButton label={t('count.finishBin')} onPress={finishBin} disabled={capturedRows.length === 0 || busy} />
      <Text style={styles.help}>{t('count.finishHelp')}</Text>
      <BigButton label={t('count.cancelCount')} variant="danger" onPress={cancelCount} disabled={busy} />

      <Text style={styles.label}>{t('count.expectedTitle')}</Text>
      {remaining.length === 0 ? (
        <Text style={styles.help}>{t('count.emptyExpected')}</Text>
      ) : (
        <>
          <Text style={styles.help}>{t('count.pickHint')}</Text>
          <LineList
            items={remaining.map((l) => ({ id: l.lineId, title: l.productName, subtitle: l.sku }))}
            removeLabel={t('common.remove')}
            onPressItem={(id) => {
              const line = remaining.find((l) => l.lineId === Number(id))
              if (line) setPrefill((p) => ({ value: line.sku, seq: (p?.seq ?? 0) + 1 }))
            }}
            pressLabel={(item) => t('count.useProduct', { sku: item.subtitle ?? item.title })}
          />
        </>
      )}
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
  field: { gap: spacing.xs },
  row: { flexDirection: 'row', gap: spacing.md },
  // dos opciones del mismo ancho: caben en 360 px
  modes: { flexDirection: 'row', gap: spacing.sm },
  mode: {
    flex: 1,
    minHeight: touchTarget,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.line,
    backgroundColor: colors.panelAlt,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.sm,
  },
  modeOn: { borderColor: colors.brand, backgroundColor: colors.brand },
  modeLabel: { color: colors.muted, fontSize: fontSize.label, fontWeight: '700', textAlign: 'center' },
  modeLabelOn: { color: colors.text },
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
