using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bathhouse
{
    // A sauna stove standing in the world: water thrown on its stones (the bucket, or another mod's bather through the Api) makes a cloud
    // of steam and a hiss on every game near, and the time of it is kept on the stove, so every sitter's heat works faster for a while.
    // (Its fields are read in Awake, never set on the prefab: a hot reload gives standing stoves a fresh copy of this.)
    public sealed class StoveSteam : MonoBehaviour
    {
        public const string SteamedKey = "bathhouse_steamed", Rpc = "Bathhouse_Steam";
        internal static readonly List<StoveSteam> Loaded = new List<StoveSteam>();
        private ZNetView _view; private Fireplace _fire; private ParticleSystem[] _mist; private AudioSource _hiss;

        private void Awake()
        {
            _view = GetComponent<ZNetView>(); _fire = GetComponent<Fireplace>();
            _mist = transform.Find("Steam")?.GetComponentsInChildren<ParticleSystem>(true) ?? new ParticleSystem[0];
            if (_view != null && _view.IsValid()) _view.Register(Rpc, RPC_Steam);
            Loaded.Add(this);
        }
        private void OnDestroy() => Loaded.Remove(this);

        internal bool Lit => _fire != null && _view != null && _view.IsValid() && _fire.IsBurning();
        internal double SteamedAt => _view != null && _view.IsValid() ? _view.GetZDO().GetLong(SteamedKey, 0L) / 1000.0 : 0;
        internal bool Steaming => Sauna.Steaming(Now, SteamedAt);
        internal static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;

        // Water on the stones: on every game (the owner keeps the time).
        internal void Throw() { if (_view != null && _view.IsValid()) _view.InvokeRPC(ZNetView.Everybody, Rpc); }
        private void RPC_Steam(long sender)
        {
            if (_view.IsOwner()) _view.GetZDO().Set(SteamedKey, (long)(Now * 1000));
            foreach (ParticleSystem ps in _mist) { ps.Play(); ps.Emit(70); }
            Hiss();
        }
        private void Hiss()
        {
            if (_hiss == null)
            {
                _hiss = gameObject.AddComponent<AudioSource>();
                _hiss.playOnAwake = false; _hiss.spatialBlend = 1; _hiss.rolloffMode = AudioRolloffMode.Linear; _hiss.minDistance = 2; _hiss.maxDistance = 24;
                // (through the game's own sound mixer, as the fire's crackle is)
                AudioSource like = GetComponentsInChildren<AudioSource>(true).FirstOrDefault(a => a != _hiss && a.outputAudioMixerGroup != null);
                if (like != null) _hiss.outputAudioMixerGroup = like.outputAudioMixerGroup;
            }
            _hiss.pitch = Random.Range(0.9f, 1.1f);
            _hiss.PlayOneShot(HissClip.Get(), 0.9f);
        }

        internal static StoveSteam Nearest(Vector3 at, float within, bool litOnly)
        {
            StoveSteam best = null; float bestD = within;
            foreach (StoveSteam s in Loaded)
            {
                if (s == null || litOnly && !s.Lit) continue;
                float d = Vector3.Distance(s.transform.position, at);
                if (d <= bestD) { best = s; bestD = d; }
            }
            return best;
        }
    }

    // The bucket by the stove: E throws a ladle of water on the stones.
    public sealed class BucketUse : MonoBehaviour, Hoverable, Interactable
    {
        public const float Reach = 2.5f;
        private float _last;
        public string GetHoverName() => "Water bucket";
        public float GetHoverOffset() => 0;
        public string GetHoverText()
        {
            StoveSteam s = StoveSteam.Nearest(transform.position, Reach, false);
            string what = s == null ? "\n<color=#c0c0c0>Set it by a sauna stove.</color>" : !s.Lit ? "\n<color=#c0c0c0>The stove is cold.</color>" : "";
            return Localization.instance.Localize($"Water bucket\n[<color=yellow><b>$KEY_Use</b></color>] Throw water on the stones") + what;
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer || Time.time - _last < 2.5f) return false;
            StoveSteam s = StoveSteam.Nearest(transform.position, Reach, false);
            if (s == null) { p.Message(MessageHud.MessageType.Center, "There's no sauna stove by the bucket."); return false; }
            if (!s.Lit) { p.Message(MessageHud.MessageType.Center, "The stones are cold: light the stove."); return false; }
            _last = Time.time;
            s.Throw();
            p.Message(MessageHud.MessageType.TopLeft, "Water hisses on the stones, and the steam rolls up.");
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    // The hiss of water on hot stones, made once: a burst of noise, bright at first and softening as the steam rolls away.
    internal static class HissClip
    {
        private static AudioClip _clip;
        internal static AudioClip Get()
        {
            if (_clip != null) return _clip;
            const int rate = 44100; float seconds = 2.6f; int n = (int)(rate * seconds);
            var data = new float[n]; var rng = new System.Random(7);
            float low = 0, low2 = 0, crackle = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float white = (float)(rng.NextDouble() * 2 - 1);
                // (high-passed: the hiss is all air; it darkens as it fades)
                float cut = Mathf.Lerp(0.18f, 0.55f, Mathf.Clamp01(t / seconds));
                low += (white - low) * cut; low2 += (low - low2) * 0.5f;
                float hiss = white - low2 * 0.85f;
                // (the first sizzle as the water meets the stones: a few sharp pops)
                if (t < 0.5f && rng.NextDouble() < 0.0016) crackle = 1f;
                crackle *= 0.985f;
                float attack = Mathf.Clamp01(t / 0.04f), fall = Mathf.Exp(-t * 1.35f) * (1 - Mathf.Clamp01((t - seconds + 0.3f) / 0.3f));
                data[i] = (hiss * 0.38f + crackle * white * 0.5f) * attack * fall;
            }
            int read = 0; // (handed over through the reader: SetData's overloads want a Span this build hasn't got)
            _clip = AudioClip.Create("Bathhouse hiss", n, 1, rate, false, buffer => { for (int i = 0; i < buffer.Length; i++) buffer[i] = read < n ? data[read++] : 0; });
            return _clip;
        }
    }
}
