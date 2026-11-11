import translate from 'Utilities/String/translate';

/**
 * The API answered, but not with a success. The status tells the caller what to do (a 401 means "sign in again"); `problem` is the
 * reason the server gave in its problem document, when it gave one.
 * The reason is shown as the server worded it, in English: the server does not know the language of the interface.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem?: string,
  ) {
    super(problem ?? translate('ServerAnswered', { status }));
    this.name = 'ApiError';
  }
}

const API_BASE = '/api/v1';

async function failure(response: Response): Promise<ApiError> {
  try {
    const body = (await response.json()) as { title?: unknown };
    return new ApiError(response.status, typeof body.title === 'string' ? body.title : undefined);
  } catch {
    // Not a problem document (a proxy's page, an empty body): the status is all there is to say.
    return new ApiError(response.status);
  }
}

/**
 * The one place the API is addressed: a URL base is added here, and nowhere else. A success whose body is not JSON (a proxy's page) is an
 * error, not a value. Writes (and the header that the server will require on them, docs/SPEC.md F-ACC-04) come with the first vertical that writes.
 */
export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, { signal });
  if (!response.ok) {
    throw await failure(response);
  }
  try {
    return (await response.json()) as T;
  } catch {
    throw new ApiError(response.status, translate('NotJson'));
  }
}
