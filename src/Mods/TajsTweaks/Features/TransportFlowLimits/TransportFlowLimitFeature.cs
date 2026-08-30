// Taj's COI Mods | TransportFlowLimitFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Input;
using Mafi.Core.Ports;
using Mafi.Core.Ports.Io;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using TajsCOI.Common.Tuning;

namespace TajsCOI.Tweaks.Features.TransportFlowLimits
{
    /// <summary>
    ///     Per-entity token-bucket flow limits for existing ordinary transports. The patch is on
    ///     the transport's narrow native receive seam, so the game's product ordering,
    ///     connectivity checks, and backpressure remain authoritative. Sandbox source/sink
    ///     content is intentionally outside this feature's whitelist.
    /// </summary>
    internal static class TransportFlowLimitFeature
    {
        internal const string ConfigKey = "TajsTweaks.TransportFlowLimit";
        internal const string HarmonyId = "TajsCOI.Tweaks.TransportFlowLimits";

        private static bool s_patchInstalled;
        private static IInputScheduler? s_inputScheduler;

        internal static bool IsInstalled => s_patchInstalled;

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            if (harmony is null)
            {
                throw new ArgumentNullException(nameof(harmony));
            }

            if (resolver is null || !resolver.TryResolve(out ISimLoopEvents? simLoopEvents) || simLoopEvents is null)
            {
                throw new InvalidOperationException("ISimLoopEvents is unavailable for transport flow limits.");
            }

            resolver.TryResolve(out s_inputScheduler);

            if (resolver.TryResolve(out ISaveManager? saveManager) && saveManager is not null)
            {
                TransportFlowLimitState.LoadForSave(saveManager.GameName);
            }

            // Harmony owners are process-lived while policies are save-scoped. Rebinding state on
            // each gameplay scene is enough; registering the same patch repeatedly would create
            // duplicate reservations after a return to the main menu.
            if (s_patchInstalled)
            {
                return;
            }

            MethodInfo target = FindReceiveMethod();
            FieldInfo? simLoopField = typeof(Transport).GetField(
                "m_simLoopEvents",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (simLoopField is null || !typeof(ISimLoopEvents).IsAssignableFrom(simLoopField.FieldType))
            {
                throw new MissingFieldException(typeof(Transport).FullName, "m_simLoopEvents");
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(TransportFlowLimitFeature), nameof(ReceivePrefix)),
                    postfix: new HarmonyMethod(typeof(TransportFlowLimitFeature), nameof(ReceivePostfix)),
                    finalizer: new HarmonyMethod(typeof(TransportFlowLimitFeature), nameof(ReceiveFinalizer)));
            }
            catch
            {
                // Remove any partial transfer owner state because this resolver-backed installer
                // does not have a generic rollback wrapper.
                try
                {
                    harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
                }
                catch
                {
                }
                throw;
            }

