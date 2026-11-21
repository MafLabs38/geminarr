import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { renderAt, serveApi } from '__tests__/render';
import Runs from 'Runs/Runs';
import type { Run } from 'Runs/useRuns';

const run = (id: number, over: Partial<Run> = {}): Run => ({
  id,
  kind: 'pass',
  startedAt: '2026-11-19T10:00:00Z',
  endedAt: '2026-11-19T10:00:03Z',
  outcome: 'ok',
  reason: null,
  listed: 40,
  opened: 7,
  turnsSent: 31,
  durationMs: 3200,
  ...over,
});

describe('the runs page', () => {
  it('lists each pass with its outcome, its counts, how long it took and why it ended as it did', async () => {
    serveApi({
      'GET /runs': () => ({
        running: false,
        runs: [
          run(2, { outcome: 'skipped', reason: 'needs-login', listed: 0, opened: 0, turnsSent: 0, durationMs: 40 }),
          run(1),
        ],
      }),
    });
    renderAt(<Runs />);

    const rows = within(await screen.findByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(within(rows[1] as HTMLElement).getByText('Skipped')).toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText('The session has expired: sign in again.')).toBeInTheDocument();
    expect(within(rows[2] as HTMLElement).getByText('Done')).toBeInTheDocument();
    expect(within(rows[2] as HTMLElement).getByText('40 listed, 7 opened, 31 turns sent')).toBeInTheDocument();
    expect(within(rows[2] as HTMLElement).getByText('3.2 s')).toBeInTheDocument();
  });

  it('writes the code of a reason that only a pass says, and shows a pass that has not ended', async () => {
    serveApi({
      'GET /runs': () => ({
        running: true,
        runs: [run(2, { outcome: null, endedAt: null }), run(1, { outcome: 'partial', reason: 'pass-timeout' })],
      }),
    });
    renderAt(<Runs />);

    expect(await screen.findByText('pass-timeout')).toBeInTheDocument();
    expect(screen.getByText('…')).toBeInTheDocument();
    expect(screen.getByText('A pass is running now.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Run now' })).toBeDisabled();
  });

  it('names the machine of the account whose passes they are', async () => {
    serveApi({ 'GET /runs': () => ({ running: false, runs: [run(1)] }) });
    renderAt(<Runs />);

    expect(await screen.findByText('Machine: geminarr-demo')).toBeInTheDocument();
  });

  it('says that no pass has run yet', async () => {
    serveApi({ 'GET /runs': () => ({ running: false, runs: [] }) });
    renderAt(<Runs />);

    expect(await screen.findByText('No pass has run yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('asks for a pass now, and reads the passes again', async () => {
    const asked = vi.fn();
    const reads = vi.fn();
    serveApi({
      'GET /runs': () => {
        reads();
        return { running: false, runs: [run(1)] };
      },
      'POST /runs': () => {
        asked();
        return {};
      },
    });
    renderAt(<Runs />);

    await userEvent.click(await screen.findByRole('button', { name: 'Run now' }));

    await waitFor(() => expect(asked).toHaveBeenCalledOnce());
    // The list is read again after the order: once to show the page, once more for the order.
    await waitFor(() => expect(reads.mock.calls.length).toBeGreaterThanOrEqual(2));
  });

  it('says it when the passes cannot be read', async () => {
    serveApi({});
    renderAt(<Runs />);

    expect(await screen.findByText(/Could not read the passes/u)).toBeInTheDocument();
  });
});
