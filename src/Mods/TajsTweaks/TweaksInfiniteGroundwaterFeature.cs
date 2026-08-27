// Taj's COI Mods | TweaksInfiniteGroundwaterFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi;
using Mafi.Core;
using Mafi.Core.Environment;
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
    ///     Owns the optional groundwater policy without putting a feature service or callback
    ///     into a save. GroundWaterManager remains the native authority for weather-driven
    ///     replenishment; this owner only adds a bounded amount at the calendar boundary.
    /// </summary>
    internal sealed class TweaksInfiniteGroundwaterFeature : IDisposable
    {
        private const string GroundwaterProductId = "Product_Virtual_Groundwater";
        private readonly DependencyResolver m_resolver;
        private readonly IGameLoopEvents m_gameLoop;
        private readonly ITajsLogger m_log;
        private ICalendar? m_calendar;
        private GroundWaterManager? m_groundWaterManager;
        private VirtualResourceManager? m_virtualResources;
        private VirtualResourceProductProto? m_groundwaterProto;
        private bool m_initStateRegistered;
        private bool m_subscribedToNewDay;
        private bool m_reportedUnavailable;
        private bool m_reportedFailure;
        private int? m_lastAutomaticDay;

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
            GroundwaterPolicy policy = TajsTweaksRuntimeState.GroundwaterPolicyMode;
            if (!GroundwaterPolicyRules.UsesAutomaticCallback(policy))
            {
                UnsubscribeFromNewDay();
                m_lastAutomaticDay = null;
                return;
            }

            // A live policy change is an explicit request and may legitimately apply once on
            // the current game date. The date guard still prevents duplicate same-day callbacks.
            m_lastAutomaticDay = null;
            if (EnsureDependencies())
            {
                SubscribeToNewDay();
                ApplyAutomaticSafely("setting changed");
            }
        }

        private void OnGameInitState()
        {
            if (GroundwaterPolicyRules.UsesAutomaticCallback(TajsTweaksRuntimeState.GroundwaterPolicyMode) &&
                EnsureDependencies())
            {
                SubscribeToNewDay();
                ApplyAutomaticSafely("game init state");
            }
        }

        private void OnNewDay()
        {
            if (GroundwaterPolicyRules.UsesAutomaticCallback(TajsTweaksRuntimeState.GroundwaterPolicyMode))
            {
                ApplyAutomaticSafely("new day");
            }
        }

        private bool EnsureDependencies()
        {
            if (m_calendar is not null &&
                m_groundWaterManager is not null &&
                m_virtualResources is not null &&
                m_groundwaterProto is not null)
            {
                return true;
            }

            try
            {
                // Resolve the native manager as a scene readiness/ownership check. Its private
                // resource manager remains authoritative; this feature never creates a parallel
                // manager or replaces the native weather callback.
                if (!m_resolver.TryResolve(out ICalendar calendar) ||
                    !m_resolver.TryResolve(out GroundWaterManager groundWaterManager) ||
                    !m_resolver.TryResolve(out VirtualResourceManager virtualResources) ||
                    !m_resolver.TryResolve(out ProtosDb protos) ||
                    !protos.TryGetProto<VirtualResourceProductProto>(
                        new ProductProto.ID(GroundwaterProductId),
                        out VirtualResourceProductProto groundwaterProto))
                {
                    if (!m_reportedUnavailable)
                    {
                        m_log.WarningOnce(
                            "Groundwater policy is enabled, but the native groundwater services are not ready; " +
                            "the feature will retry at the next game lifecycle event.");
                        m_reportedUnavailable = true;
                    }
                    return false;
                }

                m_calendar = calendar;
                m_groundWaterManager = groundWaterManager;
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
                        "Groundwater policy dependencies are not ready; the feature will retry later.");
                    m_reportedUnavailable = true;
                }

                return false;
            }
        }

        private void SubscribeToNewDay()
        {
            if (m_subscribedToNewDay || m_calendar is null)
            {
                return;
            }

            try
            {
                m_calendar.NewDay.AddNonSaveable(this, OnNewDay);
                m_subscribedToNewDay = true;
            }
            catch (Exception exception)
            {
                if (!m_reportedUnavailable)
                {
                    m_log.Exception(
                        exception,
                        "Groundwater calendar callback was unavailable; vanilla behavior remains active.");
                    m_reportedUnavailable = true;
                }
            }
        }

        private void UnsubscribeFromNewDay()
        {
            if (m_subscribedToNewDay && m_calendar is not null)
            {
                try
                {
                    m_calendar.NewDay.RemoveNonSaveable(this, OnNewDay);
                }
                catch (Exception exception)
                {
                    m_log.Exception(exception, "Groundwater calendar callback cleanup failed open.");
                }
            }

            m_subscribedToNewDay = false;
        }

        private void ApplyAutomaticSafely(string reason)
        {
            if (!EnsureDependencies() || m_calendar is null)
            {
                return;
            }

            int gameDay = m_calendar.CurrentDate.Value;
            if (!GroundwaterPolicyRules.ShouldApplyAutomatic(
                    TajsTweaksRuntimeState.GroundwaterPolicyMode,
                    m_lastAutomaticDay,
                    gameDay))
            {
                return;
            }

            if (ReplenishSafely(reason, forceFull: false, out _))
            {
                m_lastAutomaticDay = gameDay;
            }
        }

        /// <summary>
        ///     Refills all deposits to their current native capacity from a sandbox command. This
        ///     path is intentionally separate from the automatic policy and never uses wall time.
        /// </summary>
        internal string ManualRefill()
        {
            if (!EnsureDependencies())
            {
                return "Groundwater services are unavailable in this scene; no refill was performed.";
            }

            try
            {
                if (!m_resolver.TryResolve(out SandboxManager sandbox) || !sandbox.CanCheat)
                {
                    return "Manual groundwater refill is available only in sandbox mode.";
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Sandbox groundwater command was unavailable; no refill was performed.");
                return "Sandbox groundwater services are unavailable in this scene; no refill was performed.";
            }

            if (!ReplenishSafely("manual sandbox command", forceFull: true, out int refilled))
            {
                return "Groundwater refill failed open; vanilla groundwater behavior remains active.";
            }

            return "Sandbox groundwater refill completed for " + refilled + " deposit(s).";
        }

        private bool ReplenishSafely(string reason, bool forceFull, out int refilled)
        {
            refilled = 0;
            if (!EnsureDependencies() || m_virtualResources is null || m_groundwaterProto is null)
            {
                return false;
            }

            try
            {
                GroundwaterPolicy policy = forceFull
                    ? GroundwaterPolicy.Infinite
                    : TajsTweaksRuntimeState.GroundwaterPolicyMode;
                foreach (IVirtualTerrainResource resource in m_virtualResources.GetAllResourcesFor(m_groundwaterProto))
                {
                    int dailyAmount = resource.ConfiguredCapacity
                        .ScaledBy(TajsTweaksRuntimeState.GroundwaterRegenerationPercent.Percent())
                        .Value;
                    int amount = GroundwaterPolicyRules.CalculateRefill(
                        resource.Quantity.Value,
                        resource.Capacity.Value,
                        policy,
                        dailyAmount,
                        TajsTweaksRuntimeState.GroundwaterMinimumPercent);
                    if (amount > 0)
                    {
                        resource.AddAsMuchAs(new Quantity(amount));
                        refilled++;
                    }
                }

                if (reason != "new day")
                {
                    m_log.Info(
                        "Groundwater policy " + GroundwaterPolicyRules.ToSettingValue(policy) +
                        " refilled " + refilled + " deposit(s) at " + reason + ".");
                }
                m_reportedFailure = false;
                return true;
            }
            catch (Exception exception)
            {
                if (!m_reportedFailure)
                {
                    m_log.Exception(
                        exception,
                        "Groundwater policy refill failed open; vanilla groundwater behavior remains active.");
                    m_reportedFailure = true;
                }

                return false;
            }
        }

        public void Dispose()
        {
            UnsubscribeFromNewDay();
            m_calendar = null;
            m_groundWaterManager = null;
            m_virtualResources = null;
            m_groundwaterProto = null;
            m_lastAutomaticDay = null;
        }
    }
}
