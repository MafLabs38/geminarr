// Test only: tries a TCP connection from inside the browser's network namespace, as the user that this container runs as, and says whether it opened. It shows what the egress guard lets each user reach.
const http = require('node:http');
const net = require('node:net');

http
  .createServer((req, res) => {
    // The base only lets a relative path be parsed: it is never reached.
    const url = new URL(req.url, 'https://probe.invalid');
    if (url.pathname !== '/try') {
      res.statusCode = 404;
      res.end();
      return;
    }
    let answered = false;
    const socket = net.connect({
      host: url.searchParams.get('host'),
      port: Number(url.searchParams.get('port')),
      timeout: 3000,
    });
    const answer = (open) => {
      if (answered) return;
      answered = true;
      socket.destroy();
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify({ open, uid: process.getuid() }));
    };
    socket.once('connect', () => answer(true));
    socket.once('error', () => answer(false));
    socket.once('timeout', () => answer(false));
  })
  .listen(Number(process.env.PORT), '0.0.0.0');
