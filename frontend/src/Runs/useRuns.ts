import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiSend } from 'Helpers/Api/apiGet';
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

/** One pass: counts only, never what a conversation said. */
export interface Run {
  id: number;
  kind: string;
  startedAt: string;
  endedAt: string | null;
  outcome: 'ok' | 'partial' | 'failed' | 'skipped' | null;
  reason: string | null;
  listed: number;
  opened: number;
  turnsSent: number;
  durationMs: number;
}

export interface Runs {
  runs: Run[];
  running: boolean;
}

const path = '/runs?limit=5';

export const RUNS_REFRESH_MS = 10_000;

export function useRuns() {
  return useApiQuery<Runs>(path, { refetchInterval: RUNS_REFRESH_MS });
}

/** "Run now": the pass runs in the background, and the list follows. */
export function useRunNow() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => apiSend('POST', '/runs'),
    onSuccess: () => client.invalidateQueries({ queryKey: ['api', path] }),
  });
}
