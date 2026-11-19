# 0022 — The reader is an extension in the browser that a person signed in to

- **Status**: accepted
- **Date**: 2026-11-18
- **Spec**: D-30 (supersedes D-16); D-08, D-09, D-11 still hold

## Context

The need (VISION): bring the history of Gemini into Mnemarr, read-only, robustly. Spike S1 settled what can read a signed-in Gemini from a container. Driving a browser from Geminarr through its **debugging port** works, but whoever holds that port holds the browser: every cookie of the signed-in account, every page, any script. It is the only way to use a browser from outside, and it makes the process that talks to Mnemarr and the internal network the keeper of a Google session. The maintainer rejected it ("a scraper must not go through a debugging port"). The browser itself, though, already holds the session, and **a person signs in to it by hand**, as in any browser.

## Decision

1. **An extension of that browser reads the page and pushes what it reads to Geminarr** (`POST /api/v1/extension/push`). Geminarr drives nothing and holds no session: the browser has no debugging port, and Geminarr never sees a cookie. The reader (`ExtensionReader`) serves the pass from what the extension handed over; a silent extension is a reader that cannot say (`extension-silent`), never a session that is gone. Only the word of the extension says that nobody is signed in.
2. **The extension is ours** (`extension/`), small and readable: it asks for the permissions `storage`, `alarms` and `browsingData` (the last only deletes the cookies of its browser, when the operator disconnects; it reads nothing) and for **one host, the assistant's origin**. It opens the list and the conversations in background tabs, reads them as a person sees them, scrolls the list, submits no prompt and clicks nothing, and closes only the tabs it opened. It talks to one address, the one in its browser's policy.
3. **It is installed by the browser's enterprise policy** (`ExtensionSettings`, `force_installed`, an `update_url` served by Geminarr), not as a developer's unpacked extension. The policy also carries its settings (`3rdparty`: the address of Geminarr and the token; the pace of the rounds is Geminarr's, `GEMINARR__EXTENSION__ROUNDMINUTES`) read through `chrome.storage.managed`. The address and the token are in no file of the extension.
4. **Geminarr serves and signs the package**: the image builds a signed CRX3 (`tools/extension/pack.mjs`, Node alone, reproducible), and `GET /api/v1/extension/update.xml` and `/package/<file>` serve it without a session (they hold no secret). **The signing key is a build secret** (`EXTENSION_SIGNING_KEY`): the extension id is a function of the public key, so a release signed by another key is another extension; the pipeline refuses to publish a release without it. A build without the key uses an ephemeral one (a sandbox).
5. **The door is closed except for the extension.** The push route is open to the session check (the extension has no session) and requires **a bearer token** (a file of the deployment, at least 32 characters, compared as hashes). The answer is readable by one origin only, `chrome-extension://<id>`, the id being the package's. The header of a write (`X-Geminarr-Request`) is required like everywhere. A push is bounded in size and shape (ids, roles, lengths, counts) and the answer never repeats what it was told.
6. **A round is the extension's doing, but Geminarr sets the pace; a pass is Geminarr's.** The extension says that it is there every half minute and does a round when Geminarr answers that one is due (`GEMINARR__EXTENSION__ROUNDMINUTES`, 15, or a sign-in is waited for, or the operator asked), and at the start of the browser; it says what it saw; Geminarr answers a list with the conversations it wants read (new, moved up or renamed, at most `MaxOpenPerPass`); a conversation pushed asks for a pass at once; a change of "signed in" does too. The inbox holds the text only until the pass takes it (in memory: a restart loses nothing that matters, the next round brings it again), and the outbox stays the only durable copy (D-06).
7. **The extension is also how Geminarr knows the session and how the operator ends it.** It reads the label of the account button (who is signed in) and pushes it with the list; it **says that it is there every half minute** (`hello`, answered with whether a round is wanted now and whether the account is to be signed out), so that the end of a sign-in that the operator started is noticed within a minute and the first word after a restart does not wait for the hour; and it **deletes the cookies of its browser** when the operator disconnects (the permission `browsingData`: it only deletes, and the browser holds no other account). A session that is gone because the operator asked is not a login that expired (no sign-in is asked for, no mail). What the page showed is never logged, and the account is shown to the operator only.
8. **The selectors are in the extension.** A change of Gemini's page is a new version of the extension, which the browser takes within hours (its own update check is about every five hours; the extension also asks every hour) or at once by reinstalling through the policy.

## Alternatives considered

- **Drive the browser over its debugging port**: rejected for the reason above. It also cost a relay, an egress guard and a ticket for every operation a person does by hand.
- **A headless browser of Geminarr's own with the session in Geminarr's volume**: Geminarr would hold the crown jewel (D-09), and Google signs in poorly from such a browser.
- **The unofficial web API (the internal calls of the page)**: no browser to keep, but it breaks without notice, needs the cookies in Geminarr, and is the kind of use a provider polices.
- **Selectors served by Geminarr as data** (so that a change of the page needs no new extension): rejected for now. The image carries the selectors anyway, so a change is a release either way; the only gain is the few hours of the browser's update check, and a remote selector is a remote instruction for a script that runs on the account's pages.
- **The extension unpacked, in developer mode**: refused by the browser for a deployment, and a person can switch it off.

## Consequences

- Two containers in a deployment (the browser and Geminarr), which only need to reach each other over HTTP; **nothing of the session crosses that line**. Signing in to the browser's account is a person's act in that browser, through the desktop that Geminarr relays on its Connection page (ADR 0016, 0017, 0019).
- A deployment needs three things from the maintainer: the signing key (a repository secret), the token (a secret of the deployment, in Geminarr and in the browser's policy), and the policy file.
- **A long silence is a sentence, not a mail** (Q-14): a browser that is off for weeks shows as `failing` on Mnemarr's machines page with its sentence; only `needs-login` and `reader-broken` send the attention mail, and they are Mnemarr's closed list. Whether a silence should join them is Q-14, to decide with the first weeks of running.
- The inbox holds only what Geminarr asked for (the answer to a list), for an hour at most, and nothing once nobody is signed in: the text of a conversation that no pass takes does not stay.
- The extension reads **only the pages that it opened** (it asks its worker first): a page that a person opened is theirs.
- After Geminarr restarts, the connection says `extension-silent` until the extension's next `hello` (half a minute at most), which asks for a round; a browser that is off says it too, after `Extension:SilentAfterMinutes`.
- The voice conversations, a very long list (the list is scrolled up to a bound) and the lifetime of the session are measured with the real use, not here (Q-04, Q-05).

## Amendment, 2026-11-19: the closed list of pages the browser's policy lets through

The first real sign-in (the maintainer's, on the sandbox) showed that the policy, which allowed only the assistant and `accounts.google.com`, also blocked what a sign-in and the provider's own check need: the provider ends a sign-in with a page on its domain of the person's country, and may ask a person to solve a check. A person could not finish. **The maintainer decided on 2026-11-19 that these pages must not be blocked** ("il faut l'autoriser"). The list is closed, each entry has a reason, and a new kind of page is a decision (an amendment here and a change of `deploy/browser/policy.example.json` and `docs/network.md` in the same pull request):

| Host | Why |
|---|---|
| `gemini.google.com` | The assistant (D-30). |
| `accounts.google.com` | The sign-in page. |
| `accounts.google.<country>` | The provider sets its cookies on the domain of the person's country at the end of a sign-in (observed: `.de` for a browser set to German); without it the browser shows "This page is blocked" at the last step. **The rule is the decision**: the sign-in domain of the provider for the locale of the browser, one entry per locale the operator uses (the example carries a placeholder to fill in), and no other domain. |
| `www.google.com/sorry` | The provider's "unusual traffic" page: a person solves its check by hand in the desktop. |
| `www.google.com/recaptcha`, `www.gstatic.com/recaptcha`, `www.recaptcha.net` | What that check loads. |

Nothing else: the policy still blocks every other address, and the browser still holds only the dedicated account.

## Amendment, 2026-11-19: a conversation is read whole, in a window of its own, and without what the page says to a screen reader

Comparing what Mnemarr held with the conversations of the assistant, one by one, on the real account, showed two defects of the reader, both found by a person and not by a test:

- **Conversations without their beginning.** The assistant keeps in the page only the turns near the screen and brings the older ones in as the conversation is scrolled up, and it does so only for a page that is *shown*: a page of a tab in the background is not. The extension read what the page showed when it had loaded, so a conversation came with its last two turns only (a conversation of four turns, and another of four, were both sent as two). Sent like that, the turns would also have been at the wrong positions in Mnemarr, whose positions count lines from the beginning.
- **A turn that began with "You said" and was said twice.** The page puts a label and a summary of the message, for a screen reader only, in the element of the turn; the text of the element took them.

What the reader does now:

1. **A conversation is read in a window of its own, not focused** (`chrome.windows.create`, `focused: false`), closed once read. The list is still read in a tab that is not active; a tab that a person looks at (a sign-in) is never taken for a read. The window is the only place where the extension shows something on the browser's desktop, for the seconds that a conversation takes; no new permission is needed.
2. **It is read from its end to its beginning**, a window of the page at a time (each step a share of the height of the screen, so that the windows overlap), and joined by the turns that two windows have in common. It is handed over only once the top has stayed the top for a few looks; a conversation whose beginning cannot be reached (the page stops following the scroll, or the four minutes of the read are over) is given up (`giveup`), never sent in part. There is no bound on the number of screens: a conversation is only text. A tab lives five minutes at most (it was three).
3. **What the page says to a screen reader only is left out** of the text of a turn (the elements marked `visually-hidden` or `sr-only`).

Consequence to know: reading a conversation takes as many steps as it has screens (a third of a second each); only one that would need more than four minutes is not handed over.
