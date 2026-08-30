// Taj's COI Mods | PolygonConstraintMath.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Tweaks.Features.Selection
{
    internal readonly struct PolygonVector2 : IEquatable<PolygonVector2>
    {
        internal float X { get; }
        internal float Y { get; }

        internal PolygonVector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(PolygonVector2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is PolygonVector2 other && Equals(other);

        public override int GetHashCode() => (X, Y).GetHashCode();

        public static bool operator ==(PolygonVector2 left, PolygonVector2 right) => left.Equals(right);

        public static bool operator !=(PolygonVector2 left, PolygonVector2 right) => !left.Equals(right);
    }

    /// <summary>
    ///     Pure drag-coordinate constraints shared by polygon area editors.
    ///     The caller supplies the unmodified cursor position on every update; this
    ///     helper never turns the constrained result into a new drag origin.
    /// </summary>
    internal static class PolygonConstraintMath
    {
        internal static PolygonVector2 Apply(
            PolygonVector2 dragOrigin,
            PolygonVector2 intendedPosition,
            bool axisConstraintHeld,
            bool gridSnapHeld,
            float gridSize = 1f)
        {
            if (gridSnapHeld && gridSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(gridSize), "Grid size must be positive.");
            }

            float deltaX = intendedPosition.X - dragOrigin.X;
            float deltaY = intendedPosition.Y - dragOrigin.Y;
            bool constrainX = false;
            bool constrainY = false;
            float constrainedX = intendedPosition.X;
            float constrainedY = intendedPosition.Y;

            if (axisConstraintHeld)
            {
                // Ties deliberately choose X so a perfectly diagonal drag is stable.
                constrainY = Math.Abs(deltaX) >= Math.Abs(deltaY);
                constrainX = !constrainY;
                if (constrainY)
                {
                    constrainedY = dragOrigin.Y;
                }
                else
                {
                    constrainedX = dragOrigin.X;
                }
            }

            if (gridSnapHeld)
            {
                // Snap from the original intended cursor position. The coordinate
                // locked by axis mode remains locked and is never rounded away from
                // the immutable drag origin.
                if (!constrainX)
                {
                    constrainedX = Snap(intendedPosition.X, gridSize);
                }
                if (!constrainY)
                {
                    constrainedY = Snap(intendedPosition.Y, gridSize);
                }
            }

            return new PolygonVector2(constrainedX, constrainedY);
        }

        private static float Snap(float value, float gridSize) => (float)(Math.Round(value / gridSize, MidpointRounding.AwayFromZero) * gridSize);
    }
}
