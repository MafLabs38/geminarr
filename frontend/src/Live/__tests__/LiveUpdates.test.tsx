import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { liveStatus } from '../liveStatus';
import LiveUpdates, { invalidateTopic } from '../LiveUpdates';

const hub = vi.hoisted(() => {
  const handlers: Record<string, (...values: unknown[]) => void> = {};
  const state = { start: vi.fn(), stop: vi.fn(), connectionState: 'Disconnected' };
  const connection = {
    get state() {
      return state.connectionState;
    },
    on: (name: string, handler: (...values: unknown[]) => void) => {
      handlers[name] = handler;
    },
    onreconnecting: (handler: () => void) => {
      handlers.reconnecting = handler;
    },
    onreconnected: (handler: () => void) => {
      handlers.reconnected = handler;
    },
    onclose: (handler: () => void) => {
      handlers.close = handler;
    },
    start: () => state.start(),
    stop: () => state.stop(),
  };
  return { handlers, state, connection, url: { value: '', options: {} as unknown } };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Disconnected: 'Disconnected' },
  LogLevel: { None: 0 },
  HubConnectionBuilder: class {
    withUrl(url: string, options: unknown) {
      hub.url.value = url;
      hub.url.options = options;
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return hub.connection;
    }
  },
}));

function fire(name: string, ...values: unknown[]) {
  hub.handlers[name]?.(...values);
}

function setup() {
  const client = new QueryClient();
  client.setQueryData(['api', '/connection'], { state: 'NotConnected' });
  client.setQueryData(['api', '/health'], { status: 'Healthy' });
  client.setQueryData(['api', '/runs?limit=5'], { runs: [] });
  const invalidated: string[] = [];
  const original = client.invalidateQueries.bind(client);
  vi.spyOn(client, 'invalidateQueries').mockImplementation((filters) => {
    for (const query of client.getQueryCache().findAll(filters as never)) {
      invalidated.push(String(query.queryKey[1]));
    }
    return original({ ...(filters as object), refetchType: 'none' } as never);
  });
  const view = render(
    <QueryClientProvider client={client}>
      <LiveUpdates />
    </QueryClientProvider>,
  );
  return { client, invalidated, view };
}

describe('LiveUpdates', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    hub.state.start.mockReset().mockResolvedValue(undefined);
    hub.state.stop.mockReset().mockResolvedValue(undefined);
    hub.state.connectionState = 'Disconnected';
    liveStatus.set('connecting');
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('opens the channel with the header of a write, and says that it is live once it is open', async () => {
    setup();
    await vi.advanceTimersByTimeAsync(0);

    expect(hub.url.value).toBe('/api/v1/live');
    expect(hub.url.options).toEqual({ headers: { 'X-Geminarr-Request': 'live' } });
    expect(liveStatus.get()).toBe('live');
  });

  it('reads again only what a topic touches, and reads everything again once it is open', async () => {
    const { invalidated } = setup();
    await vi.advanceTimersByTimeAsync(0);
    invalidated.length = 0;

    fire('changed', 'connection');
    fire('changed', 'runs');
    fire('changed', 'unknown');

    expect(invalidated).toEqual(['/connection', '/runs?limit=5']);
  });

  it('says that it reconnects, then that it is live, and reads everything again', async () => {
    const { invalidated } = setup();
    await vi.advanceTimersByTimeAsync(0);
    invalidated.length = 0;

    fire('reconnecting');
    expect(liveStatus.get()).toBe('reconnecting');
    fire('reconnected');

    expect(liveStatus.get()).toBe('live');
    expect(invalidated.sort()).toEqual(['/connection', '/health', '/runs?limit=5']);
  });

  it('says that it is off when it cannot open, and tries again later', async () => {
    hub.state.start.mockRejectedValueOnce(new Error('refused')).mockResolvedValue(undefined);
    setup();
    await vi.advanceTimersByTimeAsync(0);
    expect(liveStatus.get()).toBe('off');

    await vi.advanceTimersByTimeAsync(10_000);

    expect(liveStatus.get()).toBe('live');
  });

  it('opens again after the channel was closed, and stops for good when the page goes away', async () => {
    const { view } = setup();
    await vi.advanceTimersByTimeAsync(0);

    fire('close');
    expect(liveStatus.get()).toBe('off');
    await vi.advanceTimersByTimeAsync(10_000);
    expect(hub.state.start).toHaveBeenCalledTimes(2);

    view.unmount();
    expect(hub.state.stop).toHaveBeenCalled();
    expect(liveStatus.get()).toBe('connecting');
  });

  it('does not open a second channel while one is being opened', async () => {
    hub.state.connectionState = 'Connecting';
    setup();
    await vi.advanceTimersByTimeAsync(0);

    expect(hub.state.start).not.toHaveBeenCalled();
  });
});

describe('invalidateTopic', () => {
  it('does nothing for a topic that it does not know', async () => {
    const client = new QueryClient();
    const spy = vi.spyOn(client, 'invalidateQueries');

    await invalidateTopic(client, 'unknown');

    expect(spy).not.toHaveBeenCalled();
  });
});
