#!/bin/sh
# Makes the web desktop of the browser reachable by Geminarr alone (ADR 0017), in two ways.
#
# 1. It listens on the loopback of the container's network namespace, and nowhere else. The image's nginx listens on every interface (3000 in clear, 3001 with a self-signed certificate), and the web desktop
#    has no sign-in of its own: without this, any other container of the same network (or any other pod of the cluster, if no network policy is enforced) could open a desktop that is signed in to a
#    Google account. Geminarr shares the namespace and reaches it as 127.0.0.1.
# 2. A request that names an origin must name the one that Geminarr gives every request that it relays (BrowserOptions.RelayOrigin), which no web page can have. A page that the browser itself loads shares
#    the namespace and could otherwise reach the desktop on the loopback. A request that names none is not from a page. (The desktop's WebSocket also checks it: SELKIES_ALLOWED_ORIGINS.)
#
# Mounted as /custom-cont-init.d/10-loopback-only.sh of the browser container (executable): the image runs these scripts at start, after it has built its nginx configuration and before nginx starts.
# Tested with the pinned image: after it, `ss -ltn` shows nothing off the loopback, and a request with another origin gets a 403. A different image or a different version of it needs the test again.
# If it does not do its job, Geminarr says so: it reads the sockets of the shared namespace and closes /browser/ (reason browser-exposed) while anything but its own port listens off the loopback.
set -eu
conf=/etc/nginx/sites-available/default
sed -i -e 's/^\([[:space:]]*\)listen 3000/\1listen 127.0.0.1:3000/' -e 's/^\([[:space:]]*\)listen 3001/\1listen 127.0.0.1:3001/' -e '/listen \[::\]/d' "$conf"
if grep -E '^[[:space:]]*listen' "$conf" | grep -v -q '127\.0\.0\.1'; then
  echo "loopback-only: a listen directive is still not on the loopback" >&2
  exit 1
fi
RULE='if ($http_origin !~ "^(https://geminarr\.invalid)?$") { return 403; }' awk '{ print } /^[[:space:]]*listen 127\.0\.0\.1:/ { print "  " ENVIRON["RULE"] }' "$conf" > "$conf.new"
cat "$conf.new" > "$conf"
rm -f "$conf.new"
if [ "$(grep -c 'geminarr' "$conf")" != "$(grep -c -E '^[[:space:]]*listen 127' "$conf")" ]; then
  echo "loopback-only: the origin rule is not in every server block" >&2
  exit 1
fi
