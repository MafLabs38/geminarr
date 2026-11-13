# Configuration

Geminarr is configured **only by environment variables and files**, so that a whole deployment is a manifest ([ADR 0007](adr/0007-geminarr-is-described-as-code-at-three-levels.md)). A setting is an environment variable prefixed `GEMINARR__`; a double underscore is a level (`GEMINARR__READER__POLLMINUTES`). The `GEMINARR__*` names are the supported contract (they are read after `appsettings.json`, so they win over it); the command line is not a channel for settings, and a secret, or the path of one, never goes there (arguments are visible to every process of the host). A secret is given as a **file** (`…FILE`), never as a value. The defaults are in bold; a setting marked with a vertical number does not exist before that vertical (the full list and its meaning are in [SPEC.md §10.2](SPEC.md#102-settings)).

## The container and the page (verticals 0 and 1)

| Variable | Default | Purpose |
|---|---|---|
| `GEMINARR__DATAFOLDER` | **`/config`** | Where the database and the settings live. A volume; the image's user (uid 1654) must be able to write it. The readiness probe fails when it is missing or read-only. |
| `GEMINARR__URLS` | **`http://+:8282`** | The address the container listens on. Plain HTTP: publish it through a reverse proxy with TLS and a login in front. The prefixed variable is the one that takes effect (it is read after `appsettings.json`); `ASPNETCORE_URLS` does not. |
| `GEMINARR__PUBLICURL` | none | The address of the page as the operator reaches it, for example `https://geminarr.homelab.example` (http or https, a host, no user name). Its host is allowed; it is also the address that goes in the signal that asks for a sign-in. A value that is not one stops the start. |
| `GEMINARR__ALLOWEDHOSTNAMES` | **`localhost;127.0.0.1;[::1]`** | The names the page may be reached by (separated by commas, semicolons or spaces; a port is ignored). Any other `Host` is refused (400), the probes excepted: see [network.md](network.md). `*` allows every name (not recommended). Not the `AllowedHosts` key of ASP.NET, which stays `*`. |
| `GEMINARR__AUTH__ADMINUSER` | none | The name of the operator. **Choose one that is not obvious**: whoever knows it can lock it out for fifteen minutes (five wrong passwords). |
| `GEMINARR__AUTH__ADMINPASSWORDFILE` | none | The file that holds the password of the operator, at least twelve characters (a mounted secret, never an environment variable). It is read at each sign-in: rotating the secret needs no restart. The name and the file are set together or not at all; **without them nobody can sign in** (the container stays alive and closed, and the health says `operator-not-configured`). |
| `GEMINARR__AUTH__COOKIESECURITY` | **`Always`** | `Always`: the session cookie is `Secure` and named with the `__Host-` prefix (it needs HTTPS). `SameAsRequest`: it is `Secure` only if the request was: **a plain-HTTP sandbox only**. |
| `GEMINARR__AUTH__SESSIONHOURS` | **`12`** | How long a session lasts, used or not, from 1 to 720 hours: the operator signs in again after it. |
| `GEMINARR__IDENTITY__NAME` | the product's name (`Geminarr`) | The name of this environment, shown at the bottom of the menu (one line, at most 40 characters). |
| `GEMINARR__IDENTITY__LOGOFILE` | none (the product's two rings) | The path of an image (png, webp or svg of at most 1 MiB, checked by its content) mounted into the container: the picture of the menu. A file that is not usable is not used, and the Settings page says so without its path. |
| `GEMINARR__IDENTITY__COLORS__NAME` / `…COLORS__VERSION` | `accent` | The color of the name and of the version: `#rgb`, `#rrggbb` or a theme token (`accent`, `accentDeep`, `secondary`, `ink`, `inkSoft`, `inkMuted`). |
| `GEMINARR__MNEMARR__URL` | none | The Mnemarr to send to (https; a loopback address is also accepted). When it is not set, the operator gives the address on the page *Mnemarr* and it is kept in the database. When it is set, the page does not change it. |
| `GEMINARR__MNEMARR__MACHINE` | **`gemini`** | The name of this machine in Mnemarr: 1 to 40 lower-case letters, digits or hyphens. A pairing lets the person who approves it name the machine, and that name wins. |
| `GEMINARR__MNEMARR__KEYFILE` | `<data folder>/mnemarr/key` | Where the key of the machine is kept: a file only the container's user can read. A key that was given as a file (mounted from a sealed secret) is used as it is. **The key is never in the database, a log or an answer of the API.** |
| `GEMINARR__MNEMARR__ENROLMENTCODEFILE` | none | A file with a one-time code from Mnemarr (a deployment as code): exchanged for the key at start, when there is none. |
| `GEMINARR__MNEMARR__ALLOWINSECUREHTTP` | **`false`** | A sandbox only: talk to Mnemarr over plain http to a host that is not the loopback. |
| `GEMINARR__READER__KIND` | **`None`** | `None` (not connected to any assistant: a pass sends what waits and says so), or `Demo`: three invented conversations that grow, **for a sandbox only**. The page says when it is on. |
| `GEMINARR__READER__POLLMINUTES` | **`20`** | Minutes between two readings (a random 20 % either way is added; doubled for each pass that failed in a row, up to `MAXBACKOFFMINUTES`). |
| `GEMINARR__READER__MAXLISTED`, `…MAXOPENPERPASS` | **`40`**, **`5`** | How many conversations of the list are looked at, and how many are opened in a pass. |
| `GEMINARR__READER__PASSTIMEOUTMINUTES`, `…MAXBACKOFFMINUTES` | **`5`**, **`360`** | Reading never lasts longer than the first; a failing reader is read again after at most the second. Whatever the poll interval, the machine says it is alive at least every 10 minutes. |
| `GEMINARR__READER__LOGINFAILURES` | **`3`** | Passes in a row that find nobody signed in before the state is `needs-login`. |
| `GEMINARR__READER__DEMOGROWSECONDS` | **`60`** | The demonstration reader only: seconds between two new turns. |
| `GEMINARR__RETENTION__RUNSDAYS` | **`90`** | How many days the runs are kept. |
| `Logging__LogLevel__Default` | **`Information`** | Standard ASP.NET logging. The logs carry ids, counts, durations and reason codes, **never** the text of a conversation, a password or a cookie. |

The `GEMINARR__*` names are the supported contract (they are read after `appsettings.json`, so they win over it); the command line is not a channel for settings, and a secret, or the path of one, never goes there (arguments are visible to every process of the host).

**Sessions are in memory**, with the keys that sign the cookies: a restart signs everybody out, and a lockout is forgotten (restarting is the way to lift one early). The page *Settings* shows what the container runs with, read only.

## From the next verticals

| Variable | Vertical | Purpose |
|---|---|---|
| `GEMINARR__PROFILEFOLDER` | 3 | The browser profile: **a volume of its own**, mode `0700` |
| `GEMINARR__READER__BACKFILLPERPASS`, `…RATELIMITPAUSEHOURS` | 4 | Backfill and the pause after a block |
| `GEMINARR__IMPORT__*`, `GEMINARR__IMPORTFOLDER` | 5 | The Takeout import |

Every limit and interval comes from configuration with a documented default; none is a literal in the code, except the fixed security policy of the sign-in and an internal cache window, which are stated in [SPEC.md §10.2](SPEC.md#102-settings). A setting that cannot work stops the start with a sentence that names it.
