import type { LucideIcon } from 'lucide-react';

import { icons } from 'Helpers/Props/icons';
import type { TranslationKey } from 'Utilities/String/en';

export interface MenuEntry {
  to: string;
  label: TranslationKey;
  icon: LucideIcon;
  /** False while the page is not built: the entry is shown, marked "Soon", and is not a link. */
  available: boolean;
}

/**
 * The menu of the application (docs/DESIGN.md §2.1). An entry whose page is not built is shown, marked "Soon": the whole shape of the
 * product is visible and nothing leads to a dead page. It becomes a link in the pull request that builds its page.
 */
export const menuEntries: ReadonlyArray<MenuEntry> = [
  { to: '/', label: 'Overview', icon: icons.OVERVIEW, available: true },
  { to: '/connection', label: 'Connection', icon: icons.CONNECTION, available: false },
  { to: '/conversations', label: 'Conversations', icon: icons.CONVERSATIONS, available: false },
  { to: '/runs', label: 'Runs', icon: icons.RUNS, available: false },
  { to: '/import', label: 'Import', icon: icons.IMPORT, available: false },
  { to: '/mnemarr', label: 'Mnemarr', icon: icons.MNEMARR, available: true },
  { to: '/settings', label: 'Settings', icon: icons.SETTINGS, available: true },
  { to: '/system/status', label: 'System', icon: icons.SYSTEM, available: true },
];
