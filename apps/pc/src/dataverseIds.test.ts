import { describe, expect, it } from 'vitest'
import { isDataverseGuid } from './dataverseIds'

describe('DataverseのID形式', () => {
  it('GUIDの形式だけを受け付け、大文字小文字は問わない', () => {
    expect(isDataverseGuid('33333333-3333-4333-8333-333333333333')).toBe(true)
    expect(isDataverseGuid('ABCDEFAB-1234-4ABC-8ABC-ABCDEFABCDEF')).toBe(true)
    expect(isDataverseGuid('not-a-guid')).toBe(false)
    expect(isDataverseGuid('{33333333-3333-4333-8333-333333333333}')).toBe(false)
    expect(isDataverseGuid('')).toBe(false)
  })
})
