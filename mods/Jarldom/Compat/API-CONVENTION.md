# Mod APIs: how Jarldom talks to other mods

Jarldom never hard-depends on another mod. Each mod that wants to be used publishes a
small static class, and Jarldom finds it by reflection through the mod's live plugin.

## Publishing side (in the other mod)

```csharp
namespace MyMod
{
    // For other mods, found by reflection: the type "MyMod.Api" in this assembly.
    // Rules: bump Version when a method is added; never change a method's signature (add a new one).
    public static class Api
    {
        public const int Version = 1;

        // Say in the comment when a method only answers on the host (and what it returns elsewhere).
        public static bool IsThing(Character c) => ...;

        // Version 2 ----
        public static Vector3[] Things() => ...;
    }
}
```

- The class is `<RootNamespace>.Api`, `public static`, in the plugin's own assembly.
- Use only game and BCL types in signatures (`Character`, `Vector3`, `string`, `int`,
  arrays): no types of your own, or the caller can't bind them.
- `Version` starts at 1. An Api with no `Version` counts as 0 (Omens and Shieldwall
  before 2026-10-13).
- Methods never throw to the caller on a normal "no": return a default
  (`false`, `int.MinValue`, an empty array) and document it.
- Releasing a new method means a new `Version` and a release of that mod; callers gate
  the method on that version, so an older copy of the mod just leaves the feature off.

## Calling side (Jarldom)

`Compat/ApiRef.cs` plus the pure `Core/ApiBinder.cs` (unit-tested in tests/Jarldom.Tests):

```csharp
private static readonly ApiRef Omens = new ApiRef("Omens.Api", "com.bobisme.omens");

Func<Character, bool> f = Omens.Bind<Func<Character, bool>>("IsGreatStag", since: 1);
bool stag = f != null && Safe(() => f(ch));
```

- The API is found as `Chainloader.PluginInfos[guid].Instance.GetType().Assembly.GetType(name)`,
  not by scanning assemblies: after an F6 reload the old assembly is still loaded.
  `ApiRef` looks again whenever the plugin instance changes.
- `Bind<T>` matches the parameter types of the delegate `T` exactly and checks the return
  type; it caches the delegate (or null) per name and type.
- A missing mod: `null`, nothing logged. A mod too old for `since`: `null`, nothing
  logged. A method that should be there but isn't (or has another shape): `null`, one
  warning in the log.
- Wrap the call itself (`Safe`): the other mod's code can still throw.
