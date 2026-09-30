/** 一覧のアイコンに出す1文字。先頭の [..]・(..)・法人格を飛ばして、名前らしい最初の文字を使う。 */
export function nameInitial(name: string) {
  const stripped = name.replace(/^(?:\s|\[[^\]]*\]|【[^】]*】|\([^)]*\)|（[^）]*）|株式会社|有限会社|合同会社)+/u, '')
  return Array.from(stripped || name)[0] ?? ''
}
