// Taj's COI Mods | TajsDashboardBlueprintsPanel.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Entities.Blueprints;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Ui;
using TajsCOI.Core.Blueprints;
using Button = Mafi.Unity.UiToolkit.Library.Button;
using Column = Mafi.Unity.UiToolkit.Library.Column;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using Panel = Mafi.Unity.UiToolkit.Library.Panel;
using Row = Mafi.Unity.UiToolkit.Library.Row;
using TextField = Mafi.Unity.UiToolkit.Library.TextField;

namespace TajsCOI.Core.Settings
{
    /// <summary>
    ///     Scene-owned management surface for the native blueprint library. The panel keeps only
    ///     short-lived row snapshots and forwards every mutation through the supported native API.
    /// </summary>
    internal static class TajsDashboardBlueprintsPanel
    {
        private sealed class BlueprintRow
        {
            internal BlueprintRow(
                string id,
                string path,
                IBlueprintItem item,
                IBlueprintsFolder parent,
                bool isFolder)
            {
                Id = id;
                Path = path;
                Item = item;
                Parent = parent;
                IsFolder = isFolder;
            }

            internal string Id { get; }
            internal string Path { get; }
            internal IBlueprintItem Item { get; }
            internal IBlueprintsFolder Parent { get; }
            internal bool IsFolder { get; }
        }

        internal static Panel Build(BlueprintsLibrary library, Action queueRefresh)
        {
            if (library is null) throw new ArgumentNullException(nameof(library));
            if (queueRefresh is null) throw new ArgumentNullException(nameof(queueRefresh));

            BlueprintsLibraryNativeAdapter adapter = new(library);
            IBlueprintsFolder root = library.Root;
            IReadOnlyList<BlueprintRow> rows = SnapshotRows(root);
            var rowsById = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
            string recyclePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsCOI",
                "BlueprintRecycleBin.json");
            var recycleBin = new BlueprintRecycleBinStore(recyclePath);
            recycleBin.Load(out string recycleLoadError);

            Panel panel = TajsDashboardUi.Card(
                "Blueprint library",
                "Native folders and blueprints are persisted by Captain of Industry. Selection is a short-lived view snapshot; edits are forwarded through the native library authority.");

            Label status = new Label(
                    ("Library status: " + library.LibraryStatus + " · " + rows.Count.ToString(CultureInfo.InvariantCulture) + " items · recycle bin: " + recycleBin.Snapshot().Count.ToString(CultureInfo.InvariantCulture)).AsLoc())
                .FontSize(11)
                .Selectable(true);
            panel.Body.Add(status);
            if (recycleLoadError.Length > 0)
            {
                panel.Body.Add(new Label(("Recycle-bin warning: " + recycleLoadError).AsLoc()).FontSize(11).Selectable(true));
            }

