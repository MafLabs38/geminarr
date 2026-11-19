import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiSend } from 'Helpers/Api/apiGet';
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

/** The Mnemarr that Geminarr sends to: never the key. */
export interface MnemarrState {
  url: string | null;
  machine: string;
  linked: boolean;
  /** The deployment gives the address: it is not changed here. */
  addressFromSettings: boolean;
  /** The code of what is wrong (`mnemarr-not-linked`, `mnemarr-refused`…), or null. */
  problem: string | null;
  lastHeartbeatAt: string | null;
  pendingLines: number;
}

export type PairingStateName = 'none' | 'pending' | 'approved' | 'rejected' | 'expired';

/** A pairing that waits for a person to approve it in Mnemarr. */
export interface Pairing {
  state: PairingStateName;
  userCode: string | null;
  verificationUrl: string | null;
  expiresAt: string | null;
}

const mnemarrPath = '/mnemarr';
const pairingPath = '/mnemarr/pairing';

export const MNEMARR_REFRESH_MS = 10_000;

export function useMnemarr() {
  return useApiQuery<MnemarrState>(mnemarrPath, { refetchInterval: MNEMARR_REFRESH_MS });
}

export function usePairing() {
  return useApiQuery<Pairing>(pairingPath, { staleTime: Infinity });
}

/** Every write refreshes what the page shows from the server. */
function useWrite<V, R>(send: (value: V) => Promise<R>, after?: (result: R) => void) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: send,
    onSuccess: async (result) => {
      after?.(result);
      await client.invalidateQueries({ queryKey: ['api', mnemarrPath] });
    },
  });
}

export function useSetAddress() {
  return useWrite((url: string) => apiSend<MnemarrState>('PUT', mnemarrPath, { url }));
}

export function useUnlink() {
  return useWrite(() => apiSend('DELETE', mnemarrPath));
}

export function useStartPairing() {
  const client = useQueryClient();
  return useWrite(
    () => apiSend<Pairing>('POST', pairingPath),
    (pairing) => client.setQueryData(['api', pairingPath], pairing),
  );
}

/** Asks Mnemarr what became of the pairing; when a person approved it, the machine is linked and the page shows it. */
export function useCheckPairing() {
  const client = useQueryClient();
  return useWrite(
    () => apiSend<Pairing>('POST', `${pairingPath}/check`),
    (pairing) => client.setQueryData(['api', pairingPath], pairing),
  );
}

export function useCancelPairing() {
  const client = useQueryClient();
  return useWrite(
    () => apiSend('DELETE', pairingPath),
    () =>
      client.setQueryData(['api', pairingPath], {
        state: 'none',
        userCode: null,
        verificationUrl: null,
        expiresAt: null,
      }),
  );
}
