# 0025 — A conversation is read from its end, as far back as Geminarr needs

- **Status**: accepted
- **Date**: 2026-11-19
- **Spec**: D-33 (SPEC §6.2; amends [ADR 0022](0022-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md) on how a conversation is read)

## Context

Sending is incremental since the beginning: Geminarr sends only the lines that Mnemarr does not hold (ADR 0014, the positions). Reading was not: every time a conversation had moved up the list (and the most recent one at every pass), the extension scrolled it from its end to its beginning and handed over all of it, to have all but the last turns thrown away. The cost grows with the length of the conversation, for turns that nothing waits for, and a continued conversation costs as much as a new one.

The assistant keeps only the turns near the screen in the page, so a conversation cannot be read at once; it can only be read from its end, backwards. And Mnemarr's positions count lines from the *beginning*: the end of a conversation says nothing of where it belongs unless it can be tied to what was sent.

## Decision

1. **The anchor.** For every conversation, Geminarr keeps the **anchor**: a short hash of its last three turns, as they were sent (masked, with their roles). Three, not one: the same short answer comes back in a conversation, three turns in a row do not. A conversation shorter than three turns has the whole of it for anchor. The anchor is a new column of the `conversations` table (schema step 4); a conversation seen before has none, and is read whole once.
2. **Geminarr says which conversations can be read from their end.** In the answer to a list, the conversations it asks for come with `anchored`: those that Geminarr holds whole (it has an anchor, everything it knows was sent, nothing waits for Mnemarr). The extension marks the page of such a conversation.
3. **The extension reads the end, then asks.** For an anchored conversation it reads from the end until it holds ten turns (or reaches the beginning), and hands them over as a **part** (`complete: false`). Geminarr looks for the anchor in them (the latest place first). If it finds the three turns, the part joins what it holds: the position of its first turn is known (`known − 3 − index`), it keeps the end, answers `more: false`, and the page is done. If it does not (the last answer was regenerated, a turn was removed, the new turns are more than the part holds), it answers `more: true` and the extension goes on from where it stopped, to the beginning, and hands the whole conversation over, as before. A conversation that Geminarr does not hold, or that has no anchor, is read whole straight away. A part is never kept if it does not join.
4. **The reader says where the part begins.** `IConversationReader.OpenAsync` gives a `ConversationContent`: the turns and the position of the first of them (0 for a whole conversation). The pass queues the lines after what Mnemarr holds; if what it needs is before the part (Mnemarr holds less than was believed), nothing is queued and the anchor is forgotten, so that the next reading is a whole one. The anchor is renewed at every reading from the last three turns of the whole conversation.
5. **Nothing changes for what is sent.** The positions, the batches and the masking are those of ADR 0014 and SPEC §5; a rewritten turn inside the anchor is seen (the anchor does not join, the whole conversation is read, the rewrite is counted); a rewrite before it is not seen, as before (only the last turn that was known was ever compared).

## Alternatives considered

- **Read the whole, as before**: the cost of a continued conversation is its whole length, and a conversation that took more than the tab lives would never be read again.
- **Identify a turn by an identifier of the page**: no such identifier is known to be stable, and Geminarr would depend on it.
- **One turn for the anchor**: a short answer that comes back ("ok", "thanks") would match at the wrong place and drop or double turns.
- **Send every part and let Geminarr cut**: it is what is done, but only for the end that was asked for: the part is a bounded window, and the extension does not send the whole conversation for each probe.

## Consequences

- A conversation continued two days later costs the screen or two that hold the new turns, whatever its length. The first reading of a long conversation still goes to its beginning, and once only.
- Two small risks, accepted: three turns in a row that are identical in the end of a conversation would be taken for the anchor in the latest place (a conversation of the same three turns said twice in a row); and a turn rewritten before the anchor is not seen. Both are as rare as they are harmless to what Mnemarr holds (it is never sent anything twice).
- The protocol has two additions that an older extension ignores (`anchored`, `more`) and one that an older Geminarr ignores (`complete`): the two sides can be updated in any order; a part that an older Geminarr takes for a whole conversation would be at the wrong positions, which is why a Geminarr that does not know `anchored` never asks for a part.
