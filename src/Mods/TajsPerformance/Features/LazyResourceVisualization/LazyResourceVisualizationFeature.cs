// Taj's COI Mods | LazyResourceVisualizationFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance.Features.LazyResourceVisualization
{
    /// <summary>
    ///     Defers the hidden whole-map resource-bar build until the first resource overlay
    ///     activation. The exact vanilla init method is still invoked before the activation call.
    ///     This candidate is opt-in because first-use latency and overlay correctness require a
    ///     real-game A/B run.
    /// </summary>
    internal sealed class LazyResourceVisualizationFeature : IPerformanceFeature
    {
        private const string HarmonyId = "TajsCOI.Performance.LazyResourceVisualization";
        private const string RendererTypeName = "Mafi.Unity.InputControl.ResVis.ResVisBarsRenderer";

        private static readonly object s_installGate = new();
        private static readonly ConditionalWeakTable<object, LazyState> s_states = new();
        private static MethodInfo? s_initState;
        private static MethodInfo? s_forceSetActive;

        public string Id => "LazyResourceVisualization";
        public string ConfigKey => LazyResourceVisualizationSettings.EnableConfigKey;

        public bool IsProcessPatchInstalled()
        {
            TargetSet? targets = FindTargets();
            MethodInfo? skip = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(SkipInitialBuild));
            MethodInfo? ensure = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(EnsureInitialized));
            return targets is not null && skip is not null && ensure is not null &&
                ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.InitState)?.Prefixes, HarmonyId, skip) &&
                targets.Activators.All(x => ProcessHarmonyPatchOwnership.HasExpected(
                    Harmony.GetPatchInfo(x)?.Prefixes,
                    HarmonyId,
                    ensure));
        }

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            TargetSet targets = FindTargets() ?? throw new MissingMethodException(
                RendererTypeName,
                "initState(), ForceSetActive(bool), and Activator.Show/ShowExactly/ShowAll() compatibility set");
            MethodInfo skip = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(SkipInitialBuild))!;
            MethodInfo ensure = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(EnsureInitialized))!;

            lock (s_installGate)
            {
                if (IsProcessPatchInstalled())
                {
                    log.Info("Already installed / compatible; lazy resource visualization was not patched again.");
                    runtime.ReportCompatibility(new CompatibilityReport(
                        "TajsPerformance", Id, CompatibilityState.Compatible,
                        "Existing process-lifetime Harmony owner on the 0.8.7a resource overlay targets",
                        "Already installed / compatible",
                        "The validated lazy-build patch remains active; no duplicate prefixes were registered."));
                    return;
                }

                var harmony = new Harmony(HarmonyId);
                try
                {
                    harmony.Patch(targets.InitState, prefix: new HarmonyMethod(skip));
                    foreach (MethodInfo activator in targets.Activators)
                    {
                        harmony.Patch(activator, prefix: new HarmonyMethod(ensure));
                    }
                }
                catch (Exception exception)
                {
                    harmony.Unpatch(targets.InitState, HarmonyPatchType.Prefix, HarmonyId);
                    foreach (MethodInfo activator in targets.Activators)
                    {
                        harmony.Unpatch(activator, HarmonyPatchType.Prefix, HarmonyId);
                    }
                    throw new InvalidOperationException(
                        "Lazy resource visualization patch installation failed open; vanilla eager initialization remains active.",
                        exception);
                }
            }

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                Id,
                CompatibilityState.Compatible,
                "Exact 0.8.7a ResVisBarsRenderer initState and Activator.Show* targets",
                "Lazy initial resource-bar build installed",
                "The hidden initial build is deferred until first overlay activation; the exact vanilla init method runs before that activation."));
        }

        private static bool SkipInitialBuild(object __instance)
        {
            LazyState? state = null;
            try
            {
                state = s_states.GetValue(__instance, _ => new LazyState());
                if (state.Initialized)
                {
                    return true;
                }

                if (state.InitializationDeferred)
                {
                    return false;
                }

                state.InitializationDeferred = true;
                s_forceSetActive?.Invoke(__instance, new object[] { false });
                return false;
            }
            catch
            {
                // A callback failure must leave the original eager initialization active.
                if (state is not null)
                {
                    state.InitializationDeferred = false;
                }
                return true;
            }
        }

        private static void EnsureInitialized(object __instance)
        {
            LazyState state = s_states.GetValue(__instance, _ => new LazyState());
            if (!state.InitializationDeferred || state.Initialized || state.InitializationInProgress)
            {
                return;
            }

            state.InitializationInProgress = true;
            try
            {
                state.Initialized = true;
                // The initState prefix allows the original method through once Initialized is true.
                s_initState!.Invoke(__instance, null);
            }
            catch
            {
                state.Initialized = false;
                throw;
            }
            finally
            {
                state.InitializationInProgress = false;
            }
        }

        private static TargetSet? FindTargets()
        {
            Type? renderer = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, "Mafi.Unity", StringComparison.Ordinal))
                ?.GetType(RendererTypeName, false);
            Type? activator = renderer?.GetNestedType("Activator", BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? initState = renderer is null
                ? null
                : renderer.GetMethod("initState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            MethodInfo? forceSetActive = renderer?.GetMethod(
                "ForceSetActive",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            MethodInfo[]? activators = activator?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.DeclaringType == activator && !x.IsStatic &&
                    ((x.Name == "ShowAll" && x.GetParameters().Length == 0) ||
                     (x.Name == "Show" && x.GetParameters().Length == 1) ||
                     (x.Name == "ShowExactly" && x.GetParameters().Length == 1)))
                .OrderBy(x => x.Name)
                .ThenBy(x => x.MetadataToken)
                .ToArray();

            if (initState is null || forceSetActive is null || activators is null || activators.Length != 4)
            {
                return null;
            }

            s_initState = initState;
            s_forceSetActive = forceSetActive;
            return new TargetSet(initState, activators);
        }

        private sealed class LazyState
        {
            internal bool InitializationDeferred;
            internal bool Initialized;
            internal bool InitializationInProgress;
        }

        private sealed class TargetSet
        {
            internal TargetSet(MethodInfo initState, IReadOnlyList<MethodInfo> activators)
            {
                InitState = initState;
                Activators = activators;
            }

            internal MethodInfo InitState { get; }
            internal IReadOnlyList<MethodInfo> Activators { get; }
        }
    }
}
