import Alert from 'Components/Alert/Alert';
import Card from 'Components/Card';
import LoadingIndicator from 'Components/LoadingIndicator';
import StatusLabel from 'Components/Status/StatusLabel';
import translate from 'Utilities/String/translate';

import styles from './HealthCard.module.css';
import { checkNameKey, reasonKey, statusKind, statusText } from './healthTexts';
import { useHealth } from './useHealth';

/**
 * What the checks of the container say, each with its state in words and icon (never by colour alone) and, when something is wrong, the sentence of its reason code.
 * An error is looked at first: a failed refresh keeps the last data in the query, and the checks under it would be stale.
 */
export default function HealthCard() {
  const { data, error, isPending } = useHealth();

  return (
    <Card title={translate('HealthTitle')}>
      {isPending && <LoadingIndicator />}
      {error && <Alert>{translate('HealthCouldNotRead', { message: error.message })}</Alert>}
      {data && !error && (
        <ul className={styles.checks}>
          {data.checks.map((check) => {
            const name = checkNameKey(check);
            const reason = reasonKey(check);
            return (
              <li className={styles.check} key={check.name}>
                <span className={styles.name}>{name ? translate(name) : check.name}</span>
                <StatusLabel kind={statusKind(check.status)}>{translate(statusText(check.status))}</StatusLabel>
                {check.status !== 'Healthy' && (
                  <span className={styles.reason}>
                    {reason ? translate(reason) : (check.description ?? check.reason)}
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}
