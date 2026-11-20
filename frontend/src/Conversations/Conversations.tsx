import { useState } from 'react';

import { type Account, useAccounts } from 'Accounts/useAccounts';
import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import LoadingIndicator from 'Components/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import StatusLabel from 'Components/Status/StatusLabel';
import { type Kind, kinds } from 'Helpers/Props/kinds';
import type { TranslationKey } from 'Utilities/String/en';
import translate from 'Utilities/String/translate';

import styles from './Conversations.module.css';
import { type Backfill, type Conversation, useConversations } from './useConversations';

const states: Readonly<Record<Conversation['state'], { kind: Kind; text: TranslationKey }>> = {
  new: { kind: kinds.INFO, text: 'ConversationStateNew' },
  synced: { kind: kinds.SUCCESS, text: 'ConversationStateSynced' },
  gone: { kind: kinds.WARNING, text: 'ConversationStateGone' },
  error: { kind: kinds.DANGER, text: 'ConversationStateError' },
};

function backfillSentence(backfill: Backfill): TranslationKey {
  if (!backfill.enabled) {
    return 'BackfillOff';
  }
  if (backfill.listed === 0) {
    return 'BackfillNotListed';
  }
  if (backfill.known < backfill.listed) {
    return 'BackfillProgress';
  }
  return backfill.listComplete ? 'BackfillDone' : 'BackfillCut';
}

function BackfillCard({ backfill }: Readonly<{ backfill: Backfill }>) {
  const reading = backfill.enabled && backfill.listed > 0;
  return (
    <Card title={translate('BackfillTitle')}>
      <p>{translate(backfillSentence(backfill), { known: backfill.known, listed: backfill.listed })}</p>
      {reading && (
        <progress
          aria-label={translate('BackfillTitle')}
          className={styles.progress}
          max={backfill.listed}
          value={backfill.known}
        />
      )}
      {backfill.enabled && !backfill.done && (
        <p className={styles.meta}>{translate('BackfillPace', { perPass: backfill.perPass })}</p>
      )}
    </Card>
  );
}

/** What Geminarr knows of the conversations, as counts and states (F-VIEW-02): never a title and never a word of one. */
function AccountSelector({
  accounts,
  value,
  onChange,
}: Readonly<{ accounts: Account[]; value: string; onChange: (id: string) => void }>) {
  return (
    <label className={styles.selector}>
      <span>{translate('ConversationsOfAccount')}</span>
      <select onChange={(event) => onChange(event.target.value)} value={value}>
        {accounts.map((account) => (
          <option key={account.id} value={account.id}>
            {account.machine ?? translate('AccountNotPaired')}
            {account.active ? ` (${translate('AccountActive')})` : ''}
          </option>
        ))}
      </select>
    </label>
  );
}

export default function Conversations() {
  const [chosen, setChosen] = useState<string | undefined>();
  const accounts = useAccounts();
  const list = accounts.data?.accounts ?? [];
  const activeId = list.find((account) => account.active)?.id;
  // What was not chosen is the account that is being read: no parameter is sent for it.
  const { data, error, isPending } = useConversations(chosen === undefined || chosen === activeId ? undefined : chosen);

  return (
    <PageContent title={translate('Conversations')}>
      <PageContentBody>
        <PageHeading scope={translate('AppName')} title={translate('Conversations')} />
        {isPending && <LoadingIndicator />}
        {error && <Alert>{translate('ConversationsLoadError', { message: error.message })}</Alert>}
        {data && !error && (
          <>
            {data.backfill && <BackfillCard backfill={data.backfill} />}
            <Card title={translate('Conversations')}>
              <p>{translate('ConversationsIntro')}</p>
              {list.length > 1 && (
                <AccountSelector accounts={list} onChange={setChosen} value={chosen ?? activeId ?? ''} />
              )}
              {data.conversations.length === 0 && <p>{translate('NoConversationsYet')}</p>}
              {data.conversations.length > 0 && (
                <div className={styles.scroll}>
                  <table className={styles.table}>
                    <thead>
                      <tr>
                        <th scope="col">{translate('ConversationId')}</th>
                        <th scope="col">{translate('ConversationState')}</th>
                        <th scope="col">{translate('ConversationTurns')}</th>
                        <th scope="col">{translate('ConversationRewritten')}</th>
                        <th scope="col">{translate('ConversationLastRead')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.conversations.map((conversation) => {
                        const state = states[conversation.state];
                        return (
                          <tr key={conversation.id}>
                            <td className={styles.mono}>{conversation.id}</td>
                            <td>
                              <StatusLabel kind={state.kind}>{translate(state.text)}</StatusLabel>
                              {conversation.lastError === 'unfinished-read' && (
                                <p className={styles.note}>{translate('ConversationUnfinished')}</p>
                              )}
                            </td>
                            <td className={styles.mono}>
                              {conversation.turnsSent} / {conversation.turnsKnown}
                            </td>
                            <td className={styles.mono}>{conversation.rewrittenTurns}</td>
                            <td className={styles.mono}>
                              {conversation.lastOpenedAt ? new Date(conversation.lastOpenedAt).toLocaleString() : '—'}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              )}
              {data.conversations.length > 0 && (
                <p className={styles.meta}>
                  {translate('ConversationsShown', { shown: data.conversations.length, total: data.total })}
                </p>
              )}
            </Card>
          </>
        )}
      </PageContentBody>
    </PageContent>
  );
}
