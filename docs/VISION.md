# Geminarr — Vision

> Why this exists, in the maintainer's own terms, then what it must do. The detail is in [SPEC.md](SPEC.md).

## The need

The maintainer talks to Gemini every day, mostly by voice on the phone, sometimes on the PC, to clarify ideas, get news and technical details, and to think out loud about things that are not (yet) projects. Some of those conversations hold a good idea (a distributed AI, one day), and the idea stays locked in the product: no API gives access to the history, and a month later it cannot be found.

[Mnemarr](https://github.com/MafLabs38/mnemarr) is the family's long-term memory: it collects conversations from the machines where assistants run, stores them as notes and finds them again **by meaning** and by text. If the Gemini conversations were there too, an idea could be found a year later by describing what it was about.

## What it must do

1. **Bring the Gemini history into Mnemarr, automatically**, as it grows (the phone and the web share one history), without the maintainer doing anything after the first sign-in.
2. **Bring the past too**, once, by the safest means available.
3. **Keep the conversations raw.** They are an idea repository: no summary, no structure imposed. Found by meaning, readable as written, like in Google's own history.
4. **Never put the main Google account at risk.** Reading Gemini means holding a signed-in session, and a session is the whole account: so it is a dedicated account that shares the maintainer's plan.
5. **Say when it needs a person**: when the session ends, ask for a sign-in again, by mail, with a link, and nothing more.
6. **Be one more *arr** in the family: one container, an API, a small page, the same quality chain, described as code and deployed by GitOps like the others.

## What it must not do

- Remember, search, summarise or consolidate (that is Mnemarr).
- Write to Gemini, or visit anything but the assistant's own pages.
- Keep a second copy of the conversations.
- Hold a mail credential, or the key of anything but its own machine in Mnemarr.

## Raw notes of the maintainer (2026-11-11), translated

- "I use Gemini and the Lite model for a lot of things, a lot of nonsense too. I'd like all that history to land in Mnemarr, because sometimes I have good ideas and it would let me bounce on them."
- "Gemini exposes no API for its notes. So a scraper that signs in with my account, reads the history, and puts it in notes in Mnemarr."
- "The way I handle Claude Code notes, a directory per machine, can fit. Mnemarr should not care how the data comes; the container holds the logic, exactly like a machine."
- "These are not building something: no specs to extract. Index them by meaning, keep them raw, skip the summary."
- "The session will last a while; when it can no longer connect, send me a mail so I sign in again and a new token exists."
- "Use a separate account that benefits from my Google AI plan, so my main account (all my passwords) cannot be compromised."
