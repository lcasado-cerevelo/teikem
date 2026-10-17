import { describe, expect, it } from 'vitest'
import { badgeText, charsLeft, isStaffMessage, MESSAGE_MAX, mergeMessages, sortConversations, totalUnread, validateMessage } from './rules'

describe('validateMessage', () => {
  it('vacío o solo espacios es «empty»', () => {
    expect(validateMessage('')).toBe('empty')
    expect(validateMessage('   \n ')).toBe('empty')
  })
  it('2000 caracteres pasa y 2001 no', () => {
    expect(validateMessage('a'.repeat(MESSAGE_MAX))).toBeNull()
    expect(validateMessage('a'.repeat(MESSAGE_MAX + 1))).toBe('tooLong')
  })
  it('charsLeft cuenta lo que falta y puede ser negativo', () => {
    expect(charsLeft('hola')).toBe(1996)
    expect(charsLeft('a'.repeat(2005))).toBe(-5)
  })
})

describe('mergeMessages', () => {
  it('ordena por id, no repite y gana el entrante', () => {
    const merged = mergeMessages([{ id: 2, body: 'dos' }, { id: 1, body: 'uno' }], [{ id: 2, body: 'dos!' }, { id: 3, body: 'tres' }])
    expect(merged.map((m) => [m.id, m.body])).toEqual([
      [1, 'uno'],
      [2, 'dos!'],
      [3, 'tres'],
    ])
  })
})

describe('conversaciones', () => {
  it('lo más reciente primero y empate por nombre', () => {
    const sorted = sortConversations([
      { publicId: 'a', personName: 'Beto', lastMessageAtUtc: '2026-10-10T10:00:00' },
      { publicId: 'b', personName: 'Ana', lastMessageAtUtc: '2026-10-11T10:00:00' },
      { publicId: 'c', personName: 'Aba', lastMessageAtUtc: '2026-10-10T10:00:00' },
    ])
    expect(sorted.map((c) => c.publicId)).toEqual(['b', 'c', 'a'])
  })
  it('suma no leídos, alinea burbujas y formatea la insignia', () => {
    expect(totalUnread([{ unread: 2 }, { unread: 3 }, {}])).toBe(5)
    expect(isStaffMessage({ senderKind: 'STAFF' })).toBe(true)
    expect(isStaffMessage({ senderKind: 'CLIENT' })).toBe(false)
    expect([badgeText(0), badgeText(7), badgeText(100)]).toEqual(['', '7', '99+'])
  })
})
