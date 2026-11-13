import { Link } from 'react-router-dom';

import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import Button from 'Components/Form/Button';
import LoadingIndicator from 'Components/LoadingIndicator';
import StatusLabel from 'Components/Status/StatusLabel';
import { type Connection, useConnection } from 'Connection/useConnection';
import { type Kind, kinds } from 'Helpers/Props/kinds';
import { hostOf } from 'Mnemarr/hostOf';
import { useMnemarr } from 'Mnemarr/useMnemarr';
import { useRunNow, useRuns } from 'Runs/useRuns';
import type { TranslationKey } from 'Utilities/String/en';
import translate from 'Utilities/String/translate';

import styles from './ConnectionCard.module.css';
import { reasonKeyOf } from './healthTexts';

const states: Readonly<Record<Connection['state'], { kind: Kind; text: TranslationKey }>> = {
  Connected: { kind: kinds.SUCCESS, text: 'ConnectionConnected' },
  NeedsLogin: { kind: kinds.DANGER, text: 'ConnectionNeedsLogin' },
  Degraded: { kind: kinds.WARNING, text: 'ConnectionDegraded' },
  NotConnected: { kind: kinds.INFO, text: 'ConnectionNotConnected' },
  Connecting: { kind: kinds.INFO, text: 'ConnectionConnecting' },
  Paused: { kind: kinds.INFO, text: 'ConnectionPaused' },
};

/** Where Geminarr stands: connected to an assistant or not, linked to a Mnemarr or not, and a button to run a pass now. */
export default function ConnectionCard() {
  const connection = useConnection();
  const mnemarr = useMnemarr();
  const runs = useRuns();
  const runNow = useRunNow();
  const state = connection.data ? states[connection.data.state] : undefined;
  const reason = reasonKeyOf(connection.data?.reason ?? null);

  return (
    <Card title={translate('ConnectionCardTitle')}>
      {connection.isPending && <LoadingIndicator />}
      {connection.error && <Alert>{connection.error.message}</Alert>}
      {connection.data && state && (
        <div className={styles.rows}>
          <StatusLabel kind={state.kind}>{translate(state.text)}</StatusLabel>
          {connection.data.state !== 'Connected' && reason && (
            <span className={styles.reason}>{translate(reason)}</span>
          )}
          {connection.data.reader === 'Demo' && <span className={styles.notice}>{translate('DemoReaderNotice')}</span>}
        </div>
      )}
      <div className={styles.rows}>
        {mnemarr.data?.linked ? (
          <StatusLabel kind={mnemarr.data.problem ? kinds.WARNING : kinds.SUCCESS}>
            {translate('LinkedTo', { host: hostOf(mnemarr.data.url ?? '') })}
          </StatusLabel>
        ) : (
          <StatusLabel kind={kinds.INFO}>{translate('MnemarrNotLinked')}</StatusLabel>
        )}
        <Link to="/mnemarr">{translate('MnemarrTitle')}</Link>
      </div>
      <Button disabled={runs.data?.running === true || runNow.isPending} onClick={() => runNow.mutate()}>
        {translate('RunNow')}
      </Button>
      {runNow.error && <Alert>{runNow.error.message}</Alert>}
    </Card>
  );
}
