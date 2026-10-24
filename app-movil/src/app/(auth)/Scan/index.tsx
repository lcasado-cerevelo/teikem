import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Platform, Pressable, ScrollView, StyleSheet, Switch, TextInput, Vibration, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import * as Speech from 'expo-speech';
import { MaterialIcons } from '@expo/vector-icons';
import { Text } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import Chip from '@components/Chip';
import SearchMultiSelect, { type MultiOption } from '@components/SearchMultiSelect';
import BarcodeScannerModal from '@components/BarcodeScannerModal';
import ScanLookupModal from '@components/ScanLookupModal';
import { useAccess } from '@hooks/access';
import { useIsOnline } from '@hooks/useIsOnline';
import { drainOutbox } from '@/offline';
import { storage } from '@/offline/db';
import { dismissProblem, downloadSnapshot, pendingScans, readMeta, readProblems, scanPiece, type ScanMeta, type ScanProblem } from '@/offline/scanStore';
import { postCloseCount } from '@services/scanApi';
import { ALL_SCOPE, SCAN_ACTIONS, type LocalScanResult, type ScanAction, type ScanScope } from '@utils/scanRules';
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

  // Sin acción por omisión: la pantalla no deja escanear nada hasta que la persona escoja qué proceso va a escanear.
  const [action, setAction] = useState<ScanAction | null>(null);
  // Paso 2 (decidido por Luis): cómo se escanea — todo, por ruta, por ciudad o por chofer — y, si no es todo, cuáles. Solo entonces empieza el escaneo.
  type Mode = 'ALL' | 'ROUTE' | 'CITY' | 'DRIVER';
  const [mode, setMode] = useState<Mode | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [started, setStarted] = useState(false);
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
  const [lookup, setLookup] = useState(false);
  const input = useRef<TextInput>(null);
  const seq = useRef(0);
  const busy = useRef(false);

  const tid = company?.tenantId ?? null;
  const scope: ScanScope = useMemo(() => (mode === 'ROUTE' || mode === 'CITY' || mode === 'DRIVER' ? { mode, ids: [...selected] } : ALL_SCOPE), [mode, selected]);
  const options: MultiOption[] = useMemo(() => {
    if (mode === 'ROUTE') return (meta?.routes ?? []).map((r) => ({ id: r.publicId, label: [r.code, r.driverName, r.status].filter(Boolean).join(' · ') }));
    if (mode === 'CITY') return (meta?.cities ?? []).map((c) => ({ id: c, label: c }));
    if (mode === 'DRIVER') return (meta?.drivers ?? []).map((d) => ({ id: d.publicId, label: d.name }));
    return [];
  }, [mode, meta]);
  const labelOf = useCallback((id: string) => options.find((o) => o.id === id)?.label ?? id, [options]);
  const canStart = action != null && mode != null && (mode === 'ALL' || selected.size > 0);
  const scopeSummary = mode === 'ALL' || mode == null ? t('SCAN.MODE_ALL') : `${t(`SCAN.MODE_${mode}`)}: ${[...selected].slice(0, 3).map((id) => labelOf(id).split(' · ')[0]).join(', ')}${selected.size > 3 ? ` +${selected.size - 3}` : ''}`;


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
    Vibration.vibrate(v === 'found' ? 60 : v === 'dup' ? [0, 60, 60, 60] : v === 'warn' ? [0, 400, 150, 400] : [0, 250]);
    if (!voice) return;
    try {
      Speech.stop();
      Speech.speak(v === 'found' ? t('SCAN.SAY_YES') : v === 'dup' ? t('SCAN.SAY_OK') : v === 'warn' ? t('SCAN.SAY_WARN') : t('SCAN.SAY_NO'), { language: i18n.language?.startsWith('en') ? 'en-US' : 'es-ES', rate: v === 'notfound' ? 0.9 : 1 });
    } catch { /* sin voz en este teléfono: queda la vibración y el color */ }
  };

  const show = (raw: string, a: ScanAction, result: LocalScanResult, orderPublicId: string | null) => {
    setLast({ result, orderPublicId });
    setLines((prev) => [{ id: ++seq.current, code: raw.trim(), action: a, result }, ...prev].slice(0, 30));
    speak(result.voice);
  };

  const submit = useCallback(async (raw: string) => {
    if (tid == null || busy.current || action == null || !started) return;
    busy.current = true;
    setText('');
    try {
      const out = await scanPiece(tid, raw, action, { scope });
      if (out === 'EMPTY') return;
      show(raw, action, out.result, out.orderPublicId);
      // El envío ya va más adelante que lo que se escanea: el sistema PARA y advierte; solo sigue si la persona lo confirma (entonces el envío REGRESA al estatus de la acción).
      if (out.result.outcome === 'BEYOND') {
        const go = await new Promise<boolean>((resolve) => Alert.alert(t('SCAN.BEYOND_TITLE'),
          out.server ? `${out.server.message}\n\n${t('SCAN.BEYOND_HELP')}` : t('SCAN.BEYOND_BODY', { ...out.result.message.params, action: t(ACTION_KEY[action]) }),
          [{ text: t('COMMON.CANCEL'), style: 'cancel', onPress: () => resolve(false) }, { text: t('SCAN.BEYOND_GO'), onPress: () => resolve(true) }], { cancelable: false }));
        if (go) {
          const again = await scanPiece(tid, raw, action, { scope, confirmedBeyond: true });
          if (again !== 'EMPTY') show(raw, action, again.result, again.orderPublicId);
        }
      }
      void refreshLocal();
    } finally {
      busy.current = false;
      setTimeout(() => input.current?.focus(), 30);   // el gatillo siguiente cae en el campo
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tid, action, scope, started, voice]);

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
  const colorOf = (v: LocalScanResult['voice']) => (v === 'found' ? '#1b873f' : v === 'dup' ? '#b7791f' : v === 'warn' ? '#d35400' : '#c0392b');
  const color = !last ? colors.gray : colorOf(last.result.voice);
  const actions: ScanAction[] = [...SCAN_ACTIONS, ...(canReturn ? (['RETURN'] as ScanAction[]) : [])];

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('SCAN.TITLE')} />
      {companies.length > 1 ? (
        <View style={styles.chips}>
          {companies.map((c) => <Chip key={c.tenantId} label={c.name} active={c.tenantId === company?.tenantId} onPress={() => { setTenantId(c.tenantId); setStarted(false); setSelected(new Set()); }} />)}
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

      {!started ? (
        <ScrollView style={styles.setup} keyboardShouldPersistTaps="handled" contentContainerStyle={styles.setupContent}>
          <Text style={[styles.choose, action == null && styles.chooseEmpty]}>{t('SCAN.STEP1')}</Text>
          <View style={styles.chipsInline}>
            {actions.map((a) => <Chip key={a} label={t(ACTION_KEY[a])} active={a === action} onPress={() => setAction(a)} />)}
          </View>
          {action == null ? <Text style={styles.sub}>{t('SCAN.CHOOSE_FIRST')}</Text> : (
            <>
              <Text style={[styles.choose, mode == null && styles.chooseEmpty]}>{t('SCAN.STEP2')}</Text>
              <View style={styles.chipsInline}>
                {(['ALL', 'ROUTE', 'CITY', 'DRIVER'] as const).map((m) => (
                  <Chip key={m} label={t(`SCAN.MODE_${m}`)} active={m === mode} onPress={() => { setMode(m); setSelected(new Set()); }} />
                ))}
              </View>
              {mode === 'ALL' ? <Text style={styles.sub}>{t('SCAN.MODE_ALL_HINT')}</Text> : null}
              {mode === 'ROUTE' || mode === 'CITY' || mode === 'DRIVER' ? (
                <SearchMultiSelect
                  options={options}
                  selected={selected}
                  onToggle={(id) => setSelected((prev) => { const n = new Set(prev); if (n.has(id)) n.delete(id); else n.add(id); return n; })}
                  onClear={() => setSelected(new Set())}
                  placeholder={t(`SCAN.SEARCH_${mode}`)}
                />
              ) : null}
              {action === 'RETURN' && !online ? <Text style={styles.error}>{t('SCAN.RETURN_NEEDS_SIGNAL')}</Text> : null}
              {mode === 'ROUTE' && action === 'ROUTE' && selected.size > 1 ? <Text style={styles.sub}>{t('SCAN.ROUTE_MANY_HINT')}</Text> : null}
              <Pressable
                style={[styles.startBtn, !canStart && styles.disabled]}
                disabled={!canStart}
                accessibilityRole="button"
                onPress={() => { setStarted(true); setTimeout(() => input.current?.focus(), 80); }}
              >
                <Text style={styles.startText}>{t('SCAN.START')}</Text>
              </Pressable>
            </>
          )}
        </ScrollView>
      ) : (
        <View style={styles.summary}>
          <Text style={styles.summaryText} numberOfLines={2}>{`${action ? t(ACTION_KEY[action]) : ''} · ${scopeSummary}`}</Text>
          <Pressable onPress={() => setStarted(false)} accessibilityRole="button" hitSlop={8}><Text style={styles.syncText}>{t('SCAN.CHANGE')}</Text></Pressable>
        </View>
      )}
      {!started ? null : (
      <>
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
        <Pressable onPress={() => setLookup(true)} hitSlop={8} accessibilityRole="button" accessibilityLabel={t('SCAN.LK_BUTTON')} style={styles.cam}>
          <MaterialIcons name="search" size={26} color={colors.primary} />
        </Pressable>
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
            <View style={[styles.dot, { backgroundColor: colorOf(item.result.voice) }]} />
            <View style={styles.lineBody}>
              <Text style={styles.lineCode}>{`${item.code} · ${t(ACTION_KEY[item.action])}`}</Text>
              <Text style={styles.sub}>{t(item.result.message.key, item.result.message.params)}</Text>
            </View>
          </View>
        )}
      />

      </>
      )}

      {tid != null ? <ScanLookupModal visible={lookup} tenantId={tid} onClose={() => { setLookup(false); setTimeout(() => input.current?.focus(), 80); }} /> : null}
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
  setup: { flexGrow: 0, maxHeight: '70%' },
  setupContent: { paddingHorizontal: 20, paddingBottom: 12, gap: 10 },
  chipsInline: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  startBtn: { backgroundColor: colors.primary, borderRadius: 12, paddingVertical: 14, alignItems: 'center', marginTop: 4 },
  startText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 16 },
  summary: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: 10, marginHorizontal: 20, marginBottom: 6, padding: 10, borderRadius: 10, backgroundColor: colors.backgroundSecondary },
  summaryText: { flex: 1, fontWeight: '700', color: colors.text },
  choose: { fontWeight: '700', fontSize: 15, color: colors.text },
  chooseEmpty: { fontSize: 18, color: colors.primary },
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
