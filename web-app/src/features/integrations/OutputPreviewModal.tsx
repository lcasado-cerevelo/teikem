// Vista previa de la salida de una integración (Lote 137b): arma un ejemplo con órdenes recientes del cliente tal como se mandaría, sin mandar nada.
import { useEffect } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal, Spinner } from '../../kernel/ui'
import { usePreviewOutput, type IntegrationDto } from './integrationsApi'

export function OutputPreviewModal({ integration, onClose }: { integration: IntegrationDto; onClose: () => void }) {
  const t = useT()
  const preview = usePreviewOutput()
  const { mutate } = preview
  useEffect(() => {
    mutate(integration.publicId ?? '')
  }, [mutate, integration.publicId])
  const data = preview.data
  return (
    <Modal open size="lg" title={t('integrations.out.previewTitle', { name: integration.name ?? '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('integrations.close')}</button>}>
      <div className="pb">
        {preview.isError ? (
          <p className="ferr" role="alert" data-testid="output-preview-error">{applyProblemDetails(preview.error).title}</p>
        ) : preview.isPending || !data ? (
          <Spinner block />
        ) : (
          <>
            <p className="help">{t(data.sample ? 'integrations.out.previewSample' : 'integrations.out.previewNoSample', { file: data.fileName ?? '', format: data.format ?? '', orders: data.orderCount ?? 0 })}</p>
            <pre className="int-json" data-testid="output-preview">{data.content}</pre>
          </>
        )}
      </div>
    </Modal>
  )
}
