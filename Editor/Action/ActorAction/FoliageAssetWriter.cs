using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Landscape.FoliagePipeline.Editor
{
    internal class FoliageAssetWriter
    {
        internal class Entry
        {
            public string name;
            public byte[] bytes;

            public Entry(string name, byte[] bytes)
            {
                this.name = name;
                this.bytes = bytes;
            }
        }

        internal class Request
        {
            public string assetKey;
            public string category;
            public List<Entry> entries;

            public Request(string assetKey, string category, List<Entry> entries)
            {
                this.assetKey = assetKey;
                this.category = category;
                this.entries = entries;
            }
        }

        private class RootPlan
        {
            public string root;
            public string assetRoot;
            public List<Request> requests = new List<Request>();
            public HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
        }

        private class FileChange
        {
            public string path;
            public string staged;
            public string backup;
            public byte[] bytes;
            public byte[] previous;
            public bool hadMeta;
            public bool attempted;
            public bool restoreFailed;
        }

        private class BatchPlan
        {
            public List<RootPlan> roots = new List<RootPlan>();
            public List<FileChange> payloads = new List<FileChange>();
            public List<FileChange> manifests = new List<FileChange>();
            public List<string> obsolete = new List<string>();
            public List<string> createdDirectories = new List<string>();
        }

        internal static string AssetKey(GameObject terrainObject)
        {
            string scenePath = terrainObject.scene.path;
            if (string.IsNullOrEmpty(scenePath))
            {
                throw new InvalidOperationException("Save the scene before baking foliage assets.");
            }
            string sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
            GlobalObjectId objectId = GlobalObjectId.GetGlobalObjectIdSlow(terrainObject);
            if (string.IsNullOrEmpty(sceneGuid) || objectId.targetObjectId == 0)
            {
                throw new InvalidOperationException("Terrain has no stable scene object ID; save the scene and rebake.");
            }
            return sceneGuid + "_" + objectId.targetObjectId.ToString("x16");
        }

        internal static void CommitBatch(List<Request> requests)
        {
            if (requests == null) { throw new ArgumentNullException(nameof(requests)); }
            if (requests.Count == 0) { return; }

            BatchPlan plan = BuildPlan(requests);
            List<FileChange> changes = new List<FileChange>(plan.payloads.Count + plan.manifests.Count);
            changes.AddRange(plan.payloads);
            changes.AddRange(plan.manifests);
            string transaction = Guid.NewGuid().ToString("N");
            try
            {
                if (changes.Count > 0)
                {
                    for (int i = 0; i < plan.roots.Count; ++i) { CreateRoot(plan.roots[i].root, plan.createdDirectories); }
                    for (int i = 0; i < changes.Count; ++i) { StageChange(changes[i], transaction); }
                    for (int i = 0; i < changes.Count; ++i) { PromoteChange(changes[i]); }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
            catch (Exception failure)
            {
                List<Exception> errors = new List<Exception>();
                errors.Add(failure);
                bool attempted = false;
                for (int i = 0; i < changes.Count; ++i) { attempted |= changes[i].attempted; }
                Rollback(changes, errors);
                CleanupTemporary(changes, errors);
                CleanupDirectories(plan.createdDirectories, errors);
                if (attempted)
                {
                    try { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
                    catch (Exception refreshFailure) { errors.Add(refreshFailure); }
                }
                if (errors.Count > 1) { throw new AggregateException("Foliage asset batch could not be restored completely.", errors); }
                throw;
            }

            CleanupTemporary(changes, null);
            for (int i = 0; i < plan.obsolete.Count; ++i)
            {
                try
                {
                    if (!AssetDatabase.DeleteAsset(plan.obsolete[i]))
                    {
                        Debug.LogWarning("Could not remove obsolete foliage asset: " + plan.obsolete[i]);
                    }
                }
                catch (Exception deletionFailure)
                {
                    Debug.LogWarning("Could not remove obsolete foliage asset: " + plan.obsolete[i] + " (" + deletionFailure.Message + ")");
                }
            }
        }

        private static BatchPlan BuildPlan(List<Request> requests)
        {
            BatchPlan plan = new BatchPlan();
            Dictionary<string, RootPlan> roots = new Dictionary<string, RootPlan>(StringComparer.Ordinal);
            for (int i = 0; i < requests.Count; ++i)
            {
                Request request = requests[i];
                if (request == null || !SafeName(request.assetKey) || (request.category != "grass_" && request.category != "tree_") || request.entries == null)
                {
                    throw new InvalidDataException("Invalid foliage asset request.");
                }
                if (!roots.TryGetValue(request.assetKey, out RootPlan root))
                {
                    root = new RootPlan();
                    root.root = Path.Combine(Application.dataPath, "Generated/Foliage/Resources", request.assetKey);
                    root.assetRoot = "Assets/Generated/Foliage/Resources/" + request.assetKey;
                    roots.Add(request.assetKey, root);
                    plan.roots.Add(root);
                }
                if (!root.categories.Add(request.category)) { throw new InvalidDataException("Duplicate foliage asset category in batch."); }
                root.requests.Add(request);
                for (int j = 0; j < request.entries.Count; ++j)
                {
                    Entry entry = request.entries[j];
                    if (entry == null || entry.bytes == null || !SafeName(entry.name) || !entry.name.StartsWith(request.category, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Invalid generated foliage asset name.");
                    }
                }
            }

            for (int i = 0; i < plan.roots.Count; ++i)
            {
                RootPlan root = plan.roots[i];
                string manifestPath = Path.Combine(root.root, "index.bytes");
                List<string> previous = File.Exists(manifestPath)
                    ? FoliageAssetCodec.DecodeManifest(File.ReadAllBytes(manifestPath))
                    : new List<string>();
                HashSet<string> previousNames = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < previous.Count; ++j)
                {
                    string name = previous[j];
                    if (!SafeName(name)) { throw new InvalidDataException("Invalid path in foliage asset index."); }
                    if (!previousNames.Add(name)) { throw new InvalidDataException("Duplicate foliage asset in index."); }
                    bool replacing = false;
                    foreach (string category in root.categories) { replacing |= name.StartsWith(category, StringComparison.Ordinal); }
                    if (!replacing) { current.Add(name); }
                }

                for (int j = 0; j < root.requests.Count; ++j)
                {
                    Request request = root.requests[j];
                    for (int k = 0; k < request.entries.Count; ++k)
                    {
                        Entry entry = request.entries[k];
                        if (!current.Add(entry.name)) { throw new InvalidDataException("Duplicate generated foliage asset name."); }
                        AddChanged(plan.payloads, Path.Combine(root.root, entry.name + ".bytes"), entry.bytes);
                    }
                }

                List<string> names = new List<string>(current);
                names.Sort(StringComparer.Ordinal);
                AddChanged(plan.manifests, manifestPath, FoliageAssetCodec.EncodeManifest(names));
                if (!Directory.Exists(root.root)) { continue; }
                foreach (string category in root.categories)
                {
                    string[] files = Directory.GetFiles(root.root, category + "*.bytes", SearchOption.TopDirectoryOnly);
                    for (int j = 0; j < files.Length; ++j)
                    {
                        string name = Path.GetFileNameWithoutExtension(files[j]);
                        if (!SafeName(name)) { throw new InvalidDataException("Invalid generated foliage asset name."); }
                        if (!current.Contains(name)) { plan.obsolete.Add(root.assetRoot + "/" + name + ".bytes"); }
                    }
                }
            }
            return plan;
        }

        private static void AddChanged(List<FileChange> changes, string path, byte[] bytes)
        {
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (previous != null && EqualBytes(previous, bytes)) { return; }
            FileChange change = new FileChange();
            change.path = path;
            change.bytes = bytes;
            change.previous = previous;
            change.hadMeta = File.Exists(path + ".meta");
            changes.Add(change);
        }

        private static void CreateRoot(string root, List<string> createdDirectories)
        {
            List<string> missing = new List<string>();
            string cursor = root;
            while (!Directory.Exists(cursor))
            {
                missing.Add(cursor);
                cursor = Path.GetDirectoryName(cursor);
            }
            for (int i = missing.Count - 1; i >= 0; --i) { createdDirectories.Add(missing[i]); }
            Directory.CreateDirectory(root);
        }

        private static void StageChange(FileChange change, string transaction)
        {
            string directory = Path.GetDirectoryName(change.path);
            string name = Path.GetFileName(change.path);
            change.staged = Path.Combine(directory, "." + name + ".writing." + transaction);
            change.backup = Path.Combine(directory, "." + name + ".rollback." + transaction);
            File.WriteAllBytes(change.staged, change.bytes);
        }

        private static void PromoteChange(FileChange change)
        {
            if (change.previous == null)
            {
                change.attempted = true;
                File.Move(change.staged, change.path);
            }
            else
            {
                change.attempted = true;
                File.Replace(change.staged, change.path, change.backup);
            }
        }

        private static void Rollback(List<FileChange> changes, List<Exception> errors)
        {
            for (int i = changes.Count - 1; i >= 0; --i)
            {
                FileChange change = changes[i];
                if (!change.attempted) { continue; }
                try
                {
                    if (change.previous == null)
                    {
                        if (!File.Exists(change.staged))
                        {
                            if (File.Exists(change.path))
                            {
                                if (!EqualBytes(File.ReadAllBytes(change.path), change.bytes))
                                {
                                    throw new IOException("New foliage asset changed before rollback: " + change.path);
                                }
                                File.Delete(change.path);
                            }
                            if (!change.hadMeta && File.Exists(change.path + ".meta")) { File.Delete(change.path + ".meta"); }
                        }
                    }
                    else if (File.Exists(change.backup))
                    {
                        if (File.Exists(change.path)) { File.Replace(change.backup, change.path, null); }
                        else { File.Move(change.backup, change.path); }
                    }
                    else if (!File.Exists(change.path) || !EqualBytes(File.ReadAllBytes(change.path), change.previous))
                    {
                        string restore = change.backup + ".restore";
                        File.WriteAllBytes(restore, change.previous);
                        if (File.Exists(change.path)) { File.Replace(restore, change.path, null); }
                        else { File.Move(restore, change.path); }
                    }
                    if (change.previous != null && !change.hadMeta && File.Exists(change.path + ".meta"))
                    {
                        File.Delete(change.path + ".meta");
                    }
                }
                catch (Exception rollbackFailure)
                {
                    change.restoreFailed = true;
                    errors.Add(rollbackFailure);
                }
            }
        }

        private static void CleanupTemporary(List<FileChange> changes, List<Exception> errors)
        {
            for (int i = 0; i < changes.Count; ++i)
            {
                FileChange change = changes[i];
                if (change.restoreFailed) { continue; }
                CleanupFile(change.staged, errors);
                if (change.staged != null) { CleanupFile(change.staged + ".meta", errors); }
                CleanupFile(change.backup, errors);
                if (change.backup != null)
                {
                    CleanupFile(change.backup + ".meta", errors);
                    CleanupFile(change.backup + ".restore", errors);
                    CleanupFile(change.backup + ".restore.meta", errors);
                }
            }
        }

        private static void CleanupFile(string path, List<Exception> errors)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return; }
            try { File.Delete(path); }
            catch (Exception cleanupFailure)
            {
                if (errors == null) { Debug.LogWarning("Could not remove temporary foliage asset: " + path + " (" + cleanupFailure.Message + ")"); }
                else { errors.Add(cleanupFailure); }
            }
        }

        private static void CleanupDirectories(List<string> directories, List<Exception> errors)
        {
            for (int i = directories.Count - 1; i >= 0; --i)
            {
                string directory = directories[i];
                try
                {
                    if (!Directory.Exists(directory) || Directory.GetFileSystemEntries(directory).Length != 0) { continue; }
                    Directory.Delete(directory);
                    string meta = directory + ".meta";
                    if (File.Exists(meta)) { File.Delete(meta); }
                }
                catch (Exception cleanupFailure) { errors.Add(cleanupFailure); }
            }
        }

        private static bool EqualBytes(byte[] first, byte[] second)
        {
            if (first.Length != second.Length) { return false; }
            for (int i = 0; i < first.Length; ++i) { if (first[i] != second[i]) { return false; } }
            return true;
        }

        private static bool SafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Contains("..") || name.Contains("/") || name.Contains("\\")) { return false; }
            for (int i = 0; i < name.Length; ++i)
            {
                char ch = name[i];
                if (!char.IsLetterOrDigit(ch) && ch != '_') { return false; }
            }
            return true;
        }

    }
}
