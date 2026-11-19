import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
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

import styles from './Runs.module.css';
import { type Run, useRunNow, useRunsPage } from './useRuns';

const outcomes: Readonly<Record<NonNullable<Run['outcome']>, { kind: Kind; text: TranslationKey }>> = {
  ok: { kind: kinds.SUCCESS, text: 'RunOutcomeOk' },
  partial: { kind: kinds.WARNING, text: 'RunOutcomePartial' },
  failed: { kind: kinds.DANGER, text: 'RunOutcomeFailed' },
  skipped: { kind: kinds.INFO, text: 'RunOutcomeSkipped' },
};

/** The sentence of a reason that the connection knows; the code itself for one that only a pass says (a timeout, an error it did not foresee). */
function why(reason: string | null): string {
  const key = reasonKeyOf(reason);
  return key ? translate(key) : (reason ?? '');
}

/** The passes, each with its outcome, its counts and its reason (F-VIEW-03). Counts only: never what a conversation said. */
export default function Runs() {
  const { data, error, isPending } = useRunsPage();
  const runNow = useRunNow();

  return (
    <PageContent title={translate('Runs')}>
      <PageContentBody>
        <PageHeading scope={translate('AppName')} title={translate('Runs')} />
        <Card title={translate('Runs')}>
          <p>{translate('RunsIntro')}</p>
          <div className={styles.actions}>
            <Button disabled={data?.running === true || runNow.isPending} onClick={() => runNow.mutate()}>
              {translate('RunNow')}
            </Button>
            {data?.running && <span>{translate('RunsRunning')}</span>}
          </div>
          {runNow.error && <Alert>{runNow.error.message}</Alert>}
          {isPending && <LoadingIndicator />}
          {error && <Alert>{translate('RunsLoadError', { message: error.message })}</Alert>}
          {data && !error && data.runs.length === 0 && <p>{translate('NoRunsYet')}</p>}
          {data && !error && data.runs.length > 0 && (
            <div className={styles.scroll}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">{translate('RunWhen')}</th>
                    <th scope="col">{translate('RunOutcome')}</th>
                    <th scope="col">{translate('RunCounts')}</th>
                    <th scope="col">{translate('RunTook')}</th>
                    <th scope="col">{translate('RunWhy')}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.runs.map((run) => {
                    const outcome = run.outcome ? outcomes[run.outcome] : undefined;
                    return (
                      <tr key={run.id}>
                        <td className={styles.mono}>{new Date(run.startedAt).toLocaleString()}</td>
                        <td>
                          {outcome ? <StatusLabel kind={outcome.kind}>{translate(outcome.text)}</StatusLabel> : '…'}
                        </td>
                        <td>
                          {translate('RunSummary', { listed: run.listed, opened: run.opened, sent: run.turnsSent })}
                        </td>
                        <td className={styles.mono}>
                          {translate('RunTookSeconds', { seconds: (run.durationMs / 1000).toFixed(1) })}
                        </td>
                        <td>{why(run.reason)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      </PageContentBody>
    </PageContent>
  );
}
