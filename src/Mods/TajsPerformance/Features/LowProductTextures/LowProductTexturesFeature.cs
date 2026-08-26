// Taj's COI Mods | LowProductTexturesFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance.Features.LowProductTextures
{
    internal sealed class LowProductTexturesFeature : IPerformanceFeature
    {
        private const string HarmonyId = "TajsCOI.Performance.LowProductTextures";
        private const string MeshTableTypeName = "Mafi.Unity.InstancedRendering.Products.ProductMeshTable";

        public string Id => "LowProductTextures";
        public string ConfigKey => LowProductTexturesSettings.EnableConfigKey;

        public bool IsProcessPatchInstalled()
        {
            MethodInfo? target = FindTarget();
            MethodInfo? patchMethod = AccessTools.Method(typeof(LowProductTexturesFeature), nameof(OverrideMipBiasRead));
            return target is not null && patchMethod is not null &&
                   ProcessHarmonyPatchOwnership.HasExpected(
                       Harmony.GetPatchInfo(target)?.Transpilers,
                       HarmonyId,
                       patchMethod);
        }

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            MethodInfo? rebuild = FindTarget();
            if (rebuild is null)
            {
                throw new MissingMethodException(MeshTableTypeName, "RebuildTextureArrays(bool)");
            }

            // Target: ProductMeshTable.RebuildTextureArrays(bool). This behavior-changing transpiler
            // replaces exactly one int field load, throws before emitting altered IL on any signature
            // mismatch, and remains installed for the process lifetime under this feature's Harmony ID.
            MethodInfo patchMethod = AccessTools.Method(typeof(LowProductTexturesFeature), nameof(OverrideMipBiasRead))!;
            lock (s_installGate)
            {
                Patches? patches = Harmony.GetPatchInfo(rebuild);
                if (ProcessHarmonyPatchOwnership.HasExpected(patches?.Transpilers, HarmonyId, patchMethod))
                {
                    log.Info("Already installed / compatible; the process-lifetime texture patch was not applied again.");
                    runtime.ReportCompatibility(
                        new CompatibilityReport(
                            "TajsPerformance",
                            Id,
                            CompatibilityState.Compatible,
                            "Existing process-lifetime Harmony owner and transpiler method",
                            "Already installed / compatible",
                            "The validated 0.8.7a texture patch remains active; no duplicate transpiler was registered."));
                    return;
                }

                if (ProcessHarmonyPatchOwnership.HasOwner(patches?.Transpilers, HarmonyId))
                {
                    throw new InvalidOperationException(
                        $"Existing Harmony owner '{HarmonyId}' has an unexpected texture transpiler ({ProcessHarmonyPatchOwnership.Describe(patches)}).");
                }

                var harmony = new Harmony(HarmonyId);
                try
                {
                    harmony.Patch(rebuild, transpiler: new HarmonyMethod(patchMethod));
                }
                catch
                {
                    harmony.Unpatch(rebuild, HarmonyPatchType.Transpiler, HarmonyId);
                    throw;
                }
            }

            int bias = LowProductTexturesSettings.MipBias;
            log.Info($"Enabled product texture mip bias {bias}; the normal renderer rebuild path and 64 px floor remain active.");
            runtime.ReportCompatibility(
                new CompatibilityReport(
                    "TajsPerformance",
                    Id,
                    CompatibilityState.Compatible,
                    "One RenderingSettingOption.Value read in ProductMeshTable.RebuildTextureArrays(bool)",
                    $"Mip bias overridden to {bias} ({(bias == 3 ? "Low" : "Very Low")})",
                    "Vanilla presets are unchanged; the normal texture-array rebuild and minimum slice clamp remain active."));
        }

        private static readonly object s_installGate = new();

        private static MethodInfo? FindTarget()
        {
            Type? meshTable = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, "Mafi.Unity", StringComparison.Ordinal))
                ?.GetType(MeshTableTypeName, false);
            return meshTable?.GetMethod(
                "RebuildTextureArrays",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(bool) },
                null);
        }

        internal static IEnumerable<CodeInstruction> OverrideMipBiasRead(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo overrideMethod = AccessTools.Method(typeof(LowProductTexturesFeature), nameof(OverrideMipBias));
            var result = new List<CodeInstruction>();
            int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                result.Add(instruction);
                if (instruction.opcode == OpCodes.Ldfld &&
                    instruction.operand is FieldInfo field &&
                    !field.IsStatic && field.FieldType == typeof(int) &&
                    field.Name == "Value" &&
                    field.DeclaringType?.FullName == "Mafi.Unity.RenderingSettingOption")
                {
                    result.Add(new CodeInstruction(OpCodes.Call, overrideMethod));
                    matches++;
                }
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one product texture quality value read, found {matches}.");
            }
            return result;
        }

        private static int OverrideMipBias(int vanillaMipBias)
        {
            // The argument is consumed from the original ldfld stack value; the configured bias replaces it.
            _ = vanillaMipBias;
            return LowProductTexturesSettings.MipBias;
        }
    }
}
