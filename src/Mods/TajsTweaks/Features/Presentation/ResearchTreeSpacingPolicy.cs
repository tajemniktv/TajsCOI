// Taj's COI Mods | ResearchTreeSpacingPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    ///     Captures the 0.8.7b research layout inputs. ResearchWindow stores raw grid coordinates
    ///     and applies these steps when it calculates both node positions and connector paths.
    /// </summary>
    internal readonly struct ResearchTreeSpacingPolicy
    {
        internal const int VanillaHorizontalStep = 366 / 3;
        internal const int VanillaVerticalStep = 165 / 3;
        internal const int CompactHorizontalStep = 98;
        internal const int CompactVerticalStep = 44;

        internal readonly int HorizontalStep;
        internal readonly int VerticalStep;

        private ResearchTreeSpacingPolicy(int horizontalStep, int verticalStep)
        {
            HorizontalStep = horizontalStep;
            VerticalStep = verticalStep;
        }

        internal bool IsCompact => HorizontalStep != VanillaHorizontalStep || VerticalStep != VanillaVerticalStep;

        internal static ResearchTreeSpacingPolicy Resolve(string? mode)
        {
            return string.Equals(mode, "compact", StringComparison.Ordinal)
                ? new ResearchTreeSpacingPolicy(CompactHorizontalStep, CompactVerticalStep)
                : new ResearchTreeSpacingPolicy(VanillaHorizontalStep, VanillaVerticalStep);
        }

        internal (int X, int Y) Apply(int gridX, int gridY) =>
            (gridX * HorizontalStep, gridY * VerticalStep);
    }
}
