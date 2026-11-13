import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiSend } from 'Helpers/Api/apiGet';
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

export type ConnectionStateName = 'NotConnected' | 'Connected' | 'Degraded' | 'NeedsLogin' | 'Connecting' | 'Paused';

/** The connection to the assistant, as the passes find it. */
export interface Connection {
  state: ConnectionStateName;
  since: string;
  lastOkAt: string | null;
  /** The code of why it is not simply connected (`needs-login`, `reader-broken`…), or null. */
  reason: string | null;
  /** `None`, `Web` or `Demo`: which reader reads. */
  reader: string;
  /** A browser reads this Geminarr: there is something to sign in to. */
  hasBrowser: boolean;
  pausedUntil: string | null;
  liveViewMinutes: number;
}

/** A live view that was asked for: where to connect, the token that opens it once, and how big the screen is. */
export interface LiveViewTicket {
  id: string;
  token: string;
  path: string;
  expiresAt: string;
  width: number;
  height: number;
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

/** Asks for a live view: the browser's screen, where the operator signs in. */
export function useStartLiveView() {
  return useWrite(() => apiSend<LiveViewTicket>('POST', `${connectionPath}/live-views`));
}

export function useCloseLiveView() {
  return useWrite(() => apiSend('DELETE', `${connectionPath}/live-views`));
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
