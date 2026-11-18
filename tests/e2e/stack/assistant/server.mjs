// A fake assistant for the end-to-end tests: synthetic pages shaped like the ones the extension reads (a list of conversations, and the turns of one), on a plain-HTTP host of the test network. Nothing here is real.
// POST /admin/signin makes the next visitor a person who signs in (a cookie is given); POST /admin/signout ends every session, as an expiry would; GET /admin/guests counts the pages shown to a visitor; GET /admin/reach?host=&port= says whether a port of another container opens; POST /admin/break shows a page that is neither (a page whose shape changed); POST /admin/add adds a conversation at the top of the list.
import http from 'node:http';
import net from 'node:net';

const conversations = [
  {
    id: 'c1a2b3c4d5e6',
    title: 'Planning a synthetic trip',
    turns: [
      ['user', 'Where could we go in spring?'],
      ['assistant', 'A fictional coast, with a made-up train.'],
    ],
  },
  {
    id: 'd2b3c4d5e6f7',
    title: 'A recipe that does not exist',
    turns: [
      ['user', 'How long does the invented soup cook?'],
      ['assistant', 'About as long as a synthetic afternoon.'],
      ['user', 'Thanks.'],
      ['assistant', 'You are welcome.'],
    ],
  },
  {
    id: 'e3c4d5e6f7a8',
    title: 'Counting to three',
    turns: [
      ['user', 'Count to three.'],
      ['assistant', 'One, two, three.'],
    ],
  },
];
// A session is a cookie of the browser (the extension clears them when the operator disconnects): a request without a valid one is a visitor. `armed` is a person who signs in at the next request.
let mode = 'in';
const sessions = new Set();
let armed = false;
let guests = 0;
let added = 0;

const COOKIE = 'fake_session';
const cookieOf = (request) => new RegExp(`(?:^|; )${COOKIE}=([^;]+)`).exec(request.headers.cookie ?? '')?.[1];

/** Whether the request is a signed-in one, and the header that gives the cookie to a person who has just signed in. */
function visitor(request) {
  const token = cookieOf(request);
  const valid = Boolean(token) && sessions.has(token);
  // The person who signs in does so at the next request, whoever makes it: a request that is signed in already uses the session that it has.
  const signsIn = armed;
  armed = false;
  if (valid) return { signedIn: true, headers: {} };
  if (signsIn) {
    const created = `s${sessions.size + 1}${Date.now()}`;
    sessions.add(created);
    return { signedIn: true, headers: { 'Set-Cookie': `${COOKIE}=${created}; Path=/` } };
  }
  guests += 1;
  return { signedIn: false, headers: {} };
}

const page = (body) =>
  `<!doctype html><html><head><meta charset="utf-8"><title>Fake assistant</title></head><body>${body}</body></html>`;
const html = (response, body, status = 200, headers = {}) =>
  response.writeHead(status, { 'Content-Type': 'text/html', ...headers }).end(page(body));
const escape = (text) => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');

function list(request, response) {
  if (mode === 'broken') return html(response, '<p>A page that is not the one that the extension knows.</p>');
  const { signedIn, headers } = visitor(request);
  // A page that nobody is signed in to has a sign-in link and lists nothing.
  const account = signedIn
    ? '<a href="https://accounts.fake.invalid/SignOutOptions" aria-label="Google Account: Jane Doe (jane.doe@example.org)">Account</a>'
    : '<a href="https://accounts.fake.invalid/ServiceLogin?continue=x">Sign in</a>';
  const links = (signedIn ? conversations : [])
    .map((item) => `<a href="/app/${item.id}">${escape(item.title)}</a>`)
    .join('');
  return html(
    response,
    `${account}<conversations-list><infinite-scroller style="display:block;height:300px;overflow:auto">${links}</infinite-scroller></conversations-list>`,
    200,
    headers,
  );
}

function conversation(request, response, id) {
  if (!visitor(request).signedIn) return html(response, '<p>Sign in to see this conversation.</p>', 200);
  const found = conversations.find((item) => item.id === id);
  if (!found) return html(response, 'Not found', 404);
  const turns = found.turns.map(([role, text]) =>
    role === 'user'
      ? `<user-query><user-query-content>${escape(text)}</user-query-content></user-query>`
      : `<model-response><message-content>${escape(text)}</message-content></model-response>`,
  );
  html(response, turns.join(''));
}

function admin(path, response) {
  if (path === '/admin/signout') {
    sessions.clear();
    mode = 'in';
  }
  if (path === '/admin/signin') {
    mode = 'in';
    armed = true;
  }
  if (path === '/admin/break') mode = 'broken';
  if (path === '/admin/add') {
    added += 1;
    conversations.unshift({
      id: `f4d5e6f7a8b${added}`,
      title: `Added later ${added}`,
      turns: [
        ['user', 'A new question.'],
        ['assistant', 'A new answer.'],
      ],
    });
  }
  response.writeHead(204).end();
}

/** Whether another container of the network can open a port of the browser container: a test of its isolation, from the outside (this one is outside). */
function reach(request, response) {
  const url = new URL(request.url, 'https://assistant.invalid');
  const socket = net.connect({
    host: url.searchParams.get('host') ?? '',
    port: Number(url.searchParams.get('port')),
    timeout: 3000,
  });
  const answer = (open) => {
    socket.destroy();
    response.writeHead(200, { 'Content-Type': 'application/json' }).end(JSON.stringify({ open }));
  };
  socket.once('connect', () => answer(true));
  socket.once('error', () => answer(false));
  socket.once('timeout', () => answer(false));
}

http
  .createServer((request, response) => {
    const path = new URL(request.url, 'https://assistant.invalid').pathname;
    if (request.method === 'POST' && path.startsWith('/admin/')) {
      request.resume();
      return admin(path, response);
    }
    if (path === '/admin/reach' && request.method === 'GET') return reach(request, response);
    if (path === '/admin/guests' && request.method === 'GET') {
      return response.writeHead(200, { 'Content-Type': 'application/json' }).end(JSON.stringify({ guests }));
    }
    if (path === '/app') return list(request, response);
    const match = /^\/app\/([A-Za-z0-9_-]+)$/.exec(path);
    return match ? conversation(request, response, match[1]) : html(response, 'Not found', 404);
  })
  .listen(8081);
