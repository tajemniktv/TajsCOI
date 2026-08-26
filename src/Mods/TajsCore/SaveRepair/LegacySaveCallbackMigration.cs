// Taj's COI Mods | LegacySaveCallbackMigration.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mafi;

namespace TajsCOI.Core.SaveRepair
{
    /// <summary>
    ///     Removes saveable callbacks and resolver-owned instances left by a legacy mod. A
    ///     successful migration must remove both the live callback and every serialized owner
    ///     reference; removing only the live delegate is not enough for a future load without the
    ///     legacy assembly.
    /// </summary>
    internal static class LegacySaveCallbackMigration
    {
        private const BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool TryDetachCallback(
            IEvent source,
            Type ownerType,
            MethodInfo callbackMethod,
            out bool callbackWasRegistered,
            out List<object> callbackOwners,
            out string failure)
        {
            callbackWasRegistered = false;
            callbackOwners = new List<object>();
            failure = string.Empty;

            MethodInfo? isAddedDefinition = FindEventMethod("IsAdded");
            MethodInfo? removeDefinition = FindEventMethod("Remove");
            if (isAddedDefinition is null || removeDefinition is null)
            {
                failure = "the 0.8.7b event API was not available";
                return false;
            }

            MethodInfo remove = removeDefinition.MakeGenericMethod(ownerType);
            if (!TryFindCallbackOwners(source, ownerType, callbackMethod.Name, out List<object> activeCallbackOwners, out failure))
            {
                return false;
            }

            foreach (object callbackOwner in activeCallbackOwners)
            {
                var callback = (Action)Delegate.CreateDelegate(typeof(Action), callbackOwner, callbackMethod);
                remove.Invoke(source, new object[] { callbackOwner, callback });
                if (IsCallbackRegistered(source, ownerType, callbackOwner, callbackMethod) ||
                    IsCallbackSaveDataRegistered(source, ownerType, callbackOwner, callbackMethod.Name))
                {
                    failure = "a legacy callback or its serialized save data could not be removed";
                    return false;
                }

                callbackWasRegistered = true;
                if (!ContainsReference(callbackOwners, callbackOwner))
                {
                    callbackOwners.Add(callbackOwner);
                }
            }

            return true;
        }

        /// <summary>
        ///     The legacy versions are known to use particular events, but scanning direct event
        ///     fields catches duplicate registrations from a partially loaded or older version
        ///     without walking the entire factory object graph.
        /// </summary>
        internal static bool TryDetachCallbacksFromResolvedEvents(
            DependencyResolver resolver,
            IEvent preferredSource,
            Type ownerType,
            MethodInfo callbackMethod,
            out bool callbackWasRegistered,
            out List<object> callbackOwners,
            out string failure)
        {
            callbackWasRegistered = false;
            callbackOwners = new List<object>();
            failure = string.Empty;

            var sources = new List<IEvent>();
            AddEventSource(sources, preferredSource);

            try
            {
                foreach (object resolvedObject in resolver.GetAllResolvedObjects())
                {
                    AddEventFields(sources, resolvedObject);
                }

                foreach (object resolvedInstance in resolver.AllResolvedInstances)
                {
                    AddEventFields(sources, resolvedInstance);
                }
            }
            catch (Exception exception)
            {
                failure = "the resolver event graph could not be inspected: " + exception.GetType().Name;
                return false;
            }

            foreach (IEvent source in sources)
            {
                if (!TryDetachCallback(
                        source,
                        ownerType,
                        callbackMethod,
                        out bool sourceCallbackWasRegistered,
                        out List<object> sourceOwners,
                        out failure))
                {
                    return false;
                }

                callbackWasRegistered |= sourceCallbackWasRegistered;
                foreach (object sourceOwner in sourceOwners)
                {
                    if (!ContainsReference(callbackOwners, sourceOwner))
                    {
                        callbackOwners.Add(sourceOwner);
                    }
                }
            }

            return true;
        }

