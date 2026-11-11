# Configuration

Geminarr is configured **only by environment variables and files**, so that a whole deployment is a manifest ([ADR 0007](adr/0007-geminarr-is-described-as-code-at-three-levels.md)). A setting is an environment variable prefixed `GEMINARR__`; a double underscore is a level (`GEMINARR__READER__POLLMINUTES`). A secret is given as a **file** (`…FILE`), never as a value. The defaults are in bold; a setting marked with a vertical number does not exist before that vertical (the full list and its meaning are in [SPEC.md §10.2](SPEC.md#102-settings)).

## Foundation (vertical 0)

| Variable | Default | Purpose |
|---|---|---|
| `GEMINARR__DATAFOLDER` | **`/config`** | Where the database and the settings live. A volume; the image's user (uid 1654) must be able to write it. The readiness probe fails when it is missing or read-only. |
| `GEMINARR__URLS` | **`http://+:8282`** | The address the container listens on. Plain HTTP: publish it through a reverse proxy with TLS and a login in front. The prefixed variable is the one that takes effect (it is read after `appsettings.json`); `ASPNETCORE_URLS` does not. |
| `Logging__LogLevel__Default` | **`Information`** | Standard ASP.NET logging. The logs carry ids, counts, durations and reason codes, **never** the text of a conversation or a cookie. |

## From the next verticals

| Variable | Vertical | Purpose |
|---|---|---|
| `GEMINARR__PUBLICURL` | 1 | The address of the page as the operator reaches it (goes into the link of the "sign in again" mail) |
| `GEMINARR__AUTH__ADMINUSER`, `…ADMINPASSWORDFILE`, `…COOKIESECURITY` | 1 | The first administrator; the cookie is `Secure` unless told otherwise (sandbox only) |
| `GEMINARR__MNEMARR__URL`, `…MACHINE`, `…KEYFILE`, `…ENROLMENTCODEFILE`, `…ALLOWINSECUREHTTP` | 1 | The Mnemarr to send to and how Geminarr joins it |
| `GEMINARR__PROFILEFOLDER` | 2 | The browser profile: **a volume of its own**, mode `0700` |
| `GEMINARR__READER__*` | 2, 3 | Intervals, limits, backoff, backfill |
| `GEMINARR__IMPORT__*`, `GEMINARR__IMPORTFOLDER` | 4 | The Takeout import |
| `GEMINARR__RETENTION__RUNSDAYS` | 3 | How long runs are kept |

Every limit and interval comes from configuration with a documented default; none is a literal in the code. A setting that cannot work stops the start with a sentence that names it.
