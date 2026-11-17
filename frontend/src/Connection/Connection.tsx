import { type MutableRefObject, useEffect, useRef, useState } from 'react';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import Button from 'Components/Form/Button';
import LoadingIndicator from 'Components/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import StatusLabel from 'Components/Status/StatusLabel';
import { reasonKeyOf } from 'Overview/healthTexts';
import { useRunNow, useRuns } from 'Runs/useRuns';
import translate from 'Utilities/String/translate';

import styles from './Connection.module.css';
import { connectionStates } from './connectionStates';
import {
  type Connection as ConnectionState,
  CONNECTION_WAITING_REFRESH_MS,
  useConnection,
  useDisconnect,
  useOpenSignIn,
  usePause,
  useResume,
} from './useConnection';

/** The hours of the quick pause. */
export const PAUSE_HOURS = 6;

/** The states in which a person may have to sign in: a paused connection keeps what it was, and a connected one has nothing to ask. */
const SIGN_IN_STATES: ReadonlySet<ConnectionState['state']> = new Set(['NotConnected', 'NeedsLogin', 'Degraded']);

/** Who the browser is signed in as, or that nobody is: the first thing the page says, in words and not only in a color. */
function Identity({ connection }: Readonly<{ connection: ConnectionState }>) {
  const { account } = connection;
  const signedIn = account !== null || connection.state === 'Connected';
  let headline = translate('NotSignedIn');
  if (account) {
    headline = translate('SignedInAs', { who: account.name ?? account.email });
  } else if (signedIn) {
    headline = translate('SignedInUnknownAccount');
  } else if (connection.signingIn) {
    headline = translate('SigningInNow');
  } else if (connection.state === 'NeedsLogin') {
    headline = translate('SessionEnded');
  }

  return (
    <div className={styles.identity}>
      <p className={styles.who}>{headline}</p>
      {account?.name && <p className={styles.email}>{account.email}</p>}
    </div>
  );
}

/** Where a person signs in: the page of the browser container, opened in a tab of its own (the account is the dedicated one, never the main one). */
function SignIn({
  pagePath,
  signingIn,
  windowRef,
}: Readonly<{ pagePath: string | null; signingIn: boolean; windowRef: MutableRefObject<Window | null> }>) {
  const runNow = useRunNow();
  const runs = useRuns();
  const signIn = useOpenSignIn();
  // The desktop of the browser is video that the page decodes itself: a page that is not a secure context (plain http on an address that is not the machine's own) shows an error of its own and nothing else.
  const secure = globalThis.isSecureContext;

  // The window is opened by the click itself (a browser lets a window open only then), and the server opens the sign-in page in the browser it shows. It is the same origin as this page, which closes it when the sign-in is done.
  const start = (path: string) => {
    windowRef.current = window.open(path, 'geminarr-browser');
    signIn.mutate();
  };

  return (
    <div className={styles.block}>
      <p>{translate(signingIn ? 'SigningInHint' : 'SignInInTheBrowser')}</p>
      <p>{translate('SignInDedicated')}</p>
      {pagePath && !secure && (
        <>
          <Alert>{translate('DesktopNeedsHttps')}</Alert>
          <a className={styles.open} href={`https://${globalThis.location.host}${globalThis.location.pathname}`}>
            {translate('OpenThisPageOverHttps')}
          </a>
        </>
      )}
      {pagePath && secure ? (
        <div className={styles.actions}>
          <Button disabled={signIn.isPending} onClick={() => start(pagePath)}>
            {translate('SignInToAssistant')}
          </Button>
          <a className={styles.open} href={pagePath} rel="noopener noreferrer" target="_blank">
            {translate('OpenTheBrowser')}
          </a>
          <Button
            disabled={runs.data?.running === true || runNow.isPending}
            variant="quiet"
            onClick={() => runNow.mutate()}
          >
            {translate('CheckAgain')}
          </Button>
        </div>
      ) : (
        !pagePath && <Alert>{translate('BrowserPageNotGiven')}</Alert>
      )}
      {(signIn.error ?? runNow.error) && <Alert>{(signIn.error ?? runNow.error)?.message}</Alert>}
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
  // A second click while a request is on its way would send it twice (a disconnect attaches to the browser each time).
  const busy = pause.isPending || resume.isPending || disconnect.isPending;

  return (
    <div className={styles.actions}>
      {paused ? (
        <Button disabled={busy} variant="quiet" onClick={() => resume.mutate()}>
          {translate('ResumePasses')}
        </Button>
      ) : (
        <>
          <Button disabled={busy} variant="quiet" onClick={() => pause.mutate(PAUSE_HOURS)}>
            {translate('PauseHours', { hours: PAUSE_HOURS })}
          </Button>
          <Button disabled={busy} variant="quiet" onClick={() => pause.mutate(null)}>
            {translate('PauseUntilResumed')}
          </Button>
        </>
      )}
      {sure ? (
        <>
          <span>{translate('DisconnectConfirm')}</span>
          <Button
            disabled={busy}
            variant="danger"
            onClick={() => disconnect.mutate(undefined, { onSettled: () => setSure(false) })}
          >
            {translate('DisconnectNow')}
          </Button>
          <Button variant="quiet" onClick={() => setSure(false)}>
            {translate('Cancel')}
          </Button>
        </>
      ) : (
        <Button disabled={busy} variant="danger" onClick={() => setSure(true)}>
          {translate('Disconnect')}
        </Button>
      )}
      {failure && <Alert>{failure.message}</Alert>}
    </div>
  );
}

/** The session of the assistant: its state, where a person signs in (the browser container's desktop, relayed under /browser/), pause and resume, and the way to clear the session (SPEC §7, ADR 0016, 0017). */
export default function Connection() {
  const { data, error, isPending } = useConnection(CONNECTION_WAITING_REFRESH_MS);
  const reason = reasonKeyOf(data?.reason ?? null);
  // The window of the browser that this page opened to sign in: closed by this page once the connection says that it is done.
  const browserWindow = useRef<Window | null>(null);
  const connected = data?.state === 'Connected';
  useEffect(() => {
    if (connected && browserWindow.current) {
      if (!browserWindow.current.closed) {
        browserWindow.current.close();
      }
      browserWindow.current = null;
      window.focus();
    }
  }, [connected]);
  const state = data ? connectionStates[data.state] : undefined;

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
              <Identity connection={data} />
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
              {data.hasBrowser && SIGN_IN_STATES.has(data.state) && (
                <SignIn pagePath={data.browserPagePath} signingIn={data.signingIn} windowRef={browserWindow} />
              )}
              {data.hasBrowser && <Actions connection={data} />}
            </>
          )}
        </Card>
      </PageContentBody>
    </PageContent>
  );
}
