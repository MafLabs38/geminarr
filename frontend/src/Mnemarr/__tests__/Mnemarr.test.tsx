import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { renderAt, serveApi } from '__tests__/render';
import Mnemarr, { PAIRING_CHECK_MS } from 'Mnemarr/Mnemarr';
import type { MnemarrState, Pairing } from 'Mnemarr/useMnemarr';

const unlinked: MnemarrState = {
  url: null,
  machine: 'gemini',
  linked: false,
  addressFromSettings: false,
  problem: 'mnemarr-not-linked',
  lastHeartbeatAt: null,
  pendingLines: 0,
};

const linked: MnemarrState = {
  url: 'https://mnemarr.homelab.example',
  machine: 'gemini-2',
  linked: true,
  addressFromSettings: false,
  problem: null,
  lastHeartbeatAt: '2026-11-13T09:00:00Z',
  pendingLines: 12,
};

const none: Pairing = { state: 'none', userCode: null, verificationUrl: null, expiresAt: null };
const pending: Pairing = {
  state: 'pending',
  userCode: 'ABCD-EFGH',
  verificationUrl: 'https://mnemarr.homelab.example/link?code=ABCD-EFGH',
  expiresAt: '2026-11-13T09:10:00Z',
};

describe('Mnemarr', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('says nothing is linked and what is wrong, and asks for the address before it pairs', async () => {
    serveApi({ 'GET /mnemarr': () => unlinked, 'GET /mnemarr/pairing': () => none });
    renderAt(<Mnemarr />);

    expect(await screen.findByText('Not linked', { selector: 'span' })).toBeInTheDocument();
    expect(screen.getByText(/No Mnemarr is linked: nothing is sent/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pair with Mnemarr' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save the address' })).toBeDisabled();
  });

  it('keeps the address that is typed', async () => {
    const calls = serveApi({
      'GET /mnemarr': () => unlinked,
      'GET /mnemarr/pairing': () => none,
      'PUT /mnemarr': () => ({ ...unlinked, url: 'https://mnemarr.homelab.example' }),
    });
    renderAt(<Mnemarr />);
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText('Address of Mnemarr'), ' https://mnemarr.homelab.example ');
    await user.click(screen.getByRole('button', { name: 'Save the address' }));

    await waitFor(() =>
      expect(calls.find((call) => call.key === 'PUT /mnemarr')?.body).toEqual({
        url: 'https://mnemarr.homelab.example',
      }),
    );
  });

  it('says why an address was refused', async () => {
    const calls = serveApi({
      'GET /mnemarr': () => unlinked,
      'GET /mnemarr/pairing': () => none,
      'PUT /mnemarr': () => Response.json({ title: 'That is not the address of a Mnemarr.' }, { status: 400 }),
    });
    renderAt(<Mnemarr />);
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText('Address of Mnemarr'), 'https://mnemarr.homelab.example/path');
    await user.click(screen.getByRole('button', { name: 'Save the address' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('That is not the address of a Mnemarr.');
    expect(calls.some((call) => call.key === 'PUT /mnemarr')).toBe(true);
  });

  it('pairs: shows the code and the page to approve with its host, asks about it, and shows the link once it is approved', async () => {
    let approved = false;
    let state: MnemarrState = { ...unlinked, url: 'https://mnemarr.homelab.example' };
    const calls = serveApi({
      'GET /mnemarr': () => state,
      'GET /mnemarr/pairing': () => none,
      'POST /mnemarr/pairing': () => pending,
      'POST /mnemarr/pairing/check': () => {
        if (approved) {
          state = linked;
        }
        return approved ? { ...none, state: 'approved' } : pending;
      },
    });
    vi.useFakeTimers({ shouldAdvanceTime: true });
    renderAt(<Mnemarr />);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    await user.click(await screen.findByRole('button', { name: 'Pair with Mnemarr' }));

    expect(await screen.findByText('In Mnemarr, approve the code ABCD-EFGH.')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'Open mnemarr.homelab.example to approve' });
    expect(link).toHaveAttribute('href', pending.verificationUrl);
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');

    await vi.advanceTimersByTimeAsync(PAIRING_CHECK_MS + 100);
    await waitFor(() =>
      expect(calls.filter((call) => call.key === 'POST /mnemarr/pairing/check').length).toBeGreaterThan(0),
    );
    approved = true;
    await vi.advanceTimersByTimeAsync(PAIRING_CHECK_MS + 100);

    expect(await screen.findByText('Linked to mnemarr.homelab.example')).toBeInTheDocument();
    expect(screen.getByText('gemini-2')).toBeInTheDocument();
    expect(screen.queryByText(/approve the code/)).not.toBeInTheDocument();
  });

  it('gives a pairing up', async () => {
    const calls = serveApi({
      'GET /mnemarr': () => ({ ...unlinked, url: 'https://mnemarr.homelab.example' }),
      'GET /mnemarr/pairing': () => pending,
      'DELETE /mnemarr/pairing': () => new Response(null, { status: 204 }),
    });
    renderAt(<Mnemarr />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(calls.some((call) => call.key === 'DELETE /mnemarr/pairing')).toBe(true));
    expect(await screen.findByRole('button', { name: 'Pair with Mnemarr' })).toBeInTheDocument();
  });

  it.each([
    ['expired', 'The pairing expired: start it again.'],
    ['rejected', 'The pairing was refused in Mnemarr.'],
  ] as const)('says a pairing that is %s', async (state, sentence) => {
    serveApi({
      'GET /mnemarr': () => ({ ...unlinked, url: 'https://mnemarr.homelab.example' }),
      'GET /mnemarr/pairing': () => none,
      'POST /mnemarr/pairing': () => ({ ...none, state }),
    });
    renderAt(<Mnemarr />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Pair with Mnemarr' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(sentence);
  });

  it('says why a pairing could not start', async () => {
    serveApi({
      'GET /mnemarr': () => ({ ...unlinked, url: 'https://mnemarr.homelab.example' }),
      'GET /mnemarr/pairing': () => none,
      'POST /mnemarr/pairing': () => Response.json({ title: 'Mnemarr did not answer.' }, { status: 502 }),
    });
    renderAt(<Mnemarr />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Pair with Mnemarr' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The pairing did not work: Mnemarr did not answer.');
  });

  it('shows where it sends, as which machine, the last sign of life and what waits, and offers to unlink', async () => {
    serveApi({ 'GET /mnemarr': () => linked });
    renderAt(<Mnemarr />);

    expect(await screen.findByText('Linked to mnemarr.homelab.example')).toBeInTheDocument();
    expect(screen.getByText('https://mnemarr.homelab.example')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unlink' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Address of Mnemarr')).not.toBeInTheDocument();
  });

  it('says what is wrong with a destination that is linked', async () => {
    serveApi({ 'GET /mnemarr': () => ({ ...linked, problem: 'mnemarr-refused' }) });
    renderAt(<Mnemarr />);

    expect(await screen.findByText(/Mnemarr refused the key of this machine/)).toBeInTheDocument();
  });

  it('says a code it does not know as it is', async () => {
    serveApi({ 'GET /mnemarr': () => ({ ...linked, problem: 'something-new' }) });
    renderAt(<Mnemarr />);

    expect(await screen.findByText('something-new')).toBeInTheDocument();
  });

  it('asks twice before it unlinks, and can be backed out of', async () => {
    const calls = serveApi({
      'GET /mnemarr': () => linked,
      'DELETE /mnemarr': () => new Response(null, { status: 204 }),
    });
    renderAt(<Mnemarr />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Unlink' }));
    expect(screen.getByText(/Really unlink\?/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.getByRole('button', { name: 'Unlink' })).toBeInTheDocument();
    expect(calls.some((call) => call.key === 'DELETE /mnemarr')).toBe(false);

    await user.click(screen.getByRole('button', { name: 'Unlink' }));
    await user.click(screen.getByRole('button', { name: 'Yes, unlink' }));

    await waitFor(() => expect(calls.some((call) => call.key === 'DELETE /mnemarr')).toBe(true));
  });

  it('says why it could not unlink, and stays where it is', async () => {
    serveApi({
      'GET /mnemarr': () => linked,
      'DELETE /mnemarr': () =>
        Response.json({ title: 'The key is a file of the deployment: take it out of the manifest.' }, { status: 409 }),
    });
    renderAt(<Mnemarr />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Unlink' }));
    await user.click(screen.getByRole('button', { name: 'Yes, unlink' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('take it out of the manifest');
    expect(screen.getByRole('button', { name: 'Yes, unlink' })).toBeEnabled();
  });

  it('does not offer to change an address that the deployment gives', async () => {
    serveApi({
      'GET /mnemarr': () => ({ ...unlinked, url: 'https://mnemarr.homelab.example', addressFromSettings: true }),
      'GET /mnemarr/pairing': () => none,
    });
    renderAt(<Mnemarr />);

    expect(await screen.findByText(/The deployment gives the address of Mnemarr/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Address of Mnemarr')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pair with Mnemarr' })).toBeEnabled();
  });

  it('says it could not read the destination', async () => {
    serveApi({});
    renderAt(<Mnemarr />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not read the destination');
  });
});
