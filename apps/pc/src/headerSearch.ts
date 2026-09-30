import type { ContactDirectoryItem } from './contactDirectory'
import type { PartnerDirectoryItem } from './partnerDirectory'

export type HeaderSearchResult = {kind: 'partner' | 'contact'; id: string; title: string; detail: string}

function normalize(value: string) {
  return value.normalize('NFKC').toLocaleLowerCase('ja-JP').replace(/\s+/g, '')
}

/** 読み込み済みの取引先・担当者から、名前などが一致するものを取引先→担当者の順に返す。 */
export function searchDirectory(partners: PartnerDirectoryItem[], contacts: ContactDirectoryItem[], query: string, limit = 8): HeaderSearchResult[] {
  const needle = normalize(query)
  if (!needle) return []
  const partnerResults = partners
    .filter(partner => [partner.name, partner.industry, partner.phone, partner.address].some(value => value && normalize(value).includes(needle)))
    .map(partner => ({kind: 'partner' as const, id: partner.id, title: partner.name, detail: ['取引先', partner.industry].filter(Boolean).join(' · ')}))
  const contactResults = contacts
    .filter(contact => [contact.name, contact.partnerName, contact.departmentRole, contact.email].some(value => value && normalize(value).includes(needle)))
    .map(contact => ({kind: 'contact' as const, id: contact.id, title: contact.name, detail: ['担当者', contact.partnerName].filter(Boolean).join(' · ')}))
  return [...partnerResults, ...contactResults].slice(0, limit)
}
