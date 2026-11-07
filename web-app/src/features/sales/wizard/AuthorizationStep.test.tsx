// Lote 223 (P3) — paso 4 del pedido guiado: nombre de quien autoriza y su firma dibujada (obligatorias solo si el cliente las exige) y la ventana de «Enviar
// pedido» que pide la contraseña con el ojito (PasswordBox) y el código MFA si la cuenta lo tiene.
import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import type { SignatureStroke } from '../../../kernel/ui'
import type { SalesOrderDto } from '../salesApi'
import { AuthorizationStep, SubmitPasswordModal } from './AuthorizationStep'
import { StepHarness, WizardProviders } from './wizardTestKit'
import type { WizardState } from './wizardRules'

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const { fakeFetch } = await import('./wizardTestApi')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: fakeFetch({ requests: [], route: null }) }) }
})

const SAVED = { publicId: 'o-1', clientName: 'Farmacia Central', subtotal: 50, taxAmount: 5.75, total: 55.75, terms: {} } as unknown as SalesOrderDto

function Step({ requiresSignature = false, hasSavedSignature = false, onState, onStrokes }: { requiresSignature?: boolean; hasSavedSignature?: boolean; onState?: (s: WizardState) => void; onStrokes?: (s: SignatureStroke[]) => void }) {
  const [strokes, setStrokes] = useState<SignatureStroke[]>([])
  return (
    <StepHarness initial={{ clientPublicId: 'c-1', cart: [] }} onState={onState}>
      {(state, change) => (
        <AuthorizationStep
          state={state}
          onChange={change}
          strokes={strokes}
          onStrokes={(s) => {
            setStrokes(s)
            onStrokes?.(s)
          }}
          requiresSignature={requiresSignature}
          hasSavedSignature={hasSavedSignature}
          saved={SAVED}
        />
      )}
    </StepHarness>
  )
}

function draw(box: Element) {
  fireEvent.pointerDown(box, { clientX: 10, clientY: 10, pointerId: 1, pointerType: 'mouse', button: 0 })
  fireEvent.pointerMove(box, { clientX: 60, clientY: 40, pointerId: 1, pointerType: 'mouse' })
  fireEvent.pointerMove(box, { clientX: 120, clientY: 30, pointerId: 1, pointerType: 'mouse' })
  fireEvent.pointerUp(box, { clientX: 120, clientY: 30, pointerId: 1, pointerType: 'mouse' })
}

describe('AuthorizationStep — paso 4 del pedido guiado', () => {
  beforeAll(() => setLang('es'))

  it('opcional para un cliente que no la exige: nombre y firma, y el resumen final', async () => {
    const user = userEvent.setup()
    let last: WizardState | null = null
    render(<WizardProviders><Step onState={(s) => (last = s)} /></WizardProviders>)
    expect(screen.getByText('La firma es opcional para este cliente.')).toBeInTheDocument()
    await user.type(screen.getByRole('textbox', { name: 'Nombre de quien autoriza' }), 'Ana Ruiz')
    expect(last!.authorizedByName).toBe('Ana Ruiz')
    expect(screen.getByRole('textbox', { name: 'Nombre de quien autoriza' })).toHaveAttribute('maxLength', '150')
    expect(screen.getByText('Farmacia Central')).toBeInTheDocument()
    expect(screen.getByText('$55.75')).toBeInTheDocument()
  })

  it('dibujar con el dedo o el ratón deja la firma; «Borrar firma» la quita', async () => {
    const user = userEvent.setup()
    const onStrokes = vi.fn()
    render(<WizardProviders><Step requiresSignature onStrokes={onStrokes} /></WizardProviders>)
    expect(screen.getByText('Este cliente exige el nombre y la firma de quien autoriza el pedido.')).toBeInTheDocument()
    const box = screen.getByRole('img', { name: 'Firma de quien autoriza: sin firma' })
    const clear = screen.getByRole('button', { name: 'Borrar firma' })
    expect(clear).toBeDisabled()
    draw(box)
    expect(onStrokes).toHaveBeenCalledTimes(1)
    expect(onStrokes.mock.calls[0][0][0]).toHaveLength(3)
    expect(screen.getByRole('img', { name: 'Firma de quien autoriza: firmado' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Borrar firma' }))
    expect(onStrokes).toHaveBeenLastCalledWith([])
    expect(screen.getByRole('img', { name: 'Firma de quien autoriza: sin firma' })).toBeInTheDocument()
  })

  it('con una firma ya guardada lo dice', () => {
    render(<WizardProviders><Step hasSavedSignature /></WizardProviders>)
    expect(screen.getByText('El pedido ya tiene una firma guardada. Dibuje otra solo si quiere reemplazarla.')).toBeInTheDocument()
  })

  it('«Enviar pedido» pide la contraseña (con el ojito) y el código MFA si la cuenta lo tiene', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    const { rerender } = render(
      <WizardProviders>
        <SubmitPasswordModal open mfa={false} busy={false} error={null} field={null} onCancel={() => {}} onConfirm={onConfirm} />
      </WizardProviders>,
    )
    const dialog = screen.getByRole('dialog', { name: 'Confirme con su contraseña' })
    await user.click(within(dialog).getByRole('button', { name: 'Enviar pedido' }))
    expect(within(dialog).getByText('Escriba su contraseña para enviar el pedido.')).toBeInTheDocument()
    expect(onConfirm).not.toHaveBeenCalled()
    const pwd = within(dialog).getByLabelText('Contraseña')
    expect(pwd).toHaveAttribute('type', 'password')
    await user.click(within(dialog).getByRole('button', { name: 'Mostrar contraseña' }))
    expect(pwd).toHaveAttribute('type', 'text')
    expect(within(dialog).queryByLabelText('Código de verificación (MFA)')).not.toBeInTheDocument()
    await user.type(pwd, 'Secreta1!{Enter}')
    expect(onConfirm).toHaveBeenCalledWith('Secreta1!', '')

    rerender(
      <WizardProviders>
        <SubmitPasswordModal open mfa busy={false} error="Contraseña incorrecta." field="password" onCancel={() => {}} onConfirm={onConfirm} />
      </WizardProviders>,
    )
    expect(screen.getByText('Contraseña incorrecta.')).toBeInTheDocument()
    expect(screen.getByLabelText('Contraseña')).toHaveAttribute('aria-invalid', 'true')
    await user.type(screen.getByLabelText('Código de verificación (MFA)'), '123456')
    await user.click(screen.getByRole('button', { name: 'Enviar pedido' }))
    expect(onConfirm).toHaveBeenLastCalledWith('Secreta1!', '123456')
  })
})
