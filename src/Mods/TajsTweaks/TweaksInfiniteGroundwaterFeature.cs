// Taj's COI Mods | TweaksInfiniteGroundwaterFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
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

            try
            {
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
            catch (Exception exception)
            {
                if (!m_reportedUnavailable)
                {
                    m_log.Exception(
                        exception,
                        "Infinite groundwater dependencies are not ready; the feature will retry later.");
                    m_reportedUnavailable = true;
                }

                return false;
            }
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

        public void Dispose()
        {
            UnsubscribeFromNewDay();
            m_calendar = null;
            m_virtualResources = null;
            m_groundwaterProto = null;
        }
    }
}
