import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { liveStatus, useLiveStatus } from '../liveStatus';

describe('liveStatus', () => {
  it('tells the pages when the channel opens and closes, and only when it changes', () => {
    liveStatus.set('connecting');
    const { result } = renderHook(() => useLiveStatus());
    expect(result.current).toBe('connecting');

    act(() => liveStatus.set('live'));
    expect(result.current).toBe('live');

    let renders = 0;
    const stop = liveStatus.subscribe(() => renders++);
    liveStatus.set('live');
    expect(renders).toBe(0);
    liveStatus.set('off');
    expect(renders).toBe(1);
    stop();
    liveStatus.set('live');
    expect(renders).toBe(1);
    liveStatus.set('connecting');
  });
});
