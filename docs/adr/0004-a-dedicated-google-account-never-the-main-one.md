# 0004 — A dedicated Google account, never the main one

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-08, D-09, D-10; SPEC §7, §11

## Context

Without an API, Geminarr reads Gemini as a signed-in user, so the container must hold a signed-in session. A Google session is not limited to one application: its cookies are valid across the account's services, so whoever holds the profile can open the mailbox and, through password resets, reach other services. The maintainer's main account holds the mailbox and the saved passwords. An attacker who got the profile would also bypass 2-step verification.

## Decision

1. Geminarr signs in to a **dedicated personal Google account** that shares the maintainer's Google AI plan through the family group (the plan's own family sharing), and from then on the maintainer uses that account for Gemini on the phone and on the PC. A Brand Account is not isolation (it is reached from the main account).
2. The account holds only Gemini conversations, has a strong password kept outside Google's password manager and 2-step verification on the phone, and is never the recovery address of another account.
3. **Read-only on the assistant's own pages**; the only other origin the browser may load is the account provider's sign-in, during the live view.
4. The profile is the crown jewel (SPEC §11): own volume, `0700`, never logged, excluded from clear backups; *Disconnect* destroys it.
5. **The past** comes by the least exposed way that is complete enough: Google Takeout (no session); else extraction from the maintainer's own browser (text only); else, last resort and with the maintainer present, an **ephemeral** sign-in of the container to the main account (SPEC §11.4).

## Alternatives considered

- Reading with the main account's session: rejected, the impact of a leak is the whole account.
- Takeout only: kept for the past, too slow (days between exports) for the present.

## Consequences

- Conversations the maintainer holds on the main account after the switch are not captured.
- Reading a consumer product by automation is against the provider's terms; the risk is accepted on the dedicated account only.
- The group's rules for changing family group are the maintainer's to check before joining.
