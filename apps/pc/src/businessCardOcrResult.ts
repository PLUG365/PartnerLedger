// 名刺の読み取り結果の項目（確認画面・正式登録で使う）。AI Builderの出力の解釈はフロー側で行う。
export type BusinessCardOcrFields = {
  addressCity?: string
  addressCountry?: string
  addressPostalCode?: string
  addressPostOfficeBox?: string
  addressState?: string
  addressStreet?: string
  businessPhone?: string
  companyName?: string
  department?: string
  email?: string
  fax?: string
  firstName?: string
  fullAddress?: string
  fullName?: string
  jobTitle?: string
  lastName?: string
  mobilePhone?: string
  website?: string
}
