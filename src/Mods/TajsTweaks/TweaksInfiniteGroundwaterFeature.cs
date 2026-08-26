// Taj's COI Mods | TweaksInfiniteGroundwaterFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Core.GameLoop;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Generation;
using TajsCOI.Common.Logging;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Refills virtual groundwater without putting a service instance into a save. The old
    ///     standalone mod serialized an empty object and then continued receiving calendar events
    ///     after a load, leaving its resolver-backed fields null.
    /// </summary>
    internal sealed class TweaksInfiniteGroundwaterFeature : IDisposable
    {
        private const string GroundwaterProductId = "Product_Virtual_Groundwater";
        private const string LegacyReplenisherTypeName =
            "InfiniteGroundwater.InfiniteGroundwaterReplenisher, InfiniteGroundwater";

        private readonly DependencyResolver m_resolver;
        private readonly IGameLoopEvents m_gameLoop;
        private readonly ITajsLogger m_log;
        private ICalendar? m_calendar;
        private VirtualResourceManager? m_virtualResources;
        private VirtualResourceProductProto? m_groundwaterProto;
        private bool m_initStateRegistered;
        private bool m_subscribedToNewDay;
        private bool m_reportedUnavailable;
        private bool m_reportedFailure;

        internal TweaksInfiniteGroundwaterFeature(
            DependencyResolver resolver,
            IGameLoopEvents gameLoop,
            ITajsLogger log)
        {
            m_resolver = resolver;
            m_gameLoop = gameLoop;
            m_log = log;
        }

        internal void Install()
        {
            if (!m_initStateRegistered)
            {
                m_gameLoop.RegisterInitState(this, OnGameInitState);
                m_initStateRegistered = true;
            }

            RefreshFromSettings();
        }

        internal void RefreshFromSettings()
        {
            if (!TajsTweaksRuntimeState.InfiniteGroundwater)
            {
                UnsubscribeFromNewDay();
                return;
            }

            if (EnsureDependencies())
            {
                SubscribeToNewDay();
                ReplenishSafely("setting enabled");
            }
        }

        /// <summary>
        ///     Removes the old standalone mod's saveable callback and resolver object while that
        ///     assembly is still loaded. This is deliberately command-driven: the caller must save
        ///     a new copy and only then remove the standalone mod.
        /// </summary>
        internal string DetachLegacyStandaloneMod()
        {
            Type? legacyType = Type.GetType(LegacyReplenisherTypeName, false);
            if (legacyType is null)
            {
                return "Legacy InfiniteGroundwater is not loaded; no migration was performed.";
            }

            object? legacyReplenisher = m_resolver.TryResolve(legacyType).ValueOrNull;
            if (legacyReplenisher is null)
            {
                return "Legacy InfiniteGroundwater is loaded but its replenisher is not resolved; no migration was performed.";
            }

            try
            {
                FieldInfo? calendarField = legacyType.GetField("m_calendar", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo? callbackMethod = legacyType.GetMethod(
                    "onNewDay",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (calendarField is null || callbackMethod is null || callbackMethod.GetParameters().Length != 0)
                {
                    return "Legacy InfiniteGroundwater has an unsupported callback shape; no migration was performed.";
                }

                if (calendarField.GetValue(legacyReplenisher) is not ICalendar calendar)
                {
                    return "Legacy InfiniteGroundwater has no active calendar; no migration was performed.";
                }

                var callback = (Action)Delegate.CreateDelegate(typeof(Action), legacyReplenisher, callbackMethod);
                IEvent newDay = calendar.NewDay;
                MethodInfo? isAddedDefinition = FindEventMethod("IsAdded");
                MethodInfo? removeDefinition = FindEventMethod("Remove");
                if (isAddedDefinition is null || removeDefinition is null)
                {
                    return "The 0.8.7b event API was not available; no migration was performed.";
                }

                MethodInfo isAdded = isAddedDefinition.MakeGenericMethod(legacyType);
                MethodInfo remove = removeDefinition.MakeGenericMethod(legacyType);
                bool callbackRegistered = (bool)(isAdded.Invoke(newDay, new object[] { legacyReplenisher, callback }) ?? false);
                if (callbackRegistered)
                {
                    remove.Invoke(newDay, new object[] { legacyReplenisher, callback });
                }

                if (!RemoveLegacyResolverEntries(legacyType, legacyReplenisher, out string cleanupFailure))
                {
                    return "The legacy event was detached, but resolver cleanup failed (" + cleanupFailure + "). " +
                           "Do not save; keep InfiniteGroundwater enabled and report this result.";
                }

                m_log.Info("Detached the legacy InfiniteGroundwater saveable callback and resolver object.");
                return callbackRegistered
                    ? "Legacy InfiniteGroundwater detached. Save a new copy now, then disable the standalone mod."
                    : "Legacy InfiniteGroundwater was already detached. Save a new copy, then disable the standalone mod.";
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Legacy InfiniteGroundwater migration failed; no safe save was produced.");
                return "Legacy InfiniteGroundwater migration failed; do not save this instance. See the log for details.";
            }
        }

        private void OnGameInitState()
        {
            if (!TajsTweaksRuntimeState.InfiniteGroundwater || !EnsureDependencies())
            {
                return;
            }

            SubscribeToNewDay();
            ReplenishSafely("game init state");
        }

        private void OnNewDay()
        {
            if (TajsTweaksRuntimeState.InfiniteGroundwater)
            {
                ReplenishSafely("new day");
            }
        }

        private bool EnsureDependencies()
        {
            if (m_calendar is not null && m_virtualResources is not null && m_groundwaterProto is not null)
            {
                return true;
            }

            if (!m_resolver.TryResolve(out ICalendar calendar) ||
                !m_resolver.TryResolve(out VirtualResourceManager virtualResources) ||
                !m_resolver.TryResolve(out ProtosDb protos) ||
                !protos.TryGetProto<VirtualResourceProductProto>(
                    new ProductProto.ID(GroundwaterProductId),
                    out VirtualResourceProductProto groundwaterProto))
            {
                if (!m_reportedUnavailable)
                {
                    m_log.WarningOnce(
                        "Infinite groundwater is enabled, but the virtual-resource services are not ready; " +
                        "the feature will retry at the next game lifecycle event.");
                    m_reportedUnavailable = true;
                }
                return false;
            }

            m_calendar = calendar;
            m_virtualResources = virtualResources;
            m_groundwaterProto = groundwaterProto;
            m_reportedUnavailable = false;
            return true;
        }

        private void SubscribeToNewDay()
        {
            if (!m_subscribedToNewDay && m_calendar is not null)
            {
                m_calendar.NewDay.AddNonSaveable(this, OnNewDay);
                m_subscribedToNewDay = true;
            }
        }

        private void UnsubscribeFromNewDay()
        {
            if (m_subscribedToNewDay && m_calendar is not null)
            {
                m_calendar.NewDay.RemoveNonSaveable(this, OnNewDay);
            }

            m_subscribedToNewDay = false;
        }

        private void ReplenishSafely(string reason)
        {
            if (!EnsureDependencies() ||
                m_virtualResources is null ||
                m_groundwaterProto is null)
            {
                return;
            }

            try
            {
                int refilled = 0;
                foreach (IVirtualTerrainResource resource in m_virtualResources.GetAllResourcesFor(m_groundwaterProto))
                {
                    Quantity deficit = resource.Capacity - resource.Quantity;
                    if (deficit > Quantity.Zero)
                    {
                        resource.AddAsMuchAs(deficit);
                        refilled++;
                    }
                }

                if (reason != "new day")
                {
                    m_log.Info("Infinite groundwater refilled " + refilled + " deposit(s) at " + reason + ".");
                }
            }
            catch (Exception exception)
            {
                if (!m_reportedFailure)
                {
                    m_log.Exception(
                        exception,
                        "Infinite groundwater refill failed open; vanilla groundwater behavior remains active.");
                    m_reportedFailure = true;
                }
            }
        }

        private static MethodInfo? FindEventMethod(string name)
        {
            foreach (MethodInfo method in typeof(IEvent).GetMethods())
            {
                if (method.Name == name && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                    method.GetParameters().Length == 2)
                {
                    return method;
                }
            }

            return null;
        }

        private bool RemoveLegacyResolverEntries(Type legacyType, object legacyReplenisher, out string failure)
        {
            failure = string.Empty;
            FieldInfo? registeredField = typeof(DependencyResolver).GetField(
                "m_resolvedInstancesByRegisteredType",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? realField = typeof(DependencyResolver).GetField(
                "m_resolvedInstancesByRealType",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? objectsField = typeof(DependencyResolver).GetField(
                "m_resolvedObjects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object? registeredMap = registeredField?.GetValue(m_resolver);
            object? realMap = realField?.GetValue(m_resolver);
            object? resolvedObjects = objectsField?.GetValue(m_resolver);
            MethodInfo? registeredRemove = registeredMap?.GetType().GetMethod("Remove", new[] { typeof(Type) });
            MethodInfo? realRemove = realMap?.GetType().GetMethod("Remove", new[] { typeof(Type) });
            MethodInfo? objectRemove = resolvedObjects?.GetType().GetMethod("Remove", new[] { typeof(object) });
            if (registeredMap is null || realMap is null || resolvedObjects is null ||
                registeredRemove is null || realRemove is null || objectRemove is null)
            {
                failure = "resolver compatibility fields are unavailable";
                return false;
            }

            if (!RemoveMapEntry(registeredMap, registeredRemove, legacyType, out failure) ||
                !RemoveMapEntry(realMap, realRemove, legacyType, out failure) ||
                !RemoveObjectEntry(resolvedObjects, objectRemove, legacyReplenisher, out failure))
            {
                return false;
            }

            return true;
        }

        private static bool RemoveMapEntry(
            object map,
            MethodInfo remove,
            Type key,
            out string failure)
        {
            failure = string.Empty;
            bool removed = (bool)(remove.Invoke(map, new object[] { key }) ?? false);
            return removed || !ContainsMapKey(map, key);
        }

        private static bool RemoveObjectEntry(
            object collection,
            MethodInfo remove,
            object value,
            out string failure)
        {
            failure = string.Empty;
            bool removed = (bool)(remove.Invoke(collection, new[] { value }) ?? false);
            return removed || !ContainsObject(collection, value);
        }

        private static bool ContainsMapKey(object map, Type key)
        {
            if (map is not IEnumerable entries)
            {
                return false;
            }

            foreach (object? entry in entries)
            {
                if (entry is null)
                {
                    continue;
                }

                PropertyInfo? keyProperty = entry.GetType().GetProperty("Key");
                if (ReferenceEquals(keyProperty?.GetValue(entry), key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsObject(object collection, object value)
        {
            if (collection is not IEnumerable entries)
            {
                return false;
            }

            foreach (object? entry in entries)
            {
                if (ReferenceEquals(entry, value))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            UnsubscribeFromNewDay();
            m_calendar = null;
            m_virtualResources = null;
            m_groundwaterProto = null;
        }
    }
}
