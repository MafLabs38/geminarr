import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy';

export interface HealthCheck {
  name: string;
  status: HealthStatus;
  /** The stable code of what is wrong, which the interface maps to a sentence (it does not parse English). */
  reason: string | null;
  description: string | null;
}

export interface Health {
  status: HealthStatus;
  checks: HealthCheck[];
}

export const HEALTH_REFRESH_MS = 15_000;

export function useHealth() {
  return useApiQuery<Health>('/health', { refetchInterval: HEALTH_REFRESH_MS });
}
