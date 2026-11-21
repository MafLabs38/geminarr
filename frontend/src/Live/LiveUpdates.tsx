import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { type QueryClient, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

import { liveStatus } from './liveStatus';

export const livePath = '/api/v1/live';

/** What each topic that the host announces makes the pages read again: a key is the path of the read (a path that starts with one of these is read again). */
const topicPaths: Readonly<Record<string, readonly string[]>> = {
  connection: ['/connection', '/accounts'],
  status: ['/health', '/system/status'],
  runs: ['/runs', '/conversations', '/accounts'],
  mnemarr: ['/mnemarr', '/accounts'],
};

export function invalidateTopic(queryClient: QueryClient, topic: string) {
  const paths = topicPaths[topic];
  if (!paths) {
    return Promise.resolve();
  }
  return queryClient.invalidateQueries({
    predicate: ({ queryKey }) => queryKey[0] === 'api' && paths.some((path) => String(queryKey[1]).startsWith(path)),
  });
}

const RETRY_AFTER_MS = 10_000;

/**
 * Opens the WebSocket of the host (below the sign-in gate, so there is a session) and turns what it announces into fresh reads. It carries no data, only "this changed": the answer comes through
 * the API, with the session, like any other read. Where it cannot be opened (a proxy that refuses it) the pages keep reading, and say so.
 */
export default function LiveUpdates() {
  const queryClient = useQueryClient();

  useEffect(() => {
    let stopped = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const connection = new HubConnectionBuilder()
      .withUrl(livePath, { headers: { 'X-Geminarr-Request': 'live' } })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10_000, 30_000])
      .configureLogging(LogLevel.None)
      .build();

    connection.on('changed', (topic: string) => {
      invalidateTopic(queryClient, topic).catch(() => undefined);
    });
    connection.onreconnecting(() => liveStatus.set('reconnecting'));
    connection.onreconnected(() => {
      liveStatus.set('live');
      // What changed while the channel was down was not announced: everything is read again.
      queryClient.invalidateQueries().catch(() => undefined);
    });
    connection.onclose(() => {
      if (!stopped) {
        liveStatus.set('off');
        retry = setTimeout(() => void open(), RETRY_AFTER_MS);
      }
    });

    async function open() {
      if (stopped || connection.state !== HubConnectionState.Disconnected) {
        return;
      }
      liveStatus.set('connecting');
      try {
        await connection.start();
        if (!stopped) {
          liveStatus.set('live');
          // Between the first read and the opening of the channel, something may have changed.
          queryClient.invalidateQueries().catch(() => undefined);
        }
      } catch {
        if (!stopped) {
          liveStatus.set('off');
          retry = setTimeout(() => void open(), RETRY_AFTER_MS);
        }
      }
    }

    void open();
    return () => {
      stopped = true;
      clearTimeout(retry);
      liveStatus.set('connecting');
      connection.stop().catch(() => undefined);
    };
  }, [queryClient]);

  return null;
}
