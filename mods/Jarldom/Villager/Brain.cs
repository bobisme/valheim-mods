using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // One villager's mind, on the game that owns its body. Movement runs every fixed update; decisions a few times a second (Villagers.Tick
    // spreads them over frames). State lives in S and is written to the ZDO every few seconds when it changed.
    internal sealed class Brain
    {
        public readonly Humanoid Body; public readonly MonsterAI Ai; public readonly ZNetView View; public readonly Mover Move;
        public VillagerState S; public Colony Colony;
        public bool Stripped; // (bathing: the armour stays off till they dress again)
        public float SongUntil, LoadSince; private float _lastWorth = -1; public int DimStage = -1;
        public Task Task; public Activity Activity = Activity.Idle; public string Doing = "";
        public string LastAttacker = ""; public Character Foe; private float _foeUntil;
        public bool KeepDoorsOpen; public float Stamina = 1f;
        public bool Fighting, Combat_Bow, Fled; public int Swings, SwingsStarted; public string CombatNote = ""; public float BlockUntil, NoBlockUntil; public Character FightTarget; public float FleeUntil; public float BlindSince = -1, UnseenUntil; public Character Unseen; public Character ShotAt; public float ShotHp; public int ShotsSince;
        private bool _dirty; private float _nextGreet, _nextSave, _nextNeeds, _nextGear, _nextFoes, _nextMoodSync, _nextWork, _born;
        private double _lastNeeds;
        private readonly List<Meal> _meals = new List<Meal>();
        private readonly Rigidbody _body;
        public Seat Held; // sitting or lying: the spot we're snapped to
        public readonly HashSet<string> Hauled = new HashSet<string>(); // gear picked up off the ground for the stores (not theirs to keep)

        public sealed class Meal { public string Prefab; public float Health, Stamina, Left, Total; }
        public sealed class Seat { public Transform Point; public string Anim; public Vector3 Offset; public float Since; }

        public Brain(Humanoid h, MonsterAI ai)
        {
            Body = h; Ai = ai; View = h.GetComponent<ZNetView>(); Move = new Mover(this); _body = h.GetComponent<Rigidbody>(); _born = Time.time;
            if (_body != null && _body.isKinematic) _body.isKinematic = false; // (a body left seated when ownership went elsewhere)
            Poses.Clear(h);
        }
        public float NoRiteUntil, NoStoreUntil, NoFoodUntil, NoRepairUntil, NoBuildUntil, NoTradeUntil, NoCraftUntil, NoHealUntil, NoScoutUntil, NoOfferUntil, NextMischief, NoCartUntil, NoGearUntil, NoGraveUntil, LeaderGoneAt, NoUpkeepUntil, NoCatchUntil, NoClaimUntil, NoCornerUntil, NoFestUntil, NoRangeUntil, NoForageUntil; public readonly HashSet<int> LampsTried = new HashSet<int>(); private float _nextStep; private bool _captureRolled; private int _bedMissing; private float _nextDecide; public float LastDecide, DecideDt = 0.25f; public double GameDt; private double _lastWorld;
        public bool Alive => Body != null && !Body.IsDead() && View != null && View.IsValid() && View.IsOwner() && S != null;
        public Vector3 Pos => Body.transform.position;
        public double Now => ZNet.instance.GetTimeSeconds();

        public bool Load()
        {
            ZDO z = View.GetZDO();
            try { S = VillagerState.Load(SoulStore.StateOf(z)); }
            catch (Exception e) { Plugin.Warn("Jarldom: a villager's state could not be read: " + e.Message); S = null; }
            if (S == null) return false;
            Colony = Colony.Of(S.Colony);
            Body.m_name = S.FullName;
            LoadMeals(Villagers.Tags(z).GetString(MealKey, "") is string m && m != "" ? m : z.GetString(MealKey, ""));
            _lastNeeds = Now;
            Body.SetMaxHealth(MaxHealth());
            return true;
        }
        public void Dirty() => _dirty = true;
        public string ShipShort; // (a shipwright: what the stores lacked for the ship, last time)
        public void Save()
        {
            if (!View.IsValid() || !View.IsOwner()) return;
            _dirty = false;
            ZDO z = View.GetZDO();
            SoulStore.Write(z, S, Colony);
            if (z.GetString(Villagers.NameKey, "") != S.FullName) z.Set(Villagers.NameKey, S.FullName);
            string meals = SaveMeals();
            Villagers.Tag(z, MealKey, meals);
        }
        // Let go of the body: stand up, stop, save (a reload, a death, ownership moving away).
        public void Release()
        {
            Stand();
            Task?.Stop(this); Task = null;
            Move.Stop();
            Body_.Blocking(Body) = false;
            if (S != null && View != null && View.IsValid() && View.IsOwner()) Save();
        }

        // ---- every fixed update ----
        public void Fixed(float dt)
        {
            if (Held != null) { Hold(); return; }
            Move.Step(dt);
            Combat.Fixed(this, dt);
        }
        private void Hold()
        {
            if (Held.Point == null) { Stand(); return; }
            Body.transform.SetPositionAndRotation(Held.Point.position + Held.Point.rotation * Held.Offset, Held.Point.rotation);
            if (_body != null) { _body.isKinematic = true; }
            Body_.Anim(Body)?.SetBool(Held.Anim, true);
        }
        public void Sit(Transform point, string anim, Vector3 offset)
        {
            Move.Stop();
            Held = new Seat { Point = point, Anim = anim, Offset = offset, Since = Time.time };
            Body.HideHandItems(false, false);
            Hold();
        }
        public void Stand()
        {
            if (Held == null) return;
            ZSyncAnimation anim = Body_.Anim(Body);
            anim?.SetBool(Held.Anim, false);
            Poses.Clear(Body);
            Vector3 off = Held.Point != null ? Held.Point.position + Held.Point.forward * 0.9f + Vector3.up * 0.3f : Pos;
            Vector3? floor = Held.Point != null ? Floor(Held.Point) : null;
            Held = null;
            if (_body != null) { _body.isKinematic = false; _body.linearVelocity = Vector3.zero; }
            if (floor.HasValue) Place(floor.Value + Vector3.up * 0.2f);
            else if (Steer.SafeSpot(off)) Place(off);
            Seats.Free(this);
        }
        // Where to stand up from a seat or bed: floor beside it (front first, then behind and the sides) with a way from there to the
        // pillars. A bench against a wall whose front faces the wall put them in the gap behind it, off the navmesh with no way out.
        // (Floor, not the seat's top: bench and table tops are navmesh too, at about the seat's height.)
        private Vector3? Floor(Transform seat)
        {
            if (Pathfinding.instance == null) return null;
            Vector3 p = seat.position, f = seat.forward; f.y = 0; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            Vector3 r = Vector3.Cross(Vector3.up, f);
            Vector3 home = Colony != null ? Colony.Position + Colony.Seat.transform.forward * 3f : Vector3.zero;
            if (Colony != null && Nav.Valid(out Vector3 h, home, 3f)) home = h; // (the spot before the high seat may be under a table)
            foreach (Vector3 d in new[] { f * 0.9f, f * 1.4f, -f * 0.9f, -f * 1.4f, r * 0.9f, -r * 0.9f })
            {
                if (!Nav.Valid(out Vector3 v, p + d, 0.6f) || v.y > p.y - 0.2f || p.y - v.y > 1.5f || Utils.DistanceXZ(v, p) > 1.8f) continue;
                if (Colony != null && !Pathfinding.instance.HavePath(v, home, Pathfinding.AgentType.Humanoid)) continue;
                return v;
            }
            return null;
        }
        public void Place(Vector3 at)
        {
            Body.transform.position = at;
            if (_body != null) { _body.position = at; _body.linearVelocity = Vector3.zero; }
            View.GetZDO()?.SetPosition(at); // (none while the world shuts down)
        }

        // ---- decisions ----
        public void Decide()
        {
            if (!Alive) return;
            if (Time.time < _nextDecide) return;
            DecideDt = Mathf.Clamp(Time.time - LastDecide, 0.05f, 2f); LastDecide = Time.time;
            // (the world's seconds since the last decision: what needs fill and work counts by, as the clock runs, which isn't always
            // as fast as the real one: sleeping players, a faster day; a jump of the clock counts as an hour at most)
            double world = Now; GameDt = _lastWorld > 0 ? Math.Min(Math.Max(0, world - _lastWorld), Clock.Hour) : 0; _lastWorld = world;
            _nextDecide = Time.time + 0.25f; // four times a second is plenty
            // (set down far out by the Valkyrie: waiting by their fire to be found, Scattered.cs)
            if (Scattered.Stranded(this)) { if (Time.time >= _nextGear) { _nextGear = Time.time + 1f; Gear.Maintain(this); } if (Time.time >= _nextFoes) { _nextFoes = Time.time + 0.3f; Combat.Think(this); } if (!Combat.Busy(this)) { Task?.Stop(this); Task = null; Idle(Scattered.Wait(this)); } return; }
            if (Colony == null || !Colony.Valid) { Colony = Colony.Of(S.Colony); if (Colony == null) { if (Follow.LeaderId(this) != 0) Away(); else Idle("waiting for the hall to load"); return; } }
            Follow.Told(this); // (a homecoming told while the hall was far away)
            if (View.GetZDO().GetLong(Scattered.RescuerKey, 0L) != 0) Plugin.Guard(() => Scattered.Home(this)); // (found out in the wilds, and home now)
            double now = Now;
            Split.Begin();
            if (Time.time >= _nextNeeds) { _nextNeeds = Time.time + 5f; Needs(now); Split.Mark("needs"); }
            if (Time.time >= _nextGear) { _nextGear = Time.time + 1f; Gear.Maintain(this); Split.Mark("gear"); }
            if (Time.time >= _nextFoes) { _nextFoes = Time.time + 0.3f; Combat.Think(this); Split.Mark("foes"); }
            if (Time.time >= _nextMoodSync) { _nextMoodSync = Time.time + 2f; SyncHover(); }
            if (Time.time >= _nextGreet) { _nextGreet = Time.time + 1.5f; Plugin.Guard(() => Greet.Near(this)); Plugin.Guard(() => Sky.Look(this)); Plugin.Guard(() => Songs.Tick(this)); Split.Mark("greet"); }
            if (Time.time >= _nextStep) { _nextStep = Time.time + 2f; Upkeep.Step(this); Split.Mark("step"); } // (where feet wear a path)
            if (_dirty && Time.time >= _nextSave) { _nextSave = Time.time + 30f; Save(); } // (the soul is sent to everyone when written: twice a minute is plenty; deaths and commands save at once)
            if (Combat.Busy(this)) return;
            Activity want = Want(); Split.Mark("want");
            // Sleep, a trade run, the road to Valhalla: carried on to the end unless a raid comes.
            // (the Thing called: the day's chores are put down at once; a trip, a cart, a boar on a rope are seen through)
            bool chore = Task is WorkTask || Task is UpkeepTask || Task is CraftTask || Task is DepositTask || Task is OutfitTask || Task is RepairTask || Task is BuildTask || Task is GothiTask || Task is HealTask || Task is EatTask;
            if (Task != null && !Task.Done && !Task.Interruptible && want != Activity.Defend && !(want == Activity.Thing && chore)) { Task.Tick(this); Split.Mark("tick"); if (Task != null && Alive) SetDoing(Task.Doing); return; }
            if (want != Activity || Task == null || Task.Done)
            {
                if (Task != null && (Task.Done || want != Activity)) { Task.Stop(this); Task = null; }
                Activity = want;
                Task = Choose(want); Split.Mark("choose");
                Task?.Start(this); Split.Mark("start");
            }
            Task?.Tick(this); Split.Mark("tick");
            if (Task != null) SetDoing(Task.Doing);
            if (Activity == Activity.Work && Task != null && !(Task is WanderTask)) CatchUp.Work(this, (float)GameDt); // (working hours, for the rates kept while away)
        }

        private bool Hurt => Body.GetHealthPercentage() < 0.1f || S.InjuredUntil > Now;
        private Activity Want()
        {
            if (Follow.Check(this)) return Activity.Follow; // (out with the jarl: a raid at home is the guards' to meet)
            if (Colony.RaidOn || Colony.HornUntil > Time.time) return Activity.Defend;
            if (Thing.Held(Colony) && Thing.Comes(this)) return Activity.Thing; // (everyone to the Thing)
            if (Hurt) return S.Needs[(int)Need.Food] < Mood.EatBelow && Time.time > NoFoodUntil && Food.CanEat(this) ? Activity.Wake : Activity.Home;
            Activity a = Schedule.At(Colony.Hour, S.Traits, heavyRain: Storm(), outdoorTask: !Indoors(S.Prof), urgent: false); // (a storm sends in those who work outside; the healer, gothi, weaver, smith and cook work under a roof)
            if (a == Activity.Work || a == Activity.Wake) { if (S.Needs[(int)Need.Food] < Mood.EatBelow && Time.time > NoFoodUntil && Food.CanEat(this)) return Activity.Wake; }
            return a;
        }
        private static bool Indoors(Prof p) => p == Prof.Healer || p == Prof.Gothi || p == Prof.Weaver || p == Prof.Smith || p == Prof.Cook;
        // A storm (thunder, or rain in a gale) drives them in; plain rain doesn't: it lasts, and work in the wet is still work.
        private static float _stormAt; private static bool _storm;
        private static bool Storm()
        {
            if (Time.time - _stormAt < 5f) return _storm;
            _stormAt = Time.time;
            string env = EnvMan.instance?.GetCurrentEnvironment()?.m_name ?? "";
            _storm = EnvMan.IsWet() && (env.IndexOf("Thunder", StringComparison.OrdinalIgnoreCase) >= 0 || env.IndexOf("Storm", StringComparison.OrdinalIgnoreCase) >= 0 || EnvMan.instance.GetWindIntensity() > 0.85f);
            return _storm;
        }
        private Task Choose(Activity a)
        {
            // (coins brought home, from a trade run or found: to the treasury first, work hours or not; carried round the hall till
            // morning, they were the treasury short for a payday)
            if ((a == Activity.Wake || a == Activity.Work || a == Activity.Hall || a == Activity.Idle) && Time.time > NoStoreUntil
                && Body.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Coins" && Work.Stores(this, i)) is ItemDrop.ItemData coins
                && Stock.Destination(Colony, coins, Pos) != null) return new DepositTask();
            switch (a)
            {
                case Activity.Sleep: return new SleepTask();
                case Activity.Home: return new SleepTask { JustRest = Hurt && Colony.Hour < 21 }; // (an early riser home at eight is going to bed: a rest is for the hurt, and ends when they mend)
                case Activity.Wake: return Food.CanEat(this) ? new EatTask() : (Task)new WanderTask(Colony.Position, 8, "getting up");
                case Activity.Work:
                    {
                        // (a stray load for the stores, carried five minutes by one whose work never empties their bag: a gothi, a skald, a
                        // healer: put away first)
                        int load = Work.Carrying(this);
                        if (load == 0) LoadSince = 0; else if (LoadSince == 0) LoadSince = Time.time;
                        if (load > 0 && Time.time - LoadSince > 300f && Time.time > NoStoreUntil) { LoadSince = Time.time; return new DepositTask(); }
                        if (Bath.Due(this)) return new BathTask(); // (washing day: their turn in the sauna)
                        Task work = Work.Next(this); Split.Mark("work.next"); if (work != null) { Leisure.Clean(this); return work; }
                        // (nothing to do: first whatever's in the bag to the stores, a light bag being a small body to send; then some sport, or the hall)
                        if (Time.time > NoStoreUntil && Work.Carrying(this) > 0 && Body.GetInventory().GetAllItems().Where(i => Work.Stores(this, i)).Any(i => Stock.Destination(Colony, i, Pos) != null)) return new DepositTask();
                        Split.Mark("deposit?");
                        return Leisure.Next(this, Gear.Problem ?? Work.WhyIdle(this));
                    }
                case Activity.Hall: Bath.Evening(this); return Work.Lamplight(this) ?? (Bath.Due(this) ? new BathTask() : (Task)new HallTask()); // (the hauler sees to the fires and torches first; on washing day, a turn in the sauna before the hall)
                case Activity.Shelter: return Leisure.Next(this, "out of the storm", indoors: true); // (games and talk under a roof)
                case Activity.Defend: return new DefendTask();
                case Activity.Follow: return new FollowTask();
                case Activity.Thing: return new ThingTask();
                default: return new WanderTask(Colony.Position, 10, "idle");
            }
        }
        public void Idle(string why) { SetDoing(why); }
        public void SetDoing(string doing)
        {
            if (doing == Doing) return;
            Doing = doing;
            Villagers.Tag(View.GetZDO(), Villagers.DoingKey, doing);
        }
        private void SyncHover()
        {
            int mood = Mood.Of(S, Now);
            ZDO z = View.GetZDO();
            if (z.GetInt(Villagers.MoodKey, -999) != mood) z.Set(Villagers.MoodKey, mood);
        }

        // ---- needs and vitals ----
        private void Needs(double now)
        {
            double hours = Math.Max(0, (now - _lastNeeds) / Clock.Hour);
            _lastNeeds = now;
            if (hours > 0)
            {
                // A jump of hours (players sleeping through the night, or the time set) that lands in the morning: the night passed and
                // they slept it, in a bed if they have one (rest isn't drained by the hours they spent asleep).
                float rest = S.Needs[(int)Need.Rest];
                Mood.Tick(S, hours);
                if (hours >= 3 && Colony != null && Colony.Hour >= Schedule.WakeAt - 1 && Colony.Hour < Schedule.WorkAt + 3)
                {
                    S.Needs[(int)Need.Rest] = Math.Max(rest, 100);
                    Mood.Add(S, Colony.BedById(S.Bed) != null ? "ownbed" : "hallfire", now);
                }
            }
            Saboteur.Tick(this);
            Mood.Expire(S, now);
            Plugin.Guard(() => Dim(now));
            Split.Mark("n.dim");
            // An ascension ordered and interrupted (a reload, the owner away): on again at dusk.
            if (Colony != null && S.Worthy && !(Task is AscendTask) && Ascensions.Pending(Colony.R.Flags, S.Vid) && Ascension.Why(Colony, S) == "") { Task?.Stop(this); Task = new AscendTask(Colony.Id); Task.Start(this); }
            // Shelter: dry and warm (under a roof, or by a fire).
            Cover.GetCoverForPoint(Body.GetCenterPoint(), out float cover, out bool roof);
            bool sheltered = cover >= 0.8f && roof, warm = EffectArea.IsPointInsideArea(Pos, EffectArea.Type.Heat) != null;
            Split.Mark("n.cover");
            if (sheltered || warm) Jarldom.Mood.Satisfy(S, Need.Shelter, (float)hours * (warm && sheltered ? 6 : 3));
            else if (EnvMan.IsWet()) { Jarldom.Mood.Satisfy(S, Need.Shelter, -(float)hours * 4); if (!Jarldom.Mood.Has(S, "rain", now)) Jarldom.Mood.Add(S, "rain", now); }
            // Company: working or resting near someone of the colony keeps loneliness off a little (the evening in the hall does most).
            if (Villagers.All.Any(o => o != this && o.S.Colony == S.Colony && Vector3.Distance(o.Pos, Pos) < 8f)) Jarldom.Mood.Satisfy(S, Need.Social, (float)hours * 0.8f);
            // Safety: walls and guards near, no threats seen lately.
            float safe = (Colony.RoomAt(Pos) != null ? 1 : 0) + (Villagers.All.Any(b => b.S.Prof == Prof.Guard && b.S.Colony == S.Colony) ? 1 : 0);
            Jarldom.Mood.Satisfy(S, Need.Safety, (float)hours * (safe * 1.5f));
            // Beauty: the rooms they live in.
            Room r = Colony.RoomAt(Pos);
            if (r != null && r.Ok) Jarldom.Mood.Satisfy(S, Need.Beauty, (float)hours * (1 + Tables.GradeRank(r.Grade)));
            // Faith: Odin's shrine and temples in the colony keep it up a little every day.
            int shrines = Colony.R.Rooms.Count(x => x.Ok && (x.Type == RoomType.OdinShrine || x.Type == RoomType.Temple));
            if (shrines > 0) Jarldom.Mood.Satisfy(S, Need.Faith, (float)hours * 0.6f * Mathf.Min(3, shrines));
            Split.Mark("n.rooms");
            if (S.Has("pious") && shrines == 0) { if (!Jarldom.Mood.Has(S, "neglect", now)) Jarldom.Mood.Add(S, "neglect", now); } else Jarldom.Mood.Remove(S, "neglect");
            if (S.Needs[(int)Need.Food] < 15) { if (!Jarldom.Mood.Has(S, "hungry", now)) Jarldom.Mood.Add(S, "hungry", now); } else Jarldom.Mood.Remove(S, "hungry");
            if (S.Foods.Count >= 3 && S.Foods.Skip(S.Foods.Count - 3).Distinct().Count() == 1) { if (!Jarldom.Mood.Has(S, "samefood", now)) Jarldom.Mood.Add(S, "samefood", now); } else Jarldom.Mood.Remove(S, "samefood");
            if (!Expect.Met(Colony, S)) { if (!Jarldom.Mood.Has(S, "plainfare", now)) Jarldom.Mood.Add(S, "plainfare", now); } else Jarldom.Mood.Remove(S, "plainfare"); // (expectations rise with the colony's standing)
            if (!Weaving.InRags(this)) { if (!Jarldom.Mood.Has(S, "dressed", now)) Jarldom.Mood.Add(S, "dressed", now); } else Jarldom.Mood.Remove(S, "dressed");
            if (S.Worthy && !Jarldom.Mood.Has(S, "valkyries", now)) Jarldom.Mood.Add(S, "valkyries", now);
            if (!Jarldom.Mood.Has(S, "gary", now) && Held == null && Compat.Gary(Pos, 5f) is Character gary)
            {
                Jarldom.Mood.Add(S, "gary", now);
                Ai.LookTowards((gary.transform.position - Pos).normalized);
                Say(new[] { "Who's a good greydwarf?", "Hello, Gary.", "Gary! Have you been eating the resin again?", "Good greydwarf, Gary. Very good.", "Gary, you rascal. Put that resin down.", "Gary, keep those claws to yourself." }[UnityEngine.Random.Range(0, 6)], 4);
            }
            if (Body.GetHealthPercentage() > 0.5f) _captureRolled = false;
            if (Body.GetHealthPercentage() < 0.1f && S.InjuredUntil < now && !_captureRolled) { _captureRolled = true; if (Captives.Take(this)) return; } // (dragged off by the Fulings: one roll a fall)
            if (Body.GetHealthPercentage() < 0.1f && S.InjuredUntil < now && !Combat.Busy(this)) { S.InjuredUntil = now + Clock.DayLength / (Colony.Best(RoomType.HealersHouse)?.Ok == true ? 2 : 1); Colony.Log("injured", S.Name, ""); Jarldom.Mood.Add(S, "injured", now); }
            if (S.InjuredUntil > 0 && S.InjuredUntil < now) { S.InjuredUntil = 0; Jarldom.Mood.Remove(S, "injured"); }
            // A bed that is gone (broken, taken down) is no longer theirs, once its area is loaded and it still can't be found.
            if (!string.IsNullOrEmpty(S.Bed) && Vector3.Distance(Pos, Colony.Position) < Colony.Radius && Colony.BedById(S.Bed) == null) { if (++_bedMissing >= 3) { S.Bed = ""; _bedMissing = 0; Colony.ReleaseBed(S.Vid); Say(new[] { "I need a bed of my own.", "My bed is gone. I need a bed of my own.", "Someone took my bed. I need a bed of my own.", "The bed is gone, and I need a bed of my own.", "I need a bed of my own, and soon." }[UnityEngine.Random.Range(0, 5)], 4); } }
            else _bedMissing = 0;
            // Food burns as a player's does; health follows the meals.
            Food.Burn(this, 5f);
            Body.SetMaxHealth(MaxHealth());
            // Meals heal as a player's do; with none, a little anyway (the game's own idle healing is part of the AI we replace). Twice in bed.
            float regen = Mathf.Max(0.4f, _meals.Sum(m => m.Left > 0 ? Regen(m) : 0)) * (Held != null ? 2 : 1);
            if (regen > 0 && Body.GetHealth() < Body.GetMaxHealth()) Body.Heal(regen * 0.5f, false);
            Stamina = Mathf.Min(1f, Stamina + 0.25f);
            int mood = Jarldom.Mood.Of(S, now);
            if (mood < -60) { if (S.LowMoodSince == 0) S.LowMoodSince = now; } else S.LowMoodSince = 0;
            Society.Breakdowns(this, mood, now);
            Plugin.Guard(() => Asks.Daily(this));
            _dirty = true;
        }
        // Far from home with the jarl (the colony isn't loaded here): only what a follower needs. Hunger, healing, fighting, keeping up.
        private void Away()
        {
            double now = Now;
            if (Time.time >= _nextNeeds)
            {
                _nextNeeds = Time.time + 5f;
                double hours = Math.Max(0, (now - _lastNeeds) / Clock.Hour); _lastNeeds = now;
                Jarldom.Mood.Tick(S, hours); Jarldom.Mood.Expire(S, now);
                if (S.Needs[(int)Need.Food] < 15) { if (!Jarldom.Mood.Has(S, "hungry", now)) Jarldom.Mood.Add(S, "hungry", now); } else Jarldom.Mood.Remove(S, "hungry");
                Food.Burn(this, 5f);
                Body.SetMaxHealth(MaxHealth());
                float regen = Mathf.Max(0.4f, _meals.Sum(m => m.Left > 0 ? Regen(m) : 0));
                if (Body.GetHealth() < Body.GetMaxHealth()) Body.Heal(regen * 0.5f, false);
                Stamina = Mathf.Min(1f, Stamina + 0.25f);
                _dirty = true;
            }
            if (Time.time >= _nextGear) { _nextGear = Time.time + 1f; Gear.Maintain(this); }
            if (Time.time >= _nextFoes) { _nextFoes = Time.time + 0.3f; Combat.Think(this); }
            if (Time.time >= _nextMoodSync) { _nextMoodSync = Time.time + 2f; SyncHover(); }
            // (a state is only written to the soul at home: on a game that doesn't have the soul it would go on the moving body)
            if (_dirty && Time.time >= _nextSave) { _nextSave = Time.time + 60f; if (SoulStore.Of(View.GetZDO()) != null) Save(); }
            if (Combat.Busy(this)) return;
            if (!Follow.Check(this)) { Idle("far from home"); return; }
            if (!(Task is FollowTask || Task is CampTask) || Task.Done) { Task?.Stop(this); Activity = Activity.Follow; Task = new FollowTask(); Task.Start(this); }
            Task.Tick(this);
            if (Task != null) SetDoing(Task.Doing);
        }
        public float MaxHealth() => 25f + _meals.Where(m => m.Left > 0).Sum(m => m.Health * Mathf.Pow(Mathf.Clamp01(m.Left / Mathf.Max(1, m.Total)), 0.3f));
        private static float Regen(Meal m) { GameObject p = Assets.Find(m.Prefab); return p != null ? p.GetComponent<ItemDrop>().m_itemData.m_shared.m_foodRegen : 1f; }
        public List<Meal> Meals => _meals;
        private const string MealKey = "jarldom_food";
        private void LoadMeals(string s)
        {
            _meals.Clear();
            foreach (string part in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length < 5) continue;
                _meals.Add(new Meal { Prefab = f[0], Health = Num(f[1]), Stamina = Num(f[2]), Left = Num(f[3]), Total = Num(f[4]) });
            }
        }
        private string SaveMeals() => string.Join(";", _meals.Select(m => $"{m.Prefab}:{m.Health:0.#}:{m.Stamina:0.#}:{m.Left:0}:{m.Total:0}"));
        private static float Num(string s) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0;

        // ---- skills ----
        public float SkillLevel(Skills.SkillType skill) => S?.Skill(skill.ToString()) ?? 0;
        public void RaiseSkill(Skills.SkillType skill, float value)
        {
            if (S == null || skill == Skills.SkillType.None) return;
            string key = skill.ToString();
            float level = S.Skill(key);
            if (level >= 100) return;
            Skills.SkillDef def = Skill(skill);
            float step = (def?.m_increseStep ?? 1f) * value * Game.m_skillGainRate;
            float need = Mathf.Pow(Mathf.Floor(level + 1), 1.5f) * 0.5f + 0.5f;
            float progress = S.Skill(key + "#") + step;
            if (progress >= need) { level = Mathf.Min(100, Mathf.Floor(level) + 1); progress = 0; Body_.LevelUp(this, skill, level); }
            S.Skills[key] = level; S.Skills[key + "#"] = progress;
            _dirty = true;
        }
        private static Skills _skills;
        private static Skills.SkillDef Skill(Skills.SkillType t)
        {
            if (_skills == null) _skills = ZNetScene.instance?.GetPrefab("Player")?.GetComponent<Skills>();
            return _skills?.m_skills?.FirstOrDefault(d => d.m_skill == t);
        }
        // The Dimming: the day their worth last rose (any rise, from anything), and what that makes them now.
        private void Dim(double now)
        {
            int today = Clock.Day(now);
            if (!S.Counts.ContainsKey(Dimming.Key)) S.Counts[Dimming.Key] = today; // (from when it's first kept: nobody dims for the days before)
            if (_lastWorth >= 0 && S.Worth > _lastWorth + 0.001f) S.Counts[Dimming.Key] = today;
            _lastWorth = S.Worth;
            int stage = Dimming.Of(S, today);
            if (stage != DimStage)
            {
                if (DimStage >= 0 && Colony != null)
                {
                    if (stage > DimStage) DimNews.Add(Colony, stage, S.Name, S.FullName);
                    else if (stage == 0) DimNews.Add(Colony, 0, S.Name, S.FullName);
                }
                DimStage = stage; Villagers.Tint(this, stage);
            }
            if (stage >= 2 && !Jarldom.Mood.Has(S, "restless", now)) Jarldom.Mood.Add(S, "restless", now);
            else if (stage < 2) Jarldom.Mood.Remove(S, "restless");
        }
        public float Productivity()
        {
            float mood = Jarldom.Mood.Multiplier(Jarldom.Mood.Of(S, Now));
            if (Jarldom.Mood.Has(S, "inspired", Now)) mood *= 1.25f; // a day of their best work
            if (Time.time < SongUntil) mood *= WorkSongs.Pace; // (singing at the work with others)
            if (DimStage > 0) mood *= Dimming.Work(DimStage); // (fading: a tenth, then a fifth slower)
            if (Colony != null) mood *= 1 + Festivals.Of(Colony.R.Flags, Festival.Midsummer, Festivals.MidsummerWork); // (Midsummer's blessing)
            return mood * Plugin.Instance.WorkRate.Value;
        }

        // ---- other people ----
        public void Attacked(Character by)
        {
            LastAttacker = by.IsPlayer() ? (by as Player)?.GetPlayerName() : Localization.instance.Localize(by.m_name);
            if (!Combat.Friendly(by) && !(Task is CatchTask ct && ct.Target == by)) { Foe = by; _foeUntil = Time.time + 20; Stand(); } // (a companion's stray blow, a tame boar's kick: not a foe)
        }
        public bool HasFoe => Foe != null && !Foe.IsDead() && Time.time < _foeUntil;
        public void SetFoeFor(float seconds) => _foeUntil = Time.time + seconds;
        public void Thanked(Player p)
        {
            if (p == null) return;
            S.Opinions[p.GetPlayerName()] = Mathf.Clamp((S.Opinions.TryGetValue(p.GetPlayerName(), out int o) ? o : 0) + 3, -100, 100);
            _dirty = true;
            Greet.Say(this, Greeting.Thanks, p, 20f, false, true);
        }
        public void Say(string text, float ttl = 6f) => Talk.Say(this, text, ttl);

        // Changes asked by the colony (a player's choice in the window), on the game that owns this villager.
        public void Command(string action, string arg)
        {
            try { Commanded(action, arg); }
            finally { if (S != null && Alive) { _dirty = true; Save(); } } // (a player's word shows at once)
        }
        private void Commanded(string action, string arg)
        {
            switch (action)
            {
                case "prof":
                    if (Enum.TryParse(arg, out Prof p)) { S.Prof = p; Task?.Stop(this); Task = null; Say(p == Prof.Hauler ? new[] { "I'll carry what needs carrying.", "Carrying is what I'm for.", "Give me the heavy things.", "Load me up, I'll carry it.", "Hauling it is, then." }[UnityEngine.Random.Range(0, 5)] : new[] { $"A {Tables.Of(p).Name.ToLower()}? Gladly.", $"A {Tables.Of(p).Name.ToLower()}? Very well, I'll try.", $"A {Tables.Of(p).Name.ToLower()}? I can do that.", $"A {Tables.Of(p).Name.ToLower()}? Gladly, then.", $"A {Tables.Of(p).Name.ToLower()}? Show me the work." }[UnityEngine.Random.Range(0, 5)], 4); }
                    break;
                case "bed":
                    Bed old = Colony?.BedById(S.Bed);
                    if (old != null) Claim(old.GetComponent<ZNetView>(), false);
                    S.Bed = "";
                    Bed bed = Colony?.BedById(arg);
                    if (bed != null)
                    {
                        string owner = Colony.BedOwner(bed, out string kind);
                        if (kind == "player" || kind == "companion" || kind == "villager" && owner != S.Vid && Colony.R.Row(owner)?.State == SoulState.Active) { Say(new[] { "That bed is someone else's.", "That bed belongs to someone else.", "That bed is taken, and not mine to have.", "Someone else sleeps in that bed.", "That bed is already spoken for." }[UnityEngine.Random.Range(0, 5)], 4); break; }
                        Claim(bed.GetComponent<ZNetView>(), true);
                        S.Bed = arg;
                        Say(new[] { "A bed of my own!", "A bed at last, and it's mine!", "A bed of my own. Finally.", "Now I have a bed of my own.", "A bed that's mine, at last!" }[UnityEngine.Random.Range(0, 5)], 4);
                    }
                    break;
                case "chest": S.Chest = arg; break;
                case "ring":
                    Mood.Add(S, "ringgifted", Now); Worth.Gain(S, "ring");
                    S.Opinions[arg] = Mathf.Clamp((S.Opinions.TryGetValue(arg, out int o) ? o : 0) + 20, -100, 100);
                    S.Bump("rings"); Say(new[] { "I'll wear it with pride.", "A fine ring. I'll wear it always.", "This ring will be seen in the hall.", "Thank you. It's mine now, and I'm proud of it.", "A gold ring, for me! I'll wear it with pride." }[UnityEngine.Random.Range(0, 5)], 5);
                    break;
                case "follow": if (long.TryParse(arg.Split('|')[0], out long leader) && leader != 0) Follow.Start(this, leader, arg.Contains("|") ? arg.Substring(arg.IndexOf('|') + 1) : ""); else { Follow.End(this, new[] { "Home, then.", "Back to the hall, then.", "Home we go.", "Enough roaming. Home.", "Home, and gladly." }[UnityEngine.Random.Range(0, 5)]); Follow.HomeFrom(this); } break;
                case "expedition": // ("<leader id>|<role>|<leader name>")
                    {
                        string[] f = arg.Split('|');
                        if (f.Length >= 3 && long.TryParse(f[0], out long lead) && lead != 0 && Expedition.CantTake(this, f[1]) == "") Expedition.Join(this, lead, f[2], f[1]);
                    }
                    break;
                case "camp": // ("<x>|<y>|<z>|<yaw>": make camp there, as one of the crew out with the jarl)
                    {
                        string[] f = arg.Split('|'); var ic = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
                        if (f.Length < 4 || !Expedition.Out(this)) break;
                        if (Combat.Busy(this)) { Say(new[] { "Not now, jarl: we're fighting!", "Camp after this fight, jarl!", "Let's see these off first, then camp." }[UnityEngine.Random.Range(0, 3)], 4); break; }
                        if (!float.TryParse(f[0], fs, ic, out float cx) || !float.TryParse(f[1], fs, ic, out float cy) || !float.TryParse(f[2], fs, ic, out float cz) || !float.TryParse(f[3], fs, ic, out float yaw)) break;
                        Task?.Stop(this); Task = new CampTask(new Vector3(cx, cy, cz), yaw); Task.Start(this);
                    }
                    break;
                case "outpost": // ("<x>|<y>|<z>|<yaw>": raise high-seat pillars there, as one of the crew out with the jarl)
                    {
                        string[] f = arg.Split('|'); var ic = System.Globalization.CultureInfo.InvariantCulture; var fs = System.Globalization.NumberStyles.Float;
                        if (f.Length < 4 || !Expedition.Out(this)) break;
                        if (float.TryParse(f[0], fs, ic, out float ox) && float.TryParse(f[1], fs, ic, out float oy) && float.TryParse(f[2], fs, ic, out float oz) && float.TryParse(f[3], fs, ic, out float oyaw))
                            Expedition.Raise(this, new Vector3(ox, oy, oz), oyaw);
                    }
                    break;
                case "summon": Follow.End(this, new[] { "Coming.", "On my way.", "Coming, jarl.", "Right behind you.", "Coming now." }[UnityEngine.Random.Range(0, 5)]); if (Colony == null || !Colony.Valid) { Follow.HomeFrom(this); break; } Task?.Stop(this); Task = new SummonTask(Colony.Position + Colony.Seat.transform.forward * 2.5f); Task.Start(this); break;
                case "dismiss": Dismiss(); return;
                case "mood": Jarldom.Mood.Add(S, arg, Now); break;
                case "opinion": { int bar = arg.LastIndexOf('|'); if (bar > 0 && int.TryParse(arg.Substring(bar + 1), out int by)) { string who = arg.Substring(0, bar); S.Opinions[who] = Mathf.Clamp((S.Opinions.TryGetValue(who, out int was) ? was : 0) + by, -100, 100); } } break;
                case "gain": Worth.Gain(S, arg, Faith.OdinWorthBonus(Colony)); S.Bump(arg + "s"); break; // (a deed the colony did together: worth, which also ends the Dimming)
                case "ungrieve": if (S.Bonds.TryGetValue(arg, out int bond) && bond > 40) { Jarldom.Mood.Remove(S, "grief"); Jarldom.Mood.Add(S, "rested", Now); } break; // (a friend lost to Hel, laid to rest)
                case "train": { string key = arg; float now = S.Skill(key); S.Skills[key] = Mathf.Min(100, now + 10); Say(new[] { $"I learned a great deal of {key.ToLower()} from the master.", $"The master taught me a fair bit of {key.ToLower()}.", $"A good lesson in {key.ToLower()}, that was.", $"The master made me better at {key.ToLower()}.", $"I'm more skilled at {key.ToLower()} for the lesson." }[UnityEngine.Random.Range(0, 5)], 5); } break;
                case "worth": if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w)) { S.Worth = Mathf.Clamp(w, 0, 100); S.Worthy = S.Worth >= 100; } break;
                case "need": { string[] f = arg.Split('='); if (f.Length == 2 && Enum.TryParse(f[0], out Need n) && float.TryParse(f[1], out float v)) S.Needs[(int)n] = Mathf.Clamp(v, 0, 100); } break;
                case "ascend": Task?.Stop(this); Task = new AscendTask(arg); Task.Start(this); break;
                case "answer": Asks.Answered(this, arg); break;
                case "farewell":
                    {
                        // (down to the shore to see one of ours off to Valhalla by ship: anyone awake and not in a fight)
                        string[] f = arg.Split('|');
                        if (f.Length < 3 || Combat.Busy(this) || Task is SleepTask || Task is AscendTask || Follow.LeaderId(this) != 0) break;
                        if (!float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fx) || !float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fz)) break;
                        Task?.Stop(this); Task = new FarewellTask(new Vector3(fx, ZoneSystem.instance.GetGroundHeight(new Vector3(fx, 0, fz)), fz), f[2]); Task.Start(this);
                    }
                    break;
                case "fest":
                    {
                        // (to the festival fire: anyone awake, not fighting, not out with the jarl)
                        string[] f = arg.Split('|');
                        if (f.Length < 4 || Combat.Busy(this) || Task is SleepTask || Task is AscendTask || Follow.LeaderId(this) != 0) break;
                        var inv = System.Globalization.CultureInfo.InvariantCulture;
                        if (!float.TryParse(f[0], System.Globalization.NumberStyles.Float, inv, out float x) || !float.TryParse(f[1], System.Globalization.NumberStyles.Float, inv, out float z)) break;
                        Task?.Stop(this); Task = new FestTask(new Vector3(x, ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0, z)), z), int.Parse(f[2]), int.Parse(f[3])); Task.Start(this);
                    }
                    break;
                case "yule":
                    {
                        // (round the Yule log for the cups: start|hearth x y z|host or guest|index|count)
                        string[] f = arg.Split('|');
                        if (f.Length < 7 || Combat.Busy(this) || Task is SleepTask || Task is AscendTask || Follow.LeaderId(this) != 0) break;
                        var inv = System.Globalization.CultureInfo.InvariantCulture; float P(int i) => float.Parse(f[i], inv);
                        Task?.Stop(this); Task = new YuleTask(long.Parse(f[0], inv), new Vector3(P(1), P(2), P(3)), f[4] == "host", int.Parse(f[5], inv), int.Parse(f[6], inv)); Task.Start(this);
                    }
                    break;
                case "winter":
                    {
                        // (a part in Winter Nights: start|at x y z|forward x z|role|index|count|names|extra)
                        string[] f = arg.Split('|');
                        if (f.Length < 11 || Combat.Busy(this) || Task is SleepTask || Task is AscendTask || Follow.LeaderId(this) != 0) break;
                        var inv = System.Globalization.CultureInfo.InvariantCulture; float P(int i) => float.Parse(f[i], inv);
                        Task?.Stop(this); Task = new WinterTask(long.Parse(f[0], inv), new Vector3(P(1), P(2), P(3)), new Vector3(P(4), 0, P(5)), f[6], int.Parse(f[7], inv), int.Parse(f[8], inv), int.Parse(f[9], inv), f[10]); Task.Start(this);
                    }
                    break;
                case "sigr":
                    {
                        // (a part in Sigrblot: start|altar x y z|forward x z|role|index|count|fighters|extra)
                        string[] f = arg.Split('|');
                        if (f.Length < 11 || Combat.Busy(this) || Task is SleepTask || Task is AscendTask || Follow.LeaderId(this) != 0) break;
                        var inv = System.Globalization.CultureInfo.InvariantCulture; float P(int i) => float.Parse(f[i], inv);
                        Task?.Stop(this); Task = new SigrTask(long.Parse(f[0], inv), new Vector3(P(1), P(2), P(3)), new Vector3(P(4), 0, P(5)), f[6], int.Parse(f[7], inv), int.Parse(f[8], inv), int.Parse(f[9], inv), f[10]); Task.Start(this);
                    }
                    break;
                case "say": if (!Combat.Busy(this) && !(Task is SleepTask)) Say(arg, 8f); break; // (a legend told, or any line the colony's owner wants said)
                case "beacon":
                    {
                        // (a guard to light the beacon for a jarl's ship coming in: up out of bed if need be, unless fighting)
                        string[] f = arg.Split('|');
                        if (f.Length < 3 || Combat.Busy(this) || Task is AscendTask || Task is BeaconTask) break;
                        if (!float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) || !float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z)) break;
                        ZoneSystem.instance.GetSolidHeight(new Vector3(x, 0, z), out float y);
                        Task?.Stop(this); Task = new BeaconTask(new Vector3(x, y, z), f[2]); Task.Start(this);
                    }
                    break;
                case "meet":
                    {
                        // (to the dock to meet a player coming home by sea: only if nothing pressing, awake, and not in a fight)
                        string[] f = arg.Split('|');
                        if (f.Length < 3 || Combat.Busy(this) || Task is SleepTask || Task is AscendTask || Task is SummonTask || Task != null && !Task.Interruptible) break;
                        if (!float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) || !float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z)) break;
                        Task?.Stop(this); Task = new MeetTask(new Vector3(x, ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0, z)), z), f[2]); Task.Start(this);
                    }
                    break;
            }
            Dirty(); Save();
        }
        // Gone for a while (a trade run): the soul and everything they carry wait in the colony's record, and come back at the pillars.
        public void Away(string why)
        {
            Release();
            Inventory bag = Body.GetInventory();
            foreach (ItemDrop.ItemData item in bag.GetAllItems()) { if (item.m_equipped) Body.UnequipItem(item, false); item.m_equipped = false; }
            var pkg = new ZPackage(); bag.Save(pkg);
            if (Colony != null) Colony.SoulAway(S, pkg.GetArray(), Pos, why);
            View.Destroy();
        }
        // A bed's claim, written so the game's own beds, Quad's companions and other colonies all respect it.
        private void Claim(ZNetView bed, bool take)
        {
            if (bed == null || !bed.IsValid()) return;
            if (!bed.IsOwner()) bed.ClaimOwnership();
            ZDO z = bed.GetZDO();
            if (take) { z.Set(Villagers.ClaimKey, S.Vid); z.Set("dhc_home", Colony.Hash(S.Vid)); z.Set("dhc_homename", S.FullName); }
            else if (z.GetString(Villagers.ClaimKey, "") == S.Vid) { z.Set(Villagers.ClaimKey, ""); z.Set("dhc_home", 0L); z.Set("dhc_homename", ""); }
        }
        // Sent away by the jarl: their things in a chest by the pillars.
        private void Dismiss()
        {
            Release();
            Bed bed = Colony?.BedById(S.Bed);
            if (bed != null) Claim(bed.GetComponent<ZNetView>(), false);
            Inventory bag = Body.GetInventory();
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            if (bag != null && bag.NrOfItems() > 0 && chestPrefab != null)
            {
                foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) { Body.UnequipItem(item, false); item.m_equipped = false; }
                Vector3 at = Colony != null ? Colony.Spot(Colony.Position + Colony.Seat.transform.right * 3, 2f) ?? Pos : Pos;
                Inventory inv = UnityEngine.Object.Instantiate(chestPrefab, at, Quaternion.identity).GetComponent<Container>().GetInventory();
                foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) if (inv.AddItem(item)) bag.RemoveItem(item);
                foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) { ItemDrop.DropItem(item, item.m_stack, at + Vector3.up, Quaternion.identity); bag.RemoveItem(item); }
            }
            if (Colony != null) Net.Act(Colony, "gone", S.Vid + "|dismissed");
            Assets.Effect("vfx_spawn", Pos);
            View.Destroy();
        }
    }

    // Reflection into the body: the private animator, block flag and a level-up note.
    internal static class Body_
    {
        internal static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        internal static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        internal static readonly AccessTools.FieldRef<Humanoid, float> DrawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");
        public static void LevelUp(Brain b, Skills.SkillType skill, float level)
        {
            if (level % 5 == 0) b.Say(new[] { $"I'm getting better at {skill.ToString().ToLower()}!", $"My {skill.ToString().ToLower()} is coming along!", $"Getting the hang of {skill.ToString().ToLower()}!", $"I'm getting better at {skill.ToString().ToLower()}, at last!", $"My {skill.ToString().ToLower()} is improving!" }[UnityEngine.Random.Range(0, 5)], 4);
        }
    }

    // News of the Dimming, gathered for a few seconds so a festival that lifts it from all of them is one line in the saga, not fourteen.
    internal static class DimNews
    {
        // (written 4 s after the last one heard, or 20 s after the first: each villager's brain hears the festival in its own time)
        private sealed class Batch { public float First, Last; public List<(string name, string full)> Who = new List<(string, string)>(); }
        private static readonly Dictionary<(string colony, int stage), Batch> Waiting = new Dictionary<(string, int), Batch>();
        public static void Add(Colony c, int stage, string name, string full)
        {
            var key = (c.Id, stage);
            if (!Waiting.TryGetValue(key, out Batch w)) Waiting[key] = w = new Batch { First = Time.time };
            w.Last = Time.time;
            w.Who.Add((name, full));
        }
        public static void Tick()
        {
            foreach (var key in Waiting.Keys.Where(k => Time.time - Waiting[k].Last > 4f || Time.time - Waiting[k].First > 20f).ToList())
            {
                var who = Waiting[key].Who; Waiting.Remove(key);
                Colony c = Colony.Of(key.colony);
                if (c == null || who.Count == 0) continue;
                string one = who[0].full, names = who.Count == 1 ? one : who.Count <= 3 ? string.Join(", ", who.Take(who.Count - 1).Select(x => x.full)) + " and " + who.Last().full : $"{who[0].full}, {who[1].full} and {who.Count - 2} others";
                bool many = who.Count > 1;
                string text = key.stage == 0 ? $"{names} {(many ? "are" : "is")} dimming no longer."
                    : key.stage == 1 ? $"{names} {(many ? "have" : "has")} done nothing worthy in {Dimming.First} days, and {(many ? "begin" : "begins")} to dim."
                    : $"{names} {(many ? "are" : "is")} fading: {Dimming.Second} days with nothing worthy. They grow restless.";
                c.Log("text", many ? "" : who[0].name, "", text);
            }
        }
        public static void Forget() => Waiting.Clear();
    }
}
