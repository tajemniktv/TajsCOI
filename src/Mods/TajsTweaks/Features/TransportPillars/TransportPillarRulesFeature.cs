// Taj's COI Mods | TransportPillarRulesFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Lifts;
using Mafi.Core.Factory.Sorters;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.Zippers;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Trains;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Applies bounded, restart-scoped pillar rules at the native support/check seams.
    ///     The game remains the owner of prototype registration, terrain occupancy, support
    ///     propagation, construction commands, and rendering.
    /// </summary>
    internal static class TransportPillarRulesFeature
    {
        internal const int VanillaTransportSupportRadius = 4;
        internal const int VanillaTransportPillarHeight = 6;
        internal const int VanillaTrainPillarHeight = 6;
        internal const int VanillaTrainSupportDistance = 7;

        internal const int MaxConfiguredSupportRadius = 16;
        internal const int MaxConfiguredPillarHeight = 16;
        internal const int MaxConfiguredTrainSupportDistance = 32;

        private static readonly BindingFlags s_instanceMethods =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly BindingFlags s_staticPublic = BindingFlags.Static | BindingFlags.Public;

        private static FieldInfo? s_transportSupportRadiusField;
        private static FieldInfo? s_transportPillarHeightField;
        private static FieldInfo? s_trainPillarHeightField;
        private static FieldInfo? s_trainSupportDistanceField;
        private static MethodInfo? s_getTransportSupportRadius;
        private static MethodInfo? s_getTransportPillarHeight;
        private static MethodInfo? s_getTrainPillarHeight;
        private static MethodInfo? s_getTrainSupportDistance;
        private static MethodInfo? s_transportBuildPrefix;
        private static MethodInfo? s_occupiedTileConstructorPrefix;
        private static MethodInfo? s_occupiedTileConstraintPostfix;
        private static bool s_initialized;

        private static int s_transportSupportRadius = VanillaTransportSupportRadius;
        private static int s_transportPillarHeight = VanillaTransportPillarHeight;
        private static int s_trainPillarHeight = VanillaTrainPillarHeight;
        private static int s_trainSupportDistance = VanillaTrainSupportDistance;
        private static bool s_ignorePillarRequirements;

        internal static void Initialize()
        {
            s_transportSupportRadius = Clamp(
                TajsTweaksRuntimeState.TransportPillarSupportRadius,
                1,
                MaxConfiguredSupportRadius,
                VanillaTransportSupportRadius);
            s_transportPillarHeight = Clamp(
                TajsTweaksRuntimeState.TransportPillarMaxHeight,
                1,
                MaxConfiguredPillarHeight,
                VanillaTransportPillarHeight);
            s_trainPillarHeight = Clamp(
                TajsTweaksRuntimeState.TrainTrackPillarMaxHeight,
                1,
                MaxConfiguredPillarHeight,
                VanillaTrainPillarHeight);
            s_trainSupportDistance = Clamp(
                TajsTweaksRuntimeState.TrainTrackPillarSupportDistance,
                1,
                MaxConfiguredTrainSupportDistance,
                VanillaTrainSupportDistance);
            s_ignorePillarRequirements = TajsTweaksRuntimeState.IgnorePillarRequirements;
            s_initialized = true;
        }

        internal static void Install(Harmony harmony)
        {
            if (!s_initialized)
            {
                Initialize();
            }

            s_transportSupportRadiusField = typeof(TransportProto).GetField("MaxPillarSupportRadius", s_instanceMethods)
                                            ?? throw new MissingFieldException(typeof(TransportProto).FullName, "MaxPillarSupportRadius");
            s_transportPillarHeightField = typeof(TransportPillarProto).GetField("MAX_PILLAR_HEIGHT", s_staticPublic)
                                           ?? throw new MissingFieldException(typeof(TransportPillarProto).FullName, "MAX_PILLAR_HEIGHT");
            s_trainPillarHeightField = typeof(TrainTrackPillarProto).GetField("MAX_PILLAR_HEIGHT", s_staticPublic)
                                       ?? throw new MissingFieldException(typeof(TrainTrackPillarProto).FullName, "MAX_PILLAR_HEIGHT");
            s_trainSupportDistanceField = typeof(TrainTrackConstants).GetField("PILLAR_SUPPORT_DISTANCE", s_staticPublic)
                                          ?? throw new MissingFieldException(typeof(TrainTrackConstants).FullName, "PILLAR_SUPPORT_DISTANCE");

            s_transportSupportRadius = Math.Max(1, s_transportSupportRadius);
            s_transportPillarHeight = Math.Max(1, s_transportPillarHeight);
            s_trainPillarHeight = Math.Max(1, s_trainPillarHeight);
            s_trainSupportDistance = Math.Max(1, s_trainSupportDistance);

            s_transportSupportRadius = Math.Min(MaxConfiguredSupportRadius, s_transportSupportRadius);
            s_transportPillarHeight = Math.Min(MaxConfiguredPillarHeight, s_transportPillarHeight);
            s_trainPillarHeight = Math.Min(MaxConfiguredPillarHeight, s_trainPillarHeight);
            s_trainSupportDistance = Math.Min(MaxConfiguredTrainSupportDistance, s_trainSupportDistance);

            s_getTransportSupportRadius = AccessTools.Method(typeof(TransportPillarRulesFeature), nameof(GetTransportSupportRadius));
            s_getTransportPillarHeight = AccessTools.Method(typeof(TransportPillarRulesFeature), nameof(GetTransportPillarHeight));
            s_getTrainPillarHeight = AccessTools.Method(typeof(TransportPillarRulesFeature), nameof(GetTrainPillarHeight));
            s_getTrainSupportDistance = AccessTools.Method(typeof(TransportPillarRulesFeature), nameof(GetTrainSupportDistance));
            s_transportBuildPrefix = AccessTools.Method(typeof(TransportPillarRulesFeature), nameof(ForceIgnorePillarRequirements));
            s_occupiedTileConstructorPrefix = AccessTools.Method(typeof(TransportPillarRulesFeature), nameof(RemovePillarConstraintFromOccupiedTile));
            s_occupiedTileConstraintPostfix = AccessTools.Method(typeof(TransportPillarRulesFeature), nameof(RemovePillarConstraintFromResult));

            MethodInfo transportBuild = AccessTools.Method(typeof(TransportsManager), "CanBuildOrJoinTransport")
                                        ?? throw new MissingMethodException(typeof(TransportsManager).FullName, "CanBuildOrJoinTransport");
            harmony.Patch(transportBuild, prefix: new HarmonyMethod(s_transportBuildPrefix));

            ConstructorInfo occupiedTileConstructor = AccessTools.Constructor(
                                                          typeof(OccupiedTileRelative),
                                                          new[]
                                                          {
                                                              typeof(short),
                                                              typeof(short),
                                                              typeof(short),
                                                              typeof(ushort),
                                                              typeof(ushort),
                                                              typeof(TileSurfaceSlimId),
                                                              typeof(short),
                                                          }) ??
                                                      throw new MissingMethodException(typeof(OccupiedTileRelative).FullName, ".ctor");
            harmony.Patch(occupiedTileConstructor, prefix: new HarmonyMethod(s_occupiedTileConstructorPrefix));

            MethodInfo occupiedTileConstraint = AccessTools.PropertyGetter(
                                                    typeof(OccupiedTileRelative),
                                                    nameof(OccupiedTileRelative.Constraint))
                                                ?? throw new MissingMethodException(typeof(OccupiedTileRelative).FullName, "Constraint");
            harmony.Patch(occupiedTileConstraint, postfix: new HarmonyMethod(s_occupiedTileConstraintPostfix));

            PatchMethods(harmony, typeof(TransportsManager));
            PatchMethods(harmony, typeof(TransportsConstructionHelper));
            PatchMethods(harmony, typeof(TransportPathFinder));
            PatchConstructor(harmony, typeof(TransportPillar));

            PatchMethods(harmony, typeof(TrainTracksPillarManager));
            PatchMethods(
                harmony,
                typeof(TrainTracksGraphManager).BaseType
                ?? throw new MissingMemberException(typeof(TrainTracksGraphManager).FullName, "BaseType"));
            PatchConstructor(harmony, typeof(TrainTrackPillar));

            PatchOptionalType(harmony, "Mafi.Unity.InputControl.Factory.StaticTransportPreview, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Unity.Ui.Controllers.TransportBuildController, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Unity.Ui.Controllers.LayoutEntityPlacing.LiftPlacementHelper, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Unity.Ui.Controllers.Trains.TrainTrackBuildController, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Unity.Factory.Transports.TransportPillarsRenderer, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Unity.Trains.TrainTrackPillarsRenderer, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Unity.Trains.TrainTracksPreviewGraphManager, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Unity.Trains.TrainsGizmosRendererMb, Mafi.Unity");
            PatchOptionalType(harmony, "Mafi.Core.Entities.Blueprints.RecoverySaveManger, Mafi.Core");
        }

        internal static string Describe()
        {
            return "transport support radius " + s_transportSupportRadius + " tiles (vanilla " +
                   VanillaTransportSupportRadius + "); transport pillar height " + s_transportPillarHeight +
                   " tiles (vanilla " + VanillaTransportPillarHeight + "); train pillar height " +
                   s_trainPillarHeight + " tiles (vanilla " + VanillaTrainPillarHeight + "); train support distance " +
                   s_trainSupportDistance + " tiles (vanilla " + VanillaTrainSupportDistance + "); ignore pillar requirements " +
                   (s_ignorePillarRequirements ? "enabled" : "disabled") + ". Restart required.";
        }

        internal static void ApplyPillarConstraintOverrides(ProtosDb protosDb)
        {
            if (!s_ignorePillarRequirements)
            {
                return;
            }

            FieldInfo combinedConstraint = typeof(EntityLayout).GetField(
                                               nameof(EntityLayout.CombinedConstraint),
                                               BindingFlags.Instance | BindingFlags.Public)
                                           ?? throw new MissingFieldException(typeof(EntityLayout).FullName, nameof(EntityLayout.CombinedConstraint));

            foreach (LayoutEntityProto proto in GetPillarLayoutPrototypes(protosDb))
            {
                combinedConstraint.SetValue(proto.Layout, RemovePillarConstraint(proto.Layout.CombinedConstraint));
            }
        }

        internal static LayoutTileConstraint RemovePillarConstraint(LayoutTileConstraint constraint) =>
            constraint & ~LayoutTileConstraint.UsingPillar;

        private static IEnumerable<LayoutEntityProto> GetPillarLayoutPrototypes(ProtosDb protosDb)
        {
            foreach (MiniZipperProto proto in protosDb.All<MiniZipperProto>())
            {
                yield return proto;
            }
            foreach (ZipperProto proto in protosDb.All<ZipperProto>())
            {
                yield return proto;
            }
            foreach (SorterProto proto in protosDb.All<SorterProto>())
            {
                yield return proto;
            }
            foreach (LiftProto proto in protosDb.All<LiftProto>())
            {
                yield return proto;
            }
        }

        internal static bool IsAreaWithinBounds(int minX, int minY, int maxX, int maxY, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (minX > maxX || minY > maxY)
            {
                return false;
            }

            long calculatedWidth = (long)maxX - minX + 1;
            long calculatedHeight = (long)maxY - minY + 1;
            if (calculatedWidth <= 0 || calculatedHeight <= 0 || calculatedWidth > 64 || calculatedHeight > 64 ||
                calculatedWidth * calculatedHeight > 4096)
            {
                return false;
            }

            width = (int)calculatedWidth;
            height = (int)calculatedHeight;
            return true;
        }

        internal static string ApplyTransportArea(
            TransportsManager manager,
            IInputScheduler scheduler,
            string? operation,
            int minX,
            int minY,
            int maxX,
            int maxY,
            string? confirmation)
        {
            if (!IsAreaWithinBounds(minX, minY, maxX, maxY, out _, out _))
            {
                return "Area rejected: bounds must be ordered and no larger than 64x64 tiles.";
            }

            string mode = (operation ?? string.Empty).Trim().ToLowerInvariant();
            if (mode != "add" && mode != "remove")
            {
                return "Usage: tajs_transport_pillars_area <add|remove> <min-x> <min-y> <max-x> <max-y> [CONFIRM]";
            }
            if (mode == "remove" && !string.Equals(confirmation, "CONFIRM", StringComparison.Ordinal))
            {
                return "No pillars were removed. Repeat the remove operation with CONFIRM.";
            }

            var commands = new List<InputCommand>();
            var positions = new HashSet<Tile2i>();
            if (mode == "add")
            {
                foreach (Transport transport in manager.Transports)
                {
                    ImmutableArray<TransportSupportInfo> supportInfo = transport.Trajectory.TilesSupportInfo;
                    for (int index = 0; index < supportInfo.Length; index++)
                    {
                        TransportSupportInfo support = supportInfo[index];
                        if (support.PillarAttachmentType == TransportPillarAttachmentType.NoAttachment ||
                            !IsInside(support.Position.Xy, minX, minY, maxX, maxY) ||
                            !positions.Add(support.Position.Xy) ||
                            manager.HasPillarAt(support.Position.Xy, support.Position.Height, out _) ||
                            !manager.CanBuildOrExtendPillarAt(support.Position.Xy, support.Position.Height))
                        {
                            continue;
                        }

                        commands.Add(new AddTransportPillarCmd(transport.Id, index));
                        if (commands.Count >= 512)
                        {
                            break;
                        }
                    }
                    if (commands.Count >= 512)
                    {
                        break;
                    }
                }
            }
            else
            {
                foreach (KeyValuePair<Tile2i, TransportPillar> item in manager.Pillars)
                {
                    if (IsInside(item.Key, minX, minY, maxX, maxY) && manager.IsPillarRedundant(item.Key))
                    {
                        commands.Add(new RemoveTransportPillarCmd(item.Value.Id));
                        if (commands.Count >= 512)
                        {
                            break;
                        }
                    }
                }
            }

            foreach (InputCommand command in commands)
            {
                scheduler.ScheduleInputCmd(command);
            }
            return "Queued " + commands.Count + " transport pillar " + mode + " operation(s); maximum batch is 512.";
        }

        private static bool IsInside(Tile2i tile, int minX, int minY, int maxX, int maxY) =>
            tile.X >= minX && tile.X <= maxX && tile.Y >= minY && tile.Y <= maxY;

        private static int Clamp(int value, int minimum, int maximum, int fallback) =>
            value <= 0 ? fallback : Math.Min(maximum, Math.Max(minimum, value));

        private static void PatchOptionalType(Harmony harmony, string typeName)
        {
            var type = Type.GetType(typeName, false);
            if (type is not null)
            {
                PatchMethods(harmony, type);
                PatchStaticConstructor(harmony, type);
            }
        }

        private static void PatchStaticConstructor(Harmony harmony, Type type)
        {
            ConstructorInfo? constructor = type.TypeInitializer;
            if (constructor is not null && constructor.GetMethodBody() is not null && UsesPillarRuleField(constructor))
            {
                harmony.Patch(constructor, transpiler: new HarmonyMethod(typeof(TransportPillarRulesFeature), nameof(ReplacePillarRules)));
            }
        }

        private static void PatchMethods(Harmony harmony, Type type)
        {
            foreach (MethodInfo method in type.GetMethods(s_instanceMethods))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null ||
                    !UsesPillarRuleField(method))
                {
                    continue;
                }
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(TransportPillarRulesFeature), nameof(ReplacePillarRules)));
            }
        }

        private static void PatchConstructor(Harmony harmony, Type type)
        {
            ConstructorInfo? constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.GetMethodBody() is not null && UsesPillarRuleField(x));
            if (constructor is not null)
            {
                harmony.Patch(constructor, transpiler: new HarmonyMethod(typeof(TransportPillarRulesFeature), nameof(ReplacePillarRules)));
            }
        }

        private static bool UsesPillarRuleField(MethodBase method)
        {
            // ReSharper disable once RedundantArgumentDefaultValue
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method, null))
            {
                if (instruction.operand is FieldInfo field &&
                    (field == s_transportSupportRadiusField || field == s_transportPillarHeightField ||
                     field == s_trainPillarHeightField || field == s_trainSupportDistanceField))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<CodeInstruction> ReplacePillarRules(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field == s_transportSupportRadiusField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = s_getTransportSupportRadius;
                }
                else if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo staticField && staticField == s_transportPillarHeightField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = s_getTransportPillarHeight;
                }
                else if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo trainHeightField &&
                         trainHeightField == s_trainPillarHeightField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = s_getTrainPillarHeight;
                }
                else if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo distanceField && distanceField == s_trainSupportDistanceField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = s_getTrainSupportDistance;
                }
                yield return instruction;
            }
        }

        private static RelTile1i GetTransportSupportRadius(TransportProto prototype)
        {
            return prototype.MaxPillarSupportRadius.Value == RelTile1i.MaxValue.Value
                ? prototype.MaxPillarSupportRadius
                : new RelTile1i(s_transportSupportRadius);
        }

        private static ThicknessTilesI GetTransportPillarHeight() => new(s_transportPillarHeight);

        private static ThicknessTilesI GetTrainPillarHeight() => new(s_trainPillarHeight);

        private static RelTile1f GetTrainSupportDistance() => new(s_trainSupportDistance);

        private static void ForceIgnorePillarRequirements(
            ref bool ignorePillars,
            ref bool skipExtraPillarsForBetterVisuals)
        {
            if (!s_ignorePillarRequirements)
            {
                return;
            }

            ignorePillars = true;
            skipExtraPillarsForBetterVisuals = true;
        }

        private static void RemovePillarConstraintFromOccupiedTile(ref ushort ___ConstraintSlim)
        {
            if (s_ignorePillarRequirements)
            {
                ___ConstraintSlim = (ushort)RemovePillarConstraint((LayoutTileConstraint)___ConstraintSlim);
            }
        }

        private static void RemovePillarConstraintFromResult(ref LayoutTileConstraint __result)
        {
            if (s_ignorePillarRequirements)
            {
                __result = RemovePillarConstraint(__result);
            }
        }
    }
}
