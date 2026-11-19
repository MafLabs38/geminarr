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
  backfill: Backfill;
}

export const CONVERSATIONS_REFRESH_MS = 15_000;

export function useConversations() {
  return useApiQuery<Conversations>('/conversations?limit=100', { refetchInterval: CONVERSATIONS_REFRESH_MS });
}
