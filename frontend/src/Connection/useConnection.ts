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
  /** A desktop of the browser is there, where a person signs in by hand (ADR 0017, 0019): it is opened by an address that is asked for, not a path of this page. */
  hasDesktop: boolean;
}

export const CONNECTION_REFRESH_MS = 15_000;

/** The Connection page is looked at by a person who is waiting for it to change (they have just signed in): it asks again every few seconds. */
export const CONNECTION_WAITING_REFRESH_MS = 2_000;

const connectionPath = '/connection';

export function useConnection(refreshMs: number = CONNECTION_REFRESH_MS) {
  return useApiQuery<Connection>(connectionPath, { refetchInterval: refreshMs });
}

function useWrite<V, R>(send: (value: V) => Promise<R>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: send,
    onSettled: () => client.invalidateQueries({ queryKey: ['api', connectionPath] }),
  });
}

/** Quiet for some hours, or until the operator resumes when no number is given. */
export function usePause() {
  return useWrite((hours: number | null) => apiSend<Connection>('POST', `${connectionPath}/pause`, { hours }));
}

export function useResume() {
  return useWrite(() => apiSend<Connection>('POST', `${connectionPath}/resume`));
}

/** The address to open the desktop of the browser by: an origin of its own, with a ticket that is worth one opening (ADR 0019). */
export function useOpenDesktop() {
  return useMutation({ mutationFn: () => apiSend<{ url: string }>('POST', `${connectionPath}/desktop`) });
}

export function useDisconnect() {
  return useWrite(() => apiSend<Connection>('POST', `${connectionPath}/disconnect`));
}
