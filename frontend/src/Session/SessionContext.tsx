import { createContext, type ReactNode, useContext } from 'react';

import type { Session } from './session';

const SessionContext = createContext<Session | null>(null);

export function SessionProvider({ session, children }: Readonly<{ session: Session; children: ReactNode }>) {
  return <SessionContext.Provider value={session}>{children}</SessionContext.Provider>;
}

/** The signed-in session. Only used below the gate, where there is one. */
export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) {
    throw new Error('useSession needs a signed-in session');
  }
  return session;
}
