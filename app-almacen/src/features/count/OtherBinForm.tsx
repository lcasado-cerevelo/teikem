// Lote A4 — "Otra posición" del conteo por producto (docs/conteo-por-producto-diseno.md §6, opción A aprobada): lo hallado
// donde el sistema no tenía nada. Se elige la zona y se escriben los datos de la posición (código, o pasillo/rack/nivel/
// posición); si el producto lleva lote, el número de lote (y, opcional, su vencimiento). La posición se crea en el servidor
// (POST /cycle-counts/{id}/bins, necesita señal) y queda "pendiente de revisión" hasta que el supervisor la confirma en la web.
import { useEffect, useState } from 'react'
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native'

import { ApiError, apiErrorMessage, isNetworkError } from '../../kernel/api/client'
import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n/useT'
import { useScanner } from '../../kernel/scanner/useScanner'
import { BigButton } from '../../kernel/ui/BigButton'
import { ScanMessage } from '../../kernel/ui/ScanMessage'
import { colors, fontSize, radius, spacing, touchTarget } from '../../kernel/ui/theme'
import { findBinByCode } from '../../kernel/warehouse/binLookup'
import { createProvisionalBin, fetchZones, findLocalBin, type CountZone, type CreatedBin } from './countApi'
import { findListedRow, resolveBinCode, type ProductRowLike } from './countLogic'
import type { CountProduct } from './localCount'
import { KeyboardInput } from '../../kernel/ui/KeyboardInput'

export interface OtherBinLot {
  number: string
  expiryDate: string | null
}

export interface OtherBinFormProps {
  warehousePublicId: string
  countId: number
  product: CountProduct
  rows: readonly ProductRowLike[]
  /** La posición quedó lista para la lista: `existing` = ya existía (no se creó una provisional). */
  onAdded: (bin: CreatedBin, lot: OtherBinLot | null, existing: boolean) => void
  onCancel: () => void
}

