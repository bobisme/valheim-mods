using System.Text.Json;
using Mono.Cecil;

if (args.Length != 2 && args.Length != 3) throw new Exception("Expected game directory, manifest.json, and optional planner DLL");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(args[0], "BepInEx/core"));
resolver.AddSearchDirectory(Path.Combine(args[0], "valheim_Data/Managed"));
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(args[0], "valheim_Data/Managed/assembly_valheim.dll"), new ReaderParameters { AssemblyResolver = resolver });
using var utils = AssemblyDefinition.ReadAssembly(Path.Combine(args[0], "valheim_Data/Managed/assembly_utils.dll"), new ReaderParameters { AssemblyResolver = resolver });
void Method(string type, string name, string result, params string[] parameters)
{
    TypeDefinition target = game.MainModule.Types.Concat(utils.MainModule.Types).Single(t => t.FullName == type);
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
Method("Menu", "Update", "System.Void");
Method("PlayerController", "TakeInput", "System.Boolean", "System.Boolean");
Method("GameCamera", "UpdateMouseCapture", "System.Void");
Method("ZInput", "GetMouseScrollWheel", "System.Single");
Method("Humanoid", "StartAttack", "System.Boolean", "Character", "System.Boolean");
Method("ZNetView", "IsValid", "System.Boolean");
Method("Piece", "GetSnapPoints", "System.Void", "System.Collections.Generic.List`1<UnityEngine.Transform>");
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
Method("Heightmap", "GetHeight", "System.Single", "System.Int32", "System.Int32");
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

// Gary uses tagged vanilla creatures and native navigation/combat. Bind private hooks to the installed game.
Method("MonsterAI","UpdateAI","System.Boolean","System.Single");
Method("MonsterAI","OnDamaged","System.Void","System.Single","Character");
Method("BaseAI","HavePath","System.Boolean","UnityEngine.Vector3");
Method("BaseAI","LookAt","System.Void","UnityEngine.Vector3");
Method("Player","Interact","System.Void","UnityEngine.GameObject","System.Boolean","System.Boolean");
Method("Humanoid","DoInteractAnimation","System.Void","UnityEngine.GameObject");
Method("Character","OnDeath","System.Void");
Method("Character","GetZAnim","ZSyncAnimation");
Method("ZSyncAnimation","HasParameter","System.Boolean","System.String","UnityEngine.AnimatorControllerParameterType");
Method("ZSyncAnimation","SetBool","System.Void","System.String","System.Boolean");
Method("Pickable","SetPicked","System.Void","System.Boolean");
Method("Game","ScaleDrops","System.Int32","UnityEngine.GameObject","System.Int32");
Method("Fireplace","IsBurning","System.Boolean");
Method("EffectArea","GetAllAreas","System.Collections.Generic.List`1<EffectArea>");
Method("EffectArea","IsPointInsideArea","EffectArea","UnityEngine.Vector3","EffectArea/Type","System.Single");
Method("Piece","GetCreator","System.Int64");
Method("ItemDrop","SetStack","System.Void","System.Int32");
Method("ItemDrop","OnCreateNew","System.Void","ItemDrop","System.Boolean");
Field("ZSFX","m_audioClips","UnityEngine.AudioClip[]");
Field("Character","m_lastHit","HitData");
Method("MonsterAI","UpdateTarget","System.Void","Humanoid","System.Single","System.Boolean&","System.Boolean&");
Method("BaseAI","UpdateAI","System.Boolean","System.Single");
Method("BaseAI","MoveTo","System.Boolean","System.Single","UnityEngine.Vector3","System.Single","System.Boolean");
Method("BaseAI","Flee","System.Boolean","System.Single","UnityEngine.Vector3");
Method("BaseAI","Follow","System.Void","UnityEngine.GameObject","System.Single");
Method("BaseAI","SetAlerted","System.Void","System.Boolean");
Method("BaseAI","SetTargetInfo","System.Void","ZDOID");
Method("Character","SetHealth","System.Void","System.Single");
Method("Character","CheckDeath","System.Void");
Method("Character","RPC_Damage","System.Void","System.Int64","HitData");
Method("Character","ApplyDamage","System.Void","HitData","System.Boolean","System.Boolean","HitData/DamageModifier");
Method("Character","RaiseSkill","System.Void","Skills/SkillType","System.Single");
Method("Character","GetHoverName","System.String");
Method("Character","GetHoverText","System.String");
Field("MonsterAI","m_targetCreature","Character");
Field("MonsterAI","m_targetStatic","StaticTarget");
Field("MonsterAI","m_lastKnownTargetPos","UnityEngine.Vector3");
Field("MonsterAI","m_beenAtLastPos","System.Boolean");
Field("MonsterAI","m_timeSinceSensedTargetCreature","System.Single");
Field("Location","s_allLocations","System.Collections.Generic.List`1<Location>");
Field("Hud","m_userHidden","System.Boolean");
Method("Teleport","Interact","System.Boolean","Humanoid","System.Boolean","System.Boolean");
Method("Character","GetCollider","UnityEngine.CapsuleCollider");
Method("Room","GetHash","System.Int32");
Method("ZNetScene","IsAreaReady","System.Boolean","UnityEngine.Vector3");
Method("ZDOMan","FindSectorObjects","System.Void","Vector2s","SimulationDistance","System.Collections.Generic.List`1<ZDO>","System.Collections.Generic.List`1<ZDO>");
Field("MineRock","m_hitAreas","UnityEngine.Collider[]");
Method("MineRock","GetHealth","System.Single");
Field("DungeonGenerator","m_roomsToLoad","System.Int32");
Field("DungeonGenerator","m_loadedRooms","DungeonGenerator/RoomPlacementData[]");
Field("Player","m_customData","System.Collections.Generic.Dictionary`2<System.String,System.String>");
var itemVersions=game.MainModule.Types.Single(t=>t.Name=="Version").NestedTypes.Single(t=>t.Name=="Item");
foreach(var (name,value) in new[]{("Quality",101),("Smaller",108),("ChunksNCheats",109)})
    if(!itemVersions.Fields.Any(f=>f.Name==name&&f.HasConstant&&(int)f.Constant==value))throw new Exception("Native chest serialization changed: "+name);
var inventorySave=game.MainModule.Types.Single(t=>t.Name=="Inventory").Methods.Single(m=>m.Name=="Save"&&m.Parameters.Count==1);
if(!inventorySave.Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_S&&Convert.ToInt32(i.Operand)==109)||
   !inventorySave.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="ZPackage"&&m.Name=="Write"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.UInt16"))
    throw new Exception("Native inventory header changed; review Gary's read-only chest parser.");
Field("ZRoutedRpc","m_functions","System.Collections.Generic.Dictionary`2<System.Int32,RoutedMethodBase>");

using var catalog = JsonDocument.Parse(File.ReadAllText(args[1]));
foreach (var entry in catalog.RootElement.GetProperty("mods").EnumerateArray())
{
    string name = entry.GetProperty("name").GetString()!;
    string directory = Path.GetDirectoryName(args[1])!;
    using var mod = AssemblyDefinition.ReadAssembly(Path.Combine(directory, name + ".dll"), new ReaderParameters { ReadSymbols = true, AssemblyResolver = resolver });
    if (!mod.MainModule.HasSymbols) throw new Exception("ScriptEngine symbols not readable: " + name);
    var attribute = mod.MainModule.Types.SelectMany(t => t.CustomAttributes).Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
    if (name == "BuildShapes")
    {
        if (mod.MainModule.AssemblyReferences.Any(r => r.Name.StartsWith("BuildOrders", StringComparison.Ordinal)))
            throw new Exception("BuildShapes must not bind a hot-reloaded BuildOrders assembly identity.");
        if (!mod.MainModule.Types.SelectMany(t => t.CustomAttributes).Any(a => a.AttributeType.FullName == "BepInEx.BepInDependency" &&
            (string)a.ConstructorArguments[0].Value == "com.dhack.buildorders"))
            throw new Exception("BuildShapes planner dependency is missing.");
    }
    foreach (var (key, index) in new[] { ("guid", 0), ("name", 1), ("version", 2) })
        if (entry.GetProperty(key).GetString() != (string)attribute.ConstructorArguments[index].Value)
            throw new Exception("Published metadata mismatch: " + name + ":" + key);
    foreach (string? file in entry.GetProperty("files").EnumerateArray().Select(e => e.GetString()))
        if (file == null || !File.Exists(Path.Combine(directory, file))) throw new Exception("Published file missing: " + file);
    Console.WriteLine(name + ": metadata and symbols verified");
}
if (args.Length == 3)
{
    using var planner = AssemblyDefinition.ReadAssembly(args[2], new ReaderParameters { ReadSymbols = true, AssemblyResolver = resolver });
    var plugin = planner.MainModule.Types.Single(t => t.FullName == "BuildOrders.Plugin");
    if (!planner.MainModule.HasSymbols || !plugin.Fields.Any(f => f.Name == "PlanningApiVersion" && f.HasConstant && (int)f.Constant == 1))
        throw new Exception("Planner symbols or API version are incompatible.");
    void Api(string name, params string[] parameters)
    {
        if (!plugin.Methods.Any(m => m.IsPublic && !m.IsStatic && m.Name == name && m.ReturnType.FullName == "System.Boolean" &&
            m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)))
            throw new Exception("Planner API signature mismatch: " + name);
    }
    Api("TryCreateGhostPlan", "Player", "System.String", "System.String[]", "UnityEngine.Vector3[]", "UnityEngine.Quaternion[]", "System.String&", "System.String&");
    Api("TryRemoveGhostPlan", "Player", "System.String", "System.Int32&", "System.String&");
    Api("IsPlanningInputAvailable", "Player");
    Api("TryGetGhostAtRay", "Player", "UnityEngine.Vector3", "UnityEngine.Vector3", "System.String&", "System.String&", "UnityEngine.Vector3&", "UnityEngine.Quaternion&", "System.Single&");
    Console.WriteLine("BuildOrders: public planning API and symbols verified; BuildShapes has no planner assembly binding.");
}
Console.WriteLine("All native crop/terrain/companion APIs, private members, and Harmony targets match the installed game.");
Console.WriteLine("Published plugin metadata matches its catalog; DLL/PDB symbols are readable by the installed Cecil.");
