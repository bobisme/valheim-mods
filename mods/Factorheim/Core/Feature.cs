using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;

namespace Factorheim
{
    // One part of the factory (logistics, production, power, science, the rocket). The plugin calls these; each override is optional.
    // Errors are caught and logged per feature.
    internal abstract class Feature
    {
        internal abstract string Name{get;}
        // The items it adds: built, registered and given recipes by Things.
        internal virtual IEnumerable<ItemDef> Items=>Array.Empty<ItemDef>();
        // Its hammer pieces: built, registered and put in the menu (when their tier is unlocked) by Pieces.
        internal virtual IEnumerable<PieceDef> Pieces=>Array.Empty<PieceDef>();
        // Its machine recipes (assemblers, chemical plants, the silo find theirs by Kind).
        internal virtual IEnumerable<Craft> Crafts=>Array.Empty<Craft>();
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
        // Its windows (GameUi): whether one is open (the plugin points GameUi.IsOpen at all features' WindowOpen: never set GameUi.IsOpen
        // yourself), and closing them (Escape, unload).
        internal virtual bool WindowOpen=>false;
        internal virtual void CloseWindow(){}
        // Its part of the `fh` Claude Tools command: `fh <Command> ...` (args[0] is "fh", args[1] the command).
        internal virtual string Command=>null;
        internal virtual string Help=>null;
        internal virtual IEnumerator Test(string[] args,Action<JObject> output,Action<string> error){error(Name+": no test command");return null;}
    }

    internal static class Features
    {
        internal static readonly Feature[] All=
        {
            new Logistics(),
            new Production(),
            new Power(),
            new Science(),
            new Rocket(),
        };
    }
}
