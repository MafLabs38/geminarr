import { useSyncExternalStore } from 'react';

/** Whether the channel that announces changes is open (ADR 0023): `live`, being opened or opened again, or not available. */
export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'off';

let current: LiveStatus = 'connecting';
const listeners = new Set<() => void>();

export const liveStatus = {
  get: () => current,
  set(next: LiveStatus) {
    if (next === current) {
      return;
    }
    current = next;
    listeners.forEach((listener) => listener());
  },
  subscribe(listener: () => void) {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};

export function useLiveStatus(): LiveStatus {
  return useSyncExternalStore(liveStatus.subscribe, liveStatus.get);
}

/** The pages still read again from time to time while the channel is open, in case something was not announced: slowly. */
export const LIVE_FALLBACK_MS = 60_000;
