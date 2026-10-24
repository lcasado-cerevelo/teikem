// Nuevo envío del portal (2026-10-23, dictado de Luis): los mismos campos y funciones que «Nuevo envío» de la web del personal. Dos maneras de entrar en la web ancha —«Rápida» (la
// rejilla de captura, todo con el teclado) y «Detallada» (pasos 01/02/03 con el ticket)—, recordada; en el celular (app instalada o web angosta) SOLO la detallada. El cliente sale de la
// sesión; quien solo puede consultar ve un aviso en lugar del formulario.
import { useRouter } from 'expo-router'
import { useEffect, useState } from 'react'
import { Pressable, Text, View } from 'react-native'

import { apiErrorMessage } from '../../kernel/api/client'
import { StoreKeys, storeGet, storeSet } from '../../kernel/storage'
import { PageHeader } from '../../kernel/ui/desk'
import { useIsWide } from '../../kernel/ui/layout'
import { useT } from '../../kernel/i18n/useT'
import { Muted, Notice, Screen, Spinner, Title, useColors } from '../../kernel/ui/ui'
import { getOrderForm, type PortalOrderFormDto, type PortalPlaceDto } from '../portalApi'
import { NewOrderDetailed } from './NewOrderDetailed'
import { NewOrderQuick } from './NewOrderQuick'

export function NewOrderScreen() {
  const colors = useColors()
  const { t } = useT()
  const router = useRouter()
  const [form, setForm] = useState<PortalOrderFormDto | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const wide = useIsWide()
  // «Rápida» o «Detallada»; se recuerda. En escritorio sale la rápida; en el celular solo existe la detallada.
  const [mode, setModeState] = useState<'quick' | 'detailed' | null>(null)
  useEffect(() => {
    void storeGet(StoreKeys.newOrderMode).then((v) => setModeState(v === 'quick' || v === 'detailed' ? v : null))
  }, [])
  const effectiveMode: 'quick' | 'detailed' = !wide ? 'detailed' : mode === 'quick' || mode === 'detailed' ? mode : 'quick'
  const setMode = (m: 'quick' | 'detailed') => {
    setModeState(m)
    void storeSet(StoreKeys.newOrderMode, m)
  }

  useEffect(() => {
    getOrderForm().then(setForm).catch((e) => setLoadError(apiErrorMessage(e)))
  }, [])

  if (loadError) return <Screen><Notice>{loadError}</Notice></Screen>
  if (!form) return <Screen><Spinner /></Screen>
  if (!form.canCreate) {
    return (
      <Screen>
        <Title>{t('newOrder.title')}</Title>
        <Notice tone="info">{t('newOrder.readOnly')}</Notice>
      </Screen>
    )
  }

  const modeSwitch = wide ? (
    <View style={{ flexDirection: 'row', gap: 8 }}>
      {(['quick', 'detailed'] as const).map((m) => (
        <Pressable key={m} accessibilityRole="button" accessibilityState={{ selected: effectiveMode === m }} testID={`neworder-mode-${m}`} onPress={() => setMode(m)}
          style={{ paddingVertical: 6, paddingHorizontal: 14, borderRadius: 18, borderWidth: 1, borderColor: effectiveMode === m ? colors.brand : colors.line, backgroundColor: effectiveMode === m ? colors.brand : colors.card }}>
          <Text style={{ color: effectiveMode === m ? colors.onBrand : colors.text, fontWeight: '600' }}>{t(`newOrder.mode.${m}`)}</Text>
        </Pressable>
      ))}
    </View>
  ) : null

  // el consignatario recién creado se suma a la lista de la pantalla (sin volver a pedirla) para que el buscador lo encuentre en el envío siguiente
  const addConsignee = (place: PortalPlaceDto) =>
    setForm((f) => (f && !(f.consignees ?? []).some((c) => c.publicId === place.publicId) ? { ...f, consignees: [...(f.consignees ?? []), place].sort((a, b) => (a.name ?? '').localeCompare(b.name ?? '')) } : f))
  const open = (id: string) => router.push({ pathname: '/order/[id]', params: { id } })
  if (wide) {
    return (
      <Screen wide>
        <PageHeader title={t('newOrder.title')} subtitle={effectiveMode === 'quick' ? t('newOrder.quick.subtitle') : t('newOrder.help')}>{modeSwitch}</PageHeader>
        {effectiveMode === 'quick' ? <NewOrderQuick form={form} onOpen={open} onConsigneeCreated={addConsignee} /> : <NewOrderDetailed form={form} onCreated={(id) => router.replace({ pathname: '/order/[id]', params: { id } })} onConsigneeCreated={addConsignee} />}
      </Screen>
    )
  }

  return (
    <Screen>
      <Title>{t('newOrder.title')}</Title>
      <Muted>{t('newOrder.help')}</Muted>
      <NewOrderDetailed form={form} onCreated={(id) => router.replace({ pathname: '/order/[id]', params: { id } })} onConsigneeCreated={addConsignee} />
    </Screen>
  )
}
