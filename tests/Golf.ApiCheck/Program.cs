using Mono.Cecil;
if(args.Length!=2)throw new Exception("Expected game directory and Golf.dll");
using var resolver=new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(args[0],"valheim_Data/Managed"));resolver.AddSearchDirectory(Path.Combine(args[0],"BepInEx/core"));
using var golf=AssemblyDefinition.ReadAssembly(args[1],new ReaderParameters{AssemblyResolver=resolver});
int count=0;
foreach(MemberReference member in golf.MainModule.GetMemberReferences())
{
    string scope=member.DeclaringType.Scope.Name;
    if(!scope.StartsWith("UnityEngine")&&!scope.StartsWith("assembly_")&&scope!="Unity.TextMeshPro"&&scope!="Splatform"&&scope!="BepInEx"&&scope!="0Harmony")continue;
    if(member is MethodReference method&&method.Resolve()==null)throw new Exception("Missing native method: "+method.FullName);
    if(member is FieldReference field&&field.Resolve()==null)throw new Exception("Missing native field: "+field.FullName);
    count++;
}
using var game=AssemblyDefinition.ReadAssembly(Path.Combine(args[0],"valheim_Data/Managed/assembly_valheim.dll"),new ReaderParameters{AssemblyResolver=resolver});
foreach(var hook in new[]{("Player","TakeInput"),("PlayerController","TakeInput"),("ZNetScene","Awake"),("ObjectDB","UpdateRegisters"),("Player","SetControls"),("Humanoid","StartAttack"),("Humanoid","OnAttackTrigger"),("Character","Awake"),("Menu","Update"),("ZSyncTransform","ClientSync"),("ZSyncTransform","OwnerSync")})
{
    var type=game.MainModule.Types.Single(t=>t.Name==hook.Item1);
    if(type.Methods.Count(m=>m.Name==hook.Item2)!=1)throw new Exception("Missing/ambiguous Harmony hook: "+hook);
}
foreach(string field in new[]{"m_isKinematicBody","m_useGravity"})
    if(!game.MainModule.Types.Single(t=>t.Name=="ZSyncTransform").Fields.Any(f=>f.Name==field&&f.FieldType.FullName=="System.Boolean"))throw new Exception("Missing sync field: "+field);
if(!game.MainModule.Types.Single(t=>t.Name=="Humanoid").Fields.Any(f=>f.Name=="m_rightItem"&&f.FieldType.FullName=="ItemDrop/ItemData"))throw new Exception("Missing equipped-item field");
if(!game.MainModule.Types.Single(t=>t.Name=="WearNTear").Fields.Any(f=>f.Name=="m_renderers"&&f.FieldType.FullName=="System.Collections.Generic.List`1<UnityEngine.Renderer>"))throw new Exception("Missing highlight renderer cache");
if(golf.MainModule.AssemblyReferences.Any(r=>r.Name.Contains("Windows")||r.Name=="System.Windows.Forms"))throw new Exception("Windows-only dependency");
Console.WriteLine($"Golf native API passed: {count} game/Unity/BepInEx member references resolved; Harmony hooks unambiguous; no Windows dependency. MVID {golf.MainModule.Mvid}.");
