using System.Text.Json;
using Mono.Cecil;

if (args.Length != 3) throw new Exception("Expected game directory, Farmhand.dll, manifest.json");
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

using var mod = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { ReadSymbols = true });
if (!mod.MainModule.HasSymbols) throw new Exception("ScriptEngine symbols not readable");
var plugin = mod.MainModule.Types.Single(t => t.FullName == "Farmhand.Plugin");
var attribute = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
using var catalog = JsonDocument.Parse(File.ReadAllText(args[2]));
var entry = catalog.RootElement.GetProperty("mods").EnumerateArray().Single();
foreach (var (key, index) in new[] { ("guid", 0), ("name", 1), ("version", 2) })
    if (entry.GetProperty(key).GetString() != (string)attribute.ConstructorArguments[index].Value)
        throw new Exception("Published metadata mismatch: " + key);
foreach (string? file in entry.GetProperty("files").EnumerateArray().Select(e => e.GetString()))
    if (file == null || !File.Exists(Path.Combine(Path.GetDirectoryName(args[1])!, file)))
        throw new Exception("Published file missing: " + file);
Console.WriteLine("All native crop APIs, private members, and Harmony targets match the installed game.");
Console.WriteLine("Published plugin metadata matches its catalog; DLL/PDB symbols are readable by the installed Cecil.");
