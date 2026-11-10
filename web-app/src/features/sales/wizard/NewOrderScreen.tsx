// Lote 223 (P3 del pedido de vendedores) — el PEDIDO GUIADO web (`/sales-orders/new-order`, sales.create, módulo SALES_ORDERS): pantalla completa con cuatro
// pasos y su barra de progreso (Cliente → Productos → Entrega → Autorización). «Siguiente» guarda el BORRADOR de la orden de venta con el paso (POST la primera
// vez, PATCH después; el servidor valida lo que el paso exige) y la dirección queda con `?order=<publicId>`: si el teléfono se apaga, el pedido está en Órdenes de
// venta como borrador y al volver a abrir la dirección se retoma donde iba. «Enviar pedido» sube la firma dibujada, pide la contraseña (y el código MFA si la
// cuenta lo tiene) y solicita con POST /submit; si falta inventario (409) o se pasa del crédito (422) abre el diálogo de faltante de siempre, que repite el
// envío con la misma contraseña. Al final, la pantalla con el número OV-#####, el total y «Nuevo pedido».
import { useContext, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { SessionContext } from '../../../app/session'
import { applyProblemDetails, toApiError } from '../../../kernel/api/problem'
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { hasInk, signatureToPng, Spinner, type SignatureStroke } from '../../../kernel/ui'
import { useClient } from '../../clients/api'
import { AvailabilityDialog } from '../OrderModal'
import { useSalesOrder, useSalesStock, type SalesAvailabilityDto, type SalesOrderDto, type SalesOrderRequest } from '../salesApi'
import { anyBackorder, orderKitPieces, orderWarehouseId, showWarehouseField, stockWarehouses } from '../stockRules'
import { AuthorizationStep, SubmitPasswordModal } from './AuthorizationStep'
import { ClientStep } from './ClientStep'
import { DeliveryStep } from './DeliveryStep'
import { CartBar, ProductsStep } from './ProductsStep'
import { useWizardMutations } from './wizardApi'
import {
  emptyWizard,
  firstInvalidStep,
  isPaymentChanged,
  orderSummary,
  resumeStep,
  stateFromDraft,
  stepError,
  stepIndex,
  stepRequest,
  submitOutcome,
  WIZARD_STEPS,
  withClient,
  withClientPayment,
  withServerPrices,
  type StepContext,
  type WizardErrorKey,
  type WizardState,
  type WizardStep,
} from './wizardRules'
import '../sales.css'
import './wizard.css'

type SubmitError = { message: string; field: 'password' | 'mfaCode' | null }

export default function NewOrderScreen() {
  const t = useT()
  const f = useFormat()
  const [params, setParams] = useSearchParams()
  // el borrador que se retoma es el de la dirección al ENTRAR; el `?order=` que pone el primer guardado no vuelve a cargarlo
  const [resumeId] = useState(() => params.get('order'))
  const resumed = useSalesOrder(resumeId)
  const [resumeDone, setResumeDone] = useState(resumeId === null)
  const mfa = useContext(SessionContext)?.me?.mfaEnabled ?? false

  const [state, setState] = useState<WizardState>(emptyWizard)
  const [step, setStep] = useState<WizardStep>('CLIENT')
  const [saved, setSaved] = useState<SalesOrderDto | null>(null)
  const [strokes, setStrokes] = useState<SignatureStroke[]>([])
  const [sigDirty, setSigDirty] = useState(false)
  const [stepErr, setStepErr] = useState<WizardErrorKey | null>(null)
  const [serverErr, setServerErr] = useState<string | null>(null)
  const [prefillFor, setPrefillFor] = useState<string | null>(null)
  const [recalculating, setRecalculating] = useState(false)
  const [preparing, setPreparing] = useState(false)
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [submitErr, setSubmitErr] = useState<SubmitError | null>(null)
  const [checking, setChecking] = useState<{ order: SalesOrderDto; check: SalesAvailabilityDto } | null>(null)
  const [done, setDone] = useState<SalesOrderDto | null>(null)
  const credentials = useRef<{ password: string; mfaCode: string } | null>(null)
  const bodyRef = useRef<HTMLDivElement>(null)
  const firstRender = useRef(true)

  const { create, update, signature, removeSignature, availability, submit } = useWizardMutations()
  const client = useClient(state.clientPublicId, true)
  // Lote 235 (Luis): la orden conserva su almacén (uno por orden). Con un solo almacén activo no se pregunta; con varios, el campo sale en el paso Cliente ya
  // escogido con el PRINCIPAL (se puede cambiar). De ese almacén sale el «Disponible: N» de cada producto, el distintivo de backorder y la nota de otros almacenes.
  const stock = useSalesStock()
  const needsWarehouse = showWarehouseField(stock.data)
  const warehouseId = orderWarehouseId(stock.data, state.warehousePublicId)
  const requiresSignature = client.data?.requireOrderSignature ?? saved?.terms?.clientRequiresSignature ?? false
  const hasSavedSignature = !!saved?.terms?.signatureFilePublicId && !sigDirty
  const ctx: StepContext = { requiresSignature, hasSignature: hasInk(strokes) || hasSavedSignature }
  const physicalId = client.data?.physicalAddress?.isActive === false ? null : client.data?.physicalAddress?.publicId ?? null
  const idx = stepIndex(step)
  const busy = create.isPending || update.isPending || signature.isPending || removeSignature.isPending || preparing

  const change = (next: (s: WizardState) => WizardState) => {
    setStepErr(null)
    setState(next)
  }

  // retomar un borrador (`?order=`): solo uno que siga en borrador (se ajusta el estado al llegar la orden, sin efecto)
  if (!resumeDone && resumed.data) {
    const dto = resumed.data
    setResumeDone(true)
    if ((dto.status ?? '').toUpperCase() === 'DRAFT') {
      setState(stateFromDraft(dto))
      setSaved(dto)
      setStep(resumeStep(dto))
    } else {
      setServerErr(t('salesWizard.resumeNotDraft', { number: dto.number ?? '' }))
    }
  }

  // la forma de pago se prellena con la del cliente al escogerlo (no al retomar: el borrador conserva la suya)
  if (prefillFor && client.data?.publicId === prefillFor) {
    const term = client.data.paymentTerm
    setPrefillFor(null)
    setState((s) => (s.clientPublicId === prefillFor ? withClientPayment(s, term) : s))
  }

  // al cambiar de paso, el foco va al paso nuevo (lectores de pantalla y teclado) y la vista vuelve arriba
  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false
      return
    }
    bodyRef.current?.focus()
  }, [step, done])

  const pickClient = (publicId: string | null, label: string) => {
    setStepErr(null)
    setState((s) => withClient(s, publicId, label))
    setPrefillFor(publicId)
  }

  /** Guarda el borrador con lo que exige `which` y deja los precios y el número que devolvió el servidor. */
  const save = async (which: WizardStep, s: WizardState): Promise<SalesOrderDto> => {
    // con varios almacenes viaja el que se ve escogido (el principal por omisión); con uno solo el servidor lo pone
    const body = stepRequest(which, needsWarehouse && !s.warehousePublicId ? { ...s, warehousePublicId: warehouseId } : s) as SalesOrderRequest
    const dto = s.orderPublicId ? await update.mutateAsync({ id: s.orderPublicId, body }) : await create.mutateAsync(body)
    setSaved(dto)
    setState((cur) => ({ ...cur, orderPublicId: dto.publicId ?? cur.orderPublicId, number: dto.number ?? cur.number, cart: withServerPrices(cur.cart, dto.lines) }))
    if (!s.orderPublicId && dto.publicId) setParams({ order: dto.publicId }, { replace: true })
    return dto
  }

  const goNext = async () => {
    setServerErr(null)
    const err = stepError(step, state, ctx)
    if (err) return setStepErr(err)
    let s = state
    // a la entrega se llega con la dirección física del cliente ya escogida (se puede cambiar)
    if (step === 'PRODUCTS' && s.deliveryMode === 'DELIVERY' && !s.consigneePublicId && physicalId) s = { ...s, consigneePublicId: physicalId }
    if (s !== state) setState(s)
    try {
      await save(step, s)
      setStep(WIZARD_STEPS[idx + 1])
    } catch (e) {
      setServerErr(applyProblemDetails(e).title)
    }
  }

  const goBack = () => {
    setStepErr(null)
    setServerErr(null)
    if (idx > 0) setStep(WIZARD_STEPS[idx - 1])
  }

  const goTo = (target: WizardStep) => {
    if (stepIndex(target) >= idx) return
    setStepErr(null)
    setServerErr(null)
    setStep(target)
  }

  /** Entrega o consignatario cambiados: se guarda el borrador para que el servidor vuelva a calcular el envío. */
  const recalc = async (patch: Partial<WizardState>) => {
    setStepErr(null)
    setServerErr(null)
    const s = { ...state, ...patch }
    setState(s)
    if (!s.orderPublicId) return
    setRecalculating(true)
    try {
      await save('PRODUCTS', s)
    } catch (e) {
      setServerErr(applyProblemDetails(e).title)
    } finally {
      setRecalculating(false)
    }
  }

  /** La firma dibujada sube como PNG (o se quita si se borró); el nombre de quien autoriza va con ella. */
  const syncSignature = async (id: string) => {
    if (!sigDirty) return
    if (hasInk(strokes)) {
      const file = await signatureToPng(strokes)
      setSaved(await signature.mutateAsync({ id, file, authorizedByName: state.authorizedByName.trim() }))
    } else if (saved?.terms?.signatureFilePublicId) {
      setSaved(await removeSignature.mutateAsync(id))
    }
    setSigDirty(false)
  }

  const startSubmit = async () => {
    setServerErr(null)
    const bad = firstInvalidStep(state, ctx)
    if (bad) {
      setStep(bad.step)
      setStepErr(bad.error)
      return
    }
    setPreparing(true)
    try {
      const dto = await save('AUTHORIZATION', state)
      await syncSignature(dto.publicId!)
      setSubmitErr(null)
      setPasswordOpen(true)
    } catch (e) {
      setServerErr(applyProblemDetails(e).title)
    } finally {
      setPreparing(false)
    }
  }

  /** POST /submit con la contraseña que se tiene en memoria; lanza el error tal cual (el diálogo de faltante lo muestra). */
  const send = async (allowBackorder: boolean, overrideCredit: boolean) => {
    const id = state.orderPublicId!
    const c = credentials.current ?? { password: '', mfaCode: '' }
    const dto = await submit.mutateAsync({
      id,
      body: {
        authMethod: 'PASSWORD',
        password: c.password,
        mfaCode: mfa && c.mfaCode ? c.mfaCode : undefined,
        allowBackorder: allowBackorder || undefined,
        overrideCredit: overrideCredit || undefined,
      },
    })
    credentials.current = null
    setDone(dto)
    return dto
  }

  const confirmPassword = async (password: string, mfaCode: string) => {
    credentials.current = { password, mfaCode }
    setSubmitErr(null)
    try {
      // Lote 235: si alguna línea pasa de lo disponible, el vendedor ya vio el distintivo «Backorder: N» al capturar: se envía con backorder sin volver a preguntar
      await send(anyBackorder(stock.data, warehouseId, state.cart.map((l) => ({ ...l, components: orderKitPieces(l.components, saved?.lines?.find((x) => x.productPublicId === l.productPublicId)) }))), false)
      setPasswordOpen(false)
    } catch (e) {
      const err = toApiError(e)
      const outcome = submitOutcome(err)
      if (outcome === 'password' || outcome === 'mfa') return setSubmitErr({ message: err.title, field: outcome === 'mfa' ? 'mfaCode' : 'password' })
      if (outcome === 'shortage' || outcome === 'credit') {
        // la contraseña ya se verificó: el diálogo de faltante repite con ella (backorder o crédito autorizado)
        try {
          const check = await availability.mutateAsync(state.orderPublicId!)
          setPasswordOpen(false)
          setChecking({ order: saved!, check })
        } catch (e2) {
          setSubmitErr({ message: applyProblemDetails(e2).title, field: null })
        }
        return
      }
      setSubmitErr({ message: err.title, field: null })
    }
  }

  const startOver = () => {
    setState(emptyWizard())
    setSaved(null)
    setStrokes([])
    setSigDirty(false)
    setStepErr(null)
    setServerErr(null)
    setDone(null)
    setChecking(null)
    setStep('CLIENT')
    setParams({}, { replace: true })
  }

  const header = (
    <div className="head">
      <div>
        <h1>{t('salesWizard.title')}</h1>
        <p>{state.number ? t('salesWizard.draftNumber', { number: state.number }) : t('salesWizard.subtitle')}</p>
      </div>
      <Link className="btn" to="/sales-orders">{t('salesWizard.exit')}</Link>
    </div>
  )

  if (!resumeDone) {
    return (
      <div className="wz-screen">
        {header}
        {resumed.isError ? <p className="ferr" role="alert">{applyProblemDetails(resumed.error).title}</p> : <Spinner block label={t('common.loading')} />}
      </div>
    )
  }

  if (done) {
    const sum = orderSummary(done)
    return (
      <div className="wz-screen">
        {header}
        <div className="wz-body" ref={bodyRef} tabIndex={-1}>
          <section className="wz-done" aria-labelledby="wz-done-title">
            <h2 id="wz-done-title">{t('salesWizard.done.title')}</h2>
            <p className="wz-done-number mono">{done.number}</p>
            <dl className="wz-final-sum">
              <div><dt>{t('salesWizard.auth.client')}</dt><dd>{done.clientName}</dd></div>
              <div><dt>{t('salesWizard.summary.total')}</dt><dd className="mono">{f.money(sum.total)}</dd></div>
            </dl>
            {done.terms?.awaitingClientApproval && <p className="note" role="status">{t('salesWizard.done.awaiting')}</p>}
            {done.hasBackorder && <p className="note" role="status">{t('salesWizard.done.backorder')}</p>}
            <div className="wz-done-actions">
              <button type="button" className="btn flow" onClick={startOver}>{t('salesWizard.done.newOrder')}</button>
              <Link className="btn" to={`/sales-orders?order=${done.publicId ?? ''}`}>{t('salesWizard.done.view')}</Link>
            </div>
          </section>
        </div>
      </div>
    )
  }

  const last = step === 'AUTHORIZATION'
  return (
    <div className="wz-screen">
      {header}
      <nav aria-label={t('salesWizard.progress')}>
        <p className="wz-progress-text">{t('salesWizard.stepOf', { n: idx + 1, total: WIZARD_STEPS.length, name: t(`salesWizard.steps.${step}`) })}</p>
        <div className="wz-bar" role="progressbar" aria-label={t('salesWizard.progress')} aria-valuemin={1} aria-valuemax={WIZARD_STEPS.length} aria-valuenow={idx + 1}>
          <span style={{ width: `${((idx + 1) / WIZARD_STEPS.length) * 100}%` }} />
        </div>
        <ol className="wz-progress">
          {WIZARD_STEPS.map((s, i) => (
            <li key={s} className={i === idx ? 'on' : i < idx ? 'past' : undefined}>
              <button type="button" disabled={i === idx || i > idx + 1 || busy} aria-current={i === idx ? 'step' : undefined} onClick={() => (i === idx + 1 ? void goNext() : goTo(s))}>
                <span className="wz-num" aria-hidden="true">{i + 1}</span>
                <span className="wz-lab">{t(`salesWizard.steps.${s}`)}</span>
              </button>
            </li>
          ))}
        </ol>
      </nav>

      <div className="wz-body" ref={bodyRef} tabIndex={-1}>
        {step === 'CLIENT' && (
          <ClientStep
            state={state}
            onChange={change}
            onPickClient={pickClient}
            client={client.data}
            clientLoading={client.isFetching}
            paymentChanged={!!state.clientPublicId && !!client.data && isPaymentChanged(state, client.data.paymentTerm)}
            needsWarehouse={needsWarehouse}
            warehouses={stockWarehouses(stock.data)}
            warehouseId={warehouseId}
            onClientCreated={(c) => {
              setStepErr(null)
              setState((s) => withClientPayment(withClient(s, c.publicId ?? null, `${c.code ?? ''} · ${c.name ?? ''}`), c.paymentTerm))
            }}
          />
        )}
        {step === 'PRODUCTS' && <ProductsStep state={state} onChange={change} stock={stock.data} warehouseId={warehouseId} />}
        {step === 'DELIVERY' && <DeliveryStep state={state} onChange={change} onRecalc={(p) => void recalc(p)} client={client.data} saved={saved} recalculating={recalculating} stock={stock.data} warehouseId={warehouseId} />}
        {step === 'AUTHORIZATION' && (
          <AuthorizationStep
            state={state}
            onChange={change}
            strokes={strokes}
            onStrokes={(next) => {
              setStepErr(null)
              setStrokes(next)
              setSigDirty(true)
            }}
            requiresSignature={requiresSignature}
            hasSavedSignature={hasSavedSignature}
            saved={saved}
          />
        )}
      </div>

      <div className="wz-foot">
        {step === 'PRODUCTS' && <CartBar state={state} onChange={change} saved={saved} stock={stock.data} warehouseId={warehouseId} />}
        {stepErr && <p className="ferr" role="alert">{t(`salesWizard.errors.${stepErr}`)}</p>}
        {serverErr && <p className="ferr" role="alert">{serverErr}</p>}
        <div className="wz-nav">
          <button type="button" className="btn" onClick={goBack} disabled={idx === 0 || busy}>{t('salesWizard.back')}</button>
          {last ? (
            <button type="button" className="btn flow" onClick={() => void startSubmit()} disabled={busy}>{busy ? t('salesWizard.saving') : t('salesWizard.send')}</button>
          ) : (
            <button type="button" className="btn flow" onClick={() => void goNext()} disabled={busy}>{busy ? t('salesWizard.saving') : t('salesWizard.next')}</button>
          )}
        </div>
        <p className="help wz-draft-help">{t('salesWizard.draftHelp')}</p>
      </div>

      <SubmitPasswordModal
        key={passwordOpen ? 'open' : 'closed'}
        open={passwordOpen}
        mfa={mfa}
        busy={submit.isPending || availability.isPending}
        error={submitErr?.message ?? null}
        field={submitErr?.field ?? null}
        onCancel={() => {
          credentials.current = null
          setPasswordOpen(false)
        }}
        onConfirm={(password, code) => void confirmPassword(password, code)}
      />
      {checking && (
        <AvailabilityDialog
          order={checking.order}
          check={checking.check}
          onRequest={(allowBackorder, overrideCredit) => send(allowBackorder, overrideCredit)}
          onClose={() => {
            credentials.current = null
            setChecking(null)
          }}
          onDone={() => setChecking(null)}
        />
      )}
    </div>
  )
}
