// «Pregúntame cómo usar el portal» (Lote 218): lo mismo que en la web del personal (web-app/src/features/notifications/KemiAsk.tsx): Kemi BUSCA las palabras de la pregunta en el manual y la FAQ DEL CLIENTE
// y trae hasta 5 opciones; al abrir una muestra la respuesta y pregunta «¿Te contesté?» (No = busca por otro lado, sin repetir). Se le puede ESCRIBIR o HABLAR (micrófono): lo dicho llega como texto y
// busca solo al terminar de hablar. Toda la búsqueda pasa por el cerebro (portalBrain): el día que Kemi tenga IA, esta pantalla no cambia.
import { Ionicons } from '@expo/vector-icons'
import { usePathname } from 'expo-router'
import { useRef, useState } from 'react'
import { Pressable, Text, TextInput, View } from 'react-native'

import { portalScreenTopics, type HelpEntry } from '../../../../shared/kemi/helpSearch'
import { useT } from '../../kernel/i18n/useT'
import { useColors } from '../../kernel/ui/theme'
import { portalBrain } from './portalBrain'
import { useKemiVoice } from './useKemiVoice'

function Opt({ label, onPress, disabled, testID }: { label: string; onPress: () => void; disabled?: boolean; testID?: string }) {
  const colors = useColors()
  return (
    <Pressable accessibilityRole="button" testID={testID} disabled={disabled} onPress={onPress} style={{ borderWidth: 1, borderColor: colors.line, borderRadius: 10, paddingVertical: 10, paddingHorizontal: 12, backgroundColor: colors.card, opacity: disabled ? 0.5 : 1 }}>
      <Text style={{ color: colors.text, fontWeight: '600' }}>{label}</Text>
    </Pressable>
  )
}

function Msg({ children, role }: { children: string; role?: 'status' | 'alert' }) {
  const colors = useColors()
  return (
    <Text accessibilityRole={role === 'alert' ? 'alert' : undefined} accessibilityLiveRegion="polite" style={{ color: colors.text, fontSize: 14 }}>
      {children}
    </Text>
  )
}

type Phase = 'ask' | 'results' | 'answer' | 'none' | 'thanks' | 'error'

