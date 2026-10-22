// Códigos de recuperación del MFA (se muestran una sola vez), como en la web del equipo: Copiar, Descargar (.txt; en el celular se comparten/guardan) y la casilla «Ya los guardé»,
// obligatoria para seguir (2026-10-22: el código del segundo paso se pide en esta misma pantalla, no en otra).
import * as Clipboard from 'expo-clipboard'
import { useState } from 'react'
import { Platform, Share, Text } from 'react-native'

import { useT } from '../i18n/useT'
import { Button, Card, Check, Notice } from './ui'

/** Texto del archivo descargable: encabezado con la cuenta y la fecha, un código por línea. */
export function recoveryCodesFile(codes: readonly string[], account: string | null | undefined, now = new Date()): string {
  return [`Teikem — códigos de recuperación${account ? ` (${account})` : ''}`, `Generados: ${now.toISOString().slice(0, 10)}`, '', ...codes, ''].join('\r\n')
}

export function RecoveryCodes({ codes, account, saved, onSavedChange }: { codes: readonly string[]; account?: string | null; saved: boolean; onSavedChange: (v: boolean) => void }) {
  const { t } = useT()
  const [note, setNote] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null)

  async function copy() {
    try {
      await Clipboard.setStringAsync(codes.join('\n'))
      setNote({ tone: 'ok', text: t('mfa.copied') })
    } catch {
      setNote({ tone: 'error', text: t('mfa.copyFailed') })
    }
  }

  async function download() {
    const text = recoveryCodesFile(codes, account)
    if (Platform.OS === 'web') {
      const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'teikem-codigos-de-recuperacion.txt'
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
      return
    }
    try {
      await Share.share({ message: text, title: 'teikem-codigos-de-recuperacion' })
    } catch {
      setNote({ tone: 'error', text: t('mfa.copyFailed') })
    }
  }

  return (
    <>
      <Card>
        {codes.map((c) => (
          <Text key={c} selectable style={{ fontSize: 16, fontFamily: 'monospace' }}>
            {c}
          </Text>
        ))}
      </Card>
      {!!note && <Notice tone={note.tone === 'ok' ? 'ok' : undefined}>{note.text}</Notice>}
      <Button title={t('mfa.copy')} variant="secondary" onPress={() => void copy()} testID="recovery-copy" />
      <Button title={Platform.OS === 'web' ? t('mfa.download') : t('mfa.share')} variant="secondary" onPress={() => void download()} testID="recovery-download" />
      <Check label={t('mfa.savedCheck')} value={saved} onChange={onSavedChange} testID="recovery-saved" />
    </>
  )
}
