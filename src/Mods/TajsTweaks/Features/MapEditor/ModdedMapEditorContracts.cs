// Taj's COI Mods | ModdedMapEditorContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mafi;

namespace TajsCOI.Tweaks.Features.MapEditor
{
    internal readonly struct MapEditorModManifest
    {
        internal MapEditorModManifest(string id, string version)
        {
            Id = id?.Trim() ?? string.Empty;
            Version = version?.Trim() ?? string.Empty;
        }

        internal string Id { get; }
        internal string Version { get; }
        internal bool IsValid => !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(Version);
    }

    internal readonly struct MapEditorModDecision
    {
        internal MapEditorModDecision(MapEditorModManifest manifest, bool compatible, string reason)
        {
            Manifest = manifest;
            Compatible = compatible;
            Reason = reason?.Trim() ?? string.Empty;
        }

        internal MapEditorModManifest Manifest { get; }
        internal bool Compatible { get; }
        internal string Reason { get; }
    }

    internal static class MapEditorModSelection
    {
        internal static bool IsCompatible(
            MapEditorModManifest requested,
            IEnumerable<MapEditorModManifest> available)
        {
            if (!requested.IsValid)
            {
                return false;
            }

            return (available ?? Array.Empty<MapEditorModManifest>()).Any(candidate =>
                candidate.IsValid &&
                string.Equals(candidate.Id, requested.Id, StringComparison.Ordinal) &&
                string.Equals(candidate.Version, requested.Version, StringComparison.Ordinal));
        }
    }

    internal static class MapEditorNativeContract
    {
        internal static string? LastFailure { get; private set; }

        internal static bool TryResolve(
            out MethodInfo? mapEditorClick,
            out MethodInfo? goToMainMenu,
            out MethodInfo? tryLoadMods,
            out FieldInfo? mainField)
        {
            try
            {
                Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "Mafi.Unity", StringComparison.Ordinal));
                gameAssembly ??= Assembly.Load("Mafi.Unity");
                Type? menuType = gameAssembly?.GetType("Mafi.Unity.MainMenu.MainMenuScreen", throwOnError: false);
                Type? mainInterfaceType = gameAssembly?.GetType("Mafi.Unity.IMain", throwOnError: false);
                Type? mainMenuArgsType = gameAssembly?.GetType("Mafi.Unity.MainMenu.MainMenuArgs", throwOnError: false);
                if (menuType is null || mainInterfaceType is null || mainMenuArgsType is null)
                {
                    throw new TypeLoadException("Mafi.Unity map-editor contract types were not loaded");
                }

                mapEditorClick = menuType.GetMethod(
                    "onMapEditorClick",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                Assembly loadedAssembly = gameAssembly!;
                Type? mainType = loadedAssembly.GetType("Mafi.Unity.Main", throwOnError: false);
                goToMainMenu = mainType?.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .SingleOrDefault(method => method.Name == "GoToMainMenu" && IsExactInstanceVoid(method, mainMenuArgsType.FullName!));
                tryLoadMods = mainType?.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .SingleOrDefault(method => method.Name == "TryLoadMods" && IsExactTryLoadMods(method));
                mainField = menuType.GetField(
                    "m_main",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                bool exact = IsExactInstanceVoid(mapEditorClick) &&
                             IsExactInstanceVoid(goToMainMenu, "Mafi.Unity.MainMenu.MainMenuArgs") &&
                             tryLoadMods is not null &&
                             mainField is not null &&
                             !mainField.IsStatic &&
                             TypeName(mainField.FieldType) == mainInterfaceType.FullName;
                LastFailure = exact
                    ? null
                    : "Mafi.Unity map-editor members do not match the exact 0.8.7b contract";
                return exact;
            }
            catch (Exception exception)
            {
                mapEditorClick = null;
                goToMainMenu = null;
                tryLoadMods = null;
                mainField = null;
                LastFailure = exception.GetType().FullName + ": " + exception.Message;
                return false;
            }
        }

        private static bool IsExactInstanceVoid(MethodInfo? method, params string[] parameterTypeNames)
        {
            if (method is null || method.IsStatic || method.ReturnType != typeof(void))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == parameterTypeNames.Length &&
                   parameters.Select(parameter => TypeName(parameter.ParameterType))
                       .SequenceEqual(parameterTypeNames, StringComparer.Ordinal);
        }

        private static bool IsExactTryLoadMods(MethodInfo? method)
        {
            if (method is null || method.IsStatic || TypeName(method.ReturnType) != "System.Boolean")
            {
                return false;
            }

            string[] expected =
            {
                "Mafi.Collections.ImmutableCollections.ImmutableArray`1[Mafi.Core.Mods.AvailableModData]",
                "System.Boolean",
                "Mafi.Collections.ImmutableCollections.ImmutableArray`1[Mafi.Core.Mods.LoadedModData]&",
                "System.String&",
            };
            return method.GetParameters().Select(parameter => TypeName(parameter.ParameterType))
                .SequenceEqual(expected, StringComparer.Ordinal);
        }

        private static string TypeName(Type type)
        {
            if (type.IsByRef)
            {
                return TypeName(type.GetElementType()!) + "&";
            }
            if (type.IsGenericType)
            {
                string definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
                string arguments = string.Join(",", type.GetGenericArguments().Select(TypeName));
                return definition + "[" + arguments + "]";
            }
            return type.FullName ?? type.Name;
        }
    }

    /// <summary>Temporary manifest-only context; resolver/mod instances are deliberately excluded.</summary>
    internal sealed class ModdedMapEditorContext
    {
        private readonly List<MapEditorModManifest> m_manifests = new();
        private readonly List<MapEditorModDecision> m_decisions = new();
        internal bool IsActive { get; private set; }
        internal IReadOnlyList<MapEditorModManifest> Manifests => m_manifests;
        internal IReadOnlyList<MapEditorModDecision> Decisions => m_decisions;

        internal void Begin(IEnumerable<MapEditorModManifest> manifests)
        {
            Clear();
            m_manifests.AddRange(
                (manifests ?? Array.Empty<MapEditorModManifest>()).Where(manifest => manifest.IsValid).GroupBy(manifest => manifest.Id, StringComparer.Ordinal)
                .Select(group => group.First()));
            IsActive = true;
        }

        internal IReadOnlyList<MapEditorModManifest> Resolve(Func<MapEditorModManifest, bool> canResolve)
        {
            m_decisions.Clear();
            List<MapEditorModManifest> compatible = new();
            foreach (MapEditorModManifest manifest in m_manifests)
            {
                bool accepted = canResolve?.Invoke(manifest) == true;
                m_decisions.Add(new MapEditorModDecision(manifest, accepted, accepted ? string.Empty : "manifest could not be resolved in editor mode"));
                if (accepted)
                {
                    compatible.Add(manifest);
                }
            }
            return compatible;
        }

        internal void Clear()
        {
            m_manifests.Clear();
            m_decisions.Clear();
            IsActive = false;
        }
    }
}
