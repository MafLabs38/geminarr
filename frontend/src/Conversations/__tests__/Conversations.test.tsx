import { screen, within } from '@testing-library/react';

import { renderAt, serveApi } from '__tests__/render';
import Conversations from 'Conversations/Conversations';
import type { Backfill, Conversation, Conversations as ConversationsResource } from 'Conversations/useConversations';

const conversation = (id: string, over: Partial<Conversation> = {}): Conversation => ({
  id,
  source: 'web',
  state: 'synced',
  turnsKnown: 4,
  turnsSent: 4,
  rewrittenTurns: 0,
  listRank: 0,
  firstSeenAt: '2026-11-19T09:00:00Z',
  lastSeenAt: '2026-11-19T10:00:00Z',
  lastOpenedAt: '2026-11-19T10:00:00Z',
  lastError: null,
  ...over,
});

const backfill = (over: Partial<Backfill> = {}): Backfill => ({
  enabled: true,
  perPass: 10,
  listed: 40,
  known: 12,
  listComplete: true,
  done: false,
  ...over,
});

function serve(resource: Partial<ConversationsResource>) {
  serveApi({
    'GET /conversations': () => ({ total: 0, conversations: [], backfill: backfill(), ...resource }),
  });
  renderAt(<Conversations />);
}

describe('the conversations page', () => {
  it('lists each conversation with its state and its counts, and never a title', async () => {
    serve({
      total: 2,
      conversations: [
        conversation('gemini-c1a2b3', { turnsSent: 2, turnsKnown: 6, state: 'new' }),
        conversation('gemini-d4e5f6', { rewrittenTurns: 1, state: 'error' }),
      ],
    });

    const rows = within(await screen.findByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(within(rows[1] as HTMLElement).getByText('gemini-c1a2b3')).toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText('Waiting to be sent')).toBeInTheDocument();
    expect(within(rows[1] as HTMLElement).getByText('2 / 6')).toBeInTheDocument();
    expect(within(rows[2] as HTMLElement).getByText('Error')).toBeInTheDocument();
    expect(screen.getByText('2 of 2 conversations, the latest first.')).toBeInTheDocument();
  });

  it('says that a conversation is gone and that one that was never opened has no date', async () => {
    serve({ total: 1, conversations: [conversation('gone-1', { state: 'gone', lastOpenedAt: null })] });

    const row = within(await screen.findByRole('table')).getAllByRole('row')[1] as HTMLElement;
    expect(within(row).getByText('Gone')).toBeInTheDocument();
    expect(within(row).getByText('—')).toBeInTheDocument();
  });

  it('says why a conversation is in error when it could not be read to its beginning', async () => {
    serve({ total: 1, conversations: [conversation('long-1', { state: 'error', lastError: 'unfinished-read' })] });

    expect(await screen.findByText(/Could not be read to its beginning/u)).toBeInTheDocument();
  });

  it('says that nothing was read yet', async () => {
    serve({ backfill: backfill({ listed: 0, known: 0 }) });

    expect(await screen.findByText('No conversation has been read yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByText('The list of the assistant has not been read yet.')).toBeInTheDocument();
  });

  it('shows how far the history is walked, and at what pace', async () => {
    serve({ total: 12, conversations: [conversation('c1')] });

    expect(await screen.findByText('12 of 40 conversations of the list are read.')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'The history' })).toHaveAttribute('value', '12');
    expect(screen.getByText(/At most 10 older conversations are read in a pass/u)).toBeInTheDocument();
  });

  it('says that the whole history is read, and then no longer talks of a pace', async () => {
    serve({ backfill: backfill({ known: 40, done: true }) });

    expect(await screen.findByText(/The whole history that the list holds is read/u)).toBeInTheDocument();
    expect(screen.queryByText(/older conversations are read in a pass/u)).not.toBeInTheDocument();
  });

  it('says that the list goes on past what one round reads when all that was listed is read', async () => {
    serve({ backfill: backfill({ known: 40, listComplete: false }) });

    expect(await screen.findByText(/goes on past what one round reads/u)).toBeInTheDocument();
  });

  it('says that the history is switched off', async () => {
    serve({ backfill: backfill({ enabled: false }) });

    expect(await screen.findByText(/Reading the history is switched off/u)).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('says it when the conversations cannot be read', async () => {
    serveApi({});
    renderAt(<Conversations />);

    expect(await screen.findByText(/Could not read the conversations/u)).toBeInTheDocument();
  });
});
