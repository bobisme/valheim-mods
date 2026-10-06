using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Farmhand
{
    public sealed partial class Plugin
    {
        private static bool IsCrop(Piece piece)
        {
            Plant plant = piece != null ? piece.GetComponent<Plant>() : null;
            return plant != null && plant.m_attachDistance <= 0 && plant.m_grownPrefabs.Length > 0 &&
                plant.m_grownPrefabs.All(g => g != null && g.GetComponent<Pickable>() != null &&
                    g.GetComponent<Pickable>().m_respawnTimeMinutes <= 0 && g.GetComponent<Pickable>().m_hideWhenPicked == null);
        }

        private IEnumerable<Vector3> RowPositions(Player p, Piece piece)
        {
            GameObject ghost = GameAccess.Ghost(p);
            if (ghost == null || !ghost.activeInHierarchy) yield break;
            float spacing = Layout.Spacing(_spacing.Value, piece.GetComponent<Plant>().m_growRadius);
            Vector3 across = p.transform.right;
            across.y = 0;
            across.Normalize();
            Vector3 center = ghost.transform.position;
            foreach (float offset in Layout.Offsets(_rowCount.Value, spacing)) yield return center + across * offset;
        }

        private IEnumerator PlantRow(Player p)
        {
            Piece piece = p.GetSelectedPiece();
            if (!IsCrop(piece)) { Say(p, "Select a crop in the cultivator menu first."); yield break; }
            Vector3[] positions = RowPositions(p, piece).ToArray();
            if (positions.Length == 0) { Say(p, "Aim at the ground first."); yield break; }
            int planted = 0, skipped = 0;
            foreach (Vector3 pos in positions)
            {
                if (!StillWorking(p)) break;
                if (GameAccess.TryPlant(p, piece, pos, out string reason)) planted++;
                else
                {
                    skipped++;
                    if (reason == "Missing seeds/materials" || reason == "Not enough stamina" || reason == "Cultivator is broken")
                    { Say(p, $"Planted {planted}; stopped: {reason}."); yield break; }
                }
                yield return new WaitForSeconds(0.15f);
            }
            Say(p, $"Planted {planted}; skipped {skipped} blocked or out-of-reach spots.");
        }

        private IEnumerator Harvest(Player p, bool replant)
        {
            var byGrownName = new Dictionary<string, Piece>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameObject prefab in p.GetBuildTool().m_pieces)
            {
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (!IsCrop(piece) || !p.IsRecipeKnown(piece.m_name) || !p.IsPieceAvailable(piece)) continue;
                foreach (GameObject grown in piece.GetComponent<Plant>().m_grownPrefabs)
                {
                    if (byGrownName.TryGetValue(grown.name, out Piece existing) && existing != piece) ambiguous.Add(grown.name);
                    else byGrownName[grown.name] = piece;
                }
            }
            foreach (string name in ambiguous) byGrownName.Remove(name);
            float radius = Mathf.Clamp(_harvestRadius.Value, 1f, 5f);
            var batch = UnityEngine.Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)
                .Where(c => c != null && !Player.IsPlacementGhost(c.gameObject) && c.CanBePicked() &&
                    byGrownName.ContainsKey(PrefabName(c.name)) &&
                    (c.transform.position - p.transform.position).sqrMagnitude <= radius * radius)
                .OrderBy(c => (c.transform.position - p.transform.position).sqrMagnitude)
                .Take(Mathf.Clamp(_batchLimit.Value, 1, 100)).ToArray();
            int harvested = 0, replanted = 0, skipped = 0;
            foreach (Pickable crop in batch)
            {
                if (!StillWorking(p)) break;
                if (crop == null || crop.GetPicked() || !crop.CanBePicked()) { skipped++; continue; }
                Vector3 pos = crop.transform.position;
                if ((pos - p.transform.position).sqrMagnitude > radius * radius || !PrivateArea.CheckAccess(pos, 0f, false))
                { skipped++; continue; }
                Piece piece = byGrownName[PrefabName(crop.name)];
                ZNetView view = crop.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) { skipped++; continue; }
                ZDOID id = view.GetZDO().m_uid;
                // Interact's bool means "play animation", not "harvest succeeded". Wait for the owner response.
                crop.Interact(p, false, false);
                float started = Time.unscaledTime;
                HarvestOutcome outcome;
                do
                {
                    yield return null;
                    if (!StillWorking(p)) yield break;
                    bool destroyedByOwner = crop == null && ZDOMan.instance.GetZDO(id) == null;
                    outcome = HarvestConfirmation.Observe(destroyedByOwner, crop != null && crop.GetPicked(), Time.unscaledTime - started);
                } while (outcome == HarvestOutcome.Waiting);
                if (outcome != HarvestOutcome.Confirmed) { skipped++; continue; }
                harvested++;
                // Drops remain normal world items. Seeds must already be available; no free seeds or inventory insertion.
                if (!StillWorking(p)) yield break;
                if (replant && GameAccess.TryPlant(p, piece, pos, out _)) replanted++;
                yield return new WaitForSeconds(0.15f);
            }
            Say(p, replant ? $"Harvested {harvested}; replanted {replanted}; {harvested - replanted} need seeds or a valid spot; skipped {skipped}." :
                $"Harvested {harvested}; skipped {skipped}.");
        }

        private static string PrefabName(string name) => name.EndsWith("(Clone)", StringComparison.Ordinal) ? name.Substring(0, name.Length - 7) : name;
    }
}
