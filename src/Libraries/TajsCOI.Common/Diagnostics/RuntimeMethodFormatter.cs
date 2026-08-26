// Taj's COI Mods | RuntimeMethodFormatter.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Linq;
using System.Reflection;

namespace TajsCOI.Common.Diagnostics
{
    /// <summary>
    ///     Formats a method using only immutable reflection metadata. It has no dependency on
    ///     Harmony or the game and is shared by Core-owned diagnostics and their consumers.
    /// </summary>
    public static class RuntimeMethodFormatter
    {
        public static string Format(MethodBase? method)
        {
            if (method is null)
            {
                return "<unknown>";
            }

            string declaringType = method.DeclaringType?.FullName ?? "<global>";
            string parameters = string.Join(
                ", ",
                method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name));
            return declaringType + "." + method.Name + "(" + parameters + ")";
        }
    }
}
