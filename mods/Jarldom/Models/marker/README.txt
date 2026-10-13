The marking staff, the look of the hammer's "Mark rooms and zones" (Jarldom 0.1.30+, a tall staff since 0.1.32, ribbons for its pennant since 0.1.34): made from scratch by stake.py in Blender.
Remake: copy stake.py to ~/.cache/jarldom-stake (the flatpak Blender can't read /tmp), `blender -b --python ~/.cache/jarldom-stake/stake.py -- render`
(previews preview_*.png there), copy ~/.cache/jarldom-stake/stake.json.gz back here. Its materials (wood, rope, iron, cloth, paint, leather) are
set in Pieces.Designator.
post.py (post.json.gz) is the stocky stake the marker had in 0.1.30-0.1.31, the watch post's look since 0.1.33 (Pieces.WatchPost);
remade the same way.
