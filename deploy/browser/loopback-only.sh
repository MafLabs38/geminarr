#!/bin/sh
# Makes the web desktop of the browser listen on the loopback of the container's network namespace, and nowhere else (ADR 0017).
#
# The image's nginx listens on every interface (3000 in clear, 3001 with a self-signed certificate). The web desktop has no sign-in of its own and must be reached by Geminarr alone, which shares the
# namespace and reaches it as 127.0.0.1: without this, any other container of the same network (or any other pod of the cluster, if no network policy is enforced) could open a desktop that is
# signed in to a Google account. Mounted as /custom-cont-init.d/10-loopback-only.sh of the browser container (executable): the image runs these scripts at start, after it has built its nginx
# configuration and before nginx starts. Tested with the pinned image: after it, `ss -ltn` shows 127.0.0.1:3000 and no 0.0.0.0:3000. A different image or a different version of it needs the test again.
set -eu
conf=/etc/nginx/sites-available/default
sed -i -e 's/^\([[:space:]]*\)listen 3000/\1listen 127.0.0.1:3000/' -e 's/^\([[:space:]]*\)listen 3001/\1listen 127.0.0.1:3001/' -e '/listen \[::\]/d' "$conf"
if grep -E '^[[:space:]]*listen' "$conf" | grep -v -q '127\.0\.0\.1'; then
  echo "loopback-only: a listen directive is still not on the loopback" >&2
  exit 1
fi
