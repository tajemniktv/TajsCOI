// Taj's COI Mods | SafeAreaCleanupProductCmd.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Serialization;

namespace TajsCOI.Tweaks.Features.Cleanup
{
    /// <summary>
    ///     Value-only product cleanup request. The processor resolves the entity and product at
    ///     execution time, so a stale rectangle selection cannot mutate a replacement entity.
    /// </summary>
    [GenerateSerializer(false, null, 0, null)]
    internal sealed class SafeAreaCleanupProductCmd : InputCommand
    {
        internal readonly EntityId EntityId;
        internal readonly ProductProto.ID ProductId;
        internal readonly Quantity Quantity;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((SafeAreaCleanupProductCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((SafeAreaCleanupProductCmd)obj).DeserializeData(reader);

        internal SafeAreaCleanupProductCmd(EntityId entityId, ProductProto.ID productId, Quantity quantity)
        {
            EntityId = entityId;
            ProductId = productId;
            Quantity = quantity;
        }

        public static void Serialize(SafeAreaCleanupProductCmd value, BlobWriter writer)
        {
            if (writer.TryStartClassSerialization(value))
            {
                writer.EnqueueDataSerialization(value, s_serializeData);
            }
        }

        protected override void SerializeData(BlobWriter writer)
        {
            base.SerializeData(writer);
            EntityId.Serialize(EntityId, writer);
            ProductProto.ID.Serialize(ProductId, writer);
            Quantity.Serialize(Quantity, writer);
        }

        public new static SafeAreaCleanupProductCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out SafeAreaCleanupProductCmd? obj,
                    (Func<BlobReader, Type, SafeAreaCleanupProductCmd>?)null,
                    (Func<BlobReader, string, SafeAreaCleanupProductCmd>?)null,
                    false))
            {
                reader.EnqueueDataDeserialization(obj!, s_deserializeData);
            }
            return obj!;
        }

        protected override void DeserializeData(BlobReader reader)
        {
            base.DeserializeData(reader);
            reader.SetField(this, "EntityId", EntityId.Deserialize(reader));
            reader.SetField(this, "ProductId", ProductProto.ID.Deserialize(reader));
            reader.SetField(this, "Quantity", Quantity.Deserialize(reader));
        }
    }

    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class SafeAreaCleanupCommandsProcessor :
        ICommandProcessor<SafeAreaCleanupProductCmd>,
        IAction<SafeAreaCleanupProductCmd>
    {
        private readonly IEntitiesManager m_entities;
        private readonly ProtosDb m_protos;
        private readonly IProductsManager m_products;

        internal SafeAreaCleanupCommandsProcessor(
            IEntitiesManager entities,
            ProtosDb protos,
            IProductsManager products)
        {
            m_entities = entities ?? throw new ArgumentNullException(nameof(entities));
            m_protos = protos ?? throw new ArgumentNullException(nameof(protos));
            m_products = products ?? throw new ArgumentNullException(nameof(products));
        }

        public void Invoke(SafeAreaCleanupProductCmd command)
        {
            if (!command.EntityId.IsValid || command.Quantity.IsNotPositive)
            {
                command.SetResultError("The cleanup request was malformed.");
                return;
            }
            if (!m_protos.TryGetProto<ProductProto>(command.ProductId, out ProductProto? product) || product is null ||
                !m_products.CanBeCleared(product))
            {
                command.SetResultError("The product is not available for safe cleanup.");
                return;
            }
            if (!m_entities.TryGetEntity<IStaticEntity>(command.EntityId, out IStaticEntity? entity) ||
                entity is null || entity.IsDestroyed)
            {
                command.SetResultError("The selected entity no longer exists.");
                return;
            }

            Quantity remaining = command.Quantity;
            Quantity removed = Quantity.Zero;
            foreach (IProductBuffer buffer in EnumerateMutableBuffers(entity))
            {
                if (remaining.IsNotPositive || buffer.Product.Id != product.Id || buffer.Quantity.IsNotPositive)
                {
                    continue;
                }

                Quantity request = buffer.Quantity.Min(remaining);
                Quantity removedFromBuffer = buffer.RemoveAsMuchAs(request);
                if (removedFromBuffer.IsPositive)
                {
                    removed += removedFromBuffer;
                    remaining -= removedFromBuffer;
                }
            }

            // ProductCleared is the native, policy-aware accounting boundary. Buffer mutation
            // without this report would leave global product statistics inconsistent.
            if (removed.IsPositive)
            {
                m_products.ProductCleared(product, removed);
                command.SetResultSuccess();
            }
            else
            {
                command.SetResultError("The selected product quantity is no longer present.");
            }
        }

        internal static IEnumerable<IProductBuffer> EnumerateMutableBuffers(IStaticEntity entity)
        {
            var seen = new HashSet<IProductBuffer>();
            foreach (IProductBufferReadOnly buffer in EnumerateReadOnlyBuffers(entity))
            {
                if (buffer is IProductBuffer mutable && seen.Add(mutable))
                {
                    yield return mutable;
                }
            }
        }

        internal static IEnumerable<IProductBufferReadOnly> EnumerateReadOnlyBuffers(IStaticEntity entity)
        {
            if (entity is IEntityWithStorageBuffersForUi storage)
            {
                foreach (IProductBufferReadOnly buffer in storage.StorageBuffers)
                {
                    yield return buffer;
                }
            }
            if (entity is IEntityWithInputBuffersForUi input)
            {
                foreach (IProductBufferReadOnly buffer in input.InputBuffers)
                {
                    yield return buffer;
                }
            }
            if (entity is IEntityWithOutputBuffersForUi output)
            {
                foreach (IProductBufferReadOnly buffer in output.OutputBuffers)
                {
                    yield return buffer;
                }
            }
            foreach (IProductBufferReadOnly buffer in entity.GetConstructionBuffers())
            {
                yield return buffer;
            }
        }
    }
}
