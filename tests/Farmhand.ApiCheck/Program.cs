using System.Text.Json;
using Mono.Cecil;

if (args.Length != 2) throw new Exception("Expected game directory and manifest.json");
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(args[0], "valheim_Data/Managed/assembly_valheim.dll"));
void Method(string type, string name, string result, params string[] parameters)
{
    TypeDefinition target = game.MainModule.Types.Single(t => t.FullName == type);
    if (!target.Methods.Any(m => m.Name == name && m.ReturnType.FullName == result &&
        m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)))
        throw new Exception($"Game API mismatch: {type}.{name}");
}
void Field(string type, string name, string fieldType)
{
    if (!game.MainModule.Types.Single(t => t.FullName == type).Fields.Any(f => f.Name == name && f.FieldType.FullName == fieldType))
        throw new Exception($"Game field mismatch: {type}.{name}");
}
Method("Player", "TakeInput", "System.Boolean");
Method("Player", "GetBuildStamina", "System.Single");
Method("Player", "GetPlaceDurability", "System.Single", "ItemDrop/ItemData");
Method("Player", "TryPlacePiece", "System.Boolean", "Piece");
Method("Player", "ConsumeResources", "System.Void", "Piece/Requirement[]", "System.Int32", "System.Int32", "System.Int32");
Method("Player", "PieceRayTest", "System.Boolean", "UnityEngine.Vector3&", "UnityEngine.Vector3&", "Piece&", "Heightmap&", "UnityEngine.Collider&", "System.Boolean");
Method("Player", "UpdatePlacementGhost", "System.Void", "System.Boolean");
Method("Player", "UpdatePlacement", "System.Void", "System.Boolean", "System.Single");
Method("Plant", "UpdateHealth", "System.Void", "System.Double");
Method("Plant", "GetStatus", "Plant/Status");
Method("Pickable", "Interact", "System.Boolean", "Humanoid", "System.Boolean", "System.Boolean");
Method("Pickable", "GetPicked", "System.Boolean");
Method("Pickable", "CanBePicked", "System.Boolean");
Method("ZDOMan", "GetZDO", "ZDO", "ZDOID");
Field("Player", "m_placementGhost", "UnityEngine.GameObject");
Field("Player", "m_placementStatus", "Player/PlacementStatus");
Field("Player", "m_lastToolUseTime", "System.Single");
Field("Player", "m_buildRemoveDebt", "System.Int32");
Field("Humanoid", "m_rightItem", "ItemDrop/ItemData");
Method("TerrainComp", "Awake", "System.Void");
Method("TerrainComp", "CheckLoad", "System.Void");
Method("TerrainComp", "InternalDoOperation", "System.Void", "UnityEngine.Vector3", "UnityEngine.Vector3", "TerrainOp/Settings");
Method("TerrainComp", "Save", "System.Void", "System.Boolean");
Method("TerrainComp", "ApplyToHeightmap", "System.Void", "UnityEngine.Texture2D", "System.Collections.Generic.List`1<System.Single>", "System.Single[]", "System.Single[]", "Heightmap");
Method("PrivateArea", "IsEnabled", "System.Boolean");
Method("PrivateArea", "IsInside", "System.Boolean", "UnityEngine.Vector3", "System.Single");
Method("PrivateArea", "IsPermitted", "System.Boolean", "System.Int64");
Field("PrivateArea", "m_allAreas", "System.Collections.Generic.List`1<PrivateArea>");
Field("TerrainComp", "m_hmap", "Heightmap");
Field("TerrainComp", "m_levelDelta", "System.Single[]");
Field("TerrainComp", "m_smoothDelta", "System.Single[]");
Field("TerrainComp", "m_modifiedHeight", "System.Boolean[]");
Field("TerrainComp", "m_operations", "System.Int32");
Field("TerrainComp", "m_lastOpPoint", "UnityEngine.Vector3");
Field("TerrainComp", "m_lastOpRadius", "System.Single");

using var catalog = JsonDocument.Parse(File.ReadAllText(args[1]));
foreach (var entry in catalog.RootElement.GetProperty("mods").EnumerateArray())
{
    string name = entry.GetProperty("name").GetString()!;
    string directory = Path.GetDirectoryName(args[1])!;
    using var mod = AssemblyDefinition.ReadAssembly(Path.Combine(directory, name + ".dll"), new ReaderParameters { ReadSymbols = true });
    if (!mod.MainModule.HasSymbols) throw new Exception("ScriptEngine symbols not readable: " + name);
    var attribute = mod.MainModule.Types.SelectMany(t => t.CustomAttributes).Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
    foreach (var (key, index) in new[] { ("guid", 0), ("name", 1), ("version", 2) })
        if (entry.GetProperty(key).GetString() != (string)attribute.ConstructorArguments[index].Value)
            throw new Exception("Published metadata mismatch: " + name + ":" + key);
    foreach (string? file in entry.GetProperty("files").EnumerateArray().Select(e => e.GetString()))
        if (file == null || !File.Exists(Path.Combine(directory, file))) throw new Exception("Published file missing: " + file);
    Console.WriteLine(name + ": metadata and symbols verified");
}
Console.WriteLine("All native crop/terrain APIs, private members, and Harmony targets match the installed game.");
Console.WriteLine("Published plugin metadata matches its catalog; DLL/PDB symbols are readable by the installed Cecil.");
