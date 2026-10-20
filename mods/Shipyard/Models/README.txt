Shipyard models: a færing (6.0 m four-oared clinker boat) in four build stages, the slipway it's built on, and a knarr
(16 m Skuldelev 1-style cargo ship) in six build stages plus its set sail and its slipway cradle.
Made by yard.py in Blender, baked low-poly by tools/bake (docs/model-baking.md).

  Export (high, untextured; add "render" for previews; 'faering', 'onslip', 'knarr' and 'knarr_onslip' are preview-only, not shipped):
    cp mods/Shipyard/Models/yard.py ~/.cache/shipyard/
    blender -b --python ~/.cache/shipyard/yard.py -- [render] <kind>     (writes ~/.cache/shipyard/<kind>.json.gz)
  Bake (what ships):
    blender -b --python $PWD/tools/bake/bake.py -- $PWD/mods/Shipyard/Models/bake_conf.py <kind>
  kinds: faering_keel faering_ribs faering_strakes faering_fit slipway
         knarr_keel knarr_ribs knarr_strakes knarr_deck knarr_mast knarr_yard knarr_sail knarr_cradle
  (The bake reads the game's wood picture from tools/bake/tex, which is git-ignored: fill it with tools/bake/gametex.py.)

Each stage is drawn whole on its own; the stages are meant shown together, each added to the last
(keel, + ribs, + strakes, + fit), all at the same origin.

Materials (slots): 0 pine, 1 oak, 2 tar, 3 iron, 4 wool (the knarr's sail: red and white stripes 0.8 m wide across x, made by
the material, so they are in the baked albedo).

Frames (game: +x starboard, +y up, +z bow):
  Hull pieces: origin amidships on the centreline at the waterline.
    keel bottom y -0.45; gunwale y +0.41 / x ±0.70 amidships; stem tips y ~0.93 at z ±2.95..3.0.
  Slipway: origin on the ground at its centre. Keel-block tops at y 0.55, so a hull sits with its
    origin 1.00 above the slipway's (keel bottom -0.45 on 0.55).

Bounds (x / y / z):                 baked tris
  faering_keel     ±0.048 / -0.45..0.961 / ±3.008       276
  faering_ribs     ±0.685 / -0.343..0.36 / ±1.482       840
  faering_strakes  ±0.73  / -0.362..0.93 / ±2.955      1732
  faering_fit      ±0.716 / -0.631..0.994 / -2.71..1.5  804
  slipway          ±1.87  / -0.006..0.877 / ±4.001      720

Places on the finished boat (hull frame):
  Thwarts (seat centres): (0, 0.043, 0.90) and (0, 0.043, -0.70); each 0.26 deep, gunwale to gunwale.
  Oarlocks (keipr + thole) on both gunwales at z 0.48 and -1.12; oars lie stowed at y ~0.1, z -1.55..1.5.
  Steering oar: starboard quarter, x ~0.44, z -2.25, loom top y ~0.99; tiller reaches inboard to x ~-0.06, y ~0.93.
    Helm (sit to steer): ~(0.15, 0.05, -1.9).
  Boarding: over the port gunwale amidships, ~(-0.8, 0.4, 0) (the steering oar is on starboard).

---------------------------------------------------------------------------------------------------------------------------
KNARR (Skuldelev 1-style cargo ship: 16.1 m over the stems, 4.8 m beam at the gunwale, 2.1 m from keel to gunwale amidships)

Stages: each is drawn whole on its own. Show them together, each added to the last, all at the hull origin (amidships, on the
centreline, at the waterline): knarr_keel, + knarr_ribs, + knarr_strakes, + knarr_deck, + knarr_mast, + knarr_yard
(+ knarr_sail when the sail is set). Show knarr_cradle only on the slipway, never on the launched ship.

  knarr_keel     The keel and the two stems: plain rounded heads curled a little inboard, a carved groove down each side.
  knarr_ribs     11 frames (z -6.5..6.5, every 1.3 m). Each is a floor timber over the keel and a futtock each side up to under
                 the gunwale. Where the hold's planks lie, the floor timbers are flat on top at y -0.645. 7 crossbeams (top y
                 0.31) at z -6.65, -5.15 and -3.7 (aft deck), 0.6 (the mast beam), and 4.15, 5.45 and 6.65 (fore deck).
  knarr_strakes  8 clinker strakes a side with tarred laps, a heavier sheer strake and a gunwale timber. The hull is closed. It
                 has no shield rack.
  knarr_deck     The half-decks (planks on the beams, a waterway timber along the hull), the hold's loose floor planks, lashing
                 cleats (3 a side, inside, y ~0, z -2.46, 0.14 and 2.74), 4 sea chests (the seats) and the two boarding ladders.
  knarr_mast     STATIC on the hull. The mast step on the floor timbers; the mast partner (fish) on the mast beam at the deck;
                 2 shrouds a side to deadeyes on the gunwales at z 0.3 and -0.6 (they meet the mast at y 10.45); the forestay
                 from the mast at y 11.75 to the stem at y 2.55; the steering oar on its boss on the starboard quarter, with its
                 tiller and withy.
  knarr_yard     TURNS about the mast axis. The round mast pole (y -0.6..12.0, radius 0.18 at the foot to 0.09 at the head,
                 truck cap to 12.07); the yard (10.0 m along x at y 11.0, radius 0.11 in the middle and 0.06 at the ends), hung
                 0.24 forward of the mast axis on a parrel; the sail furled tight under the yard (y 10.71..10.91, x ±4.75); the
                 halyard block on the yard and the halyard up to the masthead.
  knarr_sail     The set sail alone: 9.6 m wide (x ±4.8), head at y 10.9 just under the yard, foot at y 3.5, bellied forward
                 (+z) by 0.6 in the middle (no belly along the head or the side edges), red and white stripes.
  knarr_cradle   Cribbed keel blocks at z ±5.5 and ±7.0, from the ground (y -1.65) up to the keel or stem underside, their tops
                 cut to its curve. 3 pairs of side shores at z -4, 0 and 4, from chocks on the ground at x ±2.6 to pads on the
                 bilge at x ±1.6, y -0.5.

  Each model's own origin (export frame):
    Hull stages and the cradle: the hull origin.
    knarr_yard: the MAST AXIS, the hull point (0, 0, 0.6). Its x and y are hull-frame; its z is hull z - 0.6. Turn it about its
      own y axis.
    knarr_sail: the mast axis AT THE SAIL'S HEAD, the hull point (0, 10.9, 0.6). Its vertices are x -4.8..4.8, y 0 (head) down
      to -7.4 (foot), z 0.228..0.851 (hull z 0.828..1.451). Put it at local (0, 10.9, 0) under the yard's transform (hull
      (0, 10.9, 0.6)). Scaling its y toward 0 about its own origin furls it up to the yard. The yard and the sail share the
      turning axis, so turn both by the same angle. The sail's head (y 0, z 0.24) lies inside the furled bundle, so the set
      sail and the bundle meet cleanly when both show.

  Lines: The keel bottom is at y -1.10 (keel 0.20 wide and 0.25 deep, straight for z ±5.0). The stems curve up from there (arc
    radius 2.9) to stand upright at z ±7.9, with heads at y ~3.0 (tips z ±7.5..8.04). The rabbet (the planking's lower edge) is
    at y -0.85 amidships. The sheer (gunwale top ~0.07 above these): y 1.00 amidships, 1.03 at z ±3.6, 1.17 at ±5.75, 1.27 at
    ±6.4, 1.34 at ±6.8, 1.6 at the stems. Half-beam at the gunwale: 2.40 amidships, 2.32 at z ±3.6, 2.07 at ±5.1, 1.55 at
    ±6.4, 1.29 at ±6.8, 0.63 at ±7.5. The hull's outer half-width at the waterline (y 0): 2.14 amidships, 2.02 at z ±4, 1.19
    at ±6.4. At y -0.5: 1.61 amidships.

  Slipway: the hull origin sits 1.65 above the slipway's origin (keel bottom -1.10 on the block tops at 0.55). The cradle's
    ground is hull y -1.65. The slipway carries z -4..4; the cradle carries the ends.

Bounds (hull frame unless said; x / y / z):                                  baked tris  (budget)  px
  knarr_keel     ±0.11 / -1.10..3.08 / ±8.04                                    372      (500)    256
  knarr_ribs     ±2.37 / -0.82..1.23 / ±6.74                                   1448     (1600)    256
  knarr_strakes  ±2.40 / -0.86..1.66 / ±7.85                                   2534     (3200)    512
  knarr_deck     ±2.54 / -0.645..1.14 / -6.83..6.83                            1552     (1600)    256
  knarr_mast     ±2.44 / -1.20..11.76 / -7.05..7.88                             520     (1500)    256
  knarr_yard     ±5.00 / -0.60..12.07 / -0.18..0.33 (mast-axis frame)           246      (400)    256
  knarr_sail     ±4.80 / -7.40..0.00 / 0.228..0.851 (mast axis at the head)     384      (400)    256
  knarr_cradle   ±2.76 / -1.66..-0.15 / ±7.37                                   440      (500)    256

Places on the finished knarr (hull frame: +x starboard, +y up, +z bow; y 0 is the waterline):
  Helm seat: the steersman sits on the starboard aft sea chest, seat top (0.95, 0.77, -5.40), facing +z. The chest is
    x 0.50..1.40, y 0.35..0.77, z -5.63..-5.17. The tiller's end (his hand) is at (0.70, 1.42, -5.95), beside him and just
    behind.
  Other seats (sea-chest tops at y 0.77, each 0.9 across x and 0.46 along z):
    aft port (-0.95, 0.77, -4.70); fore starboard (0.95, 0.77, 5.10); fore port (-0.95, 0.77, 5.10).
  Steering oar: on the starboard quarter, loom at x 1.80, z ~-6.4: head (1.80, 1.62, -6.36), down to (1.80, -0.35, -6.50),
    blade down to y -1.20 (raked, about z -6.4..-7.0). Its boss on the hull is at y 0.65..1.08. The tiller runs from the loom
    at y ~1.42 inboard to (0.70, 1.42, -5.95) and clears the gunwale (1.27 there, top ~1.34).
  Ladders at z -1.0 (rails at z -1.22 and -0.78), on both sides (mirror x for port):
    starboard foot (2.33, -0.30, -1.0), top (2.51, 1.11, -1.0), hooked inboard over the gunwale to (2.28, 0.93..1.11, -1.0).
    On board: the gunwale top is (2.40, 1.07, -1.0); from there step down onto the hold floor at (0.6, -0.60, -1.0).
  Hold (open, no deck over it): z -3.6..4.0, between the inner edges of the half-decks.
    Open volume box: centre (0, 0.20, 0.2), size (4.3, 1.6, 7.6): x ±2.15, y -0.60..1.00, z -3.6..4.0. The hull narrows below
    y 0: the inner half-width is 1.37 at y -0.6, 2.10 at y 0 and 2.25 at y 0.31.
    Things crossing it at z 0.6: the mast (x 0); the mast beam (y 0.15..0.31, x ±2.25, z 0.51..0.69); the fish (y 0.31..0.51,
    x ±0.27, z -0.35..1.55); the mast step, which stands up through the floor to y -0.45 (x ±0.2, z -0.3..1.5).
  Walkable surfaces:
    Aft half-deck: top y 0.35, z -6.8..-3.6. Inner half-width 2.17 at z -3.6, 2.01 at -4.7, 1.69 at -5.75, 1.34 at -6.4,
      1.02 at -6.8.
    Fore half-deck: top y 0.35, z 4.0..6.8. Inner half-width 2.13 at z 4.0, 1.92 at 5.1, 1.34 at 6.4, 1.02 at 6.8.
    Hold floor: loose planks, top y -0.60, x ±0.885, z ~-3.5..3.9. Outside x ±0.9 the floor timbers and the planking rise
      with the bilge (inner hull x 1.37 at y -0.6, 2.10 at y 0).
    Gunwale tops: y ~1.07 amidships.
  Mast axis: x 0, z 0.6 (foot in the step at y -0.6, head y 12.0, truck to 12.07). The shrouds meet it at y 10.45, the
    forestay at 11.75.
  Yard: y 11.0, x ±5.0, its axis 0.24 forward of the mast axis (hull z 0.84).
  Sail: head y 10.9 (hull z 0.84), foot y 3.5, x ±4.8. Deepest belly hull z ~1.44 at x 0, y ~6.0.
    Furled bundle (always on the yard): y 10.71..10.91, x ±4.75.
  Suggested BOX colliders (centre; size; axis-aligned unless a yaw is given; mirror x for port):
    Bottom + hold floor:    (0, -0.85, 0.0);      (2.8, 0.50, 11.0)   top at y -0.60
    Lower sides (bilge):    (±1.85, -0.30, 0.0);  (0.50, 0.60, 9.0)
    Upper sides:            (±2.30, 0.50, 0.0);   (0.20, 1.10, 8.0)   top at y 1.05
    Quarter and bow sides:  (±1.75, 0.55, ±5.6);  (0.20, 1.50, 2.9). Yaw them so their outer ends turn in: starboard fore and
                            port aft -21°, starboard aft and port fore +21° (the gunwale runs from x 2.23 at |z| 4.3 to
                            1.22 at |z| 6.9).
    Aft half-deck:          (0, 0.30, -5.2);      (3.4, 0.10, 3.2)    top at y 0.35
    Fore half-deck:         (0, 0.30, 5.4);       (3.4, 0.10, 2.8)    top at y 0.35
    Hull ends (closed in):  (0, 0.40, ±7.15);     (1.2, 1.80, 0.9)
    Stems:                  (0, 1.60, ±7.75);     (0.25, 2.90, 0.6)
    Forefoot (stem arc):    (0, -0.55, ±6.4);     (0.25, 0.90, 1.6)
