// Taj's COI Mods | ProgressionSandboxFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.PropertiesDb;

namespace TajsCOI.Tweaks.Features.ProgressionSandbox
{
    internal static class ProgressionSandboxFeature
    {
        internal const string ResearchOwner = "TajsCOI.Tweaks.Sandbox.Progression.Research";
        internal const string ConstructionOwner = "TajsCOI.Tweaks.Sandbox.Progression.Construction";

        internal static void ApplyNativeProperties(IPropertiesDb propertiesDb)
        {
            ApplyToggle(
                propertiesDb.GetProperty(IdsCore.PropertyIds.ResearchStepsMultiplier),
                TajsTweaksRuntimeState.SandboxFreeResearch,
                ResearchOwner);
            ApplyToggle(
                propertiesDb.GetProperty(IdsCore.PropertyIds.ConstructionCostsMultiplier),
                TajsTweaksRuntimeState.SandboxNoConstructionCosts,
                ConstructionOwner);
        }

        /// <summary>
        /// Receives construction-manager state notifications after normal command processing.
        /// The caller supplies only entities reported by the manager; no scene-wide entity walk
        /// or saveable callback is used.
        /// </summary>
        internal sealed class DesignModeCoordinator
        {
            private readonly IConstructionManager m_manager;
            private readonly HashSet<IStaticEntity> m_pending = new();

            internal DesignModeCoordinator(IConstructionManager manager)
            {
                m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            }

            internal bool IsActive => TajsTweaksRuntimeState.SandboxDesignMode;

            internal void Observe(IStaticEntity entity, ConstructionState state)
            {
                if (!IsActive || entity is null || (state != ConstructionState.InConstruction && state != ConstructionState.PendingDeconstruction && state != ConstructionState.InDeconstruction))
                {
                    return;
                }
                m_pending.Add(entity);
            }

            internal int Flush()
            {
                if (!IsActive || m_pending.Count == 0)
                {
                    return 0;
                }

                int finalized = 0;
                foreach (IStaticEntity entity in m_pending)
                {
                    if (entity.IsDestroyed)
                    {
                        continue;
                    }

                    try
                    {
                        if (entity.ConstructionState == ConstructionState.InConstruction)
                        {
                            m_manager.MarkConstructed(entity);
                            finalized++;
                        }
                else if (entity.ConstructionState == ConstructionState.InDeconstruction)
                        {
                            m_manager.MarkDeconstructed(entity, entityRemoveReason: EntityRemoveReason.Remove);
                            finalized++;
                        }
                    }
                    catch
                    {
                        // A manager-specific invariant (occupied track, settlement module, etc.)
                        // remains authoritative; leave the entity untouched for vanilla handling.
                    }
                }

                m_pending.Clear();
                return finalized;
            }

            internal string IndicatorText => IsActive ? "DESIGN MODE — instant construction/deconstruction" : string.Empty;
        }

        private static void ApplyToggle(IProperty<Percent> property, bool disabled, string owner)
        {
            if (disabled)
            {
                property.AddOrSetModifier(owner, (-100).Percent(), Property<Percent>.BASE_GROUP);
            }
            else
            {
                property.TryRemoveModifier(owner);
            }
        }
    }
}
