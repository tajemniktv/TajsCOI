// Taj's COI Mods | EntityMetadataInspectorPatch.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Localization;
using Mafi.Unity.Ui;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Metadata;
using TajsCOI.Common.Runtime;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using Column = Mafi.Unity.UiToolkit.Library.Column;

namespace TajsCOI.Tweaks.Features.EntityMetadata
{
    /// <summary>
    ///     Adds optional alias/note text after the native inspector has activated. Metadata is
    ///     read-only here; Core remains the only owner of identity validation and persistence.
    /// </summary>
    internal static class EntityMetadataInspectorPatch
    {
        private sealed class Marker
        {
        }

        private static readonly ConditionalWeakTable<object, Marker> s_augmented = new();
        private static IEntityMetadataLookup? s_lookup;

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            if (!resolver.TryResolve(out IEntityMetadataLookup? lookup) || lookup is null)
            {
                throw new InvalidOperationException("Core entity metadata lookup is unavailable.");
            }
            s_lookup = lookup;
            MethodBase[] targets = FindTargets().ToArray();
            if (targets.Length == 0)
            {
                throw new MissingMethodException("Mafi.Unity entity inspector activation targets were not found.");
            }
            foreach (MethodBase target in targets)
            {
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(EntityMetadataInspectorPatch), nameof(OnInspectorActivated)));
            }
        }

        internal static bool HasExpectedTarget()
        {
            return FindTargets().Any();
        }

        private static IEnumerable<MethodBase> FindTargets()
        {
            MethodInfo? activate = typeof(InspectorsManager).GetMethod(
                nameof(InspectorsManager.TryActivateFor),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(IEntity), typeof(IEntityInspector).MakeByRefType() },
                null);
            if (activate is not null)
            {
                yield return activate;
            }
        }

        private static void OnInspectorActivated(IEntity entity, ref IEntityInspector inspector)
        {
            IEntityMetadataLookup? lookup = s_lookup;
            if (lookup is null || entity is null || inspector is null || s_augmented.TryGetValue(inspector, out _))
            {
                return;
            }

            try
            {
                var identity = new EntityMetadataIdentity(entity.Id.Value, "proto:" + entity.Prototype.Id.Value);
                if (!lookup.TryGetEntityMetadata(identity, out EntityMetadataRecord? metadata) || metadata is null ||
                    metadata.Alias.Length == 0 && metadata.Note.Length == 0)
                {
                    return;
                }
                if (ReadMainBody(inspector) is not Column body)
                {
                    return;
                }

                if (metadata.Alias.Length != 0)
                {
                    body.Add(new Label(("Alias: " + metadata.Alias).AsLoc()).FontBold());
                }
                if (metadata.Note.Length != 0)
                {
                    body.Add(new Label(("Note: " + metadata.Note).AsLoc()).FontSize(11));
                }
                s_augmented.Add(inspector, new Marker());
            }
            catch
            {
                // Optional UI augmentation must never interfere with native inspector behavior.
            }
        }

        private static Column? ReadMainBody(object inspector)
        {
            FieldInfo? field = FindField(inspector.GetType(), "MainBody");
            return field?.GetValue(inspector) as Column;
        }

        private static FieldInfo? FindField(Type type, string name)
        {
            for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo? field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field is not null)
                {
                    return field;
                }
            }
            return null;
        }
    }
}
