using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Games on one server must run the same shape of Jarldom's saved data: an older game rewriting a colony would drop what it doesn't
    // know of (petitions, claims). Each game says its version to every other now and then; while anyone heard lately says another data
    // version, no game writes colony data, and everyone is told who has which.
    internal static class Versions
    {
        private static readonly Dictionary<long, (string version, int data, string name, float at)> Heard_ = new Dictionary<long, (string, int, string, float)>();
        private static float _nextWarn;
        public static void Heard(long sender, string said)
        {
            if (ZDOMan.instance != null && sender == ZDOMan.GetSessionID()) return;
            string[] f = (said ?? "").Split('|');
            int data = f.Length > 1 && int.TryParse(f[1], out int d) ? d : 0;
            Heard_[sender] = (f.Length > 0 ? f[0] : "?", data, f.Length > 2 ? f[2] : "someone", Time.time);
        }
        private static IEnumerable<(string version, int data, string name, float at)> Others => Heard_.Values.Where(v => Time.time - v.at < 150f);
        public static bool Mismatch => Others.Any(v => v.data != Plugin.DataVersion);
        public static string Who => string.Join(", ", Others.Where(v => v.data != Plugin.DataVersion).Select(v => $"{v.name} has {v.version}"));
        public static void Warn()
        {
            if (!Mismatch || Time.time < _nextWarn) return;
            _nextWarn = Time.time + 300;
            string text = $"Jarldom versions differ (you have {Plugin.Version}; {Who}): colonies won't be saved until everyone runs the same version.";
            Plugin.Warn("Jarldom: " + text);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, text);
        }
        public static void Forget() => Heard_.Clear();
    }
}
