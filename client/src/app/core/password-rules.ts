/** Client-side mirror of the server PasswordPolicy, used only for live hints (the server is authoritative). */
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 128;

const RULES: readonly { code: string; satisfied: (p: string) => boolean }[] = [
  { code: 'tooShort', satisfied: p => p.length >= PASSWORD_MIN_LENGTH },
  { code: 'tooLong', satisfied: p => p.length <= PASSWORD_MAX_LENGTH },
  { code: 'missingUppercase', satisfied: p => /\p{Lu}/u.test(p) },
  { code: 'missingLowercase', satisfied: p => /\p{Ll}/u.test(p) },
  { code: 'missingDigit', satisfied: p => /\p{Nd}/u.test(p) },
  { code: 'missingSymbol', satisfied: p => /[^\p{L}\p{N}]/u.test(p) },
];

export interface PasswordHint {
  code: string;
  satisfied: boolean;
}

export function passwordHints(password: string): PasswordHint[] {
  return RULES.map(rule => ({ code: rule.code, satisfied: rule.satisfied(password) }));
}

export function isPasswordValid(password: string): boolean {
  return RULES.every(rule => rule.satisfied(password));
}
