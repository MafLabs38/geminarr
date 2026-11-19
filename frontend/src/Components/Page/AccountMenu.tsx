import Alert from 'Components/Alert/Alert';
import Button from 'Components/Form/Button';
import { useSignOut } from 'Session/session';
import { useSession } from 'Session/SessionContext';
import translate from 'Utilities/String/translate';

import styles from './AccountMenu.module.css';

/** Who is signed in, and the way out. Signing out forgets everything the session had read. */
export default function AccountMenu() {
  const session = useSession();
  const signOut = useSignOut();

  return (
    <div className={styles.menu}>
      <span className={styles.name}>{session.name}</span>
      <Button disabled={signOut.isPending} onClick={() => signOut.mutate()} variant="quiet">
        {translate('SignOut')}
      </Button>
      {signOut.isError && <Alert>{translate('SignOutFailed')}</Alert>}
    </div>
  );
}
