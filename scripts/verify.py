#!/usr/bin/env python3
"""Validate game-specific API assumptions without launching or mutating Valheim."""
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
home = Path.home()
candidates = [Path(os.environ["VALHEIM_DIR"])] if os.environ.get("VALHEIM_DIR") else [
    home / ".local/share/Steam/steamapps/common/Valheim",
    home / ".steam/steam/steamapps/common/Valheim",
    home / ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Valheim",
    Path("C:/Program Files (x86)/Steam/steamapps/common/Valheim"),
]
game = next((p.resolve() for p in candidates if (p / "valheim_Data/Managed/assembly_valheim.dll").is_file()), None)
if game is None:
    raise SystemExit("Set VALHEIM_DIR to the installed game directory.")
subprocess.run(["dotnet", "run", "--project", str(root / "tests/Farmhand.ApiCheck"), "-c", "Release",
                "-p:ValheimDir=" + str(game), "--", str(game), str(root / "dist/Farmhand.dll"),
                str(root / "dist/manifest.json")], check=True)
