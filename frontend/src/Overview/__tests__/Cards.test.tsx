import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { renderAt, serveApi } from '__tests__/render';
import type { Connection } from 'Connection/useConnection';
import ConnectionCard from 'Overview/ConnectionCard';
import RunsCard from 'Overview/RunsCard';
import type { Runs } from 'Runs/useRuns';

const connection: Connection = {
  state: 'Connected',
  since: '2026-11-13T09:00:00Z',
  lastOkAt: null,
  reason: null,
  reader: 'None',
};
const linkedState = {
  url: 'https://mnemarr.homelab.example',
  machine: 'gemini',
  linked: true,
  addressFromSettings: false,
  problem: null,
  lastHeartbeatAt: null,
  pendingLines: 0,
};
const idle: Runs = { runs: [], running: false };

describe('ConnectionCard', () => {
  it.each([
    ['Connected', 'Connected'],
    ['NeedsLogin', 'Sign in again'],
    ['Degraded', 'Degraded'],
    ['NotConnected', 'Not connected'],
  ] as const)('says the state %s in words', async (state, words) => {
    serveApi({
      'GET /connection': () => ({ ...connection, state }),
      'GET /mnemarr': () => linkedState,
      'GET /runs': () => idle,
    });
    renderAt(<ConnectionCard />);

    expect(await screen.findByText(words)).toBeInTheDocument();
  });

  it('says why the session needs a sign-in, and says Mnemarr is linked with its link to the page', async () => {
    serveApi({
      'GET /connection': () => ({ ...connection, state: 'NeedsLogin', reason: 'needs-login' }),
      'GET /mnemarr': () => linkedState,
      'GET /runs': () => idle,
    });
    renderAt(<ConnectionCard />);

    expect(await screen.findByText('The session has expired: sign in again.')).toBeInTheDocument();
    expect(await screen.findByText('Linked to mnemarr.homelab.example')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Mnemarr' })).toHaveAttribute('href', '/mnemarr');
  });

  it('says it is not linked', async () => {
    serveApi({
      'GET /connection': () => connection,
      'GET /mnemarr': () => ({ ...linkedState, linked: false }),
      'GET /runs': () => idle,
    });
    renderAt(<ConnectionCard />);

    expect(await screen.findByText('Not linked')).toBeInTheDocument();
  });

  it('says a demonstration reader is on', async () => {
    serveApi({
      'GET /connection': () => ({ ...connection, reader: 'Demo' }),
      'GET /mnemarr': () => linkedState,
      'GET /runs': () => idle,
    });
    renderAt(<ConnectionCard />);

    expect(await screen.findByText(/A demonstration reader is on/)).toBeInTheDocument();
  });

  it('runs a pass now', async () => {
    const calls = serveApi({
      'GET /connection': () => connection,
      'GET /mnemarr': () => linkedState,
      'GET /runs': () => idle,
      'POST /runs': () => new Response(null, { status: 202 }),
    });
    renderAt(<ConnectionCard />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Run now' }));

    await waitFor(() => expect(calls.some((call) => call.key === 'POST /runs')).toBe(true));
  });

  it('disables the button while a pass is running', async () => {
    serveApi({
      'GET /connection': () => connection,
      'GET /mnemarr': () => linkedState,
      'GET /runs': () => ({ runs: [], running: true }),
    });
    renderAt(<ConnectionCard />);

    await waitFor(() => expect(screen.getByRole('button', { name: 'Run now' })).toBeDisabled());
  });

  it('says it could not read the connection', async () => {
    serveApi({});
    renderAt(<ConnectionCard />);

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('shows the answer when asking for a pass is refused', async () => {
    serveApi({
      'GET /connection': () => connection,
      'GET /mnemarr': () => linkedState,
      'GET /runs': () => idle,
      'POST /runs': () => Response.json({ title: 'A pass is running.' }, { status: 409 }),
    });
    renderAt(<ConnectionCard />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Run now' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('A pass is running.');
  });
});

describe('RunsCard', () => {
  it('says no pass has run yet', async () => {
    serveApi({ 'GET /runs': () => idle });
    renderAt(<RunsCard />);

    expect(await screen.findByText('No pass has run yet.')).toBeInTheDocument();
  });

  it('lists the passes with how they ended and what they did, as counts', async () => {
    const run = {
      id: 1,
      kind: 'pass',
      startedAt: '2026-11-13T09:00:00Z',
      endedAt: '2026-11-13T09:00:02Z',
      outcome: 'ok',
      reason: null,
      listed: 3,
      opened: 2,
      turnsSent: 8,
      durationMs: 2000,
    } as const;
    serveApi({
      'GET /runs': () => ({
        runs: [
          run,
          { ...run, id: 2, outcome: 'partial' },
          { ...run, id: 3, outcome: 'failed' },
          { ...run, id: 4, outcome: 'skipped' },
          { ...run, id: 5, outcome: null, endedAt: null },
        ],
        running: false,
      }),
    });
    renderAt(<RunsCard />);

    expect(await screen.findAllByText('3 listed, 2 opened, 8 turns sent')).toHaveLength(5);
    expect(screen.getByText('Done')).toBeInTheDocument();
    expect(screen.getByText('Partly')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
    expect(screen.getByText('Skipped')).toBeInTheDocument();
    expect(screen.getByText('…')).toBeInTheDocument();
  });

  it('says it could not read the passes', async () => {
    serveApi({});
    renderAt(<RunsCard />);

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});
