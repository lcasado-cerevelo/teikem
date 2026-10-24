import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Platform, Pressable, StyleSheet, Switch, TextInput, Vibration, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import * as Speech from 'expo-speech';
import { MaterialIcons } from '@expo/vector-icons';
import { Text } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import Chip from '@components/Chip';
import OptionSelect from '@components/OptionSelect';
import BarcodeScannerModal from '@components/BarcodeScannerModal';
import { useAccess } from '@hooks/access';
import { useIsOnline } from '@hooks/useIsOnline';
import { drainOutbox } from '@/offline';
import { storage } from '@/offline/db';
import { dismissProblem, downloadSnapshot, pendingScans, readMeta, readProblems, scanPiece, type ScanMeta, type ScanProblem } from '@/offline/scanStore';
import { postCloseCount } from '@services/scanApi';
import { SCAN_ACTIONS, type LocalScanResult, type ScanAction } from '@utils/scanRules';
import { minutesSince } from '@utils/streetRules';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

const STALE_MINUTES = 30;
const ACTION_KEY: Record<ScanAction, string> = { PICKUP: 'SCAN.A_PICKUP', ARRIVE: 'SCAN.A_ARRIVE', ROUTE: 'SCAN.A_ROUTE', DEPART: 'SCAN.A_DEPART', RETURN: 'SCAN.A_RETURN' };

interface Line { id: number; code: string; action: ScanAction; result: LocalScanResult }

