"""Offline sound generation for Bob's mods: a batch of prompts x seeds through Stable Audio Open, written as .ogg with an audition
page (index.html) so Bob can listen and pick. Nothing here runs in the game; picked files are copied into the mod by hand.

  ~/.cache/jarldom-soundgen/venv/bin/python tools/soundgen/gen.py tools/soundgen/hall.json [--takes 4] [--only name,...]
  ~/.cache/jarldom-soundgen/tflux/bin/python tools/soundgen/gen.py tools/soundgen/hall.json --model tangoflux  (takes named <name>_tf_<n>)

  ELEVENLABS_API_KEY=... python3-with-numpy tools/soundgen/gen.py <batch> --model eleven  (takes named <name>_el_<n>; costs credits)

Licences: ElevenLabs, usable on paid plans. Stable Audio Open 1.0, Stability AI Community License (outputs usable). TangoFlux: non-commercial, research/academic only
(WavCaps training data) - fine to audition, check before shipping its output.

The batch file: {"out": "<dir under ~/jarldom-sounds>", "sounds": [{"name", "prompt", "negative"?, "seconds", "loop"?}]}.
loop: the end is crossfaded into the start (2 s) so it can play on repeat. Also lists any recordings already in <out>/found/ on the page.
"""
import argparse, html, json, os, subprocess, sys
from pathlib import Path

ROOT = Path.home() / "jarldom-sounds"


def crossfade_loop(audio, rate, fade_s=2.0):
    import numpy as np
    n = int(rate * fade_s)
    if audio.shape[-1] <= 2 * n:
        return audio
    body, tail = audio[..., :-n].copy(), audio[..., -n:]
    k = np.linspace(0, 1, n, dtype=audio.dtype)
    body[..., :n] = body[..., :n] * k + tail * (1 - k)
    return body


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("batch")
    ap.add_argument("--takes", type=int, default=4)
    ap.add_argument("--only", default="")
    ap.add_argument("--steps", type=int, default=0)
    ap.add_argument("--model", default="stable", choices=["stable", "tangoflux", "eleven"])
    ap.add_argument("--influence", type=float, default=0.5)  # (eleven: how closely it follows the prompt, 0..1)
    a = ap.parse_args()
    batch = json.loads(Path(a.batch).read_text())
    out = ROOT / batch["out"]
    out.mkdir(parents=True, exist_ok=True)
    only = set(filter(None, a.only.split(",")))

    import numpy as np, soundfile as sf
    if a.model == "eleven":
        # ElevenLabs sound effects (paid; outputs usable commercially on paid plans). Key from ELEVENLABS_API_KEY.
        import io, urllib.request
        key = os.environ["ELEVENLABS_API_KEY"]
        rate, tag = 44100, "_el"
        def make(s, seed):
            body = {"text": s["prompt"], "duration_seconds": min(30.0, float(s.get("seconds", 10))), "prompt_influence": a.influence,
                    "model_id": "eleven_text_to_sound_v2", "loop": bool(s.get("loop"))}
            req = urllib.request.Request("https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_192", data=json.dumps(body).encode(),
                                         headers={"xi-api-key": key, "Content-Type": "application/json"})
            mp3 = urllib.request.urlopen(req, timeout=180).read()
            pcm = subprocess.run(["ffmpeg", "-loglevel", "error", "-i", "-", "-ac", "2", "-ar", str(rate), "-f", "f32le", "-"], input=mp3, capture_output=True, check=True).stdout
            return np.frombuffer(pcm, dtype=np.float32).reshape(-1, 2).T.copy()
    elif a.model == "tangoflux":
        import torch
        from tangoflux import TangoFluxInference
        tf = TangoFluxInference(name="declare-lab/TangoFlux")
        rate, tag, steps = 44100, "_tf", a.steps or 50
        def make(s, seed):
            torch.manual_seed(seed)
            return tf.generate(s["prompt"], steps=steps, duration=min(30, int(s.get("seconds", 20)))).float().cpu().numpy()
    else:
        import torch
        from diffusers import StableAudioPipeline
        pipe = StableAudioPipeline.from_pretrained("stabilityai/stable-audio-open-1.0", torch_dtype=torch.float16).to("cuda")
        rate, tag, steps = pipe.vae.sampling_rate, "", a.steps or 150
        def make(s, seed):
            g = torch.Generator("cuda").manual_seed(seed)
            return pipe(s["prompt"], negative_prompt=s.get("negative", "music, singing, melody, instruments, modern, english speech, distortion"),
                        num_inference_steps=steps, audio_end_in_s=float(s.get("seconds", 20)), num_waveforms_per_prompt=1, generator=g).audios[0].float().cpu().numpy()

    for s in batch["sounds"]:
        if only and s["name"] not in only:
            continue
        for take in range(1, a.takes + 1):
            path = out / f"{s['name']}{tag}_{take}.ogg"
            if path.exists():
                continue
            audio = make(s, 1000 * take + len(s["name"]))  # (channels, samples)
            if audio.ndim == 1:
                audio = audio[None, :]
            if s.get("loop") and a.model != "eleven":  # (ElevenLabs makes its own loops)
                audio = crossfade_loop(audio, rate)
            peak = max(1e-6, float(np.abs(audio).max()))
            audio = audio * (0.9 / peak)
            wav = path.with_suffix(".wav")
            sf.write(wav, audio.T, rate)
            subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", str(wav), "-c:a", "libvorbis", "-q:a", "5", str(path)], check=True)
            wav.unlink()
            print("wrote", path, flush=True)
    page(out, batch)


def page(out, batch):
    rows = []
    for s in batch["sounds"]:
        takes = sorted(out.glob(f"{s['name']}_[0-9]*.ogg")) + sorted(out.glob(f"{s['name']}_tf_*.ogg")) + sorted(out.glob(f"{s['name']}_el_*.ogg"))
        if not takes:
            continue
        rows.append(f"<h2>{html.escape(s['name'])}</h2><p><i>{html.escape(s['prompt'])}</i>{' (loops)' if s.get('loop') else ''}</p>")
        rows += [f"<div>{t.name} <audio controls preload=none {'loop' if s.get('loop') else ''} src='{t.name}'></audio></div>" for t in takes]
    found = sorted((out / "found").glob("*")) if (out / "found").exists() else []
    if found:
        rows.append("<h2>Recordings (CC0, Freesound)</h2>")
        notes = (out / "found" / "SOURCES.txt").read_text() if (out / "found" / "SOURCES.txt").exists() else ""
        rows.append(f"<pre>{html.escape(notes)}</pre>")
        rows += [f"<div>{f.name} <audio controls preload=none src='found/{f.name}'></audio></div>" for f in found if f.suffix in (".ogg", ".mp3", ".wav")]
    (out / "index.html").write_text("<html><body style='font-family:sans-serif;background:#222;color:#ddd'><h1>" + html.escape(batch["out"]) +
                                    "</h1><p>Tell Claude the names that work (and what's wrong with the rest).</p>" + "\n".join(rows) + "</body></html>")
    print("page", out / "index.html")


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[2] == "--page-only":
        b = json.loads(Path(sys.argv[1]).read_text()); page(ROOT / b["out"], b)
    else:
        main()
