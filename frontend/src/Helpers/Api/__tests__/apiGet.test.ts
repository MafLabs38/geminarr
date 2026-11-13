import { ApiError, apiGet, apiSend } from 'Helpers/Api/apiGet';

describe('apiGet', () => {
  it('reads JSON from the API under /api/v1 and passes the signal on', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({ hello: 'world' }) });
    vi.stubGlobal('fetch', fetchMock);
    const signal = new AbortController().signal;

    const body = await apiGet<{ hello: string }>('/things', signal);

    expect(body).toEqual({ hello: 'world' });
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/things', { signal });
  });

  it('throws an ApiError carrying the status when the server says no', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: false, status: 404, json: () => Promise.reject(new Error('empty')) }),
    );

    const failure = await apiGet('/things').catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(ApiError);
    expect(failure).toMatchObject({ status: 404, name: 'ApiError', message: 'The server answered 404.' });
  });
});

describe('apiGet failures', () => {
  it('gives the reason of the server when it sent a problem document', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 400,
        json: () => Promise.resolve({ title: 'The request cannot be read.' }),
      }),
    );

    await expect(apiGet('/x')).rejects.toMatchObject({
      status: 400,
      problem: 'The request cannot be read.',
      message: 'The request cannot be read.',
    });
  });

  it('says only the status when the body is not a problem document', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: false, status: 502, json: () => Promise.reject(new Error('html')) }),
    );

    await expect(apiGet('/x')).rejects.toMatchObject({
      status: 502,
      problem: undefined,
      message: 'The server answered 502.',
    });
  });

  it('ignores a title that is not text', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: false, status: 500, json: () => Promise.resolve({ title: 42 }) }),
    );

    await expect(apiGet('/x')).rejects.toMatchObject({ problem: undefined });
  });
});

describe('apiGet with a body that is not JSON', () => {
  it('fails with a reason instead of giving a value', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.reject(new Error('html')) }),
    );

    await expect(apiGet('/x')).rejects.toMatchObject({ status: 200, message: 'The server did not answer with data.' });
  });
});

describe('apiSend', () => {
  it('sends a JSON body with the header that a write needs, and returns the answer', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ name: 'operator-name' }));
    vi.stubGlobal('fetch', fetchMock);

    const answer = await apiSend<{ name: string }>('POST', '/auth/login', { name: 'operator-name' });

    expect(answer).toEqual({ name: 'operator-name' });
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'X-Geminarr-Request': '1', 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: 'operator-name' }),
    });
  });

  it('sends no body and no content type when there is nothing to say, and reads nothing from a 204', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiSend('POST', '/auth/logout')).resolves.toBeUndefined();

    expect(fetchMock).toHaveBeenCalledWith('/api/v1/auth/logout', {
      method: 'POST',
      headers: { 'X-Geminarr-Request': '1' },
      body: undefined,
    });
  });

  it('gives nothing for an answer with no body that is not a 204, such as a 202 for what runs in the background', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(null, { status: 202 }))),
    );

    await expect(apiSend('POST', '/runs')).resolves.toBeUndefined();
  });

  it('fails with the reason of the server', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(Response.json({ title: 'Too many failed attempts.' }, { status: 429 })),
    );

    await expect(apiSend('POST', '/auth/login', {})).rejects.toMatchObject({
      status: 429,
      message: 'Too many failed attempts.',
    });
  });
});