export function OtherBinForm({ warehousePublicId, countId, product, rows, onAdded, onCancel }: OtherBinFormProps) {
  const { t } = useT()
  const f = useFormat()
  const [zones, setZones] = useState<CountZone[] | null>(null)
  const [zonesFromLocal, setZonesFromLocal] = useState(false)
  const [zoneId, setZoneId] = useState<number | null>(null)
  const [code, setCode] = useState('')
  const [aisle, setAisle] = useState('')
  const [rack, setRack] = useState('')
  const [level, setLevel] = useState('')
  const [position, setPosition] = useState('')
  const [lot, setLot] = useState('')
  const [expiry, setExpiry] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [existing, setExisting] = useState<CreatedBin | null>(null)
  const [busy, setBusy] = useState(false)

  const needsLot = product.trackingTypeCode === 'LOT'

  useEffect(() => {
    let cancelled = false
    fetchZones(warehousePublicId)
      .then((result) => {
        if (cancelled) return
        setZones(result.zones)
        setZonesFromLocal(result.fromLocal)
        // una sola zona: ya elegida
        if (result.zones.length === 1) setZoneId(result.zones[0].id)
      })
      .catch(() => {
        if (!cancelled) setZones([])
      })
    return () => {
      cancelled = true
    }
  }, [warehousePublicId])

  // Una posición con etiqueta: el lector escribe su código (no la crea solo: falta la zona y, si aplica, el lote).
  useScanner((scanned) => {
    setCode(scanned.trim())
    setError(null)
    setExisting(null)
  })

  const binCode = resolveBinCode({ code, aisle, rack, level, position })
  const expiryIso = expiry.trim() ? f.parseDate(expiry) : ''
  const lotData: OtherBinLot | null = needsLot ? { number: lot.trim(), expiryDate: expiryIso || null } : null
  const canSubmit = zoneId !== null && binCode !== null && (!needsLot || lot.trim().length > 0) && expiryIso !== null && !busy

  async function submit() {
    if (!canSubmit || zoneId === null || binCode === null) return
    setError(null)
    setExisting(null)
    if (findListedRow(rows, binCode, lotData?.number ?? null)) {
      setError(t('count.binAlreadyListed', { bin: binCode }))
      return
    }
    // La posición ya existe en el aparato (sincronizada): se usa tal cual, sin crear nada (no necesita señal).
    const local = findLocalBin(warehousePublicId, binCode)
    if (local) {
      onAdded(local, lotData, true)
      return
    }
    setBusy(true)
    try {
      const bin = await createProvisionalBin(countId, { zoneId, code, aisle, rack, level, position })
      onAdded(bin, lotData, false)
    } catch (err) {
      if (isNetworkError(err)) setError(t('count.otherBinNetwork'))
      else if (err instanceof ApiError) {
        setError(apiErrorMessage(err))
        // 409 "Ya existe una posición con ese código": se ofrece usar la que ya existe (el aparato aún no la tenía).
        if (err.status === 409) {
          const found = await findBinByCode(warehousePublicId, binCode).catch(() => null)
          if (found) setExisting({ id: found.id, code: found.code, isProvisional: false })
        }
      } else setError(t('errors.generic'))
    } finally {
      setBusy(false)
    }
  }

  const clearMessages = () => {
    setError(null)
    setExisting(null)
  }
  /** Cambiar un dato borra el aviso anterior (ya no corresponde a lo escrito). */
  const edit = (setter: (v: string) => void) => (v: string) => {
    setter(v)
    clearMessages()
  }

  return (
    <View style={styles.wrap}>
      <Text style={styles.title}>{t('count.otherBin')}</Text>
      <Text style={styles.help}>{t('count.otherBinHelp')}</Text>

      <Text style={styles.label}>{t('count.zoneLabel')}</Text>
      {zones === null ? (
        <View style={styles.inline}>
          <ActivityIndicator color={colors.brand} />
          <Text style={styles.help}>{t('count.zonesLoading')}</Text>
        </View>
      ) : zones.length === 0 ? (
        <Text style={styles.error}>{t('count.zonesEmpty')}</Text>
      ) : (
        <>
          {zonesFromLocal ? <Text style={styles.help}>{t('count.zonesFromLocal')}</Text> : null}
          <View style={styles.chips}>
            {zones.map((z) => {
              const selected = z.id === zoneId
              const label = z.name ? `${z.code} · ${z.name}` : z.code
              return (
                <Pressable
                  key={z.id}
                  accessibilityRole="radio"
                  accessibilityLabel={label}
                  accessibilityState={{ selected, checked: selected }}
                  onPress={() => {
                    setZoneId(z.id)
                    clearMessages()
                  }}
                  style={[styles.chip, selected && styles.chipOn]}
                >
                  <Text style={styles.chipLabel} numberOfLines={1}>
                    {label}
                  </Text>
                </Pressable>
              )
            })}
          </View>
        </>
      )}

      <Field label={t('count.binCodeLabel')} value={code} onChange={edit(setCode)} autoCapitalize="characters" />
      <Text style={styles.label}>{t('count.binPartsLabel')}</Text>
      <View style={styles.grid}>
        <Field small label={t('count.aisleLabel')} value={aisle} onChange={edit(setAisle)} autoCapitalize="characters" />
        <Field small label={t('count.rackLabel')} value={rack} onChange={edit(setRack)} autoCapitalize="characters" />
        <Field small label={t('count.levelLabel')} value={level} onChange={edit(setLevel)} autoCapitalize="characters" />
        <Field small label={t('count.positionLabel')} value={position} onChange={edit(setPosition)} autoCapitalize="characters" />
      </View>

      {needsLot ? (
        <>
          <Field label={t('count.lotLabel')} value={lot} onChange={edit(setLot)} />
          {!lot.trim() ? <Text style={styles.help}>{t('count.lotRequired')}</Text> : null}
          <Field
            label={t('count.expiryLabel')}
            value={expiry}
            onChange={edit(setExpiry)}
            placeholder={f.datePlaceholder()}
            keyboardType="numbers-and-punctuation"
          />
          {expiryIso === null ? <Text style={styles.error}>{t('count.expiryInvalid', { format: f.datePlaceholder() })}</Text> : null}
        </>
      ) : null}

      <ScanMessage tone="error" message={error} />
      {existing ? <BigButton label={t('count.useExistingBin', { bin: existing.code })} variant="secondary" onPress={() => onAdded(existing, lotData, true)} /> : null}
      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      <View style={styles.row}>
        <View style={styles.half}>
          <BigButton label={t('common.cancel')} variant="secondary" onPress={onCancel} disabled={busy} />
        </View>
        <View style={styles.half}>
          <BigButton label={t('count.createBin')} onPress={() => void submit()} disabled={!canSubmit} />
        </View>
      </View>
    </View>
  )
}

function Field({
  label,
  value,
  onChange,
  small,
  placeholder,
  autoCapitalize = 'none',
  keyboardType = 'default',
}: {
  label: string
  value: string
  onChange: (v: string) => void
  small?: boolean
  placeholder?: string
  autoCapitalize?: 'none' | 'characters'
  keyboardType?: 'default' | 'numbers-and-punctuation'
}) {
  return (
    <View style={[styles.field, small && styles.fieldSmall]}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <KeyboardInput
        value={value}
        onChangeText={onChange}
        style={styles.input}
        accessibilityLabel={label}
        autoCapitalize={autoCapitalize}
        autoCorrect={false}
        keyboardType={keyboardType}
        placeholder={placeholder}
        placeholderTextColor={colors.muted}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
  inline: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  chip: {
    minHeight: 48,
    maxWidth: '100%',
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.line,
    backgroundColor: colors.panelAlt,
    justifyContent: 'center',
  },
  chipOn: { borderColor: colors.brand, backgroundColor: colors.brandDark },
  chipLabel: { color: colors.text, fontSize: fontSize.message, fontWeight: '600' },
  // dos columnas de partes (pasillo/rack, nivel/posición): caben en 360 px sin desplazamiento horizontal
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  field: { gap: spacing.xs },
  fieldSmall: { flexBasis: '47%', flexGrow: 1, minWidth: 0 },
  fieldLabel: { color: colors.text, fontSize: fontSize.message },
  input: {
    minHeight: touchTarget,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
  row: { flexDirection: 'row', gap: spacing.md },
  half: { flex: 1 },
})
