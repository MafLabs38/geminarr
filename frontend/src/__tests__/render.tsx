import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';

import type { Session } from 'Session/session';
import { SessionProvider } from 'Session/SessionContext';
import type { SystemStatus } from 'System/Status/useSystemStatus';

export const status: SystemStatus = {
  version: '0.0.0-test',
  runtimeVersion: '.NET 10.0.0',
  startedAt: '2026-11-11T08:00:00+00:00',
  uptimeSeconds: 3725,
};

export const session: Session = { name: 'operator-name' };

/** The address of a call, whatever form `fetch` was given it in. */
function requestAddress(input: RequestInfo | URL): string {
  if (typeof input === 'string') {
    return input;
  }
  return input instanceof URL ? input.href : input.url;
}

/**
 * Answers the calls of the application like the API would, from a table of paths (the method is not looked at). A value is the JSON body of a 200, or a
 * `Response` for any other answer. A path that is not in the table is a 404.
 */
export function stubApi(routes: Readonly<Record<string, unknown>>) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const path = new URL(requestAddress(input), 'http://localhost').pathname.replace(/^\/api\/v1/, '');
      if (!(path in routes)) {
        return Promise.resolve(new Response(null, { status: 404 }));
      }
      const value = routes[path];
      return Promise.resolve(value instanceof Response ? value.clone() : Response.json(value));
    }),
  );
}

/** An answer with a status and no body, for `stubApi`. */
export const answer = (code: number) => new Response(null, { status: code });

/** Renders a screen as the signed-in operator, with a query client that does not retry and a router at the given address. */
export function renderAt(ui: ReactElement, path = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <SessionProvider session={session}>
        <MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>
      </SessionProvider>
    </QueryClientProvider>,
  );
}
