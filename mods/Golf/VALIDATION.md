# Meadow Golf 0.2.0 validation

Native Linux Creative session, 9 October 2026. Mise-managed .NET 10.0.300, net48.
Published, build and installed DLL SHA-256: `9d95004312b60093a265ca2785f4156ca265338474165f32636cff043f6af6cf`.
Final running/native-checked MVID: `4fd004ec-d9d5-4144-8d9d-8e3e8c8f22e9`.

## Checked

- Rule suite covers full nine- and eighteen-hole progression; missing/duplicate markers, horizontal and vertical hole limits, extra back-nine markers, scoring bounds, rough resistance and dry/submerged liquid thresholds.
- Native API validation resolves 508 installed-game, Unity, TMP and BepInEx member references. All Harmony targets and private sync fields are checked. Build has no warnings or Windows dependency.
- Full real-shot Creative matches completed nine and eighteen holes with one scored stroke each. The tests use the real host RPCs, ball-owner shot validation, native swing animation/contact and native cup capture; they do not inject completed scores. The nine-hole test was repeated after adding host-routed personal stops and state/score retrieval. Eighteen-hole testing also exercises cup lookup without passing a client-loaded cup view.
- Skipping an unfinished hole and joining twice leave the original ball/card intact. Personal stop and shared match end preserve every finished score. The host scoreboard retains the complete card.
- Reloads preserve ball ID, match ID, declared hole count, finished/stopped flags and the card. Independent physics scenes and temporary course markers clean up.
- Final-artifact forest fixture: 60% putt through heavy rough and a trunk-shaped obstacle, predicted/actual displacement 4.3398 m, endpoint error 0. Water fixture: predicted/actual entry at 2.0491 m, endpoint error 0. Both use unsaved colliders 100 m above play.
- An independent unsaved live GolfBall entering native water returned to its exact last lie, added exactly one penalty, and remained stable after recovery. It did not change the player's card.
- Native window inspected in a live screenshot. It renders through the game's UI canvas with inventory wood/recessed sprites, `Valheim-AveriaSerifLibre` headings and `Valheim-AveriaSansLibre` body text. Title bounds and contrast are checked; closing it restores normal input. Catalog metadata and matching portable DLL/PDB symbols pass verification.

Temporary tests do not teleport, terraform, damage, remove or modify existing structures. Course markers are individually tracked, unsaved, and have timeout/reload cleanup. In an initial test on stairs, the ball correctly rolled out of reach before a shot; a separate tiny flat test slab made subsequent lifecycle tests reproducible.

## Remaining qualification

A second real player, dedicated-server startup, disconnect/ownership handoff and network latency have not been exercised in this single-player session. Multiplayer paths validate peer identity and player ID; the host creates shared match IDs, course validation, scoreboards and stop/end changes, while each ball owner runs physics. Far-away ball state and personal stop use host routing, and ownership revisions guard against stale owner packets, but static inspection and a local routed test do not substitute for a two-player session.

Prediction is bounded and remains approximate for moving geometry, weather changes and geometry modified during a shot. Surface/obstacle rules use the real terrain/colliders; the forest fixture exercises the same resistance with an explicit fixture surface override and trunk collider. Snow/swamp resistance is rule-tested, not separately live-tested in those biomes. Liquid levels omit waves for stable rules. Existing unrelated Gary, cloth and CinderSpawner errors appeared in the shared log; the completed Golf tests produced no Golf exception.

The following historical measurements describe 0.1.0 before rough and liquid hazards were added; ranges on uncleared terrain now differ.

---

# Meadow Golf 0.1.0 validation

Native Linux Creative session, 9 October 2026. Compiled with mise-managed .NET 10.0.300 for net48.

Final installed/published DLL SHA-256: `0c0b96a1bca53cc133743bd45c50e2ba68825a4e9842dfbac537455578a55856`.
The running assembly reported MVID `7179f569-ee68-4eec-9310-89751811f591`; build, dist and installed DLL bytes matched.

## Checked

- Rule suite: labels, normalized bounded shots, cup speed/vertical checks, 18-hole scorecards, replay, fresh rounds, course changes and exact tee penalty.
- Native API checker: 448 game/Unity/BepInEx references resolve, private owner/replica fields exist, Harmony targets are unambiguous, no Windows-only dependency.
- Catalog metadata and readable matching portable DLL/PDB symbols; existing BuildOrders public interface.
- Human player successfully moved the ball and played the hole. Native cup completion saved a result; replay replaced it. The scorecard was inspected in a live screenshot.
- Accepted swing: ball stayed stationary and strokes stayed unchanged during wind-up. Native contact launched it after 0.4736 seconds and consumed one stroke. Swing and wood-hit ZSFX reported `playing: true` in separate samples.
- Golf-only reloads preserved the same saved ball ID, position, strokes and card. A disposable simulation's private scene count returned to zero after cleanup.

## Physics comparisons

Silent probes in the actual Unity world used the live ball's launch, spin, material, damping and settling rules; prediction ran in a separate physics scene with copied colliders.

| Case | Power | Predicted range | Actual range | Endpoint error |
| --- | ---: | ---: | ---: | ---: |
| Open flat ground putt | 25% | 3.575 m | 3.575 m | 0 |
| Open flat ground putt | 60% | 11.180 m | 11.180 m | 0 |
| Open ground chip | 50% | 10.096 m | 10.096 m | 0 |
| Open ground drive | 50% | 26.123 m | 26.123 m | 0 |
| Live hall ricochet, path-following predictor | 50% drive | 16.573 m | 16.571 m | 2.23 mm |
| Live hall immediate wall bounce | 100% drive | 0.397 m | 0.397 m | 0.015 mm |
| Final floor fixture | 60% putt | 11.179 m | 11.179 m | 0 |
| Final basement fixture, chip hits ceiling | 50% chip | 6.292 m | 6.292 m | 0 |
| Final ramp over flat floor | 25% putt | 0.165 m | 0.165 m | 0 |
| Final published build, short wall-blocked putt | 25% | 0.055 m | 0.055 m | 0 |

Range is horizontal displacement, not accumulated travel along the path. Initial open-ground samples preceded the collider-discovery optimization; later hall and fixture checks exercised that final algorithm. Fixtures are unsaved colliders 100 m above/below play, with independent reload/timeout cleanup. They do not alter terrain or buildings. A floor fixture initially intersected an existing hall and diverged after chaotic contacts; moving it clear of existing construction made the repeated comparison exact. Prediction remains approximate in changing environments.

Gameplay prediction splits work across approximately 2 ms slices; observed warm slices were around 2.02 ms. Individual native scene/mesh calls can exceed the target (a cold sample reached 4.98 ms). Only colliders along the swept ball path are copied; no repeating whole-world scans. Excessive/unsupported/moving geometry or a shot beyond the bounded simulation gives a short aim guide instead of a false stopping point.

## Still unverified

Remote multiplayer visibility, latency, dedicated-server initialization, disconnect/ownership transfer and rapid next-hole requests on separate peers. All participants must install and restart for the new saved prefabs. User assessment of sound balance and charge cancellation still needs hands-on feedback. Historical unrelated Gary/cloth/CinderSpawner errors were present in the shared game's log; no new Golf exception appeared in the final checks.
