import { CircleCheck, CircleX, Info, LayoutDashboard, Server, TriangleAlert } from 'lucide-react';

/** The only place icons are chosen: components import names from here, never from the icon library. */
export const icons = {
  OVERVIEW: LayoutDashboard,
  SYSTEM: Server,
  INFO: Info,
  SUCCESS: CircleCheck,
  DANGER: CircleX,
  WARNING: TriangleAlert,
} as const;
