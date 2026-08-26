// Taj's COI Mods | TweaksAudioFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi.Unity.Audio;
using UnityEngine;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Applies the audio controls at the EntitySoundMb boundary. Sound descriptors are read
    ///     once per component instance/category lookup and all adjustments are derived from the
    ///     vanilla getter/setter inputs, so scene recreation cannot compound them.
    /// </summary>
    internal static class TweaksAudioFeature
    {
        private enum SoundCategory
        {
            Machine,
            Vehicle,
            Train,
        }

        private static readonly ConditionalWeakTable<EntitySoundMb, CategoryState> s_categories = new();
        private static PropertyInfo? s_desc;

        private sealed class CategoryState
        {
            internal SoundCategory Value;
        }

        internal static void Install(Harmony harmony)
        {
            MethodInfo? maxDistance = AccessTools.PropertyGetter(typeof(EntitySoundMb), "MaxDistance");
            MethodInfo? setVolume = AccessTools.Method(typeof(EntitySoundMb), "SetVolumeMultiplier", new[] { typeof(float) });
            if (maxDistance is null || setVolume is null)
            {
                throw new MissingMethodException(typeof(EntitySoundMb).FullName, "MaxDistance/SetVolumeMultiplier");
            }
            harmony.Patch(maxDistance, postfix: new HarmonyMethod(typeof(TweaksAudioFeature), nameof(MaxDistancePostfix)));
            harmony.Patch(setVolume, prefix: new HarmonyMethod(typeof(TweaksAudioFeature), nameof(SetVolumePrefix)));

            Type? friend = AccessTools.TypeByName("Mafi.Unity.Audio.IEntitySoundFriend, Mafi.Unity");
            if (friend is not null)
            {
                InterfaceMapping mapping = typeof(EntitySoundMb).GetInterfaceMap(friend);
                for (int index = 0; index < mapping.InterfaceMethods.Length; index++)
                {
                    if (mapping.InterfaceMethods[index].Name == "get_ListenerDistance")
                    {
                        harmony.Patch(mapping.TargetMethods[index], postfix: new HarmonyMethod(typeof(TajsAudioPatch), nameof(ListenerDistancePostfix)));
                        break;
                    }
                }
            }
        }

        private static void MaxDistancePostfix(EntitySoundMb __instance, ref int __result)
        {
            float multiplier = GetRangeMultiplier(__instance);
            if (Math.Abs(multiplier - 1f) > 0.001f)
            {
                __result = Math.Max(0, (int)Math.Round(__result * multiplier, MidpointRounding.AwayFromZero));
            }
        }

        private static void SetVolumePrefix(EntitySoundMb __instance, ref float volumeMultiplier)
        {
            if (GetCategory(__instance) == SoundCategory.Train)
            {
                volumeMultiplier *= Mathf.Clamp01((float)TajsTweaksRuntimeState.TrainSoundVolume);
            }
        }

        private static void ListenerDistancePostfix(EntitySoundMb __instance, ref float __result)
        {
            float multiplier = GetRangeMultiplier(__instance);
            if (multiplier > 0.001f && Math.Abs(multiplier - 1f) > 0.001f)
            {
                __result /= multiplier;
            }
        }

        private static float GetRangeMultiplier(EntitySoundMb sound)
        {
            return GetCategory(sound) switch
            {
                SoundCategory.Vehicle => Mathf.Max(1f, (float)TajsTweaksRuntimeState.VehicleSoundRange),
                SoundCategory.Train => Mathf.Clamp((float)TajsTweaksRuntimeState.TrainSoundRange, 0.1f, 1f),
                _ => Mathf.Max(1f, (float)TajsTweaksRuntimeState.MachineSoundRange),
            };
        }

        private static SoundCategory GetCategory(EntitySoundMb sound)
        {
            if (s_categories.TryGetValue(sound, out CategoryState? state))
            {
                return state.Value;
            }
            SoundCategory category = SoundCategory.Machine;
            try
            {
                s_desc ??= typeof(EntitySoundMb).GetProperty("Desc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                string descriptor = s_desc?.GetValue(sound) as string ?? string.Empty;
                string name = sound.gameObject?.name ?? string.Empty;
                if (descriptor.IndexOf("/Trains/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    category = SoundCategory.Train;
                }
                else if (descriptor.IndexOf("/Machines/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         descriptor.IndexOf("/Buildings/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("CombustionEngine", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    category = SoundCategory.Machine;
                }
                else if (name.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Truck", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Excavator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Movement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Treads", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Drive", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    category = SoundCategory.Vehicle;
                }
            }
            catch
            {
                // Machine is the conservative vanilla category.
            }
            s_categories.Add(sound, new CategoryState { Value = category });
            return category;
        }

        private static class TajsAudioPatch
        {
            internal static void ListenerDistancePostfix(EntitySoundMb __instance, ref float __result) =>
                TweaksAudioFeature.ListenerDistancePostfix(__instance, ref __result);
        }
    }
}
