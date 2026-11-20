import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

/** One conversation: counts and states. Never a title and never a turn: Geminarr keeps neither. */
export interface Conversation {
  id: string;
  source: string;
  state: 'new' | 'synced' | 'gone' | 'error';
  turnsKnown: number;
  turnsSent: number;
  rewrittenTurns: number;
  listRank: number | null;
  firstSeenAt: string;
  lastSeenAt: string;
  lastOpenedAt: string | null;
  lastError: string | null;
}

/** How far the history has been walked (the list that the page showed, and how much of it is read). */
export interface Backfill {
  enabled: boolean;
  perPass: number;
  listed: number;
  known: number;
  listComplete: boolean;
  done: boolean;
}

export interface Conversations {
  total: number;
  conversations: Conversation[];
  /** The progress of the history: only for the account that is being read (the list that the page showed is its own), null for another one. */
  backfill: Backfill | null;
}

export const CONVERSATIONS_REFRESH_MS = 15_000;

/** The conversations of the account that is being read, or of the one that `account` names (the id that the accounts give, never an address). */
export function useConversations(account?: string) {
  const path = account ? `/conversations?limit=100&account=${encodeURIComponent(account)}` : '/conversations?limit=100';
  return useApiQuery<Conversations>(path, { refetchInterval: CONVERSATIONS_REFRESH_MS });
}
