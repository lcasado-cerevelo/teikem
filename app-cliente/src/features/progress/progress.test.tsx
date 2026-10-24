// Barra de progreso (Lote 103): reglas de cada tramo y la barra con su texto. Tantas etapas como estatus tiene el camino normal de la compañía.
import { render, screen } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { FailedAttemptsChip, StatusWithAttempts } from './FailedAttempts'
import { ProgressBar, ProgressSteps } from './ProgressBar'
import { progressPercent, segmentKinds, stepKind } from './progressRules'
import { toProgressInfo } from './progressInfo'

describe('reglas de la barra', () => {
  it('en marcha: lo anterior hecho, la etapa actual resaltada y lo que falta apagado (con cualquier número de etapas)', () => {
    expect(segmentKinds(3, 8, 'IN_PROGRESS')).toEqual(['done', 'done', 'current', 'todo', 'todo', 'todo', 'todo', 'todo'])
    expect(segmentKinds(1, 4, 'IN_PROGRESS')).toEqual(['current', 'todo', 'todo', 'todo'])
    expect(segmentKinds(2, 3, 'IN_PROGRESS')).toHaveLength(3)
  })
  it('entregada: todo hecho; detenida: la etapa actual en alerta; cancelada: todo apagado', () => {
    expect(segmentKinds(5, 5, 'COMPLETED')).toEqual(Array(5).fill('done'))
    expect(segmentKinds(3, 8, 'ALERT')).toEqual(['done', 'done', 'alert', 'todo', 'todo', 'todo', 'todo', 'todo'])
    expect(segmentKinds(0, 4, 'CANCELLED')).toEqual(Array(4).fill('cancelled'))
    expect(segmentKinds(1, 0, 'IN_PROGRESS')).toEqual([])
  })
  it('porcentaje y lista de etapas', () => {
    expect(progressPercent(2, 4, 'IN_PROGRESS')).toBe(50)
    expect(progressPercent(4, 4, 'COMPLETED')).toBe(100)
    expect(progressPercent(0, 4, 'CANCELLED')).toBe(0)
    expect([0, 1, 2, 3].map((i) => stepKind(i, 2, 'IN_PROGRESS'))).toEqual(['done', 'current', 'todo', 'todo'])
  })
  it('del DTO del API: sin etapas no hay barra', () => {
    expect(toProgressInfo(null)).toBeNull()
    expect(toProgressInfo({ step: 0, total: 0, state: 'IN_PROGRESS', statusLabel: 'x' })).toBeNull()
    expect(toProgressInfo({ step: 2, total: 8, state: 'IN_PROGRESS', statusLabel: 'Confirmada' })).toEqual({ step: 2, total: 8, state: 'IN_PROGRESS', statusLabel: 'Confirmada', failedAttempts: 0 })
  })
})

describe('ProgressBar', () => {
  beforeEach(() => setLang('es'))

  it('dibuja un tramo por etapa y dice en qué paso va', async () => {
    await render(<ProgressBar progress={{ step: 3, total: 8, state: 'IN_PROGRESS', statusLabel: 'Recogido' }} />)
    expect(screen.getAllByTestId(/^segment-/)).toHaveLength(8)
    expect(screen.getAllByTestId('segment-done')).toHaveLength(2)
    expect(screen.getAllByTestId('segment-current')).toHaveLength(1)
    expect(screen.getAllByTestId('segment-todo')).toHaveLength(5)
    expect(screen.getByText('Paso 3 de 8 · Recogido')).toBeTruthy()
  })

  it('una orden detenida avisa dónde se quedó', async () => {
    await render(<ProgressBar progress={{ step: 2, total: 5, state: 'ALERT', statusLabel: 'En espera' }} />)
    expect(screen.getAllByTestId('segment-alert')).toHaveLength(1)
    expect(screen.getByText('En espera · va por el paso 2 de 5')).toBeTruthy()
  })

  it('entregada muestra el estatus tal cual', async () => {
    await render(<ProgressBar progress={{ step: 4, total: 4, state: 'COMPLETED', statusLabel: 'Entregada' }} />)
    expect(screen.getByText('Entregada')).toBeTruthy()
    expect(screen.getAllByTestId('segment-done')).toHaveLength(4)
  })

  it('cancelada se ve apagada y con su estatus', async () => {
    await render(<ProgressBar progress={{ step: 0, total: 4, state: 'CANCELLED', statusLabel: 'Cancelada' }} />)
    expect(screen.getAllByTestId('segment-cancelled')).toHaveLength(4)
    expect(screen.getByText('Cancelada')).toBeTruthy()
  })

  it('la lista de etapas marca hechas, actual y por venir', async () => {
    await render(<ProgressSteps labels={['Recibida', 'Recogida', 'En camino', 'Entregada']} progress={{ step: 2, total: 4, state: 'IN_PROGRESS', statusLabel: 'Recogida' }} />)
    expect(screen.getAllByTestId('step-done')).toHaveLength(1)
    expect(screen.getAllByTestId('step-current')).toHaveLength(1)
    expect(screen.getAllByTestId('step-todo')).toHaveLength(2)
    expect(screen.getByText('En camino')).toBeTruthy()
  })
})

describe('intentos fallidos', () => {
  beforeEach(() => setLang('es'))
  it('el DTO trae cuántos intentos fallidos lleva la orden', () => {
    expect(toProgressInfo({ step: 2, total: 4, state: 'IN_PROGRESS', statusLabel: 'Asignada', failedAttempts: 2 })?.failedAttempts).toBe(2)
  })
  it('una píldora por intentos: uno, varios o ninguno (no se pinta)', async () => {
    const one = await render(<FailedAttemptsChip count={1} />)
    expect(screen.getByText('1 intento fallido')).toBeTruthy()
    await one.unmount()
    const many = await render(<FailedAttemptsChip count={2} />)
    expect(screen.getByText('2 intentos fallidos')).toBeTruthy()
    await many.unmount()
    await render(<FailedAttemptsChip count={0} />)
    expect(screen.queryByText(/intento/)).toBeNull()
  })
  it('el estatus con su píldora debajo, en la tabla', async () => {
    await render(<StatusWithAttempts status="ASSIGNED" label="Asignada" attempts={2} />)
    expect(await screen.findByText('Asignada')).toBeTruthy()
    expect(await screen.findByText('2 intentos fallidos')).toBeTruthy()
  })
  it('la barra de progreso muestra la píldora cuando la orden ya intentó entregarse', async () => {
    await render(<ProgressBar progress={{ step: 3, total: 4, state: 'IN_PROGRESS', statusLabel: 'Reprogramada', failedAttempts: 1 }} compact />)
    expect(await screen.findByText('1 intento fallido')).toBeTruthy()
  })
  it('en inglés', async () => {
    setLang('en')
    await render(<FailedAttemptsChip count={3} />)
    expect(await screen.findByText('3 failed attempts')).toBeTruthy()
  })
})
