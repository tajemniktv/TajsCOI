// Taj's COI Mods | SaveLoadReadBufferFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
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

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            ConstructorInfo? target = typeof(BlobReader).GetConstructor(new[]
            {
                typeof(System.IO.Stream),
                typeof(int),
                typeof(Mafi.Collections.ImmutableCollections.ImmutableArray<ISpecialSerializerFactory>),
            });
            if (target is null)
            {
                throw new MissingMethodException(typeof(BlobReader).FullName, ".ctor(Stream, int, ImmutableArray<ISpecialSerializerFactory>)");
            }

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(
                target,
                transpiler: new HarmonyMethod(typeof(SaveLoadReadBufferFeature), nameof(ReplaceBufferSize)));

            int kibibytes = SaveLoadReadBufferSettings.BufferBytes / 1024;
            log.Info($"Enabled {kibibytes} KiB buffered save reader; vanilla checksum and loading paths remain active.");
            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                Id,
                CompatibilityState.Compatible,
                "Exactly one 4 KiB BlobReader buffer-size constant in the 0.8.7a constructor",
                $"Replaced with {kibibytes} KiB",
                "Opt-in read-buffer change installed; checksum preflight and deserialization behavior are unchanged."));
        }

        internal static IEnumerable<CodeInstruction> ReplaceBufferSize(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsConstant(SaveLoadReadBufferSettings.VanillaBufferBytes))
                {
                    var replacement = new CodeInstruction(OpCodes.Ldc_I4, SaveLoadReadBufferSettings.BufferBytes);
                    replacement.labels.AddRange(instruction.labels);
                    replacement.blocks.AddRange(instruction.blocks);
                    result.Add(replacement);
                    replacements++;
                }
                else
                {
                    result.Add(instruction);
                }
            }

            if (replacements != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one {SaveLoadReadBufferSettings.VanillaBufferBytes}-byte BlobReader buffer constant, found {replacements}.");
            }
            return result;
        }
    }
}
