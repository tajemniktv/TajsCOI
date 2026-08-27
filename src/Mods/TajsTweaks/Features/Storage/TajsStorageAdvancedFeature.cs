// Taj's COI Mods | TajsStorageAdvancedFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Tweaks.Configuration;
using UnityEngine.UIElements;
using UiButton = Mafi.Unity.UiToolkit.Library.Button;
using UiColumn = Mafi.Unity.UiToolkit.Library.Column;
using UiLabel = Mafi.Unity.UiToolkit.Library.Label;
using UiScrollColumn = Mafi.Unity.UiToolkit.Library.ScrollColumn;
using UiTextField = Mafi.Unity.UiToolkit.Library.TextField;
using UiToggle = Mafi.Unity.UiToolkit.Library.Toggle;
using CoiStorage = Mafi.Core.Buildings.Storages.Storage;

namespace TajsCOI.Tweaks.Features.Storage
{
    /// <summary>
    ///     Adds the issue #70 controls at the native storage inspector boundary. Every reflection
    ///     seam is optional; a missing inspector member leaves the vanilla inspector untouched.
    /// </summary>
    internal static class TajsStorageAdvancedFeature
    {
        private sealed class InspectorState
        {
            internal readonly object Inspector;
            internal readonly PanelWithHeader Panel;
            internal readonly UiLabel Status;
            internal readonly UiToggle AllowAll;
            internal readonly IReadOnlyList<(UiToggle Toggle, StorageTransferFields Field)> Fields;
            internal readonly UiTextField ImportUntil;
            internal readonly UiTextField ExportFrom;
            internal readonly UiTextField TransportFrom;
            internal readonly UiTextField TransportUntil;
            internal readonly UiTextField Capacity;
            internal readonly ButtonText ApplyThresholds;
            internal readonly ButtonText ApplyCapacity;
            internal readonly ButtonText Copy;
            internal readonly ButtonText Paste;
            internal readonly ButtonText Preview;
            internal readonly ButtonText ApplyAll;
            internal readonly UiLabel PreviewStatus;
            internal int LastEntityId = -1;
            internal int PreviewSourceId = -1;
            internal string PreviewFingerprint = string.Empty;
            internal string LastShownTransferReport = string.Empty;
            internal bool DirtyThresholds;
            internal bool DirtyCapacity;

            internal InspectorState(
                object inspector,
                PanelWithHeader panel,
                UiLabel status,
                UiToggle allowAll,
                IReadOnlyList<(UiToggle Toggle, StorageTransferFields Field)> fields,
                UiTextField importUntil,
                UiTextField exportFrom,
                UiTextField transportFrom,
                UiTextField transportUntil,
                UiTextField capacity,
                ButtonText applyThresholds,
                ButtonText applyCapacity,
                ButtonText copy,
                ButtonText paste,
                ButtonText preview,
                ButtonText applyAll,
                UiLabel previewStatus)
            {
                Inspector = inspector;
                Panel = panel;
                Status = status;
                AllowAll = allowAll;
                Fields = fields;
                ImportUntil = importUntil;
                ExportFrom = exportFrom;
                TransportFrom = transportFrom;
                TransportUntil = transportUntil;
                Capacity = capacity;
                ApplyThresholds = applyThresholds;
                ApplyCapacity = applyCapacity;
                Copy = copy;
                Paste = paste;
                Preview = preview;
                ApplyAll = applyAll;
                PreviewStatus = previewStatus;
            }

            internal StorageTransferFields SelectedFields()
            {
                var selected = StorageTransferFields.None;
                foreach ((UiToggle toggle, StorageTransferFields field) in Fields)
                {
                    if (toggle.GetValue())
                    {
                        selected |= field;
                    }
                }

                return selected;
            }
        }

        private static readonly ConditionalWeakTable<object, InspectorState> s_states = new();
        private static readonly object s_gate = new();
        private static PropertyInfo? s_entityProperty;
        private static MethodInfo? s_addPanelMethod;
        private static FieldInfo? s_optionsProviderField;
        private static IInputScheduler? s_inputScheduler;
        private static IEntitiesManager? s_entities;
        private static ProtosDb? s_protosDb;
        private static int s_clipboardSourceId = -1;
        private static bool s_patchRegistered;

        [ThreadStatic]
        private static bool s_allowAllProtoCheck;

        private static readonly Percent[] s_lowAlertOptions =
        {
            Percent.Zero, 5.Percent(), 10.Percent(), 15.Percent(), 25.Percent(), 50.Percent(), 75.Percent(),
        };