        internal static bool TryInspectCallbacksFromResolvedEvents(
            DependencyResolver resolver,
            IEvent preferredSource,
            Type ownerType,
            string methodName,
            out int callbackCount,
            out int eventCount,
            out string failure)
        {
            callbackCount = 0;
            eventCount = 0;
            failure = string.Empty;

            var sources = new List<IEvent>();
            AddEventSource(sources, preferredSource);

            try
            {
                foreach (object resolvedObject in resolver.GetAllResolvedObjects())
                {
                    AddEventFields(sources, resolvedObject);
                }

                foreach (object resolvedInstance in resolver.AllResolvedInstances)
                {
                    AddEventFields(sources, resolvedInstance);
                }
            }
            catch (Exception exception)
            {
                failure = "the resolver event graph could not be inspected: " + exception.GetType().Name;
                return false;
            }

            var owners = new List<object>();
            foreach (IEvent source in sources)
            {
                if (!TryFindCallbackOwners(source, ownerType, methodName, out List<object> sourceOwners, out failure))
                {
                    return false;
                }

                eventCount++;
                foreach (object sourceOwner in sourceOwners)
                {
                    if (!ContainsReference(owners, sourceOwner))
                    {
                        owners.Add(sourceOwner);
                    }
                }
            }

            callbackCount = owners.Count;
            return true;
        }

