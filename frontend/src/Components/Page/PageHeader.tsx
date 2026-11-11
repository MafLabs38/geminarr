import { Link } from 'react-router-dom';

import translate from 'Utilities/String/translate';

import styles from './PageHeader.module.css';

/** The header of the page: the name of the product, as text (the brand has no logo yet). The account menu arrives with the sign-in. */
export default function PageHeader() {
  return (
    <header className={styles.header}>
      <Link className={styles.brand} to="/">
        <span className={styles.name}>{translate('AppName')}</span>
      </Link>
    </header>
  );
}
