// Taj's COI Mods | SaveLoadReadBufferFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mafi.Serialization;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance.Features.SaveLoadReadBuffer
{
    /// <summary>
    ///     Replaces only BlobReader's 4 KiB BufferedReadStream constructor argument. It does not
    ///     remove checksum preflight, change decompression, or alter serialization semantics.
    /// </summary>
    internal sealed class SaveLoadReadBufferFeature : IPerformanceFeature
    {
        private const string HarmonyId = "TajsCOI.Performance.SaveLoadReadBuffer";

        public string Id => "SaveLoadReadBuffer";

        public string ConfigKey => SaveLoadReadBufferSettings.EnableConfigKey;

        public bool IsProcessPatchInstalled()
        {
            ConstructorInfo? target = FindTarget();
            MethodInfo? patchMethod = AccessTools.Method(typeof(SaveLoadReadBufferFeature), nameof(ReplaceBufferSize));
            return target is not null && patchMethod is not null &&
                   ProcessHarmonyPatchOwnership.HasExpected(
                       Harmony.GetPatchInfo(target)?.Transpilers,
                       HarmonyId,
                       patchMethod);
        }

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            ConstructorInfo? target = FindTarget();
            if (target is null)
            {
                throw new MissingMethodException(typeof(BlobReader).FullName, ".ctor(Stream, int, ImmutableArray<ISpecialSerializerFactory>)");
            }

            // Target: BlobReader(Stream, int, ImmutableArray<ISpecialSerializerFactory>). This
            // behavior-changing transpiler identifies exactly one semantic BufferedReadStream
            // construction with the vanilla 4 KiB argument, throws before altered IL is returned
            // on mismatch, and remains installed for the process lifetime.
            MethodInfo patchMethod = AccessTools.Method(typeof(SaveLoadReadBufferFeature), nameof(ReplaceBufferSize))!;
            lock (s_installGate)
            {
                Patches? patches = Harmony.GetPatchInfo(target);
                if (ProcessHarmonyPatchOwnership.HasExpected(patches?.Transpilers, HarmonyId, patchMethod))
                {
                    log.Info("Already installed / compatible; the process-lifetime save reader patch was not applied again.");
                    runtime.ReportCompatibility(
                        new CompatibilityReport(
                            "TajsPerformance",
                            Id,
                            CompatibilityState.Compatible,
                            "Existing process-lifetime Harmony owner and transpiler method",
                            "Already installed / compatible",
                            "The validated 0.8.7a save-reader patch remains active; no duplicate transpiler was registered."));
                    return;
                }

                if (ProcessHarmonyPatchOwnership.HasOwner(patches?.Transpilers, HarmonyId))
                {
                    throw new InvalidOperationException(
                        $"Existing Harmony owner '{HarmonyId}' has an unexpected save-reader transpiler ({ProcessHarmonyPatchOwnership.Describe(patches)}).");
                }

                var harmony = new Harmony(HarmonyId);
                try
                {
                    harmony.Patch(target, transpiler: new HarmonyMethod(patchMethod));
                }
                catch (Exception exception)
                {
                    harmony.Unpatch(target, HarmonyPatchType.Transpiler, HarmonyId);
                    throw new InvalidOperationException(
                        $"Save-reader semantic IL validation failed; existing Harmony patches were {ProcessHarmonyPatchOwnership.Describe(patches)}.",
                        exception);
                }
            }

            int kibibytes = SaveLoadReadBufferSettings.BufferBytes / 1024;
            log.Info($"Enabled {kibibytes} KiB buffered save reader; vanilla checksum and loading paths remain active.");
            runtime.ReportCompatibility(
                new CompatibilityReport(
                    "TajsPerformance",
                    Id,
                    CompatibilityState.Compatible,
                    "Exactly one 4 KiB BlobReader buffer-size constant in the 0.8.7a constructor",
                    $"Replaced with {kibibytes} KiB",
                    "Opt-in read-buffer change installed; checksum preflight and deserialization behavior are unchanged."));
        }

        private static readonly object s_installGate = new();

        private static ConstructorInfo? FindTarget() => typeof(BlobReader).GetConstructor(
            new[] { typeof(System.IO.Stream), typeof(int), typeof(Mafi.Collections.ImmutableCollections.ImmutableArray<ISpecialSerializerFactory>) });

        internal static IEnumerable<CodeInstruction> ReplaceBufferSize(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> input = instructions.ToList();
            var result = new List<CodeInstruction>(input);
            ConstructorInfo? bufferedReader = typeof(BufferedReadStream).GetConstructor(new[] { typeof(System.IO.Stream), typeof(int), typeof(bool) });
            if (bufferedReader is null)
            {
                throw new MissingMethodException(typeof(BufferedReadStream).FullName, ".ctor(Stream, int, bool)");
            }

            int replacements = 0;
            int constructionSites = 0;
            var relevantSites = new List<string>();
            for (int index = 0; index < input.Count; index++)
            {
                CodeInstruction instruction = input[index];
                if (instruction.opcode != OpCodes.Newobj || instruction.operand is not ConstructorInfo constructor ||
                    constructor != bufferedReader)
                {
                    continue;
                }

                constructionSites++;
                relevantSites.Add(DescribeSite(input, index));
                if (index < 2 || !input[index - 2].LoadsConstant(SaveLoadReadBufferSettings.VanillaBufferBytes) ||
                    !input[index - 1].LoadsConstant(1))
                {
                    continue;
                }

                CodeInstruction vanillaBuffer = input[index - 2];
                var replacement = new CodeInstruction(OpCodes.Ldc_I4, SaveLoadReadBufferSettings.BufferBytes);
                replacement.labels.AddRange(vanillaBuffer.labels);
                replacement.blocks.AddRange(vanillaBuffer.blocks);
                result[index - 2] = replacement;
                replacements++;
            }

            if (constructionSites != 1 || replacements != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one semantic BufferedReadStream(Stream, {SaveLoadReadBufferSettings.VanillaBufferBytes}, true) construction with the vanilla buffer argument; " +
                    $"found {constructionSites} construction site(s) and {replacements} replaceable site(s). " +
                    $"Relevant IL: {string.Join(" | ", relevantSites)}");
            }
            return result;
        }

        private static string DescribeSite(IReadOnlyList<CodeInstruction> instructions, int constructorIndex)
        {
            int first = Math.Max(0, constructorIndex - 3);
            int last = Math.Min(instructions.Count - 1, constructorIndex);
            var parts = new List<string>();
            for (int index = first; index <= last; index++)
            {
                CodeInstruction instruction = instructions[index];
                parts.Add($"{index}:{instruction.opcode.Name}({instruction.operand ?? "-"})");
            }
            return string.Join(",", parts);
        }
    }
}
