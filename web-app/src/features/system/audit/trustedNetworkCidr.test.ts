import { describe, expect, it } from 'vitest'
import { cidrContains, formatCount, formatIp, isPrivateRange, parseCidr, parseIp, PREFIX_SHORTCUTS, withPrefix, type ParsedCidr } from './trustedNetworkCidr'

const ok = (text: string): ParsedCidr => {
  const r = parseCidr(text)
  expect(r.error).toBeUndefined()
  return r.cidr!
}

describe('IP preferida · lectura de IPs', () => {
  it('IPv4 estricto y IPv6, ida y vuelta', () => {
    expect(formatIp(parseIp('203.0.113.10')!.value, 4)).toBe('203.0.113.10')
    expect(parseIp(' 203.0.113.10 ')?.family).toBe(4)
    expect(formatIp(parseIp('2001:db8::1')!.value, 6)).toBe('2001:db8::1')
    expect(formatIp(parseIp('2001:0db8:0000:0000:0000:0000:0000:0001')!.value, 6)).toBe('2001:db8::1')
    expect(formatIp(parseIp('::1')!.value, 6)).toBe('::1')
    expect(formatIp(parseIp('::')!.value, 6)).toBe('::')
    expect(formatIp(parseIp('fe80:0:0:1:0:0:0:2')!.value, 6)).toBe('fe80:0:0:1::2')
  })

  it('formas sueltas o mal escritas no son IPs', () => {
    for (const bad of ['', '  ', 'abc', '127.1', '203.0.113', '203.0.113.256', '203.0.113.10.5', '1', 'fe80::1%eth0', '1::2::3', ':1::2', '12345::1', '2001:db8:1:2:3:4:5:6:7']) {
      expect(parseIp(bad)).toBeNull()
    }
  })

  it('una IPv6 mapeada a IPv4 se lee como IPv4', () => {
    const ip = parseIp('::ffff:203.0.113.10')!
    expect(ip.family).toBe(4)
    expect(formatIp(ip.value, 4)).toBe('203.0.113.10')
  })
})

describe('IP preferida · rangos', () => {
  it('una IP es /32; un rango se normaliza a su red y avisa si se ajustó', () => {
    const single = ok('203.0.113.10')
    expect([single.text, single.single, single.count, single.adjusted]).toEqual(['203.0.113.10/32', true, 1n, false])
    const range = ok('192.168.10.5/24')
    expect([range.text, range.single, range.count, range.adjusted, range.first, range.last]).toEqual(['192.168.10.0/24', false, 256n, true, '192.168.10.0', '192.168.10.255'])
    expect(ok('192.168.10.0/24').adjusted).toBe(false)
    expect(ok('198.51.100.8/29').count).toBe(8n)
    expect(ok('2001:db8:abcd::/48').count).toBe(1n << 80n)
    expect(ok('2001:db8:abcd::/48').text).toBe('2001:db8:abcd::/48')
  })

  it('mismos errores y orden que el servidor', () => {
    expect(parseCidr('').error).toBe('required')
    expect(parseCidr('abc').error).toBe('invalid')
    expect(parseCidr('127.1').error).toBe('invalid')
    expect(parseCidr('203.0.113.10/33').error).toBe('prefix')
    expect(parseCidr('203.0.113.10/').error).toBe('prefix')
    expect(parseCidr('203.0.113.10 / 24').error).toBe('prefix')
    expect(parseCidr('2001:db8::1/129').error).toBe('prefix')
    expect(parseCidr('10.0.0.0/8').error).toBe('tooWide')
    expect(parseCidr('0.0.0.0/0').error).toBe('tooWide')
    expect(parseCidr('::/0').error).toBe('tooWide')
    expect(parseCidr('2001:db8::/32').error).toBe('tooWide')
    expect(parseCidr('0.0.0.0').error).toBe('notRoutable')
    expect(parseCidr('127.0.0.1').error).toBe('notRoutable')
    expect(parseCidr('224.0.0.1').error).toBe('notRoutable')
    expect(parseCidr('::1').error).toBe('notRoutable')
    expect(parseCidr('ff02::1').error).toBe('notRoutable')
    expect(parseCidr('::ffff:203.0.113.0/120').error).toBe('mapped')
    expect(ok('::ffff:203.0.113.10').text).toBe('203.0.113.10/32')
  })

  it('los rangos más anchos que se aceptan: /16 y /48', () => {
    expect(ok('172.16.5.5/16').text).toBe('172.16.0.0/16')
    expect(ok('172.16.5.5/16').count).toBe(65536n)
    expect(ok('2001:db8:abcd:1::/48').text).toBe('2001:db8:abcd::/48')
  })

  it('coincidencia: dentro y fuera, familias distintas y sin IP', () => {
    const net = ok('198.51.100.8/29')
    expect(cidrContains(net, parseIp('198.51.100.15'))).toBe(true)
    expect(cidrContains(net, parseIp('198.51.100.16'))).toBe(false)
    expect(cidrContains(net, parseIp('198.51.100.7'))).toBe(false)
    expect(cidrContains(ok('203.0.113.10'), parseIp('203.0.113.10'))).toBe(true)
    expect(cidrContains(ok('203.0.113.10'), parseIp('2001:db8::1'))).toBe(false)
    expect(cidrContains(ok('2001:db8:abcd::/48'), parseIp('2001:db8:abcd:ffff::1'))).toBe(true)
    expect(cidrContains(ok('2001:db8:abcd::/48'), parseIp('2001:db8:abce::1'))).toBe(false)
    expect(cidrContains(net, null)).toBe(false)
    // un cliente IPv4 visto como ::ffff:a.b.c.d coincide con la entrada IPv4
    expect(cidrContains(ok('192.168.10.0/24'), parseIp('::ffff:192.168.10.77'))).toBe(true)
  })

  it('rangos privados (no son IPs de internet)', () => {
    for (const p of ['10.1.2.0/24', '172.20.0.0/16', '192.168.10.0/24', '169.254.1.0/24', '100.64.0.0/16', 'fd12:3456:789a::/48']) expect(isPrivateRange(ok(p))).toBe(true)
    for (const p of ['203.0.113.10', '198.51.100.0/24', '172.32.0.0/16', '2001:db8:abcd::/48']) expect(isPrivateRange(ok(p))).toBe(false)
  })

  it('atajos de tamaño y cambio del prefijo escrito', () => {
    expect(PREFIX_SHORTCUTS.map((s) => [s.prefix, s.count])).toEqual([[32, 1], [29, 8], [28, 16], [27, 32], [26, 64], [25, 128], [24, 256], [20, 4096], [16, 65536]])
    expect(withPrefix('192.168.10.5', 24)).toBe('192.168.10.5/24')
    expect(withPrefix('192.168.10.5/32', 24)).toBe('192.168.10.5/24')
    expect(withPrefix('   ', 24)).toBe('')
    expect(formatCount(65536n, 'en')).toBe('65,536')
  })
})
