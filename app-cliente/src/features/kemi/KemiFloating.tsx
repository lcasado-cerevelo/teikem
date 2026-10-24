// Kemi flotante en el portal (Lote 218): la mascota abajo a la derecha (o donde la persona la arrastre; se acuerda por persona), con vida, el globo «Te llegó un mensaje → Ver» y el panel de
// opciones (Pregúntame cómo usar el portal, Mis mensajes, Ver mis avisos, llevarla a su esquina, dormirla). Dormida se esconde en un botoncito (KemiSleepButton) y no muestra globos.
// Es el mismo comportamiento de la web del personal (web-app/src/features/notifications/Kemi.tsx), con las mismas reglas compartidas (shared/kemi/rules.ts).
import { useRouter } from 'expo-router'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Animated, PanResponder, Platform, Pressable, ScrollView, Text, useWindowDimensions, View } from 'react-native'

import { DEFAULT_POS, DRAG_THRESHOLD, anchorStyle, clampPos, isMoved, quadrant, type KemiPos } from '../../../../shared/kemi/rules'
import { useT } from '../../kernel/i18n/useT'
import { useColors } from '../../kernel/ui/theme'
import { useIsWide } from '../../kernel/ui/layout'
import { KemiAsk } from './KemiAsk'
import { KemiMascot } from './KemiMascot'
import { useKemi } from './KemiProvider'
import { useKemiLife } from './useKemiLife'

const FAB = 58
/** En el celular abajo van las pestañas: Kemi se queda encima de ellas. */
const TABS_INSET = 64

