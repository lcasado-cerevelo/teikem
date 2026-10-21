// El documento legal para LEER (sin «Acepto»): lo abre el pie de Ajustes en la parte que se pidió (`part=privacy` o `terms`). Distinto de LegalScreen, que es la
// aceptación obligatoria del primer ingreso.
import { useLocalSearchParams, useRouter } from 'expo-router'
import { useEffect, useRef } from 'react'
import { ScrollView, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Button, Muted, Notice, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { useAsync } from '../../kernel/ui/useAsync'
import { getLegalDocument } from '../portalApi'

export function LegalReadScreen() {
  const colors = useColors()
  const { t, lang } = useT()
  const router = useRouter()
  const { part } = useLocalSearchParams<{ part?: string }>()
  const doc = useAsync(() => getLegalDocument(lang), [lang])
  const scroll = useRef<ScrollView>(null)
  const partY = useRef<Record<string, number>>({})
  const d = doc.data
  // al llegar el documento se baja a la parte pedida (privacidad = la primera; términos = donde empieza la segunda)
  useEffect(() => {
    const y = part ? partY.current[part] : undefined
    if (d && y != null) scroll.current?.scrollTo({ y, animated: false })
  }, [d, part])
  let lastPart = ''
  return (
    <View style={{ flex: 1, backgroundColor: colors.bg, padding: 16, gap: 10, alignItems: 'center' }}>
      <View style={{ flex: 1, width: '100%', maxWidth: 720, gap: 10 }}>
        <Title>{d?.title ?? t('legal.title')}</Title>
        {!!doc.error && <Notice>{apiErrorMessage(doc.error)}</Notice>}
        {doc.loading && !d ? (
          <Spinner />
        ) : (
          !!d && (
            <ScrollView
              ref={scroll}
              testID="legal-read-scroll"
              style={{ flex: 1, backgroundColor: colors.card, borderRadius: 12, borderWidth: 1, borderColor: colors.line }}
              contentContainerStyle={{ padding: 14, gap: 6 }}
            >
              <Text style={{ fontWeight: '700', color: colors.text }}>{d.intro}</Text>
              <Muted>{d.version}</Muted>
              {(d.sections ?? []).map((s) => {
                const first = s.part !== lastPart
                const heading = first ? (s.part === 'privacy' ? d.partPrivacy : d.partTerms) : null
                lastPart = s.part ?? ''
                return (
                  <View
                    key={s.id}
                    style={{ gap: 6 }}
                    onLayout={
                      first && s.part
                        ? (e) => {
                            partY.current[s.part as string] = e.nativeEvent.layout.y
                          }
                        : undefined
                    }
                  >
                    {!!heading && <Text style={{ fontSize: 18, fontWeight: '800', marginTop: 16, color: colors.brand }}>{heading}</Text>}
                    <Text style={{ fontSize: 15, fontWeight: '700', marginTop: 8, color: colors.text }}>{s.heading}</Text>
                    {(s.paragraphs ?? []).map((p, i) => (
                      <Text key={i} style={{ fontSize: 14, lineHeight: 21, color: colors.text }}>
                        {p}
                      </Text>
                    ))}
                  </View>
                )
              })}
            </ScrollView>
          )
        )}
        <Button title={t('legalFooter.back')} variant="secondary" onPress={() => (router.canGoBack() ? router.back() : router.replace('/settings'))} testID="legal-read-back" />
      </View>
    </View>
  )
}
