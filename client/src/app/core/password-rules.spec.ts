import { isPasswordValid, passwordHints } from './password-rules';

describe('password rules', () => {
  it('accepts a password satisfying every rule', () => {
    expect(isPasswordValid('Correct-Horse-1')).toBe(true);
    expect(passwordHints('Correct-Horse-1').every(h => h.satisfied)).toBe(true);
  });

  it('reports each missing requirement', () => {
    const unmet = passwordHints('abc').filter(h => !h.satisfied).map(h => h.code);
    expect(unmet).toEqual(['tooShort', 'missingUppercase', 'missingDigit', 'missingSymbol']);
  });

  it('treats spaces as symbols like the server', () => {
    expect(isPasswordValid('Correct Horse 1')).toBe(true);
  });

  it('rejects passwords longer than the maximum', () => {
    expect(isPasswordValid(`Aa1-${'x'.repeat(130)}`)).toBe(false);
  });
});
