#!/usr/bin/env python3
"""Run the standalone algorithm tests for every mod."""
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
for project in sorted((root / "tests").glob("*.Tests/*.csproj")):
    subprocess.run(["dotnet", "run", "--project", str(project), "-c", "Release"], check=True)