        private static readonly Percent[] s_highAlertOptions = { 75.Percent(), 80.Percent(), 90.Percent(), Percent.Hundred };

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            if (resolver.TryResolve(out ISaveManager saveManager))
            {
                TajsStorageAdvancedState.LoadForSave(saveManager.GameName);
            }

            resolver.TryResolve(out s_inputScheduler);
            resolver.TryResolve(out s_entities);
            resolver.TryResolve(out s_protosDb);
            if (TajsTweaksRuntimeState.StorageInspectorControls && s_entities is not null)
            {
                ApplyLoadedCapacities(s_entities);
            }

            if (!s_patchRegistered)
            {
                PatchStorageProductSupport(harmony);
                PatchStorageCapacityAssignment(harmony);
                PatchStorageConfig(harmony);
                PatchAlertOptions(harmony);
                PatchStorageInspector(harmony);
                s_patchRegistered = true;
            }
        }

        internal static void Reset()
        {
            lock (s_gate)
            {
                s_clipboardSourceId = -1;
            }

            TajsStorageAdvancedState.Clear();
            TajsStorageAdvancedState.UnbindSave();
            s_inputScheduler = null;
            s_entities = null;
            s_protosDb = null;
        }

        internal static void ApplySettings(DependencyResolver resolver)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls)
            {
                return;
            }

            resolver.TryResolve(out s_entities);
            if (s_entities is not null)
            {
                ApplyLoadedCapacities(s_entities);
            }
        }

        private static void PatchStorageProductSupport(Harmony harmony)
        {
            MethodInfo? instanceMethod = AccessTools.Method(typeof(CoiStorage), nameof(CoiStorage.IsProductSupported), new[] { typeof(ProductProto) });
            if (instanceMethod is not null)
            {
                harmony.Patch(instanceMethod, postfix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(StorageProductSupportPostfix)));
            }

            MethodInfo? replace = AccessTools.Method(typeof(CoiStorage), "TryReplaceSelf");
            if (replace is not null)
            {
                harmony.Patch(
                    replace,
                    prefix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(ReplacePrefix)),
                    finalizer: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(ReplaceFinalizer)));
            }

            MethodInfo? protoMethod = AccessTools.Method(typeof(StorageProto), nameof(StorageProto.IsProductSupported), new[] { typeof(ProductProto) });
            if (protoMethod is not null)
            {
                harmony.Patch(protoMethod, postfix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(ProtoProductSupportPostfix)));
            }
        }

        private static void PatchStorageConfig(Harmony harmony)
        {
            MethodInfo? add = AccessTools.Method(typeof(CoiStorage), "AddToConfigInternal", new[] { typeof(EntityConfigData) });
            MethodInfo? apply = AccessTools.Method(typeof(CoiStorage), "ApplyConfigInternal", new[] { typeof(EntityConfigData) });
            if (add is not null)
            {
                harmony.Patch(add, postfix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(AddConfigPostfix)));
            }

            if (apply is not null)
            {
                harmony.Patch(apply, postfix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(ApplyConfigPostfix)));
            }
        }

        private static void PatchStorageCapacityAssignment(Harmony harmony)
        {
            MethodInfo? assign = AccessTools.Method(typeof(StorageBase), "TryAssignProduct", new[] { typeof(ProductProto) });
            if (assign is not null)
            {
                harmony.Patch(assign, postfix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(ProductAssignmentPostfix)));
            }

            MethodInfo? replaceProduct = AccessTools.Method(
                typeof(StorageBase),
                "SetNewProductAndCapacityClearOld",
                new[] { typeof(ProductProto) });
            if (replaceProduct is not null)
            {
                harmony.Patch(replaceProduct, postfix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(ProductReplacedPostfix)));
            }
        }

        private static void PatchAlertOptions(Harmony harmony)
        {
            MethodInfo? method = AccessTools.Method(typeof(Dropdown<Percent>), "SetOptions", new[] { typeof(Percent[]) });
            if (method is not null)
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(AlertOptionsPrefix)));
            }
        }

        private static void PatchStorageInspector(Harmony harmony)
        {
            Type? inspectorType = typeof(PanelWithHeader).Assembly.GetType("Mafi.Unity.Ui.Inspectors.StorageInspector");
            if (inspectorType is null)
            {
                return;
            }

            s_entityProperty = inspectorType.GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Type? cursor = inspectorType;
            while (cursor is not null && s_addPanelMethod is null)
            {
                s_addPanelMethod = cursor.GetMethod(
                    "AddPanelWithHeader",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(UiComponent[]) },
                    null);
                cursor = cursor.BaseType;
            }

            if (s_entityProperty is null || s_addPanelMethod is null)
            {
                return;
            }

            foreach (ConstructorInfo constructor in inspectorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TajsStorageAdvancedFeature), nameof(InspectorConstructorPostfix)));
            }
        }

        private static void StorageProductSupportPostfix(CoiStorage __instance, ProductProto product, ref bool __result)
        {
            if (__result || !TajsTweaksRuntimeState.StorageInspectorControls || product is null || __instance is null)
            {
                return;
            }

            if (TajsStorageAdvancedState.IsAllowAll(__instance.Id.Value) && IsCompatibleProduct(__instance, product))
            {
                __result = true;
            }
        }

        private static void ReplacePrefix(CoiStorage __instance)
        {
            s_allowAllProtoCheck = TajsTweaksRuntimeState.StorageInspectorControls &&
                                   __instance is not null &&
                                   TajsStorageAdvancedState.IsAllowAll(__instance.Id.Value) &&
                                   !TajsStorageAdvancedConfiguration.IsRestricted(__instance);
        }

        private static void ReplaceFinalizer() => s_allowAllProtoCheck = false;

        private static void ProtoProductSupportPostfix(StorageProto __instance, ProductProto product, ref bool __result)
        {
            if (__result || !s_allowAllProtoCheck || product is null || __instance is null || !__instance.ProductType.HasValue)
            {
                return;
            }

            __result = __instance.ProductType.Value.Matches(product.Type) &&
                       TajsStorageAdvancedConfiguration.IsRealProduct(product);
        }

        private static void AddConfigPostfix(CoiStorage __instance, EntityConfigData data)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls || __instance is null || data is null ||
                TajsStorageAdvancedConfiguration.IsRestricted(__instance))
            {
                return;
            }

            if (TajsConfigurationPipeline.TryCapture(__instance, data))
            {
                return;
            }

            // Always emit the explicit state. PreserveUnselectedFields overlays the target
            // value when product/allowed-product transfer is not selected.
            data.SetBool(
                TajsStorageAdvancedConfiguration.AllowAllProductsKey,
                TajsStorageAdvancedState.IsAllowAll(__instance.Id.Value));

            // A zero marker is intentional: when capacity is selected for a transfer, a
            // source without an override resets the destination to its native prototype value.
            data.SetInt(
                TajsStorageAdvancedConfiguration.CapacityOverrideKey,
                TajsStorageAdvancedState.GetCapacityOverride(__instance.Id.Value) ?? 0);
        }

        private static void ApplyConfigPostfix(CoiStorage __instance, EntityConfigData data)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls || __instance is null || data is null ||
                TajsStorageAdvancedConfiguration.IsRestricted(__instance))
            {
                return;
            }

            if (TajsConfigurationPipeline.TryApply(__instance, data))
            {
                return;
            }

            bool? enabled = data.GetBool(TajsStorageAdvancedConfiguration.AllowAllProductsKey);
            if (enabled == true)
            {
                TajsStorageAdvancedState.SetAllowAll(__instance.Id.Value);
            }
            else if (enabled == false)
            {
                TajsStorageAdvancedState.ClearAllowAll(__instance.Id.Value);
            }

            int? capacity = data.GetInt(TajsStorageAdvancedConfiguration.CapacityOverrideKey);
            if (capacity.HasValue)
            {
                if (capacity.Value > 0)
                {
                    TajsStorageAdvancedConfiguration.TryApplyCapacity(__instance, capacity.Value, out _);
                }
                else if (capacity.Value == 0)
                {
                    TajsStorageAdvancedConfiguration.TryClearCapacityOverride(__instance, out _);
                }
            }
        }

        private static void ProductAssignmentPostfix(StorageBase __instance, ProductProto product, bool __result)
        {
            if (!__result || !TajsTweaksRuntimeState.StorageInspectorControls ||
                __instance is not CoiStorage storage || product is null ||
                TajsStorageAdvancedConfiguration.IsRestricted(storage))
            {
                return;
            }

            int? capacity = TajsStorageAdvancedState.GetCapacityOverride(storage.Id.Value);
            if (capacity.HasValue)
            {
                TajsStorageAdvancedConfiguration.TryApplyCapacity(storage, capacity.Value, out _);
            }
        }

        private static void ProductReplacedPostfix(StorageBase __instance, ProductProto product)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls || __instance is not CoiStorage storage ||
                product is null || TajsStorageAdvancedConfiguration.IsRestricted(storage))
            {
                return;
            }

            int? capacity = TajsStorageAdvancedState.GetCapacityOverride(storage.Id.Value);
            if (capacity.HasValue)
            {
                TajsStorageAdvancedConfiguration.TryApplyCapacity(storage, capacity.Value, out _);
            }
        }

        private static void ApplyLoadedCapacities(IEntitiesManager entities)
        {
            try
            {
                foreach (CoiStorage storage in entities.GetAllEntitiesOfType<CoiStorage>())
                {
                    if (storage is null)
                    {
                        continue;
                    }

                    int? capacity = TajsStorageAdvancedState.GetCapacityOverride(storage.Id.Value);
                    if (capacity.HasValue && !TajsStorageAdvancedConfiguration.IsRestricted(storage))
                    {
                        TajsStorageAdvancedConfiguration.TryApplyCapacity(storage, capacity.Value, out _, remember: false);
                    }
                }
            }
            catch
            {
                // Persistence is optional; one malformed entity must not affect the scene.
            }
        }

        private static void AlertOptionsPrefix(ref Percent[] options)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls || options is null || !IsStorageAlertOptionsCall())
            {
                return;
            }

            if (options.Length == 4 &&
                options[0] == Percent.Zero && options[1] == 25.Percent() &&
                options[2] == 50.Percent() && options[3] == 75.Percent())
            {
                options = s_lowAlertOptions.ToArray();
            }
            else if (options.Length == 4 &&
                     options[0] == 25.Percent() && options[1] == 50.Percent() &&
                     options[2] == 75.Percent() && options[3] == Percent.Hundred)
            {
                options = s_highAlertOptions.ToArray();
            }
        }

        private static bool IsStorageAlertOptionsCall()
        {
            try
            {
                StackTrace trace = new(1, false);
                foreach (StackFrame? frame in trace.GetFrames() ?? Array.Empty<StackFrame>())
                {
                    MethodBase? method = frame.GetMethod();
                    if (method?.Name == "CreateStorageAlertBtn" &&
                        method.DeclaringType?.FullName == "Mafi.Unity.Ui.Library.Inspectors.StorageAlertUi")
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // UI-only compatibility probe: retaining vanilla options is the safe fallback.
            }

            return false;
        }

        private static void InspectorConstructorPostfix(object __instance)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls || __instance is null || s_states.TryGetValue(__instance, out _))
            {
                return;
            }

            try
            {
                AddInspectorControls(__instance);
            }
            catch
            {
                // Inspector controls are optional and must never break the native inspector.
            }
        }

        private static void AddInspectorControls(object inspector)
        {
            if (s_addPanelMethod is null || s_entityProperty is null)
            {
                return;
            }

            var status = new UiLabel(Loc("No storage selected."));
            var allowAll = new UiToggle(standalone: true);
            ((IComponentWithLabel)allowAll).SetLabel(Loc("Allow all compatible products"));
            allowAll.Tooltip(Loc("Shows every real product of this storage's supported type. Nuclear-waste storage remains restricted."));

            var fieldChoices = new List<(UiToggle Toggle, StorageTransferFields Field)>();
            var fieldColumn = new UiColumn(2.pt());
            AddFieldChoice(fieldColumn, fieldChoices, StorageTransferFields.ProductAssignment, "Product assignment");
            AddFieldChoice(fieldColumn, fieldChoices, StorageTransferFields.LogisticsThresholds, "Logistics thresholds and priorities");
            AddFieldChoice(fieldColumn, fieldChoices, StorageTransferFields.ImportExportEnablement, "Import/export enablement");
            AddFieldChoice(fieldColumn, fieldChoices, StorageTransferFields.TruckPolicy, "Truck policy");
            AddFieldChoice(fieldColumn, fieldChoices, StorageTransferFields.Alerts, "Alert thresholds");
            AddFieldChoice(fieldColumn, fieldChoices, StorageTransferFields.KeepFullEmpty, "Keep-full/keep-empty mode");
            AddFieldChoice(fieldColumn, fieldChoices, StorageTransferFields.CapacityOverride, "Capacity override");

            UiTextField importUntil = CreatePercentField("Import until %");
            UiTextField exportFrom = CreatePercentField("Export from %");
            UiTextField transportFrom = CreatePercentField("Transport from %");
            UiTextField transportUntil = CreatePercentField("Transport until %");
            UiTextField capacity = CreateCapacityField();
            var applyThresholds = new ButtonText(UiButton.General, Loc("Apply numeric thresholds"));
            var applyCapacity = new ButtonText(UiButton.General, Loc("Apply capacity"));

            var copy = new ButtonText(UiButton.General, Loc("Copy configuration"));
            var paste = new ButtonText(UiButton.General, Loc("Paste configuration"));
            var preview = new ButtonText(UiButton.General, Loc("Preview apply to all"));
            var applyAll = new ButtonText(UiButton.Primary, Loc("Apply to all compatible"));
            var previewStatus = new UiLabel(Loc("Preview required before applying to all."));

            var body = new UiScrollColumn();
            body.MaxHeight(420.px());
            body.Gap(3.pt());
            body.AlignItemsStretch();
            body.Add(status);
            body.Add(allowAll);
            body.Add(new UiLabel(Loc("Numeric logistics thresholds (0-100)")).FontBold());
            body.Add(CreatePercentRow("Import until", importUntil));
            body.Add(CreatePercentRow("Export from", exportFrom));
            body.Add(CreatePercentRow("Transport from", transportFrom));
            body.Add(CreatePercentRow("Transport until", transportUntil));
            body.Add(applyThresholds);
            body.Add(CreatePercentRow("Capacity", capacity));
            body.Add(applyCapacity);
            body.Add(new UiLabel(Loc("Fields copied by the buttons below")).FontBold());
            body.Add(fieldColumn);
            body.Add(new Row(2.pt()) { copy, paste, preview, applyAll });
            body.Add(previewStatus);

            var panel = (PanelWithHeader)s_addPanelMethod.Invoke(inspector, new object[] { new UiComponent[] { body } })!;
            panel.Title(Loc("Advanced storage controls"));
            panel.RootElement.style.flexShrink = 1f;
            panel.RootElement.style.minHeight = 0f;

            var state = new InspectorState(
                inspector,
                panel,
                status,
                allowAll,
                fieldChoices,
                importUntil,
                exportFrom,
                transportFrom,
                transportUntil,
                capacity,
                applyThresholds,
                applyCapacity,
                copy,
                paste,
                preview,
                applyAll,
                previewStatus);
            s_states.Add(inspector, state);

            allowAll.OnValueChanged(on =>
            {
                if (TryGetStorage(inspector, out CoiStorage storage) && !TajsStorageAdvancedConfiguration.IsRestricted(storage))
                {
                    if (on)
                    {
                        TajsStorageAdvancedState.SetAllowAll(storage.Id.Value);
                    }
                    else
                    {
                        TajsStorageAdvancedState.ClearAllowAll(storage.Id.Value);
                    }
                }
            });

            foreach (Mafi.Unity.UiToolkit.Library.TextField field in new[] { importUntil, exportFrom, transportFrom, transportUntil })
            {
                field.OnValueChanged(_ => state.DirtyThresholds = true);
            }
            capacity.OnValueChanged(_ => state.DirtyCapacity = true);

            applyThresholds.OnClick((Action)(() => ApplyNumericThresholds(state)), false);
            applyCapacity.OnClick((Action)(() => ApplyCapacity(state)), false);
            copy.OnClick((Action)(() => CopyConfiguration(state)), false);
            paste.OnClick((Action)(() => PasteConfiguration(state)), false);
            preview.OnClick((Action)(() => PreviewConfiguration(state)), false);
            applyAll.OnClick((Action)(() => ApplyConfigurationToAll(state)), false);

            panel.RootElement.schedule.Execute((Action)(() => RefreshInspector(state))).Every(250L);
            WireProductPicker(inspector);
            RefreshInspector(state);
        }

        private static void AddFieldChoice(
            UiColumn parent,
            ICollection<(UiToggle Toggle, StorageTransferFields Field)> choices,
            StorageTransferFields field,
            string label)
        {
            var toggle = new UiToggle(standalone: true);
            ((IComponentWithLabel)toggle).SetLabel(Loc(label));
            toggle.Value(true);
            parent.Add(toggle);
            choices.Add((toggle, field));
        }

        private static UiTextField CreatePercentField(string placeholder)
        {
            var field = new UiTextField();
            field.Placeholder(Loc(placeholder));
            field.MaxWidth(100.px());
            return field;
        }

        private static UiTextField CreateCapacityField()
        {
            var field = new UiTextField();
            field.Placeholder(Loc("Capacity (whole units)"));
            field.MaxWidth(140.px());
            return field;
        }

        private static Row CreatePercentRow(string label, UiTextField field)
        {
            var row = new Row(2.pt());
            row.Add(new UiLabel(Loc(label)));
            row.Add(field);
            return row;
        }

        private static void RefreshInspector(InspectorState state)
        {
            if (!TryGetStorage(state.Inspector, out CoiStorage storage))
            {
                state.Panel.RootElement.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
                return;
            }

            if (TajsStorageAdvancedConfiguration.IsRestricted(storage))
            {
                state.Panel.RootElement.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
                return;
            }

            state.Panel.RootElement.style.display = new StyleEnum<DisplayStyle>(StyleKeyword.Null);
            if (state.LastEntityId != storage.Id.Value)
            {
                state.LastEntityId = storage.Id.Value;
                state.PreviewSourceId = -1;
                state.PreviewFingerprint = string.Empty;
                state.DirtyThresholds = false;
                state.DirtyCapacity = false;
            }

            state.AllowAll.Value(TajsStorageAdvancedState.IsAllowAll(storage.Id.Value));
            if (!state.DirtyThresholds)
            {
                state.ImportUntil.Text(storage.ImportUntilPercent.ToIntPercentRounded().ToString());
                state.ExportFrom.Text(storage.ExportFromPercent.ToIntPercentRounded().ToString());
                state.TransportFrom.Text(storage.TransportFromPercent.ToIntPercentRounded().ToString());
                state.TransportUntil.Text(storage.TransportUntilPercent.ToIntPercentRounded().ToString());
            }

            if (!state.DirtyCapacity)
            {
                int capacity = TajsStorageAdvancedState.GetCapacityOverride(storage.Id.Value) ??
                               (storage.Capacity.IsPositive ? storage.Capacity.Value : storage.Prototype.Capacity.Value);
                state.Capacity.Text(capacity.ToString());
            }

            state.Status.Value(Loc("Storage " + storage.Id.Value + " supports " + storage.Prototype.ProductType!.Value + "."));
            string transferReport = TajsStorageAdvancedState.LastTransferReport;
            if (!string.IsNullOrWhiteSpace(transferReport) && transferReport != state.LastShownTransferReport)
            {
                state.LastShownTransferReport = transferReport;
                state.PreviewStatus.Value(Loc(transferReport));
            }
        }

        private static void ApplyNumericThresholds(InspectorState state)
        {
            if (!TryGetStorage(state.Inspector, out CoiStorage storage) || s_inputScheduler is null)
            {
                SetStatus(state, "Storage input scheduling is unavailable.", important: true);
                return;
            }

            if (!TryParsePercent(state.ImportUntil, out Percent importUntil, out string error) ||
                !TryParsePercent(state.ExportFrom, out Percent exportFrom, out error) ||
                !TryParsePercent(state.TransportFrom, out Percent transportFrom, out error) ||
                !TryParsePercent(state.TransportUntil, out Percent transportUntil, out error))
            {
                SetStatus(state, error, important: true);
                return;
            }

            if (transportFrom > transportUntil)
            {
                SetStatus(state, "Transport from must not exceed transport until.", important: true);
                return;
            }

            s_inputScheduler.ScheduleInputCmd(
                new StorageSetSliderStepCmd(
                    storage.Id,
                    importUntil: importUntil,
                    exportFrom: exportFrom,
                    transportFrom: transportFrom,
                    transportUntil: transportUntil));
            state.DirtyThresholds = false;
            SetStatus(state, "Numeric thresholds queued through the native storage command.", important: false);
        }

        private static void ApplyCapacity(InspectorState state)
        {
            if (!TryGetStorage(state.Inspector, out CoiStorage storage) || s_inputScheduler is null)
            {
                SetStatus(state, "Storage input scheduling is unavailable.", important: true);
                return;
            }

            string text = state.Capacity.GetText()?.Trim() ?? string.Empty;
            if (!int.TryParse(text, out int capacity) || capacity <= 0)
            {
                SetStatus(state, "Enter a positive whole-number capacity.", important: true);
                return;
            }

            if (storage.CurrentQuantity.Value > capacity)
            {
                SetStatus(state, "Capacity cannot be below the storage's current quantity.", important: true);
                return;
            }

            s_inputScheduler.ScheduleInputCmd(new TajsStorageCapacityCmd(storage.Id, capacity));
            state.DirtyCapacity = false;
            SetStatus(state, "Capacity change queued; inventory quantities were not changed.", important: false);
        }

        private static bool TryParsePercent(
            Mafi.Unity.UiToolkit.Library.TextField field,
            out Percent value,
            out string error)
        {
            string text = field.GetText()?.Trim() ?? string.Empty;
            if (!int.TryParse(text, out int parsed) || parsed < 0 || parsed > 100)
            {
                value = Percent.Zero;
                error = "Enter a whole percentage from 0 to 100.";
                return false;
            }

            value = parsed.Percent();
            error = string.Empty;
            return true;
        }

        private static void CopyConfiguration(InspectorState state)
        {
            if (!TryGetStorage(state.Inspector, out CoiStorage storage) || TajsStorageAdvancedConfiguration.IsRestricted(storage))
            {
                SetStatus(state, "This storage is restricted from configuration transfer.", important: true);
                return;
            }

            lock (s_gate)
            {
                s_clipboardSourceId = storage.Id.Value;
            }

            state.PreviewSourceId = -1;
            state.PreviewFingerprint = string.Empty;
            SetStatus(state, "Storage configuration copied; inventory quantities were not copied.", important: false);
        }

        private static void PasteConfiguration(InspectorState state)
        {
            if (!TryGetStorage(state.Inspector, out CoiStorage target) || s_inputScheduler is null)
            {
                SetStatus(state, "Storage input scheduling is unavailable.", important: true);
                return;
            }

            if (state.SelectedFields() == StorageTransferFields.None)
            {
                SetStatus(state, "Select at least one configuration field to paste.", important: true);
                return;
            }

            int sourceId;
            lock (s_gate)
            {
                sourceId = s_clipboardSourceId;
            }

            if (sourceId < 0)
            {
                SetStatus(state, "Copy a storage configuration first.", important: true);
                return;
            }

            s_inputScheduler.ScheduleInputCmd(
                new TajsStorageConfigurationCmd(
                    sourceId,
                    new[] { target.Id.Value },
                    state.SelectedFields()));
            SetStatus(state, "Storage configuration paste queued.", important: false);
        }

        private static void PreviewConfiguration(InspectorState state)
        {
            if (!TryGetStorage(state.Inspector, out CoiStorage source) || s_entities is null)
            {
                SetStatus(state, "Storage entity enumeration is unavailable.", important: true);
                return;
            }

            StorageTransferFields fields = state.SelectedFields();
            if (fields == StorageTransferFields.None)
            {
                SetStatus(state, "Select at least one configuration field before preview.", important: true);
                return;
            }
            List<StorageTransferCandidate> candidates = BuildCandidates(source, s_entities.GetAllEntitiesOfType<CoiStorage>());
            int compatible = candidates.Count(x => x.Compatible);
            int skipped = candidates.Count - compatible;
            state.PreviewSourceId = source.Id.Value;
            state.PreviewFingerprint = Fingerprint(source, fields, candidates);
            state.PreviewStatus.Value(
                Loc(
                    "Preview: " + compatible + " compatible storage(s), " + skipped + " skipped. Fields: " +
                    TajsStorageAdvancedConfiguration.DescribeFields(fields) + "."));
            SetStatus(state, skipped == 0 ? "Preview ready." : "Preview ready; skipped entities will be reported.", important: skipped != 0);
        }

        private static void ApplyConfigurationToAll(InspectorState state)
        {
            if (!TryGetStorage(state.Inspector, out CoiStorage source) || s_entities is null || s_inputScheduler is null)
            {
                SetStatus(state, "Storage input scheduling is unavailable.", important: true);
                return;
            }

            IEnumerable<CoiStorage> storages = s_entities.GetAllEntitiesOfType<CoiStorage>();
            StorageTransferFields fields = state.SelectedFields();
            if (fields == StorageTransferFields.None)
            {
                SetStatus(state, "Select at least one configuration field before bulk apply.", important: true);
                return;
            }
            List<StorageTransferCandidate> candidates = BuildCandidates(source, storages);
            string fingerprint = Fingerprint(source, fields, candidates);
            if (state.PreviewSourceId != source.Id.Value || state.PreviewFingerprint != fingerprint)
            {
                PreviewConfiguration(state);
                SetStatus(state, "Review the preview, then click Apply to all compatible again.", important: true);
                return;
            }

            int[] targets = candidates.Where(x => x.Compatible).Select(x => x.EntityId).ToArray();
            s_inputScheduler.ScheduleInputCmd(new TajsStorageConfigurationCmd(source.Id.Value, targets, fields));
            SetStatus(state, "Bulk storage configuration queued; skipped reasons will be reported after execution.", important: false);
        }

        private static List<StorageTransferCandidate> BuildCandidates(CoiStorage source, IEnumerable<CoiStorage> storages)
        {
            var result = new List<StorageTransferCandidate>();
            foreach (CoiStorage storage in storages)
            {
                if (storage is null || storage.Id == source.Id)
                {
                    continue;
                }

                bool compatible = TajsStorageAdvancedConfiguration.IsCompatible(source, storage, out string reason);
                result.Add(new StorageTransferCandidate(storage.Id.Value, compatible, reason));
            }

            return result;
        }

        private static string Fingerprint(
            CoiStorage source,
            StorageTransferFields fields,
            IEnumerable<StorageTransferCandidate> candidates)
        {
            var sourceValues = new List<string>();
            if ((fields & StorageTransferFields.ProductAssignment) != 0)
            {
                sourceValues.Add(source.StoredProduct.HasValue ? source.StoredProduct.Value.Id.Value : "-");
                sourceValues.Add(TajsStorageAdvancedState.IsAllowAll(source.Id.Value) ? "1" : "0");
            }

            if ((fields & StorageTransferFields.LogisticsThresholds) != 0)
            {
                sourceValues.Add(source.ImportUntilPercent.RawValue.ToString());
                sourceValues.Add(source.ExportFromPercent.RawValue.ToString());
                sourceValues.Add(source.TransportFromPercent.RawValue.ToString());
                sourceValues.Add(source.TransportUntilPercent.RawValue.ToString());
                sourceValues.Add(source.ImportPriority.ToString());
                sourceValues.Add(source.ExportPriority.ToString());
                sourceValues.Add(source.GeneralPriority.ToString());
            }

            if ((fields & StorageTransferFields.ImportExportEnablement) != 0)
            {
                sourceValues.Add(source.IsLogisticsInputDisabled ? "1" : "0");
                sourceValues.Add(source.IsLogisticsOutputDisabled ? "1" : "0");
                sourceValues.Add(source.AllowNonAssignedOutput ? "1" : "0");
            }

            if ((fields & StorageTransferFields.TruckPolicy) != 0)
            {
                sourceValues.Add(TajsStorageAdvancedConfiguration.IsEnforcingCustomVehicles(source) ? "1" : "0");
                sourceValues.Add(string.Join(",", source.AllowedTruckGroups.Select(group => group.Id.Value).OrderBy(id => id)));
                sourceValues.Add(string.Join(",", source.AllVehicles.Select(vehicle => vehicle.Prototype.Id.Value).OrderBy(id => id)));
            }

            if ((fields & StorageTransferFields.Alerts) != 0)
            {
                sourceValues.Add(source.AlertWhenAboveEnabled ? "1" : "0");
                sourceValues.Add(source.AlertWhenAbove.RawValue.ToString());
                sourceValues.Add(source.AlertWhenBelowEnabled ? "1" : "0");
                sourceValues.Add(source.AlertWhenBelow.RawValue.ToString());
            }

            if ((fields & StorageTransferFields.KeepFullEmpty) != 0)
            {
                sourceValues.Add(source.CheatMode.ToString());
            }

            if ((fields & StorageTransferFields.CapacityOverride) != 0)
            {
                sourceValues.Add((TajsStorageAdvancedState.GetCapacityOverride(source.Id.Value) ?? 0).ToString());
            }

            return source.Id.Value + ":" + (int)fields + ":" + string.Join("|", sourceValues) + ":" +
                   string.Join(",", candidates.Select(x => x.EntityId + ":" + x.Compatible));
        }

        private static void SetStatus(InspectorState state, string text, bool important) => state.Status.Value(Loc(text));

        private static bool TryGetStorage(object inspector, out CoiStorage storage)
        {
            storage = s_entityProperty?.GetValue(inspector) as CoiStorage ?? null!;
            return storage is not null && !storage.IsDestroyed;
        }

        private static bool IsCompatibleProduct(CoiStorage storage, ProductProto product) =>
            !TajsStorageAdvancedConfiguration.IsRestricted(storage) &&
            storage.Prototype.ProductType!.Value.Matches(product.Type) &&
            TajsStorageAdvancedConfiguration.IsRealProduct(product);

        private static void WireProductPicker(object inspector)
        {
            try
            {
                if (inspector is not UiComponent root)
                {
                    return;
                }

                var queue = new Queue<UiComponent>();
                queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    UiComponent component = queue.Dequeue();
                    if (component is SingleProductPickerUi picker)
                    {
                        s_optionsProviderField ??= picker.ProtoPickerPopup.GetType().GetField(
                            "m_optionsProvider",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        if (s_optionsProviderField?.GetValue(picker.ProtoPickerPopup) is Func<IEnumerable<ProductProto>> original)
                        {
                            s_optionsProviderField.SetValue(
                                picker.ProtoPickerPopup,
                                new Func<IEnumerable<ProductProto>>(() =>
                                {
                                    if (TajsTweaksRuntimeState.StorageInspectorControls &&
                                        TryGetStorage(inspector, out CoiStorage storage) &&
                                        TajsStorageAdvancedState.IsAllowAll(storage.Id.Value) &&
                                        s_protosDb is not null)
                                    {
                                        return TajsStorageAdvancedConfiguration.CompatibleProducts(storage, s_protosDb);
                                    }

                                    return original();
                                }));
                        }

                        return;
                    }

                    foreach (UiComponent child in component.AllChildren)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
            catch
            {
                // A changed picker tree leaves the native picker/provider intact.
            }
        }

        private static LocStrFormatted Loc(string text) =>
            LocalizationManager.CreateAlreadyLocalizedStr("TAJS_STORAGE_" + text.GetHashCode().ToString("X"), text).AsFormatted;
    }
}
