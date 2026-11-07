// La entrega de un despacho por la compañía enlazada (Lote 227, P7), tal como se ve en la ficha de la orden de venta y en la lista de entregas: estado del envío, número y estatus de la orden de
// la otra compañía, quién recibió y cuándo, y el comprobante (se lee en vivo de allá solo al pedirlo; sus archivos se abren con la sesión).
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, Spinner, toast, type ChipTone } from '../../kernel/ui'
import { openProofFile, useLinkedDeliveryProof, type TenantLinkDeliveryDto } from './tenantLinkApi'

const DELIVERY_TONE: Record<string, ChipTone> = { PENDING: 'warn', CREATED: 'route', FAILED: 'fail' }

function ProofDetails({ delivery }: { delivery: TenantLinkDeliveryDto }) {
  const t = useT()
  const f = useFormat()
  const proof = useLinkedDeliveryProof(delivery.publicId ?? null)
  if (proof.isPending) return <Spinner />
  if (proof.isError) return <p className="ferr" role="alert">{applyProblemDetails(proof.error).title}</p>
  const p = proof.data
  if (!p) return <p className="help">{t('tenantLink.proof.none')}</p>
  return (
    <div className="tl-proof">
      <div>{t('tenantLink.proof.recipient', { name: p.recipientName ?? '—', when: f.dateTime(p.deliveredAtUtc) })}</div>
      {p.notes && <div className="help">{p.notes}</div>}
      {(p.files ?? []).map((file) => (
        <button
          key={file.publicId}
          type="button"
          className="linkbtn"
          onClick={() => void openProofFile(delivery.publicId!, file.publicId!).catch(() => toast.error(t('tenantLink.proof.fileError')))}
        >
          {file.fileName ?? t('tenantLink.proof.file')}
        </button>
      ))}
    </div>
  )
}

export function LinkedDeliveryInfo({ delivery }: { delivery: TenantLinkDeliveryDto }) {
  const t = useT()
  const f = useFormat()
  const [showProof, setShowProof] = useState(false)
  const state = delivery.state ?? 'PENDING'
  const delivered = !!delivery.deliveredAtUtc
  return (
    <div className="tl-delivery">
      <div>
        <Chip tone={DELIVERY_TONE[state] ?? 'neutral'}>{t(`tenantLink.delivery.state.${state}`)}</Chip>{' '}
        {delivery.providerCompanyName && <span className="help">{delivery.providerCompanyName}</span>}
      </div>
      {delivery.providerOrderNumber && (
        <div>
          {t('tenantLink.delivery.order')}: <b className="mono">{delivery.providerOrderNumber}</b>
          {delivery.providerStatusLabel && <> · <Chip tone={delivered ? 'deliv' : 'neutral'}>{delivery.providerStatusLabel}</Chip></>}
        </div>
      )}
      {state === 'FAILED' && delivery.error && <div className="cs-err">{delivery.error}</div>}
      {delivered && <div className="help">{t('tenantLink.proof.recipient', { name: delivery.deliveredTo ?? '—', when: f.dateTime(delivery.deliveredAtUtc) })}</div>}
      {delivery.providerOrderPublicId && (
        <div>
          <button type="button" className="linkbtn" aria-expanded={showProof} onClick={() => setShowProof((v) => !v)}>
            {showProof ? t('tenantLink.proof.hide') : t('tenantLink.proof.show')}
          </button>
          {showProof && <ProofDetails delivery={delivery} />}
        </div>
      )}
    </div>
  )
}
