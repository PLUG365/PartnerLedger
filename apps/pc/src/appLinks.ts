import { isDataverseGuid } from './dataverseIds'

export type PartnerDetailTab = '基本情報' | '担当者' | '契約・期限' | '掲示板' | '共有設定'
export type PartnerLink = {id: string; tab: PartnerDetailTab}

/** 通知メールのリンク（partnerId、契約の通知はpartnerTab=contracts付き）から開く取引先。GUIDの形でなければ使わない。 */
export function partnerLinkFromQuery(params: Record<string, string | undefined>): PartnerLink | undefined {
  const id = params.partnerId
  if (!id || !isDataverseGuid(id)) return undefined
  return {id, tab: params.partnerTab === 'contracts' ? '契約・期限' : '基本情報'}
}
