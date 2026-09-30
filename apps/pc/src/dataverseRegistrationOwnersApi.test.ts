import { describe, expect, it } from 'vitest'
import type { Systemusers } from './generated/models/SystemusersModel'
import { toRegistrationOwner } from './dataverseRegistrationOwnersApi'

describe('主担当候補の境界', () => {
  it('Application Userを主担当候補へ含めない', () => {
    expect(toRegistrationOwner({
      systemuserid: '11111111-1111-4111-8111-111111111111',
      fullname: 'Connector App #',
      applicationid: '22222222-2222-4222-8222-222222222222',
    } as Systemusers)).toBeUndefined()
  })

  it('Microsoftのサポート用アカウントなど、通常以外のユーザーを主担当候補へ含めない', () => {
    for (const accessmode of [1, 2, 3, 4, 5]) {
      expect(toRegistrationOwner({
        systemuserid: '44444444-4444-4444-8444-444444444444',
        fullname: 'Support User',
        applicationid: null,
        accessmode,
      } as unknown as Systemusers)).toBeUndefined()
    }
  })

  it('人間の有効ユーザーは候補へ変換する', () => {
    expect(toRegistrationOwner({
      systemuserid: '33333333-3333-4333-8333-333333333333',
      fullname: '主担当候補',
      internalemailaddress: 'owner@example.invalid',
      applicationid: null,
      accessmode: 0,
    } as unknown as Systemusers)).toEqual({
      id: '33333333-3333-4333-8333-333333333333',
      name: '主担当候補',
      email: 'owner@example.invalid',
    })
  })
})
