// A fake Mnemarr for the end-to-end tests: its ingest API as a machine sees it (enrolment by a one-time code, pairing by asking, positions, conversations, heartbeat), in memory, with a key and a store for each machine.
// GET /admin/conversations says what a machine holds (the machine of the enrolment code by default, `?machine=` for another); GET /admin/machines lists the machines that exist.
import http from 'node:http';

/** The machine that the one-time code of the stack enrols, and its key. */
const ENROLLED = { machine: 'gemini', key: 'mnm_e2e-fake-key' };
/** machine -> { key, held: session -> lines }. A machine that is paired again under the same name gets a new key and keeps what it holds, as Mnemarr does. */
const machines = new Map([[ENROLLED.machine, { key: ENROLLED.key, held: new Map() }]]);
let codeUsed = false;
let pairings = 0;
const asked = new Map();
const json = (response, value, status = 200) =>
  response.writeHead(status, { 'Content-Type': 'application/json' }).end(JSON.stringify(value));

function ingest(response, held, body) {
  const { format, session, from, lines } = JSON.parse(body);
  if (format !== 'chat-turns' || !Array.isArray(lines)) return json(response, {}, 400);
  const current = held.get(session) ?? [];
  if (from > current.length) return json(response, {}, 400);
  const before = current.length;
  current.push(...lines.slice(current.length - from));
  held.set(session, current);
  return json(response, { next: current.length, accepted: current.length - before });
}

/** A machine asks to be paired: a person approves at once (the page of approval is Mnemarr's, not under test here), and the poll gives the key. */
function pair(response, body) {
  const { hostname } = JSON.parse(body);
  if (typeof hostname !== 'string' || !/^[a-z0-9-]{1,48}$/.test(hostname)) return json(response, {}, 400);
  pairings += 1;
  const deviceCode = `device-${pairings}-${hostname}`;
  asked.set(deviceCode, hostname);
  return json(response, {
    userCode: `E2E-${String(pairings).padStart(4, '0')}`,
    deviceCode,
    // eslint-disable-next-line sonarjs/no-clear-text-protocols -- a fake on the private network of the test stack, which speaks plain HTTP
    verificationUrl: 'http://10.254.77.10:8090/pair',
    expiresInSeconds: 600,
    intervalSeconds: 1,
  });
}

function pollPairing(response, body) {
  const hostname = asked.get(JSON.parse(body).deviceCode);
  if (!hostname) return json(response, { state: 'expired' });
  asked.delete(JSON.parse(body).deviceCode);
  const key = `mnm_e2e-key-${hostname}`;
  machines.set(hostname, { key, held: machines.get(hostname)?.held ?? new Map() });
  return json(response, { state: 'approved', key, machine: hostname });
}

const machineOfKey = (request) => {
  const bearer = /^Bearer (.+)$/.exec(request.headers.authorization ?? '')?.[1];
  return [...machines.entries()].find(([, value]) => value.key === bearer)?.[0];
};

http
  .createServer((request, response) => {
    const url = new URL(request.url, 'https://mnemarr.invalid');
    const path = url.pathname;
    let body = '';
    request.on('data', (chunk) => (body += chunk));
    request.on('end', () => {
      if (path === '/admin/conversations') {
        const held = machines.get(url.searchParams.get('machine') ?? ENROLLED.machine)?.held ?? new Map();
        return json(response, Object.fromEntries([...held].map(([session, lines]) => [session, lines.length])));
      }
      if (path === '/admin/machines') return json(response, [...machines.keys()]);
      if (path === '/api/v1/ingest/enrol') {
        // One time, as the real one: a second account does not enrol under the machine of the first.
        if (!body.includes('E2E-CODE') || codeUsed) return json(response, {}, 401);
        codeUsed = true;
        return json(response, { ...ENROLLED, kind: 'satellite' });
      }
      if (path === '/api/v1/ingest/pair') return pair(response, body);
      if (path === '/api/v1/ingest/pair/poll') return pollPairing(response, body);
      const machine = machineOfKey(request);
      if (!machine) return json(response, {}, 401);
      const { held } = machines.get(machine);
      if (path === '/api/v1/ingest/positions')
        return json(
          response,
          [...held].map(([session, lines]) => ({ session, lines: lines.length })),
        );
      if (path === '/api/v1/ingest/conversations') return ingest(response, held, body);
      if (path === '/api/v1/ingest/heartbeat') return response.writeHead(204).end();
      return json(response, {}, 404);
    });
  })
  .listen(8090);
