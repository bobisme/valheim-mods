using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The Warstone: a standing stone you build with the hammer. It wards everyone near it (more with each siege it has held), is the
    // crafting station for the staves, and is what the horde marches on. Shift+E on it sounds the war horn.
    internal static class Stone
    {
        internal const string PrefabName="BobWarstone",StationName="Warstone";
        internal static int Hash=>PrefabName.GetStableHashCode();
        // Saved on the stone.
        internal const string MarksKey="bob_sw_marks",CrackedKey="bob_sw_cracked",PhaseKey="bob_sw_phase",HealthKey="bob_sw_health",MaxHealthKey="bob_sw_maxhealth",
            RiftKey="bob_sw_rift",StartKey="bob_sw_start",SiegeKey="bob_sw_siege",PlanKey="bob_sw_plan",WaveKey="bob_sw_wave",QueueKey="bob_sw_queue",
            WaveAtKey="bob_sw_waveat",SpawnedKey="bob_sw_spawned",StageKey="bob_sw_stage",CauseKey="bob_sw_cause",CooldownKey="bob_sw_cooldown",
            CalledKey="bob_sw_called",HeldKey="bob_sw_held",BaseKey="bob_sw_base",FallenKey="bob_sw_fallen",KillsKey="bob_sw_kills",QuietKey="bob_sw_quiet";
        private static GameObject _prefab;
        internal static GameObject Prefab=>_prefab;
        internal static CraftingStation Station=>_prefab!=null?_prefab.GetComponent<CraftingStation>():null;

        internal static GameObject Ensure()
        {
            if(_prefab!=null)return _prefab;
            GameObject bench=Assets.Find("piece_workbench"),look=Assets.Find("BossStone_Eikthyr");
            if(bench==null||look==null||ZNetScene.instance==null)return null;
            GameObject go=Object.Instantiate(bench,Assets.Holder);
            go.name=PrefabName;
            // Keep the workbench's working parts (station, area marker, base area); swap its table for the stone.
            foreach(string part in new[]{"floor_2x2_snow","New","Worn","Broken","WorkbenchBroken_Destruction","collider"})
            {Transform t=go.transform.Find(part);if(t!=null)Object.DestroyImmediate(t.gameObject);}
            Object.DestroyImmediate(go.GetComponent<WearNTear>()); // the horde's blows go to the siege, not the piece
            int layer=LayerMask.NameToLayer("piece");
            Transform source=look.transform.Find("Model");
            var model=new GameObject("Model"){layer=layer};
            model.transform.SetParent(go.transform,false);model.transform.localScale=Vector3.one*1.1f;
            model.AddComponent<MeshFilter>().sharedMesh=source.GetComponent<MeshFilter>().sharedMesh;
            model.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
            var solid=model.AddComponent<MeshCollider>();solid.sharedMesh=source.GetComponent<MeshCollider>()?.sharedMesh??source.GetComponent<MeshFilter>().sharedMesh;
            // Convex: the game sets a ghost down by its nearest convex collider, and with none it puts it far off in the sky.
            solid.convex=true;
            // The boss stone's glow and light, lit while a siege is on.
            Transform effects=look.transform.Find("active_effects");
            if(effects!=null)
            {
                GameObject glow=Object.Instantiate(effects.gameObject,go.transform,false);
                glow.name="SiegeGlow";glow.SetActive(false);
                foreach(GuidePoint g in glow.GetComponentsInChildren<GuidePoint>(true))Object.DestroyImmediate(g.gameObject);
                foreach(Light l in glow.GetComponentsInChildren<Light>(true)){l.color=new Color(1f,0.25f,0.15f);l.transform.localPosition=new Vector3(0,2.6f,-1.6f);}
            }
            Transform roof=go.transform.Find("roof_check_pint");if(roof!=null)roof.localPosition=new Vector3(0,2.2f,0);
            Transform connect=go.transform.Find("connectionEffectPoint");if(connect!=null)connect.localPosition=new Vector3(0,2.6f,0);
            CircleProjector marker=go.GetComponentInChildren<CircleProjector>(true);
            if(marker!=null){marker.m_radius=Policy.WardRadius;marker.m_nrOfSegments=80;}

            Piece piece=go.GetComponent<Piece>();
            piece.m_name=StationName;
            piece.m_description="A standing stone that wards your home: regeneration, carry weight and more for all within 40 m, twice as strong under a roof by a fire. "+
                "It grows stronger with every siege it holds. Shift+E sounds its war horn and calls a horde to break against it; staves are crafted here.";
            piece.m_category=Piece.PieceCategory.Misc;
            piece.m_comfort=0;
            GameObject pillar=Assets.Find("stone_pillar");
            if(pillar!=null&&pillar.GetComponent<Piece>() is Piece stonePiece)piece.m_placeEffect=stonePiece.m_placeEffect;
            piece.m_resources=new[]{Need("Stone",30),Need("Wood",10),Need("Flint",5),Need("Resin",5)}.Where(r=>r.m_resItem!=null).ToArray();
            piece.m_craftingStation=bench.GetComponent<CraftingStation>();
            Sprite icon=Assets.Icon(look,PrefabName,Quaternion.Euler(0,-30,0));
            if(icon!=null)piece.m_icon=icon;

            CraftingStation station=go.GetComponent<CraftingStation>();
            station.m_name=StationName;station.m_icon=piece.m_icon;
            station.m_craftRequireRoof=false;station.m_craftRequireFire=false;station.m_showBasicRecipies=false;station.m_useDistance=3.5f;
            station.m_rangeBuild=Policy.PowerRadius(Policy.MaxLevel); // sockets and upgrades are built within its farthest reach

            var target=go.AddComponent<StaticTarget>();target.m_primaryTarget=false;target.m_randomTarget=false; // only sieges aim at it
            go.AddComponent<Warstone>();
            _prefab=go;
            return go;
        }
        private static Piece.Requirement Need(string item,int amount)=>new Piece.Requirement{m_resItem=Assets.Find(item)?.GetComponent<ItemDrop>(),m_amount=amount,m_recover=true};
        internal static void Forget(){_prefab=null;}
    }

    internal sealed class Warstone:MonoBehaviour,IDestructible
    {
        internal static readonly List<Warstone> Loaded=new List<Warstone>();
        internal ZNetView View;
        internal StaticTarget Target;
        private GameObject _glow;
        private float _askedAt=-100,_nextMarker;private CircleProjector _marker;
        internal ZDO Z=>View!=null&&View.IsValid()?View.GetZDO():null;
        internal Phase Phase=>(Phase)(Z?.GetInt(Stone.PhaseKey,0)??0);
        internal int Marks=>Z?.GetInt(Stone.MarksKey,0)??0;
        internal bool Cracked=>Z?.GetBool(Stone.CrackedKey,false)??false;
        internal int Strength=>Policy.Strength(Marks,Cracked);
        // Its level: 1 + the upgrades built around it (the game's own station extensions).
        private CraftingStation _station;
        internal int Level=>Policy.Level(_station!=null?_station.GetExtentionCount(true):0);
        internal long Siege=>Z?.GetLong(Stone.SiegeKey,0L)??0L;
        internal Vector3 Rift=>Z?.GetVec3(Stone.RiftKey,transform.position)??transform.position;

        private void Awake()
        {
            View=GetComponent<ZNetView>();Target=GetComponent<StaticTarget>();_station=GetComponent<CraftingStation>();_marker=GetComponentInChildren<CircleProjector>(true);
            if(View==null||!View.IsValid())return;
            Transform glow=transform.Find("SiegeGlow");_glow=glow!=null?glow.gameObject:null;
            Loaded.Add(this);
        }
        private void OnDestroy(){Loaded.Remove(this);Director.Forget(this);}
        private void Update()
        {
            if(View==null||!View.IsValid())return;
            if(_glow!=null&&_glow.activeSelf!=(Phase!=Phase.Idle))_glow.SetActive(Phase!=Phase.Idle);
            // The ring shown while you look at it marks how far it feeds staves.
            if(_marker!=null&&Time.time>=_nextMarker){_nextMarker=Time.time+2;float reach=Policy.PowerRadius(Level);if(Mathf.Abs(_marker.m_radius-reach)>0.1f)_marker.m_radius=reach;}
            if(View.IsOwner()){Director.Run(this);Director.Footing(this);}
        }

        // ---- hot reload: the old copy's component comes off, the new one goes on ----
        internal static void DetachAll(){foreach(Warstone w in Loaded.ToList())if(w!=null)Object.Destroy(w);Loaded.Clear();}
        internal static void AttachAll()
        {
            foreach(ZNetView view in Assets.Instances(Stone.Hash))
            {
                foreach(MonoBehaviour stale in view.GetComponents<MonoBehaviour>())if(stale!=null&&stale.GetType().Name==nameof(Warstone)&&stale.GetType()!=typeof(Warstone))Object.DestroyImmediate(stale);
                if(view.GetComponent<Warstone>()==null)view.gameObject.AddComponent<Warstone>();
            }
        }

        // ---- the horn ----
        internal bool Horn(Player player)
        {
            if(Phase==Phase.Battle&&Director.CanCallEarly(this,out int bonus)){Net.Call(this,Cause.Early);return true;}
            if(Phase!=Phase.Idle){player.Message(MessageHud.MessageType.Center,Phase==Phase.Battle?"The horn can call the next wave once this one is all out of the rift.":"The horde is already coming.");return true;}
            double cooldown=(Z.GetLong(Stone.CooldownKey,0L)-ZNet.instance.GetTime().Ticks)/(double)System.TimeSpan.TicksPerSecond;
            if(cooldown>0){player.Message(MessageHud.MessageType.Center,$"The stone is still ringing from the last siege. Wait {Mathf.CeilToInt((float)cooldown/60)} more minutes.");return true;}
            if(Time.time-_askedAt>6)
            {
                _askedAt=Time.time;
                player.Message(MessageHud.MessageType.Center,$"Sound the war horn? A {Policy.Rosters[Director.StageNow()].Name} will gather and march on this stone.\nShift+E again to call them.");
                return true;
            }
            _askedAt=-100;
            Net.Call(this,Cause.Horn);
            return true;
        }

        // ---- the hover line ----
        internal string Hover()
        {
            string line=$"{Stone.StationName} <color=#E8C070>{Policy.Title(Marks)}{(Marks>0?" ("+Policy.Numeral(Marks)+")":"")}</color>";
            if(Cracked)line+=" <color=#E05040>cracked</color>";
            switch(Phase)
            {
                case Phase.Gathering:
                    double wait=(Z.GetLong(Stone.StartKey,0L)-ZNet.instance.GetTime().Ticks)/(double)System.TimeSpan.TicksPerSecond;
                    line+=$"\n<color=#FF7050>The horde gathers: {Mathf.Max(0,Mathf.CeilToInt((float)wait))} s</color>";break;
                case Phase.Battle:
                    float health=Z.GetFloat(Stone.HealthKey,1),max=Mathf.Max(1,Z.GetFloat(Stone.MaxHealthKey,1));
                    var plan=Policy.Load(Z.GetString(Stone.PlanKey,""));
                    line+=$"\n<color=#FF7050>Siege: wave {Mathf.Min(plan.Count,Z.GetInt(Stone.WaveKey,0)+1)} of {plan.Count} · stone {Mathf.RoundToInt(100*health/max)}%</color>";
                    string next=Director.NextWaveText(this);
                    if(next!="")line+=$"\n<color=#C0C0C0>Next: {next}</color>";
                    if(Director.CanCallEarly(this,out int early))line+=$"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Call the next wave now (+{early} warshards)";
                    break;
                default:
                    line+="\n[<color=yellow><b>$KEY_Use</b></color>] Staves   [<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Sound the war horn";
                    if(Player.m_localPlayer!=null&&!Player.m_localPlayer.IsRecipeKnown(Policy.Staves[0].Name))
                        line+="\n<color=#A0A0A0>Hold a siege: the horde's warshards teach you its staves</color>";
                    break;
            }
            int level=Level,fed=Planted.Loaded.Count(p=>p!=null&&p.Holding&&p.Stone()==this);
            line+=$"\n<color=#C0C0C0>Level {level} of {Policy.MaxLevel}: feeds {fed}/{Policy.Capacity(level)} staves within {Policy.PowerRadius(level):0} m</color>";
            int held=Z.GetInt(Stone.HeldKey,0),fallen=Z.GetInt(Stone.FallenKey,0);
            if(held+fallen>0)line+=$"\n<color=#A0A0A0>Sieges held {held}, fallen {fallen}</color>";
            return Localization.instance.Localize(line);
        }

        // ---- the horde's blows (from any player's game) ----
        public DestructibleType GetDestructibleType()=>DestructibleType.Default;
        public void Damage(HitData hit)
        {
            if(Phase!=Phase.Battle||hit==null)return;
            Character attacker=hit.GetAttacker();
            if(attacker==null||attacker.GetComponent<Raider>()==null)return; // only the horde wears it down
            float damage=hit.GetTotalDamage();
            if(damage<=0)return;
            Net.Damage(this,damage,hit.m_point);
        }
    }

    // The stone is a crafting station with a horn: Shift+E (alt use) sounds it, its level is its marks, and the hover tells its story.
    [HarmonyPatch(typeof(CraftingStation),nameof(CraftingStation.Interact))]
    internal static class SoundHorn
    {
        private static bool Prefix(CraftingStation __instance,Humanoid user,bool repeat,bool alt,ref bool __result)
        {
            Warstone stone=__instance.GetComponent<Warstone>();
            if(stone==null)return true;
            if(repeat){__result=false;return false;}
            if(!alt)return true;
            __result=user is Player player&&player==Player.m_localPlayer&&stone.Horn(player);
            return false;
        }
    }
    [HarmonyPatch(typeof(CraftingStation),nameof(CraftingStation.GetHoverText))]
    internal static class StoneHover
    {
        private static void Postfix(CraftingStation __instance,ref string __result)
        {
            Warstone stone=__instance.GetComponent<Warstone>();
            if(stone!=null&&Player.m_localPlayer!=null&&__instance.InUseDistance(Player.m_localPlayer))__result=stone.Hover();
        }
    }
    // Not while a siege is on.
    [HarmonyPatch(typeof(Piece),nameof(Piece.CanBeRemoved))]
    internal static class KeepDuringSiege
    {
        private static void Postfix(Piece __instance,ref bool __result)
        {
            if(__result&&__instance.GetComponent<Warstone>() is Warstone stone&&stone.Phase!=Phase.Idle)__result=false;
        }
    }
    // One Warstone to a home: none within 80 m of another.
    [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
    internal static class OneStone
    {
        private static readonly AccessTools.FieldRef<Player,GameObject> Ghost=AccessTools.FieldRefAccess<Player,GameObject>("m_placementGhost");
        private static bool Prefix(Player __instance,Piece piece,ref bool __result)
        {
            if(piece==null||piece.gameObject.name!=Stone.PrefabName&&Utils.GetPrefabName(piece.gameObject)!=Stone.PrefabName)return true;
            GameObject ghost=Ghost(__instance);
            Vector3 at=ghost!=null?ghost.transform.position:__instance.transform.position;
            if(Warstone.Loaded.Any(w=>w!=null&&Vector3.Distance(w.transform.position,at)<Policy.MinApart))
            {
                __instance.Message(MessageHud.MessageType.Center,"Another Warstone stands too near. One to a home.");
                __result=false;return false;
            }
            return true;
        }
    }
}
