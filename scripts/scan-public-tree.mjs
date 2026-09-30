// 公開前の検査（2026-09-30）：リポジトリに、移送元の環境や人の情報が入っていないかを調べる。
// 対象は Git 管理下のファイルと、まだ追加していない（.gitignore で除外していない）ファイル。
// 1. 一般の形：Dataverse の環境 URL、onmicrosoft.com のアドレス、PC のユーザーフォルダのパス（架空の例は除く）。
// 2. 実 ID の一覧：リポジトリの外に置いた一覧（環境変数 PL_PRIVATE_SCAN_LIST か ~/.partnerledger/private-scan-list.txt）。
//    無い端末では、この部分を飛ばす。一覧は「種類<TAB>値」の行。
// 3. zip：中身を検査できないので、置くこと自体を不可にする（配布物はリポジトリに入れない）。
// 見つかった値そのものは出さない（ファイルと種類だけ）。見つかれば終了コード 1。
import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync, statSync } from 'node:fs'
import { homedir } from 'node:os'
import { join } from 'node:path'

const root = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim()
const files = execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: root, encoding: 'utf8' })
  .split('\0')
  .filter(Boolean)
  .filter((file) => existsSync(join(root, file)) && statSync(join(root, file)).isFile())

// 架空の例（テストや文書の見本）として使ってよい名前。
const fictional = /^(example|contoso|fabrikam|someone|user|username|yourname|public|default)$/i
const generic = [
  {
    kind: 'Dataverse の環境 URL',
    pattern: /([a-z0-9-]+)\.(?:api\.)?crm\d*\.(?:dynamics\.com|dynamics\.cn|microsoftdynamics\.(?:us|de)|appsplatform\.us)/gi,
  },
  { kind: 'onmicrosoft.com のアドレス', pattern: /[a-z0-9._%+-]+@([a-z0-9-]+)\.onmicrosoft\.com/gi },
  { kind: 'PC のユーザーフォルダのパス', pattern: /[a-z]:[\\/]+users[\\/]+([a-z0-9._-]+)/gi },
]

const listPath = process.env.PL_PRIVATE_SCAN_LIST || join(homedir(), '.partnerledger', 'private-scan-list.txt')
const privateEntries = existsSync(listPath)
  ? readFileSync(listPath, 'utf8')
      .split(/\r?\n/)
      .filter((line) => line && !line.startsWith('#') && line.includes('\t'))
      .map((line) => {
        const [kind, value] = line.split('\t')
        return { kind, value: value.trim().toLowerCase() }
      })
      .filter((entry) => entry.value.length >= 2)
  : []

const findings = []
for (const file of files) {
  if (/\.zip$/i.test(file)) {
    findings.push({ file, kind: 'zip ファイル（中身を検査できない）' })
    continue
  }
  const bytes = readFileSync(join(root, file))
  // UTF-8 と UTF-16LE（DLL などの中の文字列）の両方で調べる。
  const texts = [bytes.toString('utf8'), bytes.toString('utf16le')].map((text) => text.toLowerCase())
  const kinds = new Set()
  for (const text of texts) {
    for (const { kind, pattern } of generic) {
      for (const match of text.matchAll(pattern)) {
        if (!fictional.test(match[1])) kinds.add(kind)
      }
    }
    for (const { kind, value } of privateEntries) {
      if (text.includes(value)) kinds.add(`実 ID の一覧：${kind}`)
    }
  }
  for (const kind of kinds) findings.push({ file, kind })
}

console.log(`公開前の検査：${files.length} ファイル`)
if (privateEntries.length === 0) {
  console.log(`  実 ID の一覧が無いので、一般の形と zip だけを検査しました（${listPath}）。`)
} else {
  console.log(`  実 ID の一覧：${privateEntries.length} 件`)
}
if (findings.length > 0) {
  for (const { file, kind } of findings) console.error(`  見つかった：${file}（${kind}）`)
  console.error('公開前の検査に失敗しました。値を架空の例に置き換えるか、Git 管理から外してください。')
  process.exit(1)
}
console.log('  問題は見つかりませんでした。')
