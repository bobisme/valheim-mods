#!/usr/bin/env python3
"""Validate game-specific API assumptions without launching or mutating Valheim."""
import argparse
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument("--planner-assembly", type=Path, help="Also verify the BuildOrders public planning interface and symbols")
parser.add_argument("--cigars-assembly", type=Path, help="Also verify Quad's Cigars public smoking interface and symbols")
args = parser.parse_args()
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
command = ["dotnet", "run", "--project", str(root / "tests/Farmhand.ApiCheck"), "-c", "Release",
           "-p:ValheimDir=" + str(game), "--", str(game), str(root / "dist/manifest.json")]
if args.planner_assembly:
    command.append(str(args.planner_assembly.resolve()))
if args.cigars_assembly:
    if not args.planner_assembly:
        command.append("-")
    command.append(str(args.cigars_assembly.resolve()))
subprocess.run(command, check=True)

# Golf uses native Rigidbody synchronization with private owner/replica settings.
golf = root / "dist/Golf.dll"
if golf.is_file():
    subprocess.run(["dotnet", "run", "--project", str(root / "tests/Golf.ApiCheck"), "-c", "Release",
                    "-p:ValheimDir=" + str(game), "--", str(game), str(golf)], check=True)