        internal static bool RemoveResolverEntries(
            DependencyResolver resolver,
            Type legacyType,
            IEnumerable<object> owners,
            out string failure)
        {
            failure = string.Empty;
            FieldInfo? registeredField = typeof(DependencyResolver).GetField(
                "m_resolvedInstancesByRegisteredType",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? realField = typeof(DependencyResolver).GetField(
                "m_resolvedInstancesByRealType",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? objectsField = typeof(DependencyResolver).GetField(
                "m_resolvedObjects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? multiInstanceField = typeof(DependencyResolver).GetField(
                "m_multiInstanceDeps",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo? disposableField = typeof(DependencyResolver).GetField(
                "m_instancedToBeDisposed",
                BindingFlags.Instance | BindingFlags.NonPublic);

            object? registeredMap = registeredField?.GetValue(resolver);
            object? realMap = realField?.GetValue(resolver);
            object? resolvedObjects = objectsField?.GetValue(resolver);
            object? multiInstanceDeps = multiInstanceField?.GetValue(resolver);
            object? disposableObjects = disposableField?.GetValue(resolver);

            MethodInfo? registeredRemove = registeredMap?.GetType().GetMethod("Remove", new[] { typeof(Type) });
            MethodInfo? realRemove = realMap?.GetType().GetMethod("Remove", new[] { typeof(Type) });
            MethodInfo? resolvedObjectRemove = resolvedObjects?.GetType().GetMethod("Remove", new[] { typeof(object) });
            MethodInfo? multiInstanceRemove = multiInstanceDeps?.GetType().GetMethod("Remove", new[] { typeof(object) });
            MethodInfo? disposableRemove = disposableObjects?.GetType().GetMethod("Remove", new[] { typeof(object) });

            if (registeredMap is null || realMap is null || resolvedObjects is null ||
                multiInstanceDeps is null || disposableObjects is null ||
                registeredRemove is null || realRemove is null || resolvedObjectRemove is null ||
                multiInstanceRemove is null || disposableRemove is null)
            {
                failure = "resolver compatibility fields are unavailable";
                return false;
            }

            var uniqueOwners = new List<object>();
            foreach (object owner in owners)
            {
                if (!ContainsReference(uniqueOwners, owner))
                {
                    uniqueOwners.Add(owner);
                }
            }

            AddResolvedObjectsOfType(resolvedObjects, legacyType, uniqueOwners);
            AddResolvedObjectsOfType(realMap, legacyType, uniqueOwners);
            AddResolvedObjectsOfType(registeredMap, legacyType, uniqueOwners);
            AddResolvedObjectsOfType(multiInstanceDeps, legacyType, uniqueOwners);
            AddResolvedObjectsOfType(disposableObjects, legacyType, uniqueOwners);

            if (!RemoveMapEntriesForLegacyObjects(registeredMap, registeredRemove, legacyType, uniqueOwners, out failure) ||
                !RemoveMapEntriesForLegacyObjects(realMap, realRemove, legacyType, uniqueOwners, out failure))
            {
                return false;
            }

            if (!RemoveObjectsFromCollection(resolvedObjects, resolvedObjectRemove, legacyType, uniqueOwners, out failure) ||
                !RemoveObjectsFromCollection(multiInstanceDeps, multiInstanceRemove, legacyType, uniqueOwners, out failure) ||
                !RemoveObjectsFromCollection(disposableObjects, disposableRemove, legacyType, uniqueOwners, out failure))
            {
                return false;
            }

            if (HasLegacyObjects(registeredMap, legacyType, uniqueOwners) ||
                HasLegacyObjects(realMap, legacyType, uniqueOwners) ||
                HasLegacyObjects(resolvedObjects, legacyType, uniqueOwners) ||
                HasLegacyObjects(multiInstanceDeps, legacyType, uniqueOwners) ||
                HasLegacyObjects(disposableObjects, legacyType, uniqueOwners))
            {
                failure = "the legacy object remained in one or more serialized resolver collections";
                return false;
            }

            return true;
        }

        private static MethodInfo? FindEventMethod(string name)
        {
            foreach (MethodInfo method in typeof(IEvent).GetMethods())
            {
                if (method.Name == name && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                    method.GetParameters().Length == 2)
                {
                    return method;
                }
            }

            return null;
        }

        private static bool TryFindCallbackOwners(
            IEvent source,
            Type ownerType,
            string methodName,
            out List<object> owners,
            out string failure)
        {
            owners = new List<object>();
            failure = string.Empty;
            PropertyInfo? callbacksProperty = FindInstanceProperty(source.GetType(), "Callbacks");
            object? callbackCollection = callbacksProperty?.GetValue(source);
            if (callbackCollection is not IEnumerable callbacks)
            {
                failure = "the active event callback list was unavailable";
                return false;
            }

            if (!AddCallbackOwners(callbacks, ownerType, methodName, owners))
            {
                failure = "the active event callback list had an unsupported shape";
                return false;
            }

            FieldInfo? saveDataField = FindInstanceField(source.GetType(), "m_callbacksSaveData");
            object? saveData = saveDataField?.GetValue(source);
            if (saveData is IEnumerable saveDataEntries &&
                !AddCallbackOwners(saveDataEntries, ownerType, methodName, owners))
            {
                failure = "the serialized event callback list had an unsupported shape";
                return false;
            }

            return true;
        }

        private static bool AddCallbackOwners(
            IEnumerable entries,
            Type ownerType,
            string methodName,
            List<object> owners)
        {
            foreach (object? callbackData in entries)
            {
                if (callbackData is null)
                {
                    continue;
                }

                FieldInfo? ownerField = callbackData.GetType().GetField("Owner", InstanceFieldFlags);
                FieldInfo? methodNameField = callbackData.GetType().GetField("MethodName", InstanceFieldFlags);
                FieldInfo? declaringTypeField = callbackData.GetType().GetField("DeclaringType", InstanceFieldFlags);
                if (ownerField is null || methodNameField is null)
                {
                    return false;
                }

                object? owner = ownerField.GetValue(callbackData);
                string? registeredMethodName = methodNameField.GetValue(callbackData) as string;
                object? declaringType = declaringTypeField?.GetValue(callbackData);
                if (owner is not null && owner.GetType() == ownerType && registeredMethodName == methodName &&
                    (declaringTypeField is null || Equals(declaringType, ownerType)) &&
                    !ContainsReference(owners, owner))
                {
                    owners.Add(owner);
                }
            }

            return true;
        }

        private static bool IsCallbackRegistered(
            IEvent source,
            Type ownerType,
            object owner,
            MethodInfo callbackMethod)
        {
            MethodInfo? isAddedDefinition = FindEventMethod("IsAdded");
            if (isAddedDefinition is null)
            {
                return true;
            }

            MethodInfo isAdded = isAddedDefinition.MakeGenericMethod(ownerType);
            var callback = (Action)Delegate.CreateDelegate(typeof(Action), owner, callbackMethod);
            return (bool)(isAdded.Invoke(source, new object[] { owner, callback }) ?? false);
        }

        private static bool IsCallbackSaveDataRegistered(IEvent source, Type ownerType, object owner, string methodName)
        {
            FieldInfo? saveDataField = FindInstanceField(source.GetType(), "m_callbacksSaveData");
            object? saveData = saveDataField?.GetValue(source);
            if (saveData is not IEnumerable entries)
            {
                return true;
            }

            foreach (object? entry in entries)
            {
                if (entry is null)
                {
                    continue;
                }

                FieldInfo? ownerField = entry.GetType().GetField("Owner", InstanceFieldFlags);
                FieldInfo? declaringTypeField = entry.GetType().GetField("DeclaringType", InstanceFieldFlags);
                FieldInfo? methodNameField = entry.GetType().GetField("MethodName", InstanceFieldFlags);
                if (ownerField is null || declaringTypeField is null || methodNameField is null)
                {
                    return true;
                }

                if (ReferenceEquals(ownerField.GetValue(entry), owner) &&
                    Equals(declaringTypeField.GetValue(entry), ownerType) &&
                    string.Equals(methodNameField.GetValue(entry) as string, methodName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static PropertyInfo? FindInstanceProperty(Type type, string name)
        {
            for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                PropertyInfo? property = current.GetProperty(name, InstanceFieldFlags | BindingFlags.DeclaredOnly);
                if (property is not null)
                {
                    return property;
                }
            }

            return null;
        }

        private static FieldInfo? FindInstanceField(Type type, string name)
        {
            for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo? field = current.GetField(name, InstanceFieldFlags | BindingFlags.DeclaredOnly);
                if (field is not null)
                {
                    return field;
                }
            }

            return null;
        }

        private static void AddEventSource(List<IEvent> sources, IEvent source)
        {
            foreach (IEvent existing in sources)
            {
                if (ReferenceEquals(existing, source))
                {
                    return;
                }
            }

            sources.Add(source);
        }

        private static void AddEventFields(List<IEvent> sources, object candidate)
        {
            if (candidate is IEvent directEvent)
            {
                AddEventSource(sources, directEvent);
            }

            for (Type? current = candidate.GetType(); current is not null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(InstanceFieldFlags | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic)
                    {
                        continue;
                    }

                    object? value;
                    try
                    {
                        value = field.GetValue(candidate);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value is IEvent eventSource)
                    {
                        AddEventSource(sources, eventSource);
                    }
                }
            }
        }

        private static void AddResolvedObjectsOfType(object collection, Type legacyType, List<object> owners)
        {
            if (collection is not IEnumerable entries)
            {
                return;
            }

            foreach (object? entry in entries)
            {
                object? value = GetCollectionValue(entry);
                if (value is not null && value.GetType() == legacyType && !ContainsReference(owners, value))
                {
                    owners.Add(value);
                }
            }
        }

        private static bool RemoveMapEntriesForLegacyObjects(
            object map,
            MethodInfo remove,
            Type legacyType,
            List<object> owners,
            out string failure)
        {
            failure = string.Empty;
            if (map is not IEnumerable entries)
            {
                failure = "a resolver map had an unsupported shape";
                return false;
            }

            var keys = new List<Type>();
            foreach (object? entry in entries)
            {
                if (entry is null)
                {
                    continue;
                }

                object? key = entry.GetType().GetProperty("Key")?.GetValue(entry);
                object? value = entry.GetType().GetProperty("Value")?.GetValue(entry);
                if (key is Type typedKey &&
                    (Equals(typedKey, legacyType) || IsLegacyObject(value, legacyType, owners)))
                {
                    keys.Add(typedKey);
                }
            }

            foreach (Type key in keys)
            {
                remove.Invoke(map, new object[] { key });
            }

            return true;
        }

        private static bool RemoveObjectsFromCollection(
            object collection,
            MethodInfo remove,
            Type legacyType,
            List<object> owners,
            out string failure)
        {
            failure = string.Empty;
            if (collection is not IEnumerable entries)
            {
                failure = "a resolver collection had an unsupported shape";
                return false;
            }

            var values = new List<object>();
            foreach (object? entry in entries)
            {
                if (entry is not null && IsLegacyObject(entry, legacyType, owners))
                {
                    values.Add(entry);
                }
            }

            foreach (object value in values)
            {
                remove.Invoke(collection, new[] { value });
            }

            return true;
        }

        private static bool HasLegacyObjects(object collection, Type legacyType, List<object> owners)
        {
            if (collection is not IEnumerable entries)
            {
                return true;
            }

            foreach (object? entry in entries)
            {
                if (IsLegacyObject(GetCollectionValue(entry), legacyType, owners))
                {
                    return true;
                }
            }

            return false;
        }

        private static object? GetCollectionValue(object? entry)
        {
            if (entry is null)
            {
                return null;
            }

            PropertyInfo? valueProperty = entry.GetType().GetProperty("Value");
            return valueProperty?.GetValue(entry) ?? entry;
        }

        private static bool IsLegacyObject(object? value, Type legacyType, List<object> owners)
        {
            if (value is null)
            {
                return false;
            }

            if (value.GetType() == legacyType)
            {
                return true;
            }

            foreach (object owner in owners)
            {
                if (ReferenceEquals(owner, value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsReference(IEnumerable<object> values, object value)
        {
            foreach (object candidate in values)
            {
                if (ReferenceEquals(candidate, value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
