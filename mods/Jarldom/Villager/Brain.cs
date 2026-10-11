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
        public Task Task; public Activity Activity = Activity.Idle; public string Doing = "";
        public string LastAttacker = ""; public Character Foe; private float _foeUntil;
        public bool KeepDoorsOpen; public float Stamina = 1f;
        public bool Fighting, Combat_Bow, Fled; public int Swings, SwingsStarted; public string CombatNote = ""; public float BlockUntil, NoBlockUntil; public Character FightTarget; public float FleeUntil;
        private bool _dirty; private float _nextSave, _nextNeeds, _nextGear, _nextFoes, _nextMoodSync, _nextWork, _born;
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
        public float NoStoreUntil, NoFoodUntil, NoRepairUntil, NoBuildUntil, NoTradeUntil, NoCraftUntil, NoHealUntil, NoScoutUntil, NoOfferUntil, NextMischief, NoCartUntil, NoGearUntil, NoGraveUntil, LeaderGoneAt, NoUpkeepUntil, NoCatchUntil; public readonly HashSet<int> LampsTried = new HashSet<int>(); private float _nextStep; private bool _captureRolled; private int _bedMissing; private float _nextDecide; public float LastDecide, DecideDt = 0.25f; public double GameDt; private double _lastWorld;
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
            LoadMeals(z.GetString(MealKey, ""));
            _lastNeeds = Now;
            Body.SetMaxHealth(MaxHealth());
            return true;
        }
        public void Dirty() => _dirty = true;
        public void Save()
        {
            if (!View.IsValid() || !View.IsOwner()) return;
            _dirty = false;
            ZDO z = View.GetZDO();
            SoulStore.Write(z, S, Colony);
            if (z.GetString(Villagers.NameKey, "") != S.FullName) z.Set(Villagers.NameKey, S.FullName);
            string meals = SaveMeals();
            if (z.GetString(MealKey, "") != meals) z.Set(MealKey, meals);
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
            Held = null;
            if (_body != null) { _body.isKinematic = false; _body.linearVelocity = Vector3.zero; }
            if (Steer.SafeSpot(off)) Place(off);
            Seats.Free(this);
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
            if (Colony == null || !Colony.Valid) { Colony = Colony.Of(S.Colony); if (Colony == null) { if (Follow.LeaderId(this) != 0) Away(); else Idle("waiting for the hall to load"); return; } }
            Follow.Told(this); // (a homecoming told while the hall was far away)
            double now = Now;
            if (Time.time >= _nextNeeds) { _nextNeeds = Time.time + 5f; Needs(now); }
            if (Time.time >= _nextGear) { _nextGear = Time.time + 1f; Gear.Maintain(this); }
            if (Time.time >= _nextFoes) { _nextFoes = Time.time + 0.3f; Combat.Think(this); }
            if (Time.time >= _nextMoodSync) { _nextMoodSync = Time.time + 2f; SyncHover(); }
            if (Time.time >= _nextStep) { _nextStep = Time.time + 2f; Upkeep.Step(this); } // (where feet wear a path)
            if (_dirty && Time.time >= _nextSave) { _nextSave = Time.time + 30f; Save(); } // (the soul is sent to everyone when written: twice a minute is plenty; deaths and commands save at once)
            if (Combat.Busy(this)) return;
            Activity want = Want();
            // Sleep, a trade run, the road to Valhalla: carried on to the end unless a raid comes.
            if (Task != null && !Task.Done && !Task.Interruptible && want != Activity.Defend) { Task.Tick(this); if (Task != null && Alive) SetDoing(Task.Doing); return; }
            if (want != Activity || Task == null || Task.Done)
            {
                if (Task != null && (Task.Done || want != Activity)) { Task.Stop(this); Task = null; }
                Activity = want;
                Task = Choose(want);
                Task?.Start(this);
            }
            Task?.Tick(this);
            if (Task != null) SetDoing(Task.Doing);
            if (Activity == Activity.Work && Task != null && !(Task is WanderTask)) CatchUp.Work(this, (float)GameDt); // (working hours, for the rates kept while away)
        }

        private Activity Want()
        {
            if (Follow.Check(this)) return Activity.Follow; // (out with the jarl: a raid at home is the guards' to meet)
            if (Colony.RaidOn || Colony.HornUntil > Time.time) return Activity.Defend;
            if (Thing.Held(Colony) && Thing.Comes(this)) return Activity.Thing; // (everyone to the Thing)
            if (Body.GetHealthPercentage() < 0.1f || S.InjuredUntil > Now) return S.Needs[(int)Need.Food] < Mood.EatBelow && Time.time > NoFoodUntil && Food.CanEat(this) ? Activity.Wake : Activity.Home;
            Activity a = Schedule.At(Colony.Hour, S.Traits, heavyRain: Storm(), outdoorTask: true, urgent: false);
            if (a == Activity.Work || a == Activity.Wake) { if (S.Needs[(int)Need.Food] < Mood.EatBelow && Time.time > NoFoodUntil && Food.CanEat(this)) return Activity.Wake; }
            return a;
        }
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
            switch (a)
            {
                case Activity.Sleep: return new SleepTask();
                case Activity.Home: return new SleepTask { JustRest = Colony.Hour < 21 };
                case Activity.Wake: return Food.CanEat(this) ? new EatTask() : (Task)new WanderTask(Colony.Position, 8, "getting up");
                case Activity.Work: { Task work = Work.Next(this); if (work != null) { Leisure.Clean(this); return work; } return Leisure.Next(this, Gear.Problem ?? Work.WhyIdle(this)); } // (nothing to do: some sport, or the hall)
                case Activity.Hall: return Work.Lamplight(this) ?? new HallTask(); // (the hauler sees to the fires and torches first)
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
            View.GetZDO().Set(Villagers.DoingKey, doing);
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
            // Shelter: dry and warm (under a roof, or by a fire).
            Cover.GetCoverForPoint(Body.GetCenterPoint(), out float cover, out bool roof);
            bool sheltered = cover >= 0.8f && roof, warm = EffectArea.IsPointInsideArea(Pos, EffectArea.Type.Heat) != null;
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
            if (S.Has("pious") && shrines == 0) { if (!Jarldom.Mood.Has(S, "neglect", now)) Jarldom.Mood.Add(S, "neglect", now); } else Jarldom.Mood.Remove(S, "neglect");
            if (S.Needs[(int)Need.Food] < 15) { if (!Jarldom.Mood.Has(S, "hungry", now)) Jarldom.Mood.Add(S, "hungry", now); } else Jarldom.Mood.Remove(S, "hungry");
            if (S.Foods.Count >= 3 && S.Foods.Skip(S.Foods.Count - 3).Distinct().Count() == 1) { if (!Jarldom.Mood.Has(S, "samefood", now)) Jarldom.Mood.Add(S, "samefood", now); } else Jarldom.Mood.Remove(S, "samefood");
            if (!Weaving.InRags(this)) { if (!Jarldom.Mood.Has(S, "dressed", now)) Jarldom.Mood.Add(S, "dressed", now); } else Jarldom.Mood.Remove(S, "dressed");
            if (S.Worthy && !Jarldom.Mood.Has(S, "valkyries", now)) Jarldom.Mood.Add(S, "valkyries", now);
            if (!Jarldom.Mood.Has(S, "gary", now) && Held == null && Compat.Gary(Pos, 5f) is Character gary)
            {
                Jarldom.Mood.Add(S, "gary", now);
                Ai.LookTowards((gary.transform.position - Pos).normalized);
                Say(new[] { "Who's a good greydwarf?", "Hello, Gary.", "Gary! Have you been eating the resin again?" }[UnityEngine.Random.Range(0, 3)], 4);
            }
            if (Body.GetHealthPercentage() > 0.5f) _captureRolled = false;
            if (Body.GetHealthPercentage() < 0.1f && S.InjuredUntil < now && !_captureRolled) { _captureRolled = true; if (Captives.Take(this)) return; } // (dragged off by the Fulings: one roll a fall)
            if (Body.GetHealthPercentage() < 0.1f && S.InjuredUntil < now && !Combat.Busy(this)) { S.InjuredUntil = now + Clock.DayLength / (Colony.Best(RoomType.HealersHouse)?.Ok == true ? 2 : 1); Colony.Log("injured", S.Name, ""); Jarldom.Mood.Add(S, "injured", now); }
            if (S.InjuredUntil > 0 && S.InjuredUntil < now) { S.InjuredUntil = 0; Jarldom.Mood.Remove(S, "injured"); }
            // A bed that is gone (broken, taken down) is no longer theirs, once its area is loaded and it still can't be found.
            if (!string.IsNullOrEmpty(S.Bed) && Vector3.Distance(Pos, Colony.Position) < Colony.Radius && Colony.BedById(S.Bed) == null) { if (++_bedMissing >= 3) { S.Bed = ""; _bedMissing = 0; Colony.ReleaseBed(S.Vid); Say("I need a bed of my own.", 4); } }
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
            if (!(Task is FollowTask) || Task.Done) { Task?.Stop(this); Activity = Activity.Follow; Task = new FollowTask(); Task.Start(this); }
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
        public float Productivity()
        {
            float mood = Jarldom.Mood.Multiplier(Jarldom.Mood.Of(S, Now));
            if (Jarldom.Mood.Has(S, "inspired", Now)) mood *= 1.25f; // a day of their best work
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
                    if (Enum.TryParse(arg, out Prof p)) { S.Prof = p; Task?.Stop(this); Task = null; Say(p == Prof.Hauler ? "I'll carry what needs carrying." : $"A {Tables.Of(p).Name.ToLower()}? Gladly.", 4); }
                    break;
                case "bed":
                    Bed old = Colony?.BedById(S.Bed);
                    if (old != null) Claim(old.GetComponent<ZNetView>(), false);
                    S.Bed = "";
                    Bed bed = Colony?.BedById(arg);
                    if (bed != null)
                    {
                        string owner = Colony.BedOwner(bed, out string kind);
                        if (kind == "player" || kind == "companion" || kind == "villager" && owner != S.Vid && Colony.R.Row(owner)?.State == SoulState.Active) { Say("That bed is someone else's.", 4); break; }
                        Claim(bed.GetComponent<ZNetView>(), true);
                        S.Bed = arg;
                        Say("A bed of my own!", 4);
                    }
                    break;
                case "chest": S.Chest = arg; break;
                case "ring":
                    Mood.Add(S, "ringgifted", Now); Worth.Gain(S, "ring");
                    S.Opinions[arg] = Mathf.Clamp((S.Opinions.TryGetValue(arg, out int o) ? o : 0) + 20, -100, 100);
                    S.Bump("rings"); Say("I'll wear it with pride.", 5);
                    break;
                case "follow": if (long.TryParse(arg.Split('|')[0], out long leader) && leader != 0) Follow.Start(this, leader, arg.Contains("|") ? arg.Substring(arg.IndexOf('|') + 1) : ""); else { Follow.End(this, "Home, then."); Follow.HomeFrom(this); } break;
                case "summon": Follow.End(this, "Coming."); if (Colony == null || !Colony.Valid) { Follow.HomeFrom(this); break; } Task?.Stop(this); Task = new GoTask(Colony.Position + Colony.Seat.transform.forward * 2.5f, "coming to the pillars", 2f, true); Task.Start(this); break;
                case "dismiss": Dismiss(); return;
                case "mood": Jarldom.Mood.Add(S, arg, Now); break;
                case "train": { string key = arg; float now = S.Skill(key); S.Skills[key] = Mathf.Min(100, now + 10); Say($"I learned a great deal of {key.ToLower()} from the master.", 5); } break;
                case "worth": if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w)) { S.Worth = Mathf.Clamp(w, 0, 100); S.Worthy = S.Worth >= 100; } break;
                case "need": { string[] f = arg.Split('='); if (f.Length == 2 && Enum.TryParse(f[0], out Need n) && float.TryParse(f[1], out float v)) S.Needs[(int)n] = Mathf.Clamp(v, 0, 100); } break;
                case "ascend": Task?.Stop(this); Task = new AscendTask(arg); Task.Start(this); break;
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
            if (level % 5 == 0) b.Say($"I'm getting better at {skill.ToString().ToLower()}!", 4);
        }
    }
}
