import { useQuery } from '@tanstack/react-query';

import { apiGet } from 'Helpers/Api/apiGet';

interface ApiQueryOptions<T> {
  /** Milliseconds between two automatic refreshes; or a function of the last answer, which says when to ask again, or false to stop. */
  refetchInterval?: number | ((data: T | undefined) => number | false);
  /** False: nothing is asked (the person may not read it). */
  enabled?: boolean;
  /** Milliseconds during which the answer is not asked again; `Infinity` for what never changes while the page is open. */
  staleTime?: number;
}

/** Server data goes through here, never through a direct `fetch` in a component. */
export function useApiQuery<T>(path: string, options: ApiQueryOptions<T> = {}) {
  const { refetchInterval, ...rest } = options;
  return useQuery<T>({
    queryKey: ['api', path],
    queryFn: ({ signal }) => apiGet<T>(path, signal),
    ...rest,
    refetchInterval:
      typeof refetchInterval === 'function' ? (query) => refetchInterval(query.state.data) : refetchInterval,
  });
}