            try
            {
                TransportFlowLimitInspectorPatch.Install(harmony);
            }
            catch
            {
                // The inspector is optional; retain the transfer seam when UI signatures move.
            }
            s_patchInstalled = true;
        }

        internal static void Reset()
        {
            s_inputScheduler = null;
            TransportFlowLimitInspectorPatch.Reset();
            TransportFlowLimitState.UnbindSave();
        }

        internal static bool TryGetConfiguredLimit(int entityId, out double unitsPerSimulationSecond) =>
            TransportFlowLimitState.TryGetLimit(entityId, out unitsPerSimulationSecond);

        internal static bool TrySetConfiguredLimit(int entityId, double unitsPerSimulationSecond) =>
            TransportFlowLimitState.TrySetLimit(entityId, unitsPerSimulationSecond);

        internal static bool QueueSetConfiguredLimit(int entityId, double unitsPerSimulationSecond)
        {
            if (entityId < 0 || double.IsNaN(unitsPerSimulationSecond) ||
                double.IsInfinity(unitsPerSimulationSecond) || unitsPerSimulationSecond < 0d ||
                unitsPerSimulationSecond > TransportFlowLimitState.MaxLimitUnitsPerSecond ||
                s_inputScheduler is null)
            {
                return false;
            }

            s_inputScheduler.ScheduleInputCmd(
                new TransportFlowLimitCmd(new EntityId(entityId), unitsPerSimulationSecond));
            return true;
        }

        internal static bool ClearConfiguredLimit(int entityId) => TransportFlowLimitState.ClearLimit(entityId);

        internal static string DescribeConfiguredLimit(int entityId)
        {
            return TryGetConfiguredLimit(entityId, out double limit)
                ? "Flow limit " + limit.ToString("0.###", CultureInfo.InvariantCulture) + " units/s"
                : "Flow limit unlimited";
        }

        /// <summary>
        ///     Configuration-pipeline projection used by blueprint/copy handlers. Only positive
        ///     limits are emitted; absence means native/unlimited and is therefore preserved when
        ///     a destination has no copied policy.
        /// </summary>
        internal static IReadOnlyDictionary<string, object> ReadBlueprintValues(object runtimeEntity)
        {
            if (runtimeEntity is not Transport transport ||
                !TryGetConfiguredLimit(transport.Id.Value, out double limit))
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [ConfigKey] = limit,
            };
        }

        /// <summary>
        ///     Applies a copied value to a newly configured transport. The entity is resolved by
        ///     the native blueprint pipeline, and only value data crosses the copy boundary.
        /// </summary>
        internal static bool ApplyBlueprintValues(object runtimeEntity, IReadOnlyDictionary<string, object> values)
        {
            if (runtimeEntity is not Transport transport || values is null ||
                !values.TryGetValue(ConfigKey, out object? raw))
            {
                return false;
            }

            if (!TryReadFiniteDouble(raw, out double limit) ||
                limit < 0d || limit > TransportFlowLimitState.MaxLimitUnitsPerSecond)
            {
                return false;
            }

            return TrySetConfiguredLimit(transport.Id.Value, limit);
        }

        private static MethodInfo FindReceiveMethod()
        {
            InterfaceMapping map = typeof(Transport).GetInterfaceMap(typeof(IEntityWithPorts));
            MethodInfo? target = map.InterfaceMethods
                .Select((method, index) => new { method, target = map.TargetMethods[index] })
                .Where(pair => pair.method.Name == nameof(IEntityWithPorts.ReceiveAsMuchAsFromPort))
                .Select(pair => pair.target)
                .FirstOrDefault();
            if (target is null || target.ReturnType != typeof(Quantity))
            {
                throw new MissingMethodException(
                    typeof(Transport).FullName,
                    nameof(IEntityWithPorts.ReceiveAsMuchAsFromPort));
            }

            ParameterInfo[] parameters = target.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(ProductQuantity) ||
                parameters[1].ParameterType != typeof(IoPortToken))
            {
                throw new MissingMethodException(
                    typeof(Transport).FullName,
                    nameof(IEntityWithPorts.ReceiveAsMuchAsFromPort) + "(ProductQuantity, IoPortToken)");
            }

            return target;
        }

        private static bool ReceivePrefix(
            Transport __instance,
            ref ProductQuantity pq,
            ref Quantity __result,
            out TransportFlowLimitState.Reservation __state,
            ISimLoopEvents ___m_simLoopEvents)
        {
            __state = default;
            if (__instance is null || pq.IsEmpty || ___m_simLoopEvents is null)
            {
                return true;
            }

            int requested = pq.Quantity.Value;
            if (requested <= 0 ||
                !TransportFlowLimitState.TryReserve(
                    __instance.Id.Value,
                    requested,
                    ___m_simLoopEvents.CurrentStep.Value,
                    out int allowed,
                    out __state))
            {
                return true;
            }

            if (allowed <= 0)
            {
                // Skipping the native call is essential here: mutating pq to zero and allowing
                // the original method to return zero would make IoPortData believe the original
                // request was fully accepted and silently drop the product.
                __result = new Quantity(requested);
                return false;
            }

            // Returning an empty quantity is a native no-op. The source IoPort sees the full
            // unoffered remainder below and naturally retries later, preserving backpressure
            // without dropping or truncating cargo.
            pq = pq.WithNewQuantity(new Quantity(allowed));
            return true;
        }

        private static void ReceivePostfix(
            Transport __instance,
            ref Quantity __result,
            TransportFlowLimitState.Reservation __state)
        {
            if (__instance is not null && !__state.IsEmpty)
            {
                // Transport.ReceiveAsMuchAsFromPort returns the unaccepted remainder (the
                // native IEntityWithPorts contract), while the bucket completion API consumes
                // the amount actually accepted.
                int remainder = Math.Max(0, Math.Min(__state.Reserved, __result.Value));
                int unoffered = Math.Max(0, __state.Requested - __state.Reserved);
                __result = new Quantity(unoffered + remainder);
                TransportFlowLimitState.CompleteReservation(
                    __instance.Id.Value,
                    __state,
                    __state.Reserved - remainder);
            }
        }

        private static Exception? ReceiveFinalizer(
            Exception? __exception,
            Transport __instance,
            TransportFlowLimitState.Reservation __state)
        {
            if (__exception is not null && __instance is not null && !__state.IsEmpty)
            {
                // Never swallow a vanilla exception; only return the reservation to avoid a
                // failed native call consuming the per-transport budget.
                TransportFlowLimitState.RefundReservation(__instance.Id.Value, __state);
            }

            return __exception;
        }

        private static bool TryReadFiniteDouble(object? value, out double result)
        {
            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
            catch (Exception exception) when (
                exception is FormatException || exception is InvalidCastException || exception is OverflowException)
            {
                result = 0d;
                return false;
            }
        }
    }

    /// <summary>
    ///     Common-only read interface implementation for profiler/diagnostic consumers. It has no
    ///     mutation methods and therefore cannot become a second policy owner.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class TransportFlowLimitReader : ITransportFlowLimitReader
    {
        public bool TryGetConfiguredLimit(int entityId, out double unitsPerSimulationSecond) =>
            TransportFlowLimitFeature.TryGetConfiguredLimit(entityId, out unitsPerSimulationSecond);
    }
}
