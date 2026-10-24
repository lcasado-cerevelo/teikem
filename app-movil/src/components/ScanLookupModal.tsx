import { useState } from 'react';
import { ActivityIndicator, Modal, Pressable, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import { MaterialIcons } from '@expo/vector-icons';
import { Text } from '@components/Text';
import { lookupPiece } from '@/offline/scanStore';
import type { LookupResult } from '@services/scanApi';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

const RED = ['ORDER_CANCELLED', 'ORDER_FINAL', 'ORDER_RETURNED', 'ORDER_INACTIVE', 'PIECE_INACTIVE'];
const AMBER = ['PIECE_LOST', 'PIECE_CLAIMED', 'PIECE_TO_VERIFY', 'PIECE_RESOLVED', 'REAPPEARED'];

// Lote 216 — la lupa (Luis): buscar por el número de la pieza lo que sea que haya en la base de datos, para saber por qué un escaneo no la encontró (cancelada, estatus final, dada de baja, reclamación, perdida…). No cuenta como escaneo.
// Con señal consulta la base; sin señal muestra solo lo bajado y lo dice.
export default function ScanLookupModal({ visible, tenantId, onClose }: { visible: boolean; tenantId: number; onClose: () => void }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const [res, setRes] = useState<LookupResult | null>(null);
  const [failed, setFailed] = useState(false);

  const search = async () => {
    setBusy(true);
    setFailed(false);
    try {
      const r = await lookupPiece(tenantId, text);
      setRes(r === 'EMPTY' ? null : r);
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <SafeAreaView style={styles.container}>
        <View style={styles.header}>
          <Text style={styles.title}>{t('SCAN.LK_TITLE')}</Text>
          <Pressable onPress={onClose} hitSlop={10} accessibilityRole="button" accessibilityLabel={t('COMMON.CANCEL')}><MaterialIcons name="close" size={26} color={colors.text} /></Pressable>
        </View>
        <Text style={styles.help}>{t('SCAN.LK_HELP')}</Text>
        <View style={styles.row}>
          <TextInput style={styles.input} value={text} onChangeText={setText} onSubmitEditing={() => void search()} placeholder={t('SCAN.LK_PLACEHOLDER')} placeholderTextColor={colors.muted} autoCapitalize="none" autoCorrect={false} autoFocus returnKeyType="search" accessibilityLabel={t('SCAN.LK_PLACEHOLDER')} />
          <Pressable style={[styles.go, !text.trim() && styles.disabled]} disabled={!text.trim() || busy} onPress={() => void search()} accessibilityRole="button" accessibilityLabel={t('SCAN.LK_SEARCH')}>
            {busy ? <ActivityIndicator color={colors.primaryContrast} /> : <MaterialIcons name="search" size={24} color={colors.primaryContrast} />}
          </Pressable>
        </View>
        <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.body}>
          {failed ? <Text style={styles.red}>{t('SCAN.LK_ERROR')}</Text> : null}
          {res?.offline ? <Text style={styles.amber}>{t('SCAN.LK_OFFLINE')}</Text> : null}
          {res && res.items.length === 0 ? <Text style={styles.help}>{t('SCAN.LK_NONE', { code: res.code })}</Text> : null}
          {res && res.matchedBy && res.matchedBy !== 'PIECE' ? <Text style={styles.help}>{t('SCAN.LK_MATCHED_ORDER', { by: t(`SCAN.LK_BY_${res.matchedBy}`, { defaultValue: res.matchedBy }) })}</Text> : null}
          {res?.items.map((it, i) => (
            <View key={`${it.orderNumber}-${it.pieceNumber ?? i}`} style={styles.card}>
              <Text style={styles.ref}>{it.pieceNumber ?? it.orderNumber}{it.pieceNumber == null ? `  (${t('SCAN.LK_IS_ORDER')})` : ''}</Text>
              <View style={styles.flags}>
                {it.flags.map((f) => <Text key={f} style={[styles.flag, RED.includes(f) ? styles.flagRed : AMBER.includes(f) ? styles.flagAmber : styles.flagInfo]}>{t(`SCAN.LK_FLAG_${f}`)}</Text>)}
              </View>
              {([
                ['SCAN.LK_ORDER', it.orderNumber], ['SCAN.LK_WAYBILL', it.waybillNumber], ['SCAN.LK_STATUS', it.status], ['SCAN.LK_CLIENT', it.clientName],
                ['SCAN.LK_CONSIGNEE', [it.consigneeName, it.city].filter(Boolean).join(' · ')],
                ['SCAN.LK_ROUTE', it.tripCode ? [it.tripCode, it.tripStatus, it.driverName].filter(Boolean).join(' · ') : null],
                ['SCAN.LK_CLAIM', it.claimNumber ? [it.claimNumber, it.claimStatus].filter(Boolean).join(' · ') : null], ['SCAN.LK_PIECES', String(it.pieces)],
              ] as [string, string | null][]).filter(([, v]) => v).map(([k, v]) => (
                <Text key={k} style={styles.line}><Text style={styles.key}>{t(k)}: </Text>{v}</Text>
              ))}
              {it.scans.length > 0 ? <Text style={styles.line}><Text style={styles.key}>{t('SCAN.LK_SCANS')}: </Text>{it.scans.map((s) => t(`SCAN.A_${s.action}`)).join(', ')}</Text> : null}
            </View>
          ))}
        </ScrollView>
      </SafeAreaView>
    </Modal>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, paddingHorizontal: 20 },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', paddingVertical: 14 },
  title: { fontSize: 20, fontWeight: '700', color: colors.text },
  help: { color: colors.muted, fontSize: 13, marginBottom: 8 },
  row: { flexDirection: 'row', gap: 8, alignItems: 'center', marginBottom: 10 },
  input: { flex: 1, borderWidth: 2, borderColor: colors.primary, borderRadius: 12, paddingHorizontal: 12, paddingVertical: 10, fontSize: 18, color: colors.text },
  go: { backgroundColor: colors.primary, borderRadius: 12, width: 48, height: 48, alignItems: 'center', justifyContent: 'center' },
  disabled: { opacity: 0.4 },
  body: { paddingBottom: 30, gap: 10 },
  red: { color: '#c0392b', fontWeight: '700' },
  amber: { color: '#b7791f', fontWeight: '700' },
  card: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, padding: 12, gap: 4 },
  ref: { fontWeight: '700', fontSize: 17, color: colors.text },
  flags: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, marginVertical: 4 },
  flag: { fontSize: 12, fontWeight: '700', paddingHorizontal: 8, paddingVertical: 3, borderRadius: 10, overflow: 'hidden' },
  flagRed: { backgroundColor: '#fdecea', color: '#a93226' },
  flagAmber: { backgroundColor: '#fef5e7', color: '#9c6500' },
  flagInfo: { backgroundColor: '#eaf2fb', color: '#1f4e79' },
  line: { color: colors.text, fontSize: 13 },
  key: { color: colors.muted, fontWeight: '700' },
});
