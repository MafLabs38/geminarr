// A fake assistant for the end-to-end tests: a synthetic page that needs a sign-in, on its own host, like the real one. Nothing here is real.
// `assistant` is the assistant (the page of the conversations), `signin` is the account provider's sign-in page. A POST to /admin/expire (on `assistant`) ends every session, as an expiry would.
const http = require('node:http');

const sessions = new Set();
let counter = 0;

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

http
  .createServer((request, response) => {
    const url = new URL(request.url, 'http://x');
    const host = (request.headers.host ?? '').split(':')[0];
    if (host === 'signin' && url.pathname === '/signin') {
      if (request.method === 'POST') {
        request.resume();
        request.on('end', () => {
          const token = `s${++counter}`;
          sessions.add(token);
          response.writeHead(302, { Location: `http://assistant:8081/auth?t=${token}` });
          response.end();
        });
        return;
      }
      response.writeHead(200, { 'Content-Type': 'text/html' }).end(signIn(url.searchParams.get('next') ?? '/app'));
      return;
    }
    if (host === 'assistant' && url.pathname === '/auth') {
      response
        .writeHead(302, {
          'Set-Cookie': `assistant=${url.searchParams.get('t')}; Path=/; Max-Age=3600`,
          Location: '/app',
        })
        .end();
      return;
    }
    if (url.pathname === '/admin/expire' && request.method === 'POST') {
      sessions.clear();
      response.writeHead(204).end();
      return;
    }
    if (host === 'assistant' && url.pathname === '/app') {
      const token = cookie(request);
      if (token && sessions.has(token)) {
        response
          .writeHead(200, { 'Content-Type': 'text/html' })
          .end(page('Fake assistant', '<h1 style="margin:40px">Fake assistant: signed in</h1>'));
        return;
      }
      response.writeHead(302, { Location: 'http://signin:8081/signin?next=/app' }).end();
      return;
    }
    response.writeHead(404).end();
  })
  .listen(8081);
