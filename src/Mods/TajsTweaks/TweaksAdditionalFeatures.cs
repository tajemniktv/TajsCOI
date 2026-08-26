// Taj's COI Mods | TweaksAdditionalFeatures.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Base;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities.Static;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Localization;
using Mafi.Unity.Entities.Static;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Schedules the game's normal paid quick-remove command after a qualifying demolition
    ///     request. No inventory is changed directly and unsupported command shapes fail open.
    /// </summary>
    internal static class TweaksQuickRemoveFeature
    {
        private static FieldInfo? s_schedulerField;
        private static MethodInfo? s_scheduleGeneric;

        internal static void Install(Harmony harmony)
        {
            Type? controller = AccessTools.TypeByName("Mafi.Unity.Ui.Controllers.Tools.DeleteEntityInputController, Mafi.Unity");
            MethodInfo? schedule = controller is null
                ? null
                : AccessTools.Method(controller, "scheduleCommand", new[] { typeof(IInputCommand) });
            s_schedulerField = controller?.GetField("m_inputScheduler", BindingFlags.Instance | BindingFlags.NonPublic);
            if (schedule is null || s_schedulerField is null)
            {
                throw new MissingMethodException(controller?.FullName, "scheduleCommand/m_inputScheduler");
            }
            harmony.Patch(schedule, postfix: new HarmonyMethod(typeof(TweaksQuickRemoveFeature), nameof(ScheduleCommandPostfix)));
        }

        private static void ScheduleCommandPostfix(object __instance, IInputCommand cmd)
        {
            if (!TajsTweaksRuntimeState.QuickRemoveOnDemolish ||
                !string.Equals(cmd.GetType().Name, "StartDeconstructionOfStaticEntityCmd", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                FieldInfo? entityField = cmd.GetType().GetField("EntityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object? scheduler = s_schedulerField?.GetValue(__instance);
                if (entityField is null || scheduler is null)
                {
                    return;
                }

                if (s_scheduleGeneric is null)
                {
                    s_scheduleGeneric = scheduler.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(method => method.Name == "ScheduleInputCmd" && method.IsGenericMethodDefinition);
                }
                if (s_scheduleGeneric is null || entityField.GetValue(cmd) is not EntityId entityId)
                {
                    return;
                }

                s_scheduleGeneric.MakeGenericMethod(typeof(QuickRemoveFromEntityCmd))
                    .Invoke(scheduler, new object[] { new QuickRemoveFromEntityCmd(entityId) });
            }
            catch
            {
                // Demolition remains authoritative; quick-remove is an optional convenience.
            }
        }
    }

    internal static class TweaksPlanningColorFeature
    {
        private static readonly ColorRgba[] s_colors =
        {
            new ColorRgba(153, 153, 153, 128),
            new ColorRgba(255, 221, 40, 128),
            new ColorRgba(255, 140, 30, 128),
            new ColorRgba(235, 60, 60, 128),
            new ColorRgba(255, 120, 190, 128),
            new ColorRgba(170, 90, 230, 128),
            new ColorRgba(70, 195, 80, 128),
            new ColorRgba(170, 230, 60, 128),
            new ColorRgba(245, 245, 245, 128),
        };

        internal static void Install(Harmony harmony)
        {
            MethodInfo? method = typeof(InstancedChunkBasedLayoutEntitiesRenderer).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.Name == "GetBlueprintColor" &&
                                              candidate.GetParameters().Length == 2 &&
                                              candidate.GetParameters()[0].ParameterType == typeof(IStaticEntity) &&
                                              candidate.GetParameters()[1].ParameterType == typeof(ColorRgba).MakeByRefType());
            if (method is null)
            {
                throw new MissingMethodException(typeof(InstancedChunkBasedLayoutEntitiesRenderer).FullName, "GetBlueprintColor");
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(TweaksPlanningColorFeature), nameof(GetBlueprintColorPostfix)));
        }

        private static void GetBlueprintColorPostfix(IStaticEntity entity, ref ColorRgba color, bool __result)
        {
            if (!__result || entity is null ||
                !TryGetColor(TajsTweaksRuntimeState.PlanningBuildingColor, out ColorRgba replacement))
            {
                return;
            }

            try
            {
                IEntityConstructionProgress? progress = entity.ConstructionProgress.ValueOrNull;
                if (progress is not null && progress.IsPaused)
                {
                    color = replacement;
                }
            }
            catch
            {
                // Renderer compatibility is presentation-only.
            }
        }

        private static bool TryGetColor(string value, out ColorRgba color)
        {
            int index = value switch
            {
                "yellow" => 1,
                "orange" => 2,
                "red" => 3,
                "pink" => 4,
                "purple" => 5,
                "green" => 6,
                "lime" => 7,
                "white" => 8,
                _ => 0,
            };
            if (index == 0)
            {
                color = default;
                return false;
            }
            color = s_colors[index];
            return true;
        }
    }

    internal static class TweaksBattleScoreFeature
    {
        private static PropertyInfo? s_travelingFleet;
        private static PropertyInfo? s_fleetEntity;
        private static PropertyInfo? s_battleFleet;
        private static MethodInfo? s_getBattleScore;

        internal static void Install(Harmony harmony)
        {
            Type? panel = AccessTools.TypeByName("Mafi.Unity.Ui.World.WorldMapWindow+ShipStatusPanel, Mafi.Unity");
            ConstructorInfo? constructor = panel?.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.GetParameters().Length >= 2);
            if (constructor is null)
            {
                throw new MissingMethodException(panel?.FullName, ".ctor");
            }
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TweaksBattleScoreFeature), nameof(ConstructorPostfix)));
        }

        private static void ConstructorPostfix(object __instance, object fleetManager)
        {
            if (!TajsTweaksRuntimeState.BattleScoreOnMap || __instance is not PanelWithHeader panel || fleetManager is null)
            {
                return;
            }

            try
            {
                var value = new Display().Value(ReadBattleScore(fleetManager).ToString().AsLoc());
                var row = new Row(1.pt())
                {
                    (Action<Row>)(item => item.JustifyItemsCenter().FlexGrow(1f).Padding(2.pt())),
                    new Label(Tr.BattleScore.AppendColon()).FontBold(),
                    value,
                };
                panel.BodyAdd(row);
                object manager = fleetManager;
                panel.Schedule.Execute(() => value.Value(ReadBattleScore(manager).ToString().AsLoc())).Every(1000L);
            }
            catch
            {
                // The native world-map panel remains usable if the optional row changes shape.
            }
        }

        private static int ReadBattleScore(object fleetManager)
        {
            try
            {
                s_travelingFleet ??= fleetManager.GetType().GetProperty("TravelingFleet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object? travelingFleet = s_travelingFleet?.GetValue(fleetManager);
                if (travelingFleet is null)
                {
                    return 0;
                }
                s_fleetEntity ??= travelingFleet.GetType().GetProperty("FleetEntity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object? fleetEntity = s_fleetEntity?.GetValue(travelingFleet);
                s_getBattleScore ??= fleetEntity?.GetType().GetMethod("GetBattleScore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fleetEntity is not null && s_getBattleScore?.Invoke(fleetEntity, null) is int score && score > 0)
                {
                    return score;
                }
                s_battleFleet ??= travelingFleet.GetType().GetProperty("BattleFleet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object? battleFleet = s_battleFleet?.GetValue(travelingFleet);
                return battleFleet?.GetType().GetMethod("GetBattleScore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(battleFleet, null) as int? ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal static class TweaksSteamStorageFeature
    {
        private static readonly HashSet<string> s_steamIds = new(StringComparer.Ordinal)
        {
            "Product_SteamSp", "Product_SteamHi", "Product_SteamLP", "Product_SteamDepleted",
        };
        private static readonly HashSet<string> s_exhaustIds = new(StringComparer.Ordinal) { "Product_Exhaust" };

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            MethodInfo? supported = AccessTools.Method(typeof(StorageProto), "IsProductSupported", new[] { typeof(ProductProto) });
            if (supported is null)
            {
                throw new MissingMethodException(typeof(StorageProto).FullName, "IsProductSupported");
            }
            harmony.Patch(supported, prefix: new HarmonyMethod(typeof(TweaksSteamStorageFeature), nameof(IsProductSupportedPrefix)));
            if (!TajsTweaksRuntimeState.AllowSteam && !TajsTweaksRuntimeState.AllowExhaust)
            {
                return;
            }
            if (!resolver.TryResolve(out ProtosDb protosDb))
            {
                throw new InvalidOperationException("ProtosDb is unavailable for steam/exhaust compatibility.");
            }
            PatchProducts(protosDb);
        }

        private static bool IsProductSupportedPrefix(object __instance, ProductProto product, ref bool __result)
        {
            if (__instance is not FluidStorageProto || product is null || !IsEnabledProduct(product.Id.ToString()))
            {
                return true;
            }
            __result = true;
            return false;
        }

        private static bool IsEnabledProduct(string id) =>
            (TajsTweaksRuntimeState.AllowSteam && s_steamIds.Contains(id)) ||
            (TajsTweaksRuntimeState.AllowExhaust && s_exhaustIds.Contains(id));

        private static void PatchProducts(ProtosDb protosDb)
        {
            var products = new List<ProductProto>();
            foreach (ProductProto.ID id in GetEnabledProductIds())
            {
                Option<ProductProto> product = protosDb.Get<ProductProto>(id);
                if (!product.HasValue)
                {
                    continue;
                }
                ProductProto value = product.Value;
                FieldInfo? storable = typeof(ProductProto).GetField("<IsStorable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic) ??
                                      typeof(ProductProto).GetField("IsStorable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                storable?.SetValue(value, true);
                products.Add(value);
            }
            if (products.Count > 0)
            {
                PatchStorageFilters(protosDb, products);
                PatchTruckFilters(protosDb, products);
            }
        }

        private static IEnumerable<ProductProto.ID> GetEnabledProductIds()
        {
            if (TajsTweaksRuntimeState.AllowSteam)
            {
                yield return Ids.Products.SteamSp;
                yield return Ids.Products.SteamHi;
                yield return Ids.Products.SteamLo;
                yield return Ids.Products.SteamDepleted;
            }
            if (TajsTweaksRuntimeState.AllowExhaust)
            {
                yield return Ids.Products.Exhaust;
            }
        }

        private static void PatchStorageFilters(ProtosDb protosDb, List<ProductProto> products)
        {
            FieldInfo? filter = typeof(StorageProto).GetField("m_productsFilter", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? backing = typeof(StorageProto).GetField("<StorableProducts>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (FluidStorageProto storage in protosDb.All<FluidStorageProto>())
            {
                if (filter?.GetValue(storage) is Func<ProductProto, bool> original)
                {
                    HashSet<ProductProto> allowed = new(products);
                    filter.SetValue(storage, new Func<ProductProto, bool>(product => original(product) || allowed.Contains(product)));
                }
                if (backing?.GetValue(storage) is Set<ProductProto> storableProducts)
                {
                    foreach (ProductProto product in products)
                    {
                        storableProducts.Add(product);
                    }
                }
            }
        }

        private static void PatchTruckFilters(ProtosDb protosDb, List<ProductProto> products)
        {
            FieldInfo? filter = typeof(TruckProto).GetField("m_productsFilter", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? backing = typeof(TruckProto).GetField("<AllowedProducts>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (TruckProto truck in protosDb.All<TruckProto>())
            {
                if (!truck.ProductType.HasValue || !truck.ProductType.Value.Equals(FluidProductProto.ProductType))
                {
                    continue;
                }
                if (filter?.GetValue(truck) is Func<ProductProto, bool> original)
                {
                    HashSet<ProductProto> allowed = new(products);
                    filter.SetValue(truck, new Func<ProductProto, bool>(product => original(product) || allowed.Contains(product)));
                }
                if (backing?.GetValue(truck) is Set<ProductProto> allowedProducts)
                {
                    foreach (ProductProto product in products)
                    {
                        allowedProducts.Add(product);
                    }
                }
            }
        }
    }

    internal static class TweaksFarmAlertFeature
    {
        private sealed class Entry
        {
            internal WeakReference<UiComponent> Row = null!;
            internal WeakReference<object> Inspector = null!;
        }

        private static readonly List<Entry> s_entries = new();
        private static MethodInfo? s_setVisible;
        private static PropertyInfo? s_entity;
        private static PropertyInfo? s_outputBuffers;

        internal static void Install(Harmony harmony)
        {
            Type? inspector = AccessTools.TypeByName("Mafi.Unity.Ui.Inspectors.FarmInspector, Mafi.Unity");
            ConstructorInfo? constructor = inspector?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
            if (constructor is null)
            {
                throw new MissingMethodException(inspector?.FullName, ".ctor");
            }
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TweaksFarmAlertFeature), nameof(InspectorConstructorPostfix)));
        }

        internal static void Tick()
        {
            for (int index = s_entries.Count - 1; index >= 0; index--)
            {
                Entry entry = s_entries[index];
                if (!entry.Row.TryGetTarget(out UiComponent? row) || !entry.Inspector.TryGetTarget(out object? inspector))
                {
                    s_entries.RemoveAt(index);
                    continue;
                }
                int outputBufferCount = GetOutputBufferCount(GetEntity(inspector));
                bool visible = TajsTweaksRuntimeState.FarmFullToggleAlways || outputBufferCount < 0 || outputBufferCount > 0;
                try
                {
                    s_setVisible?.Invoke(row, new object[] { visible });
                }
                catch
                {
                    s_entries.RemoveAt(index);
                }
            }
        }

        private static void InspectorConstructorPostfix(object __instance)
        {
            try
            {
                UiComponent? root = GetRoot(__instance);
                UiComponent? row = root is null ? null : FindBellRow(root, 0);
                if (row is null)
                {
                    return;
                }
                s_setVisible ??= FindSetVisible(row.GetType());
                if (s_setVisible is not null)
                {
                    s_entries.Add(new Entry
                    {
                        Row = new WeakReference<UiComponent>(row),
                        Inspector = new WeakReference<object>(__instance),
                    });
                }
            }
            catch
            {
                // The existing farm inspector is never a hard dependency.
            }
        }

        private static UiComponent? GetRoot(object inspector)
        {
            if (inspector is UiComponent component)
            {
                return component;
            }
            foreach (string name in new[] { "Body", "RootElement", "Root", "Panel", "Window" })
            {
                if (AccessTools.Property(inspector.GetType(), name)?.GetValue(inspector) is UiComponent root)
                {
                    return root;
                }
            }
            for (Type? type = inspector.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (typeof(UiComponent).IsAssignableFrom(field.FieldType) && field.GetValue(inspector) is UiComponent root)
                    {
                        return root;
                    }
                }
            }
            return null;
        }

        private static UiComponent? FindBellRow(UiComponent node, int depth)
        {
            if (depth > 14)
            {
                return null;
            }
            if (node.GetType().Name.IndexOf("PanelRow", StringComparison.Ordinal) >= 0 && LooksLikeBellRow(node))
            {
                return node;
            }
            foreach (UiComponent child in node.AllChildren)
            {
                UiComponent? result = FindBellRow(child, depth + 1);
                if (result is not null)
                {
                    return result;
                }
            }
            return null;
        }

        private static bool LooksLikeBellRow(UiComponent row)
        {
            UiComponent? body = AccessTools.Property(row.GetType(), "Body")?.GetValue(row) as UiComponent;
            if (body is null)
            {
                return false;
            }
            bool button = false;
            bool divider = false;
            bool label = false;
            foreach (UiComponent child in body.AllChildren)
            {
                string name = child.GetType().Name;
                button |= name.IndexOf("ButtonIcon", StringComparison.Ordinal) >= 0;
                divider |= name.IndexOf("VerticalDivider", StringComparison.Ordinal) >= 0;
                label |= name == "Label";
            }
            return button && divider && label;
        }

        private static MethodInfo? FindSetVisible(Type type) =>
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "SetVisible" &&
                                          method.GetParameters().Length == 1 &&
                                          method.GetParameters()[0].ParameterType == typeof(bool));

        private static object? GetEntity(object inspector)
        {
            s_entity ??= AccessTools.Property(inspector.GetType(), "Entity");
            return s_entity?.GetValue(inspector);
        }

        private static int GetOutputBufferCount(object? farm)
        {
            if (farm is null)
            {
                return -1;
            }
            try
            {
                s_outputBuffers ??= AccessTools.Property(farm.GetType(), "OutputBuffers");
                if (s_outputBuffers?.GetValue(farm) is IEnumerable buffers)
                {
                    int count = 0;
                    foreach (object _ in buffers)
                    {
                        count++;
                    }
                    return count;
                }
            }
            catch
            {
                // Preserve the native row when a private shape changes. Hiding an unknown
                // row would silently turn a compatibility miss into a behavior regression.
            }
            return -1;
        }
    }

    internal static class TweaksClassicRecipeFeature
    {
        private static readonly BindingFlags s_any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static FieldInfo? s_durationContainer;
        private static FieldInfo? s_inputs;
        private static FieldInfo? s_outputs;
        private static FieldInfo? s_duration;
        private static FieldInfo? s_speed;
        private static FieldInfo? s_boosted;
        private static FieldInfo? s_pollutedAir;
        private static FieldInfo? s_doNotNormalize;
        private static MethodInfo? s_addInput;
        private static MethodInfo? s_addOutput;
        private static MethodInfo? s_addProductsNames;
        private static MethodInfo? s_addPollutedAir;
        private static Type? s_recipeProductUi;
        private static MethodInfo? s_createStaticCopy;
        private static MethodInfo? s_floater;
        private static bool s_initialized;
        private static bool s_bypass;

        internal static void Install(Harmony harmony)
        {
            Type type = typeof(RecipeUi);
            // 0.8.7a used a private m_durationContainer field. In 0.8.7b the same
            // presentation container is exposed as a protected DurationContainer field.
            s_durationContainer = type.GetField("DurationContainer", s_any) ??
                                   type.GetField("m_durationContainer", s_any);
            s_inputs = type.GetField("m_inputs", s_any);
            s_outputs = type.GetField("m_outputs", s_any);
            s_duration = type.GetField("m_duration", s_any);
            s_speed = type.GetField("m_speedMultiplier", s_any);
            s_boosted = type.GetField("m_isBoosted", s_any);
            s_pollutedAir = type.GetField("m_pollutedAir", s_any);
            s_doNotNormalize = type.GetField("m_doNotNormalize", s_any);
            s_addInput = type.GetMethods(s_any).FirstOrDefault(method => method.Name is "addInput" or "AddInput");
            s_addOutput = type.GetMethods(s_any).FirstOrDefault(method => method.Name is "addOutput" or "AddOutput");
            s_addProductsNames = type.GetMethods(s_any).FirstOrDefault(method => method.Name == "AddProductsNames");
            s_addPollutedAir = type.GetMethods(s_any).FirstOrDefault(method => method.Name == "AddPollutedAir");
            s_recipeProductUi = type.Assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == "IRecipeProductUi");
            s_createStaticCopy = s_recipeProductUi?.GetMethods().FirstOrDefault(method => method.Name == "CreateStaticCopy");
            Type? floating = type.Assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == "FloatingPanelExtensions");
            s_floater = floating?.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == "Floater" && method.GetParameters().Length >= 2 && method.GetParameters()[1].ParameterType.Name.Contains("Func"));
            s_initialized = s_durationContainer is not null && s_inputs is not null && s_outputs is not null &&
                            s_duration is not null && s_speed is not null && s_boosted is not null && s_doNotNormalize is not null;
            if (!s_initialized)
            {
                throw new MissingMemberException(type.FullName, "recipe display fields");
            }

            ConstructorInfo? recipeConstructor = type.GetConstructors().FirstOrDefault();
            if (recipeConstructor is not null)
            {
                harmony.Patch(recipeConstructor, postfix: new HarmonyMethod(typeof(TweaksClassicRecipeFeature), nameof(RecipeConstructorPostfix)));
            }
            ConstructorInfo? machineConstructor = typeof(MachineRecipeUi).GetConstructors().FirstOrDefault();
            if (machineConstructor is not null)
            {
                harmony.Patch(machineConstructor, postfix: new HarmonyMethod(typeof(TweaksClassicRecipeFeature), nameof(RecipePostfix)));
            }
            ConstructorInfo? staticConstructor = typeof(StaticRecipeUi).GetConstructors().FirstOrDefault();
            if (staticConstructor is not null)
            {
                harmony.Patch(staticConstructor, postfix: new HarmonyMethod(typeof(TweaksClassicRecipeFeature), nameof(RecipePostfix)));
            }
        }

        private static void RecipeConstructorPostfix(RecipeUi __instance)
        {
            if (TajsTweaksRuntimeState.ClassicRecipeDisplay && !s_bypass)
            {
                s_doNotNormalize!.SetValue(__instance, true);
            }
        }

        private static void RecipePostfix(RecipeUi __instance)
        {
            if (!TajsTweaksRuntimeState.ClassicRecipeDisplay)
            {
                return;
            }
            try
            {
                __instance.Normalize(normalizeOn: false, update: true);
                if (__instance is not MachineRecipeUi || s_floater is null || s_durationContainer!.GetValue(__instance) is not UiComponent durationContainer)
                {
                    return;
                }
                RecipeUi parent = __instance;
                Func<Option<UiComponent>> create = () => CreateNormalizedFloater(parent);
                s_floater.MakeGenericMethod(durationContainer.GetType()).Invoke(null, new object?[] { durationContainer, create, null, false });
            }
            catch
            {
                // A recipe inspector should never fail because a presentation helper changed.
            }
        }

        private static Option<UiComponent> CreateNormalizedFloater(RecipeUi parent)
        {
            s_bypass = true;
            try
            {
                var recipe = new RecipeUi();
                recipe.SetDuration((Duration)s_duration!.GetValue(parent)!, (Percent)s_speed!.GetValue(parent)!, (bool)s_boosted!.GetValue(parent)!);
                CopyProducts(parent, recipe, s_inputs!, s_addInput!);
                CopyProducts(parent, recipe, s_outputs!, s_addOutput!);
                object? polluted = s_pollutedAir?.GetValue(parent);
                if (polluted is not null && s_addPollutedAir is not null)
                {
                    s_addPollutedAir.Invoke(recipe, new[] { polluted });
                }
                s_addProductsNames?.Invoke(recipe, null);
                return Option.Some<UiComponent>(recipe);
            }
            catch
            {
                return Option<UiComponent>.None;
            }
            finally
            {
                s_bypass = false;
            }
        }

        private static void CopyProducts(RecipeUi source, RecipeUi target, FieldInfo field, MethodInfo add)
        {
            if (s_recipeProductUi is null || s_createStaticCopy is null || field.GetValue(source) is not UiComponent products)
            {
                return;
            }
            foreach (UiComponent child in products.AllChildren)
            {
                if (!s_recipeProductUi.IsInstanceOfType(child) || !child.IsVisible())
                {
                    continue;
                }
                object? copy = s_createStaticCopy.Invoke(child, new object[] { target });
                if (copy is not null)
                {
                    MethodInfo method = add.IsGenericMethodDefinition ? add.MakeGenericMethod(copy.GetType()) : add;
                    method.Invoke(target, new[] { copy });
                }
            }
        }
    }
}
