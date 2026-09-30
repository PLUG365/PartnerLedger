const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Dataverseの行ID（GUID）の形式か。応答の検証と、呼出し前の入力検証に使う。 */
export function isDataverseGuid(value: string): boolean {
  return guidPattern.test(value)
}
