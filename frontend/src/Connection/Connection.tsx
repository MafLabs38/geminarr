import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';

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
import LiveView from './LiveView';
import {
  type Connection as ConnectionState,
  type LiveViewTicket,
  useCloseLiveView,
  useConnection,
  useDisconnect,
  usePause,
  useResume,
  useStartLiveView,
} from './useConnection';

/** The hours of the quick pause. */
export const PAUSE_HOURS = 6;

export const states: Readonly<Record<ConnectionState['state'], { kind: Kind; text: TranslationKey }>> = {
  Connected: { kind: kinds.SUCCESS, text: 'ConnectionConnected' },
  NeedsLogin: { kind: kinds.DANGER, text: 'ConnectionNeedsLogin' },
  Degraded: { kind: kinds.WARNING, text: 'ConnectionDegraded' },
  NotConnected: { kind: kinds.INFO, text: 'ConnectionNotConnected' },
  Connecting: { kind: kinds.INFO, text: 'ConnectionConnecting' },
  Paused: { kind: kinds.INFO, text: 'ConnectionPaused' },
};

function Actions({ connection, onSignIn }: Readonly<{ connection: ConnectionState; onSignIn: () => void }>) {
  const pause = usePause();
  const resume = useResume();
  const disconnect = useDisconnect();
  const [sure, setSure] = useState(false);
  const paused = connection.state === 'Paused';
  const connecting = connection.state === 'Connecting';
  const failure = pause.error ?? resume.error ?? disconnect.error;

  return (
    <div className={styles.actions}>
      <Button disabled={paused || connecting} onClick={onSignIn}>
        {translate('SignInToAssistant')}
      </Button>
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
            {translate('CancelPairing')}
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

/** The session of the assistant: its state, the live view where the operator signs in to the dedicated account, pause and resume, and the way to destroy the profile (SPEC §7). */
export default function Connection() {
  const { data, error, isPending } = useConnection();
  const start = useStartLiveView();
  const close = useCloseLiveView();
  const [ticket, setTicket] = useState<LiveViewTicket | null>(null);
  const { mutate: closeLiveView } = close;
  const reason = reasonKeyOf(data?.reason ?? null);
  const state = data ? states[data.state] : undefined;

  const client = useQueryClient();
  const ended = useCallback(
    (signedIn: boolean) => {
      setTicket(null);
      if (signedIn) {
        // The server has settled the state before it said so: it is only read again.
        void client.invalidateQueries({ queryKey: ['api', '/connection'] });
      } else {
        closeLiveView();
      }
    },
    [client, closeLiveView],
  );

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
              {data.hasBrowser && (
                <Actions
                  connection={data}
                  onSignIn={() =>
                    start.mutate(undefined, {
                      onSuccess: (created) => setTicket(created),
                    })
                  }
                />
              )}
              {start.error && <Alert>{start.error.message}</Alert>}
            </>
          )}
        </Card>
        {ticket && (
          <Card title={translate('LiveViewTitle')}>
            <LiveView onEnded={ended} ticket={ticket} />
          </Card>
        )}
      </PageContentBody>
    </PageContent>
  );
}
