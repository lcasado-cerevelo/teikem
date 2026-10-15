// Lote 53 — reglas puras de la pantalla de mensajes y la conexión en vivo (con una conexión falsa).
import type { HubConnection } from '@microsoft/signalr'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { avatarTone, dayKey, dayLabel, groupMessages, initialsOf, isOnline, liveDecision, messageBubbleText, presenceText, shouldSend } from './messageRules'
import type { MessageDto } from './messagesApi'
import { connectMessagesHub, type LiveEvent } from './messagesRealtime'

const t = (key: string, params?: Record<string, string | number>) => (params ? `${key}:${JSON.stringify(params)}` : key)

describe('avatar', () => {
  it('iniciales de dos letras', () => {
    expect(initialsOf('Ana Rivera')).toBe('AR')
    expect(initialsOf('  luis  ')).toBe('LU')
    expect(initialsOf('Juan Carlos Pérez')).toBe('JC')
    expect(initialsOf('')).toBe('·')
    expect(initialsOf(null)).toBe('·')
  })
  it('el tono es estable y está entre 0 y 5', () => {
    expect(avatarTone('Ana Rivera')).toBe(avatarTone('Ana Rivera'))
    for (const n of ['Ana', 'Luis', 'Pedro', 'Rosa', 'Zoe', '']) expect(avatarTone(n)).toBeGreaterThanOrEqual(0), expect(avatarTone(n)).toBeLessThanOrEqual(5)
  })
})

describe('presencia', () => {
  const now = Date.parse('2026-10-15T12:00:00Z')
  const date = (iso: string) => `D(${iso.slice(0, 10)})`
  it('en línea si lo vieron hace menos de 90 s', () => {
    expect(isOnline('2026-10-15T11:58:40Z', now)).toBe(true)
    expect(isOnline('2026-10-15T11:58:30Z', now)).toBe(false)
    expect(isOnline(null, now)).toBe(false)
  })
  it('el texto: en línea, hace un momento, minutos, horas, fecha, nunca', () => {
    expect(presenceText(true, null, now, t, date)).toBe('messages.presence.online')
    expect(presenceText(false, null, now, t, date)).toBe('messages.presence.never')
    expect(presenceText(false, '2026-10-15T11:59:50Z', now, t, date)).toBe('messages.presence.justNow')
    expect(presenceText(false, '2026-10-15T11:55:00Z', now, t, date)).toBe('messages.presence.minutes:{"n":5}')
    expect(presenceText(false, '2026-10-15T09:00:00Z', now, t, date)).toBe('messages.presence.hours:{"n":3}')
    expect(presenceText(false, '2026-10-10T09:00:00Z', now, t, date)).toBe('messages.presence.date:{"date":"D(2026-10-10)"}')
  })
})

describe('días y grupos de mensajes', () => {
  const mk = (id: number, sender: number, iso: string): MessageDto => ({ id, senderId: sender, senderName: `P${sender}`, body: `m${id}`, createdAtUtc: iso, mine: sender === 1 })
  it('hoy, ayer y fecha', () => {
    const now = new Date(2026, 9, 15, 12, 0, 0).getTime()
    expect(dayLabel(new Date(2026, 9, 15, 8, 0).toISOString(), now)).toBe('today')
    expect(dayLabel(new Date(2026, 9, 14, 23, 0).toISOString(), now)).toBe('yesterday')
    expect(dayLabel(new Date(2026, 9, 1, 8, 0).toISOString(), now)).toBe('date')
    expect(dayKey(new Date(2026, 0, 5, 1, 0).toISOString())).toBe('2026-01-05')
  })
  it('junta los seguidos de la misma persona dentro de 5 minutos y marca el cambio de día', () => {
    const a = new Date(2026, 9, 14, 10, 0, 0)
    const iso = (min: number) => new Date(a.getTime() + min * 60_000).toISOString()
    const groups = groupMessages([mk(1, 2, iso(0)), mk(2, 2, iso(2)), mk(3, 2, iso(10)), mk(4, 1, iso(11)), mk(5, 1, iso(60 * 24))])
    expect(groups.map((g) => g.messages.map((m) => m.id))).toEqual([[1, 2], [3], [4], [5]])
    expect(groups.map((g) => g.showDay)).toEqual([true, false, false, true])
    expect(groups.map((g) => g.mine)).toEqual([false, false, true, true])
  })
})

