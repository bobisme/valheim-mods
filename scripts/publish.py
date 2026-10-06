#!/usr/bin/env python3
"""Build the mod-manager feed using the mise-managed dotnet on PATH."""
import json
from pathlib import Path
import re
import shutil
import subprocess

root = Path(__file__).resolve().parent.parent
dist = root / "dist"
dist.mkdir(exist_ok=True)
mods = []
for project in sorted((root / "mods").glob("*/*.csproj")):
    subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--nologo", "-p:DeployToGame=false"], check=True)
    source = (project.parent / "Plugin.cs").read_text()
    fields = {}
    for field in ("Guid", "Name", "Version"):
        fields[field] = re.search(r'public const string ' + field + r'\s*=\s*"([^"\n]+)";', source).group(1)
    name = fields["Name"]
    files = [name + ".dll", name + ".pdb"]
    for file in files:
        shutil.copyfile(project.parent / "bin/Release/net48" / file, dist / file)
    description = (project.parent / "DESCRIPTION.txt").read_text().strip()
    notes = (project.parent / "CHANGELOG.txt").read_text().strip().split("\n\n", 1)[0]
    mods.append(dict(guid=fields["Guid"], name=name, version=fields["Version"], description=description,
                     notes=notes, restart="", cover="", files=files))
(dist / "manifest.json").write_text(json.dumps({"mods": mods}, indent=2) + "\n")
print("Published", len(mods), "mods to", dist)
