import { type Kind, kinds } from 'Helpers/Props/kinds';
import type { TranslationKey } from 'Utilities/String/en';

import type { Connection } from './useConnection';

/** How each state of the connection is shown: its kind and its words. One map for the page and the card of the overview, so that a new state is shown the same way in both. */
export const connectionStates: Readonly<Record<Connection['state'], { kind: Kind; text: TranslationKey }>> = {
  Connected: { kind: kinds.SUCCESS, text: 'ConnectionConnected' },
  NeedsLogin: { kind: kinds.DANGER, text: 'ConnectionNeedsLogin' },
  Degraded: { kind: kinds.WARNING, text: 'ConnectionDegraded' },
  NotConnected: { kind: kinds.INFO, text: 'ConnectionNotConnected' },
  Paused: { kind: kinds.INFO, text: 'ConnectionPaused' },
};

/**
 * What a connection says in words. Before anything was ever read, "needs login" is not an expired session nor a defect: it is the state of a Geminarr that waits for its first sign-in, shown as information.
 */
export function connectionView(connection: Connection): { kind: Kind; text: TranslationKey; reason: string | null } {
  const waiting =
    connection.lastOkAt === null &&
    (connection.state === 'NeedsLogin' || connection.reason === 'needs-login') &&
    connection.account === null;
  if (waiting) {
    return { kind: kinds.INFO, text: 'ConnectionAwaitingSignIn', reason: 'awaiting-sign-in' };
  }
  const state = connectionStates[connection.state];
  return { kind: state.kind, text: state.text, reason: connection.reason };
}
