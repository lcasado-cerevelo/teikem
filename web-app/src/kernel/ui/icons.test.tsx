import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { IconPlus, IconUpload } from './icons'

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
})
