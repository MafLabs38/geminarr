# 0026 — One account signed in is one machine of notes

- **Status**: accepted
- **Date**: 2026-11-20
- **Spec**: D-35, §8; with [ADR 0027](0027-any-account-may-be-signed-in-and-there-is-no-import.md) (D-34: no account is forbidden, no import)

## Context

The maintainer wants to read two Google accounts with the one Geminarr: the main one, read for its history and then signed out, and another one that stays signed in and goes on being read. What each says must go to a machine of its own in Mnemarr, as if two machines were connected to two clouds. Until now Geminarr had one destination (one machine, one key: the table has a single row), one set of conversations, and a rule that the account was "the dedicated one": a connection of the main account would have put everything in the same machine, with no way to part it.

## Decision

1. **The account that is signed in selects a workspace.** The account is read from the label of the account button (already read for the Connection page) and told apart by the hash of its lower-cased address. Everything that belongs to an account lives in `accounts/<key>/`: its database (conversations, outbox, runs, destination: the same schema) and its Mnemarr key. The root database keeps the state of the connection, which is not an account's.
2. **One machine per account, named from its address, paired by asking.** The name is `geminarr-` and the part before the `@` (sanitised, at most 40 characters, a short suffix of the key if the name is taken). Nobody types it. The pairing is the existing one, for the active account. The address of Mnemarr stays one.
3. **A pass works on the workspace that was active when it began** (an ambient account context that follows the awaits), so that a switch or a sign-out in the middle of a pass never writes to the other workspace. With no active account the stores answer from an empty database that is thrown away, and a write that an operator asks for is refused.
4. **Nothing is read for an account until its machine is paired**, and only the active account sends a heartbeat.
5. **Switching is _Disconnect_ then _Sign in_.** *Disconnect* deletes the cookies, so every account is signed out. Two accounts signed in at once are not supported. An account that is signed out is not read.
6. **The Connection, Mnemarr and Conversations pages know the accounts**, and an account can be forgotten (its workspace deleted; Mnemarr is not touched).
7. **No account is forbidden**, and nothing is imported ([ADR 0027](0027-any-account-may-be-signed-in-and-there-is-no-import.md)).

## Alternatives considered

- **An `account` column in every table.** Smaller at first, but every query and every cache would have to carry it, and the first one that forgets mixes two accounts' notes: the very failure to avoid. A workspace makes a mixture impossible by construction, and leaves the code of a pass as it is.
- **One Geminarr (one browser) per account.** Two deployments, two browsers to keep, for a person who has one at a time.
- **The machine name typed by the person.** One more thing to get wrong (a name already used replaces the key of the other machine in Mnemarr), for nothing: the address says which account it is.
- **Keeping the two accounts signed in at once** (Google's account chooser): the page that is read would have to be told which account to show, which no part of the reader knows today.

## Consequences

- Storage is split in two (a root database, a database per account). The singletons that kept state of their own (the link, the positions asked of Mnemarr, what the extension holds) are keyed by the account or dropped when it changes.
- What waits for Mnemarr in the workspace of an account that is signed out is sent by the pass of the account that is signed in, as that account. Each account is a "computer" of its own for Mnemarr (`computerId`, a hash of the key): Mnemarr refuses to give the machine of one account to another that asks for the same name.
- The machine of an account that is signed out stays in Mnemarr, silent. It is better made *intermittent* when it is approved there, so that it raises no alert.
- A key that the deployment gives as a file is one key, for one machine: it serves the one account that exists, and a second account is not linked to it (a line in the log says so; pair it instead). Forget the first account and the second one is served.
- The account that is **active** is the one signed in or, from the end of a session that expired until *Disconnect*, the one that was signed in last: its machine goes on telling Mnemarr, which sends the mail. A session signed in to an account that cannot be named is no account (nothing is read for it).
- **A data folder from before accounts is not adopted.** The root database no longer holds conversations, an outbox or a destination, and `<data>/mnemarr/key` is not read: the first account that signs in starts a workspace and is paired as a machine of its own. Nothing was released with data in it (the first deployment was never signed in), so no move was written; an operator who has such a folder re-pairs, and what Mnemarr already holds stays under the old machine.
- The name of the machine keeps the part of the address before the `@` on disk and in Mnemarr; the address itself is kept in memory only.
- A large change in one pull request, as the maintainer asked; the micro-spec and the plan of the work were kept outside the repository to hold the thread.
