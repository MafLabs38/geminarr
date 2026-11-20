import { useMutation, useQueryClient } from '@tanstack/react-query';

import { apiSend } from 'Helpers/Api/apiGet';
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

/**
 * One account that is known (SPEC §8.3). The id is opaque and the address is never in it: the machine, named from the address, is what says which account it is. Nothing of a conversation.
 */
export interface Account {
  id: string;
  /** The machine of the account in Mnemarr (the one that it is paired as, or will be); null when an account that is not signed in has none yet. */
  machine: string | null;
  /** The account that the pages follow: the one that is signed in, or whose session expired and that tells Mnemarr. */
  active: boolean;
  signedIn: boolean;
  linked: boolean;
  lastSignOfLifeAt: string | null;
  waitingLines: number;
  conversations: number;
  lastReadAt: string | null;
}

interface Accounts {
  accounts: Account[];
}

export const ACCOUNTS_REFRESH_MS = 15_000;

const path = '/accounts';

export function useAccounts() {
  return useApiQuery<Accounts>(path, { refetchInterval: ACCOUNTS_REFRESH_MS });
}

/** Forgets an account: its workspace is deleted here (what Mnemarr holds is not touched). */
export function useForgetAccount() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiSend('DELETE', `${path}/${id}`),
    onSettled: () => client.invalidateQueries({ predicate: ({ queryKey }) => queryKey[0] === 'api' }),
  });
}
