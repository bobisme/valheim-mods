using BepInEx;

namespace Jarldom
{
    // A mod's Api, found through its live plugin (by guid): the type "<Mod>.Api" in the assembly the running plugin came from, not
    // the first assembly with that name (after an F6 reload the old one is still loaded, and a name search can find the stale copy).
    // Looked up again whenever the plugin instance changes (loaded late, reloaded, gone).
    internal sealed class ApiRef
    {
        private readonly string _type; private readonly string[] _guids;
        private BaseUnityPlugin _plugin; private ApiBinder _binder = new ApiBinder(null);
        public ApiRef(string type, params string[] guids) { _type = type; _guids = guids; }

        private ApiBinder Now()
        {
            BaseUnityPlugin p = Compat.Find(_guids);
            if (p != _plugin) { _plugin = p; _binder = new ApiBinder(p?.GetType().Assembly.GetType(_type), Plugin.Warn); }
            return _binder;
        }
        public bool Present => Now().Present;
        public int Version => Now().Version;
        public T Bind<T>(string name, int since = 0) where T : class => Now().Bind<T>(name, since);
    }
}
