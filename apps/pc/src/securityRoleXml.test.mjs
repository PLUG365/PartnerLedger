import { readFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../../')
const systemAdminRolePath = resolve(repositoryRoot, 'solutions/PartnerLedger/Roles/PL システム管理.xml')

describe('PL システム管理ロールの承認取消境界', () => {
  it('申請一覧と限定取消に必要な申請権限だけを含み、提出版の全件Readを与えない', async () => {
    const xml = await readFile(systemAdminRolePath, 'utf8')
    const privileges = [...xml.matchAll(/<RolePrivilege name="([^"]+)" level="([^"]+)"\s*\/>/g)]
      .map(([, name, level]) => ({name, level}))
    const requestPrivileges = privileges.filter(privilege => privilege.name === 'prvReadpl_Request' || privilege.name === 'prvWritepl_Request')

    expect(requestPrivileges).toEqual([
      {name: 'prvReadpl_Request', level: 'Global'},
      {name: 'prvWritepl_Request', level: 'Global'},
    ])
    expect(privileges).not.toContainEqual({name: 'prvReadpl_SubmissionVersion', level: 'Global'})
  })
})

describe('承認結果の標準Update境界', () => {
  it('通常利用者と承認者のRoleにApprovalLinkのWriteを付与しない', async () => {
    for (const roleName of ['PL 利用者', 'PL 承認']) {
      const xml = await readFile(resolve(repositoryRoot, `solutions/PartnerLedger/Roles/${roleName}.xml`), 'utf8')
      expect(xml).not.toMatch(/<RolePrivilege name="prvWritepl_ApprovalLink"\s/)
    }
  })
})

describe('人向けRoleの環境変数権限', () => {
  // 2026-09-26 Role方針③：承認系PluginのRun-asはシステム管理者なので、どの人向けRoleにも環境変数の権限は要らない。
  it('4つの人向けRoleのどれにも、環境変数の読取・変更権限を与えない', async () => {
    for (const roleName of ['PL システム保守', 'PL システム管理', 'PL 利用者', 'PL 承認']) {
      const xml = await readFile(resolve(repositoryRoot, `solutions/PartnerLedger/Roles/${roleName}.xml`), 'utf8')
      expect(xml, roleName).not.toMatch(/<RolePrivilege name="prv(Read|Create|Write|Delete)EnvironmentVariable/)
    }
  })
})
