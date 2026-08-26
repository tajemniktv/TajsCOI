// Taj's COI Mods | TweaksCompatibilityFeatures.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Entities;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles.Trucks;
using UnityEngine;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Bridges Tajs settings into Gameplay++ without taking a compile-time dependency on
    ///     that optional mod. Gameplay++ remains the owner of bridge geometry and lane changes;
    ///     these patches only replace its setting accessors.
    /// </summary>
    internal static class TweaksGameplayPlusPlusFeature
    {
        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            Type scaler = AccessTools.TypeByName("GameplayPP.BridgeVehicleScaler")
                          ?? throw new TypeLoadException("GameplayPP.BridgeVehicleScaler");
            Patch(harmony, scaler, "GetBridgeMode", nameof(GetBridgeModePostfix));
            Patch(harmony, scaler, "IsTrussEnabled", nameof(IsTrussEnabledPostfix));
            Patch(harmony, scaler, "IsCableEnabled", nameof(IsCableEnabledPostfix));
            Patch(harmony, scaler, "IsCenterDrivingEnabled", nameof(IsCenterDrivingEnabledPostfix));
            ReapplyPrototypeCompatibility(resolver);
        }

        private static void Patch(Harmony harmony, Type type, string methodName, string postfixName)
        {
            MethodInfo method = AccessTools.Method(type, methodName)
                                ?? throw new MissingMethodException(type.FullName, methodName);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(TweaksGameplayPlusPlusFeature), postfixName));
        }

        private static void GetBridgeModePostfix(ref int __result)
        {
            if (!string.Equals(TajsTweaksRuntimeState.BridgeScaleMode, "off", StringComparison.Ordinal))
            {
                __result = string.Equals(TajsTweaksRuntimeState.BridgeScaleMode, "gradual", StringComparison.Ordinal) ? 2 : 1;
            }
            else if (TajsTweaksRuntimeState.BridgeTrussEnabled || TajsTweaksRuntimeState.BridgeCableEnabled)
            {
                // Gameplay++ gates its bridge lane-mask patch on a non-zero mode even when
                // scaling itself is not requested. Enable its instant compatibility path while
                // leaving the Tajs scaling policy off.
                __result = 1;
            }
        }

        private static void IsTrussEnabledPostfix(ref bool __result)
        {
            if (TajsTweaksRuntimeState.BridgeTrussEnabled)
            {
                __result = true;
            }
        }

        private static void IsCableEnabledPostfix(ref bool __result)
        {
            if (TajsTweaksRuntimeState.BridgeCableEnabled)
            {
                __result = true;
            }
        }

        private static void IsCenterDrivingEnabledPostfix(ref bool __result)
        {
            if (TajsTweaksRuntimeState.CenterDriving)
            {
                __result = true;
            }
        }

        private static void ReapplyPrototypeCompatibility(DependencyResolver resolver)
        {
            if (!resolver.TryResolve(out ProtosDb protosDb))
            {
                return;
            }
            Type? patchType = AccessTools.TypeByName("GameplayPP.BridgeAccessPatch");
            if (patchType is null)
            {
                // The accessor bridge remains useful even when a particular Gameplay++
                // prototype-patch seam was renamed. Leave that optional subfeature alone.
                return;
            }
            MethodInfo? lanePatch = AccessTools.Method(patchType, "PatchVehicleLaneMasks", new[] { typeof(ProtosDb) });
            if (lanePatch is not null && (TajsTweaksRuntimeState.BridgeTrussEnabled || TajsTweaksRuntimeState.BridgeCableEnabled))
            {
                lanePatch.Invoke(null, new object[] { protosDb });
            }
            if (!TajsTweaksRuntimeState.CenterDriving)
            {
                return;
            }
            MethodInfo? centerPatch = patchType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name == "ShiftBridgeLanesToCenter" && x.GetParameters().Length == 2 &&
                                     x.GetParameters()[0].ParameterType == typeof(ProtosDb));
            if (centerPatch is not null)
            {
                centerPatch.Invoke(null, new object[] { protosDb, Fix32.FromFloat(0.9f) });
            }
        }
    }

    /// <summary>
    ///     Makes the optional Gameplay++ Parking HQ shipyard transfer safe when explicitly
    ///     enabled: only storable products are moved, and the truck removes exactly what the
    ///     shipyard accepted. The default mode leaves the provider's own policy untouched.
    /// </summary>
    internal static class TweaksParkingHqOffloadFeature
    {
        private static WeakReference<DependencyResolver>? s_resolver;

        internal static void SetResolver(DependencyResolver resolver) =>
            s_resolver = new WeakReference<DependencyResolver>(resolver);

        internal static void Install(Harmony harmony)
        {
            Type manager = AccessTools.TypeByName("GameplayPP.ParkingHQManager")
                           ?? throw new TypeLoadException("GameplayPP.ParkingHQManager");
            MethodInfo offload = AccessTools.Method(manager, "OffloadCargoToShipyard", new[] { typeof(Truck) })
                                 ?? throw new MissingMethodException(manager.FullName, "OffloadCargoToShipyard");
            harmony.Patch(offload, prefix: new HarmonyMethod(typeof(TweaksParkingHqOffloadFeature), nameof(OffloadPrefix)));
        }

        private static bool OffloadPrefix(Truck truck)
        {
            string mode = TajsTweaksRuntimeState.ParkingHqOffloadMode;
            if (string.Equals(mode, "vanilla", StringComparison.Ordinal))
            {
                return true;
            }
            if (string.Equals(mode, "disabled", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                if (truck is null || truck.Cargo.IsEmpty || s_resolver is null ||
                    !s_resolver.TryGetTarget(out DependencyResolver? resolver) ||
                    !resolver.TryResolve(out IEntitiesManager entities))
                {
                    return true;
                }

                Shipyard? shipyard = entities.GetAllEntitiesOfType<Shipyard>().FirstOrDefault();
                if (shipyard is null)
                {
                    return true;
                }

                var cargo = new Mafi.Collections.Lyst<ProductQuantity>();
                truck.Cargo.GetCargoProducts(cargo);
                foreach (ProductQuantity product in cargo)
                {
                    if (!product.Product.IsStorable || product.Quantity.IsNotPositive)
                    {
                        continue;
                    }

                    shipyard.StoreProduct(product);
                    truck.TakeCargo(product);
                }

                return false;
            }
            catch
            {
                // Preserve the provider's original behavior if the optional seam changes.
                return true;
            }
        }
    }

    /// <summary>
    ///     Optional adapter for the world-marker provider used by Tweaks++/Cheat++ builds. The
    ///     provider owns marker creation; Tajs only mirrors its public static scale setting and
    ///     restores the original value when the Tajs override is cleared.
    /// </summary>
    internal static class TweaksKeepFullEmptyMarkerFeature
    {
        private static FieldInfo? s_scaleField;
        private static float? s_original;
        private static bool s_searched;
        private static int s_assemblyCountAtSearch = -1;

        internal static void Install(Harmony _) => FindScaleField();

        internal static void Apply()
        {
            FindScaleField();
            if (s_scaleField is null || s_scaleField.FieldType != typeof(float))
            {
                return;
            }
            try
            {
                if (!s_original.HasValue)
                {
                    s_original = (float)s_scaleField.GetValue(null)!;
                }
                if (TajsTweaksRuntimeState.KeepFullEmptyLabelScale > 0)
                {
                    s_scaleField.SetValue(null, Mathf.Clamp((float)TajsTweaksRuntimeState.KeepFullEmptyLabelScale, 0.1f, 1f));
                }
                else if (s_original.HasValue)
                {
                    s_scaleField.SetValue(null, s_original.Value);
                }
            }
            catch
            {
                // Third-party marker providers are intentionally best-effort.
            }
        }

        internal static void Reset()
        {
            if (s_scaleField is null || !s_original.HasValue || s_scaleField.FieldType != typeof(float))
            {
                return;
            }

            try
            {
                s_scaleField.SetValue(null, s_original.Value);
            }
            catch
            {
                // The provider may have been unloaded before gameplay teardown completed.
            }
            finally
            {
                s_original = null;
            }
        }

        private static void FindScaleField()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (s_scaleField is not null || s_searched && assemblies.Length == s_assemblyCountAtSearch)
            {
                return;
            }
            s_searched = true;
            s_assemblyCountAtSearch = assemblies.Length;
            try
            {
                foreach (Assembly assembly in assemblies)
                {
                    string name = assembly.GetName().Name ?? string.Empty;
                    if (name.IndexOf("Tweaks", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("Cheat", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException exception)
                    {
                        types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
                    }
                    foreach (Type type in types)
                    {
                        FieldInfo? field = type.GetField("KfKeLabelScale", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
                                           type.GetField("KeepFullEmptyLabelScale", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field?.FieldType == typeof(float))
                        {
                            s_scaleField = field;
                            return;
                        }
                    }
                }
            }
            catch
            {
                // An optional provider can disappear or hide its metadata without affecting Tajs.
            }
        }
    }
}
