import { useState } from 'react';

import BrandLogo from 'Components/BrandLogo';
import { useIdentity } from 'Identity/identity';
import { identityColorToCss } from 'Identity/identityColor';
import { useSystemStatus } from 'System/Status/useSystemStatus';
import translate from 'Utilities/String/translate';

import styles from './IdentityBadge.module.css';

/**
 * The identity of this Geminarr at the bottom of the menu (ADR 0013), the same as Mnemarr's and Portarr's: the name, its logo (Geminarr's own when none is set) and the version of the running
 * Geminarr, straight on the sidebar (no card). Only the image is clipped to rounded corners. The texts take the colors that are set (the accent by default). Small by design; on a
 * small screen only the logo remains, the texts stay available to assistive technology. The version follows the system status (asked every 30 seconds), so a new version
 * shows up without reloading the page.
 */
export default function IdentityBadge() {
  const { data, isPending } = useIdentity();
  const status = useSystemStatus();
  const [failedUrl, setFailedUrl] = useState<string | null>(null);

  if (isPending) {
    return null;
  }

  const name = data?.name ?? translate('AppName');
  const logoUrl = data?.logoUrl && data.logoUrl !== failedUrl ? data.logoUrl : null;
  const version = status.data?.version ?? data?.version;

  return (
    <div className={styles.badge} title={name}>
      <span className={styles.name} style={{ color: identityColorToCss(data?.colors.name) }}>
        {name}
      </span>
      {logoUrl ? (
        <img alt={name} className={styles.logo} onError={() => setFailedUrl(logoUrl)} src={logoUrl} />
      ) : (
        <span aria-hidden="true" className={styles.logo}>
          <BrandLogo size={56} />
        </span>
      )}
      {version ? (
        <span className={styles.version} style={{ color: identityColorToCss(data?.colors.version) }}>
          {translate('VersionNumber', { version })}
        </span>
      ) : null}
    </div>
  );
}
