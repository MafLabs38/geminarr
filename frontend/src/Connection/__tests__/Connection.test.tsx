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
  browserPageUrl: 'https://browser.homelab.example/',
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

    expect(link).toHaveAttribute('href', 'https://browser.homelab.example/');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'));
    expect(screen.getByText(/Use the dedicated account, never your main one/)).toBeInTheDocument();
  });

  it('says when the address of the page of the browser is not given', async () => {
    serveApi({ 'GET /connection': () => ({ ...base, browserPageUrl: null }) });
    renderAt(<Connection />);

    expect(await screen.findByRole('alert')).toHaveTextContent('GEMINARR__BROWSER__PAGEURL');
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
