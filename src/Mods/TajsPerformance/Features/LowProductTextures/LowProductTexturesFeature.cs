// Taj's COI Mods | LowProductTexturesFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            Type? meshTable = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, "Mafi.Unity", StringComparison.Ordinal))
                ?.GetType(MeshTableTypeName, false);
            MethodInfo? rebuild = meshTable?.GetMethod(
                "RebuildTextureArrays",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(bool) },
                null);
            if (rebuild is null)
            {
                throw new MissingMethodException(MeshTableTypeName, "RebuildTextureArrays(bool)");
            }

            new Harmony(HarmonyId).Patch(
                rebuild,
                transpiler: new HarmonyMethod(typeof(LowProductTexturesFeature), nameof(OverrideMipBiasRead)));

            int bias = LowProductTexturesSettings.MipBias;
            log.Info($"Enabled product texture mip bias {bias}; the normal renderer rebuild path and 64 px floor remain active.");
            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                Id,
                CompatibilityState.Compatible,
                "One RenderingSettingOption.Value read in ProductMeshTable.RebuildTextureArrays(bool)",
                $"Mip bias overridden to {bias} ({(bias == 3 ? "Low" : "Very Low")})",
                "Vanilla presets are unchanged; the normal texture-array rebuild and minimum slice clamp remain active."));
        }

        internal static IEnumerable<CodeInstruction> OverrideMipBiasRead(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo overrideMethod = AccessTools.Method(typeof(LowProductTexturesFeature), nameof(OverrideMipBias));
            var result = new List<CodeInstruction>();
            int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                result.Add(instruction);
                if (instruction.operand is FieldInfo field &&
                    field.Name == "Value" &&
                    field.DeclaringType?.FullName == "Mafi.Unity.RenderingSettingOption")
                {
                    result.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Call, overrideMethod));
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

        private static int OverrideMipBias(int vanillaMipBias) => LowProductTexturesSettings.MipBias;
    }
}
