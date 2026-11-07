import { useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView, useSafeAreaInsets } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import CompanyChips from '@components/CompanyChips';
import ClientStep from '@components/sales/ClientStep';
import ProductsStep, { CartBar } from '@components/sales/ProductsStep';
import DeliveryStep from '@components/sales/DeliveryStep';
import { AuthorizationStep, SignatureBlock } from '@components/sales/AuthorizationStep';
import { PasswordSheet, ShortageSheet } from '@components/sales/SubmitSheets';
import { Badge, Button, ErrorText, Help, SumRow } from '@components/sales/ui';
import { useAccess } from '@hooks/access';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { authenticate, isBiometricAvailable } from '@services/auth/biometricAuth';
import {
  checkAvailability, createDraft, fetchClientForOrder, fetchLookups, fetchMfaEnabled, fetchSalesOrder, fetchWarehouses, removeSignature, submitOrder,
  updateDraft, uploadSignature, type ClientForOrder, type SalesAvailability, type SalesOrder,
} from '@services/salesApi';
import {
  afterBiometric, emptyWizard, errorMessage, firstAuthStep, firstInvalidStep, isPaymentChanged, money, orderSummary, requiredDateValid, resumeStep,
  signatureAction, signatureBase64, stateFromDraft, stepError, stepIndex, stepRequest, submitBody, submitOutcome, toApiError, WIZARD_STEPS, withClient,
  withClientPayment, withServerPrices, type StepContext, type SubmitAuth, type WizardErrorKey, type WizardState, type WizardStep,
} from '@utils/salesOrderRules';

// Lote 226 (P4 del pedido de vendedores) — NUEVO PEDIDO en la app del personal (sales.create): los mismos cuatro pasos del pedido guiado de la web (Cliente →
// Productos → Entrega → Autorización) con su barra de progreso. «Siguiente» guarda el BORRADOR de la orden de venta en el servidor con el paso (POST la primera
// vez, PATCH después): si el teléfono se apaga, el pedido está en «Mis pedidos» (y en Órdenes de venta de la web) como borrador y se retoma donde iba. Sin señal no
// se avanza (no hay modo sin señal en esta versión). «Enviar pedido» sube la firma dibujada con el dedo, pide la HUELLA o el rostro (la misma de la pantalla de
// bloqueo) y manda authMethod=BIOMETRIC; si el teléfono no la tiene, si no se confirma o si la verificación de la sesión venció (403 aal2_required), pide la
// contraseña (y el código MFA). Si falta inventario (409) o se pasa del crédito (422), la hoja del faltante repite el envío. Al final: OV-#####, total y «Nuevo pedido».

type StepErr = WizardErrorKey | 'requiredDate';
type PasswordReason = 'NO_BIOMETRIC' | 'BIOMETRIC_FAILED' | 'EXPIRED';