describe('Kemi y mensajes en vivo', () => {
  it('el globo dice quién escribió y cuántos', () => {
    expect(messageBubbleText('Ana', 1, t)).toBe('messages.kemi.bubbleOne:{"name":"Ana"}')
    expect(messageBubbleText('Ana', 3, t)).toBe('messages.kemi.bubbleMany:{"name":"Ana","n":3}')
  })
  it('lo que se está viendo se marca leído sin avisar; silenciado o dormida solo cuenta', () => {
    const base = { muted: false, threadOpen: false, windowVisible: true, asleep: false }
    expect(liveDecision({ ...base, threadOpen: true })).toBe('markRead')
    expect(liveDecision({ ...base, threadOpen: true, muted: true })).toBe('markRead')
    expect(liveDecision({ ...base, threadOpen: true, windowVisible: false })).toBe('bubble')
    expect(liveDecision(base)).toBe('bubble')
    expect(liveDecision({ ...base, muted: true })).toBe('quiet')
    expect(liveDecision({ ...base, asleep: true })).toBe('quiet')
  })
  it('Enter envía; Mayús+Enter no; vacío no; componiendo con IME no', () => {
    expect(shouldSend({ key: 'Enter', shiftKey: false }, 'hola')).toBe(true)
    expect(shouldSend({ key: 'Enter', shiftKey: true }, 'hola')).toBe(false)
    expect(shouldSend({ key: 'Enter', shiftKey: false }, '   ')).toBe(false)
    expect(shouldSend({ key: 'Enter', shiftKey: false, isComposing: true }, 'hola')).toBe(false)
    expect(shouldSend({ key: 'a', shiftKey: false }, 'hola')).toBe(false)
  })
})

/** Una conexión falsa con lo mínimo que usa el cliente. */
function fakeConnection() {
  const handlers = new Map<string, (e: LiveEvent) => void>()
  const cb: { reconnected?: () => void; reconnecting?: () => void; close?: () => void } = {}
  const conn = {
    on: vi.fn((name: string, h: (e: LiveEvent) => void) => void handlers.set(name, h)),
    onreconnected: vi.fn((h: () => void) => void (cb.reconnected = h)),
    onreconnecting: vi.fn((h: () => void) => void (cb.reconnecting = h)),
    onclose: vi.fn((h: () => void) => void (cb.close = h)),
    start: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined),
  }
  return { conn: conn as unknown as HubConnection, fake: conn, handlers, cb }
}

describe('connectMessagesHub', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())
  const ev = (tenantId: number): LiveEvent => ({ tenantId, threadPublicId: 't', messageId: 1, senderId: 2, senderName: 'Ana', preview: 'hola', muted: false })

  it('conecta, avisa el estado y solo entrega los eventos de la compañía actual', async () => {
    const f = fakeConnection()
    const onEvent = vi.fn()
    const onState = vi.fn()
    const h = connectMessagesHub({ url: '/hubs/messages', token: () => 'tok', tenantId: 7, onEvent, onState, create: () => f.conn })
    await vi.advanceTimersByTimeAsync(0)
    expect(f.fake.start).toHaveBeenCalledTimes(1)
    expect(onState).toHaveBeenLastCalledWith(true)
    f.handlers.get('MessageReceived')!(ev(7))
    f.handlers.get('MessageReceived')!(ev(8))   // otra compañía de la misma persona: se ignora
    f.handlers.get('ThreadChanged')!(ev(7))
    expect(onEvent.mock.calls.map((c) => c[0])).toEqual(['MessageReceived', 'ThreadChanged'])
    await h.stop()
    expect(f.fake.stop).toHaveBeenCalled()
  })

  it('al caer y volver avisa los dos estados (la web se pone al día al volver)', async () => {
    const f = fakeConnection()
    const onState = vi.fn()
    connectMessagesHub({ url: '/h', token: () => 'tok', tenantId: null, onEvent: vi.fn(), onState, create: () => f.conn })
    await vi.advanceTimersByTimeAsync(0)
    f.cb.reconnecting!()
    expect(onState).toHaveBeenLastCalledWith(false)
    f.cb.reconnected!()
    expect(onState).toHaveBeenLastCalledWith(true)
  })

  it('si no puede conectar reintenta cada 10 s hasta lograrlo, y detenerla cancela el reintento', async () => {
    const f = fakeConnection()
    f.fake.start.mockRejectedValueOnce(new Error('sin red')).mockResolvedValue(undefined)
    const onState = vi.fn()
    const h = connectMessagesHub({ url: '/h', token: () => 'tok', tenantId: null, onEvent: vi.fn(), onState, create: () => f.conn })
    await vi.advanceTimersByTimeAsync(0)
    expect(onState).toHaveBeenLastCalledWith(false)
    await vi.advanceTimersByTimeAsync(10_000)
    expect(f.fake.start).toHaveBeenCalledTimes(2)
    expect(onState).toHaveBeenLastCalledWith(true)
    await h.stop()

    const g = fakeConnection()
    g.fake.start.mockRejectedValue(new Error('sin red'))
    const h2 = connectMessagesHub({ url: '/h', token: () => 'tok', tenantId: null, onEvent: vi.fn(), onState: vi.fn(), create: () => g.conn })
    await vi.advanceTimersByTimeAsync(0)
    await h2.stop()
    await vi.advanceTimersByTimeAsync(30_000)
    expect(g.fake.start).toHaveBeenCalledTimes(1)   // detenida: no vuelve a intentar
  })

  it('si la cierra el servidor se vuelve a conectar', async () => {
    const f = fakeConnection()
    connectMessagesHub({ url: '/h', token: () => 'tok', tenantId: null, onEvent: vi.fn(), onState: vi.fn(), create: () => f.conn })
    await vi.advanceTimersByTimeAsync(0)
    f.cb.close!()
    await vi.advanceTimersByTimeAsync(10_000)
    expect(f.fake.start).toHaveBeenCalledTimes(2)
  })
})
