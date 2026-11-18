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
  hasDesktop: true,
};

describe('Connection', () => {
  beforeEach(() => {
    Object.defineProperty(globalThis, 'isSecureContext', { configurable: true, value: true });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

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
    expect(screen.queryByRole('button', { name: 'Sign in to the assistant' })).not.toBeInTheDocument();
  });

  it('offers one way in to sign in by hand, the Sign in button, and no other link to the browser', async () => {
    serveApi({
      'GET /connection': () => ({ ...base, state: 'NeedsLogin', reason: 'needs-login' }),
      'GET /runs': () => ({ runs: [], running: false }),
    });
    renderAt(<Connection />);

    expect(await screen.findByRole('button', { name: 'Sign in to the assistant' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /browser/i })).not.toBeInTheDocument();
    expect(screen.getByText(/Use the dedicated account, never your main one/)).toBeInTheDocument();
  });

  it('says that nobody is signed in, and that the account is not read when only the session is known', async () => {
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

  it('opens the browser desktop when the person presses Sign in, and closes that window when the connection says it is done', async () => {
    let signedIn = false;
    const close = vi.fn();
    const opened = { closed: false, close, location: { href: 'about:blank' }, opener: globalThis } as unknown as Window;
    const open = vi.spyOn(globalThis, 'open').mockReturnValue(opened);
    serveApi({
      'GET /connection': () => (signedIn ? { ...base, state: 'Connected' } : base),
      'GET /runs': () => ({ runs: [], running: false }),
      'POST /connection/desktop': () => ({ url: 'https://localhost:8283/enter?ticket=a-ticket' }),
    });
    renderAt(<Connection />);

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in to the assistant' }));

    // The window is opened by the click, empty, and goes to the desktop's own origin with the ticket once the server has given it.
    expect(open).toHaveBeenCalledWith('about:blank', '_blank');
    await waitFor(() => expect(opened.location.href).toBe('https://localhost:8283/enter?ticket=a-ticket'));
    expect(opened.opener, 'the window keeps its link to this page, so that this page can close it').toBe(globalThis);
    expect(close).not.toHaveBeenCalled();

    signedIn = true;
    await userEvent.click(await screen.findByRole('button', { name: 'Check again' }));

    expect(
      await screen.findByText('Signed in (the account is not read yet)', {}, { timeout: 5000 }),
    ).toBeInTheDocument();
    await waitFor(() => expect(close).toHaveBeenCalledTimes(1));
  }, 15_000);

  it('does not leave the first window behind on a second click, and opens a new one each time', async () => {
    const first = {
      closed: false,
      close: vi.fn(),
      location: { href: 'about:blank' },
      opener: globalThis,
    } as unknown as Window;
    const second = {
      closed: false,
      close: vi.fn(),
      location: { href: 'about:blank' },
      opener: globalThis,
    } as unknown as Window;
    const open = vi.spyOn(globalThis, 'open').mockReturnValueOnce(first).mockReturnValueOnce(second);
    serveApi({
      'GET /connection': () => base,
      'GET /runs': () => ({ runs: [], running: false }),
      'POST /connection/desktop': () => ({ url: 'https://localhost:8283/enter?ticket=a-ticket' }),
    });
    renderAt(<Connection />);

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in to the assistant' }));
    await waitFor(() => expect(first.location.href).toBe('https://localhost:8283/enter?ticket=a-ticket'));
    await userEvent.click(await screen.findByRole('button', { name: 'Sign in to the assistant' }));

    expect(first.close).toHaveBeenCalledTimes(1);
    expect(open).toHaveBeenNthCalledWith(2, 'about:blank', '_blank');
  });

  it('closes the window and says why when the address of the desktop could not be given', async () => {
    const close = vi.fn();
    vi.spyOn(globalThis, 'open').mockReturnValue({ closed: false, close, location: { href: '' } } as unknown as Window);
    serveApi({
      'GET /connection': () => base,
      'GET /runs': () => ({ runs: [], running: false }),
      'POST /connection/desktop': () =>
        Response.json({ title: 'The session is not one that is open any more: sign in again.' }, { status: 400 }),
    });
    renderAt(<Connection />);

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in to the assistant' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('sign in again');
    await waitFor(() => expect(close).toHaveBeenCalledTimes(1));
  });

  it('says that the desktop needs HTTPS, and points to the https address, instead of opening a window that shows an error', async () => {
    Object.defineProperty(globalThis, 'isSecureContext', { configurable: true, value: false });
    serveApi({ 'GET /connection': () => base, 'GET /runs': () => ({ runs: [], running: false }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('alert')).toHaveTextContent('works over HTTPS only');
    expect(screen.getByRole('link', { name: 'Open this page over HTTPS' })).toHaveAttribute(
      'href',
      expect.stringMatching(/^https:\/\//),
    );
    expect(screen.queryByRole('button', { name: 'Sign in to the assistant' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check again' })).toBeInTheDocument();
  });

  it('says when no desktop is given to relay', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, hasDesktop: false }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('alert')).toHaveTextContent('GEMINARR__BROWSER__DESKTOPURL');
  });

  it('does not ask to sign in when the session is good', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'Connected' }) });
    renderAt(<Connection />);

    await screen.findByText('Connected', { selector: 'span' });
    expect(screen.queryByRole('button', { name: 'Sign in to the assistant' })).not.toBeInTheDocument();
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
    expect(screen.queryByRole('button', { name: 'Sign in to the assistant' })).not.toBeInTheDocument();
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
