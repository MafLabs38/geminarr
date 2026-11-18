// A fake assistant for the end-to-end tests: synthetic pages shaped like the ones the extension reads (a list of conversations, and the turns of one), on a plain-HTTP host of the test network. Nothing here is real.
// POST /admin/signout shows the page of a visitor who is not signed in; POST /admin/signin goes back; POST /admin/add adds a conversation at the top of the list.
import http from 'node:http';

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
let signedIn = true;
let added = 0;

const page = (body) =>
  `<!doctype html><html><head><meta charset="utf-8"><title>Fake assistant</title></head><body>${body}</body></html>`;
const html = (response, body, status = 200) =>
  response.writeHead(status, { 'Content-Type': 'text/html' }).end(page(body));
const escape = (text) => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');

function list(response) {
  // A page that nobody is signed in to lists nothing.
  const links = (signedIn ? conversations : [])
    .map((item) => `<a href="/app/${item.id}">${escape(item.title)}</a>`)
    .join('');
  const account = signedIn
    ? '<a href="https://accounts.fake.invalid/SignOutOptions">Account</a>'
    : '<a href="https://accounts.fake.invalid/signin">Sign in</a>';
  html(
    response,
    `${account}<conversations-list><infinite-scroller style="display:block;height:300px;overflow:auto">${links}</infinite-scroller></conversations-list>`,
  );
}

function conversation(response, id) {
  const found = conversations.find((item) => item.id === id);
  if (!found) return html(response, 'Not found', 404);
  const turns = found.turns.map(([role, text]) =>
    role === 'user'
      ? `<user-query><user-query-content>${escape(text)}</user-query-content></user-query>`
      : `<model-response><message-content>${escape(text)}</message-content></model-response>`,
  );
  html(response, turns.join(''));
}

http
  .createServer((request, response) => {
    const path = new URL(request.url, 'https://assistant.invalid').pathname;
    if (request.method === 'POST' && path.startsWith('/admin/')) {
      request.resume();
      if (path === '/admin/signout') signedIn = false;
      if (path === '/admin/signin') signedIn = true;
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
      return response.writeHead(204).end();
    }
    if (path === '/app') return list(response);
    const match = /^\/app\/([A-Za-z0-9_-]+)$/.exec(path);
    return match ? conversation(response, match[1]) : html(response, 'Not found', 404);
  })
  .listen(8081);
