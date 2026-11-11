import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';

import type { SystemStatus } from 'System/Status/useSystemStatus';

export const status: SystemStatus = {
  version: '0.0.0-test',
  runtimeVersion: '.NET 10.0.0',
  startedAt: '2026-11-11T08:00:00+00:00',
  uptimeSeconds: 3725,
};

/** Answers the calls of the application like the API would, from a table of paths. A path that is not in it is a 404. */
export function stubApi(routes: Readonly<Record<string, unknown>>) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const path = new URL(String(input), 'http://localhost').pathname.replace(/^\/api\/v1/, '');
      return path in routes
        ? Promise.resolve(Response.json(routes[path]))
        : Promise.resolve(new Response(null, { status: 404 }));
    }),
  );
}

/** Renders a screen with a query client that does not retry and a router at the given address. */
export function renderAt(ui: ReactElement, path = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}
