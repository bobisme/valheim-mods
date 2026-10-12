using System.Text.Json;
using Mono.Cecil;

if (args.Length < 2 || args.Length > 4) throw new Exception("Expected game directory, manifest.json, and optional planner DLL / Cigars DLL");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(args[0], "BepInEx/core"));
resolver.AddSearchDirectory(Path.Combine(args[0], "valheim_Data/Managed"));
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(args[0], "valheim_Data/Managed/assembly_valheim.dll"), new ReaderParameters { AssemblyResolver = resolver });
using var utils = AssemblyDefinition.ReadAssembly(Path.Combine(args[0], "valheim_Data/Managed/assembly_utils.dll"), new ReaderParameters { AssemblyResolver = resolver });
void Method(string type, string name, string result, params string[] parameters)
{
    TypeDefinition target = game.MainModule.Types.Concat(utils.MainModule.Types).SelectMany(t => t.NestedTypes.Prepend(t)).Single(t => t.FullName == type);
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
Method("Player", "GetPlayerID", "System.Int64");
Method("ZNetView", "IsValid", "System.Boolean");
Method("ZNetView", "IsOwner", "System.Boolean");
Method("ZNetView", "ClaimOwnership", "System.Void");
Method("Piece", "GetSnapPoints", "System.Void", "System.Collections.Generic.List`1<UnityEngine.Transform>");
// Hallwright reads actual unlocked recipes, native collider geometry and support losses.
Method("Player", "IsRecipeKnown", "System.Boolean", "System.String");
Method("WearNTear", "GetMaterialProperties", "System.Void", "System.Single&", "System.Single&", "System.Single&", "System.Single&");
Method("WearNTear", "GetCOM", "UnityEngine.Vector3");
Method("WearNTear", "GetSupport", "System.Single");
Field("WearNTear", "m_comOffset", "UnityEngine.Vector3");
Field("WearNTear", "m_supports", "System.Boolean");
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
Method("ItemDrop","RemoveOne","System.Boolean");
Method("ItemDrop","Load","System.Void");
Method("ItemDrop","CanPickup","System.Boolean","System.Boolean");
Method("ItemDrop","IsPiece","System.Boolean");
Method("ItemDrop","InTar","System.Boolean");
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
Method("Minimap","UpdatePins","System.Void");
Method("Minimap","AddPin","Minimap/PinData","UnityEngine.Vector3","Minimap/PinType","System.String","System.Boolean","System.Boolean","System.Int64","Splatform.PlatformUserID");
Method("Minimap","RemovePin","System.Void","Minimap/PinData");
Field("Minimap","m_pins","System.Collections.Generic.List`1<Minimap/PinData>");
Field("Minimap","m_pinUpdateRequired","System.Boolean");
// Gary activities reuse native emote/weather/item saves without cloning gameplay components.
Method("EnvMan","IsWet","System.Boolean");
Method("EnvMan","IsNight","System.Boolean");
Method("Cover","IsUnderRoof","System.Boolean","UnityEngine.Vector3");
Method("Player","StartEmote","System.Boolean","System.String","System.Boolean");
Method("ItemDrop","DropItem","ItemDrop","ItemDrop/ItemData","System.Int32","UnityEngine.Vector3","UnityEngine.Quaternion");
Method("ItemDrop","Start","System.Void");
Method("ItemDrop","AutoStackItems","System.Void");
Method("Inventory","GetItem","ItemDrop/ItemData","System.String","System.Int32","System.Boolean");
Method("Inventory","RemoveItem","System.Boolean","ItemDrop/ItemData","System.Int32");
Method("RandomFlyingBird","get_Instances","System.Collections.Generic.List`1<IMonoUpdater>");
Field("ZDOVars","s_emoteID","System.Int32");
Field("ZDOVars","s_emote","System.Int32");
// Gary's reforestation reads the player's bed point and the ground's farm/path state, and plants native saplings.
Method("PlayerProfile","GetCustomSpawnPoint","UnityEngine.Vector3");
Method("PlayerProfile","HaveCustomSpawnPoint","System.Boolean");
Method("Heightmap","IsCultivated","System.Boolean","UnityEngine.Vector3");
Method("Heightmap","IsCleared","System.Boolean","UnityEngine.Vector3");
Method("Heightmap","FindHeightmap","Heightmap","UnityEngine.Vector3");
Field("Plant","m_biome","Heightmap/Biome");
Field("Plant","m_growRadius","System.Single");
Field("Piece","m_placeEffect","EffectList");
// Gary's caretaking: crops harvested by hand, falling trees, stray fire, trophies, and planting crops for the player.
Method("Pickable","Interact","System.Boolean","Humanoid","System.Boolean","System.Boolean");
Method("TreeLog","Awake","System.Void");
Field("Fire","s_fires","System.Collections.Generic.List`1<Fire>");
Method("ItemStand","GetAttachedItem","System.Int32");
Method("Piece","SetCreator","System.Void","System.Int64","Splatform.PlatformUserID");
Field("Plant","m_grownPrefabs","UnityEngine.GameObject[]");
Field("Plant","m_needCultivatedGround","System.Boolean");
Method("ZNetView","ClaimOwnership","System.Void");
Method("ItemDrop","RemoveOne","System.Boolean");
// Gary passengers pause native locomotion and synchronize a boat-relative deck position.
Method("Character","UpdateMotion","System.Void","System.Single");
Method("Character","IsAttached","System.Boolean");
Method("Character","IsAttachedToShip","System.Boolean");
Method("Character","GetStandingOnShip","Ship");
Method("Character","GetRelativePosition","System.Boolean","ZDOID&","System.String&","UnityEngine.Vector3&","UnityEngine.Quaternion&","UnityEngine.Vector3&");
Method("Player","GetControlledShip","Ship");
Method("Ship","IsPlayerInBoat","System.Boolean","Player");
Method("Ship","get_Instances","System.Collections.Generic.List`1<IMonoUpdater>");
Method("ZSyncTransform","ClientSync","System.Void","System.Single");
Method("ZSyncTransform","OwnerSync","System.Void");
Method("ZSyncTransform","CustomFixedUpdate","System.Void","System.Single");
Method("ZSyncTransform","SyncNow","System.Void");
Field("ZSyncTransform","m_characterParentSync","System.Boolean");
Field("Character","m_lastGroundBody","UnityEngine.Rigidbody");
Field("Character","m_lastGroundCollider","UnityEngine.Collider");
Field("Character","m_lastGroundTouch","System.Single");
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
Method("ZPackage","Size","System.Int32");
Method("ZPackage","GetArray","System.Byte[]");
Field("ZRoutedRpc","m_functions","System.Collections.Generic.Dictionary`2<System.Int32,RoutedMethodBase>");

// ChestSearch uses native permission/ownership and displays cloned slot visuals, never synthetic inventory transfers.
Method("Container","RPC_OpenResponse","System.Void","System.Int64","System.Boolean");
Method("Container","Load","System.Boolean");
Method("Container","CheckAccess","System.Boolean","System.Int64");
Field("Container","m_nview","ZNetView");
Method("InventoryGui","Update","System.Void");
Method("InventoryGui","Hide","System.Void");
Method("InventoryGui","CloseContainer","System.Void");
Method("InventoryGui","OnSelectedItem","System.Void","InventoryGrid","ItemDrop/ItemData","Vector2i","InventoryGrid/Modifier");
Method("InventoryGui","OnReleasedItem","System.Void","InventoryGrid","ItemDrop/ItemData","Vector2i");
Method("InventoryGui","OnRightClickItem","System.Void","InventoryGrid","ItemDrop/ItemData","Vector2i");
Field("InventoryGui","m_dragGo","UnityEngine.GameObject");
Method("InventoryGrid","GetHoveredElement","InventoryElement");
Method("InventoryGrid","GetElementPos","Vector2i","InventoryElement");
Method("InventoryElement","Initialize","System.Void","System.Int32","System.Int32");
Method("Inventory","MoveItemToThis","System.Boolean","Inventory","ItemDrop/ItemData","System.Int32","System.Int32","System.Int32");

// Spyglass registers a cloned item and recipe, narrows the native camera, and uncovers map through the minimap's own explore.
Method("ZNetScene","Awake","System.Void");
Method("ObjectDB","UpdateRegisters","System.Void");
Method("ObjectDB","CopyOtherDB","System.Void","ObjectDB");
Field("ZNetScene","m_namedPrefabs","System.Collections.Generic.Dictionary`2<System.Int32,UnityEngine.GameObject>");
Method("GameCamera","UpdateCamera","System.Void","System.Single");
Field("GameCamera","m_camera","UnityEngine.Camera");
Field("GameCamera","m_skyCamera","UnityEngine.Camera");
Field("GameCamera","m_fov","System.Single");
Method("PlayerController","LateUpdate","System.Void");
Field("PlayerController","m_mouseSens","System.Single");
Method("Minimap","Explore","System.Void","UnityEngine.Vector3","System.Single");
Method("WorldGenerator","GetHeight","System.Single","System.Single","System.Single");
Field("Character","m_eye","UnityEngine.Transform");

// Wayfinder: map clicks lay a course, the weather is foretold from the clock, ships carry names, fittings and the helm's course.
Method("Minimap","OnMapLeftClick","System.Void");
Method("Minimap","OnMapDblClick","System.Void");
Method("Minimap","RemovePinUnderPointer","System.Void");
Method("Minimap","ScreenToWorldPoint","UnityEngine.Vector3","UnityEngine.Vector3");
Method("Minimap","GetClosestPin","Minimap/PinData","UnityEngine.Vector3","System.Single","System.Boolean");
Method("Minimap","IsExplored","System.Boolean","UnityEngine.Vector3");
Method("Minimap","UpdateExplore","System.Void","System.Single","Player");
Field("Minimap","m_pins","System.Collections.Generic.List`1<Minimap/PinData>");
Field("Minimap","m_exploreRadius","System.Single");
Method("MapTable","Start","System.Void");
Method("MapTable","OnWrite","System.Boolean","Switch","Humanoid","ItemDrop/ItemData");
Method("MapTable","OnRead","System.Boolean","Switch","Humanoid","ItemDrop/ItemData","System.Boolean");
Method("EnvMan","SelectWeightedEnvironment","EnvSetup","System.Collections.Generic.List`1<EnvEntry>");
Method("EnvMan","GetAvailableEnvironments","System.Collections.Generic.List`1<EnvEntry>","BiomeSector");
Field("EnvMan","m_environmentDuration","System.Int64");
Field("EnvMan","m_windPeriodDuration","System.Int64");
Method("Ship","Awake","System.Void");
Method("Ship","GetWindAngleFactor","System.Single");
Method("ShipControlls","GetHoverText","System.String");
Method("ShipControlls","Interact","System.Boolean","Humanoid","System.Boolean","System.Boolean");
Method("Chair","GetHoverText","System.String");
Method("Chair","Interact","System.Boolean","Humanoid","System.Boolean","System.Boolean");
Method("Fireplace","GetHoverText","System.String");
Method("Fireplace","Interact","System.Boolean","Humanoid","System.Boolean","System.Boolean");
Method("Humanoid","UseItem","System.Void","Inventory","ItemDrop/ItemData","System.Boolean");
Method("Floating","GetLiquidLevel","System.Single","UnityEngine.Vector3","System.Single","LiquidType");
Field("RandomFlyingBird","m_randomNoise","EffectList");
Field("Player","m_customData","System.Collections.Generic.Dictionary`2<System.String,System.String>");

// Grave Goods: treasure chests seeded once, item numbers raised by their powers, worn powers added up, tooltips and slots.
Method("Container","CheckForChanges","System.Void");
Method("Container","Load","System.Boolean");
Field("Container","m_nview","ZNetView");
Method("ItemDrop/ItemData","GetDamage","HitData/DamageTypes","System.Int32","System.Single");
Method("ItemDrop/ItemData","GetArmor","System.Single","System.Int32","System.Single");
Method("ItemDrop/ItemData","GetMaxDurability","System.Single","System.Int32");
Method("ItemDrop/ItemData","GetBaseBlockPower","System.Single","System.Int32");
Method("ItemDrop/ItemData","GetWeight","System.Single","System.Int32");
Method("ItemDrop/ItemData","GetTooltip","System.String","ItemDrop/ItemData","System.Int32","System.Boolean","System.Single","System.Int32","System.Boolean");
Method("Player","GetTotalFoodValue","System.Void","System.Single&","System.Single&","System.Single&");
Method("SEMan","ModifyHealthRegen","System.Void","System.Single&");
Method("SEMan","ModifyStaminaRegen","System.Void","System.Single&");
Method("SEMan","ModifyMaxCarryWeight","System.Void","System.Single","System.Single&");
Method("SEMan","ModifySkillLevel","System.Void","Skills/SkillType","System.Single&");
Method("SEMan","ApplyStatusEffectSpeedMods","System.Void","System.Single&","UnityEngine.Vector3");
Method("SEMan","ModifyAttackStaminaUsage","System.Void","System.Single","System.Single&","System.Boolean");
Method("SEMan","ModifyAttack","System.Void","Skills/SkillType","HitData&");
Field("SEMan","m_character","Character");
Method("Character","Damage","System.Void","HitData");
Method("Character","RPC_Damage","System.Void","System.Int64","HitData");
Method("InventoryGrid","CreateItemTooltip","System.Void","ItemDrop/ItemData","UITooltip");
Method("InventoryGrid","UpdateGui","System.Void","Player","ItemDrop/ItemData");
Field("InventoryGrid","m_elements","System.Collections.Generic.List`1<InventoryElement>");
Field("InventoryGrid","m_inventory","Inventory");
Field("InventoryGui","m_containerGrid","InventoryGrid");
Method("InventoryGui","Show","System.Void","Container","System.Int32");
Method("ItemDrop","GetHoverText","System.String");
Method("ItemDrop","Start","System.Void");
Method("ZoneSystem","SetGlobalKey","System.Void","System.String");
Method("ZoneSystem","GetGlobalKey","System.Boolean","System.String");

// Delivery: the horn's menu from UseItem, calls sent to everyone, the courier's body (the player model with no network) and his run.
Method("Game","Start","System.Void");
Field("ZNetView","m_forceDisableInit","System.Boolean");
Method("VisEquipment","SetModel","System.Void","System.Int32");
Method("VisEquipment","SetSkinColor","System.Void","UnityEngine.Vector3");
Method("VisEquipment","SetHairColor","System.Void","UnityEngine.Vector3");
Method("VisEquipment","SetHairItem","System.Void","System.Int32");
Method("VisEquipment","SetBeardItem","System.Void","System.Int32");
Method("VisEquipment","SetChestItem","System.Void","System.Int32");
Method("VisEquipment","SetLegItem","System.Void","System.Int32");
Method("VisEquipment","SetShoulderItem","System.Void","System.Int32","System.Int32","System.Int32");
Method("Pathfinding","GetPath","System.Boolean","UnityEngine.Vector3","UnityEngine.Vector3","System.Collections.Generic.List`1<UnityEngine.Vector3>","Pathfinding/AgentType","System.Boolean","System.Boolean","System.Boolean");
Method("Chat","SetNpcText","System.Void","UnityEngine.GameObject","UnityEngine.Vector3","System.Single","System.Single","System.String","System.String","System.Boolean");
Method("Player","IsRecipeKnown","System.Boolean","System.String");
Method("Player","IsKnownMaterial","System.Boolean","System.String");
Method("Character","InInterior","System.Boolean");
Method("ZoneSystem","GetGroundHeight","System.Single","UnityEngine.Vector3");
Field("CookingStation","m_conversion","System.Collections.Generic.List`1<CookingStation/ItemConversion>");

// Omens: a networked sign the host creates as a raw ZDO, the game's own raid events, and night timing.
Method("ZNet","Shutdown","System.Void","System.Boolean");
Method("ZNet","GetWorldUID","System.Int64");
Method("RandEventSystem","SetRandomEventByName","System.Void","System.String","UnityEngine.Vector3");
Method("RandEventSystem","HaveEvent","System.Boolean","System.String");
Method("RandEventSystem","GetCurrentRandomEvent","RandomEvent");
Method("ZDOMan","CreateNewZDO","ZDO","UnityEngine.Vector3","System.Int32");
Method("ZDOMan","DestroyZDO","System.Void","ZDO");
Method("ZDOMan","GetAllZDOsWithPrefabIterative","System.Boolean","System.String","System.Collections.Generic.List`1<ZDO>","System.Int32&");
Method("ZoneSystem","GetSolidHeight","System.Boolean","UnityEngine.Vector3","System.Single&","System.Int32");
Method("EnvMan","IsNight","System.Boolean");
Field("EnvMan","m_dayLengthSec","System.Int64");
Field("Game","m_eventRate","System.Single");
Method("SEMan","AddStatusEffect","StatusEffect","System.Int32","System.Boolean","System.Int32","System.Single","System.Int16");
Method("BaseAI","SetHuntPlayer","System.Void","System.Boolean");
Method("Character","SetLevel","System.Void","System.Int32");
Method("ZoneSystem","GetGlobalKey","System.Boolean","GlobalKeys");
Method("WorldGenerator","GetBiome","Heightmap/Biome","System.Single","System.Single","System.Single","System.Boolean");
Field("RandEventSystem","m_events","System.Collections.Generic.List`1<RandomEvent>");
Method("EnvMan","SetEnv","System.Void","EnvSetup","System.Single","System.Single","System.Single","System.Single","System.Single");
Field("EnvMan","m_dirLight","UnityEngine.Light");
Method("SpawnSystem","UpdateSpawnList","System.Void","System.Collections.Generic.List`1<SpawnSystem/SpawnData>","System.DateTime","System.Boolean","System.String");
Method("SpawnSystem","GetLevelUpChance","System.Single","UnityEngine.Vector3","System.Single");
Method("Character","Start","System.Void");
Field("Character","m_name","System.String");
Method("CharacterDrop","GenerateDropList","System.Collections.Generic.List`1<System.Collections.Generic.KeyValuePair`2<UnityEngine.GameObject,System.Int32>>");
Field("Odin","m_despawn","EffectList");
Field("SE_Stats","m_raiseSkill","Skills/SkillType");
Field("SE_Stats","m_raiseSkillModifier","System.Single");
Method("ItemDrop","SetStack","System.Void","System.Int32");
Method("Minimap","AddPin","Minimap/PinData","UnityEngine.Vector3","Minimap/PinType","System.String","System.Boolean","System.Boolean","System.Int64","Splatform.PlatformUserID");
Method("ZoneSystem","GetGroundHeight","System.Boolean","UnityEngine.Vector3","System.Single&");
Field("SE_Stats","m_healthRegenMultiplier","System.Single");
Field("StatusEffect","m_ttl","System.Single");
Method("EnvMan","SetForceEnvironment","System.Void","System.String");
Method("Character","Damage","System.Void","HitData");

// DualWield routes a matching second weapon into the off hand and borrows the game's dual weapons' stance and attacks.
Method("Humanoid","EquipItem","System.Boolean","ItemDrop/ItemData","System.Boolean");
Method("Humanoid","UnequipItem","System.Void","ItemDrop/ItemData","System.Boolean");
Method("Humanoid","UnequipAllItems","System.Void");
Method("Humanoid","SetupEquipment","System.Void");
Method("Humanoid","SetupAnimationState","System.Void");
Method("Humanoid","SetAnimationState","System.Void","ItemDrop/ItemData/AnimationState");
Method("Humanoid","TriggerEquipEffect","System.Void","ItemDrop/ItemData");
Method("Humanoid","GetCurrentBlocker","ItemDrop/ItemData");
Method("Attack","Start","System.Boolean","Humanoid","UnityEngine.Rigidbody","ZSyncAnimation","CharacterAnimEvent","VisEquipment","ItemDrop/ItemData","Attack","System.Single","System.Single");
Method("VisEquipment","SetLeftHandEquipped","System.Boolean","System.Int32","System.Int32","System.Int32");
Field("Humanoid","m_rightItem","ItemDrop/ItemData");
Field("Humanoid","m_leftItem","ItemDrop/ItemData");
Field("Humanoid","m_visEquipment","VisEquipment");
Field("VisEquipment","m_leftItemInstance","UnityEngine.GameObject");

// Shieldwall: the horde's march (targets chosen before the game's own AI), the Warstone as a crafting station with a horn,
// planted staves' friendly-fire guard, raids drawn to the stone, siege music, and the planting key.
Method("MonsterAI","UpdateAI","System.Boolean","System.Single");
Field("MonsterAI","m_targetCreature","Character");
Field("MonsterAI","m_targetStatic","StaticTarget");
Field("MonsterAI","m_updateTargetTimer","System.Single");
Field("MonsterAI","m_timeSinceAttacking","System.Single");
Field("MonsterAI","m_despawnInDay","System.Boolean");
Method("BaseAI","SetAlerted","System.Void","System.Boolean");
Method("BaseAI","CanSeeTarget","System.Boolean","Character");
Method("BaseAI","CanSeeTarget","System.Boolean","StaticTarget");
Field("Character","m_group","System.String");
Method("Character","Awake","System.Void");
Method("CraftingStation","Interact","System.Boolean","Humanoid","System.Boolean","System.Boolean");
Method("CraftingStation","GetHoverText","System.String");
Method("CraftingStation","GetLevel","System.Int32","System.Boolean");
Method("Piece","CanBeRemoved","System.Boolean");
Field("Player","m_placementGhost","UnityEngine.GameObject");
Method("Player","Update","System.Void");
Method("Player","UpdateKnownRecipesList","System.Void");
Method("Projectile","IsValidTarget","System.Boolean","IDestructible");
Field("Projectile","m_owner","Character");
Field("Projectile","m_weapon","ItemDrop/ItemData");
Field("Projectile","m_gravity","System.Single");
Method("Projectile","Setup","System.Void","Character","UnityEngine.Vector3","System.Single","HitData","ItemDrop/ItemData","ItemDrop/ItemData");
Method("Aoe","ShouldHit","System.Boolean","UnityEngine.Collider");
Field("Aoe","m_owner","Character");
Field("Aoe","m_itemData","ItemDrop/ItemData");
Method("RandEventSystem","SetRandomEvent","System.Void","RandomEvent","UnityEngine.Vector3");
Method("RandEventSystem","GetMusicOverride","System.String");
Field("RandomEvent","m_nearBaseOnly","System.Boolean");
Field("RandomEvent","m_forceMusic","System.String");
Field("ZNetScene","m_instances","System.Collections.Generic.Dictionary`2<ZDO,ZNetView>");
Method("Pathfinding","GetPath","System.Boolean","UnityEngine.Vector3","UnityEngine.Vector3","System.Collections.Generic.List`1<UnityEngine.Vector3>","Pathfinding/AgentType","System.Boolean","System.Boolean","System.Boolean");
Method("Pathfinding","HavePath","System.Boolean","UnityEngine.Vector3","UnityEngine.Vector3","Pathfinding/AgentType");
Method("ItemDrop","SaveToZDO","System.Void","ItemDrop/ItemData","ZDO","System.Int32");
Method("ItemDrop","LoadFromZDO","System.Void","ItemDrop/ItemData","ZDO","System.Int32");
Method("Cover","GetCoverForPoint","System.Void","UnityEngine.Vector3","System.Single&","System.Boolean&","System.Single");
Field("SE_Stats","m_addArmor","System.Single");
Field("SE_Stats","m_eitrRegenMultiplier","System.Single");
// Shieldwall 0.2: stave sockets and Warstone upgrades (station extensions), the native boss bar, kills and tower damage.
Field("StationExtension","m_craftingStation","CraftingStation");
Field("StationExtension","m_maxStationDistance","System.Single");
Field("StationExtension","m_stack","System.Boolean");
Method("CraftingStation","GetExtentionCount","System.Int32","System.Boolean");
Field("CraftingStation","m_rangeBuild","System.Single");
Field("EnemyHud","m_hudRoot","UnityEngine.GameObject");
Field("EnemyHud","m_baseHudBoss","UnityEngine.GameObject");
Field("WearNTear","m_onDestroyed","System.Action");
Field("WearNTear","m_new","UnityEngine.GameObject");
Field("WearNTear","m_health","System.Single");
Method("WearNTear","Damage","System.Void","HitData");
Method("WearNTear","GetHealthPercentage","System.Single");
Method("Character","OnDeath","System.Void");
Method("Player","TeleportTo","System.Boolean","UnityEngine.Vector3","UnityEngine.Quaternion","System.Boolean");
Method("Inventory","CountItems","System.Int32","System.String","System.Int32","System.Boolean");
Method("Inventory","RemoveItem","System.Void","System.String","System.Int32","System.Int32","System.Boolean");
// Shieldwall 0.3.2: asking the host whether it has the mod before building pieces it would delete.
Method("ZNet","GetServerPeer","ZNetPeer");
Field("ZNetPeer","m_uid","System.Int64");
// Shieldwall 0.3: sockets as one-slot containers, the pickaxe's dig and the hoe's raise for earthworks.
Field("Container","m_bkg","UnityEngine.Sprite");
Field("Container","m_width","System.Int32");
Field("Container","m_height","System.Int32");
Field("Container","m_checkGuardStone","System.Boolean");
Field("Container","m_openEffects","EffectList");
Method("Container","GetHoverText","System.String");
Method("Container","Interact","System.Boolean","Humanoid","System.Boolean","System.Boolean");
Method("Container","UseItem","System.Boolean","Humanoid","ItemDrop/ItemData");
Method("Container","IsInUse","System.Boolean");
Method("Container","GetInventory","Inventory");
Method("ObjectDB","TryGetItemPrefab","System.Boolean","ItemDrop/ItemData/SharedData","UnityEngine.GameObject&");

// Cheap instance lists instead of whole-scene searches: plants via SlowUpdate, item stands as they wake.
Method("SlowUpdate","GetAllInstaces","System.Collections.Generic.List`1<SlowUpdate>");
Method("ItemStand","Awake","System.Void");
// TrophyHall reads trophies on player-built item stands inside bases and adds comfort beside the game's own.
Method("ItemStand","GetAttachedItem","System.Int32");
Method("ItemStand","HaveAttachment","System.Boolean");
Field("ItemStand","m_guardianPower","StatusEffect");
Method("EffectArea","IsPointInsideArea","EffectArea","UnityEngine.Vector3","EffectArea/Type","System.Single");
Method("SE_Rested","CalculateComfortLevel","System.Int32","System.Boolean","UnityEngine.Vector3");
Field("SE_Stats","m_addMaxCarryWeight","System.Single");
Field("SE_Stats","m_raiseSkillModifier","System.Single");
Field("Recipe","m_craftingStation","CraftingStation");

// PassengerCart clones the game's cart into a bigger hammer piece and seats passengers through the native attach.
Method("Player","AttachStart","System.Void","UnityEngine.Transform","UnityEngine.GameObject","System.Boolean","System.Boolean","System.Boolean","System.String","UnityEngine.Vector3","UnityEngine.Transform");
Method("Player","AttachStop","System.Void");
Method("Player","GetAttachPoint","UnityEngine.Transform");
Method("Vagon","IsAttached","System.Boolean","Character");
Field("Vagon","m_baseMass","System.Single");
Field("Vagon","m_loadVis","System.Collections.Generic.List`1<Vagon/LoadData>");
Field("Vagon","m_name","System.String");
Field("Container","m_name","System.String");
Field("WearNTear","m_health","System.Single");
Field("PieceTable","m_pieces","System.Collections.Generic.List`1<UnityEngine.GameObject>");
Field("ZNetScene","m_namedPrefabs","System.Collections.Generic.Dictionary`2<System.Int32,UnityEngine.GameObject>");
Method("ZNetScene","Awake","System.Void");

// LocalPortals clones the wood portal's building rules, draws views through linked mirrors with a second camera, carries the
// game camera through the glass, and walks the player through with the camera's smoothing and fall height carried along.
Method("GameCamera","LateUpdate","System.Void");
Method("GameCamera","CollideRay2","System.Void","UnityEngine.Vector3","UnityEngine.Vector3","UnityEngine.Vector3&");
Field("GameCamera","m_playerPos","UnityEngine.Vector3");
Field("GameCamera","m_playerVel","UnityEngine.Vector3");
Field("GameCamera","m_skyCamera","UnityEngine.Camera");
Field("GameCamera","m_blockCameraMask","UnityEngine.LayerMask");
Field("Character","m_maxAirAltitude","System.Single");
Field("Player","m_lookPitch","System.Single");
Method("Character","GetCollider","UnityEngine.CapsuleCollider");
Method("Character","GetVisual","UnityEngine.GameObject");
Method("Character","SetLookDir","System.Void","UnityEngine.Vector3","System.Single");
Method("Character","GetLookDir","UnityEngine.Vector3");
Method("Character","ResetCloth","System.Void");
Method("Player","GetAllPlayers","System.Collections.Generic.List`1<Player>");
Field("Vagon","m_instances","System.Collections.Generic.List`1<Vagon>");
Method("WearNTear","SetupColliders","System.Void");
// ...and puts arrows, bombs and dropped items through the glass.
Method("Projectile","FixedUpdate","System.Void");
Method("Projectile","Setup","System.Void","Character","UnityEngine.Vector3","System.Single","HitData","ItemDrop/ItemData","ItemDrop/ItemData");
Field("Projectile","m_nview","ZNetView");
Field("Projectile","m_vel","UnityEngine.Vector3");
Field("Projectile","m_didHit","System.Boolean");
Field("Projectile","m_haveStartPoint","System.Boolean");
Field("Projectile","m_startPoint","UnityEngine.Vector3");
Field("Projectile","m_owner","Character");
Field("Projectile","m_gravity","System.Single");
Field("Projectile","m_drag","System.Single");
Field("Projectile","m_rayRadius","System.Single");
Field("ItemDrop","m_nview","ZNetView");
// ...and walks creatures through: their path to a mirror, their walking carried round, and their hunters told.
Method("BaseAI","MoveTo","System.Boolean","System.Single","UnityEngine.Vector3","System.Single","System.Boolean");
Method("BaseAI","MoveTowards","System.Void","UnityEngine.Vector3","System.Boolean");
Field("BaseAI","m_character","Character");
Field("BaseAI","m_lastFindPathTime","System.Single");
Field("BaseAI","m_path","System.Collections.Generic.List`1<UnityEngine.Vector3>");
Field("MonsterAI","m_targetCreature","Character");
Field("MonsterAI","m_lastKnownTargetPos","UnityEngine.Vector3");
Field("MonsterAI","m_beenAtLastPos","System.Boolean");
Field("Character","m_currentVel","UnityEngine.Vector3");
Field("Character","m_moveDir","UnityEngine.Vector3");
Method("Character","GetAllCharacters","System.Collections.Generic.List`1<Character>");
// ...and keeps the area round a far mirror loaded and alive.
Method("ZNetScene","CreateDestroyObjects","System.Void");
Method("ZNetScene","CreateObjects","System.Void","System.Collections.Generic.List`1<ZDO>","System.Collections.Generic.List`1<ZDO>");
Method("ZNetScene","RemoveObjects","System.Void","System.Collections.Generic.List`1<ZDO>","System.Collections.Generic.List`1<ZDO>");
Field("ZNetScene","m_tempCurrentObjects","System.Collections.Generic.List`1<ZDO>");
Field("ZNetScene","m_tempCurrentDistantObjects","System.Collections.Generic.List`1<ZDO>");
Method("ZoneSystem","PokeLocalZone","System.Boolean","Vector2s");
Method("ZDOMan","ReleaseZDOS","System.Void","System.Single");
Field("ZDOMan","m_releaseZDOTimer","System.Single");
Method("WearNTear","GetHighlightRenderers","System.Collections.Generic.List`1<UnityEngine.Renderer>");
Field("WearNTear","m_renderers","System.Collections.Generic.List`1<UnityEngine.Renderer>");
Field("WearNTear","m_new","UnityEngine.GameObject");
Field("WearNTear","m_fragmentRoots","UnityEngine.GameObject[]");
Method("TextInput","RequestText","System.Void","TextReceiver","System.String","System.Int32");
Method("PrivateArea","CheckAccess","System.Boolean","UnityEngine.Vector3","System.Single","System.Boolean","System.Boolean");
Method("ZNetView","Unregister","System.Void","System.String");
// ...sends a joined player their far side, and hears the far side through the glass.
Method("ZDOMan","CreateSyncList","System.Void","ZDOMan/ZDOPeer","System.Collections.Generic.List`1<ZDO>");
Method("ZDOMan","ServerSortSendZDOS","System.Void","System.Collections.Generic.List`1<ZDO>","UnityEngine.Vector3","ZDOMan/ZDOPeer");
Method("ZDOMan","ServerSendCompare","System.Int32","ZDO","ZDO");
Method("ZDOMan","FindSectorObjects","System.Void","Vector2s","SimulationDistance","System.Collections.Generic.List`1<ZDO>","System.Collections.Generic.List`1<ZDO>");
Method("ZoneSystem","CreateGhostZones","System.Boolean","UnityEngine.Vector3");
Method("ZNet","SendServerSyncPlayerData","System.Void","ZNetPeer");
Method("Minimap","IsExplored","System.Boolean","UnityEngine.Vector3");
Method("ZSFX","Start","System.Void");
Field("ZSFX","m_audioSource","UnityEngine.AudioSource");

// Bob's Pipes: hotbar dispatch, saved item bowls, mutual smoking and native item registrations.
Method("Player","UseHotbarItem","System.Void","System.Int32");
Method("Player","OnDeath","System.Void");
Method("Humanoid","UseItem","System.Void","Inventory","ItemDrop/ItemData","System.Boolean");
Method("Inventory","Changed","System.Void","System.Boolean","System.Boolean");
Method("Inventory","RemoveItem","System.Boolean","ItemDrop/ItemData","System.Int32");
Method("ItemDrop/ItemData","GetTooltip","System.String","ItemDrop/ItemData","System.Int32","System.Boolean","System.Single","System.Int32","System.Boolean");
Method("ObjectDB","UpdateRegisters","System.Void");
Method("ObjectDB","Awake","System.Void");
Method("ObjectDB","CopyOtherDB","System.Void","ObjectDB");
Method("ZNetScene","Awake","System.Void");
Method("Player","Awake","System.Void");
Method("SEMan","AddStatusEffect","StatusEffect","StatusEffect","System.Boolean","System.Int32","System.Single","System.Int16");
Method("SEMan","RemoveStatusEffect","System.Boolean","System.Int32","System.Boolean");
Method("SEMan","HaveStatusEffect","System.Boolean","System.Int32");
Method("ZSyncAnimation","SetTrigger","System.Void","System.String");
Method("EnvMan","GetWindForce","UnityEngine.Vector3");
Field("Inventory","m_onChanged","System.Action");
Field("VisEquipment","m_rightItem","System.Int32");
Field("ZNetScene","m_namedPrefabs","System.Collections.Generic.Dictionary`2<System.Int32,UnityEngine.GameObject>");

// Demolisher removes pieces by the hammer's own path (Player.RemovePiece) and asks with the game's yes/no popup.
Method("Piece","GetAllPiecesInRadius","System.Void","UnityEngine.Vector3","System.Single","System.Collections.Generic.List`1<Piece>");
Method("Piece","IsPlacedByPlayer","System.Boolean");
Method("Piece","DropResources","System.Void","HitData");
Method("WearNTear","Remove","System.Void","System.Boolean");
Method("Location","IsInsideNoBuildLocation","System.Boolean","UnityEngine.Vector3");
Method("CraftingStation","HaveBuildStationInRange","CraftingStation","System.String","UnityEngine.Vector3");
Method("Player","NoCostCheat","System.Boolean");
Method("Player","UpdateKnownRecipesList","System.Void");
Method("Player","UpdateAvailablePiecesList","System.Void");
Method("UnifiedPopup","Push","System.Void","PopupBase");
Method("UnifiedPopup","Pop","System.Void");
Method("YesNoPopup",".ctor","System.Void","System.String","System.String","PopupButtonCallback","PopupButtonCallback","System.Boolean","System.Boolean");
Method("MaterialMan","ResetValue","System.Void","UnityEngine.GameObject","System.Int32");

using var catalog = JsonDocument.Parse(File.ReadAllText(args[1]));
foreach (var entry in catalog.RootElement.GetProperty("mods").EnumerateArray())
{
    string name = entry.GetProperty("name").GetString()!;
    string directory = Path.GetDirectoryName(args[1])!;
    using var mod = AssemblyDefinition.ReadAssembly(Path.Combine(directory, entry.GetProperty("files").EnumerateArray().Select(f=>f.GetString()!).Single(f=>f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))), new ReaderParameters { ReadSymbols = true, AssemblyResolver = resolver });
    if (!mod.MainModule.HasSymbols) throw new Exception("ScriptEngine symbols not readable: " + name);
    var attribute = mod.MainModule.Types.SelectMany(t => t.CustomAttributes).Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
    if (name == "BuildShapes")
    {
        if (mod.MainModule.AssemblyReferences.Any(r => r.Name.StartsWith("BuildOrders", StringComparison.Ordinal)))
            throw new Exception("BuildShapes must not bind a hot-reloaded BuildOrders assembly identity.");
        if (!mod.MainModule.Types.SelectMany(t => t.CustomAttributes).Any(a => a.AttributeType.FullName == "BepInEx.BepInDependency" &&
            (string)a.ConstructorArguments[0].Value == "com.quad.buildorders"))
            throw new Exception("BuildShapes planner dependency is missing.");
    }
    if (entry.GetProperty("guid").GetString() == "com.bobisme.bobspipes")
    {
        if (mod.MainModule.AssemblyReferences.Any(r => r.Name.StartsWith("CigarSmoking", StringComparison.Ordinal)))
            throw new Exception("Pipes must not bind a hot-reloaded Cigars assembly identity.");
        if (!mod.MainModule.Types.SelectMany(t => t.CustomAttributes).Any(a => a.AttributeType.FullName == "BepInEx.BepInDependency" &&
            (string)a.ConstructorArguments[0].Value == "com.quad.cigarsmoking"))
            throw new Exception("Pipes Cigars dependency is missing.");
        if (string.IsNullOrWhiteSpace(entry.GetProperty("restart").GetString()))
            throw new Exception("New saved pipe items require a published restart notice.");
    }
    foreach (var (key, index) in new[] { ("guid", 0), ("name", 1), ("version", 2) })
        if (entry.GetProperty(key).GetString() != (string)attribute.ConstructorArguments[index].Value)
            throw new Exception("Published metadata mismatch: " + name + ":" + key);
    foreach (string? file in entry.GetProperty("files").EnumerateArray().Select(e => e.GetString()))
        if (file == null || !File.Exists(Path.Combine(directory, file))) throw new Exception("Published file missing: " + file);
    Console.WriteLine(name + ": metadata and symbols verified");
}
if (args.Length >= 3 && args[2] != "-")
{
    string plannerPdb=Path.ChangeExtension(args[2],".pdb");
    if(!File.Exists(plannerPdb))throw new Exception("Planner PDB is missing.");
    byte[] pdbHeader;using(var input=File.OpenRead(plannerPdb)){pdbHeader=new byte[32];input.ReadExactly(pdbHeader);}
    bool portablePlannerPdb=System.Text.Encoding.ASCII.GetString(pdbHeader,0,4)=="BSJB";
    if(!portablePlannerPdb && !System.Text.Encoding.ASCII.GetString(pdbHeader).StartsWith("Microsoft C/C++ MSF 7.00"))
        throw new Exception("Unrecognized planner PDB format.");
    using var planner = AssemblyDefinition.ReadAssembly(args[2], new ReaderParameters { ReadSymbols = portablePlannerPdb, AssemblyResolver = resolver });
    var plugin = planner.MainModule.Types.Single(t => t.FullName == "BuildOrders.Plugin");
    if ((portablePlannerPdb && !planner.MainModule.HasSymbols) || !plugin.Fields.Any(f => f.Name == "PlanningApiVersion" && f.HasConstant && (int)f.Constant == 1))
        throw new Exception("Planner symbols or API version are incompatible.");
    void Api(string name, params string[] parameters)
    {
        if (!plugin.Methods.Any(m => m.IsPublic && !m.IsStatic && m.Name == name && m.ReturnType.FullName == "System.Boolean" &&
            m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)))
            throw new Exception("Planner API signature mismatch: " + name);
    }
    Api("TryCreateGhostPlan", "Player", "System.String", "System.String[]", "UnityEngine.Vector3[]", "UnityEngine.Quaternion[]", "System.String&", "System.String&");
    var shellLimit=plugin.Fields.SingleOrDefault(f=>f.Name=="MaximumShellPieces");
    if(shellLimit==null || !shellLimit.HasConstant || (int)shellLimit.Constant<2048)
        throw new Exception("BuildOrders lacks the 2,048-piece whole-building capability required by Hallwright.");
    Api("TryCreateBuildingShell", "Player", "System.String", "System.String[]", "UnityEngine.Vector3[]", "UnityEngine.Quaternion[]", "System.String&", "System.String&");
    Api("TryRemoveGhostPlan", "Player", "System.String", "System.Int32&", "System.String&");
    Api("IsPlanningInputAvailable", "Player");
    Api("TryGetGhostAtRay", "Player", "UnityEngine.Vector3", "UnityEngine.Vector3", "System.String&", "System.String&", "UnityEngine.Vector3&", "UnityEngine.Quaternion&", "System.Single&");
    Console.WriteLine(portablePlannerPdb?"BuildOrders: public planning API and symbols verified; BuildShapes has no planner assembly binding.":"BuildOrders: public planning API verified; Windows PDB matching/readability is not validated by this Linux check. BuildShapes has no planner assembly binding.");
}
if (args.Length == 4)
{
    using var cigars=AssemblyDefinition.ReadAssembly(args[3],new ReaderParameters{ReadSymbols=true,AssemblyResolver=resolver});
    var plugin=cigars.MainModule.Types.Single(t=>t.FullName=="CigarSmoking.Plugin");
    if(!cigars.MainModule.HasSymbols || !plugin.Fields.Any(f=>f.Name=="SmokingApiVersion"&&f.HasConstant&&(int)f.Constant>=1))
        throw new Exception("Cigars symbols or smoking API version are incompatible.");
    void SmokeApi(string name,string result,params string[] parameters)
    {
        if(!plugin.Methods.Any(m=>m.IsPublic&&!m.IsStatic&&m.Name==name&&m.ReturnType.FullName==result&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters)))
            throw new Exception("Cigars smoking API signature mismatch: "+name);
    }
    SmokeApi("RegisterSmokingEffect","System.Boolean","System.String");
    SmokeApi("UnregisterSmokingEffect","System.Void","System.String");
    SmokeApi("StopOtherSmoking","System.Boolean","Character","System.String");
    Console.WriteLine("Quad's Cigars: public smoking API v1 and symbols verified; Pipes has no Cigars assembly binding.");
}
Console.WriteLine("All native crop/terrain/companion APIs, private members, and Harmony targets match the installed game.");
Console.WriteLine("Published plugin metadata matches its catalog; DLL/PDB symbols are readable by the installed Cecil.");