// Lote 216 — Estación de escaneo de rutas para el Android con gatillo (trips.scan). Funciona SIN SEÑAL: se baja la descarga (hasta 5000 envíos no completados, los más recientes) y cada lectura se contesta al instante;
// los escaneos quedan en la cola y salen al servidor cuando hay señal. Las mismas acciones de la web: Recoger, Llegó, Enrutar, Salir y Devolver (esta última exige señal y orders.return).
export default function Scan() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const access = useAccess();
  const online = useIsOnline();
  const companies = useMemo(() => access.companiesWith('trips.scan'), [access]);
  const [tenantId, setTenantId] = useState<number | null>(null);
  const company = companies.find((c) => c.tenantId === tenantId) ?? companies[0] ?? null;
  const canReturn = !!company && access.companiesWith('orders.return').some((c) => c.tenantId === company.tenantId);

  const [action, setAction] = useState<ScanAction>('ROUTE');
  const [routeIdx, setRouteIdx] = useState<number | null>(null);
  const [meta, setMeta] = useState<ScanMeta | null>(null);
  const [downloading, setDownloading] = useState<{ done: number; total: number } | null>(null);
  const [downloadError, setDownloadError] = useState(false);
  const [pending, setPending] = useState(0);
  const [problems, setProblems] = useState<ScanProblem[]>([]);
  const [showProblems, setShowProblems] = useState(false);
  const [text, setText] = useState('');
  const [lines, setLines] = useState<Line[]>([]);
  const [last, setLast] = useState<{ result: LocalScanResult; orderPublicId: string | null } | null>(null);
  const [voice, setVoice] = useState(true);
  const [camera, setCamera] = useState(false);
  const input = useRef<TextInput>(null);
  const seq = useRef(0);
  const busy = useRef(false);

  const routes = useMemo(() => meta?.routes ?? [], [meta]);
  const route = routeIdx != null ? routes[routeIdx - 1] : null;
  const tid = company?.tenantId ?? null;

  const refreshLocal = useCallback(async () => {
    if (tid == null) return;
    setMeta(await readMeta(tid));
    setPending((await pendingScans(tid)).length);
    setProblems(await readProblems(tid));
  }, [tid]);

  const download = useCallback(async () => {
    if (tid == null || downloading) return;
    setDownloadError(false);
    setDownloading({ done: 0, total: 0 });
    try {
      await drainOutbox();   // primero lo escaneado: así la descarga ya lo trae
      await downloadSnapshot(tid, (done, total) => setDownloading({ done, total }));
    } catch {
      setDownloadError(true);
    } finally {
      setDownloading(null);
      await refreshLocal();
    }
  }, [tid, downloading, refreshLocal]);

  // al abrir (o cambiar de compañía): lo guardado, y si falta o está viejo y hay señal, se baja de nuevo
  useEffect(() => {
    let alive = true;
    void (async () => {
      await refreshLocal();
      if (!alive || tid == null) return;
      const m = await readMeta(tid);
      const age = minutesSince(m?.downloadedAtUtc);
      if (online && (!m || age == null || age > STALE_MINUTES)) void download();
    })();
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tid]);

  // el contador de pendientes y la salida de la cola
  useEffect(() => {
    const id = setInterval(() => void refreshLocal(), 4000);
    return () => clearInterval(id);
  }, [refreshLocal]);
  useEffect(() => {
    if (online) void drainOutbox().then(refreshLocal);
  }, [online, refreshLocal]);

  useEffect(() => {
    void storage.kvGet('scan.voice').then((v) => { if (v === '0') setVoice(false); });
  }, []);

  const speak = (v: LocalScanResult['voice']) => {
    Vibration.vibrate(v === 'found' ? 60 : v === 'dup' ? [0, 60, 60, 60] : [0, 250]);
    if (!voice) return;
    try {
      Speech.stop();
      Speech.speak(v === 'found' ? t('SCAN.SAY_YES') : v === 'dup' ? t('SCAN.SAY_OK') : t('SCAN.SAY_NO'), { language: i18n.language?.startsWith('en') ? 'en-US' : 'es-ES' });
    } catch { /* sin voz en este teléfono: queda la vibración y el color */ }
  };

  const submit = useCallback(async (raw: string) => {
    if (tid == null || busy.current) return;
    busy.current = true;
    setText('');
    try {
      const out = await scanPiece(tid, raw, action, { tripPublicId: action === 'ROUTE' ? route?.publicId ?? null : null, tripCode: action === 'ROUTE' ? route?.code ?? null : null });
      if (out === 'EMPTY') return;
      setLast({ result: out.result, orderPublicId: out.orderPublicId });
      setLines((prev) => [{ id: ++seq.current, code: raw.trim(), action, result: out.result }, ...prev].slice(0, 30));
      speak(out.result.voice);
      void refreshLocal();
    } finally {
      busy.current = false;
      setTimeout(() => input.current?.focus(), 30);   // el gatillo siguiente cae en el campo
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tid, action, route, voice]);

  const closeCount = async () => {
    if (tid == null || !last?.orderPublicId) return;
    try {
      let r = await postCloseCount(tid, last.orderPublicId, false);
      if (r.needsConfirmation) {
        const go = await new Promise<boolean>((resolve) => Alert.alert(t('SCAN.CLOSE_TITLE'), r.message, [
          { text: t('COMMON.CANCEL'), style: 'cancel', onPress: () => resolve(false) },
          { text: t('SCAN.CLOSE_OK'), onPress: () => resolve(true) },
        ]));
        if (!go) return;
        r = await postCloseCount(tid, last.orderPublicId, true);
      }
      Alert.alert(t('SCAN.CLOSE_TITLE'), r.message);
    } catch {
      Alert.alert(t('SCAN.CLOSE_TITLE'), t('SCAN.CLOSE_ERROR'));
    }
  };

  const age = minutesSince(meta?.downloadedAtUtc);
  const color = !last ? colors.gray : last.result.voice === 'found' ? '#1b873f' : last.result.voice === 'dup' ? '#b7791f' : '#c0392b';
  const actions: ScanAction[] = [...SCAN_ACTIONS, ...(canReturn ? (['RETURN'] as ScanAction[]) : [])];

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('SCAN.TITLE')} />
      {companies.length > 1 ? (
        <View style={styles.chips}>
          {companies.map((c) => <Chip key={c.tenantId} label={c.name} active={c.tenantId === company?.tenantId} onPress={() => setTenantId(c.tenantId)} />)}
        </View>
      ) : null}

      <View style={styles.statusRow}>
        <Text style={styles.sub}>
          {meta ? t('SCAN.DOWNLOADED', { n: meta.total, m: age ?? 0 }) : t('SCAN.NOT_DOWNLOADED')}
          {meta?.capped ? ` · ${t('SCAN.CAPPED', { n: 5000 })}` : ''}
        </Text>
        <Text style={[styles.sub, { color: online ? colors.primary : '#c0392b', fontWeight: '700' }]}>{online ? t('SCAN.ONLINE') : t('SCAN.OFFLINE')}</Text>
      </View>
      <View style={styles.statusRow}>
        <Text style={styles.sub}>{t('SCAN.PENDING', { n: pending })}</Text>
        <Pressable onPress={() => void download()} disabled={!online || !!downloading} accessibilityRole="button" style={[styles.syncBtn, (!online || !!downloading) && styles.disabled]}>
          {downloading ? <ActivityIndicator size="small" color={colors.primary} /> : <MaterialIcons name="sync" size={18} color={colors.primary} />}
          <Text style={styles.syncText}>{downloading ? t('SCAN.DOWNLOADING', { done: downloading.done, total: downloading.total }) : t('SCAN.SYNC')}</Text>
        </Pressable>
      </View>
      {downloadError ? <Text style={styles.error}>{t('SCAN.DOWNLOAD_ERROR')}</Text> : null}

      <View style={styles.chips}>
        {actions.map((a) => <Chip key={a} label={t(ACTION_KEY[a])} active={a === action} onPress={() => setAction(a)} />)}
      </View>
      {action === 'ROUTE' ? (
        <View style={styles.pad}>
          <OptionSelect
            options={routes.map((r, i) => ({ id: i + 1, label: `${r.code} · ${r.driverName ?? t('TRIPS_TODAY.NO_DRIVER')} · ${r.status}` }))}
            value={routeIdx}
            placeholder={t('SCAN.ROUTE_PLACEHOLDER')}
            onSelect={setRouteIdx}
            onClear={() => setRouteIdx(null)}
          />
        </View>
      ) : null}
      {action === 'RETURN' && !online ? <Text style={styles.error}>{t('SCAN.RETURN_NEEDS_SIGNAL')}</Text> : null}

      <View style={styles.inputRow}>
        <TextInput
          ref={input}
          style={styles.input}
          value={text}
          onChangeText={setText}
          onSubmitEditing={() => void submit(text)}
          autoFocus
          autoCapitalize="none"
          autoCorrect={false}
          blurOnSubmit={false}
          returnKeyType="done"
          // el lector con gatillo escribe como un teclado: no hace falta que salga el teclado en pantalla
          showSoftInputOnFocus={Platform.OS === 'web'}
          placeholder={t('SCAN.PLACEHOLDER')}
          placeholderTextColor={colors.muted}
          accessibilityLabel={t('SCAN.PLACEHOLDER')}
        />
        {Platform.OS !== 'web' ? (
          <Pressable onPress={() => setCamera(true)} hitSlop={8} accessibilityLabel={t('ORDERS.SCAN_OPEN')} style={styles.cam}>
            <MaterialIcons name="qr-code-scanner" size={26} color={colors.primary} />
          </Pressable>
        ) : null}
      </View>

      <View style={[styles.result, { borderColor: color }]}>
        {last ? (
          <>
            <Text style={[styles.resultMsg, { color }]}>{t(last.result.message.key, last.result.message.params)}</Text>
            {last.result.total > 0 ? <Text style={styles.resultSub}>{t('SCAN.COUNT', { a: last.result.scanned, b: last.result.total })}{last.result.orderNumber ? ` · ${last.result.orderNumber}` : ''}</Text> : null}
            {last.result.routeNote ? <Text style={styles.resultSub}>{t(last.result.routeNote.key, last.result.routeNote.params)}</Text> : null}
          </>
        ) : (
          <Text style={styles.sub}>{t('SCAN.READY')}</Text>
        )}
      </View>

      <View style={styles.toolsRow}>
        <View style={styles.voiceRow}>
          <Switch value={voice} onValueChange={(v) => { setVoice(v); void storage.kvSet('scan.voice', v ? '1' : '0'); }} accessibilityLabel={t('SCAN.VOICE')} />
          <Text style={styles.sub}>{t('SCAN.VOICE')}</Text>
        </View>
        <Pressable onPress={() => void closeCount()} disabled={!online || !last?.orderPublicId} accessibilityRole="button" style={[styles.syncBtn, (!online || !last?.orderPublicId) && styles.disabled]}>
          <Text style={styles.syncText}>{t('SCAN.CLOSE_COUNT')}</Text>
        </Pressable>
        <Pressable onPress={() => setShowProblems((v) => !v)} accessibilityRole="button" style={styles.syncBtn}>
          <Text style={[styles.syncText, problems.length > 0 && { color: '#c0392b' }]}>{t('SCAN.PROBLEMS', { n: problems.length })}</Text>
        </Pressable>
      </View>

      {showProblems ? (
        <View style={styles.pad}>
          {problems.length === 0 ? <Text style={styles.sub}>{t('SCAN.NO_PROBLEMS')}</Text> : (
            <>
              {problems.slice(0, 8).map((p) => (
                <View key={p.id} style={styles.problem}>
                  <Text style={styles.problemMsg}>{`${p.code}${p.orderNumber ? ` (${p.orderNumber})` : ''} · ${t(ACTION_KEY[p.action as ScanAction] ?? 'SCAN.A_ROUTE')}`}</Text>
                  <Text style={styles.sub}>{p.message}</Text>
                  <Pressable onPress={() => { if (tid != null) void dismissProblem(tid, p.id).then(refreshLocal); }} accessibilityRole="button"><Text style={styles.syncText}>{t('SCAN.DISMISS')}</Text></Pressable>
                </View>
              ))}
              <Pressable onPress={() => { if (tid != null) void dismissProblem(tid, 'all').then(refreshLocal); }} accessibilityRole="button"><Text style={styles.syncText}>{t('SCAN.DISMISS_ALL')}</Text></Pressable>
            </>
          )}
        </View>
      ) : null}

      <FlatList
        data={lines}
        keyExtractor={(l) => String(l.id)}
        style={styles.list}
        keyboardShouldPersistTaps="handled"
        renderItem={({ item }) => (
          <View style={styles.line}>
            <View style={[styles.dot, { backgroundColor: item.result.voice === 'found' ? '#1b873f' : item.result.voice === 'dup' ? '#b7791f' : '#c0392b' }]} />
            <View style={styles.lineBody}>
              <Text style={styles.lineCode}>{`${item.code} · ${t(ACTION_KEY[item.action])}`}</Text>
              <Text style={styles.sub}>{t(item.result.message.key, item.result.message.params)}</Text>
            </View>
          </View>
        )}
      />

      <BarcodeScannerModal visible={camera} onClose={() => setCamera(false)} onScanned={(code) => { setCamera(false); void submit(code); }} />
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  pad: { marginHorizontal: 20, marginBottom: 6 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginHorizontal: 20, marginBottom: 8 },
  statusRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginHorizontal: 20, marginBottom: 4 },
  sub: { fontSize: 12, color: colors.muted },
  error: { marginHorizontal: 20, color: '#c0392b', fontWeight: '600', marginBottom: 4 },
  syncBtn: { flexDirection: 'row', alignItems: 'center', gap: 6, paddingVertical: 4, paddingHorizontal: 8 },
  syncText: { color: colors.primary, fontWeight: '700', fontSize: 12 },
  disabled: { opacity: 0.4 },
  inputRow: { flexDirection: 'row', alignItems: 'center', gap: 8, marginHorizontal: 20, marginVertical: 8, borderWidth: 2, borderColor: colors.primary, borderRadius: 12, paddingHorizontal: 12 },
  input: { flex: 1, fontSize: 20, paddingVertical: 12, color: colors.text },
  cam: { padding: 4 },
  result: { marginHorizontal: 20, borderWidth: 3, borderRadius: 14, padding: 14, minHeight: 92, justifyContent: 'center', gap: 4 },
  resultMsg: { fontSize: 18, fontWeight: '700' },
  resultSub: { fontSize: 13, color: colors.text },
  toolsRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginHorizontal: 20, marginTop: 8 },
  voiceRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  problem: { borderWidth: 1, borderColor: colors.gray, borderRadius: 10, padding: 8, marginBottom: 6, gap: 2 },
  problemMsg: { fontWeight: '700', color: colors.text, fontSize: 13 },
  list: { marginTop: 8, marginHorizontal: 20 },
  line: { flexDirection: 'row', gap: 10, paddingVertical: 6, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.gray, alignItems: 'center' },
  dot: { width: 10, height: 10, borderRadius: 5 },
  lineBody: { flex: 1 },
  lineCode: { fontWeight: '700', color: colors.text, fontSize: 13 },
});
