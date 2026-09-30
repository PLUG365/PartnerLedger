import { readFile, readdir } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const scriptsDirectory = resolve(dirname(fileURLToPath(import.meta.url)), '../../../scripts')

describe('Plugin Step Run-as の再登録境界', () => {
  it('既存Stepの実行主体をCalling Userへ自動リセットする旧経路を残さない', async () => {
    const scriptNames = (await readdir(scriptsDirectory))
      .filter(name => /^register-.*\.ps1$/i.test(name))
    const offenders = []

    for (const name of scriptNames) {
      const source = await readFile(resolve(scriptsDirectory, name), 'utf8')
      if (/['"]impersonatinguserid@odata\.bind['"]\s*=\s*\$null/i.test(source)) {
        offenders.push(name)
      }
    }

    expect(offenders).toEqual([])
  })
})