export function KemiAsk({ onBack }: { onBack: () => void }) {
  const { t, lang } = useT()
  const colors = useColors()
  const pathname = usePathname()
  const [phase, setPhase] = useState<Phase>('ask')
  const [draft, setDraft] = useState('')
  const [question, setQuestion] = useState('')
  const [hits, setHits] = useState<HelpEntry[]>([])
  const [open, setOpen] = useState<HelpEntry | null>(null)
  const [round, setRound] = useState(0)
  const [busy, setBusy] = useState(false)
  const seen = useRef(new Set<HelpEntry>())

  const run = async (q: string, other: boolean) => {
    setBusy(true)
    try {
      const found = await portalBrain.ask({ text: q, lang, topics: portalScreenTopics(pathname), exclude: other ? seen.current : undefined })
      for (const e of found) seen.current.add(e)
      setHits(found)
      setRound(other ? round + 1 : 0)
      setPhase(found.length ? 'results' : 'none')
    } catch {
      setPhase('error')
    } finally {
      setBusy(false)
    }
  }

  const ask = (text: string) => {
    const q = text.trim()
    if (!q || busy) return
    seen.current = new Set()
    setQuestion(q)
    void run(q, false)
  }

  // lo dicho llena el campo mientras la persona habla y, al terminar, pregunta solo
  const voice = useKemiVoice(lang, (text, final) => {
    setDraft(text)
    if (final) ask(text)
  })

  const reset = () => {
    seen.current = new Set()
    setDraft('')
    setQuestion('')
    setHits([])
    setOpen(null)
    setRound(0)
    setPhase('ask')
  }

  return (
    <View style={{ gap: 10 }} testID="kemi-ask">
      {phase === 'ask' && (
        <View style={{ gap: 10 }}>
          <Msg>{t('kemi.askIntro')}</Msg>
          {lang === 'en' && <Text style={{ color: colors.muted, fontSize: 12 }}>{t('kemi.askSpanishNote')}</Text>}
          <View style={{ flexDirection: 'row', gap: 8, alignItems: 'center' }}>
            <TextInput
              testID="kemi-input"
              value={draft}
              onChangeText={setDraft}
              onSubmitEditing={() => ask(draft)}
              placeholder={voice.listening ? t('kemi.listening') : t('kemi.askPlaceholder')}
              placeholderTextColor={colors.muted}
              accessibilityLabel={t('kemi.askLabel')}
              maxLength={300}
              returnKeyType="search"
              style={{ flex: 1, minHeight: 44, borderWidth: 1, borderColor: voice.listening ? colors.flow : colors.line, borderRadius: 10, paddingHorizontal: 12, color: colors.text, backgroundColor: colors.card }}
            />
            {voice.supported && (
              <Pressable
                testID="kemi-mic"
                accessibilityRole="button"
                accessibilityLabel={voice.listening ? t('kemi.micStop') : t('kemi.micStart')}
                accessibilityState={{ selected: voice.listening }}
                onPress={() => (voice.listening ? voice.stop() : void voice.start())}
                style={{ width: 44, height: 44, borderRadius: 22, alignItems: 'center', justifyContent: 'center', backgroundColor: voice.listening ? colors.flow : colors.neutralBg, borderWidth: 1, borderColor: voice.listening ? colors.flow : colors.line }}
              >
                <Ionicons name={voice.listening ? 'mic' : 'mic-outline'} size={22} color={voice.listening ? colors.onFlow : colors.text} />
              </Pressable>
            )}
          </View>
          {voice.listening && <Text testID="kemi-listening" style={{ color: colors.muted, fontSize: 13 }}>{t('kemi.listening')}</Text>}
          {!!voice.error && (
            <Text testID="kemi-voice-error" accessibilityRole="alert" style={{ color: colors.danger, fontSize: 13 }}>
              {t(`kemi.voice.${voice.error}`)}
            </Text>
          )}
          <View style={{ gap: 8 }}>
            <Opt testID="kemi-send" label={busy ? t('kemi.askLoading') : t('kemi.askSend')} onPress={() => ask(draft)} disabled={draft.trim() === '' || busy} />
            <Opt label={t('kemi.askBack')} onPress={onBack} />
          </View>
        </View>
      )}

      {phase !== 'ask' && (
        <Text testID="kemi-you" style={{ alignSelf: 'flex-end', maxWidth: '90%', backgroundColor: colors.neutralBg, color: colors.text, borderRadius: 12, paddingVertical: 6, paddingHorizontal: 10, overflow: 'hidden' }}>
          {question}
        </Text>
      )}

      {phase === 'results' && (
        <View style={{ gap: 8 }}>
          <Msg role="status">{round === 0 ? t('kemi.askResults') : t('kemi.askOther')}</Msg>
          {hits.map((h, i) => (
            <Pressable
              key={i}
              testID={`kemi-hit-${i}`}
              accessibilityRole="button"
              onPress={() => {
                setOpen(h)
                setPhase('answer')
              }}
              style={{ borderWidth: 1, borderColor: colors.line, borderRadius: 10, padding: 10, backgroundColor: colors.card, gap: 2 }}
            >
              <Text style={{ color: colors.text, fontWeight: '600' }}>{h.t}</Text>
              <Text style={{ color: colors.muted, fontSize: 12 }}>{h.c}</Text>
            </Pressable>
          ))}
          <Opt label={t('kemi.askChange')} onPress={() => setPhase('ask')} />
        </View>
      )}

      {phase === 'answer' && open && (
        <View style={{ gap: 8 }}>
          <View testID="kemi-answer" accessibilityLabel={open.t} style={{ borderWidth: 1, borderColor: colors.line, borderRadius: 10, padding: 12, backgroundColor: colors.card, gap: 6 }}>
            <Text style={{ color: colors.text, fontWeight: '700' }}>{open.t}</Text>
            <Text style={{ color: colors.text, lineHeight: 20 }}>{open.x}</Text>
          </View>
          <Msg>{t('kemi.askAnswered')}</Msg>
          <View style={{ flexDirection: 'row', gap: 8 }}>
            <View style={{ flex: 1 }}>
              <Opt testID="kemi-yes" label={t('kemi.askYes')} onPress={() => setPhase('thanks')} />
            </View>
            <View style={{ flex: 1 }}>
              <Opt testID="kemi-no" label={t('kemi.askNo')} onPress={() => void run(question, true)} disabled={busy} />
            </View>
          </View>
          <Opt label={t('kemi.askToList')} onPress={() => setPhase('results')} />
        </View>
      )}

      {phase === 'none' && (
        <View style={{ gap: 8 }}>
          <Msg role="status">{round === 0 ? t('kemi.askNone') : t('kemi.askNoMore')}</Msg>
          <Opt label={t('kemi.askChange')} onPress={() => setPhase('ask')} />
        </View>
      )}

      {phase === 'thanks' && (
        <View style={{ gap: 8 }}>
          <Msg role="status">{t('kemi.askThanks')}</Msg>
          <Opt label={t('kemi.askAgain')} onPress={reset} />
        </View>
      )}

      {phase === 'error' && (
        <View style={{ gap: 8 }}>
          <Msg role="alert">{t('kemi.askError')}</Msg>
          <Opt label={t('kemi.askChange')} onPress={() => setPhase('ask')} />
        </View>
      )}

      {phase !== 'ask' && <Opt label={t('kemi.askBack')} onPress={onBack} />}
    </View>
  )
}
