import { QueryClient } from '@tanstack/react-query';
import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';

import { createQueryClient } from 'App/queryClient';
import Page from 'Components/Page/Page';
import { ApiError } from 'Helpers/Api/apiGet';
import { endSession, fetchSession, forgetSessionData, sessionKey } from 'Session/session';
import SessionGate from 'Session/SessionGate';

describe('fetchSession', () => {
  it('gives the session the server says', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json({ name: 'operator-name' })));

    await expect(fetchSession()).resolves.toEqual({ name: 'operator-name' });
  });

  it('gives nobody when the server says 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));

    await expect(fetchSession()).resolves.toBeNull();
  });

  it('fails when the server fails for any other reason', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })));

    await expect(fetchSession()).rejects.toBeInstanceOf(ApiError);
  });
});

describe('forgetting what a session read', () => {
  it('forgets every query but the session, and every mutation, when somebody else signs in', () => {
    const client = new QueryClient();
    client.setQueryData(sessionKey, { name: 'operator-name' });
    client.setQueryData(['api', '/health'], { status: 'Healthy' });

    forgetSessionData(client);

    expect(client.getQueryData(sessionKey)).toEqual({ name: 'operator-name' });
    expect(client.getQueryData(['api', '/health'])).toBeUndefined();
  });

  it('forgets a query whose key does not have the shape of the others', () => {
    const client = new QueryClient();
    client.setQueryData(sessionKey, { name: 'operator-name' });
    client.setQueryData(['somewhere-else'], { read: true });
    client.setQueryData([], { read: true });

    forgetSessionData(client);

    expect(client.getQueryData(['somewhere-else'])).toBeUndefined();
    expect(client.getQueryData([])).toBeUndefined();
    expect(client.getQueryData(sessionKey)).toEqual({ name: 'operator-name' });
  });

  it('puts the session to nobody first and forgets the rest', () => {
    const client = new QueryClient();
    client.setQueryData(sessionKey, { name: 'operator-name' });
    client.setQueryData(['api', '/settings'], { settings: [] });

    endSession(client);

    expect(client.getQueryData(sessionKey)).toBeNull();
    expect(client.getQueryData(['api', '/settings'])).toBeUndefined();
  });
});

function renderApp(client: QueryClient) {
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <SessionGate>
          <Page>
            <p>the application</p>
          </Page>
        </SessionGate>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('SessionGate', () => {
  it('shows nothing of the application while it does not know, and the sign-in page when nobody is signed in', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })));

    renderApp(createQueryClient());

    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByText('the application')).not.toBeInTheDocument();
  });

  it('says it could not find out when the server fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })));

    // The client of the application retries once, after a second: this one does not, so that the test does not wait.
    renderApp(new QueryClient({ defaultOptions: { queries: { retry: false } } }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not find out whether you are signed in.');
  });

  it('goes back to the sign-in page when the session ends, and signs out through the server', async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const path = String(input).replace('/api/v1', '');
        calls.push(`${init?.method ?? 'GET'} ${path}`);
        if (path === '/auth/session') {
          return Promise.resolve(Response.json({ name: 'operator-name' }));
        }
        if (path === '/auth/logout') {
          return Promise.resolve(new Response(null, { status: 204 }));
        }
        return Promise.resolve(new Response(null, { status: 404 }));
      }),
    );
    renderApp(createQueryClient());
    expect(await screen.findByText('the application')).toBeInTheDocument();

    await userEvent.setup().click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument());
    expect(calls).toContain('POST /auth/logout');
    expect(screen.queryByText('the application')).not.toBeInTheDocument();
  });

  it('stays signed in and says so when the server could not end the session', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) =>
        Promise.resolve(
          String(input).endsWith('/auth/session')
            ? Response.json({ name: 'operator-name' })
            : new Response(null, { status: 500 }),
        ),
      ),
    );
    renderApp(createQueryClient());
    await screen.findByText('the application');

    await userEvent.setup().click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not sign out');
    expect(screen.getByText('the application')).toBeInTheDocument();
  });

  it('puts the sign-in page back when the sign-out itself is answered 401', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) =>
        Promise.resolve(
          String(input).endsWith('/auth/session')
            ? Response.json({ name: 'operator-name' })
            : new Response(null, { status: 401 }),
        ),
      ),
    );
    renderApp(createQueryClient());
    await screen.findByText('the application');

    await userEvent.setup().click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('puts the sign-in page back when any call is answered 401', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) =>
        Promise.resolve(
          String(input).endsWith('/auth/session')
            ? Response.json({ name: 'operator-name' })
            : new Response(null, { status: 401 }),
        ),
      ),
    );
    const client = createQueryClient();
    renderApp(client);
    await screen.findByText('the application');

    await expect(
      client.fetchQuery({ queryKey: ['api', '/health'], queryFn: () => Promise.reject(new ApiError(401)) }),
    ).rejects.toBeInstanceOf(ApiError);

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });
});
