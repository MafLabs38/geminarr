// A fake Mnemarr for the end-to-end tests: its ingest API as a machine sees it (enrolment, positions, conversations, heartbeat), in memory. GET /admin/conversations says what it holds.
import http from 'node:http';

const KEY = 'mnm_e2e-fake-key';
const held = new Map();
const json = (response, value, status = 200) =>
  response.writeHead(status, { 'Content-Type': 'application/json' }).end(JSON.stringify(value));

function ingest(response, body) {
  const { format, session, from, lines } = JSON.parse(body);
  if (format !== 'chat-turns' || !Array.isArray(lines)) return json(response, {}, 400);
  const current = held.get(session) ?? [];
  if (from > current.length) return json(response, {}, 400);
  const before = current.length;
  current.push(...lines.slice(current.length - from));
  held.set(session, current);
  return json(response, { next: current.length, accepted: current.length - before });
}

http
  .createServer((request, response) => {
    const path = new URL(request.url, 'https://mnemarr.invalid').pathname;
    let body = '';
    request.on('data', (chunk) => (body += chunk));
    request.on('end', () => {
      if (path === '/admin/conversations')
        return json(response, Object.fromEntries([...held].map(([session, lines]) => [session, lines.length])));
      if (path === '/api/v1/ingest/enrol')
        return body.includes('E2E-CODE')
          ? json(response, { key: KEY, machine: 'gemini', kind: 'satellite' })
          : json(response, {}, 401);
      if (request.headers.authorization !== `Bearer ${KEY}`) return json(response, {}, 401);
      if (path === '/api/v1/ingest/positions')
        return json(
          response,
          [...held].map(([session, lines]) => ({ session, lines: lines.length })),
        );
      if (path === '/api/v1/ingest/conversations') return ingest(response, body);
      if (path === '/api/v1/ingest/heartbeat') return response.writeHead(204).end();
      return json(response, {}, 404);
    });
  })
  .listen(8090);
