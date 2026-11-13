import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

export type ConnectionStateName = 'NotConnected' | 'Connected' | 'Degraded' | 'NeedsLogin';

/** The connection to the assistant, as the passes find it. */
export interface Connection {
  state: ConnectionStateName;
  since: string;
  lastOkAt: string | null;
  /** The code of why it is not simply connected (`needs-login`, `reader-broken`…), or null. */
  reason: string | null;
  /** `None` or `Demo`: which reader reads. */
  reader: string;
}

export const CONNECTION_REFRESH_MS = 15_000;

export function useConnection() {
  return useApiQuery<Connection>('/connection', { refetchInterval: CONNECTION_REFRESH_MS });
}
