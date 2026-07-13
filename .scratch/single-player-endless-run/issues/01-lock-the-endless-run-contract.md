# Lock the Endless Run contract

Type: grilling
Status: resolved

## Question

What exact lifecycle should define an Endless Run—including Shift duration, starting Cash Quota, quota growth, between-shift timing, success transition, failure transition, pause/restart behavior, and which state carries across Shifts—so every later system shares one unambiguous contract?

## Answer

An Endless Run begins with a three-second countdown into a three-minute Shift. Shift 1 requires $750; each cleared Shift adds $250 to the next Cash Quota. Reaching the quota ends the Shift immediately and opens a paused Pit Stop. Letting the timer expire below quota ends the run.

The player's wallet, health, and total run cash carry across Shifts. Shift cash, active Fares, passenger state, pickup zones, and positions reset; every taxi returns to the depot for the next countdown. The Pit Stop initially offers a $100 full repair, separating spendable wallet cash from the unspent total-run record. The player can continue directly if no repair is wanted.

The Cash Quota is the only continuation condition. Rivals cannot end a run by placing ahead of the player. Pressure escalates systemically: the base run starts with two Rivals and adds one on every second cleared Shift, capped at six. Quotas continue increasing without a terminal victory state.

Run-over presents total run cash and cleared-Shift count. Its primary action starts a completely fresh run, resetting quota, wallet, health, run cash, and Rival count. Returning to the main menu also clears the run.

Implemented in `modes/TaxiMode.cs`, `GameManager.cs`, and `ui/RetroNeonCabShell.cs`. Verified by `tests/single_player_level_smoke_test.gd` through three Shifts, paid repair, Rival escalation, quota failure, and fresh-run reset; the full build and smoke suite pass.
