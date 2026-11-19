import { useQuery } from '@tanstack/react-query';

import { apiGet } from 'Helpers/Api/apiGet';
import { LIVE_FALLBACK_MS, useLiveStatus } from 'Live/liveStatus';

interface ApiQueryOptions<T> {
  /** Milliseconds between two automatic refreshes; or a function of the last answer, which says when to ask again, or false to stop. */
  refetchInterval?: number | ((data: T | undefined) => number | false);
  /** False: nothing is asked (the person may not read it). */
  enabled?: boolean;
  /** Milliseconds during which the answer is not asked again; `Infinity` for what never changes while the page is open. */
  staleTime?: number;
}

/** While the channel that announces changes is open, a read that was asked again every few seconds is asked again slowly: the announcement does it. */
function pace<T>(interval: ApiQueryOptions<T>['refetchInterval'], live: boolean) {
  const slow = (every: number | false) => (live && every !== false ? Math.max(every, LIVE_FALLBACK_MS) : every);
  if (typeof interval === 'function') {
    return (query: { state: { data: T | undefined } }) => slow(interval(query.state.data));
  }
  return interval === undefined ? undefined : slow(interval);
}

/** Server data goes through here, never through a direct `fetch` in a component. */
export function useApiQuery<T>(path: string, options: ApiQueryOptions<T> = {}) {
  const { refetchInterval, ...rest } = options;
  const live = useLiveStatus() === 'live';
  return useQuery<T>({
    queryKey: ['api', path],
    queryFn: ({ signal }) => apiGet<T>(path, signal),
    ...rest,
    refetchInterval: pace(refetchInterval, live),
  });
}
