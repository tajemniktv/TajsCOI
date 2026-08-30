// Taj's COI Mods | BlueprintsLibraryNativeAdapter.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using Mafi.Core.Entities.Blueprints;

namespace TajsCOI.Core.Blueprints
{
    /// <summary>
    /// Thin, scene-owned bridge to the native 0.8.7b blueprint library. It deliberately forwards
    /// all mutations to the game's authority; the sidecar store contains metadata only.
    /// </summary>
    public sealed class BlueprintsLibraryNativeAdapter
    {
        private readonly BlueprintsLibrary m_library;

        public BlueprintsLibraryNativeAdapter(BlueprintsLibrary library) => m_library = library ?? throw new ArgumentNullException(nameof(library));

        public bool IsReady => m_library.LibraryStatus != BlueprintsLibrary.Status.LoadingInProgress;

        public bool TryImport(IBlueprintsFolder targetFolder, string nativePayload, out IBlueprintItem? item, out string error)
        {
            if (targetFolder is null) throw new ArgumentNullException(nameof(targetFolder));
            item = null;
            if (!IsReady) { error = "Blueprint library is still loading."; return false; }
            try
            {
                if (!m_library.TryAddBlueprintFromString(targetFolder, nativePayload ?? string.Empty, out IBlueprintItem result))
                {
                    error = "Native blueprint payload was rejected.";
                    return false;
                }
                item = result;
                error = string.Empty;
                return true;
            }
            catch (Exception ex) { error = "Native blueprint import failed: " + ex.Message; return false; }
        }

        public bool TryExport(IBlueprint blueprint, out string payload, out string error)
        {
            if (blueprint is null) throw new ArgumentNullException(nameof(blueprint));
            try
            {
                payload = m_library.ConvertToString(blueprint, clearHubIds: true);
                error = string.Empty;
                return !string.IsNullOrWhiteSpace(payload);
            }
            catch (Exception ex) { payload = string.Empty; error = "Native blueprint export failed: " + ex.Message; return false; }
        }

        public bool TryRename(IBlueprintItem item, string title, out string error) => TryMutation(() => m_library.RenameItem(item, title), out error);
        public bool TrySetDescription(IBlueprintItem item, string description, out string error) => TryMutation(() => m_library.SetDescription(item, description), out error);
        public bool TryDelete(IBlueprintsFolder parent, IBlueprintItem item, out string error) => TryMutation(() => m_library.DeleteItem(parent, item), out error);
        public bool TryMoveBlueprint(IBlueprint blueprint, IBlueprintsFolder currentParent, IBlueprintsFolder newParent, out string error) => TryMutation(() => m_library.TryMoveBlueprint(blueprint, currentParent, newParent), out error);

        private static bool TryMutation(Action action, out string error)
        {
            try { action(); error = string.Empty; return true; }
            catch (Exception ex) { error = "Native blueprint library operation failed: " + ex.Message; return false; }
        }

        private static bool TryMutation(Func<bool> action, out string error)
        {
            try { bool result = action(); error = result ? string.Empty : "Native blueprint library rejected the operation."; return result; }
            catch (Exception ex) { error = "Native blueprint library operation failed: " + ex.Message; return false; }
        }
    }
}
