// Taj's COI Mods | TajsOverclockingFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Offices;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Waste;
using Mafi.Core.Entities;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Input;
using Mafi.Core.Population;
using Mafi.Core.Products;
using Mafi.Core.SaveGame;
using Mafi.Unity.Entities;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Metadata;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using UnityEngine;
using EntityId = Mafi.Core.EntityId;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    ///     Owns the gameplay parts of issues #65 and #146. Machine speed is changed through the
    ///     native private base-speed seam, while transport speed is represented by save-scoped policy
    ///     metadata and bounded extra movement calls. No policy metadata participates in vanilla
    ///     object deserialization.
    /// </summary>
    internal sealed class TajsOverclockingFeature
    {
        internal const string HarmonyId = "TajsCOI.Tweaks.Overclocking";
        private const int DefaultPercent = 100;
        private const int MaxTransportExtraMovesPerTick = 8;
        private const float MaxTransportPendingMoves = MaxTransportExtraMovesPerTick;
        private const int MaxLegacyImportRecords = 100000;

        private sealed class AutoPlan
        {
            internal int Id;
            internal Machine Machine = null!;
            internal int Current;
            internal int Target;
            internal int Minimum;
            internal bool HasDemandSignal;
        }

        private sealed class Listing
        {
            internal IEntity Entity = null!;
            internal string Type = string.Empty;
            internal int Rate;
            internal OverclockEffectivePolicy Policy;
        }

        private static TajsOverclockingFeature? s_current;

        private readonly DependencyResolver m_resolver;
        private readonly ITajsSettings m_settings;
        private readonly IEntityMetadataLookup? m_metadata;
        private readonly ITajsLogger m_log;
        private readonly OverclockingStateStore m_store = new();
        private readonly IEntitiesManager m_entities;
        private readonly IInputScheduler? m_inputScheduler;
        private readonly IProductsManager? m_products;
        private readonly IElectricityManager? m_electricity;
        private readonly IWorkersManager? m_workers;
        private readonly EntitiesRenderingManager? m_rendering;
        private readonly FieldInfo m_machineSpeedBase;
        private readonly MethodInfo m_machineUpdateSpeed;
        private readonly MethodInfo? m_transportTryMoveProducts;
        private readonly PropertyInfo? m_oreSorterSortedPerDuration;
        private readonly MethodInfo? m_oreSorterUpdateCapacity;
        private readonly FieldInfo? m_officeRecipeTimer;
        private readonly MethodInfo? m_officeTimerDecrement;
        private readonly FieldInfo? m_wasteRecipeTimer;
        private readonly MethodInfo? m_wasteTimerDecrement;
        private float m_nextAutoTime;
        private int m_maintenanceTick;
        private readonly Dictionary<int, float> m_extraCycleAccumulators = new();
        private readonly Dictionary<int, float> m_transportExtraMoveAccumulators = new();
        private readonly HashSet<int> m_transportSpeedDisabled = new();
        private readonly Dictionary<int, ulong> m_highlights = new();
        private readonly OverclockingSelectionTool m_selection;
        private bool m_installed;

        internal static TajsOverclockingFeature? Current => s_current;
        internal bool IsInstalled => m_installed;
        internal IReadOnlyList<OverclockGroup> Groups => m_store.Groups;
        internal string SelectionStatus { get; set; } = string.Empty;

        internal TajsOverclockingFeature(DependencyResolver resolver, ITajsSettings settings, ITajsRuntime runtime)
        {
            m_resolver = resolver;
            m_settings = settings;
            m_log = runtime.GetLogger(TajsTweaksSettingsCatalog.ModId, "Overclocking");
            if (!resolver.TryResolve(out m_entities!))
            {
                throw new InvalidOperationException("The current scene has no entity manager.");
            }

            resolver.TryResolve(out m_inputScheduler);
            resolver.TryResolve(out m_products);
            resolver.TryResolve(out m_electricity);
            resolver.TryResolve(out m_workers);
            resolver.TryResolve(out m_rendering);
            resolver.TryResolve(out m_metadata);
            m_selection = new OverclockingSelectionTool(m_entities, this);

            BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            m_machineSpeedBase = typeof(Machine).GetField("m_speedFactorBase", privateInstance)
                                 ?? throw new MissingFieldException(typeof(Machine).FullName, "m_speedFactorBase");
            m_machineUpdateSpeed = typeof(Machine).GetMethod("updateSpeedFactor", privateInstance)
                                   ?? throw new MissingMethodException(typeof(Machine).FullName, "updateSpeedFactor");
            m_transportTryMoveProducts = typeof(Transport).GetMethod("tryMoveProducts", privateInstance);

            m_oreSorterUpdateCapacity = FindMethod("Mafi.Core.Buildings.OreSorting.OreSortingPlant", "updateCapacity", privateInstance);
            m_oreSorterSortedPerDuration = FindType("Mafi.Core.Buildings.OreSorting.OreSortingPlant")?.GetProperty(
                "SortedPerDuration",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            m_officeRecipeTimer = FindField("Mafi.Core.Buildings.Offices.OfficeBuilding", "m_recipeStepsLeft", privateInstance);
            m_officeTimerDecrement = FindTimerMethod(m_officeRecipeTimer, "DecrementOnly");
            m_wasteRecipeTimer = FindField("Mafi.Core.Buildings.Waste.WasteSortingPlant", "m_recyclingTimer", privateInstance);
            m_wasteTimerDecrement = FindTimerMethod(m_wasteRecipeTimer, "DecrementOnly");
        }

        internal void Install(Harmony harmony)
        {
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                // Keep the compatibility seam installable even when the feature is disabled;
                // settings can then be changed live without recreating the scene.
            }

            m_store.LoadForSave(GetSaveName());
            OverclockingPatches.Install(harmony, this);
            TransportOverclockingPatches.Install(harmony);
            ImportLegacyBoostsIfNeeded();
            ImportLegacyTransportBoostsIfNeeded();
            if (TajsTweaksRuntimeState.Overclocking)
            {
                ReconcileLoadedMachines();
            }
            else
            {
                ResetLoadedRates();
            }
            foreach (Transport transport in m_entities.GetAllEntitiesOfType<Transport>())
            {
                TransportOverclockingPatches.RegisterTransport(transport);
            }
            try
            {
                // The gameplay patches remain useful when the optional inspector UI moves in a
                // future COI build. Do not disable the whole feature because that seam changed.
                OverclockingInspectorPatch.Install(harmony);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Overclocking inspector integration unavailable; gameplay controls remain active.");
            }
            m_installed = true;
            s_current = this;
        }

        internal void Tick()
        {
            if (!m_installed || !TajsTweaksRuntimeState.Overclocking)
            {
                return;
            }

            if (++m_maintenanceTick >= 120)
            {
                m_maintenanceTick = 0;
                m_store.Prune(IsKnownSupportedEntity);
            }

            if (Time.realtimeSinceStartup < m_nextAutoTime)
            {
                return;
            }

            m_nextAutoTime = Time.realtimeSinceStartup + Math.Max(1, TajsTweaksRuntimeState.OverclockAutoIntervalSeconds);
            RunAutoCycle();
        }

        internal void Dispose()
        {
            if (ReferenceEquals(s_current, this))
            {
                s_current = null;
            }

            m_store.Save();
            m_transportExtraMoveAccumulators.Clear();
            m_transportSpeedDisabled.Clear();
            ClearHighlights();
            m_selection.Deactivate();
            m_installed = false;
        }

        internal void RefreshSettings()
        {
            if (!m_installed)
            {
                return;
            }

            // Re-reading the native base after a live setting change also restores policies
            // when the feature is toggled back on. Turning the feature off leaves the policy
            // file intact but returns all supported entities to the vanilla rate immediately.
            if (TajsTweaksRuntimeState.Overclocking)
            {
                ReconcileLoadedMachines();
                ReconcileKnownNonMachineEntities();
            }
            else
            {
                ResetLoadedRates();
            }
        }

        internal int GetPercent(EntityId id)
        {
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                return DefaultPercent;
            }

            if (!TryGetSupportedEntity(id, out object? entity))
            {
                return DefaultPercent;
            }

            if (entity is Machine machine)
            {
                try
                {
                    return ((Percent)m_machineSpeedBase.GetValue(machine)!).ToIntPercentRounded();
                }
                catch
                {
                    return DefaultPercent;
                }
            }

            OverclockEffectivePolicy effective = GetEffectivePolicy(id.Value);
            return effective.Auto ? DefaultPercent : effective.ManualPercent;
        }

        internal bool IsAuto(EntityId id) => TajsTweaksRuntimeState.Overclocking && GetEffectivePolicy(id.Value).Auto;

        internal OverclockEffectivePolicy GetEffectivePolicy(int entityId) => GetEffectivePolicy(entityId, ignoreEntityOverride: false);

        internal OverclockEffectivePolicy GetPolicyAfterEntityReset(int entityId) =>
            GetEffectivePolicy(entityId, ignoreEntityOverride: true);

        private OverclockEffectivePolicy GetEffectivePolicy(int entityId, bool ignoreEntityOverride)
        {
            OverclockGroup? group = m_store.GetGroupForEntity(entityId);
            OverclockEntityPolicy? entity = null;
            if (!ignoreEntityOverride)
            {
                m_store.TryGetEntity(entityId, out entity);
            }
            int min = entity is not null && entity.HasBoundsOverride
                ? entity.MinPercent
                : group is not null
                    ? group.MinPercent
                    : TajsTweaksRuntimeState.OverclockMinPercent;
            int max = entity is not null && entity.HasBoundsOverride
                ? entity.MaxPercent
                : group is not null
                    ? group.MaxPercent
                    : TajsTweaksRuntimeState.OverclockMaxPercent;
            OverclockBounds normalizedBounds = OverclockBounds.Normalize(min, max);
            min = normalizedBounds.MinPercent;
            max = normalizedBounds.MaxPercent;
            // COI's non-machine production timers only have a safe accelerated path. Keep
            // ore sorting, offices, and waste sorting at vanilla speed when underclocking is
            // requested; Machines alone support the 10%..100% range.
            if (TryGetSupportedEntity(new EntityId(entityId), out object? supported) && supported is not Machine)
            {
                min = Math.Max(100, min);
                max = Math.Max(min, max);
            }
            int manual = entity is not null && entity.HasManualOverride
                ? entity.ManualPercent
                : group is not null && group.ManualDefault > 0
                    ? group.ManualDefault
                    : DefaultPercent;
            bool auto = entity is not null && entity.HasAutoOverride ? entity.Auto : group?.Auto == true;
            return new OverclockEffectivePolicy(
                OverclockingMath.ClampPercent(manual, min, max),
                auto,
                min,
                max,
                group?.Id ?? -1);
        }

        internal bool QueueSetManual(EntityId id, int percent, out string message)
        {
            message = string.Empty;
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                message = "Per-machine overclocking is disabled.";
                return false;
            }

            if (!TryGetSupportedEntity(id, out _))
            {
                message = "Entity '" + id.Value + "' is not a supported overclocking entity.";
                return false;
            }

            OverclockEffectivePolicy effective = GetEffectivePolicy(id.Value);
            int clamped = OverclockingMath.ClampPercent(percent, effective.MinPercent, effective.MaxPercent);
            if (m_inputScheduler is null)
            {
                message = "The normal input scheduler is unavailable.";
                return false;
            }

            m_inputScheduler.ScheduleInputCmd(TajsOverclockPolicyCmd.SetManual(id, clamped));
            message = "Overclock command queued for entity " + id.Value + " at " + clamped + "%.";
            return true;
        }

        internal bool QueueSetAuto(EntityId id, bool enabled, int? minimum, int? maximum, out string message)
        {
            message = string.Empty;
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                message = "Per-machine overclocking is disabled.";
                return false;
            }

            if (!TryGetSupportedEntity(id, out _))
            {
                message = "Entity '" + id.Value + "' is not a supported overclocking entity.";
                return false;
            }

            if (m_inputScheduler is null)
            {
                message = "The normal input scheduler is unavailable.";
                return false;
            }

            m_inputScheduler.ScheduleInputCmd(TajsOverclockPolicyCmd.SetAuto(id, enabled, minimum, maximum));
            message = "Overclock Auto command queued for entity " + id.Value + ".";
            return true;
        }

        internal bool QueueReset(EntityId id, out string message)
        {
            message = string.Empty;
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                message = "Per-machine overclocking is disabled.";
                return false;
            }

            if (!TryGetSupportedEntity(id, out _))
            {
                message = "Entity '" + id.Value + "' is not a supported overclocking entity.";
                return false;
            }

            if (m_inputScheduler is null)
            {
                message = "The normal input scheduler is unavailable.";
                return false;
            }

            m_inputScheduler.ScheduleInputCmd(TajsOverclockPolicyCmd.Reset(id));
            message = "Overclock reset command queued for entity " + id.Value + ".";
            return true;
        }

        private bool TryQueueGroupCommand(OverclockGroup? group, out string message)
        {
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                message = "Per-machine overclocking is disabled.";
                return false;
            }

            if (group is null || group.Locked)
            {
                message = "Group is missing or locked.";
                return false;
            }

            if (m_inputScheduler is null)
            {
                message = "The normal input scheduler is unavailable.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        internal bool QueueDeleteGroup(int groupId, out string message)
        {
            if (!TryQueueGroupCommand(m_store.GetGroup(groupId), out message))
            {
                return false;
            }

            m_inputScheduler!.ScheduleInputCmd(TajsOverclockPolicyCmd.DeleteGroup(groupId));
            message = "Overclock group delete command queued for group " + groupId + ".";
            return true;
        }

        internal bool QueueAddToGroup(int groupId, EntityId id, out string message)
        {
            if (!TryQueueGroupCommand(m_store.GetGroup(groupId), out message))
            {
                return false;
            }

            if (!TryGetSupportedEntity(id, out _))
            {
                message = "Entity '" + id.Value + "' is not a supported overclocking entity.";
                return false;
            }

            if (m_store.GetGroup(groupId)!.Members.Contains(id.Value))
            {
                message = "Entity " + id.Value + " is already a member of group " + groupId + ".";
                return false;
            }

            m_inputScheduler!.ScheduleInputCmd(TajsOverclockPolicyCmd.AddToGroup(groupId, id));
            message = "Overclock group add command queued for entity " + id.Value + ".";
            return true;
        }

        internal bool QueueRemoveFromGroup(int groupId, EntityId id, out string message)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (!TryQueueGroupCommand(group, out message))
            {
                return false;
            }

            if (!group!.Members.Contains(id.Value))
            {
                message = "Entity " + id.Value + " is not a member of group " + groupId + ".";
                return false;
            }

            m_inputScheduler!.ScheduleInputCmd(TajsOverclockPolicyCmd.RemoveFromGroup(groupId, id));
            message = "Overclock group remove command queued for entity " + id.Value + ".";
            return true;
        }

        internal bool QueueSetGroupDefault(int groupId, int percent, out string message)
        {
            if (!TryQueueGroupCommand(m_store.GetGroup(groupId), out message))
            {
                return false;
            }

            int clamped = OverclockingMath.ClampPercent(
                percent,
                TajsTweaksRuntimeState.OverclockMinPercent,
                TajsTweaksRuntimeState.OverclockMaxPercent);
            m_inputScheduler!.ScheduleInputCmd(TajsOverclockPolicyCmd.SetGroupDefault(groupId, clamped));
            message = "Overclock group default command queued for group " + groupId + " at " + clamped + "%.";
            return true;
        }

        internal bool QueueApplyGroupToMembers(int groupId, int percent, out string message)
        {
            if (!TryQueueGroupCommand(m_store.GetGroup(groupId), out message))
            {
                return false;
            }

            int clamped = OverclockingMath.ClampPercent(
                percent,
                TajsTweaksRuntimeState.OverclockMinPercent,
                TajsTweaksRuntimeState.OverclockMaxPercent);
            m_inputScheduler!.ScheduleInputCmd(TajsOverclockPolicyCmd.ApplyGroup(groupId, clamped));
            message = "Overclock group apply command queued for group " + groupId + " at " + clamped + "%.";
            return true;
        }

        internal bool QueueSetGroupAuto(int groupId, bool enabled, int? minimum, int? maximum, out string message)
        {
            if (!TryQueueGroupCommand(m_store.GetGroup(groupId), out message))
            {
                return false;
            }

            m_inputScheduler!.ScheduleInputCmd(TajsOverclockPolicyCmd.SetGroupAuto(groupId, enabled, minimum, maximum));
            message = "Overclock group Auto command queued for group " + groupId + ".";
            return true;
        }

        // Execution-only methods are called by input-command processors (and by the native
        // config restore seam). Player-facing callers must use the Queue* methods above so
        // mutations retain deterministic input ordering.
        internal bool ExecuteSetManual(EntityId id, int percent, out string message)
        {
            message = string.Empty;
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                message = "Per-machine overclocking is disabled.";
                return false;
            }

            if (!TryGetSupportedEntity(id, out object? entity))
            {
                message = "Entity '" + id.Value + "' is not a supported overclocking entity.";
                return false;
            }

            OverclockEffectivePolicy effective = GetEffectivePolicy(id.Value);
            int clamped = OverclockingMath.ClampPercent(percent, effective.MinPercent, effective.MaxPercent);
            if (!ApplyRate(entity!, clamped))
            {
                message = "The supported speed seam was unavailable for entity " + id.Value + ".";
                return false;
            }

            OverclockEntityPolicy policy = m_store.GetOrCreateEntity(id.Value);
            policy.HasManualOverride = true;
            policy.ManualPercent = clamped;
            policy.HasAutoOverride = true;
            policy.Auto = false;
            m_store.Save();
            message = "Entity " + id.Value + " now runs at " + clamped + "%.";
            return true;
        }

        internal bool ExecuteSetAuto(EntityId id, bool enabled, int? minimum, int? maximum, out string message)
        {
            message = string.Empty;
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                message = "Per-machine overclocking is disabled.";
                return false;
            }

            if (!TryGetSupportedEntity(id, out object? entity))
            {
                message = "Entity '" + id.Value + "' is not a supported overclocking entity.";
                return false;
            }

            bool hadPreviousPolicy = m_store.TryGetEntity(id.Value, out OverclockEntityPolicy? previousPolicy) && previousPolicy is not null;
            OverclockEntityPolicy? previousSnapshot = hadPreviousPolicy ? ClonePolicy(previousPolicy!) : null;
            OverclockEntityPolicy policy = m_store.GetOrCreateEntity(id.Value);
            policy.HasAutoOverride = true;
            policy.Auto = enabled;
            if (minimum.HasValue || maximum.HasValue)
            {
                OverclockEffectivePolicy current = GetEffectivePolicy(id.Value);
                policy.HasBoundsOverride = true;
                OverclockBounds bounds = OverclockBounds.Normalize(
                    minimum ?? current.MinPercent,
                    maximum ?? current.MaxPercent);
                policy.MinPercent = bounds.MinPercent;
                policy.MaxPercent = bounds.MaxPercent;
            }

            OverclockEffectivePolicy effective = GetEffectivePolicy(id.Value);
            int targetPercent = enabled || effective.Auto ? DefaultPercent : effective.ManualPercent;
            if (!ApplyRate(entity!, targetPercent))
            {
                RestoreEntityPolicy(id.Value, hadPreviousPolicy, previousSnapshot);
                message = "The supported speed seam was unavailable for entity " + id.Value + ".";
                return false;
            }

            m_store.Save();
            message = "Auto mode " + (enabled ? "enabled" : "disabled") + " for entity " + id.Value + ".";
            return true;
        }

        internal bool ExecuteResetPolicy(EntityId id, out string message)
        {
            message = string.Empty;
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                message = "Per-machine overclocking is disabled.";
                return false;
            }

            if (!TryGetSupportedEntity(id, out object? entity))
            {
                message = "Entity '" + id.Value + "' is not a supported overclocking entity.";
                return false;
            }

            bool hadPreviousPolicy = m_store.TryGetEntity(id.Value, out OverclockEntityPolicy? previousPolicy) && previousPolicy is not null;
            OverclockEntityPolicy? previousSnapshot = hadPreviousPolicy ? ClonePolicy(previousPolicy!) : null;
            m_store.RemoveEntity(id.Value);
            OverclockEffectivePolicy effective = GetEffectivePolicy(id.Value);
            if (!ApplyRate(entity!, effective.Auto ? DefaultPercent : effective.ManualPercent))
            {
                RestoreEntityPolicy(id.Value, hadPreviousPolicy, previousSnapshot);
                message = "The supported speed seam was unavailable for entity " + id.Value + ".";
                return false;
            }

            m_store.Save();
            message = "Entity " + id.Value + " returned to its group/global policy.";
            return true;
        }

        private static OverclockEntityPolicy ClonePolicy(OverclockEntityPolicy source) => new()
        {
            HasManualOverride = source.HasManualOverride,
            ManualPercent = source.ManualPercent,
            HasAutoOverride = source.HasAutoOverride,
            Auto = source.Auto,
            HasBoundsOverride = source.HasBoundsOverride,
            MinPercent = source.MinPercent,
            MaxPercent = source.MaxPercent,
        };

        private void RestoreEntityPolicy(int entityId, bool hadPreviousPolicy, OverclockEntityPolicy? previousSnapshot)
        {
            if (!hadPreviousPolicy || previousSnapshot is null)
            {
                m_store.RemoveEntity(entityId);
                return;
            }

            OverclockEntityPolicy restored = m_store.GetOrCreateEntity(entityId);
            restored.HasManualOverride = previousSnapshot.HasManualOverride;
            restored.ManualPercent = previousSnapshot.ManualPercent;
            restored.HasAutoOverride = previousSnapshot.HasAutoOverride;
            restored.Auto = previousSnapshot.Auto;
            restored.HasBoundsOverride = previousSnapshot.HasBoundsOverride;
            restored.MinPercent = previousSnapshot.MinPercent;
            restored.MaxPercent = previousSnapshot.MaxPercent;
        }

        internal OverclockGroup CreateGroup(string? name) => m_store.CreateGroup(name);

        internal OverclockGroup? GetGroup(int groupId) => m_store.GetGroup(groupId);

        internal bool TryGetEntityPolicy(EntityId entityId, out OverclockEntityPolicy? policy) =>
            m_store.TryGetEntity(entityId.Value, out policy);

        internal bool CanControl(EntityId entityId) =>
            TajsTweaksRuntimeState.Overclocking && TryGetSupportedEntity(entityId, out _);

        internal bool ExecuteDeleteGroup(int groupId)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (group is null || group.Locked)
            {
                return false;
            }

            int[] members = group.Members.ToArray();
            if (!m_store.DeleteGroup(groupId))
            {
                return false;
            }

            foreach (int id in members)
            {
                if (TryGetSupportedEntity(new EntityId(id), out object? entity))
                {
                    OverclockEffectivePolicy policy = GetEffectivePolicy(id);
                    ApplyRate(entity!, policy.Auto ? DefaultPercent : policy.ManualPercent);
                }
            }

            return true;
        }

        internal bool RenameGroup(int groupId, string name) => m_store.RenameGroup(groupId, name);

        internal bool SetGroupLocked(int groupId, bool locked)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (group is null)
            {
                return false;
            }

            group.Locked = locked;
            m_store.Save();
            return true;
        }

        internal bool SetGroupColor(int groupId, int colorIndex)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (group is null)
            {
                return false;
            }

            group.ColorIndex = Math.Max(0, Math.Min(8, colorIndex));
            m_store.Save();
            if (m_store.SelectedGroupId == groupId)
            {
                ShowGroup(groupId);
            }
            return true;
        }

        internal bool ShowGroup(int groupId)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (group is null || m_rendering is null)
            {
                return false;
            }

            ClearHighlights();
            var colors = new[]
            {
                new ColorRgba(153, 153, 153, 96),
                new ColorRgba(255, 221, 40, 96),
                new ColorRgba(255, 140, 30, 96),
                new ColorRgba(235, 60, 60, 96),
                new ColorRgba(255, 120, 190, 96),
                new ColorRgba(170, 90, 230, 96),
                new ColorRgba(70, 195, 80, 96),
                new ColorRgba(170, 230, 60, 96),
                new ColorRgba(245, 245, 245, 96),
            };
            ColorRgba color = colors[Math.Max(0, Math.Min(colors.Length - 1, group.ColorIndex))].SetA((byte)(group.HighlightAlpha * 255 / 100));
            foreach (int id in group.Members)
            {
                if (!TryGetSupportedEntity(new EntityId(id), out object? entity) || entity is not IRenderedEntity rendered)
                {
                    continue;
                }

                try
                {
                    ulong highlight = m_rendering.AddHighlight(rendered, color);
                    if (highlight != 0)
                    {
                        m_highlights[id] = highlight;
                    }
                }
                catch
                {
                }
            }

            return true;
        }

        internal string StartGroupSelection(int groupId) => m_selection.Activate(groupId);

        internal void UpdateSelectionInput() => m_selection.UpdateInput();

        internal void CancelSelection() => m_selection.Deactivate();

        internal bool IsSelectionActive => m_selection.IsActive;

        internal void ClearHighlights()
        {
            if (m_rendering is null)
            {
                m_highlights.Clear();
                return;
            }

            foreach (ulong highlight in m_highlights.Values)
            {
                try
                {
                    m_rendering.RemoveHighlight(highlight);
                }
                catch
                {
                }
            }

            m_highlights.Clear();
        }

        internal bool ExecuteAddToGroup(int groupId, EntityId id)
        {
            if (!TryGetSupportedEntity(id, out object? entity) || !m_store.AddMember(groupId, id.Value))
            {
                return false;
            }

            OverclockEffectivePolicy policy = GetEffectivePolicy(id.Value);
            ApplyRate(entity!, policy.Auto ? DefaultPercent : policy.ManualPercent);
            return true;
        }

        internal bool ExecuteRemoveFromGroup(int groupId, EntityId id)
        {
            if (!m_store.RemoveMember(groupId, id.Value))
            {
                return false;
            }

            if (TryGetSupportedEntity(id, out object? entity))
            {
                OverclockEffectivePolicy policy = GetEffectivePolicy(id.Value);
                ApplyRate(entity!, policy.Auto ? DefaultPercent : policy.ManualPercent);
            }

            return true;
        }

        internal bool ExecuteGroupDefault(int groupId, int percent, out string message)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (group is null || group.Locked)
            {
                message = "Group is missing or locked.";
                return false;
            }

            int clamped = OverclockingMath.ClampPercent(
                percent,
                TajsTweaksRuntimeState.OverclockMinPercent,
                TajsTweaksRuntimeState.OverclockMaxPercent);
            group.ManualDefault = clamped;
            group.Auto = false;
            ApplyGroupMembers(
                group,
                entity =>
                {
                    int entityId = ((IEntity)entity).Id.Value;
                    if (!m_store.TryGetEntity(entityId, out OverclockEntityPolicy? p) || !p!.HasManualOverride)
                    {
                        ApplyRate(entity, clamped);
                    }
                });
            m_store.Save();
            message = "Group " + groupId + " default set to " + clamped + "%.";
            return true;
        }

        internal bool ExecuteApplyGroupToMembers(int groupId, int percent, out string message)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (group is null || group.Locked)
            {
                message = "Group is missing or locked.";
                return false;
            }

            int clamped = OverclockingMath.ClampPercent(
                percent,
                TajsTweaksRuntimeState.OverclockMinPercent,
                TajsTweaksRuntimeState.OverclockMaxPercent);
            group.ManualDefault = clamped;
            group.Auto = false;
            foreach (int id in group.Members.ToArray())
            {
                if (!TryGetSupportedEntity(new EntityId(id), out object? entity))
                {
                    continue;
                }

                ApplyRate(entity!, clamped);
                OverclockEntityPolicy policy = m_store.GetOrCreateEntity(id);
                policy.HasManualOverride = true;
                policy.ManualPercent = clamped;
                policy.HasAutoOverride = true;
                policy.Auto = false;
            }

            m_store.Save();
            message = "Applied " + clamped + "% to supported members of group " + groupId + ".";
            return true;
        }

        internal bool ExecuteGroupAuto(int groupId, bool enabled, int? minimum, int? maximum, out string message)
        {
            OverclockGroup? group = m_store.GetGroup(groupId);
            if (group is null || group.Locked)
            {
                message = "Group is missing or locked.";
                return false;
            }

            group.Auto = enabled;
            if (minimum.HasValue || maximum.HasValue)
            {
                OverclockBounds bounds = OverclockBounds.Normalize(
                    minimum ?? group.MinPercent,
                    maximum ?? group.MaxPercent);
                group.MinPercent = bounds.MinPercent;
                group.MaxPercent = bounds.MaxPercent;
            }

            foreach (int id in group.Members.ToArray())
            {
                if (m_store.TryGetEntity(id, out OverclockEntityPolicy? policy) && policy!.HasAutoOverride)
                {
                    continue;
                }

                if (TryGetSupportedEntity(new EntityId(id), out object? entity))
                {
                    ApplyRate(entity!, enabled ? DefaultPercent : GetEffectivePolicy(id).ManualPercent);
                }
            }

            m_store.Save();
            message = "Auto mode " + (enabled ? "enabled" : "disabled") + " for group " + groupId + ".";
            return true;
        }

        internal string Status(EntityId id)
        {
            if (!TryGetSupportedEntity(id, out _))
            {
                return "Entity " + id.Value + ": unsupported or not found.";
            }

            OverclockEffectivePolicy policy = GetEffectivePolicy(id.Value);
            string display = GetMetadataDisplay(id, out string note);
            return "Entity " + id.Value + display + ": rate=" + GetPercent(id) + "%, auto=" + policy.Auto +
                   ", bounds=" + policy.MinPercent + "-" + policy.MaxPercent + "%, group=" + policy.GroupId + note + ".";
        }

        internal string ListEntities(string? typeFilter, string? stateFilter, int? groupId, string? sort)
        {
            string type = (typeFilter ?? "all").Trim().ToLowerInvariant();
            string state = (stateFilter ?? "all").Trim().ToLowerInvariant();
            string ordering = (sort ?? "id").Trim().ToLowerInvariant();
            if (groupId.HasValue && m_store.GetGroup(groupId.Value) is null)
            {
                return "Group " + groupId.Value + " does not exist.";
            }

            var listings = new List<Listing>();
            foreach (IEntity entity in EnumerateSupportedEntities())
            {
                string entityType = GetEntityType(entity);
                if (type != "all" && type != entityType)
                {
                    continue;
                }

                OverclockEffectivePolicy policy = GetEffectivePolicy(entity.Id.Value);
                int rate = GetPercent(entity.Id);
                bool inGroup = policy.GroupId >= 0;
                bool stateMatches = state == "all" ||
                                    state == "auto" && policy.Auto ||
                                    state == "manual" && !policy.Auto ||
                                    state == "boosted" && rate != DefaultPercent ||
                                    state == "default" && rate == DefaultPercent ||
                                    state == "group" && inGroup;
                if (!stateMatches || groupId.HasValue && policy.GroupId != groupId.Value)
                {
                    continue;
                }

                listings.Add(new Listing { Entity = entity, Type = entityType, Rate = rate, Policy = policy });
            }

            IEnumerable<Listing> ordered = ordering switch
            {
                "rate" => listings.OrderByDescending(item => item.Rate).ThenBy(item => item.Entity.Id.Value),
                "type" => listings.OrderBy(item => item.Type).ThenBy(item => item.Entity.Id.Value),
                "state" => listings.OrderByDescending(item => item.Policy.Auto).ThenBy(item => item.Rate).ThenBy(item => item.Entity.Id.Value),
                _ => listings.OrderBy(item => item.Entity.Id.Value),
            };

            string[] lines = ordered.Take(1024).Select(item =>
            {
                string display = GetMetadataDisplay(item.Entity, out string note);
                return item.Entity.Id.Value + display + " type=" + item.Type + " rate=" + item.Rate + "% auto=" + item.Policy.Auto +
                       " group=" + item.Policy.GroupId + note;
            }).ToArray();
            return lines.Length == 0 ? "No supported entities matched." : string.Join(" | ", lines);
        }

        private string GetMetadataDisplay(EntityId id, out string note)
        {
            note = string.Empty;
            if (m_metadata is null || !TryGetSupportedEntity(id, out object? entity) || entity is not IEntity typed)
            {
                return string.Empty;
            }
            return GetMetadataDisplay(typed, out note);
        }

        private string GetMetadataDisplay(IEntity entity, out string note)
        {
            note = string.Empty;
            if (m_metadata is null)
            {
                return string.Empty;
            }

            try
            {
                var identity = new EntityMetadataIdentity(entity.Id.Value, "proto:" + entity.Prototype.Id.Value);
                if (!m_metadata.TryGetEntityMetadata(identity, out EntityMetadataRecord? metadata) || metadata is null)
                {
                    return string.Empty;
                }
                if (metadata.Note.Length != 0)
                {
                    note = " note=\"" + metadata.Note.Replace("\"", "'") + "\"";
                }
                return metadata.Alias.Length == 0 ? string.Empty : " alias=\"" + metadata.Alias.Replace("\"", "'") + "\"";
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static int GetPercentFor(object instance)
        {
            if (instance is IEntity entity && s_current is not null)
            {
                return s_current.GetPercent(entity.Id);
            }

            return DefaultPercent;
        }

        internal static bool IsAutoFor(object instance) => instance is IEntity entity && s_current?.IsAuto(entity.Id) == true;

        internal void AdvanceExtraCycles(object entity)
        {
            try
            {
                if (entity is Transport transport)
                {
                    AdvanceTransport(transport);
                    return;
                }

                if (!TajsTweaksRuntimeState.Overclocking || entity is not IEntity identified || GetPercent(identified.Id) <= 100)
                {
                    return;
                }

                int id = identified.Id.Value;
                float progress = GetPercent(identified.Id) / 100f - 1f;
                m_extraCycleAccumulators.TryGetValue(id, out float accumulator);
                accumulator += progress;

                FieldInfo? timerField = entity is OfficeBuilding ? m_officeRecipeTimer : entity is WasteSortingPlant ? m_wasteRecipeTimer : null;
                MethodInfo? decrement = entity is OfficeBuilding ? m_officeTimerDecrement : entity is WasteSortingPlant ? m_wasteTimerDecrement : null;
                while (accumulator >= 1f && timerField is not null && decrement is not null)
                {
                    accumulator -= 1f;
                    object? timer = timerField.GetValue(entity);
                    if (timer is not null)
                    {
                        decrement.Invoke(timer, null);
                    }
                    else
                    {
                        break;
                    }
                }

                m_extraCycleAccumulators[id] = accumulator;
            }
            catch
            {
                // Non-standard timer semantics are optional; unsupported versions remain vanilla.
            }
        }

        private void AdvanceTransport(Transport transport)
        {
            if (!TajsTweaksRuntimeState.Overclocking || m_transportTryMoveProducts is null ||
                m_transportSpeedDisabled.Contains(transport.Id.Value))
            {
                return;
            }

            int percent = GetPercent(transport.Id);
            if (percent <= DefaultPercent)
            {
                m_transportExtraMoveAccumulators.Remove(transport.Id.Value);
                return;
            }

            try
            {
                if (transport.IsProductsRemovalInProgress || !transport.FirstProduct.HasValue || transport.IsTooLong)
                {
                    return;
                }

                int id = transport.Id.Value;
                m_transportExtraMoveAccumulators.TryGetValue(id, out float accumulator);
                accumulator += percent / 100f - 1f;
                int moves = 0;
                while (accumulator >= 1f && moves < MaxTransportExtraMovesPerTick)
                {
                    if (transport.IsProductsRemovalInProgress || !transport.FirstProduct.HasValue || transport.IsTooLong)
                    {
                        break;
                    }

                    bool moved = (bool)m_transportTryMoveProducts.Invoke(transport, null)!;
                    if (!moved)
                    {
                        break;
                    }

                    accumulator -= 1f;
                    moves++;
                }

                m_transportExtraMoveAccumulators[id] = Math.Min(MaxTransportPendingMoves, Math.Max(0f, accumulator));
            }
            catch (Exception exception)
            {
                m_transportSpeedDisabled.Add(transport.Id.Value);
                m_transportExtraMoveAccumulators.Remove(transport.Id.Value);
                m_log.Exception(exception, "Transport speed boosting disabled for entity " + transport.Id.Value + ".");
            }
        }

        internal static void ReapplyMachine(Machine machine)
        {
            if (s_current is null || !TajsTweaksRuntimeState.Overclocking)
            {
                return;
            }

            OverclockEffectivePolicy policy = s_current.GetEffectivePolicy(machine.Id.Value);
            int desired = policy.Auto ? DefaultPercent : policy.ManualPercent;
            try
            {
                int current = ((Percent)s_current.m_machineSpeedBase.GetValue(machine)!).ToIntPercentRounded();
                if (current != desired)
                {
                    s_current.ApplyRate(machine, desired);
                }
            }
            catch
            {
            }
        }

        private void ReconcileLoadedMachines()
        {
            foreach (Machine machine in m_entities.GetAllEntitiesOfType<Machine>())
            {
                try
                {
                    int nativePercent = ((Percent)m_machineSpeedBase.GetValue(machine)!).ToIntPercentRounded();
                    if (!m_store.TryGetEntity(machine.Id.Value, out OverclockEntityPolicy? policy))
                    {
                        if (nativePercent != DefaultPercent)
                        {
                            policy = m_store.GetOrCreateEntity(machine.Id.Value);
                            policy.HasManualOverride = true;
                            policy.ManualPercent = nativePercent;
                        }
                    }

                    OverclockEffectivePolicy effective = GetEffectivePolicy(machine.Id.Value);
                    ApplyRate(machine, effective.Auto ? DefaultPercent : effective.ManualPercent);
                }
                catch (Exception exception)
                {
                    m_log.Exception(exception, "Failed to reconcile overclock state for machine " + machine.Id.Value + ".");
                }
            }

            m_store.Save();
            ReconcileKnownNonMachineEntities();
        }

        private void RunAutoCycle()
        {
            var ids = new HashSet<int>();
            foreach (KeyValuePair<int, OverclockEntityPolicy> pair in m_store.Entities)
            {
                if (pair.Value.HasAutoOverride && pair.Value.Auto)
                {
                    ids.Add(pair.Key);
                }
            }

            foreach (OverclockGroup group in m_store.Groups.Where(group => group.Auto))
            {
                ids.UnionWith(group.Members);
            }

            var plans = new List<AutoPlan>();
            foreach (int id in ids.OrderBy(value => value))
            {
                if (!TryGetSupportedEntity(new EntityId(id), out object? entity))
                {
                    continue;
                }

                OverclockEffectivePolicy policy = GetEffectivePolicy(id);
                if (!policy.Auto || entity is not Machine machine)
                {
                    continue;
                }

                int current = GetPercent(machine.Id);
                float? fill = GetOutputFill(machine);
                bool hasDemandSignal = OverclockingMath.HasDemandSignal(fill);
                int desired = hasDemandSignal
                    ? OverclockingMath.DesiredPercentForFill(
                        fill!.Value,
                        policy.MinPercent,
                        policy.MaxPercent,
                        TajsTweaksRuntimeState.OverclockAutoLowFill,
                        TajsTweaksRuntimeState.OverclockAutoNeutralFill,
                        TajsTweaksRuntimeState.OverclockAutoHighFill)
                    // Missing demand telemetry is a reduction-only fallback. It never invents
                    // an increase, and returns the entity to its configured manual neutral state.
                    : Math.Min(current, policy.ManualPercent);
                int next = OverclockingMath.ApplyHysteresis(
                    current,
                    desired,
                    new OverclockBounds(policy.MinPercent, policy.MaxPercent),
                    TajsTweaksRuntimeState.OverclockAutoDeadbandPercent,
                    TajsTweaksRuntimeState.OverclockAutoMaxStepPercent,
                    TajsTweaksRuntimeState.OverclockAutoStepPercent);

                plans.Add(
                    new AutoPlan
                    {
                        Id = id,
                        Machine = machine,
                        Current = current,
                        Target = next,
                        Minimum = policy.MinPercent,
                        HasDemandSignal = hasDemandSignal,
                    });
            }

            if (plans.Count == 0)
            {
                return;
            }

            bool hasBudgetTelemetry = TryGetAutoBudget(out int powerBudget, out int workerBudget);

            // Always perform reductions first. This makes the result deterministic and frees
            // capacity for the increase pass in the same cycle.
            foreach (AutoPlan plan in plans.Where(plan => plan.Target < plan.Current))
            {
                ApplyAutoPlan(plan, plan.Target, ref powerBudget, ref workerBudget);
            }

            if (hasBudgetTelemetry && (powerBudget < 0 || workerBudget < 0))
            {
                ForceTrimAutoPlans(plans, ref powerBudget, ref workerBudget);
            }

            if (hasBudgetTelemetry && powerBudget >= 0 && workerBudget >= 0)
            {
                foreach (AutoPlan plan in plans.Where(plan => plan.Target > plan.Current)
                             .OrderByDescending(plan => plan.Target - plan.Current)
                             .ThenBy(plan => plan.Id))
                {
                    int candidate = plan.Target;
                    while (candidate > plan.Current)
                    {
                        if (TryGetIncrementalCosts(plan.Machine, plan.Current, candidate, out int powerDelta, out int workerDelta) &&
                            powerDelta <= powerBudget && workerDelta <= workerBudget)
                        {
                            ApplyAutoPlan(plan, candidate, ref powerBudget, ref workerBudget);
                            break;
                        }

                        candidate = Math.Max(plan.Current, candidate - Math.Max(1, TajsTweaksRuntimeState.OverclockAutoStepPercent));
                    }
                }
            }

            m_store.Save();
        }

        private float? GetOutputFill(Machine machine)
        {
            if (m_products is null)
            {
                return null;
            }

            try
            {
                RecipeProto? recipe = machine.LastRecipeInProgress.ValueOrNull;
                if (recipe is null && machine.RecipesAssigned.Count > 0)
                {
                    recipe = machine.RecipesAssigned[0];
                }

                if (recipe is null)
                {
                    return null;
                }

                float highest = -1f;
                foreach (RecipeOutput output in recipe.AllOutputs)
                {
                    if (output is null || output.Product is null || output.IsPollution || output.HideInUi)
                    {
                        continue;
                    }

                    ProductStats? stats = m_products.GetStatsFor(output.Product);
                    if (stats is null || stats.StorageCapacity.Value <= 0)
                    {
                        continue;
                    }

                    highest = Math.Max(highest, (float)stats.StoredQuantityTotal.Value * 100f / stats.StorageCapacity.Value);
                }

                return highest < 0f ? null : Mathf.Clamp(highest, 0f, 100f);
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetAutoBudget(out int powerBudget, out int workerBudget)
        {
            powerBudget = 0;
            workerBudget = 0;
            try
            {
                if (m_electricity is null || m_workers is null)
                {
                    return false;
                }

                int generation = m_electricity.GenerationCapacityThisTick.Value;
                int consumed = m_electricity.ConsumedThisTick.Value;
                int reserve = (int)Math.Round(generation * TajsTweaksRuntimeState.OverclockAutoPowerReserve / 100d);
                powerBudget = generation - consumed - reserve;
                workerBudget = m_workers.AmountOfFreeWorkersOrMissing - TajsTweaksRuntimeState.OverclockAutoWorkerReserve;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetIncrementalCosts(Machine machine, int fromPercent, int toPercent, out int powerDelta, out int workerDelta)
        {
            powerDelta = 0;
            workerDelta = 0;
            try
            {
                int basePower = machine.Prototype.ConsumedPowerPerTick.Value;
                RecipeProto? recipe = machine.LastRecipeInProgress.ValueOrNull;
                if (recipe is not null)
                {
                    basePower = machine.Prototype.ConsumedPowerPerTick.ScaledBy(recipe.PowerMultiplier).Value;
                }

                powerDelta = OverclockingMath.RoundCost(basePower, toPercent, TajsTweaksRuntimeState.OverclockPowerCurve) -
                             OverclockingMath.RoundCost(basePower, fromPercent, TajsTweaksRuntimeState.OverclockPowerCurve);
                int baseWorkers = machine.Prototype.Costs.Workers;
                workerDelta = OverclockingMath.WorkersAt(baseWorkers, toPercent, TajsTweaksRuntimeState.OverclockWorkerCurve) -
                              OverclockingMath.WorkersAt(baseWorkers, fromPercent, TajsTweaksRuntimeState.OverclockWorkerCurve);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyAutoPlan(AutoPlan plan, int target, ref int powerBudget, ref int workerBudget)
        {
            if (target == plan.Current || !ApplyRate(plan.Machine, target))
            {
                return;
            }

            if (TryGetIncrementalCosts(plan.Machine, plan.Current, target, out int powerDelta, out int workerDelta))
            {
                powerBudget -= powerDelta;
                workerBudget -= workerDelta;
            }

            plan.Current = target;
        }

        private void ForceTrimAutoPlans(List<AutoPlan> plans, ref int powerBudget, ref int workerBudget)
        {
            AutoPlan[] order = plans.Where(plan => plan.Current > plan.Minimum)
                .OrderByDescending(plan => plan.Current)
                .ThenBy(plan => plan.Id)
                .ToArray();
            int maxIterations = Math.Max(1, order.Length * 1000);
            for (int iteration = 0; iteration < maxIterations && (powerBudget < 0 || workerBudget < 0); iteration++)
            {
                bool changed = false;
                foreach (AutoPlan plan in order)
                {
                    if (plan.Current <= plan.Minimum)
                    {
                        continue;
                    }

                    int next = Math.Max(plan.Minimum, plan.Current - Math.Max(1, TajsTweaksRuntimeState.OverclockAutoMaxStepPercent));
                    ApplyAutoPlan(plan, next, ref powerBudget, ref workerBudget);
                    plan.Target = plan.Current;
                    changed = true;
                    if (powerBudget >= 0 && workerBudget >= 0)
                    {
                        return;
                    }
                }

                if (!changed)
                {
                    return;
                }
            }
        }

        private bool ApplyRate(object entity, int percent)
        {
            try
            {
                if (entity is Machine machine)
                {
                    var oldBaseSpeed = (Percent)m_machineSpeedBase.GetValue(machine)!;
                    m_machineSpeedBase.SetValue(machine, percent.Percent());
                    m_machineUpdateSpeed.Invoke(machine, null);
                    if (oldBaseSpeed.ToIntPercentRounded() != percent && machine.WorkedThisTick)
                    {
                        RestartActiveMachineAnimation(machine);
                    }
                    RefreshConsumers(machine);
                    return true;
                }

                Type type = entity.GetType();
                if (m_oreSorterUpdateCapacity is not null && type.FullName == "Mafi.Core.Buildings.OreSorting.OreSortingPlant")
                {
                    m_oreSorterUpdateCapacity.Invoke(entity, new object[] { percent.Percent() });
                    if (entity is OreSortingPlant sorter && m_oreSorterSortedPerDuration is not null)
                    {
                        int adjusted = 100 + Math.Max(0, percent - 100);
                        m_oreSorterSortedPerDuration.SetValue(sorter, sorter.Prototype.QuantityPerDuration.ScaledBy(adjusted.Percent()));
                    }
                    RefreshConsumers(entity);
                    return true;
                }

                if (type.FullName == "Mafi.Core.Buildings.Offices.OfficeBuilding" ||
                    type.FullName == "Mafi.Core.Buildings.Waste.WasteSortingPlant")
                {
                    if (percent <= DefaultPercent && entity is IEntity identified)
                    {
                        m_extraCycleAccumulators.Remove(identified.Id.Value);
                    }

                    return type.FullName == "Mafi.Core.Buildings.Offices.OfficeBuilding" &&
                           m_officeRecipeTimer is not null && m_officeTimerDecrement is not null ||
                           type.FullName == "Mafi.Core.Buildings.Waste.WasteSortingPlant" &&
                           m_wasteRecipeTimer is not null && m_wasteTimerDecrement is not null;
                }

                if (entity is Transport transport && m_transportTryMoveProducts is not null)
                {
                    TransportOverclockingPatches.RegisterTransport(transport);
                    if (percent <= DefaultPercent)
                    {
                        m_transportExtraMoveAccumulators.Remove(transport.Id.Value);
                        m_transportSpeedDisabled.Remove(transport.Id.Value);
                    }

                    RefreshConsumers(transport);
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Applying overclock rate failed.");
                return false;
            }
        }

        private void RestartActiveMachineAnimation(Machine machine)
        {
            try
            {
                if (!machine.RecipeProductionTicks.IsPositive)
                {
                    return;
                }

                FieldInfo? recipeResultField = typeof(Machine).GetField(
                    "m_recipeResult",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object? recipeResult = recipeResultField?.GetValue(machine);
                FieldInfo? durationField = recipeResult?.GetType().GetField(
                    "Duration",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (durationField?.GetValue(recipeResult) is Duration duration && duration.IsPositive)
                {
                    machine.AnimationStatesProvider.Start(duration);
                }
            }
            catch
            {
                // The native recipe timer remains authoritative if animation resynchronization
                // is unavailable on a future COI build.
            }
        }

        private bool TryGetSupportedEntity(EntityId id, out object? entity)
        {
            if (m_entities.TryGetEntity<Machine>(id, out Machine? machine))
            {
                entity = machine;
                return true;
            }

            if (m_oreSorterUpdateCapacity is not null && m_oreSorterSortedPerDuration is not null &&
                m_entities.TryGetEntity<OreSortingPlant>(id, out OreSortingPlant? sorter))
            {
                entity = sorter;
                return true;
            }

            if (m_officeRecipeTimer is not null && m_officeTimerDecrement is not null &&
                m_entities.TryGetEntity<OfficeBuilding>(id, out OfficeBuilding? office))
            {
                entity = office;
                return true;
            }

            if (m_wasteRecipeTimer is not null && m_wasteTimerDecrement is not null &&
                m_entities.TryGetEntity<WasteSortingPlant>(id, out WasteSortingPlant? waste))
            {
                entity = waste;
                return true;
            }

            if (m_transportTryMoveProducts is not null && TransportOverclockingPatches.SpeedSeamAvailable &&
                m_entities.TryGetEntity<Transport>(id, out Transport? transport))
            {
                entity = transport;
                return true;
            }

            Type? oreType = FindType("Mafi.Core.Buildings.OreSorting.OreSortingPlant");
            if (m_oreSorterUpdateCapacity is not null && m_oreSorterSortedPerDuration is not null &&
                oreType is not null && TryGetEntityByType(id, oreType, out entity))
            {
                return true;
            }

            Type? officeType = FindType("Mafi.Core.Buildings.Offices.OfficeBuilding");
            if (m_officeRecipeTimer is not null && m_officeTimerDecrement is not null &&
                officeType is not null && TryGetEntityByType(id, officeType, out entity))
            {
                return true;
            }

            Type? wasteType = FindType("Mafi.Core.Buildings.Waste.WasteSortingPlant");
            if (m_wasteRecipeTimer is not null && m_wasteTimerDecrement is not null &&
                wasteType is not null && TryGetEntityByType(id, wasteType, out entity))
            {
                return true;
            }

            entity = null;
            return false;
        }

        private IEnumerable<IEntity> EnumerateSupportedEntities()
        {
            foreach (Machine machine in m_entities.GetAllEntitiesOfType<Machine>())
            {
                yield return machine;
            }

            if (m_oreSorterUpdateCapacity is not null && m_oreSorterSortedPerDuration is not null)
            {
                foreach (OreSortingPlant sorter in m_entities.GetAllEntitiesOfType<OreSortingPlant>())
                {
                    yield return sorter;
                }
            }

            if (m_officeRecipeTimer is not null && m_officeTimerDecrement is not null)
            {
                foreach (OfficeBuilding office in m_entities.GetAllEntitiesOfType<OfficeBuilding>())
                {
                    yield return office;
                }
            }

            if (m_wasteRecipeTimer is not null && m_wasteTimerDecrement is not null)
            {
                foreach (WasteSortingPlant waste in m_entities.GetAllEntitiesOfType<WasteSortingPlant>())
                {
                    yield return waste;
                }
            }

            if (m_transportTryMoveProducts is not null && TransportOverclockingPatches.SpeedSeamAvailable)
            {
                foreach (Transport transport in m_entities.GetAllEntitiesOfType<Transport>())
                {
                    yield return transport;
                }
            }
        }

        private static string GetEntityType(IEntity entity)
        {
            return entity switch
            {
                Machine => "machine",
                OreSortingPlant => "ore",
                OfficeBuilding => "office",
                WasteSortingPlant => "waste",
                Transport transport => TransportOverclockingPatches.IsBelt(transport) ? "belt" : "pipe",
                _ => "other",
            };
        }

        private void ImportLegacyTransportBoostsIfNeeded()
        {
            try
            {
                string saveName = Sanitize(GetSaveName());
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Captain of Industry",
                    "Mori++ Saved settings",
                    "Boost++ Saved settings");
                string[] candidates = { Path.Combine(root, saveName, "transports.txt"), Path.Combine(root, "transports.txt") };
                string? legacy = candidates.FirstOrDefault(File.Exists);
                if (legacy is null)
                {
                    return;
                }

                int imported = 0;
                foreach (string line in File.ReadAllLines(legacy).Take(MaxLegacyImportRecords))
                {
                    string[] fields = line.Split('=');
                    if (fields.Length != 2 || !int.TryParse(fields[0], out int id) ||
                        !float.TryParse(
                            fields[1],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float multiplier) ||
                        multiplier <= 1f || !TryGetSupportedEntity(new EntityId(id), out object? entity) ||
                        entity is not Transport || m_store.TryGetEntity(id, out OverclockEntityPolicy? existing) && existing!.HasManualOverride)
                    {
                        continue;
                    }

                    OverclockEntityPolicy policy = m_store.GetOrCreateEntity(id);
                    policy.HasManualOverride = true;
                    policy.ManualPercent = OverclockingMath.ClampPercent(
                        (int)Math.Round(multiplier * 100f),
                        100,
                        TajsTweaksRuntimeState.OverclockMaxPercent);
                    imported++;
                }

                if (imported > 0)
                {
                    m_store.Save();
                    m_log.Info("Imported " + imported + " legacy Boost++ belt/pipe policies into TajsTweaks.");
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Legacy Boost++ belt/pipe policy import failed open.");
            }
        }

        private bool IsKnownSupportedEntity(int id) => TryGetSupportedEntity(new EntityId(id), out _);

        private void ApplyGroupMembers(OverclockGroup group, Action<object> action)
        {
            foreach (int id in group.Members.OrderBy(value => value))
            {
                if (TryGetSupportedEntity(new EntityId(id), out object? entity))
                {
                    action(entity!);
                }
            }
        }

        private void ReconcileKnownNonMachineEntities()
        {
            var ids = new HashSet<int>(m_store.Entities.Keys);
            foreach (OverclockGroup group in m_store.Groups)
            {
                ids.UnionWith(group.Members);
            }

            foreach (int id in ids.OrderBy(value => value))
            {
                if (TryGetSupportedEntity(new EntityId(id), out object? entity) && entity is not Machine)
                {
                    OverclockEffectivePolicy policy = GetEffectivePolicy(id);
                    ApplyRate(entity!, policy.Auto ? DefaultPercent : policy.ManualPercent);
                }
            }
        }

        private void ResetLoadedRates()
        {
            try
            {
                foreach (Machine machine in m_entities.GetAllEntitiesOfType<Machine>())
                {
                    ApplyRate(machine, DefaultPercent);
                }
            }
            catch
            {
            }

            var ids = new HashSet<int>(m_store.Entities.Keys);
            foreach (OverclockGroup group in m_store.Groups)
            {
                ids.UnionWith(group.Members);
            }

            foreach (int id in ids)
            {
                if (TryGetSupportedEntity(new EntityId(id), out object? entity) && entity is not Machine)
                {
                    ApplyRate(entity!, DefaultPercent);
                }
            }
        }

        private void RefreshConsumers(object entity)
        {
            TryCallConsumerMethod(entity, "m_electricityConsumer", "OnPowerRequiredChanged");
            TryCallConsumerMethod(entity, "m_computingConsumer", "OnComputingRequiredChanged");
            try
            {
                PropertyInfo? maintenance = entity.GetType().GetProperty("Maintenance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object? provider = maintenance?.GetValue(entity);
                provider?.GetType().GetMethod("OnCostModifierChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(provider, null);
            }
            catch
            {
            }

            if (entity is IEntityWithWorkers workerEntity)
            {
                try
                {
                    m_workers?.ReturnWorkersVoluntarily(workerEntity);
                }
                catch
                {
                }
            }
        }

        private static void TryCallConsumerMethod(object entity, string fieldName, string methodName)
        {
            try
            {
                FieldInfo? field = FindField(entity.GetType(), fieldName);
                object? optional = field?.GetValue(entity);
                object? consumer = optional?.GetType().GetProperty("ValueOrNull", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(optional);
                consumer?.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(consumer, null);
            }
            catch
            {
            }
        }

        private void ImportLegacyBoostsIfNeeded()
        {
            if (m_store.Entities.Count > 0 || m_store.Groups.Count > 0)
            {
                return;
            }

            try
            {
                string saveName = GetSaveName();
                string safeName = Sanitize(saveName);
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Captain of Industry",
                    "Mori++ Saved settings",
                    "Boost++ Saved settings");
                string[] candidates = { Path.Combine(root, safeName, "boosts.txt"), Path.Combine(root, "boosts.txt") };
                string? legacy = candidates.FirstOrDefault(File.Exists);
                if (legacy is null)
                {
                    return;
                }

                int imported = 0;
                foreach (string line in File.ReadAllLines(legacy).Take(MaxLegacyImportRecords))
                {
                    string[] fields = line.Split('=');
                    if (fields.Length != 2 || !int.TryParse(fields[0], out int id) || !int.TryParse(fields[1], out int percent) ||
                        !TryGetSupportedEntity(new EntityId(id), out _))
                    {
                        continue;
                    }

                    OverclockEntityPolicy policy = m_store.GetOrCreateEntity(id);
                    policy.HasManualOverride = true;
                    policy.ManualPercent = OverclockingMath.ClampPercent(percent, 10, TajsTweaksRuntimeState.OverclockMaxPercent);
                    imported++;
                }

                if (imported > 0)
                {
                    m_store.Save();
                    m_log.Info("Imported " + imported + " legacy Boost++ machine policies into TajsTweaks.");
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Legacy Boost++ policy import failed open.");
            }
        }

        private string GetSaveName()
        {
            try
            {
                if (m_resolver.TryResolve(out ISaveManager saveManager) && !string.IsNullOrWhiteSpace(saveManager.GameName))
                {
                    return saveManager.GameName;
                }
            }
            catch
            {
            }

            return "current";
        }

        private static Type? FindType(string fullName) => typeof(Machine).Assembly.GetType(fullName);

        private static FieldInfo? FindField(string fullName, string name, BindingFlags flags)
        {
            Type? type = FindType(fullName);
            return type is null ? null : FindField(type, name);
        }

        private static FieldInfo? FindField(Type type, string name)
        {
            while (type is not null)
            {
                FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field is not null)
                {
                    return field;
                }

                type = type.BaseType!;
            }

            return null;
        }

        private static MethodInfo? FindMethod(string fullName, string name, BindingFlags flags)
        {
            Type? type = FindType(fullName);
            return type?.GetMethod(name, flags);
        }

        private static MethodInfo? FindTimerMethod(FieldInfo? timerField, string name) => timerField?.FieldType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private bool TryGetEntityByType(EntityId id, Type entityType, out object? entity)
        {
            try
            {
                MethodInfo? method = m_entities.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(candidate =>
                        candidate.Name == "TryGetEntity" && candidate.IsGenericMethodDefinition && candidate.GetParameters().Length == 2);
                if (method is not null)
                {
                    object[] args = { id, null! };
                    bool found = (bool)method.MakeGenericMethod(entityType).Invoke(m_entities, args)!;
                    entity = args[1];
                    return found;
                }
            }
            catch
            {
            }

            entity = null;
            return false;
        }

        private static string Sanitize(string value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }

            return result.Trim();
        }
    }
}
