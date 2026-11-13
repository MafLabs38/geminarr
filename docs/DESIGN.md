# Geminarr — Design and Frontend Guidelines

> **Status**: v1, 2026-11-11. Mandatory for all UI code (human- or AI-written).
> Geminarr follows **[Portarr's DESIGN.md](https://github.com/MafLabs38/portarr/blob/98458ee1f80e140cf15adab2416a386970c7bfc6/docs/DESIGN.md)**, pinned to the commit of 2026-10-28 so that a later edit in another repository cannot change these rules without a review here (the *arr page anatomy, component vocabulary, design tokens, accessibility rules and pull request checklist), exactly as Mnemarr does. This page records what is specific to Geminarr. Where nothing is said here, the pinned document applies; the decisions it cites (D-xx, V7) are Portarr's own and do not apply here.

## 1. What is shared with Portarr and Mnemarr

- The **page anatomy**: a header (56 px), a sidebar (220 px) and a page content with a heading, as in Sonarr.
- The **theme tokens**: same names and values (`--paper`, `--paperCard`, `--ink`, `--accent`, `--shadowCard`…), in `frontend/src/Styles/Themes/themes.css`. A component never carries a hard-coded color, font, radius or shadow.
- The **fonts** (Fredoka, Nunito, JetBrains Mono), self-hosted through `@fontsource`: the interface makes no request to a third party.
- The **code conventions**: CSS Modules, one component per file, absolute imports from `src`, icons only through `Helpers/Props/icons.ts`, the `kind` vocabulary, every text through `translate()`.
- **Server data only through `useApiQuery`** (`Helpers/Hooks`), which goes through `apiGet` (`Helpers/Api`): never a `fetch` in a component. The write path (`apiSend`, a mutation hook next to the feature's query hooks, the `X-Geminarr-Request` header the server will enforce, F-ACC-04) arrives with the first vertical that writes.

## 2. What is specific to Geminarr

### 2.1 The interface watches and signs in; it never shows a conversation

Geminarr's page tells the operator whether it works and lets them act on the connection. It **never displays the text of a conversation** (Mnemarr is where conversations are read, D-06). Its menu:

| Entry | Content | State |
|---|---|---|
| Overview | Is Geminarr up (the API answers), and the **health**: each check with its state and, when something is wrong, the sentence of its reason code. Later: the session, Mnemarr, the last runs | built (API state, health) |
| Settings | The settings the container runs with, read only, a secret as *set* or *not set* | built |
| Connection | The state of the session, *Sign in* (the live view), *Pause*, *Disconnect* | soon (vertical 3) |
| Conversations | Metadata only: title, turns known and sent, rewritten turns, state | soon (vertical 4) |
| Runs | Outcome, counts, reason of each pass | soon (vertical 4) |
| Import | Upload a Takeout archive, see the result | soon (vertical 5) |
| Mnemarr | The destination, its state, pairing | soon (vertical 2) |
| System | Version, runtime, start and uptime | built |

An entry whose page is not built is **shown, marked "Soon", and is not a link** (`aria-disabled`). It becomes a link in the pull request that builds its page.

### 2.1a The sign-in comes first

Nothing of the application is shown, and nothing is asked of the API, until somebody is signed in: the sign-in page is the first screen, a failed call answered 401 puts it back and forgets what was read, and the answer to a wrong name and a wrong password is the same sentence. Who is signed in is in the header, with the way out.

### 2.2 States are never conveyed by color alone

The connection and the health use the `StatusLabel` component (icon, text and color). The states and reason codes of SPEC §7.2 and §10.3 each have a sentence in `Utilities/String/en.ts`; the page maps codes, it does not parse English.

### 2.3 The live view

- It is a **canvas** inside a page of the application, not an iframe, with a visible *Close* and a countdown of the ten minutes. The canvas is focusable and labelled; while it has the focus the keyboard goes to the remote browser, and a documented key combination gives the focus back to the page (an accessibility requirement: a keyboard user must be able to leave it).
- It says in words what it is ("You are controlling the browser of the Geminarr container") and which account it expects ("Sign in with the dedicated account, not your main one").
- Frames are never stored; nothing in the DOM contains what is typed.

### 2.4 The brand

No logo yet: the header shows the name as text. Playfulness is for the overview and empty states ("Nothing to collect yet."), never for errors or alerts.

### 2.5 Not there yet

The light theme, the color-impaired mode and the forms arrive with the verticals that need them. Until then there is only the dark theme.

## 3. Pull request UI checklist

The checklist of Portarr's document applies, with this addition: a new menu entry is added to `frontend/src/App/navigation.ts` as unavailable, and turned on in the pull request that delivers its page; and **nothing a conversation said ever reaches the DOM, a log or a screenshot kept in the repository**.
