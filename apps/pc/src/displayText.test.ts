import { describe, expect, it } from 'vitest'
import { nameInitial } from './displayText'

describe('一覧アイコンの頭文字', () => {
  it('先頭の括弧書きや法人格を飛ばして、名前の最初の文字を返す', () => {
    expect(nameInitial('[DEMO] Alpha Partner Ltd.')).toBe('A')
    expect(nameInitial('[DEMO][R2] Beta')).toBe('B')
    expect(nameInitial('株式会社レッドハウス')).toBe('レ')
    expect(nameInitial('（株）青空')).toBe('青')
    expect(nameInitial('井上 真帆')).toBe('井')
  })

  it('飛ばすと何も残らない名前は、元の最初の文字を返す', () => {
    expect(nameInitial('[DEMO]')).toBe('[')
    expect(nameInitial('')).toBe('')
  })
})
