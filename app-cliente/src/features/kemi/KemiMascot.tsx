// La mascota de Teikem (Kemi) en el portal: el MISMO dibujo de la web (web-app/src/features/notifications/KemiMascot.tsx), con react-native-svg, y la cara que le pone `faceOf` (shared/kemi/rules.ts),
// que es lo mismo que la web hace con CSS. Dormida: ojos cerrados y una «z».
import { memo } from 'react'
import Svg, { ClipPath, Defs, Ellipse, G, Path, Rect, Circle, Text as SvgText } from 'react-native-svg'

import type { KemiFace } from '../../../../shared/kemi/rules'

const CALM: KemiFace = { eyeL: { sx: 1, sy: 1 }, eyeR: { sx: 1, sy: 1 }, pupil: { x: 0, y: 0 }, mouth: 'smile' }
const BODY = 'M62 22 L102 44 V90 L62 112 L22 90 V44 Z'

function Eye({ cx, face, side }: { cx: number; face: KemiFace; side: 'l' | 'r' }) {
  const e = side === 'l' ? face.eyeL : face.eyeR
  const cy = 60
  return (
    <G transform={`translate(${cx} ${cy}) scale(${e.sx} ${e.sy}) translate(${-cx} ${-cy})`}>
      <Ellipse cx={cx} cy={cy} rx={11} ry={13} fill="#FFFFFF" />
      <G transform={`translate(${face.pupil.x} ${face.pupil.y})`}>
        <Circle cx={cx + 2} cy={63} r={5} fill="#0B2C66" />
        <Circle cx={cx - 0.5} cy={58} r={1.9} fill="#FFFFFF" />
      </G>
    </G>
  )
}

function Mouth({ kind }: { kind: KemiFace['mouth'] }) {
  switch (kind) {
    case 'happy':
      return <Path d="M50 86 Q63 106 76 86 Z" fill="#FF6A1A" stroke="#FF6A1A" strokeWidth={3} strokeLinejoin="round" />
    case 'surprised':
      return <Ellipse cx={63} cy={93} rx={5.5} ry={7} fill="#FF6A1A" />
    case 'tongue':
      return (
        <>
          <Path d="M52 88 Q63 97 74 88" fill="none" stroke="#FF6A1A" strokeWidth={4.5} strokeLinecap="round" />
          <Path d="M57 91 Q63 109 69 91 Z" fill="#FF8FA3" stroke="#FF8FA3" strokeWidth={2} strokeLinejoin="round" />
        </>
      )
    case 'worried':
      return <Path d="M52 96 Q63 86 74 96" fill="none" stroke="#FF6A1A" strokeWidth={4.5} strokeLinecap="round" />
    default:
      return <Path d="M52 88 Q63 97 74 88" fill="none" stroke="#FF6A1A" strokeWidth={4.5} strokeLinecap="round" />
  }
}

interface Props {
  size?: number
  sleeping?: boolean
  face?: KemiFace
}

function KemiMascotView({ size = 38, sleeping = false, face = CALM }: Props) {
  return (
    <Svg width={size} height={size} viewBox="0 0 124 132" accessibilityElementsHidden importantForAccessibility="no-hide-descendants" testID={sleeping ? 'kemi-mascot-asleep' : 'kemi-mascot'}>
      <Defs>
        <ClipPath id="kemi-body">
          <Path d={BODY} />
        </ClipPath>
      </Defs>
      <Path d="M62 22 V10" stroke="#FF6A1A" strokeWidth={4} strokeLinecap="round" />
      <Circle cx={62} cy={8} r={5.5} fill="#FF6A1A" />
      <Path d={BODY} fill="#0B2C66" />
      <G clipPath="url(#kemi-body)">
        <Rect x={22} y={22} width={40} height={90} fill="#1F6FE5" />
        <Rect x={62} y={22} width={40} height={90} fill="#15347A" />
      </G>
      <Path d={BODY} fill="none" stroke="#FFFFFF" strokeWidth={2.5} />
      {sleeping ? (
        <G stroke="#FFFFFF" strokeWidth={4.5} strokeLinecap="round" fill="none">
          <Path d="M39 62 Q49 70 59 62" />
          <Path d="M67 62 Q77 70 87 62" />
        </G>
      ) : (
        <>
          <Eye cx={49} face={face} side="l" />
          <Eye cx={77} face={face} side="r" />
        </>
      )}
      {sleeping ? <Path d="M52 88 Q63 97 74 88" fill="none" stroke="#FF6A1A" strokeWidth={4.5} strokeLinecap="round" /> : <Mouth kind={face.mouth} />}
      {sleeping && (
        <SvgText x={92} y={30} fontSize={26} fontWeight="700" fill="#FF8B4D">
          z
        </SvgText>
      )}
    </Svg>
  )
}

export const KemiMascot = memo(KemiMascotView)
