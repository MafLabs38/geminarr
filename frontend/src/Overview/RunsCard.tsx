import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import LoadingIndicator from 'Components/LoadingIndicator';
import StatusLabel from 'Components/Status/StatusLabel';
import { type Kind, kinds } from 'Helpers/Props/kinds';
import { type Run, useRuns } from 'Runs/useRuns';
import type { TranslationKey } from 'Utilities/String/en';
import translate from 'Utilities/String/translate';

import styles from './RunsCard.module.css';

const outcomes: Readonly<Record<NonNullable<Run['outcome']>, { kind: Kind; text: TranslationKey }>> = {
  ok: { kind: kinds.SUCCESS, text: 'RunOutcomeOk' },
  partial: { kind: kinds.WARNING, text: 'RunOutcomePartial' },
  failed: { kind: kinds.DANGER, text: 'RunOutcomeFailed' },
  skipped: { kind: kinds.INFO, text: 'RunOutcomeSkipped' },
};

/** The latest passes: when, how they ended and what they did, as counts. Never what a conversation said. */
export default function RunsCard() {
  const { data, error, isPending } = useRuns();

  return (
    <Card title={translate('RunsCardTitle')}>
      {isPending && <LoadingIndicator />}
      {error && <Alert>{error.message}</Alert>}
      {data && !error && data.runs.length === 0 && <p>{translate('NoRunsYet')}</p>}
      {data && !error && data.runs.length > 0 && (
        <ul className={styles.runs}>
          {data.runs.map((run) => {
            const outcome = run.outcome ? outcomes[run.outcome] : undefined;
            return (
              <li className={styles.run} key={run.id}>
                <span className={styles.when}>{new Date(run.startedAt).toLocaleString()}</span>
                {outcome ? <StatusLabel kind={outcome.kind}>{translate(outcome.text)}</StatusLabel> : <span>…</span>}
                <span className={styles.summary}>
                  {translate('RunSummary', { listed: run.listed, opened: run.opened, sent: run.turnsSent })}
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}
