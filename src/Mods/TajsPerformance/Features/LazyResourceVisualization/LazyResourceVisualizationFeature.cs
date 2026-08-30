// Taj's COI Mods | LazyResourceVisualizationFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
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
        private static FieldInfo? s_rendererField;
        private static int s_deferralDisabled;

        public string Id => "LazyResourceVisualization";
        public string ConfigKey => LazyResourceVisualizationSettings.EnableConfigKey;

        public bool IsProcessPatchInstalled() => HasProcessPatchInstalled();

        private static bool HasProcessPatchInstalled()
        {
            TargetSet? targets = FindTargets();
            MethodInfo? skip = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(SkipInitialBuild));
            MethodInfo? ensure = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(EnsureInitialized));
            MethodInfo? ensureForceSetActive = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(EnsureForceSetActive));
            return targets is not null && skip is not null && ensure is not null &&
                   ensureForceSetActive is not null &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.InitState)?.Prefixes, HarmonyId, skip) &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.ForceSetActive)?.Prefixes, HarmonyId, ensureForceSetActive) &&
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
            MethodInfo ensureForceSetActive = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(EnsureForceSetActive))!;

            lock (s_installGate)
            {
                if (IsProcessPatchInstalled())
                {
                    log.Info("Already installed / compatible; lazy resource visualization was not patched again.");
                    runtime.ReportCompatibility(
                        new CompatibilityReport(
                            "TajsPerformance",
                            Id,
                            CompatibilityState.Compatible,
                            "Existing process-lifetime Harmony owner on the 0.8.7b resource overlay targets",
                            "Already installed / compatible",
                            "The validated lazy-build patch remains active; no duplicate prefixes were registered."));
                    return;
                }

                InstallPatches(targets, skip, ensure, ensureForceSetActive);
            }

            runtime.ReportCompatibility(
                new CompatibilityReport(
                    "TajsPerformance",
                    Id,
                    CompatibilityState.Compatible,
                    "Exact 0.8.7b ResVisBarsRenderer initState and Activator.Show* targets",
                    "Lazy initial resource-bar build installed",
                    "The hidden initial build is deferred until first overlay activation; the exact vanilla init method runs before that activation."));
        }

        /// <summary>
        ///     Installs the process-lifetime candidate from the data-only mod constructor when the
        ///     persisted opt-in is already true. This runs before dependency resolution creates
        ///     gameplay-scene services, so the load-time renderer seam is actually covered.
        /// </summary>
        internal static bool TryInstallProcessEarly()
        {
            try
            {
                TargetSet? targets = FindTargets();
                if (targets is null)
                {
                    return false;
                }

                MethodInfo? skip = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(SkipInitialBuild));
                MethodInfo? ensure = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(EnsureInitialized));
                MethodInfo? ensureForceSetActive = AccessTools.Method(typeof(LazyResourceVisualizationFeature), nameof(EnsureForceSetActive));
                if (skip is null || ensure is null || ensureForceSetActive is null)
                {
                    return false;
                }

                lock (s_installGate)
                {
                    if (HasProcessPatchInstalled())
                    {
                        return true;
                    }

                    InstallPatches(targets, skip, ensure, ensureForceSetActive);
                    return true;
                }
            }
            catch
            {
                // The early path is deliberately fail-open. The scene host can retry later and
                // vanilla initialization remains active when the private seam is unavailable.
                return false;
            }
        }

        private static void InstallPatches(
            TargetSet targets,
            MethodInfo skip,
            MethodInfo ensure,
            MethodInfo ensureForceSetActive)
        {
            var harmony = new Harmony(HarmonyId);
            try
            {
                harmony.Patch(targets.InitState, prefix: new HarmonyMethod(skip));
                harmony.Patch(targets.ForceSetActive, prefix: new HarmonyMethod(ensureForceSetActive));
                foreach (MethodInfo activator in targets.Activators)
                {
                    harmony.Patch(activator, prefix: new HarmonyMethod(ensure));
                }
            }
            catch (Exception exception)
            {
                harmony.Unpatch(targets.InitState, HarmonyPatchType.Prefix, HarmonyId);
                harmony.Unpatch(targets.ForceSetActive, HarmonyPatchType.Prefix, HarmonyId);
                foreach (MethodInfo activator in targets.Activators)
                {
                    harmony.Unpatch(activator, HarmonyPatchType.Prefix, HarmonyId);
                }
                throw new InvalidOperationException(
                    "Lazy resource visualization patch installation failed open; vanilla eager initialization remains active.",
                    exception);
            }
        }

        private static bool SkipInitialBuild(object __instance)
        {
            // If the renderer field/bookkeeping seam fails during first activation, permit the
            // original init method through for every subsequent call. A process-wide fail-open
            // switch is safer than allowing a single uninitialized overlay to remain hidden.
            if (Volatile.Read(ref s_deferralDisabled) != 0)
            {
                return true;
            }

            LazyState? state = null;
            try
            {
                state = s_states.GetValue(__instance, _ => new LazyState());
                if (state.Initialized)
                {
                    return true;
                }

                if (state.EagerFallbackRequested)
                {
                    state.EagerFallbackRequested = false;
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
            try
            {
                object? renderer = s_rendererField?.DeclaringType?.IsInstanceOfType(__instance) == true
                    ? s_rendererField.GetValue(__instance)
                    : __instance;
                if (renderer is null)
                {
                    Interlocked.Exchange(ref s_deferralDisabled, 1);
                    return;
                }

                EnsureInitializedRenderer(renderer);
            }
            catch
            {
                // A reflection/state failure must not break the vanilla overlay activation. Let
                // the original initState through before returning so the first activation still
                // gets the native renderer build even when the private field changed shape.
                Interlocked.Exchange(ref s_deferralDisabled, 1);
            }
        }

        private static void EnsureForceSetActive(object __instance, bool isActive)
        {
            if (!isActive)
            {
                return;
            }

            EnsureInitialized(__instance);
        }

        private static void EnsureInitializedRenderer(object renderer)
        {
            LazyState? state = null;
            try
            {
                state = s_states.GetValue(renderer, _ => new LazyState());
                if (!state.InitializationDeferred || state.Initialized || state.InitializationInProgress)
                {
                    return;
                }

                state.InitializationInProgress = true;
                try
                {
                    state.Initialized = true;
                    // The initState prefix allows the original method through once Initialized is true.
                    s_initState!.Invoke(renderer, null);
                }
                catch
                {
                    // If deferred initialization fails, immediately replay the exact original
                    // init path. The one-shot flag lets its prefix pass through instead of
                    // deferring the fallback a second time.
                    InvokeVanillaInitialization(renderer, state);
                }
                finally
                {
                    state.InitializationInProgress = false;
                }
            }
            catch
            {
                // If our bookkeeping/reflection path fails, replay the exact vanilla init path
                // rather than allowing first activation to continue with an uninitialized
                // renderer. This remains fail-open if the private target itself is unavailable.
                Interlocked.Exchange(ref s_deferralDisabled, 1);
                InvokeVanillaInitialization(renderer, state);
            }
        }

        private static void InvokeVanillaInitialization(object renderer, LazyState? state)
        {
            if (s_initState is null)
            {
                return;
            }

            if (state is not null)
            {
                state.Initialized = false;
                state.InitializationDeferred = false;
                state.EagerFallbackRequested = true;
            }

            try
            {
                s_initState.Invoke(renderer, null);
            }
            catch
            {
                if (state is not null)
                {
                    state.EagerFallbackRequested = false;
                }
            }
        }

        internal static TargetSet? FindTargets()
        {
            Type? renderer = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, "Mafi.Unity", StringComparison.Ordinal))
                ?.GetType(RendererTypeName, false);
            BindingFlags rendererFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Type? activator = renderer?.GetNestedType("Activator", BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo? rendererField = activator?.GetField(
                "Renderer",
                rendererFlags);
            MethodInfo? initState = renderer is null
                ? null
                : renderer.GetMethod("initState", rendererFlags, null, Type.EmptyTypes, null);
            MethodInfo? forceSetActive = renderer?.GetMethod(
                "ForceSetActive",
                rendererFlags,
                null,
                new[] { typeof(bool) },
                null);
            BindingFlags activatorFlags = rendererFlags;
            MethodInfo? show = activator?.GetMethod(
                "Show",
                activatorFlags,
                null,
                new[] { typeof(Mafi.Core.Products.ProductProto) },
                null);
            MethodInfo? showAll = activator?.GetMethod(
                "ShowAll",
                activatorFlags,
                null,
                Type.EmptyTypes,
                null);
            Type productSequence = typeof(IEnumerable<>).MakeGenericType(typeof(Mafi.Core.Products.ProductProto));
            Type productArray = typeof(Mafi.Collections.ImmutableCollections.ImmutableArray<>).MakeGenericType(typeof(Mafi.Core.Products.ProductProto));
            MethodInfo[] showExactly = activator?.GetMethods(activatorFlags)
                .Where(x => x.DeclaringType == activator && !x.IsStatic && x.Name == "ShowExactly" &&
                            x.GetParameters().Length == 1 &&
                            (x.GetParameters()[0].ParameterType == productSequence ||
                             x.GetParameters()[0].ParameterType == productArray))
                .OrderBy(x => x.GetParameters()[0].ParameterType == productSequence ? 0 : 1)
                .ToArray() ?? Array.Empty<MethodInfo>();

            if (initState is null || initState.ReturnType != typeof(void) ||
                forceSetActive is null || forceSetActive.ReturnType != typeof(void) ||
                rendererField is null || rendererField.FieldType != renderer || !rendererField.IsInitOnly ||
                show is null || show.ReturnType != typeof(void) ||
                showAll is null || showAll.ReturnType != typeof(void) ||
                showExactly.Length != 2 || showExactly.Any(method => method.ReturnType != typeof(void)))
            {
                return null;
            }

            s_initState = initState;
            s_forceSetActive = forceSetActive;
            s_rendererField = rendererField;
            return new TargetSet(initState, forceSetActive, rendererField, show, showExactly, showAll);
        }

        private sealed class LazyState
        {
            internal bool InitializationDeferred;
            internal bool Initialized;
            internal bool InitializationInProgress;
            internal bool EagerFallbackRequested;
        }

        /// <summary>
        ///     Exact 0.8.7b resource-visualization seam. Keeping each member named makes the
        ///     contract test describe the target surface instead of relying on method ordering.
        /// </summary>
        internal sealed class TargetSet
        {
            internal TargetSet(
                MethodInfo initState,
                MethodInfo forceSetActive,
                FieldInfo rendererField,
                MethodInfo show,
                IReadOnlyList<MethodInfo> showExactly,
                MethodInfo showAll)
            {
                InitState = initState;
                ForceSetActive = forceSetActive;
                RendererField = rendererField;
                Show = show;
                ShowExactly = showExactly;
                ShowAll = showAll;
                Activators = new[] { show, showAll, showExactly[0], showExactly[1] };
            }

            internal MethodInfo InitState { get; }
            internal MethodInfo ForceSetActive { get; }
            internal FieldInfo RendererField { get; }
            internal MethodInfo Show { get; }
            internal IReadOnlyList<MethodInfo> ShowExactly { get; }
            internal MethodInfo ShowAll { get; }
            internal IReadOnlyList<MethodInfo> Activators { get; }
        }
    }
}
