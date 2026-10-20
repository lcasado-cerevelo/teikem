// Lote 146 — «¿Contra qué?»: con «el despacho exige documento» activo en la compañía, Despacho no deja sacar mercancía libre; se escoge el documento contra el que se despacha
// (aviso de salida y/o orden de venta, según lo que exija la compañía). El servidor también lo exige (409 en el despacho manual).
import { StyleSheet, Text, View } from 'react-native'
import { useRouter } from 'expo-router'

import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { KeyboardScreen } from '../../kernel/ui/KeyboardScreen'
import { colors, fontSize, spacing } from '../../kernel/ui/theme'
import type { DispatchRequirement } from './stockExit'

const KEYS = { ANY_DOCUMENT: 'dispatch.requiresAnyDocument', EXIT_NOTICE: 'dispatch.requiresExitNotice', SALES_ORDER: 'dispatch.requiresSalesOrder' } as const

export function DispatchDocumentChooser({ requirement }: { requirement: Exclude<DispatchRequirement, 'FREE'> }) {
  const { t } = useT()
  const router = useRouter()
  return (
    <KeyboardScreen contentContainerStyle={styles.fill}>
      <Text style={styles.title}>{t('dispatch.against')}</Text>
      <Text style={styles.help}>{t(KEYS[requirement])}</Text>
      {requirement !== 'SALES_ORDER' ? <BigButton label={t('home.exitDispatch')} onPress={() => router.replace('/exit-dispatch')} testID="against-exit-notice" /> : null}
      {requirement !== 'EXIT_NOTICE' ? <BigButton label={t('home.salesDispatch')} variant="secondary" onPress={() => router.replace('/sales-dispatch')} testID="against-sales-order" /> : null}
      <View style={styles.bottom}>
        <BigButton label={t('common.back')} variant="danger" onPress={() => router.replace('/home')} />
      </View>
    </KeyboardScreen>
  )
}

const styles = StyleSheet.create({
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
  bottom: { marginTop: 'auto' },
})
