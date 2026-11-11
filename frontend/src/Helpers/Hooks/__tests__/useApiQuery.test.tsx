import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';

import { stubApi } from '__tests__/render';
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

function wrapper({ children }: Readonly<{ children: ReactNode }>) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe('useApiQuery', () => {
  it('reads the path under the API and gives its answer', async () => {
    stubApi({ '/things': { count: 2 } });

    const { result } = renderHook(() => useApiQuery<{ count: number }>('/things'), { wrapper });

    await waitFor(() => expect(result.current.data).toEqual({ count: 2 }));
  });

  it('asks nothing when it is not enabled', () => {
    stubApi({ '/things': { count: 2 } });

    const { result } = renderHook(() => useApiQuery('/things', { enabled: false }), { wrapper });

    expect(result.current.fetchStatus).toBe('idle');
    expect(fetch).not.toHaveBeenCalled();
  });

  it('reports the failure when the server answers an error', async () => {
    stubApi({});

    const { result } = renderHook(() => useApiQuery('/missing'), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('asks again when the refresh interval is a function of the last answer', async () => {
    stubApi({ '/things': { count: 1 } });
    const interval = vi.fn(() => false as const);

    const { result } = renderHook(() => useApiQuery('/things', { refetchInterval: interval }), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(interval).toHaveBeenCalledWith({ count: 1 });
  });
});
