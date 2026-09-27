using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Landscape.FoliagePipeline
{
    public static class FoliageResidency
    {
        internal const int DefaultTerrainBudget = 2;
        internal const int DefaultDetailPageBudget = 8;
        internal const int DefaultPageVisibilityHoldFrames = 12;

        internal class PageLease
        {
            internal ulong species;
        }

        private class TerrainEntry
        {
            internal Terrain terrain;
            internal GrassComponent grass;
            internal TreeComponent tree;
            internal bool resident;
            internal float distance;
        }

        private class CameraView
        {
            internal Camera camera;
            internal float3 origin;
            internal Matrix4x4 viewMatrix;
            internal Matrix4x4 projectionMatrix;
            internal int frame;
            internal Plane[] planes = new Plane[6];
        }

        private struct PageCandidate
        {
            internal GrassComponent component;
            internal int species;
            internal ulong speciesKey;
            internal int pageX;
            internal int pageY;
            internal bool visible;
            internal bool resident;
            internal bool selected;
            internal float distance;
            internal int lastUse;
        }

        private static readonly Dictionary<ulong, TerrainEntry> s_Entries = new Dictionary<ulong, TerrainEntry>();
        private static readonly Dictionary<ulong, CameraView> s_Views = new Dictionary<ulong, CameraView>();
        private static readonly Dictionary<ulong, int> s_PageSlots = new Dictionary<ulong, int>();
        private static readonly HashSet<PageLease> s_PageLeases = new HashSet<PageLease>();
        private static readonly List<TerrainEntry> s_TerrainSort = new List<TerrainEntry>();
        private static readonly List<PageCandidate> s_PageSort = new List<PageCandidate>();
        private static readonly List<ulong> s_StaleViews = new List<ulong>();
        private static int s_Frame;
        private static int s_ResidentTerrains;
        private static int s_ResidentDetailPages;
        private static int s_SelectedDetailPages;
        private static long s_PageHits;
        private static long s_PageEvictions;
        private static int s_TerrainBudget = DefaultTerrainBudget;
        private static int s_DetailPageBudget = DefaultDetailPageBudget;
        private static int s_PageVisibilityHoldFrames = DefaultPageVisibilityHoldFrames;
        private static float s_GrassDistanceScale = 1f;

        public static int ResidentTerrains { get { return s_ResidentTerrains; } }
        public static int ResidentDetailPages { get { return s_ResidentDetailPages; } }
        public static int SelectedDetailPages { get { return s_SelectedDetailPages; } }
        public static long PageHits { get { return s_PageHits; } }
        public static long PageEvictions { get { return s_PageEvictions; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Entries.Clear();
            s_Views.Clear();
            s_PageSlots.Clear();
            s_PageLeases.Clear();
            s_TerrainSort.Clear();
            s_PageSort.Clear();
            s_StaleViews.Clear();
            s_ResidentTerrains = 0;
            s_ResidentDetailPages = 0;
            s_SelectedDetailPages = 0;
            s_PageHits = 0;
            s_PageEvictions = 0;
            s_TerrainBudget = DefaultTerrainBudget;
            s_DetailPageBudget = DefaultDetailPageBudget;
            s_PageVisibilityHoldFrames = DefaultPageVisibilityHoldFrames;
            s_GrassDistanceScale = 1f;
        }

        internal static bool TryAcquireDetailPageSlot(in ulong species, out PageLease lease)
        {
            s_PageSlots.TryGetValue(species, out int count);
            lease = null;
            if (count >= s_DetailPageBudget) { return false; }
            lease = new PageLease();
            lease.species = species;
            s_PageLeases.Add(lease);
            s_PageSlots[species] = count + 1;
            ++s_ResidentDetailPages;
            return true;
        }

        internal static void ReleaseDetailPageSlot(PageLease lease)
        {
            if (lease == null || !s_PageLeases.Remove(lease)) { return; }
            ulong species = lease.species;
            int count = s_PageSlots[species];
            if (count == 1) { s_PageSlots.Remove(species); }
            else { s_PageSlots[species] = count - 1; }
            --s_ResidentDetailPages;
        }

        internal static void Register(Terrain terrain, GrassComponent grass)
        {
            TerrainEntry entry = GetEntry(terrain);
            entry.grass = grass;
        }

        internal static void Register(Terrain terrain, TreeComponent tree)
        {
            TerrainEntry entry = GetEntry(terrain);
            entry.tree = tree;
        }

        internal static void Unregister(Terrain terrain, GrassComponent grass)
        {
            if (terrain == null || !s_Entries.TryGetValue(EntityId.ToULong(terrain.GetEntityId()), out TerrainEntry entry)) { return; }
            if (entry.grass == grass)
            {
                s_PageEvictions += grass.ResidentDetailPageCount();
                entry.grass = null;
                grass.SetResident(false);
            }
            if (entry.grass == null && entry.tree == null) { s_Entries.Remove(EntityId.ToULong(terrain.GetEntityId())); }
        }

        internal static void Unregister(Terrain terrain, TreeComponent tree)
        {
            if (terrain == null || !s_Entries.TryGetValue(EntityId.ToULong(terrain.GetEntityId()), out TerrainEntry entry)) { return; }
            if (entry.tree == tree) { entry.tree = null; tree.SetResident(false); }
            if (entry.grass == null && entry.tree == null) { s_Entries.Remove(EntityId.ToULong(terrain.GetEntityId())); }
        }

        private static TerrainEntry GetEntry(Terrain terrain)
        {
            ulong key = EntityId.ToULong(terrain.GetEntityId());
            if (s_Entries.TryGetValue(key, out TerrainEntry entry)) { return entry; }
            entry = new TerrainEntry();
            entry.terrain = terrain;
            s_Entries.Add(key, entry);
            return entry;
        }

        private static float DistanceSquared(float3 origin, Vector3 corner, Vector3 size)
        {
            float dx = math.max(math.max(corner.x - origin.x, origin.x - corner.x - size.x), 0f);
            float dz = math.max(math.max(corner.z - origin.z, origin.z - corner.z - size.z), 0f);
            return dx * dx + dz * dz;
        }

        internal static bool IsResidentCameraType(in CameraType cameraType)
        {
            return cameraType == CameraType.Game || cameraType == CameraType.VR;
        }

        internal static bool PageDemand(BoundSection[] bounds, GrassSection[] sections, in int numSection, in int pageX, in int pageY,
            in float3 origin, Plane[] planes, in float drawDistance, out bool visible, out float distance)
        {
            visible = false;
            distance = float.MaxValue;
            if (bounds == null || sections == null || bounds.Length != numSection * numSection || sections.Length != bounds.Length || drawDistance <= 0f) { return false; }
            float maxDistance = drawDistance + (drawDistance * 0.5f);
            float maxDistanceSq = maxDistance * maxDistance;
            int xStart = pageX * numSection / FoliageAssetCodec.PageAxis;
            int xEnd = ((pageX + 1) * numSection + FoliageAssetCodec.PageAxis - 1) / FoliageAssetCodec.PageAxis;
            int yStart = pageY * numSection / FoliageAssetCodec.PageAxis;
            int yEnd = ((pageY + 1) * numSection + FoliageAssetCodec.PageAxis - 1) / FoliageAssetCodec.PageAxis;
            bool eligible = false;
            for (int x = xStart; x < xEnd; ++x)
            {
                for (int y = yStart; y < yEnd; ++y)
                {
                    int index = FoliageLogic.CellIndex(x, y, numSection);
                    if (sections[index] == null || sections[index].count <= 0) { continue; }
                    Aabb bound = bounds[index].boundBox;
                    float3 topCenter = bound.center + new float3(0f, bound.extents.y, 0f);
                    float cellDistance = math.lengthsq(origin - topCenter);
                    if (cellDistance > maxDistanceSq) { continue; }
                    eligible = true;
                    if (cellDistance < distance) { distance = cellDistance; }
                    if (!visible && GeometryUtility.TestPlanesAABB(planes, bound)) { visible = true; }
                }
            }
            return eligible;
        }

        internal static bool ApplySettings(FoliageRenderSettings settings)
        {
            int terrainBudget = math.max(settings.residentTerrainBudget, 1);
            int pageBudget = math.max(settings.grassDetailPageBudget, 0);
            int holdFrames = math.max(settings.pageVisibilityHoldFrames, 0);
            float grassScale = math.max(settings.grassDistanceScale, 0f);
            bool settingsChanged = s_TerrainBudget != terrainBudget || s_DetailPageBudget != pageBudget ||
                s_PageVisibilityHoldFrames != holdFrames || s_GrassDistanceScale != grassScale;
            s_TerrainBudget = terrainBudget;
            s_DetailPageBudget = pageBudget;
            s_PageVisibilityHoldFrames = holdFrames;
            s_GrassDistanceScale = grassScale;
            return settingsChanged;
        }

        internal static void UpdateView(Camera camera, in float3 viewOrigin, FoliageRenderSettings settings)
        {
            if (camera == null || !IsResidentCameraType(camera.cameraType)) { return; }
            bool settingsChanged = ApplySettings(settings);
            s_Frame = Time.frameCount;
            ulong cameraKey = EntityId.ToULong(camera.GetEntityId());
            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 projectionMatrix = camera.projectionMatrix;
            if (!s_Views.TryGetValue(cameraKey, out CameraView view))
            {
                view = new CameraView();
                s_Views.Add(cameraKey, view);
            }
            else if (!settingsChanged && view.frame == s_Frame && math.all(view.origin == viewOrigin) &&
                view.viewMatrix == viewMatrix && view.projectionMatrix == projectionMatrix) { return; }
            view.camera = camera;
            view.origin = viewOrigin;
            view.viewMatrix = viewMatrix;
            view.projectionMatrix = projectionMatrix;
            view.frame = s_Frame;
            GeometryUtility.CalculateFrustumPlanes(camera, view.planes);
            s_StaleViews.Clear();
            foreach (var pair in s_Views)
            {
                if (pair.Value.camera == null || s_Frame - pair.Value.frame > 2) { s_StaleViews.Add(pair.Key); }
            }
            for (int i = 0; i < s_StaleViews.Count; ++i) { s_Views.Remove(s_StaleViews[i]); }
            s_TerrainSort.Clear();
            foreach (var pair in s_Entries)
            {
                TerrainEntry entry = pair.Value;
                if (entry.terrain == null || entry.terrain.terrainData == null) { continue; }
                entry.distance = float.MaxValue;
                foreach (var viewPair in s_Views)
                {
                    float distance = DistanceSquared(viewPair.Value.origin, entry.terrain.transform.position, entry.terrain.terrainData.size);
                    if (distance < entry.distance) { entry.distance = distance; }
                }
                s_TerrainSort.Add(entry);
            }
            s_TerrainSort.Sort((a, b) =>
            {
                float da = a.distance * (a.resident ? 0.81f : 1f);
                float db = b.distance * (b.resident ? 0.81f : 1f);
                int result = da.CompareTo(db);
                if (result != 0) { return result; }
                return EntityId.ToULong(a.terrain.GetEntityId()).CompareTo(EntityId.ToULong(b.terrain.GetEntityId()));
            });
            s_ResidentTerrains = 0;
            for (int i = 0; i < s_TerrainSort.Count; ++i)
            {
                TerrainEntry entry = s_TerrainSort[i];
                bool resident = i < s_TerrainBudget;
                if (resident) { ++s_ResidentTerrains; }
                if (entry.resident != resident)
                {
                    entry.resident = resident;
                    if (entry.grass != null)
                    {
                        if (!resident) { s_PageEvictions += entry.grass.ResidentDetailPageCount(); }
                        entry.grass.SetResident(resident);
                    }
                    if (entry.tree != null) { entry.tree.SetResident(resident); }
                }
                else if (resident)
                {
                    if (entry.grass != null) { entry.grass.SetResident(true); }
                    if (entry.tree != null) { entry.tree.SetResident(true); }
                }
            }

            s_PageSort.Clear();
            for (int i = 0; i < s_TerrainSort.Count; ++i)
            {
                TerrainEntry entry = s_TerrainSort[i];
                if (!entry.resident || entry.grass == null) { continue; }
                GrassComponent grass = entry.grass;
                int speciesCount = grass.grassSectors == null ? 0 : grass.grassSectors.Length;
                BoundSection[] bounds = grass.boundSector == null ? null : grass.boundSector.sections;
                for (int species = 0; species < speciesCount; ++species)
                {
                    GrassSection[] sections = grass.grassSectors[species].sections;
                    for (int x = 0; x < FoliageAssetCodec.PageAxis; ++x)
                    {
                        for (int y = 0; y < FoliageAssetCodec.PageAxis; ++y)
                        {
                            if (grass.IsDetailEmpty(species, x, y))
                            {
                                if (grass.IsDetailResident(species, x, y)) { ++s_PageEvictions; }
                                grass.SetDetailResident(species, x, y, false, 0);
                                continue;
                            }
                            PageCandidate candidate = new PageCandidate();
                            candidate.component = grass;
                            candidate.species = species;
                            candidate.speciesKey = grass.grassSectors[species].speciesKey;
                            candidate.pageX = x;
                            candidate.pageY = y;
                            candidate.visible = false;
                            candidate.distance = float.MaxValue;
                            bool eligible = false;
                            foreach (var viewPair in s_Views)
                            {
                                CameraView candidateView = viewPair.Value;
                                if (!PageDemand(bounds, sections, grass.numSection, x, y, candidateView.origin, candidateView.planes,
                                    grass.ResolveDrawDistance(settings), out bool visible, out float distance)) { continue; }
                                eligible = true;
                                if (visible) { candidate.visible = true; }
                                if (distance < candidate.distance) { candidate.distance = distance; }
                            }
                            if (!eligible)
                            {
                                if (grass.IsDetailResident(species, x, y)) { ++s_PageEvictions; }
                                grass.SetDetailResident(species, x, y, false, 0);
                                continue;
                            }
                            candidate.resident = grass.IsDetailResident(species, x, y);
                            if (candidate.visible) { grass.MarkPageVisible(species, x, y, s_Frame); }
                            else if (candidate.resident)
                            {
                                int lastVisible = grass.PageLastVisible(species, x, y);
                                candidate.visible = lastVisible >= 0 && s_Frame - lastVisible <= s_PageVisibilityHoldFrames;
                            }
                            candidate.lastUse = grass.PageLastUsed(species, x, y);
                            s_PageSort.Add(candidate);
                        }
                    }
                }
            }
            s_PageSort.Sort((a, b) =>
            {
                if (a.speciesKey != b.speciesKey) { return a.speciesKey.CompareTo(b.speciesKey); }
                if (a.visible != b.visible) { return a.visible ? -1 : 1; }
                float da = a.distance * (a.resident ? 0.81f : 1f);
                float db = b.distance * (b.resident ? 0.81f : 1f);
                int result = da.CompareTo(db);
                if (result != 0) { return result; }
                if (a.resident != b.resident) { return a.resident ? -1 : 1; }
                result = b.lastUse.CompareTo(a.lastUse);
                if (result != 0) { return result; }
                result = EntityId.ToULong(a.component.GetEntityId()).CompareTo(EntityId.ToULong(b.component.GetEntityId()));
                if (result != 0) { return result; }
                result = a.pageX.CompareTo(b.pageX);
                return result != 0 ? result : a.pageY.CompareTo(b.pageY);
            });
            ulong lastSpecies = 0;
            bool hasSpecies = false;
            int used = 0;
            s_SelectedDetailPages = 0;
            for (int i = 0; i < s_PageSort.Count; ++i)
            {
                PageCandidate candidate = s_PageSort[i];
                if (!hasSpecies || candidate.speciesKey != lastSpecies)
                {
                    lastSpecies = candidate.speciesKey;
                    hasSpecies = true;
                    used = 0;
                }
                candidate.selected = used < s_DetailPageBudget;
                if (candidate.selected)
                {
                    ++used;
                    ++s_SelectedDetailPages;
                    if (candidate.resident) { ++s_PageHits; }
                }
                else if (candidate.resident)
                {
                    ++s_PageEvictions;
                    candidate.component.SetDetailResident(candidate.species, candidate.pageX, candidate.pageY, false, 0);
                }
                s_PageSort[i] = candidate;
            }
            for (int i = 0; i < s_PageSort.Count; ++i)
            {
                PageCandidate candidate = s_PageSort[i];
                if (candidate.selected)
                {
                    candidate.component.SetDetailResident(candidate.species, candidate.pageX, candidate.pageY, true, s_Frame);
                }
            }
        }
    }
}
