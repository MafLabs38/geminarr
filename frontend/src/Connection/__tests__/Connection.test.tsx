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
  liveViewMinutes: 10,
};

const ticket = {
  id: 'abc',
  token: 't',
  path: '/api/v1/connection/live-views/abc',
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  width: 1280,
  height: 800,
};

class QuietSocket {
  static readonly OPEN = 1;
  readyState = 1;
  onmessage = null;
  onclose = null;
  send() {}
  close() {}
}

describe('Connection', () => {
  beforeEach(() => {
    vi.stubGlobal('WebSocket', QuietSocket);
    HTMLCanvasElement.prototype.getContext = vi.fn(() => null) as never;
  });

  it.each([
    ['NotConnected', 'Not connected'],
    ['Connected', 'Connected'],
    ['NeedsLogin', 'Sign in again'],
    ['Degraded', 'Degraded'],
    ['Connecting', 'Signing in'],
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

  it('says that no browser reads, and offers no sign-in', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, hasBrowser: false, reader: 'None' }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('alert')).toHaveTextContent('No browser reads this Geminarr');
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument();
  });

  it('opens the live view when the operator signs in, and closes it by the server when they close it', async () => {
    const calls = serveApi({
      'GET /connection': () => base,
      'POST /connection/live-views': () => ticket,
      'DELETE /connection/live-views': () => new Response(null, { status: 204 }),
    });
    renderAt(<Connection />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('You are controlling the browser of the Geminarr container.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(calls.some((call) => call.key === 'DELETE /connection/live-views')).toBe(true));
    expect(screen.queryByText('You are controlling the browser of the Geminarr container.')).not.toBeInTheDocument();
  });

  it('says why the live view could not be opened', async () => {
    serveApi({
      'GET /connection': () => base,
      'POST /connection/live-views': () => Response.json({ title: 'A live view is already open.' }, { status: 409 }),
    });
    renderAt(<Connection />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('A live view is already open.');
  });

  it('does not offer to sign in while the passes are paused or a person is signing in', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, state: 'Paused', pausedUntil: '2026-11-13T15:00:00Z' }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Resume' })).toBeInTheDocument();
    expect(screen.getByText('Paused until')).toBeInTheDocument();
  });

  it('pauses for some hours or until resumed, and resumes', async () => {
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
