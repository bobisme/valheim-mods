using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;

namespace Godforged
{
    // One relic (or a family of them). The plugin calls these; each override is optional. Errors are caught and logged per gadget.
    internal abstract class Feature
    {
        internal abstract string Name{get;}
        // The items it adds: built, registered and given recipes by Things.
        internal virtual IEnumerable<ItemDef> Items=>Array.Empty<ItemDef>();
        // Settings: called once from the plugin's Awake, before anything is built.
        internal virtual void Bind(ConfigFile config){}
        // Networked prefabs and build pieces of its own: called whenever the game's lists are (re)made, after the items. Must be safe to call again.
        internal virtual void Register(ZNetScene scene){}
        // After the plugin has loaded (also after a hot reload in a world).
        internal virtual void Start(){}
        internal virtual void Tick(){}
        internal virtual void LateTick(){}
        internal virtual void FixedTick(){}
        internal virtual void Gui(){}
        // Unloading: take down whatever it put in the world or on the screen.
        internal virtual void Stop(){}
        // Its part of the `gf` Claude Tools command: `gf <Command> ...` (args[0] is "gf", args[1] the command).
        internal virtual string Command=>null;
        internal virtual string Help=>null;
        internal virtual IEnumerator Test(string[] args,Action<JObject> output,Action<string> error){error(Name+": no test command");return null;}
    }

    internal static class Features
    {
        internal static readonly Feature[] All=
        {
            new Throws(),
            new Mjolnir(),
            new Jarngreipr(),
            new Gungnir(),
        };
    }
}
