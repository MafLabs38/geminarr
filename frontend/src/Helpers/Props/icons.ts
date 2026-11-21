import {
  CircleCheck,
  CircleX,
  History,
  Info,
  LayoutDashboard,
  Link2,
  MessagesSquare,
  Plug,
  Server,
  Settings,
  TriangleAlert,
} from 'lucide-react';

/** The only place icons are chosen: components import names from here, never from the icon library. */
export const icons = {
  OVERVIEW: LayoutDashboard,
  CONNECTION: Plug,
  CONVERSATIONS: MessagesSquare,
  RUNS: History,
  MNEMARR: Link2,
  SYSTEM: Server,
  SETTINGS: Settings,
  INFO: Info,
  SUCCESS: CircleCheck,
  DANGER: CircleX,
  WARNING: TriangleAlert,
} as const;
