import { shouldRetry } from 'App/queryClient';
import { ApiError } from 'Helpers/Api/apiGet';

describe('shouldRetry', () => {
  it('retries once for a failure that may pass', () => {
    expect(shouldRetry(0, new Error('network'))).toBe(true);
    expect(shouldRetry(1, new Error('network'))).toBe(false);
  });

  it.each([401, 403, 404])('does not retry an answer %i that will not change', (code) => {
    expect(shouldRetry(0, new ApiError(code, 'no'))).toBe(false);
  });
});
