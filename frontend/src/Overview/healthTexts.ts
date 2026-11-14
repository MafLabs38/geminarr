import { type Kind, kinds } from 'Helpers/Props/kinds';
import type { TranslationKey } from 'Utilities/String/en';

import type { HealthCheck, HealthStatus } from './useHealth';

/** The sentence of each reason code the server gives (docs/SPEC.md §10.3). A code this version does not know falls back to the words of the server. */
const reasonTexts: ReadonlyMap<string, TranslationKey> = new Map<string, TranslationKey>([
  ['data-folder-missing', 'ReasonDataFolderMissing'],
  ['data-folder-read-only', 'ReasonDataFolderReadOnly'],
  ['operator-not-configured', 'ReasonOperatorNotConfigured'],
  ['operator-password-unreadable', 'ReasonOperatorPasswordUnreadable'],
  ['operator-password-weak', 'ReasonOperatorPasswordWeak'],
  ['database-unreadable', 'ReasonDatabaseUnreadable'],
  ['database-step-unknown', 'ReasonDatabaseStepUnknown'],
  ['outbox-growing', 'ReasonOutboxGrowing'],
  ['not-connected', 'ReasonConnectionNotConnected'],
  ['needs-login', 'ReasonNeedsLogin'],
  ['reader-broken', 'ReasonReaderBroken'],
  ['rate-limited', 'ReasonRateLimited'],
  ['unreachable', 'ReasonUnreachable'],
  ['paused', 'ReasonPaused'],
  ['connecting', 'ReasonConnecting'],
  ['profile-missing', 'ReasonProfileMissing'],
  ['profile-unreadable', 'ReasonProfileUnreadable'],
  ['profile-not-private', 'ReasonProfileNotPrivate'],
  ['mnemarr-not-linked', 'ReasonMnemarrNotLinked'],
  ['mnemarr-insecure-address', 'ReasonMnemarrInsecureAddress'],
  ['mnemarr-refused', 'ReasonMnemarrRefused'],
  ['mnemarr-unreachable', 'ReasonMnemarrUnreachable'],
  ['mnemarr-rejected', 'ReasonMnemarrRejected'],
]);

/** The name of each check, as the server calls it. */
const checkNames: Readonly<Record<string, TranslationKey>> = {
  'data-folder': 'CheckDataFolder',
  database: 'CheckDatabase',
  operator: 'CheckOperator',
  connection: 'CheckConnection',
  mnemarr: 'CheckMnemarr',
  outbox: 'CheckOutbox',
  profile: 'CheckProfile',
};

/** The sentence of a reason code (of a check, or of what the destination or the connection says), or nothing for a code this version does not know. */
export function reasonKeyOf(code: string | null): TranslationKey | undefined {
  return code ? reasonTexts.get(code) : undefined;
}

export function reasonKey(check: HealthCheck): TranslationKey | undefined {
  return check.reason ? reasonTexts.get(check.reason) : undefined;
}

export function checkNameKey(check: HealthCheck): TranslationKey | undefined {
  return checkNames[check.name];
}

const statusKinds: Readonly<Record<HealthStatus, Kind>> = {
  Healthy: kinds.SUCCESS,
  Degraded: kinds.WARNING,
  Unhealthy: kinds.DANGER,
};

const statusTexts: Readonly<Record<HealthStatus, TranslationKey>> = {
  Healthy: 'HealthHealthy',
  Degraded: 'HealthDegraded',
  Unhealthy: 'HealthUnhealthy',
};

export const statusKind = (status: HealthStatus): Kind => statusKinds[status];

export const statusText = (status: HealthStatus): TranslationKey => statusTexts[status];
