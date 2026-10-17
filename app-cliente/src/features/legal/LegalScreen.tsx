// Política de privacidad y términos de uso: documento único; «Acepto» se habilita solo al llegar al final del texto. Se pide en el primer ingreso y cada vez que cambia la versión.
import { useRouter } from 'expo-router'
import { useState } from 'react'
import { ScrollView, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { logout } from '../../kernel/auth/portalAuth'
import { useT } from '../../kernel/i18n/useT'
import { Button, colors, Muted, Notice, Spinner, Title } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { acceptLegal, getLegalDocument } from '../portalApi'

/** ¿Llegó el lector al final del texto? (margen de unos píxeles; un texto que cabe sin desplazar cuenta como leído). */
export function reachedEnd(m: { contentOffset: { y: number }; layoutMeasurement: { height: number }; contentSize: { height: number } }, margin = 24): boolean {
  return m.contentOffset.y + m.layoutMeasurement.height >= m.contentSize.height - margin
}

export function LegalScreen({ onAccepted }: { onAccepted: () => void }) {
  const { t, lang } = useT()
  const router = useRouter()
  const doc = useAsync(() => getLegalDocument(lang), [lang])
  const [read, setRead] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function accept() {
    if (!doc.data?.version) return
    setBusy(true)
    setError(null)
    try {
      await acceptLegal(doc.data.version, lang)
      onAccepted()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const d = doc.data
  let lastPart = ''
  return (
    <View style={{ flex: 1, backgroundColor: colors.bg, padding: 16, gap: 10, alignItems: 'center' }}>
      <View style={{ flex: 1, width: '100%', maxWidth: 720, gap: 10 }}>
        <Title>{d?.title ?? t('legal.title')}</Title>
        <Muted>{t('legal.lead')}</Muted>
        {!!doc.error && <Notice>{apiErrorMessage(doc.error)}</Notice>}
        {doc.loading && !d ? (
          <Spinner />
        ) : (
          !!d && (
            <ScrollView
              testID="legal-scroll"
              style={{ flex: 1, backgroundColor: '#fff', borderRadius: 12, borderWidth: 1, borderColor: colors.line }}
              contentContainerStyle={{ padding: 14, gap: 6 }}
              scrollEventThrottle={64}
              onScroll={(e) => reachedEnd(e.nativeEvent) && setRead(true)}
              onContentSizeChange={(_w, h) => h < 300 && setRead(true)}
            >
              <Text style={{ fontWeight: '700', color: colors.text }}>{d.intro}</Text>
              <Muted>{d.version}</Muted>
              {(d.sections ?? []).map((s) => {
                const heading = s.part !== lastPart ? (s.part === 'privacy' ? d.partPrivacy : d.partTerms) : null
                lastPart = s.part ?? ''
                return (
                  <View key={s.id} style={{ gap: 6 }}>
                    {!!heading && <Text style={{ fontSize: 18, fontWeight: '800', marginTop: 16, color: colors.brand }}>{heading}</Text>}
                    <Text style={{ fontSize: 15, fontWeight: '700', marginTop: 8, color: colors.text }}>{s.heading}</Text>
                    {(s.paragraphs ?? []).map((p, i) => (
                      <Text key={i} style={{ fontSize: 14, lineHeight: 21, color: colors.text }}>{p}</Text>
                    ))}
                  </View>
                )
              })}
            </ScrollView>
          )
        )}
        {!read && !!d && <Muted>{t('legal.scrollHint')}</Muted>}
        {!!error && <Notice>{error}</Notice>}
        <Button title={t('legal.accept')} busy={busy} disabled={!read || !d} onPress={() => void accept()} testID="legal-accept" />
        <Button title={t('legal.decline')} variant="secondary" onPress={() => void logout().then(() => router.replace('/login'))} />
      </View>
    </View>
  )
}
