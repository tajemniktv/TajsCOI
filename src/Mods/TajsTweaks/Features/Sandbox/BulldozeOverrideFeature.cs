// Taj's COI Mods | BulldozeOverrideFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Validators;

namespace TajsCOI.Tweaks.Features.Sandbox
{
    /// <summary>
    /// Relaxes only ClearingChecker's soft pre-eligibility result.  Command execution still runs
    /// the native EntitiesManager validators, so roads, tracks, ports, settlement modules and
    /// every other hard invariant retain their normal cleanup/pathability checks.
    /// </summary>
    internal static class BulldozeOverrideFeature
    {
        internal const string HarmonyId = "TajsCOI.Tweaks.Sandbox.Bulldoze";

        private static readonly HashSet<string> s_hardInvariantTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "Road", "TrainTrack", "TransportPillar", "TrainDepot", "SettlementSquare", "SettlementSquareModule",
            "Bridge", "Farm", "CargoDepot", "CargoShip", "Shipyard", "Rocket", "NuclearReactor", "Reactor", "Ruins",
        };

        internal static void Install(Harmony harmony)
        {
            MethodInfo? target = typeof(ClearingChecker)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name.IndexOf("IEntityRemovalValidator", StringComparison.Ordinal) >= 0 &&
                                          method.Name.EndsWith("CanRemove", StringComparison.Ordinal) &&
                                          method.GetParameters().Length == 2);
            if (target is null)
            {
                throw new MissingMethodException(typeof(ClearingChecker).FullName, "IEntityRemovalValidator<IStaticEntity>.CanRemove");
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(BulldozeOverrideFeature), nameof(CanRemovePostfix)));
        }

        private static void CanRemovePostfix(
            IStaticEntity entity,
            EntityRemoveReason reason,
            ref EntityValidationResult __result)
        {
            if (!TajsTweaksRuntimeState.SandboxAlwaysAllowBulldoze ||
                reason == EntityRemoveReason.Collapse ||
                __result.IsSuccess ||
                !IsWhitelisted(entity))
            {
                return;
            }

            // Only the soft pre-check is replaced.  Dangerous/hard-invariant classes are never
            // eligible for this bypass and therefore still require the native confirmation path.
            if (RequiresNativeConfirmation(entity.GetType()))
            {
                return;
            }

            __result = EntityValidationResult.Success;
        }

        internal static bool IsWhitelisted(IStaticEntity? entity)
        {
            if (entity is null)
            {
                return false;
            }
            return IsWhitelistedType(entity.GetType(), TajsTweaksRuntimeState.SandboxBulldozeWhitelist);
        }

        internal static bool IsWhitelistedType(Type type, string? configured)
        {
            if (type is null || string.IsNullOrWhiteSpace(configured) || RequiresNativeConfirmation(type))
            {
                return false;
            }

            string fullName = type.FullName ?? type.Name;
            foreach (string token in configured!.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = token.Trim();
                if (candidate.Length > 0 && (string.Equals(candidate, type.Name, StringComparison.Ordinal) ||
                                             string.Equals(candidate, fullName, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RequiresNativeConfirmation(Type type)
        {
            string name = type.Name;
            return s_hardInvariantTokens.Any(token => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
