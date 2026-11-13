import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import Login from 'Login/Login';

function renderLogin() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <Login />
    </QueryClientProvider>,
  );
  return client;
}

async function fillAndSubmit() {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText('Name'), 'operator-name');
  await user.type(screen.getByLabelText('Password'), 'a long enough password');
  await user.click(screen.getByRole('button', { name: 'Sign in' }));
}

describe('Login', () => {
  it('sends the name and the password with the header of a write, and keeps the session it is given', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ name: 'operator-name' }));
    vi.stubGlobal('fetch', fetchMock);
    const client = renderLogin();

    await fillAndSubmit();

    await waitFor(() => expect(client.getQueryData(['api', '/auth/session'])).toEqual({ name: 'operator-name' }));
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/auth/login',
      expect.objectContaining({
        method: 'POST',
        headers: { 'X-Geminarr-Request': '1', 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: 'operator-name', password: 'a long enough password' }),
      }),
    );
  });

  it('says the same thing for a wrong name and a wrong password, and does not say which', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(Response.json({ title: 'The name or the password is wrong.' }, { status: 401 })),
    );
    renderLogin();

    await fillAndSubmit();

    expect(await screen.findByRole('alert')).toHaveTextContent('The name or the password is wrong.');
  });

  it('asks to wait when the name is locked', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 429 })));
    renderLogin();

    await fillAndSubmit();

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many failed attempts');
  });

  it('says it could not sign in when the server did not answer as expected', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 502 })));
    renderLogin();

    await fillAndSubmit();

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not sign in');
  });

  it('asks for both fields and hides the password', () => {
    renderLogin();

    expect(screen.getByLabelText('Name')).toBeRequired();
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
  });
});
