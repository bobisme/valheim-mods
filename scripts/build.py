#!/usr/bin/env python3
"""Build all local mods; deploying to the game is an explicit option."""
import argparse
from pathlib import Path
import subprocess

args = argparse.ArgumentParser()
args.add_argument("--install", action="store_true")
install = args.parse_args().install
root = Path(__file__).resolve().parent.parent
for project in sorted((root / "mods").glob("*/*.csproj")):
    subprocess.run(["dotnet", "build", str(project), "-c", "Release",
                    "-p:DeployToGame=" + str(install).lower()], check=True)
