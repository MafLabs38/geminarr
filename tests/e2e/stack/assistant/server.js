// A fake assistant for the end-to-end tests: a synthetic page that needs a sign-in, on its own host, like the real one. Nothing here is real.
// `assistant` is the assistant (the page of the conversations), `signin` is the account provider's sign-in page. A POST to /admin/expire (on `assistant`) ends every session, as an expiry would.
const http = require('node:http');

const sessions = new Set();
let counter = 0;
// A guest mode shows the page of the assistant to a visitor who is not signed in (as the real one does), without the cookie of an account.
let guest = false;
// How many times the page of the assistant was asked for: a test tells a pass that looked from one that stepped aside.
let visits = 0;

const page = (title, body) =>
  `<!doctype html><html><head><meta charset="utf-8"><title>${title}</title></head><body style="margin:0;font-family:sans-serif">${body}</body></html>`;

const signIn = (next) =>
  page(
    'Sign in',
    `<h1 style="position:absolute;left:100px;top:20px;margin:0">Fake sign-in</h1>
     <form method="post" action="/signin?next=${encodeURIComponent(next)}">
       <input id="name" name="name" autocomplete="off" style="position:absolute;left:100px;top:100px;width:300px;height:40px">
       <input id="password" name="password" type="password" autocomplete="off" style="position:absolute;left:100px;top:160px;width:300px;height:40px">
       <button id="go" type="submit" style="position:absolute;left:100px;top:220px;width:300px;height:40px">Sign in</button>
     </form>`,
  );

function cookie(request) {
  const match = /(?:^|; )assistant=([^;]+)/.exec(request.headers.cookie ?? '');
  return match ? match[1] : null;
}

const send = (response, status, headers, body) => response.writeHead(status, headers ?? {}).end(body);

// One function for each page of the fake: the handler below only picks which.
function signInPage(request, response, url) {
  if (request.method === 'POST') {
    request.resume();
    request.on('end', () => {
      const token = `s${++counter}`;
      sessions.add(token);
      guest = false;
      send(response, 302, { Location: `http://assistant:8081/auth?t=${token}` });
    });
    return;
  }
  send(response, 200, { 'Content-Type': 'text/html' }, signIn(url.searchParams.get('next') ?? '/app'));
}

function auth(response, url) {
  send(response, 302, {
    'Set-Cookie': `assistant=${url.searchParams.get('t')}; Path=/; Max-Age=3600`,
    Location: '/app',
  });
}

function app(request, response) {
  visits += 1;
  const token = cookie(request);
  if (token && sessions.has(token)) {
    send(
      response,
      200,
      { 'Content-Type': 'text/html' },
      page('Fake assistant', '<h1 style="margin:40px">Fake assistant: signed in</h1>'),
    );
    return;
  }
  if (guest) {
    send(
      response,
      200,
      { 'Content-Type': 'text/html' },
      page('Fake assistant', '<h1 style="margin:40px">Fake assistant: guest</h1>'),
    );
    return;
  }
  send(response, 302, { Location: 'http://signin:8081/signin?next=/app' });
}

function admin(request, response, url) {
  if (url.pathname === '/admin/session' && request.method === 'POST') {
    // A session for a person who signs in: the test plays the person, in the browser container (the cookie is set by /auth).
    const token = `s${++counter}`;
    sessions.add(token);
    send(response, 200, { 'Content-Type': 'application/json' }, JSON.stringify({ token }));
    return true;
  }
  if (url.pathname === '/admin/expire' && request.method === 'POST') {
    sessions.clear();
    send(response, 204, undefined);
    return true;
  }
  if (url.pathname === '/admin/visits' && request.method === 'GET') {
    send(response, 200, { 'Content-Type': 'application/json' }, JSON.stringify({ visits }));
    return true;
  }
  if (url.pathname === '/admin/guest' && request.method === 'POST') {
    sessions.clear();
    guest = true;
    send(response, 204, undefined);
    return true;
  }
  return false;
}

// Each route: [host, path, page].
const routes = [
  ['signin', '/signin', (request, response, url) => signInPage(request, response, url)],
  ['assistant', '/auth', (request, response, url) => auth(response, url)],
  ['assistant', '/app', (request, response) => app(request, response)],
];

http
  .createServer((request, response) => {
    const url = new URL(request.url, 'http://x');
    const host = (request.headers.host ?? '').split(':')[0];
    if (!admin(request, response, url)) {
      const route = routes.find(([routeHost, path]) => routeHost === host && path === url.pathname);
      if (route) {
        route[2](request, response, url);
      } else {
        send(response, 404, undefined);
      }
    }
  })
  .listen(8081);
