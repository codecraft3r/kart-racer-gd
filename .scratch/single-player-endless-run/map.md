# Decide and build the Endless Run

Label: wayfinder:map

## Destination

A playable, verified offline Endless Run built from a decision-complete product and technical design: consecutive 2–3 minute Shifts with rising Cash Quotas, brief between-shift choices, escalating Rival pressure, and a run-ending quota failure.

## Notes

- Domain: PAIN TAXI. Use the language in [CONTEXT.md](../../CONTEXT.md).
- Every session should consult [Tactical Taxis: Game Design Document](../../docs/TacticalTaxis_GDD.md) and inspect the current implementation before deciding.
- Favor immediate arcade readability, meaningful risk/reward at driving speed, and reuse of the working solo foundation.
- The Cash Quota is the only pass/fail condition. Rivals create pressure, steal opportunities, and affect bonuses, but finishing behind one does not end a successful Shift.
- The current baseline already passes the C# build plus solo, UI, road-generation, and audio smoke tests as of 2026-07-13.
- This effort explicitly carries execution into the map: after decisions are resolved, implement the resulting thin playable slices and verify them in the running game.

## Decisions so far

- [Lock the Endless Run contract](issues/01-lock-the-endless-run-contract.md) — Three-minute Shifts start at a $750 quota, rise by $250, pause at a repair-capable Pit Stop on success, add a Rival every two clears, and end only when the player misses quota.

## Not yet specified

- Exact tuning curves and numerical values; these should emerge from the run-contract and escalation decisions, then be validated through prototypes.
- The amount of authored versus systemic variation needed to sustain repeat runs; revisit after the escalation structure is known.
- Whether local records need ghosts, seeded daily runs, or only personal bests; revisit after scoring is defined.
- Persistent progression outside an Endless Run; reconsider only if the self-contained arcade loop proves replayable without it.

## Out of scope

- Multiplayer networking, lobbies, replication, and online competitive balance.
- A story campaign, mission map, narrative progression, or permanent stat-grind progression.
- Additional cities, major art-production expansion, and final storefront/platform packaging.
