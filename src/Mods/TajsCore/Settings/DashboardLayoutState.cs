// Taj's COI Mods | DashboardLayoutState.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TajsCOI.Core.Settings
{
    internal static class DashboardLayoutState
    {
        private const string Header = "TajsCoreDashboardLayoutV1";

        internal static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Captain of Industry",
            "TajsCOI",
            "dashboard-layout.txt");

        internal static bool TryLoad(out float width, out float height)
            => TryLoad(FilePath, out width, out height);

        internal static bool TryLoad(string path, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 3 || lines[0] != Header ||
                    !float.TryParse(lines[1], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    !float.TryParse(lines[2], NumberStyles.Float, CultureInfo.InvariantCulture, out height) ||
                    !IsFinite(width) || !IsFinite(height) || width <= 0f || height <= 0f)
                {
                    width = 0f;
                    height = 0f;
                    return false;
                }
                return true;
            }
            catch
            {
                width = 0f;
                height = 0f;
                return false;
            }
        }

        internal static bool TrySave(float width, float height)
            => TrySave(FilePath, width, height);

        internal static bool TrySave(string path, float width, float height)
        {
            if (!IsFinite(width) || !IsFinite(height) || width <= 0f || height <= 0f)
            {
                return false;
            }

            string? temporary = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                temporary = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllLines(
                    temporary,
                    new[] { Header, width.ToString("R", CultureInfo.InvariantCulture), height.ToString("R", CultureInfo.InvariantCulture) },
                    new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    File.Replace(temporary, fullPath, fullPath + ".bak", true);
                }
                else
                {
                    File.Move(temporary, fullPath);
                }
                temporary = null;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (temporary is not null)
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }
                }
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