/** El botón de Kemi dormida: despertarla la devuelve a su lugar flotante. */
export function KemiSleepButton({ floating = false }: { floating?: boolean }) {
  const { t } = useT()
  const colors = useColors()
  const kemi = useKemi()
  if (!kemi.asleep || !kemi.ready) return null
  return (
    <Pressable
      testID="kemi-wake"
      accessibilityRole="button"
      accessibilityLabel={t('kemi.wake')}
      onPress={() => kemi.setAsleep(false)}
      style={[{ width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' }, floating && { position: 'absolute', top: 8, right: 8, backgroundColor: colors.card, borderWidth: 1, borderColor: colors.line }]}
    >
      <KemiMascot size={26} sleeping />
    </Pressable>
  )
}

/** Kemi despierta: la mascota, el globo del mensaje nuevo y su panel de opciones. */
export function KemiFloating() {
  const { t } = useT()
  const colors = useColors()
  const router = useRouter()
  const kemi = useKemi()
  const wide = useIsWide()
  const { width: viewW, height: viewH } = useWindowDimensions()
  const [asking, setAsking] = useState(false)
  const [dragPos, setDragPos] = useState<KemiPos | null>(null)
  const [hover, setHover] = useState(false)
  const inset = wide || Platform.OS === 'web' && viewW > 700 ? 0 : TABS_INSET
  const excited = kemi.bubble !== null
  const life = useKemiLife(!kemi.asleep && !excited && !kemi.kemiOpen, excited)

  const shown = clampPos(dragPos ?? kemi.pos, viewW, viewH - inset)
  const side = quadrant(shown, viewW, viewH - inset)
  const anchor = anchorStyle(shown, viewW, viewH - inset)
  const style = {
    position: 'absolute' as const,
    top: anchor.top === 'auto' ? undefined : anchor.top,
    bottom: anchor.bottom === 'auto' ? undefined : anchor.bottom + inset,
    left: anchor.left === 'auto' ? undefined : anchor.left,
    right: anchor.right === 'auto' ? undefined : anchor.right,
    alignItems: side.h === 'left' ? ('flex-start' as const) : ('flex-end' as const),
    gap: 8,
    flexDirection: side.v === 'top' ? ('column-reverse' as const) : ('column' as const),
  }

  // arrastrar: un movimiento de menos de DRAG_THRESHOLD sigue siendo un toque
  const start = useRef<KemiPos>(DEFAULT_POS)
  const moved = useRef(false)
  const latest = useRef({ shown, viewW, viewH, inset })
  latest.current = { shown, viewW, viewH, inset }
  const pan = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponderCapture: (_e, g) => Math.hypot(g.dx, g.dy) > DRAG_THRESHOLD,
        onPanResponderGrant: () => {
          start.current = latest.current.shown
          moved.current = false
        },
        onPanResponderMove: (_e, g) => {
          moved.current = true
          const c = latest.current
          setDragPos(clampPos({ right: start.current.right - g.dx, bottom: start.current.bottom - g.dy }, c.viewW, c.viewH - c.inset))
        },
        onPanResponderRelease: () => {
          setDragPos((p) => {
            if (p) kemi.setPos(p)
            return null
          })
          kemi.setKemiOpen(false)
        },
        onPanResponderTerminate: () => setDragPos(null),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [kemi.setPos, kemi.setKemiOpen],
  )

  // Kemi brinca mientras el globo está a la vista
  const jump = useRef(new Animated.Value(0)).current
  useEffect(() => {
    if (!excited) {
      jump.setValue(0)
      return
    }
    const loop = Animated.loop(Animated.sequence([Animated.timing(jump, { toValue: -8, duration: 260, useNativeDriver: true }), Animated.timing(jump, { toValue: 0, duration: 260, useNativeDriver: true })]))
    loop.start()
    return () => loop.stop()
  }, [excited, jump])

  if (kemi.asleep || !kemi.ready) return null
  const total = kemi.unread
  const go = (path: '/messages' | '/notices') => {
    kemi.setKemiOpen(false)
    router.push(path)
  }
  const opt = (key: string, label: string, onPress: () => void, count?: number) => (
    <Pressable key={key} testID={`kemi-opt-${key}`} accessibilityRole="button" onPress={onPress} style={{ borderWidth: 1, borderColor: colors.line, borderRadius: 10, paddingVertical: 10, paddingHorizontal: 12, backgroundColor: colors.card, flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: 8 }}>
      <Text style={{ color: colors.text, fontWeight: '600', flexShrink: 1 }}>{label}</Text>
      {!!count && count > 0 && (
        <View style={{ backgroundColor: colors.flow, borderRadius: 10, minWidth: 20, paddingHorizontal: 6, alignItems: 'center' }}>
          <Text style={{ color: colors.onFlow, fontSize: 12, fontWeight: '800' }}>{count > 99 ? '99+' : count}</Text>
        </View>
      )}
    </Pressable>
  )

  const panelW = Math.min(330, viewW - 24)
  const panel = kemi.kemiOpen && (
    <View testID="kemi-panel" accessibilityRole="none" accessibilityLabel={t('kemi.name')} style={{ width: panelW, maxHeight: Math.max(240, viewH - inset - 110), backgroundColor: colors.card, borderRadius: 14, borderWidth: 1, borderColor: colors.line, padding: 12, gap: 10, shadowColor: '#000', shadowOpacity: 0.18, shadowRadius: 12, shadowOffset: { width: 0, height: 4 }, elevation: 8 }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: 10 }}>
        <KemiMascot size={30} />
        <View style={{ flex: 1 }}>
          <Text style={{ color: colors.text, fontWeight: '800' }}>{t('kemi.name')}</Text>
          <Text style={{ color: colors.muted, fontSize: 12 }}>{t('kemi.subtitle')}</Text>
        </View>
        <Pressable testID="kemi-close" accessibilityRole="button" accessibilityLabel={t('kemi.close')} onPress={() => kemi.setKemiOpen(false)} hitSlop={8}>
          <Text style={{ color: colors.muted, fontSize: 18 }}>✕</Text>
        </Pressable>
      </View>
      <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={{ gap: 8 }}>
        {asking ? (
          <KemiAsk onBack={() => setAsking(false)} />
        ) : (
          <>
            <Text style={{ color: colors.text }}>{total > 0 ? t('kemi.hiUnread', { n: total }) : t('kemi.hi')}</Text>
            {opt('ask', t('kemi.optAsk'), () => setAsking(true))}
            {opt('mine', t('kemi.optMessages'), () => go('/messages'), total)}
            {opt('avisos', t('kemi.optAvisos'), () => go('/notices'))}
            {isMoved(shown) && opt('home', t('kemi.optHome'), () => { setDragPos(null); kemi.setPos(DEFAULT_POS); kemi.setKemiOpen(false) })}
            {opt('sleep', t('kemi.optSleep'), () => { setAsking(false); kemi.setAsleep(true) })}
          </>
        )}
      </ScrollView>
    </View>
  )

  const bubble = kemi.bubble !== null && !kemi.kemiOpen && (
    <View testID="kemi-bubble" accessibilityRole="alert" style={{ maxWidth: panelW, flexDirection: 'row', alignItems: 'center', gap: 10, backgroundColor: colors.card, borderRadius: 14, borderWidth: 1, borderColor: colors.line, paddingVertical: 8, paddingHorizontal: 12, shadowColor: '#000', shadowOpacity: 0.15, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 6 }}>
      <Text style={{ color: colors.text, flexShrink: 1 }}>{kemi.bubble === 1 ? t('kemi.bubble.one') : t('kemi.bubble.many', { n: kemi.bubble })}</Text>
      <Pressable testID="kemi-see" accessibilityRole="button" onPress={() => { kemi.dismissBubble(); go('/messages') }}>
        <Text style={{ color: colors.link, fontWeight: '800' }}>{t('kemi.see')} →</Text>
      </Pressable>
      <Pressable testID="kemi-dismiss" accessibilityRole="button" accessibilityLabel={t('kemi.dismiss')} onPress={kemi.dismissBubble} hitSlop={8}>
        <Text style={{ color: colors.muted }}>✕</Text>
      </Pressable>
    </View>
  )

  return (
    <View pointerEvents="box-none" style={{ position: 'absolute', top: 0, left: 0, right: 0, bottom: 0 }}>
      <View style={style} pointerEvents="box-none">
        {panel}
        {bubble}
        <Animated.View {...pan.panHandlers} style={{ transform: [{ translateY: jump }] }}>
          <Pressable
            testID="kemi-fab"
            accessibilityRole="button"
            accessibilityLabel={t('kemi.fab')}
            accessibilityState={{ expanded: kemi.kemiOpen }}
            onPress={() => {
              if (!moved.current) {
                if (!kemi.kemiOpen) setAsking(false)   // al volver a abrirla se ven sus opciones, no la pregunta de la vez pasada
                kemi.setKemiOpen(!kemi.kemiOpen)
              }
              moved.current = false
            }}
            onHoverIn={() => { setHover(true); life.onHoverIn() }}
            onHoverOut={() => { setHover(false); life.onHoverOut() }}
            onPointerMove={Platform.OS === 'web' ? life.onPointerMove : undefined}
            style={{ width: FAB, height: FAB, borderRadius: FAB / 2, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.card, borderWidth: 1, borderColor: hover || excited ? colors.flow : colors.line, shadowColor: '#000', shadowOpacity: 0.2, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 6 }}
          >
            <KemiMascot size={38} face={life.face} />
            {total > 0 && (
              <View style={{ position: 'absolute', top: -2, right: -2, backgroundColor: colors.flow, borderRadius: 10, minWidth: 20, paddingHorizontal: 5, alignItems: 'center' }}>
                <Text style={{ color: colors.onFlow, fontSize: 12, fontWeight: '800' }}>{total > 99 ? '99+' : total}</Text>
              </View>
            )}
          </Pressable>
        </Animated.View>
      </View>
    </View>
  )
}
