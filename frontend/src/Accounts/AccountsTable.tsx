import { useState } from 'react';
import { Link } from 'react-router-dom';

import Alert from 'Components/Alert/Alert';
import Button from 'Components/Form/Button';
import LoadingIndicator from 'Components/LoadingIndicator';
import StatusLabel from 'Components/Status/StatusLabel';
import { kinds } from 'Helpers/Props/kinds';
import translate from 'Utilities/String/translate';

import styles from './AccountsTable.module.css';
import { type Account, useAccounts, useForgetAccount } from './useAccounts';

function stateOf(account: Account) {
  if (account.signedIn) {
    return { kind: kinds.SUCCESS, text: translate('AccountSignedIn') };
  }
  return account.active
    ? { kind: kinds.WARNING, text: translate('AccountSessionEnded') }
    : { kind: kinds.INFO, text: translate('AccountSignedOut') };
}

function Row({ account, canForget }: Readonly<{ account: Account; canForget: boolean }>) {
  const forget = useForgetAccount();
  const [sure, setSure] = useState(false);
  const state = stateOf(account);
  return (
    <tr>
      <td>
        <span className={styles.machine}>{account.machine ?? translate('AccountNotPaired')}</span>
        {account.active && <span className={styles.note}>{translate('AccountActive')}</span>}
        {account.signedIn && !account.linked && (
          <span className={styles.note}>
            <Link to="/mnemarr">{translate('AccountPairIt')}</Link>
          </span>
        )}
        {!account.signedIn && !account.linked && (
          <span className={styles.note}>{translate('AccountSignInToPair')}</span>
        )}
      </td>
      <td>
        <StatusLabel kind={state.kind}>{state.text}</StatusLabel>
      </td>
      <td>
        <StatusLabel kind={account.linked ? kinds.SUCCESS : kinds.INFO}>
          {translate(account.linked ? 'AccountLinked' : 'AccountNotLinked')}
        </StatusLabel>
      </td>
      <td className={styles.mono}>{account.waitingLines}</td>
      <td className={styles.mono}>{account.conversations}</td>
      <td className={styles.mono}>{account.lastReadAt ? new Date(account.lastReadAt).toLocaleString() : '—'}</td>
      {canForget && (
        <td>
          {!account.active &&
            (sure ? (
              <div className={styles.confirm}>
                <span>{translate('ForgetAccountConfirm')}</span>
                <Button
                  disabled={forget.isPending}
                  variant="danger"
                  onClick={() => forget.mutate(account.id, { onSettled: () => setSure(false) })}
                >
                  {translate('ForgetAccountNow')}
                </Button>
                <Button variant="quiet" onClick={() => setSure(false)}>
                  {translate('Cancel')}
                </Button>
              </div>
            ) : (
              <Button variant="quiet" onClick={() => setSure(true)}>
                {translate('ForgetAccount')}
              </Button>
            ))}
          {forget.error && <Alert>{forget.error.message}</Alert>}
        </td>
      )}
    </tr>
  );
}

/** The accounts that are known, one row each: its machine, its state and what it holds. Metadata only, never an address. */
export default function AccountsTable({ canForget = false }: Readonly<{ canForget?: boolean }>) {
  const { data, error, isPending } = useAccounts();

  return (
    <div className={styles.wrap}>
      {isPending && <LoadingIndicator />}
      {error && <Alert>{error.message}</Alert>}
      {data && !error && data.accounts.length === 0 && <p>{translate('NoAccountsYet')}</p>}
      {data && !error && data.accounts.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">{translate('AccountMachine')}</th>
              <th scope="col">{translate('AccountState')}</th>
              <th scope="col">{translate('Status')}</th>
              <th scope="col">{translate('AccountWaiting')}</th>
              <th scope="col">{translate('AccountConversations')}</th>
              <th scope="col">{translate('AccountLastRead')}</th>
              {canForget && <th scope="col" />}
            </tr>
          </thead>
          <tbody>
            {data.accounts.map((account) => (
              <Row account={account} canForget={canForget} key={account.id} />
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
