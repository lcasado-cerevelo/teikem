import { useState } from 'react'
import { ActivityIndicator, Alert, Pressable, StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { ApiError, isNetworkError } from '../kernel/api/client'
import { useT } from '../kernel/i18n/useT'
import { findBinByCode } from '../kernel/warehouse/binLookup'
import { findProductByCode } from '../kernel/warehouse/productLookup'
import { useActiveWarehouse } from '../kernel/warehouse/activeWarehouse'
import { BigButton } from '../kernel/ui/BigButton'
import { KeyboardInput } from '../kernel/ui/KeyboardInput'
import { KeyboardScreen } from '../kernel/ui/KeyboardScreen'
import { ScanField } from '../kernel/ui/ScanField'
import { ScanMessage } from '../kernel/ui/ScanMessage'
import { colors, fontSize, radius, spacing } from '../kernel/ui/theme'
import { vibrateError, vibrateOk } from '../kernel/ui/feedback'
import { findReceiptByNumber, reportDamage } from '../features/damage/damageApi'
import {
  buildDamageRequest,
  DAMAGE_CAUSES,
  damageBlock,
  damageStep,
  defaultCause,
  EMPTY_DAMAGE,
  type DamageDisposition,
  type DamageDraft,
  type DamageOrigin,
} from '../features/damage/damageLogic'

/**
 * 2026-10-08 — Daño: algo que se dañó en el almacén (se escanea la posición donde está) o que llegó dañado en un recibo (se teclea o escanea el número
 * del recibo). Se escanea el producto, se escribe la cantidad (y el lote si el producto lleva lote), se elige la causa (informativa) y qué hacer: mandarlo
 * a cuarentena o desecharlo de una vez. Necesita señal: mueve inventario en el servidor, sin cola (como Consultar).
 */
export default function DamageScreen() {
  const { t } = useT()
  const router = useRouter()
  const warehousePublicId = useActiveWarehouse().publicId
  const [draft, setDraft] = useState<DamageDraft>(EMPTY_DAMAGE)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (!warehousePublicId) {
    return (
      <View style={styles.fill}>
        <Text style={styles.error}>{t('errors.generic')}</Text>
      </View>
    )
  }

  const step = damageStep(draft)
  const block = damageBlock(draft)

  function pickOrigin(origin: DamageOrigin) {
    setNotice(null)
    setError(null)
    setDraft({ ...EMPTY_DAMAGE, origin, cause: defaultCause(origin) })
  }

  function fail(message: string) {
    setError(message)
    vibrateError()
  }

  function failure(err: unknown) {
    fail(isNetworkError(err) ? t('damage.needSignal') : err instanceof ApiError ? err.title : t('errors.generic'))
  }

  async function scanSource(code: string) {
    setError(null)
    setBusy(true)
    try {
      if (draft.origin === 'WAREHOUSE') {
        const bin = await findBinByCode(warehousePublicId!, code)
        if (!bin) return fail(t('damage.binNotFound'))
        setDraft((d) => ({ ...d, bin: { id: bin.id, code: bin.code } }))
      } else {
        const receipt = await findReceiptByNumber(warehousePublicId!, code)
        if (!receipt) return fail(t('damage.receiptNotFound'))
        setDraft((d) => ({ ...d, receipt }))
      }
      vibrateOk()
    } catch (err) {
      failure(err)
    } finally {
      setBusy(false)
    }
  }

  function scanProduct(code: string) {
    setError(null)
    const product = findProductByCode(code)
    if (!product) return fail(t('damage.productNotFound'))
    if (product.trackingTypeCode === 'SERIAL') return fail(t('damage.serialNotSupported'))
    setDraft((d) => ({ ...d, product: { publicId: product.publicId, sku: product.sku, name: product.name, trackingTypeCode: product.trackingTypeCode } }))
    vibrateOk()
  }

  async function submit(disposition: DamageDisposition) {
    if (block) {
      fail(t(block === 'qty' ? 'damage.qtyInvalid' : 'damage.lotRequired'))
      return
    }
    setError(null)
    setBusy(true)
    try {
      const dto = await reportDamage(buildDamageRequest(warehousePublicId!, draft, disposition))
      setDraft(EMPTY_DAMAGE)
      setNotice(t(disposition === 'QUARANTINE' ? 'damage.reportedQuarantine' : 'damage.reportedDiscard', { code: dto.code ?? '' }))
      vibrateOk()
    } catch (err) {
      failure(err)
    } finally {
      setBusy(false)
    }
  }

  function confirmDiscard() {
    if (block) {
      fail(t(block === 'qty' ? 'damage.qtyInvalid' : 'damage.lotRequired'))
      return
    }
    Alert.alert(t('damage.discardConfirmTitle'), t('damage.discardConfirmBody', { qty: draft.qtyText.trim(), sku: draft.product?.sku ?? '' }), [
      { text: t('common.no'), style: 'cancel' },
      { text: t('damage.toDiscard'), style: 'destructive', onPress: () => void submit('DISCARD') },
    ])
  }

  const restart = () => {
    setDraft(EMPTY_DAMAGE)
    setError(null)
  }

  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('damage.title')}</Text>
      <ScanMessage message={notice} tone="ok" />

      {step === 'origin' ? (
        <>
          <Text style={styles.label}>{t('damage.chooseOrigin')}</Text>
          <BigButton label={t('damage.originWarehouse')} onPress={() => pickOrigin('WAREHOUSE')} testID="damage-origin-warehouse" />
          <Text style={styles.help}>{t('damage.originWarehouseHelp')}</Text>
          <BigButton label={t('damage.originReceipt')} variant="secondary" onPress={() => pickOrigin('RECEIPT')} testID="damage-origin-receipt" />
          <Text style={styles.help}>{t('damage.originReceiptHelp')}</Text>
        </>
      ) : null}

      {step === 'source' ? (
        <ScanField
          key={draft.origin}
          label={t(draft.origin === 'WAREHOUSE' ? 'damage.scanBin' : 'damage.scanReceipt')}
          help={t(draft.origin === 'WAREHOUSE' ? 'damage.scanBinHelp' : 'damage.scanReceiptHelp')}
          error={error}
          onSubmit={(code) => void scanSource(code)}
          pick={draft.origin === 'WAREHOUSE' ? 'bin' : undefined}
          testID="damage-source"
        />
      ) : null}

      {step === 'product' ? (
        <>
          <Text style={styles.summary}>{draft.origin === 'WAREHOUSE' ? t('damage.inBin', { bin: draft.bin?.code ?? '' }) : t('damage.inReceipt', { number: draft.receipt?.number ?? '' })}</Text>
          <ScanField label={t('damage.scanProduct')} error={error} onSubmit={scanProduct} pick="product" testID="damage-product" />
        </>
      ) : null}

      {step === 'details' ? (
        <>
          <Text style={styles.summary}>{draft.origin === 'WAREHOUSE' ? t('damage.inBin', { bin: draft.bin?.code ?? '' }) : t('damage.inReceipt', { number: draft.receipt?.number ?? '' })}</Text>
          <Text style={styles.product}>{`${draft.product?.sku ?? ''} · ${draft.product?.name ?? ''}`}</Text>
          <View style={styles.field}>
            <Text style={styles.label}>{t('damage.qtyLabel')}</Text>
            <KeyboardInput
              autoFocus
              value={draft.qtyText}
              onChangeText={(v) => setDraft((d) => ({ ...d, qtyText: v }))}
              keyboardType="decimal-pad"
              style={styles.input}
              accessibilityLabel={t('damage.qtyLabel')}
              testID="damage-qty"
            />
          </View>
          {draft.product?.trackingTypeCode === 'LOT' ? (
            <View style={styles.field}>
              <Text style={styles.label}>{t('damage.lotLabel')}</Text>
              <KeyboardInput value={draft.lot} onChangeText={(v) => setDraft((d) => ({ ...d, lot: v }))} autoCapitalize="characters" style={styles.input} accessibilityLabel={t('damage.lotLabel')} />
            </View>
          ) : null}
          <Text style={styles.label}>{t('damage.causeLabel')}</Text>
          <View style={styles.causes}>
            {DAMAGE_CAUSES.map((c) => (
              <Pressable
                key={c.code}
                accessibilityRole="radio"
                accessibilityState={{ selected: draft.cause === c.code }}
                onPress={() => setDraft((d) => ({ ...d, cause: c.code }))}
                style={[styles.cause, draft.cause === c.code && styles.causeOn]}
              >
                <Text style={styles.causeText}>{t(c.key)}</Text>
              </Pressable>
            ))}
          </View>
          <ScanMessage message={error} tone="error" />
          {busy ? <ActivityIndicator color={colors.brand} /> : null}
          <BigButton label={t('damage.toQuarantine')} onPress={() => void submit('QUARANTINE')} disabled={busy} testID="damage-quarantine" />
          <Text style={styles.help}>{t('damage.quarantineHelp')}</Text>
          <BigButton label={t('damage.toDiscard')} variant="danger" onPress={confirmDiscard} disabled={busy} testID="damage-discard" />
          <Text style={styles.help}>{t('damage.discardHelp')}</Text>
        </>
      ) : null}

      {step !== 'origin' ? <BigButton label={t('damage.restart')} variant="secondary" onPress={restart} disabled={busy} /> : null}
      <View style={styles.bottom}>
        <BigButton label={t('common.back')} variant="secondary" onPress={() => router.replace('/home')} disabled={busy} />
      </View>
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
  summary: { color: colors.muted, fontSize: fontSize.label, fontWeight: '600' },
  product: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '700' },
  field: { gap: spacing.xs },
  bottom: { marginTop: 'auto' },
  input: { minHeight: 56, borderWidth: 2, borderColor: colors.line, borderRadius: 12, paddingHorizontal: spacing.md, fontSize: 20, color: colors.text, backgroundColor: colors.panelAlt },
  causes: { gap: spacing.sm },
  cause: { minHeight: 56, justifyContent: 'center', paddingHorizontal: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  causeOn: { borderColor: colors.brand, backgroundColor: colors.brandDark },
  causeText: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
})
