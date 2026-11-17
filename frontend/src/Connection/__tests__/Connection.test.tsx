import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { renderAt, serveApi } from '__tests__/render';
import Connection, { PAUSE_HOURS } from 'Connection/Connection';
import type { Connection as ConnectionResource } from 'Connection/useConnection';

const base: ConnectionResource = {
  state: 'NotConnected',
  since: '2026-11-13T09:00:00Z',
  lastOkAt: null,
  reason: 'not-connected',
  reader: 'Web',
  hasBrowser: true,
  pausedUntil: null,
  browserPagePath: '/browser/',
  account: null,
  signingIn: false,
};

describe('Connection', () => {
  it.each([
    ['NotConnected', 'Not connected'],
    ['Connected', 'Connected'],
    ['NeedsLogin', 'Sign in again'],
    ['Degraded', 'Degraded'],
    ['Paused', 'Paused'],
  ] as const)('says the state %s in words', async (state, words) => {
    serveApi({ 'GET /connection': () => ({ ...base, state }) });
    renderAt(<Connection />);

    expect(await screen.findByText(words, { selector: 'span' })).toBeInTheDocument();
  });

  it('says why the session needs a sign-in', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'NeedsLogin', reason: 'needs-login' }) });
    renderAt(<Connection />);

    expect(await screen.findByText('The session has expired: sign in again.')).toBeInTheDocument();
  });

  it('says that no browser reads, and shows nothing to sign in to', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, hasBrowser: false, reader: 'None' }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('alert')).toHaveTextContent('No browser reads this Geminarr');
    expect(screen.queryByRole('link', { name: 'Open the browser' })).not.toBeInTheDocument();
  });

  it('sends the person to the page of the browser, in a tab of its own, to sign in by hand', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'NeedsLogin', reason: 'needs-login' }) });
    renderAt(<Connection />);

    const link = await screen.findByRole('link', { name: 'Open the browser' });

    expect(link).toHaveAttribute('href', '/browser/');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'));
    expect(screen.getByText(/Use the dedicated account, never your main one/)).toBeInTheDocument();
  });

  it('says who the browser is signed in as, by name and by address', async () => {
    serveApi({
      'GET /connection': () => ({
        ...base,
        state: 'Connected',
        account: { name: 'Jane Doe', email: 'jane@example.org' },
      }),
    });
    renderAt(<Connection />);

    expect(await screen.findByText('Signed in as Jane Doe')).toBeInTheDocument();
    expect(screen.getByText('jane@example.org')).toBeInTheDocument();
  });

  it('says the address when the page gave no name', async () => {
    serveApi({
      'GET /connection': () => ({ ...base, state: 'Connected', account: { name: null, email: 'jane@example.org' } }),
    });
    renderAt(<Connection />);

    expect(await screen.findByText('Signed in as jane@example.org')).toBeInTheDocument();
  });

  it('says that nobody is signed in, and that the account is not read yet when only the session is known', async () => {
    serveApi({ 'GET /connection': () => base });
    const view = renderAt(<Connection />);
    expect(await screen.findByText('Not signed in')).toBeInTheDocument();
    view.unmount();

    serveApi({ 'GET /connection': () => ({ ...base, state: 'Connected' }) });
    renderAt(<Connection />);
    expect(await screen.findByText('Signed in (the account is not read yet)')).toBeInTheDocument();
  });

  it('says that the session has ended when a sign-in is asked for again', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'NeedsLogin', reason: 'needs-login' }) });
    renderAt(<Connection />);

    expect(await screen.findByText('The session has ended: sign in again')).toBeInTheDocument();
  });

  it('lets the person ask for a check at once instead of waiting for the next pass', async () => {
    let asked = 0;
    serveApi({
      'GET /connection': () => base,
      'GET /runs': () => ({ runs: [], running: false }),
      'POST /runs': () => {
        asked += 1;
        return new Response(null, { status: 202 });
      },
    });
    renderAt(<Connection />);

    await userEvent.click(await screen.findByRole('button', { name: 'Check again' }));

    await waitFor(() => expect(asked).toBe(1));
  });

  it('opens the browser and its sign-in page when the person presses Sign in, and closes that window when the sign-in is done', async () => {
    let signedIn = false;
    let posted = 0;
    const close = vi.fn();
    const opened = { closed: false, close } as unknown as Window;
    const open = vi.spyOn(globalThis, 'open').mockReturnValue(opened);
    serveApi({
      'GET /connection': () =>
        signedIn
          ? { ...base, state: 'Connected', account: { name: 'Jane Doe', email: 'jane@example.org' } }
          : { ...base, signingIn: posted > 0 },
      'GET /runs': () => ({ runs: [], running: false }),
      'POST /connection/sign-in': () => {
        posted += 1;
        return { ...base, signingIn: true };
      },
    });
    renderAt(<Connection />);

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in to the assistant' }));

    expect(open).toHaveBeenCalledWith('/browser/', 'geminarr-browser');
    expect(await screen.findByText('Signing in…')).toBeInTheDocument();
    expect(close).not.toHaveBeenCalled();

    signedIn = true;
    await userEvent.click(await screen.findByRole('button', { name: 'Check again' }));

    expect(await screen.findByText('Signed in as Jane Doe', {}, { timeout: 5000 })).toBeInTheDocument();
    await waitFor(() => expect(close).toHaveBeenCalledTimes(1));
    open.mockRestore();
  });

  it('says why the sign-in page could not be opened', async () => {
    const open = vi.spyOn(globalThis, 'open').mockReturnValue(null);
    serveApi({
      'GET /connection': () => base,
      'GET /runs': () => ({ runs: [], running: false }),
      'POST /connection/sign-in': () =>
        Response.json({ title: 'The browser container could not be reached.' }, { status: 502 }),
    });
    renderAt(<Connection />);

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in to the assistant' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('could not be reached');
    open.mockRestore();
  });

  it('says when no desktop is given to relay', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, browserPagePath: null }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('alert')).toHaveTextContent('GEMINARR__BROWSER__DESKTOPURL');
  });

  it('does not ask to sign in when the session is good', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'Connected' }) });
    renderAt(<Connection />);

    await screen.findByText('Connected', { selector: 'span' });
    expect(screen.queryByRole('link', { name: 'Open the browser' })).not.toBeInTheDocument();
  });

  it('shows the pause and the way to resume while the passes are paused', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'Paused', pausedUntil: '2026-11-13T15:00:00Z' }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('button', { name: 'Resume' })).toBeInTheDocument();
    expect(screen.getByText('Paused until')).toBeInTheDocument();
  });

  it('pauses for some hours or until resumed', async () => {
    const calls = serveApi({
      'GET /connection': () => base,
      'POST /connection/pause': () => base,
      'POST /connection/resume': () => base,
    });
    renderAt(<Connection />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: `Pause for ${PAUSE_HOURS} hours` }));
    await user.click(screen.getByRole('button', { name: 'Pause until I resume' }));

    await waitFor(() =>
      expect(calls.filter((call) => call.key === 'POST /connection/pause').map((call) => call.body)).toEqual([
        { hours: PAUSE_HOURS },
        { hours: null },
      ]),
    );
  });

  it('resumes a paused connection', async () => {
    const calls = serveApi({
      'GET /connection': () => ({ ...base, state: 'Paused', pausedUntil: '2026-11-13T15:00:00Z' }),
      'POST /connection/resume': () => base,
    });
    renderAt(<Connection />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Resume' }));

    await waitFor(() => expect(calls.some((call) => call.key === 'POST /connection/resume')).toBe(true));
  });

  it('shows no sign-in instructions while the passes are paused', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'Paused', pausedUntil: '2026-11-13T15:00:00Z' }) });
    renderAt(<Connection />);

    await screen.findByRole('button', { name: 'Resume' });
    expect(screen.queryByRole('link', { name: 'Open the browser' })).not.toBeInTheDocument();
  });

  it('asks twice before it disconnects, and can be backed out of', async () => {
    const calls = serveApi({
      'GET /connection': () => ({ ...base, state: 'Connected' }),
      'POST /connection/disconnect': () => base,
    });
    renderAt(<Connection />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Disconnect' }));
    expect(screen.getByText(/Really disconnect\?/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(calls.some((call) => call.key === 'POST /connection/disconnect')).toBe(false);

    await user.click(screen.getByRole('button', { name: 'Disconnect' }));
    await user.click(screen.getByRole('button', { name: 'Yes, disconnect' }));

    await waitFor(() => expect(calls.some((call) => call.key === 'POST /connection/disconnect')).toBe(true));
  });

  it('reads the state again after a disconnect that did not reach the browser, which forgot the connection all the same', async () => {
    let connected = true;
    const calls = serveApi({
      'GET /connection': () => ({ ...base, state: connected ? 'Connected' : 'NotConnected' }),
      'POST /connection/disconnect': () => {
        connected = false;
        return Response.json({ title: 'The browser could not be reached.' }, { status: 502 });
      },
    });
    renderAt(<Connection />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Disconnect' }));
    await user.click(screen.getByRole('button', { name: 'Yes, disconnect' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The browser could not be reached.');
    await waitFor(() => expect(calls.filter((call) => call.key === 'GET /connection').length).toBeGreaterThan(1));
    expect(await screen.findByText('Not connected', { selector: 'span' })).toBeInTheDocument();
  });

  it('says why a pause was refused', async () => {
    serveApi({
      'GET /connection': () => base,
      'POST /connection/pause': () => Response.json({ title: 'A pause lasts more than nothing.' }, { status: 400 }),
    });
    renderAt(<Connection />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Pause until I resume' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('A pause lasts more than nothing.');
  });

  it('says it could not read the connection', async () => {
    serveApi({});
    renderAt(<Connection />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not read the connection');
  });
});
