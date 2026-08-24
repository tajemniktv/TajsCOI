// Taj's COI Mods | ManualAssetTrimService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Simulation;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;

namespace TajsCOI.Performance.Features.ManualAssetTrim
{
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class ManualAssetTrimService
    {
        private readonly DependencyResolver m_resolver;
        private readonly SimLoopEvents m_simLoop;
        private readonly ITajsLogger m_log;
        private readonly ITajsRuntime m_runtime;
        private readonly Type? m_assetsDbType;
        private readonly MethodInfo? m_clearCachedAssets;
        private readonly MethodInfo? m_unloadUnusedAssets;
        private readonly PropertyInfo? m_asyncIsDone;

        private object? m_operation;
        private long m_startedTicks;
        private long m_managedBefore;
        private UnityMemorySnapshot m_unityBefore;
        private string m_lastResult = "No manual asset trim has run.";

        public ManualAssetTrimService(
            DependencyResolver resolver,
            SimLoopEvents simLoop,
            ITajsRuntime runtime,
            ITajsSettings settings)
        {
            m_resolver = resolver;
            m_simLoop = simLoop;
            m_runtime = runtime;
            m_log = runtime.GetLogger("TajsPerformance", "ManualAssetTrim");

            PerformanceSettingsCatalog.RegisterAll(settings);
            ManualAssetTrimSettings.Update(
                settings.Get<bool>(PerformanceSettingsCatalog.ModId, ManualAssetTrimSettings.EnableConfigKey));
            settings.Changed += OnSettingChanged;

            Assembly? unityAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, "Mafi.Unity", StringComparison.Ordinal));
            m_assetsDbType = unityAssembly?.GetType("Mafi.Unity.AssetsDb", false);
            m_clearCachedAssets = m_assetsDbType?.GetMethod(
                "ClearCachedAssets",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);

            Type? resources = FindType("UnityEngine.Resources", "UnityEngine.CoreModule");
            m_unloadUnusedAssets = resources?.GetMethod(
                "UnloadUnusedAssets",
                BindingFlags.Static | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            m_asyncIsDone = m_unloadUnusedAssets?.ReturnType.GetProperty("isDone", BindingFlags.Instance | BindingFlags.Public);

            bool compatible = BindingsAreCompatible();
            ReportCompatibility(compatible);
        }

        private void OnSettingChanged(object? sender, SettingChangedEventArgs change)
        {
            if (!string.Equals(change.Descriptor.ModId, PerformanceSettingsCatalog.ModId, StringComparison.Ordinal) ||
                !string.Equals(change.Descriptor.Key, ManualAssetTrimSettings.EnableConfigKey, StringComparison.Ordinal))
            {
                return;
            }

            ManualAssetTrimSettings.Update((bool)change.NewValue);
            bool compatible = BindingsAreCompatible();
            ReportCompatibility(compatible);
        }

