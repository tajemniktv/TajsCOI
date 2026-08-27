// Taj's COI Mods | RuntimeContractAssertions.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Xunit;

namespace TajsCOI.Tests.RuntimeContracts
{
    /// <summary>
    ///     Small, intentionally boring assertions for the runtime seams TajsCOI consumes.  These
    ///     helpers keep failure messages focused on the expected signature and the exact game
    ///     assembly set loaded by the test process; they are not a general reflection framework.
    /// </summary>
    internal static class RuntimeContractAssertions
    {
        private const BindingFlags AnyMethod =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static MethodInfo RequireMethod(
            Type declaringType,
            string name,
            Type returnType,
            bool isStatic,
            params Type[] parameterTypes)
        {
            MethodInfo[] candidates = declaringType
                .GetMethods(AnyMethod | BindingFlags.DeclaredOnly)
                .Where(method =>
                    string.Equals(method.Name, name, StringComparison.Ordinal) &&
                    method.IsStatic == isStatic &&
                    method.ReturnType == returnType &&
                    ParametersMatch(method, parameterTypes))
                .ToArray();

            string expected = FormatSignature(declaringType, name, returnType, isStatic, parameterTypes);
            Assert.True(
                candidates.Length == 1,
                "Runtime contract missing or ambiguous: " + expected + Environment.NewLine +
                "Loaded game context: " + GameAssemblyContext.Describe());
            return candidates[0];
        }

        internal static Type RequireType(Assembly assembly, string fullName)
        {
            Type? type = assembly?.GetType(fullName, throwOnError: false, ignoreCase: false);
            Assert.True(
                type is not null,
                "Runtime type contract missing: " + fullName + Environment.NewLine +
                "Loaded game context: " + GameAssemblyContext.Describe());
            return type!;
        }

        internal static Patches AssertHarmonyTarget(MethodBase target, string ownerId)
        {
            Patches? patches = Harmony.GetPatchInfo(target);
            Assert.True(
                patches is not null && HasOwner(patches, ownerId),
                "Harmony target contract missing owner '" + ownerId + "': " + target + Environment.NewLine +
                "Loaded game context: " + GameAssemblyContext.Describe());
            return patches!;
        }

        internal static void AssertHarmonyOwnerCount(MethodBase target, string ownerId, int expected)
        {
            Patches patches = AssertHarmonyTarget(target, ownerId);
            int count = (patches.Prefixes?.Count(patch => patch.owner == ownerId) ?? 0) +
                        (patches.Postfixes?.Count(patch => patch.owner == ownerId) ?? 0) +
                        (patches.Transpilers?.Count(patch => patch.owner == ownerId) ?? 0) +
                        (patches.Finalizers?.Count(patch => patch.owner == ownerId) ?? 0);
            Assert.Equal(expected, count);
        }

        internal static ConstructorInfo RequireConstructor(Type declaringType, params Type[] parameterTypes)
        {
            ConstructorInfo[] candidates = declaringType
                .GetConstructors(AnyMethod)
                .Where(constructor => ParametersMatch(constructor, parameterTypes))
                .ToArray();
            string expected = declaringType.FullName + ".ctor(" + FormatParameters(parameterTypes) + ")";
            Assert.True(
                candidates.Length == 1,
                "Runtime contract missing or ambiguous: " + expected + Environment.NewLine +
                "Loaded game context: " + GameAssemblyContext.Describe());
            return candidates[0];
        }

        internal static FieldInfo RequireField(
            Type declaringType,
            string name,
            Type fieldType,
            bool isStatic)
        {
            FieldInfo? field = declaringType.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(
                field is not null && field.FieldType == fieldType && field.IsStatic == isStatic,
                "Runtime field contract mismatch: " + declaringType.FullName + "." + name +
                " expected " + fieldType.FullName + ", static=" + isStatic + Environment.NewLine +
                "Observed: " + (field is null ? "<missing>" : field.FieldType.FullName + ", static=" + field.IsStatic) +
                Environment.NewLine + "Loaded game context: " + GameAssemblyContext.Describe());
            return field!;
        }

        internal static PropertyInfo RequireProperty(
            Type declaringType,
            string name,
            Type propertyType,
            bool isStatic,
            bool requireGetter = true,
            bool requireSetter = false)
        {
            PropertyInfo? property = declaringType.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? getter = property?.GetGetMethod(true);
            MethodInfo? setter = property?.GetSetMethod(true);
            Assert.True(
                property is not null && property.PropertyType == propertyType &&
                (!requireGetter || getter is not null) && (!requireSetter || setter is not null) &&
                (getter ?? setter)?.IsStatic == isStatic,
                "Runtime property contract mismatch: " + declaringType.FullName + "." + name +
                " expected " + propertyType.FullName + ", static=" + isStatic + Environment.NewLine +
                "Loaded game context: " + GameAssemblyContext.Describe());
            return property!;
        }

        internal static Assembly RequireAssembly(string name)
        {
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, name, StringComparison.Ordinal));
            if (assembly is null)
            {
                try
                {
                    assembly = Assembly.Load(name);
                }
                catch (FileNotFoundException)
                {
                    // The assertion below supplies the contract-specific context.
                }
            }
            Assert.True(
                assembly is not null,
                "Required game assembly '" + name + "' is not loaded." + Environment.NewLine +
                "Loaded game context: " + GameAssemblyContext.Describe());
            return assembly!;
        }

        internal static string FormatSignature(
            Type declaringType,
            string name,
            Type returnType,
            bool isStatic,
            IReadOnlyList<Type> parameterTypes) =>
            (isStatic ? "static " : string.Empty) + returnType.FullName + " " +
            declaringType.FullName + "." + name + "(" + FormatParameters(parameterTypes) + ")";

        private static bool ParametersMatch(MethodBase method, IReadOnlyList<Type> expected)
        {
            ParameterInfo[] actual = method.GetParameters();
            return actual.Length == expected.Count && actual.Select(parameter => parameter.ParameterType).SequenceEqual(expected);
        }

        private static string FormatParameters(IEnumerable<Type> parameterTypes) =>
            string.Join(", ", parameterTypes.Select(type => type.FullName));

        private static bool HasOwner(Patches patches, string ownerId) =>
            (patches.Prefixes?.Any(patch => patch.owner == ownerId) ?? false) ||
            (patches.Postfixes?.Any(patch => patch.owner == ownerId) ?? false) ||
            (patches.Transpilers?.Any(patch => patch.owner == ownerId) ?? false) ||
            (patches.Finalizers?.Any(patch => patch.owner == ownerId) ?? false);
    }

    internal static class GameAssemblyContext
    {
        private static readonly string[] RequiredAssemblies = { "Mafi", "Mafi.Core", "Mafi.Base", "Mafi.Unity" };

        static GameAssemblyContext()
        {
            // Emit the exact resolver context once when the contract assembly starts using it;
            // this makes CI failures actionable even when no individual assertion is reached.
            Console.WriteLine("[RuntimeContracts] " + Describe());
        }

        internal static string Describe()
        {
            string root = Environment.GetEnvironmentVariable("COI_ROOT") ?? "<COI_ROOT unset>";
            var builder = new StringBuilder(root);
            foreach (string name in RequiredAssemblies)
            {
                Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, name, StringComparison.Ordinal));
                builder.Append(" | ").Append(name).Append('=');
                if (assembly is null)
                {
                    builder.Append("<not loaded>");
                    continue;
                }

                builder.Append(assembly.GetName().Version?.ToString() ?? "<no version>");
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    builder.Append('@').Append(assembly.Location);
                }
            }

            return builder.ToString();
        }
    }
}
