namespace Omens
{
    // For other mods (Jarldom reads and moves the gods' favour through it), found by reflection: typeof "Omens.Api" in this assembly.
    // The ledger lives on the host, so only the host's game can answer.
    public static class Api
    {
        // The gods' favour (-5..5), or int.MinValue on a game that isn't the host.
        public static int GetFate() => Director.ApiFate();
        // Move the gods' favour by delta steps, for a reason told in the log; false on a game that isn't the host.
        public static bool AddFate(int delta, string why) => Director.ApiAddFate(delta, why);
    }
}