            Label feedback = new Label().FontSize(11).Hide();
            Row toolbar = new Row(3.pt()).AlignItemsCenter().Wrap();
            toolbar.Add(
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "New folder",
                    "Assets/Unity/UserInterface/General/Folder.svg",
                    () =>
                    {
                        try
                        {
                            IBlueprintsFolder folder = library.AddNewFolder(root);
                            feedback.Value(("Created folder: " + folder.Name).AsLoc()).Show();
                            queueRefresh();
                        }
                        catch (Exception ex)
                        {
                            feedback.Value(("Folder creation failed: " + ex.Message).AsLoc()).Show();
                        }
                    }),
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Refresh",
                    "Assets/Unity/UserInterface/General/Repeat.svg",
                    queueRefresh),
                feedback);
            panel.Body.Add(toolbar);

            TextField filter = new TextField().Placeholder("Search name, folder, path, or prototype".AsLoc()).MaxWidth(420.px());
            var tableModel = new DataTableModel<BlueprintRow>(
                new[]
                {
                    DataTableColumn<BlueprintRow>.CreateText(
                        "name",
                        "Name",
                        row => row.Item.Name,
                        width: DataTableColumnWidth.Constrained(180, 300),
                        visibilityPriority: 10),
                    DataTableColumn<BlueprintRow>.CreateText(
                        "kind",
                        "Type",
                        row => row.IsFolder ? "Folder" : "Blueprint",
                        width: DataTableColumnWidth.Fixed(100),
                        visibilityPriority: 9),
                    DataTableColumn<BlueprintRow>.CreateText(
                        "path",
                        "Path",
                        row => row.Path,
                        width: DataTableColumnWidth.Constrained(180, 420),
                        visibilityPriority: 8),
                    DataTableColumn<BlueprintRow>.CreateText(
                        "summary",
                        "Summary",
                        FormatSummary,
                        width: DataTableColumnWidth.Flex(),
                        visibilityPriority: 1),
                },
                row => row.Id,
                DataTableSelectionMode.Single);

            BlueprintRow? selected = null;
            Label detail = new Label("Select a folder or blueprint to inspect its details.".AsLoc())
                .FontSize(11)
                .Selectable(true);
            TextField name = new TextField().Placeholder("Selected name".AsLoc()).MaxWidth(320.px());
            TextField description = new TextField().Placeholder("Selected description".AsLoc()).MaxWidth(520.px());
            TextField deleteConfirmation = new TextField().Placeholder("Type DELETE to remove".AsLoc()).MaxWidth(190.px());
            Label exportText = new Label().FontSize(10).Selectable(true).Hide();
            TextField importText = new TextField().Placeholder("Paste native blueprint/folder payload".AsLoc()).MaxWidth(700.px());
            Label importPreview = new Label().FontSize(11).Selectable(true);

            void BindDetails(BlueprintRow? row)
            {
                selected = row;
                if (row is null)
                {
                    name.Text(string.Empty);
                    description.Text(string.Empty);
                    detail.Value("Select a folder or blueprint to inspect its details.".AsLoc());
                    return;
                }

                name.Text(row.Item.Name);
                description.Text(row.Item.Desc);
                detail.Value(FormatDetails(row).AsLoc());
                deleteConfirmation.Text(string.Empty);
            }

            TajsDataTable<BlueprintRow> table = new(
                tableModel,
                ids => BindDetails(ids.Count == 1 && rowsById.TryGetValue(ids.First(), out BlueprintRow? row) ? row : null));
            table.Refresh(rows);
            table.SetAvailableWidth(760f);
            filter.OnValueChanged(_ => table.SetFilter(row => Matches(row, filter.GetText())));
            panel.Body.Add(filter, table);

            Panel selectedPanel = TajsDashboardUi.Card("Selected item", "Metadata edits are native library operations and are persisted by the game.");
            Row metadata = new Row(3.pt()).AlignItemsCenter().Wrap();
            Label metadataFeedback = new Label().FontSize(11).Hide();
            metadata.Add(
                name,
                description,
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Save metadata",
                    "Assets/Unity/UserInterface/General/Save.svg",
                    () =>
                    {
                        if (selected is null)
                        {
                            metadataFeedback.Value("Select an item first.".AsLoc()).Show();
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(name.GetText()))
                        {
                            metadataFeedback.Value("A blueprint or folder name cannot be empty.".AsLoc()).Show();
                            return;
                        }

                        bool renamed = adapter.TryRename(selected.Item, name.GetText().Trim(), out string renameError);
                        bool described = adapter.TrySetDescription(selected.Item, description.GetText(), out string descriptionError);
                        if (!renamed || !described)
                        {
                            metadataFeedback.Value(("Metadata was not fully saved: " + (renameError.Length > 0 ? renameError : descriptionError)).AsLoc()).Show();
                            return;
                        }

                        metadataFeedback.Value("Metadata saved.".AsLoc()).Show();
                        queueRefresh();
                    }),
                metadataFeedback);
            selectedPanel.Body.Add(detail, metadata);

            Row selectedActions = new Row(3.pt()).AlignItemsCenter().Wrap();
            selectedActions.Add(
                TajsDashboardUi.ActionButton(
                    Button.Warning,
                    "Delete",
                    "Assets/Unity/UserInterface/General/Cancel.svg",
                    () =>
                    {
                        if (selected is null)
                        {
                            metadataFeedback.Value("Select an item first.".AsLoc()).Show();
                            return;
                        }
                        if (!string.Equals(deleteConfirmation.GetText().Trim(), "DELETE", StringComparison.Ordinal))
                        {
                            metadataFeedback.Value("Type DELETE before removing a library item.".AsLoc()).Show();
                            return;
                        }

                        if (!TryCreatePortableEnvelope(library, selected, out BlueprintPortableEnvelope? envelope, out string error) ||
                            !recycleBin.TryAdd(envelope!, out error) ||
                            !recycleBin.Save(out error))
                        {
                            metadataFeedback.Value(("Item was not moved to the recycle bin: " + error).AsLoc()).Show();
                            return;
                        }

                        if (!adapter.TryDelete(selected.Parent, selected.Item, out error))
                        {
                            recycleBin.Remove(envelope!.StableId);
                            recycleBin.Save(out _);
                            metadataFeedback.Value(error.AsLoc()).Show();
                            return;
                        }

                        metadataFeedback.Value("Item moved to the recycle bin; restore or purge it below.".AsLoc()).Show();
                        queueRefresh();
                    }),
                deleteConfirmation);
            selectedPanel.Body.Add(selectedActions);

            Panel sharing = TajsDashboardUi.Card(
                "Portable export/import",
                "Native payloads are versioned by the game. Preview parses without adding anything; import remains explicit and reports malformed content before mutation.");
            Row sharingActions = new Row(3.pt()).AlignItemsCenter().Wrap();
            sharingActions.Add(
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Export selected",
                    "Assets/Unity/UserInterface/General/ExportToString.svg",
                    () =>
                    {
                        if (selected is not BlueprintRow row)
                        {
                            sharingActions.Add(new Label("Select a blueprint or folder first.".AsLoc()).FontSize(11));
                            return;
                        }

                        try
                        {
                            string payload = row.Item is IBlueprint blueprint
                                ? library.ConvertToString(blueprint, clearHubIds: true)
                                : row.Item is IBlueprintsFolder folder
                                    ? library.ConvertToString(folder, clearHubIds: true)
                                    : string.Empty;
                            string portable = BlueprintPortableCodec.Export(row.Item, payload, ParentPath(row.Path), GetPrototypeIds(row.Item));
                            exportText.Value(("Export payload:\n" + portable).AsLoc()).Show();
                        }
                        catch (Exception ex)
                        {
                            exportText.Value(("Export failed: " + ex.Message).AsLoc()).Show();
                        }
                    }),
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Preview import",
                    "Assets/Unity/UserInterface/General/Search.svg",
                    () =>
                    {
                        if (!TryGetNativePayload(importText.GetText(), out string nativePayload, out string envelopeError))
                        {
                            importPreview.Value(("Import preview rejected: " + envelopeError).AsLoc());
                        }
                        else if (adapter.TryPreview(nativePayload, out IBlueprintItem? item, out string error))
                        {
                            IBlueprintItem previewItem = item!;
                            string missing = GetMissingContent(previewItem);
                            importPreview.Value(
                                ("Preview: " + (previewItem is IBlueprintsFolder ? "folder" : "blueprint") +
                                 " · " + previewItem.Name + " · " +
                                 (missing.Length == 0 ? "ready to import" : "missing content: " + missing) +
                                 " · no library mutation").AsLoc());
                        }
                        else
                        {
                            importPreview.Value(("Import preview rejected: " + error).AsLoc());
                        }
                    }),
                TajsDashboardUi.ActionButton(
                    Button.Primary,
                    "Import to root",
                    "Assets/Unity/UserInterface/General/Package.svg",
                    () =>
                    {
                        if (!TryGetNativePayload(importText.GetText(), out string nativePayload, out string envelopeError))
                        {
                            importPreview.Value(("Import rejected: " + envelopeError).AsLoc());
                            return;
                        }
                        if (!adapter.TryPreview(nativePayload, out IBlueprintItem? previewItem, out string previewError))
                        {
                            importPreview.Value(("Import rejected: " + previewError).AsLoc());
                            return;
                        }
                        string missingContent = GetMissingContent(previewItem!);
                        if (missingContent.Length > 0)
                        {
                            importPreview.Value(("Import rejected; missing content: " + missingContent).AsLoc());
                            return;
                        }
                        if (!adapter.TryImport(root, nativePayload, out _, out string error))
                        {
                            importPreview.Value(("Import rejected: " + error).AsLoc());
                            return;
                        }

                        importPreview.Value("Imported into the native root folder.".AsLoc());
                        importText.Text(string.Empty);
                        queueRefresh();
                    }));
            sharing.Body.Add(sharingActions, importText, importPreview, exportText);

            panel.Body.Add(selectedPanel, sharing, BuildRecycleBinPanel(adapter, root, recycleBin, queueRefresh));
            return panel;
        }

        private static Panel BuildRecycleBinPanel(
            BlueprintsLibraryNativeAdapter adapter,
            IBlueprintsFolder root,
            BlueprintRecycleBinStore recycleBin,
            Action queueRefresh)
        {
            Panel panel = TajsDashboardUi.Card(
                "Recycle bin",
                "Deleted native items remain as portable payloads until explicitly restored or purged. The sidecar contains values only, so dashboard rebuilds cannot retain stale UI objects.");
            IReadOnlyList<BlueprintPortableEnvelope> entries = recycleBin.Snapshot();
            if (entries.Count == 0)
            {
                panel.Body.Add(new Label("Recycle bin is empty.".AsLoc()).FontSize(11));
                return panel;
            }

            TextField purgeConfirmation = new TextField().Placeholder("Type PURGE to permanently remove".AsLoc()).MaxWidth(260.px());
            Label feedback = new Label().FontSize(11).Hide();
            panel.Body.Add(purgeConfirmation);
            foreach (BlueprintPortableEnvelope entry in entries)
            {
                Row row = new Row(3.pt()).AlignItemsCenter().Wrap();
                string kind = string.Equals(entry.ItemKind, "folder", StringComparison.OrdinalIgnoreCase) ? "Folder" : "Blueprint";
                row.Add(
                    new Label((entry.Name + " · " + kind + " · " + entry.FolderPath).AsLoc()).FontSize(11).FlexGrow(1f),
                    TajsDashboardUi.ActionButton(
                        Button.Area,
                        "Restore",
                        "Assets/Unity/UserInterface/General/Repeat.svg",
                        () =>
                        {
                            IBlueprintsFolder target = FindFolder(root, entry.FolderPath) ?? root;
                            if (!adapter.TryImport(target, entry.NativePayload, out _, out string error))
                            {
                                feedback.Value(("Restore failed: " + error).AsLoc()).Show();
                                return;
                            }
                            recycleBin.Remove(entry.StableId);
                            if (!recycleBin.Save(out error))
                            {
                                feedback.Value(("Restored, but recycle-bin state could not be saved: " + error).AsLoc()).Show();
                                return;
                            }
                            feedback.Value(("Restored " + entry.Name + ".").AsLoc()).Show();
                            queueRefresh();
                        }),
                    TajsDashboardUi.ActionButton(
                        Button.Warning,
                        "Purge",
                        "Assets/Unity/UserInterface/General/Cancel.svg",
                        () =>
                        {
                            if (!string.Equals(purgeConfirmation.GetText().Trim(), "PURGE", StringComparison.Ordinal))
                            {
                                feedback.Value("Type PURGE before permanently removing a recycled item.".AsLoc()).Show();
                                return;
                            }
                            recycleBin.Remove(entry.StableId);
                            if (!recycleBin.Save(out string error))
                            {
                                feedback.Value(("Purge failed: " + error).AsLoc()).Show();
                                return;
                            }
                            feedback.Value(("Purged " + entry.Name + ".").AsLoc()).Show();
                            queueRefresh();
                        }));
                panel.Body.Add(row);
            }
            panel.Body.Add(feedback);
            return panel;
        }

        private static bool TryCreatePortableEnvelope(
            BlueprintsLibrary library,
            BlueprintRow row,
            out BlueprintPortableEnvelope? envelope,
            out string error)
        {
            envelope = null;
            error = string.Empty;
            try
            {
                string nativePayload = row.Item is IBlueprint blueprint
                    ? library.ConvertToString(blueprint, clearHubIds: true)
                    : row.Item is IBlueprintsFolder folder
                        ? library.ConvertToString(folder, clearHubIds: true)
                        : string.Empty;
                string serialized = BlueprintPortableCodec.Export(row.Item, nativePayload, ParentPath(row.Path), GetPrototypeIds(row.Item));
                if (!BlueprintPortableCodec.TryRead(serialized, out envelope, out error))
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Native blueprint export failed: " + ex.Message;
                return false;
            }
        }

        private static string ParentPath(string path)
        {
            int separator = path.LastIndexOf('/');
            return separator < 0 ? string.Empty : path.Substring(0, separator);
        }

        private static IBlueprintsFolder? FindFolder(IBlueprintsFolder root, string path)
        {
            string normalized = path?.Trim('/') ?? string.Empty;
            if (normalized.Length == 0) return root;
            IBlueprintsFolder current = root;
            foreach (string segment in normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                IBlueprintsFolder? next = current.Folders.FirstOrDefault(folder => string.Equals(folder.Name, segment, StringComparison.Ordinal));
                if (next is null) return null;
                current = next;
            }
            return current;
        }

        private static IReadOnlyList<BlueprintRow> SnapshotRows(IBlueprintsFolder root)
        {
            var result = new List<BlueprintRow>();
            AddFolder(root, "", result, includeSelf: false);
            return result;
        }

        private static void AddFolder(
            IBlueprintsFolder folder,
            string parentPath,
            ICollection<BlueprintRow> output,
            bool includeSelf)
        {
            string path = includeSelf
                ? CombinePath(parentPath, folder.Name)
                : parentPath;
            if (includeSelf)
            {
                output.Add(new BlueprintRow("folder:" + path, path, folder, folder.ParentFolder.ValueOrNull ?? folder, isFolder: true));
            }

            foreach (IBlueprintsFolder child in folder.Folders)
            {
                AddFolder(child, path, output, includeSelf: true);
            }

            foreach (IBlueprint blueprint in folder.Blueprints)
            {
                string blueprintPath = CombinePath(path, blueprint.Name);
                output.Add(new BlueprintRow("blueprint:" + blueprintPath, blueprintPath, blueprint, folder, isFolder: false));
            }
        }

        private static string CombinePath(string parent, string name) =>
            string.IsNullOrEmpty(parent) ? name : parent + "/" + name;

        private static IReadOnlyList<string> GetPrototypeIds(IBlueprintItem item)
        {
            if (item is IBlueprint blueprint)
            {
                return blueprint.AllMajorProtos
                    .Select(pair => pair.Key.Id.Value)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();
            }

            var references = new Set<string>();
            item.CollectReferencedProtoRefs(references);
            return references.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        private static bool TryGetNativePayload(string text, out string nativePayload, out string error)
        {
            nativePayload = text?.Trim() ?? string.Empty;
            error = string.Empty;
            if (nativePayload.Length == 0)
            {
                error = "Import payload is empty.";
                return false;
            }

            // Native game payloads begin with B/F framing bytes. JSON is reserved for the
            // versioned Tajs envelope so malformed envelopes cannot be mistaken for raw content.
            if (!nativePayload.StartsWith("{", StringComparison.Ordinal))
            {
                return true;
            }

            if (!BlueprintPortableCodec.TryRead(nativePayload, out BlueprintPortableEnvelope? envelope, out error))
            {
                return false;
            }

            nativePayload = envelope!.NativePayload;
            return true;
        }

        private static string FormatSummary(BlueprintRow row)
        {
            if (row.Item is IBlueprintsFolder folder)
            {
                return folder.Folders.Count.ToString(CultureInfo.InvariantCulture) + " folders · " +
                       folder.Blueprints.Count.ToString(CultureInfo.InvariantCulture) + " blueprints";
            }

            if (row.Item is not IBlueprint blueprint)
            {
                return string.Empty;
            }

            string mods = blueprint.ReferencedModIds.Length == 0
                ? "base content"
                : blueprint.ReferencedModIds.Length.ToString(CultureInfo.InvariantCulture) + " mods";
            return blueprint.Items.Length.ToString(CultureInfo.InvariantCulture) + " entities · " +
                   blueprint.MostFrequentProtos.Length.ToString(CultureInfo.InvariantCulture) + " protos · " + mods;
        }

        private static string FormatDetails(BlueprintRow row)
        {
            var lines = new List<string>
            {
                row.IsFolder ? "Folder" : "Blueprint",
                "Path: " + row.Path,
                "Description: " + (row.Item.Desc ?? string.Empty),
                "Hub identity: " + row.Item.HubId.ToString(CultureInfo.InvariantCulture) + "/" + row.Item.HubVersionId.ToString(CultureInfo.InvariantCulture),
            };
            if (row.Item is IBlueprintsFolder folder)
            {
                lines.Add("Folders: " + folder.Folders.Count.ToString(CultureInfo.InvariantCulture));
                lines.Add("Blueprints: " + folder.Blueprints.Count.ToString(CultureInfo.InvariantCulture));
                return string.Join("\n", lines);
            }

            if (row.Item is IBlueprint blueprint)
            {
                lines.Add("Entities: " + blueprint.Items.Length.ToString(CultureInfo.InvariantCulture));
                lines.Add("Major prototypes: " + blueprint.AllMajorProtos.Length.ToString(CultureInfo.InvariantCulture));
                lines.Add("Referenced mods: " + (blueprint.ReferencedModIds.Length == 0 ? "none" : string.Join(", ", blueprint.ReferencedModIds)));
                string missing = blueprint.ProtosThatFailedToLoad.ValueOrNull ?? string.Empty;
                if (missing.Length > 0)
                {
                    lines.Add("Missing content:\n" + missing);
                }
            }
            return string.Join("\n", lines);
        }

        private static string GetMissingContent(IBlueprintItem item)
        {
            if (item is not IBlueprint blueprint)
            {
                return string.Empty;
            }

            return blueprint.ProtosThatFailedToLoad.ValueOrNull ?? string.Empty;
        }

        private static bool Matches(BlueprintRow row, string? rawSearch)
        {
            string search = rawSearch?.Trim() ?? string.Empty;
            if (search.Length == 0)
            {
                return true;
            }

            return row.Item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   row.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (row.Item.Desc ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   row.Item is IBlueprint blueprint && blueprint.ReferencedModIds.Any(mod => mod.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