export default function NewSalesOrder() {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const insets = useSafeAreaInsets();
  const router = useRouter();
  const qc = useQueryClient();
  const access = useAccess();
  const params = useLocalSearchParams<{ tenantId?: string; order?: string }>();
  const companies = useMemo(() => access.companiesWith('sales.create'), [access]);
  const [tenantPick, setTenantPick] = useState<number | null>(params.tenantId ? Number(params.tenantId) : null);
  const company = companies.find((c) => c.tenantId === tenantPick) ?? companies[0] ?? null;
  const tenantId = company?.tenantId ?? 0;

  const [resumeId] = useState(() => (typeof params.order === 'string' && params.order ? params.order : null));
  const [resumeDone, setResumeDone] = useState(resumeId === null);
  const [state, setState] = useState<WizardState>(emptyWizard);
  const [step, setStep] = useState<WizardStep>('CLIENT');
  const [saved, setSaved] = useState<SalesOrder | null>(null);
  const [drawn, setDrawn] = useState<string | null>(null);
  const [sigDirty, setSigDirty] = useState(false);
  const [stepErr, setStepErr] = useState<StepErr | null>(null);
  const [serverErr, setServerErr] = useState<string | null>(null);
  const [prefillFor, setPrefillFor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [recalculating, setRecalculating] = useState(false);
  const [password, setPassword] = useState<{ reason: PasswordReason; error: string | null; field: 'password' | 'mfaCode' | null } | null>(null);
  const [shortage, setShortage] = useState<{ check: SalesAvailability; error: string | null } | null>(null);
  const [done, setDone] = useState<SalesOrder | null>(null);
  const auth = useRef<SubmitAuth | null>(null);
  const scrollRef = useRef<ScrollView>(null);

  const resumed = useQuery({ queryKey: ['sales-order', tenantId, resumeId], queryFn: () => fetchSalesOrder(tenantId, resumeId!), enabled: !!resumeId && !!tenantId && !resumeDone });
  const client = useQuery({
    queryKey: ['sales-client', tenantId, state.clientPublicId ?? ''],
    queryFn: () => fetchClientForOrder(tenantId, state.clientPublicId!),
    enabled: !!tenantId && !!state.clientPublicId,
    staleTime: 60_000,
  });
  const methods = useQuery({ queryKey: ['sales-lookups', tenantId, 'SalesPaymentMethod'], queryFn: () => fetchLookups(tenantId, 'SalesPaymentMethod'), enabled: !!tenantId, staleTime: 600_000 });
  const terms = useQuery({ queryKey: ['sales-lookups', tenantId, 'PaymentTerm'], queryFn: () => fetchLookups(tenantId, 'PaymentTerm'), enabled: !!tenantId, staleTime: 600_000 });
  const warehouses = useQuery({ queryKey: ['sales-warehouses', tenantId], queryFn: () => fetchWarehouses(tenantId), enabled: !!tenantId, staleTime: 600_000 });
  const mfa = useQuery({ queryKey: ['sales-mfa', tenantId], queryFn: () => fetchMfaEnabled(tenantId), enabled: !!tenantId, staleTime: 600_000 });

  const needsWarehouse = (warehouses.data?.length ?? 0) > 1;
  const requiresSignature = client.data?.requireOrderSignature ?? saved?.terms?.clientRequiresSignature ?? false;
  const savedSignature = saved?.terms?.signatureFilePublicId ?? null;
  const hasSignature = signatureBase64(drawn) !== '' || (!!savedSignature && !sigDirty);
  const ctx: StepContext = { requiresSignature, hasSignature, needsWarehouse };
  const physicalId = client.data?.physicalAddress?.isActive === false ? null : client.data?.physicalAddress?.publicId ?? null;
  const idx = stepIndex(step);

  // retomar un borrador (desde «Mis pedidos»): solo uno que siga en borrador
  if (!resumeDone && resumed.data) {
    const dto = resumed.data;
    setResumeDone(true);
    if ((dto.status ?? '').toUpperCase() === 'DRAFT') {
      setState(stateFromDraft(dto));
      setSaved(dto);
      setStep(resumeStep(dto));
    } else {
      setServerErr(t('SALES_ORDER.RESUME_NOT_DRAFT', { number: dto.number ?? '' }));
    }
  }
  if (!resumeDone && resumed.isError) setResumeDone(true);

  // la forma de pago se prellena con la del cliente al escogerlo (no al retomar: el borrador conserva la suya)
  if (prefillFor && client.data?.publicId === prefillFor) {
    const term = client.data.paymentTerm;
    setPrefillFor(null);
    setState((s) => (s.clientPublicId === prefillFor ? withClientPayment(s, term) : s));
  }

  // al cambiar de paso, la vista vuelve arriba
  useEffect(() => {
    scrollRef.current?.scrollTo({ y: 0, animated: false });
  }, [step, done]);

  const problem = (e: unknown) => {
    const api = toApiError(e);
    return api.network ? t('SALES_ORDER.NO_SIGNAL') : errorMessage(api) ?? t('SALES_ORDER.ERROR');
  };

  const change = (next: (s: WizardState) => WizardState) => {
    setStepErr(null);
    setState(next);
  };

  const pickClient = (publicId: string | null, label: string) => {
    setStepErr(null);
    setState((s) => withClient(s, publicId, label));
    setPrefillFor(publicId);
  };

  /** Guarda el borrador con lo que exige `which` y deja los precios y el número que devolvió el servidor. */
  const save = async (which: WizardStep, s: WizardState): Promise<SalesOrder> => {
    const body = stepRequest(which, s);
    const dto = s.orderPublicId ? await updateDraft(tenantId, s.orderPublicId, body) : await createDraft(tenantId, body);
    setSaved(dto);
    setState((cur) => ({ ...cur, orderPublicId: dto.publicId ?? cur.orderPublicId, number: dto.number ?? cur.number, cart: withServerPrices(cur.cart, dto.lines) }));
    void qc.invalidateQueries({ queryKey: ['my-sales-orders'] });
    return dto;
  };

  const goNext = async () => {
    setServerErr(null);
    const err = stepError(step, state, ctx) ?? (step === 'DELIVERY' && !requiredDateValid(state.requiredDate) ? 'requiredDate' : null);
    if (err) return setStepErr(err);
    let s = state;
    // a la entrega se llega con la dirección física del cliente ya escogida (se puede cambiar)
    if (step === 'PRODUCTS' && s.deliveryMode === 'DELIVERY' && !s.consigneePublicId && physicalId) s = { ...s, consigneePublicId: physicalId };
    if (s !== state) setState(s);
    setBusy(true);
    try {
      await save(step, s);
      setStep(WIZARD_STEPS[idx + 1]);
    } catch (e) {
      setServerErr(problem(e));
    } finally {
      setBusy(false);
    }
  };

  const goBack = () => {
    setStepErr(null);
    setServerErr(null);
    if (idx > 0) setStep(WIZARD_STEPS[idx - 1]);
  };

  /** Entrega o consignatario cambiados: se guarda el borrador para que el servidor vuelva a calcular el envío. */
  const recalc = async (patch: Partial<WizardState>) => {
    setStepErr(null);
    setServerErr(null);
    const s = { ...state, ...patch };
    setState(s);
    if (!s.orderPublicId) return;
    setRecalculating(true);
    try {
      await save('PRODUCTS', s);
    } catch (e) {
      setServerErr(problem(e));
    } finally {
      setRecalculating(false);
    }
  };

  /** La firma dibujada sube como PNG (o se quita si se borró la que había); el nombre de quien autoriza va con ella. */
  const syncSignature = async (id: string) => {
    const action = signatureAction(sigDirty, drawn, savedSignature);
    if (action === 'upload') setSaved(await uploadSignature(tenantId, id, signatureBase64(drawn), state.authorizedByName.trim()));
    else if (action === 'remove') setSaved(await removeSignature(tenantId, id));
    setSigDirty(false);
  };

  /** POST /submit con la reautenticación que se tiene en memoria; devuelve el error tal cual para que lo clasifique quien llama. */
  const send = async (allowBackorder: boolean, overrideCredit: boolean) => {
    const a = auth.current!;
    const dto = await submitOrder(tenantId, state.orderPublicId!, submitBody(a, { mfa: !!mfa.data || (a.method === 'PASSWORD' && a.mfaCode.trim() !== ''), allowBackorder, overrideCredit }));
    auth.current = null;
    setPassword(null);
    setShortage(null);
    setDone(dto);
    void qc.invalidateQueries({ queryKey: ['my-sales-orders'] });
    return dto;
  };

  /** Lo que pasó al enviar: la contraseña mala se queda en su hoja; el faltante o el crédito abren la hoja del faltante; la verificación vencida pide la contraseña. */
  const handleSendError = async (e: unknown) => {
    const api = toApiError(e);
    const outcome = submitOutcome(api);
    if (outcome === 'password' || outcome === 'mfa') {
      setPassword((p) => ({ reason: p?.reason ?? 'NO_BIOMETRIC', error: errorMessage(api), field: outcome === 'mfa' ? 'mfaCode' : 'password' }));
      return;
    }
    if (outcome === 'reauth') {
      auth.current = null;
      setShortage(null);
      setPassword({ reason: 'EXPIRED', error: null, field: null });
      return;
    }
    if (outcome === 'shortage' || outcome === 'credit') {
      // la reautenticación ya se verificó: la hoja del faltante repite con ella (backorder o crédito autorizado)
      try {
        const check = await checkAvailability(tenantId, state.orderPublicId!);
        // en iOS una hoja no se abre mientras otra se está cerrando: si estaba la de la contraseña, se espera a que se cierre
        const hadPassword = !!password;
        setPassword(null);
        if (hadPassword) setTimeout(() => setShortage({ check, error: null }), 400);
        else setShortage({ check, error: null });
      } catch (e2) {
        setServerErr(problem(e2));
      }
      return;
    }
    auth.current = null;
    if (password) setPassword({ ...password, error: problem(e), field: null });
    else setServerErr(problem(e));
  };

  const trySend = async (allowBackorder = false, overrideCredit = false) => {
    // nunca se manda sin haber confirmado antes (huella o contraseña) en esta misma acción
    if (!auth.current) {
      setShortage(null);
      setPassword({ reason: 'EXPIRED', error: null, field: null });
      return;
    }
    setBusy(true);
    try {
      await send(allowBackorder, overrideCredit);
    } catch (e) {
      if (shortage && submitOutcome(toApiError(e)) !== 'reauth') setShortage({ ...shortage, error: problem(e) });
      else await handleSendError(e);
    } finally {
      setBusy(false);
    }
  };

  const startSubmit = async () => {
    setServerErr(null);
    const bad = firstInvalidStep(state, ctx);
    if (bad) {
      setStep(bad.step);
      setStepErr(bad.error);
      return;
    }
    setBusy(true);
    try {
      const dto = await save('AUTHORIZATION', state);
      await syncSignature(dto.publicId);
    } catch (e) {
      setServerErr(problem(e));
      setBusy(false);
      return;
    }
    setBusy(false);
    // la huella (o el rostro, o el PIN del teléfono) como en la pantalla de bloqueo; sin huella, la contraseña
    if (firstAuthStep(await isBiometricAvailable()) === 'PASSWORD') return setPassword({ reason: 'NO_BIOMETRIC', error: null, field: null });
    const ok = await authenticate(t('SALES_ORDER.SEND.BIOMETRIC_PROMPT'), t('LOCK.CANCEL'));
    if (afterBiometric(ok) === 'password') return setPassword({ reason: 'BIOMETRIC_FAILED', error: null, field: null });
    auth.current = { method: 'BIOMETRIC' };
    await trySend();
  };

  const confirmPassword = async (pw: string, code: string) => {
    auth.current = { method: 'PASSWORD', password: pw, mfaCode: code };
    setPassword((p) => (p ? { ...p, error: null, field: null } : p));
    await trySend();
  };

  const startOver = () => {
    setState(emptyWizard());
    setSaved(null);
    setDrawn(null);
    setSigDirty(false);
    setStepErr(null);
    setServerErr(null);
    setDone(null);
    setShortage(null);
    setStep('CLIENT');
  };

  if (!company) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader title={t('SALES_ORDER.NEW')} />
        <Text style={styles.empty}>{t('SALES_ORDER.NO_ACCESS')}</Text>
      </SafeAreaView>
    );
  }

  if (!resumeDone) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader title={t('SALES_ORDER.NEW')} />
        <ActivityIndicator color={colors.primary} />
      </SafeAreaView>
    );
  }

  if (done) {
    const sum = orderSummary(done);
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader title={t('SALES_ORDER.NEW')} />
        <ScrollView ref={scrollRef} contentContainerStyle={styles.body}>
          <Text style={styles.doneTitle} accessibilityRole="header">{t('SALES_ORDER.DONE.TITLE')}</Text>
          <Text style={styles.doneNumber} accessibilityLiveRegion="polite">{done.number}</Text>
          <View style={styles.sumBox}>
            <SumRow label={t('SALES_ORDER.AUTH.CLIENT')} value={done.clientName ?? ''} />
            <SumRow label={t('SALES_ORDER.SUMMARY.TOTAL')} value={money(sum.total)} strong />
          </View>
          {done.terms?.awaitingClientApproval ? <Help>{t('SALES_ORDER.DONE.AWAITING')}</Help> : null}
          {done.hasBackorder ? <Help>{t('SALES_ORDER.DONE.BACKORDER')}</Help> : null}
          <Button label={t('SALES_ORDER.DONE.NEW_ORDER')} onPress={startOver} />
          <Button kind="outline" label={t('SALES_ORDER.MINE')} onPress={() => router.replace({ pathname: '/(auth)/SalesOrders', params: { tenantId: String(tenantId) } })} />
        </ScrollView>
      </SafeAreaView>
    );
  }

  const last = step === 'AUTHORIZATION';
  const pct = ((idx + 1) / WIZARD_STEPS.length) * 100;
  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('SALES_ORDER.NEW')} />
      {companies.length > 1 && !state.orderPublicId ? <CompanyChips companies={companies} activeId={tenantId} onPick={(id) => { setTenantPick(id); startOver(); }} /> : null}
      <View style={styles.progress}>
        <View style={styles.progressTop}>
          <Text style={styles.progressText}>{t('SALES_ORDER.STEP_OF', { n: idx + 1, total: WIZARD_STEPS.length, name: t(`SALES_ORDER.STEPS.${step}`) })}</Text>
          {state.number ? <Badge label={t('SALES_ORDER.DRAFT_NUMBER', { number: state.number })} /> : null}
        </View>
        <View style={styles.bar} accessibilityRole="progressbar" accessibilityLabel={t('SALES_ORDER.PROGRESS')} accessibilityValue={{ min: 1, max: WIZARD_STEPS.length, now: idx + 1 }}>
          <View style={[styles.barFill, { width: `${pct}%` }]} />
        </View>
      </View>

      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView ref={scrollRef} style={styles.scroll} contentContainerStyle={styles.body} keyboardShouldPersistTaps="handled">
          {step === 'CLIENT' ? (
            <ClientStep
              tenantId={tenantId}
              state={state}
              onChange={change}
              onPickClient={pickClient}
              client={client.data}
              clientLoading={client.isFetching}
              paymentChanged={!!state.clientPublicId && !!client.data && isPaymentChanged(state, client.data.paymentTerm)}
              warehouses={warehouses.data ?? []}
              canCreateClient={access.can('clients.create_basic') || access.can('clients.create')}
              methods={methods.data ?? []}
              terms={terms.data ?? []}
              onClientCreated={(c: ClientForOrder) => {
                setStepErr(null);
                qc.setQueryData(['sales-client', tenantId, c.publicId], c);
                setState((s) => withClientPayment(withClient(s, c.publicId, `${c.code} · ${c.name}`), c.paymentTerm));
              }}
            />
          ) : null}
          {step === 'PRODUCTS' ? <ProductsStep tenantId={tenantId} state={state} onChange={change} /> : null}
          {step === 'DELIVERY' ? (
            <DeliveryStep tenantId={tenantId} state={state} onChange={change} onRecalc={(p) => void recalc(p)} client={client.data} saved={saved} recalculating={recalculating} canPickOther={access.can('locations.read')} />
          ) : null}
          {step === 'AUTHORIZATION' ? <AuthorizationStep state={state} onChange={change} requiresSignature={requiresSignature} saved={saved} /> : null}
        </ScrollView>

        <View style={[styles.foot, { paddingBottom: 10 + insets.bottom }]}>
          {step === 'PRODUCTS' ? <CartBar state={state} onChange={change} /> : null}
          {step === 'AUTHORIZATION' ? (
            <SignatureBlock
              tenantId={tenantId}
              drawn={drawn}
              onDrawn={(d) => { setStepErr(null); setDrawn(d); setSigDirty(true); }}
              savedFilePublicId={savedSignature}
              dirty={sigDirty}
              required={requiresSignature}
            />
          ) : null}
          {stepErr ? <ErrorText>{t(`SALES_ORDER.ERRORS.${stepErr}`)}</ErrorText> : null}
          {serverErr ? <ErrorText>{serverErr}</ErrorText> : null}
          <View style={styles.nav}>
            <Button kind="outline" flex={1} label={t('SALES_ORDER.BACK')} onPress={goBack} disabled={idx === 0 || busy} />
            {last ? (
              <Button flex={1.5} label={t('SALES_ORDER.SEND.BUTTON')} onPress={() => void startSubmit()} busy={busy} testID="sales-send" />
            ) : (
              <Button flex={1.5} label={t('SALES_ORDER.NEXT')} onPress={() => void goNext()} busy={busy} disabled={recalculating} testID="sales-next" />
            )}
          </View>
          <Text style={styles.draftHelp}>{t('SALES_ORDER.DRAFT_HELP')}</Text>
        </View>
      </KeyboardAvoidingView>

      <PasswordSheet
        key={password ? `open-${password.reason}` : 'closed'}
        visible={!!password}
        reason={password?.reason ?? 'NO_BIOMETRIC'}
        mfa={!!mfa.data}
        busy={busy}
        error={password?.error ?? null}
        field={password?.field ?? null}
        onCancel={() => { auth.current = null; setPassword(null); }}
        onConfirm={(pw, code) => void confirmPassword(pw, code)}
      />
      <ShortageSheet
        visible={!!shortage}
        number={state.number ?? ''}
        check={shortage?.check ?? null}
        busy={busy}
        error={shortage?.error ?? null}
        onCancel={() => { auth.current = null; setShortage(null); }}
        onSend={(allowBackorder, overrideCredit) => void trySend(allowBackorder, overrideCredit)}
      />
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1 },
  scroll: { flex: 1 },
  body: { paddingHorizontal: 20, paddingBottom: 20, gap: 14 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  progress: { paddingHorizontal: 20, paddingBottom: 10, gap: 6 },
  progressTop: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  progressText: { fontSize: 14, fontWeight: '600', color: colors.text },
  bar: { height: 6, borderRadius: 3, backgroundColor: colors.gray, overflow: 'hidden' },
  barFill: { height: 6, backgroundColor: colors.primary },
  foot: { paddingHorizontal: 20, paddingTop: 8, gap: 8, borderTopWidth: 1, borderTopColor: colors.gray, backgroundColor: colors.background },
  nav: { flexDirection: 'row', gap: 10 },
  draftHelp: { fontSize: 11, color: colors.muted, textAlign: 'center' },
  doneTitle: { fontSize: 20, fontWeight: '700', color: colors.text, textAlign: 'center', marginTop: 10 },
  doneNumber: { fontSize: 28, fontWeight: '700', color: colors.primary, textAlign: 'center' },
  sumBox: { borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 12, backgroundColor: colors.backgroundSecondary },
});
