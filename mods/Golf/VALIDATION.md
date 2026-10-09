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
