import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

export interface Setting {
  name: string;
  value: string;
  secret: boolean;
}

export function useSettings() {
  return useApiQuery<{ settings: Setting[] }>('/settings');
}
