import { useState } from 'react';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import Button from 'Components/Form/Button';
import LoadingIndicator from 'Components/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import StatusLabel from 'Components/Status/StatusLabel';
import { type Kind, kinds } from 'Helpers/Props/kinds';
import { reasonKeyOf } from 'Overview/healthTexts';
import type { TranslationKey } from 'Utilities/String/en';
import translate from 'Utilities/String/translate';

import styles from './Connection.module.css';
import { type Connection as ConnectionState, useConnection, useDisconnect, usePause, useResume } from './useConnection';

/** The hours of the quick pause. */
export const PAUSE_HOURS = 6;

/** The states in which a person may have to sign in: a paused connection keeps what it was, and a connected one has nothing to ask. */
const SIGN_IN_STATES: ReadonlySet<ConnectionState['state']> = new Set(['NotConnected', 'NeedsLogin', 'Degraded']);

export const states: Readonly<Record<ConnectionState['state'], { kind: Kind; text: TranslationKey }>> = {
  Connected: { kind: kinds.SUCCESS, text: 'ConnectionConnected' },
  NeedsLogin: { kind: kinds.DANGER, text: 'ConnectionNeedsLogin' },
  Degraded: { kind: kinds.WARNING, text: 'ConnectionDegraded' },
  NotConnected: { kind: kinds.INFO, text: 'ConnectionNotConnected' },
  Paused: { kind: kinds.INFO, text: 'ConnectionPaused' },
};

/** Where a person signs in: the page of the browser container, opened in a tab of its own (the account is the dedicated one, never the main one). */
function SignIn({ pagePath }: Readonly<{ pagePath: string | null }>) {
  return (
    <div className={styles.block}>
      <p>{translate('SignInInTheBrowser')}</p>
      <p>{translate('SignInDedicated')}</p>
      {pagePath ? (
        <a className={styles.open} href={pagePath} rel="noopener noreferrer" target="_blank">
          {translate('OpenTheBrowser')}
        </a>
      ) : (
        <Alert>{translate('BrowserPageNotGiven')}</Alert>
      )}
    </div>
  );
}

function Actions({ connection }: Readonly<{ connection: ConnectionState }>) {
  const pause = usePause();
  const resume = useResume();
  const disconnect = useDisconnect();
  const [sure, setSure] = useState(false);
  const paused = connection.state === 'Paused';
  const failure = pause.error ?? resume.error ?? disconnect.error;

  return (
    <div className={styles.actions}>
      {paused ? (
        <Button variant="quiet" onClick={() => resume.mutate()}>
          {translate('ResumePasses')}
        </Button>
      ) : (
        <>
          <Button variant="quiet" onClick={() => pause.mutate(PAUSE_HOURS)}>
            {translate('PauseHours', { hours: PAUSE_HOURS })}
          </Button>
          <Button variant="quiet" onClick={() => pause.mutate(null)}>
            {translate('PauseUntilResumed')}
          </Button>
        </>
      )}
      {sure ? (
        <>
          <span>{translate('DisconnectConfirm')}</span>
          <Button variant="danger" onClick={() => disconnect.mutate(undefined, { onSettled: () => setSure(false) })}>
            {translate('DisconnectNow')}
          </Button>
          <Button variant="quiet" onClick={() => setSure(false)}>
            {translate('Cancel')}
          </Button>
        </>
      ) : (
        <Button variant="danger" onClick={() => setSure(true)}>
          {translate('Disconnect')}
        </Button>
      )}
      {failure && <Alert>{failure.message}</Alert>}
    </div>
  );
}

/** The session of the assistant: its state, where a person signs in (the browser container's own page), pause and resume, and the way to clear the session (SPEC §7, ADR 0016). */
export default function Connection() {
  const { data, error, isPending } = useConnection();
  const reason = reasonKeyOf(data?.reason ?? null);
  const state = data ? states[data.state] : undefined;

  return (
    <PageContent title={translate('Connection')}>
      <PageContentBody>
        <PageHeading scope={translate('AppName')} title={translate('Connection')} />
        <Card title={translate('Connection')}>
          <p>{translate('ConnectionIntro')}</p>
          {isPending && <LoadingIndicator />}
          {error && <Alert>{translate('ConnectionCouldNotRead', { message: error.message })}</Alert>}
          {data && state && !error && (
            <>
              <DescriptionList
                items={[
                  {
                    title: translate('Status'),
                    data: <StatusLabel kind={state.kind}>{translate(state.text)}</StatusLabel>,
                  },
                  {
                    title: translate('ConnectionSince'),
                    data: new Date(data.since).toLocaleString(),
                    mono: true,
                  },
                  {
                    title: translate('ConnectionLastGood'),
                    data: data.lastOkAt ? new Date(data.lastOkAt).toLocaleString() : '—',
                    mono: true,
                  },
                  ...(data.pausedUntil
                    ? [
                        {
                          title: translate('PausedUntil'),
                          data: new Date(data.pausedUntil).toLocaleString(),
                          mono: true,
                        },
                      ]
                    : []),
                ]}
              />
              {data.reason && reason && data.state !== 'Connected' && (
                <p className={styles.reason}>{translate(reason)}</p>
              )}
              {!data.hasBrowser && <Alert>{translate('NoBrowserNotice')}</Alert>}
              {data.hasBrowser && SIGN_IN_STATES.has(data.state) && <SignIn pagePath={data.browserPagePath} />}
              {data.hasBrowser && <Actions connection={data} />}
            </>
          )}
        </Card>
      </PageContentBody>
    </PageContent>
  );
}
