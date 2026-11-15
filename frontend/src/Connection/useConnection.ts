import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiSend } from 'Helpers/Api/apiGet';
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

export type ConnectionStateName = 'NotConnected' | 'Connected' | 'Degraded' | 'NeedsLogin' | 'Paused';

/** The connection to the assistant, as the passes find it. */
export interface Connection {
  state: ConnectionStateName;
  since: string;
  lastOkAt: string | null;
  /** The code of why it is not simply connected (`needs-login`, `reader-broken`…), or null. */
  reason: string | null;
  /** `None`, `Web` or `Demo`: which reader reads. */
  reader: string;
  /** A browser reads this Geminarr: there is something to pause and to disconnect. */
  hasBrowser: boolean;
  pausedUntil: string | null;
  /** The page of the browser container, where a person signs in by hand (ADR 0016), or null when it is not given. */
  browserPageUrl: string | null;
}

export const CONNECTION_REFRESH_MS = 15_000;

const connectionPath = '/connection';

export function useConnection() {
  return useApiQuery<Connection>(connectionPath, { refetchInterval: CONNECTION_REFRESH_MS });
}

function useWrite<V, R>(send: (value: V) => Promise<R>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: send,
    onSuccess: () => client.invalidateQueries({ queryKey: ['api', connectionPath] }),
  });
}

/** Quiet for some hours, or until the operator resumes when no number is given. */
export function usePause() {
  return useWrite((hours: number | null) => apiSend<Connection>('POST', `${connectionPath}/pause`, { hours }));
}

export function useResume() {
  return useWrite(() => apiSend<Connection>('POST', `${connectionPath}/resume`));
}

export function useDisconnect() {
  return useWrite(() => apiSend<Connection>('POST', `${connectionPath}/disconnect`));
}