        private void ReportCompatibility(bool compatible)
        {
            CompatibilityState state = !ManualAssetTrimSettings.Enabled
                ? CompatibilityState.Disabled
                : compatible ? CompatibilityState.Compatible : CompatibilityState.Disabled;
            m_runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                "ManualAssetTrim",
                state,
                "AssetsDb.ClearCachedAssets plus Resources.UnloadUnusedAssets, invoked manually while paused",
                compatible ? "0.8.7a cache and Unity unload contracts resolved" : "Required cache or Unity unload contract unavailable",
                !ManualAssetTrimSettings.Enabled
                    ? "Disabled by configuration; no automatic trim exists."
                    : compatible
                        ? "Paused-only command available; no periodic trim exists."
                        : "Command is unavailable; no caches or assets will be changed."));
        }

        [ConsoleCommand(
            invokeOnMainThread: true,
            documentation: "Clears CoI's reloadable asset cache and starts Unity unused-asset unloading. Requires paused simulation and explicit config opt-in.",
            customCommandName: "trim_unused_assets")]
        public string TrimUnusedAssets()
        {
            RefreshCompletion();
            if (!ManualAssetTrimSettings.Enabled)
            {
                return "Manual asset trim is disabled. Enable 'TajsPerformance.enable_manual_asset_trim'.";
            }
            if (!m_simLoop.IsSimPaused)
            {
                return "Manual asset trim rejected: pause the simulation first.";
            }
            if (m_operation is not null)
            {
                return "Manual asset trim is already running.";
            }
            if (!BindingsAreCompatible())
            {
                return "Manual asset trim unavailable: required 0.8.7a bindings were not resolved.";
            }

            try
            {
                Type assetsDbType = m_assetsDbType!;
                MethodInfo clearCachedAssets = m_clearCachedAssets!;
                MethodInfo unloadUnusedAssets = m_unloadUnusedAssets!;
                object? assets = m_resolver.TryResolve(assetsDbType).ValueOrNull;
                if (assets is null)
                {
                    return "Manual asset trim unavailable: no active gameplay AssetsDb was resolved.";
                }

                m_managedBefore = GC.GetTotalMemory(false);
                m_unityBefore = ReadUnityMemory();
                m_startedTicks = Stopwatch.GetTimestamp();
                clearCachedAssets.Invoke(assets, null);
                m_operation = unloadUnusedAssets.Invoke(null, null);
                if (m_operation is null)
                {
                    throw new InvalidOperationException("Resources.UnloadUnusedAssets returned no operation.");
                }
                m_lastResult = "Manual asset trim started.";
                return m_lastResult;
            }
            catch (Exception exception)
            {
                m_operation = null;
                m_log.Exception(exception, "Manual asset trim failed; it will not retry automatically.");
                m_lastResult = "Manual asset trim failed: " + Unwrap(exception).Message;
                return m_lastResult;
            }
        }

        [ConsoleCommand(
            invokeOnMainThread: true,
            documentation: "Shows completion and memory deltas for the last explicit unused-asset trim.",
            customCommandName: "trim_unused_assets_status")]
        public string GetStatus()
        {
            RefreshCompletion();
            return m_lastResult;
        }

        private void RefreshCompletion()
        {
            if (m_operation is null || m_asyncIsDone is null)
            {
                return;
            }

            try
            {
                if (!(bool)m_asyncIsDone.GetValue(m_operation))
                {
                    m_lastResult = $"Manual asset trim running for {ElapsedMilliseconds():F1} ms.";
                    return;
                }

                long managedAfter = GC.GetTotalMemory(false);
                UnityMemorySnapshot unityAfter = ReadUnityMemory();
                m_lastResult =
                    $"Manual asset trim completed in {ElapsedMilliseconds():F1} ms; " +
                    $"managed delta={FormatBytes(managedAfter - m_managedBefore)}, " +
                    $"Unity allocated/reserved/graphics delta=" +
                    $"{FormatOptionalDelta(m_unityBefore.Allocated, unityAfter.Allocated)}/" +
                    $"{FormatOptionalDelta(m_unityBefore.Reserved, unityAfter.Reserved)}/" +
                    $"{FormatOptionalDelta(m_unityBefore.Graphics, unityAfter.Graphics)}.";
                m_operation = null;
            }
            catch (Exception exception)
            {
                m_operation = null;
                m_log.Exception(exception, "Manual asset trim status inspection failed.");
                m_lastResult = "Manual asset trim status unavailable: " + Unwrap(exception).Message;
            }
        }

        private bool BindingsAreCompatible()
        {
            MethodInfo? clear = m_clearCachedAssets;
            MethodInfo? unload = m_unloadUnusedAssets;
            PropertyInfo? isDone = m_asyncIsDone;
            MethodInfo? isDoneGetter = isDone?.GetGetMethod(false);
            return m_assetsDbType is not null &&
                clear is { IsPublic: true, IsStatic: false } && clear.ReturnType == typeof(void) &&
                clear.GetParameters().Length == 0 &&
                unload is { IsPublic: true, IsStatic: true } && unload.GetParameters().Length == 0 &&
                string.Equals(unload.ReturnType.FullName, "UnityEngine.AsyncOperation", StringComparison.Ordinal) &&
                isDone is not null && isDone.PropertyType == typeof(bool) &&
                isDoneGetter is { IsPublic: true, IsStatic: false } && isDoneGetter.GetParameters().Length == 0;
        }

        private double ElapsedMilliseconds() =>
            (Stopwatch.GetTimestamp() - m_startedTicks) * 1000.0 / Stopwatch.Frequency;

        private static UnityMemorySnapshot ReadUnityMemory()
        {
            try
            {
                Type? profiler = FindType("UnityEngine.Profiling.Profiler", "UnityEngine.CoreModule");
                return profiler is null
                    ? UnityMemorySnapshot.Unavailable
                    : new UnityMemorySnapshot(
                        InvokeLong(profiler, "GetTotalAllocatedMemoryLong"),
                        InvokeLong(profiler, "GetTotalReservedMemoryLong"),
                        InvokeLong(profiler, "GetAllocatedMemoryForGraphicsDriver"));
            }
            catch
            {
                return UnityMemorySnapshot.Unavailable;
            }
        }

        private static long InvokeLong(Type type, string methodName) =>
            Convert.ToInt64(type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null));

        private static Type? FindType(string fullName, string assemblyName) =>
            Type.GetType(fullName + ", " + assemblyName, false) ??
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, assemblyName, StringComparison.Ordinal))
                ?.GetType(fullName, false);

        private static string FormatOptionalDelta(long before, long after) =>
            before < 0 || after < 0 ? "unavailable" : FormatBytes(after - before);

        private static string FormatBytes(long bytes) =>
            (bytes / (1024.0 * 1024.0)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MiB";

        private static Exception Unwrap(Exception exception) =>
            exception is TargetInvocationException invocation && invocation.InnerException is not null
                ? invocation.InnerException
                : exception;

        private readonly struct UnityMemorySnapshot
        {
            internal static readonly UnityMemorySnapshot Unavailable = new(-1, -1, -1);

            internal UnityMemorySnapshot(long allocated, long reserved, long graphics)
            {
                Allocated = allocated;
                Reserved = reserved;
                Graphics = graphics;
            }

            internal long Allocated { get; }
            internal long Reserved { get; }
            internal long Graphics { get; }
        }
    }
}
