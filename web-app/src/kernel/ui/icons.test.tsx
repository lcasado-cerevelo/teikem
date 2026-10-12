import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { IconPlus, IconUpload } from './icons'
import { IconBolt, IconForm, IconScan, IconTruck, IconVolume, IconVolumeOff } from './screenIcons'

describe('íconos del kit', () => {
  it('IconPlus e IconUpload pintan un SVG decorativo', () => {
    const { container } = render(
      <>
        <IconPlus />
        <IconUpload />
      </>,
    )
    const svgs = container.querySelectorAll('svg[aria-hidden="true"]')
    expect(svgs).toHaveLength(2)
  })

  it('IconBolt e IconForm (segmentado Rápida / Detallada) pintan un SVG decorativo', () => {
    const { container } = render(
      <>
        <IconBolt />
        <IconForm />
      </>,
    )
    expect(container.querySelectorAll('svg[aria-hidden="true"]')).toHaveLength(2)
  })

  it('IconTruck (Flota y mantenimiento) pinta un SVG decorativo', () => {
    const { container } = render(<IconTruck />)
    expect(container.querySelectorAll('svg[aria-hidden="true"]')).toHaveLength(1)
  })

  it('IconScan, IconVolume e IconVolumeOff (Estación de escaneo) pintan un SVG decorativo', () => {
    const { container } = render(
      <>
        <IconScan />
        <IconVolume />
        <IconVolumeOff />
      </>,
    )
    expect(container.querySelectorAll('svg[aria-hidden="true"]')).toHaveLength(3)
  })
})
