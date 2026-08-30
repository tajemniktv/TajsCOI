// Taj's COI Mods | RecipePickerLayoutPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    ///     Pure policy for the dedicated RecipePicker presentation. Vanilla values are taken from
    ///     the 0.8.7b RecipeUi/RecipesColumn seam: 36px product icons, 1pt (4px) card gap, one
    ///     vertical column. The custom values are clamped before they reach UI code.
    /// </summary>
    internal readonly struct RecipePickerLayoutPolicy
    {
        internal const int VanillaTileSize = 36;
        internal const double VanillaSpacingPoints = 1;
        internal const int VanillaColumns = 1;
        internal const float CompactTileScale = 0.75f;
        internal const int CompactColumns = 2;

        internal readonly float TileScale;
        internal readonly double SpacingPoints;
        internal readonly int Columns;

        private RecipePickerLayoutPolicy(float tileScale, double spacingPoints, int columns)
        {
            TileScale = tileScale;
            SpacingPoints = spacingPoints;
            Columns = columns;
        }

        internal bool IsVanilla => Math.Abs(TileScale - 1f) < 0.0001f &&
                                   Math.Abs(SpacingPoints - VanillaSpacingPoints) < 0.0001 &&
                                   Columns == VanillaColumns;

        internal static RecipePickerLayoutPolicy Resolve(string? density, int tileSize, double spacingPoints, int columns)
        {
            if (string.Equals(density, "compact", StringComparison.Ordinal))
            {
                return new RecipePickerLayoutPolicy(CompactTileScale, 0, CompactColumns);
            }

            if (!string.Equals(density, "custom", StringComparison.Ordinal))
            {
                return new RecipePickerLayoutPolicy(1f, VanillaSpacingPoints, VanillaColumns);
            }

            int safeTileSize = Math.Max(24, Math.Min(72, tileSize));
            double safeSpacing = double.IsNaN(spacingPoints) || double.IsInfinity(spacingPoints)
                ? VanillaSpacingPoints
                : Math.Max(0, Math.Min(8, spacingPoints));
            int safeColumns = Math.Max(1, Math.Min(4, columns));
            return new RecipePickerLayoutPolicy(safeTileSize / (float)VanillaTileSize, safeSpacing, safeColumns);
        }
    }
}
