import { Link } from 'react-router-dom';

import BrandLogo from 'Components/BrandLogo';
import translate from 'Utilities/String/translate';

import AccountMenu from './AccountMenu';
import styles from './PageHeader.module.css';

/** The header of the page: its mark and its name, and who is signed in. */
export default function PageHeader() {
  return (
    <header className={styles.header}>
      <Link className={styles.brand} to="/">
        <BrandLogo size={30} />
        <span className={styles.name}>{translate('AppName')}</span>
      </Link>
      <AccountMenu />
    </header>
  );
}
