import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

/** The colors of the two texts of the badge: a hex color (`#rgb`, `#rrggbb`) or a theme token name (accent, ink, inkMuted…). */
export interface IdentityColors {
  name: string;
  version: string;
}

/** How this Geminarr presents itself at the bottom of the menu (ADR 0013). */
export interface Identity {
  /** The name of this Geminarr; "Geminarr" when nothing else is set. */
  name: string;
  /** The custom logo, with a hash of the file in its address; null for Geminarr's own. */
  logoUrl: string | null;
  /** The version of the running Geminarr. */
  version: string;
  /** Why a logo that was given is not used. */
  logoProblem: string | null;
  colors: IdentityColors;
}

const path = '/identity';

/** The same for everyone who is signed in, and it changes only with a deployment: it is asked once. */
export function useIdentity() {
  return useApiQuery<Identity>(path, { staleTime: Infinity });
}
