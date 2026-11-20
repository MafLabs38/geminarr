import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { activeAccount, renderAt, serveApi } from '__tests__/render';
import AccountsTable from 'Accounts/AccountsTable';
import type { Account } from 'Accounts/useAccounts';

const other: Account = {
  ...activeAccount,
  id: '89abcdef',
  machine: 'geminarr-john-roe',
  active: false,
  signedIn: false,
  linked: true,
  waitingLines: 3,
  conversations: 12,
  lastReadAt: '2026-11-19T10:00:00Z',
};

/** The row of an account, found by its machine (the name is what tells an account from another), not by its place in the table. */
const rowOf = (machine: string): HTMLElement => screen.getByText(machine).closest('tr') as HTMLElement;

describe('the accounts that are known', () => {
  it('shows each account by its machine, with its state and what it holds, and says which one is being read', async () => {
    serveApi({ 'GET /accounts': () => ({ accounts: [activeAccount, other] }) });
    renderAt(<AccountsTable />);

    expect(within(await screen.findByRole('table')).getAllByRole('row')).toHaveLength(3);
    expect(within(rowOf('geminarr-demo')).getByText('being read')).toBeInTheDocument();
    expect(within(rowOf('geminarr-demo')).getByText('Signed in')).toBeInTheDocument();
    expect(within(rowOf('geminarr-john-roe')).getByText('Signed out')).toBeInTheDocument();
    // Columns: machine, state, status, waiting lines, conversations, last read.
    const cells = within(rowOf('geminarr-john-roe')).getAllByRole('cell');
    expect(cells[3]).toHaveTextContent('3');
    expect(cells[4]).toHaveTextContent('12');
  });

  it('says that the session of the account being read ended when nobody is signed in', async () => {
    serveApi({ 'GET /accounts': () => ({ accounts: [{ ...activeAccount, signedIn: false }] }) });
    renderAt(<AccountsTable />);

    expect(await screen.findByText('Session ended')).toBeInTheDocument();
  });

  it('offers to pair the machine of an account that is signed in and not paired, and says to sign in one that is not', async () => {
    serveApi({
      'GET /accounts': () => ({ accounts: [activeAccount, { ...other, linked: false, machine: null }] }),
    });
    renderAt(<AccountsTable />);

    expect(await screen.findByRole('link', { name: 'Pair its machine' })).toHaveAttribute('href', '/mnemarr');
    expect(screen.getByText('Sign this account in to pair its machine.')).toBeInTheDocument();
    expect(screen.getByText('Not paired yet')).toBeInTheDocument();
  });

  it('says that no account has been signed in yet', async () => {
    serveApi({ 'GET /accounts': () => ({ accounts: [] }) });
    renderAt(<AccountsTable />);

    expect(await screen.findByText('No account has been signed in yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('forgets an account that is not the one being read, after a confirmation, and not the one being read', async () => {
    const forgotten = vi.fn();
    serveApi({
      'GET /accounts': () => ({ accounts: [activeAccount, other] }),
      'DELETE /accounts/89abcdef': () => {
        forgotten();
        return new Response(null, { status: 204 });
      },
    });
    renderAt(<AccountsTable canForget />);

    await screen.findByRole('table');
    expect(within(rowOf('geminarr-demo')).queryByRole('button', { name: 'Forget' })).not.toBeInTheDocument();
    await userEvent.click(within(rowOf('geminarr-john-roe')).getByRole('button', { name: 'Forget' }));
    expect(forgotten).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Yes, forget it' }));

    await waitFor(() => expect(forgotten).toHaveBeenCalledOnce());
  });

  it('can be given up before it is done', async () => {
    const forgotten = vi.fn();
    serveApi({
      'GET /accounts': () => ({ accounts: [activeAccount, other] }),
      'DELETE /accounts/89abcdef': () => {
        forgotten();
        return new Response(null, { status: 204 });
      },
    });
    renderAt(<AccountsTable canForget />);

    await screen.findByRole('table');
    await userEvent.click(within(rowOf('geminarr-john-roe')).getByRole('button', { name: 'Forget' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(forgotten).not.toHaveBeenCalled();
    expect(within(rowOf('geminarr-john-roe')).getByRole('button', { name: 'Forget' })).toBeInTheDocument();
  });

  it('says why an account could not be forgotten', async () => {
    serveApi({
      'GET /accounts': () => ({ accounts: [activeAccount, other] }),
      'DELETE /accounts/89abcdef': () =>
        new Response(JSON.stringify({ title: 'It is the one being read.' }), {
          status: 409,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    });
    renderAt(<AccountsTable canForget />);

    await screen.findByRole('table');
    await userEvent.click(within(rowOf('geminarr-john-roe')).getByRole('button', { name: 'Forget' }));
    await userEvent.click(screen.getByRole('button', { name: 'Yes, forget it' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });
});
