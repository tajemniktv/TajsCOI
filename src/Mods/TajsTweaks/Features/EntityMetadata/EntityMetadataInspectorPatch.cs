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
using Mafi.Unity.UiToolkit.Component;
using TajsCOI.Common.Metadata;
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
        private static readonly object s_gate = new();

        private sealed class Marker
        {
            internal readonly List<UiComponent> Elements = new();
        }

        // Marker values contain only inspector-owned UI elements. Weak keys ensure this process-
        // static cache cannot keep an old scene's inspector (or its UI tree) alive.
        private static readonly ConditionalWeakTable<object, Marker> s_augmented = new();
        private static WeakReference<IEntityMetadataLookup>? s_lookup;
        private static bool s_installed;

        internal static IEntityMetadataLookup Install(Harmony harmony, DependencyResolver resolver)
        {
            if (!resolver.TryResolve(out IEntityMetadataLookup? lookup) || lookup is null)
            {
                throw new InvalidOperationException("Core entity metadata lookup is unavailable.");
            }

            return Install(harmony, lookup);
        }

        internal static IEntityMetadataLookup Install(Harmony harmony, IEntityMetadataLookup lookup)
        {
            if (harmony is null)
            {
                throw new ArgumentNullException(nameof(harmony));
            }

            if (lookup is null)
            {
                throw new ArgumentNullException(nameof(lookup));
            }

            lock (s_gate)
            {
                if (!s_installed)
                {
                    MethodBase[] targets = FindTargets().ToArray();
                    if (targets.Length == 0)
                    {
                        throw new MissingMethodException("Mafi.Unity entity inspector activation targets were not found.");
                    }

                    try
                    {
                        foreach (MethodBase target in targets)
                        {
                            harmony.Patch(
                                target,
                                postfix: new HarmonyMethod(typeof(EntityMetadataInspectorPatch), nameof(OnInspectorActivated)));
                        }
                        s_installed = true;
                    }
                    catch
                    {
                        // The host also rolls back its owner, but doing so here keeps this helper
                        // transactional for direct/runtime-contract callers as well.
                        try
                        {
                            harmony.UnpatchAll(harmony.Id);
                        }
                        catch
                        {
                            // Preserve the original compatibility failure.
                        }
                        throw;
                    }
                }

                // The Harmony callback is process-lived, but its lookup is scene-lived. Rebinding
                // replaces only the weak target and never gives the process a strong scene root.
                s_lookup = new WeakReference<IEntityMetadataLookup>(lookup);
                return lookup;
            }
        }

        /// <summary>
        ///     Binds the current gameplay-scene lookup without creating a process-lifetime strong
        ///     reference to its resolver-owned service.
        /// </summary>
        internal static void Bind(IEntityMetadataLookup lookup)
        {
            if (lookup is null)
            {
                throw new ArgumentNullException(nameof(lookup));
            }

            lock (s_gate)
            {
                s_lookup = new WeakReference<IEntityMetadataLookup>(lookup);
            }
        }

        /// <summary>
        ///     Disconnects one scene's lookup from the process-lifetime callback. An old host cannot
        ///     clear a newer scene's binding because the expected lookup is compared by identity.
        /// </summary>
        internal static void Unbind(IEntityMetadataLookup? expected)
        {
            if (expected is null)
            {
                return;
            }

            lock (s_gate)
            {
                if (s_lookup is null || !s_lookup.TryGetTarget(out IEntityMetadataLookup? current) ||
                    ReferenceEquals(current, expected))
                {
                    s_lookup = null;
                }
            }
        }

        internal static void Unbind() => Reset();

        /// <summary>
        ///     Clears whichever scene binding remains. Harmony installation is intentionally not
        ///     reset: the target patch is process-lived and is reused by the next gameplay scene.
        /// </summary>
        internal static void Reset()
        {
            lock (s_gate)
            {
                s_lookup = null;
            }
        }

        internal static bool IsBoundTo(IEntityMetadataLookup lookup)
        {
            if (lookup is null)
            {
                return false;
            }

            lock (s_gate)
            {
                return s_lookup is not null && s_lookup.TryGetTarget(out IEntityMetadataLookup? current) &&
                       ReferenceEquals(current, lookup);
            }
        }

        internal static bool HasLiveLookup
        {
            get
            {
                lock (s_gate)
                {
                    if (s_lookup is not null && s_lookup.TryGetTarget(out _))
                    {
                        return true;
                    }

                    s_lookup = null;
                    return false;
                }
            }
        }

        internal static bool IsInstalled
        {
            get
            {
                lock (s_gate)
                {
                    return s_installed;
                }
            }
        }

        internal static bool HasExpectedTarget() => FindTargets().Any();

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
            IEntityMetadataLookup? lookup = GetLookup();
            if (lookup is null || entity is null || inspector is null)
            {
                return;
            }

            try
            {
                if (!s_augmented.TryGetValue(inspector, out Marker? marker))
                {
                    marker = new Marker();
                    s_augmented.Add(inspector, marker);
                }
                foreach (UiComponent element in marker.Elements)
                {
                    element.RemoveFromHierarchy();
                }
                marker.Elements.Clear();

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
                    Label alias = new Label(("Alias: " + metadata.Alias).AsLoc()).FontBold();
                    body.Add(alias);
                    marker.Elements.Add(alias);
                }
                if (metadata.Note.Length != 0)
                {
                    Label note = new Label(("Note: " + metadata.Note).AsLoc()).FontSize(11);
                    body.Add(note);
                    marker.Elements.Add(note);
                }
            }
            catch
            {
                // Optional UI augmentation must never interfere with native inspector behavior.
            }
        }

        private static IEntityMetadataLookup? GetLookup()
        {
            lock (s_gate)
            {
                if (s_lookup is not null && s_lookup.TryGetTarget(out IEntityMetadataLookup? lookup))
                {
                    return lookup;
                }

                s_lookup = null;
                return null;
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
