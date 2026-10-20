// Tema claro y oscuro: las dos paletas tienen los mismos colores y TODOS los pares de texto y fondo cumplen el contraste de accesibilidad (WCAG AA, 4.5); la elección se recuerda y cambia al instante; el selector de idioma y de tema.
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { Text } from 'react-native'

import { getLang, setLang } from '../i18n/i18n'
import { StoreKeys, storeGet, storeSet } from '../storage'
import { PreferencesBar } from './PreferencesBar'
import { contrastRatio, DARK, getThemePref, initTheme, LIGHT, PALETTES, resolveScheme, setThemePref, useColors } from './theme'
import { Button, Card } from './ui'

const AA = 4.5

describe('paletas', () => {
  it('las dos tienen exactamente los mismos colores y todos son #RRGGBB', () => {
    expect(Object.keys(DARK).sort()).toEqual(Object.keys(LIGHT).sort())
    for (const palette of [LIGHT, DARK]) for (const value of Object.values(palette)) expect(value).toMatch(/^#[0-9A-F]{6}$/i)
    expect(PALETTES.light).toBe(LIGHT)
    expect(PALETTES.dark).toBe(DARK)
  })

  it.each([
    ['claro', LIGHT],
    ['oscuro', DARK],
  ])('en el tema %s todo texto se lee sobre su fondo (contraste AA)', (_name, p) => {
    // texto principal y secundario sobre la pantalla y las tarjetas
    for (const bg of [p.bg, p.card]) {
      expect(contrastRatio(p.text, bg)).toBeGreaterThanOrEqual(7)
      expect(contrastRatio(p.muted, bg)).toBeGreaterThanOrEqual(AA)
      expect(contrastRatio(p.link, bg)).toBeGreaterThanOrEqual(AA)
      expect(contrastRatio(p.danger, bg)).toBeGreaterThanOrEqual(AA)
    }
    // las etiquetas y avisos: texto de cada tono sobre su propio fondo
    for (const [fg, bg] of [[p.ok, p.okBg], [p.warn, p.warnBg], [p.danger, p.dangerBg], [p.info, p.infoBg], [p.muted, p.neutralBg]] as const) expect(contrastRatio(fg, bg)).toBeGreaterThanOrEqual(AA)
    // texto sobre los rellenos: el botón principal (naranja) lleva azul marino oscuro (el blanco no llega a AA sobre naranja), el secundario (azul marino) y el peligroso, blanco; y la barra de la marca
    expect(contrastRatio(p.onFlow, p.flow)).toBeGreaterThanOrEqual(AA)
    expect(contrastRatio(p.onSecondary, p.secondary)).toBeGreaterThanOrEqual(AA)
    expect(contrastRatio('#FFFFFF', p.dangerSolid)).toBeGreaterThanOrEqual(AA)
    expect(contrastRatio(p.onBrand, p.brand)).toBeGreaterThanOrEqual(7)
  })

  it('la barra de progreso se distingue del fondo y de la tarjeta (los tramos hechos y los que faltan)', () => {
    for (const p of [LIGHT, DARK]) {
      for (const bg of [p.bg, p.card]) {
        expect(contrastRatio(p.done, bg)).toBeGreaterThanOrEqual(3)
        expect(contrastRatio(p.link, bg)).toBeGreaterThanOrEqual(3)   // el tramo actual se pinta con el color de enlace (naranja legible)
        expect(contrastRatio(p.segAlert, bg)).toBeGreaterThanOrEqual(3)
        expect(contrastRatio(p.segTodo, bg)).toBeGreaterThanOrEqual(1.15)   // apagado a propósito, pero visible
      }
      expect(contrastRatio(p.done, p.segTodo)).toBeGreaterThanOrEqual(3)    // hecho vs. por venir
    }
  })

  it('el contraste: blanco sobre negro 21; igual 1', () => {
    expect(contrastRatio('#FFFFFF', '#000000')).toBeCloseTo(21, 0)
    expect(contrastRatio('#336699', '#336699')).toBeCloseTo(1, 5)
  })
})

describe('elección del tema', () => {
  it('Automático sigue al sistema (y si no dice, claro); Claro y Oscuro mandan', () => {
    expect(resolveScheme('auto', 'dark')).toBe('dark')
    expect(resolveScheme('auto', 'light')).toBe('light')
    expect(resolveScheme('auto', null)).toBe('light')
    expect(resolveScheme('light', 'dark')).toBe('light')
    expect(resolveScheme('dark', 'light')).toBe('dark')
  })
})

function Probe() {
  const c = useColors()
  return (
    <>
      <Text testID="bg">{c.bg}</Text>
      <Card>
        <Text>tarjeta</Text>
      </Card>
      <Button title="Aceptar" onPress={() => undefined} />
    </>
  )
}

describe('cambiar el tema y el idioma', () => {
  beforeEach(async () => {
    setThemePref('auto')
    setLang('es')
  })

  it('por omisión es Automático y español', () => {
    expect(getThemePref()).toBe('auto')
    expect(getLang()).toBe('es')
  })

  it('al escoger Oscuro los componentes cambian al instante y la elección se guarda; al arrancar se recupera', async () => {
    await render(
      <>
        <Probe />
        <PreferencesBar />
      </>,
    )
    expect(screen.getByTestId('bg')).toHaveTextContent(LIGHT.bg)
    await fireEvent.press(screen.getByTestId('theme-dark'))
    await waitFor(() => expect(screen.getByTestId('bg')).toHaveTextContent(DARK.bg))
    expect(screen.getByTestId('theme-dark').props.accessibilityState.selected).toBe(true)
    expect(await storeGet(StoreKeys.theme)).toBe('dark')
    await fireEvent.press(screen.getByTestId('theme-light'))
    await waitFor(() => expect(screen.getByTestId('bg')).toHaveTextContent(LIGHT.bg))
    // «arranque»: lo que quedó guardado se carga
    await storeSet(StoreKeys.theme, 'dark')
    await initTheme()
    expect(getThemePref()).toBe('dark')
  })

  it('el selector de idioma cambia los textos sin recargar y se recuerda; Español es el de omisión', async () => {
    await render(<PreferencesBar />)
    expect(screen.getByTestId('lang-es').props.accessibilityState.selected).toBe(true)
    expect(screen.getByText('Automático')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('lang-en'))
    await waitFor(() => expect(getLang()).toBe('en'))
    expect(await screen.findByText('Automatic')).toBeTruthy()
    expect(screen.getByText('Dark')).toBeTruthy()
    expect(await storeGet(StoreKeys.lang)).toBe('en')
  })
})
