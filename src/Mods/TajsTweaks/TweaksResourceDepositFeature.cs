// Taj's COI Mods | TweaksResourceDepositFeature.cs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Unity.InputControl.ResVis;
using UnityEngine;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Builds a small, scene-owned index over the chunks already selected by the native
    ///     resource-visibility renderer. Labels are reused when the cluster topology is stable;
    ///     native resource-renderer dirty chunks are debounced and only intersecting clusters are
    ///     resampled after a terrain/resource change.
    /// </summary>
    internal static class TweaksResourceDepositFeature
    {
        private static WeakReference<ResourceDepositLabelController>? s_controller;

        internal static void Install(Harmony _)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (typeof(ResVisBarsRenderer).GetField("m_productsVisibility", flags) is null ||
                typeof(ResVisBarsRenderer).GetField("m_chunksWithProducts", flags) is null)
            {
                throw new MissingMemberException(typeof(ResVisBarsRenderer).FullName, "resource visibility indexes");
            }

            MethodInfo markChunkDirty = AccessTools.Method(typeof(ResVisBarsRenderer), "markChunkDirty")
                ?? throw new MissingMethodException(typeof(ResVisBarsRenderer).FullName, "markChunkDirty");
            _.Patch(markChunkDirty, postfix: new HarmonyMethod(typeof(TweaksResourceDepositFeature), nameof(NativeChunkDirty)));
        }

        private static void NativeChunkDirty(ResVisBarsRenderer __instance, Chunk2i chunk)
        {
            if (TryGetController(out ResourceDepositLabelController? controller) &&
                controller is not null && controller.IsForRenderer(__instance))
            {
                controller.MarkDirty(chunk);
            }
        }

        internal static void Tick(DependencyResolver resolver)
        {
            if (!TajsTweaksRuntimeState.ResourceOverlay || !TajsTweaksRuntimeState.ResourceOverlayDepth)
            {
                if (TryGetController(out ResourceDepositLabelController? disabled) && disabled is not null)
                {
                    disabled.SetFeatureEnabled(false);
                }
                return;
            }

            if (!resolver.TryResolve(out TerrainManager terrain) ||
                !resolver.TryResolve(out ResVisBarsRenderer renderer))
            {
                return;
            }

            if (TryGetController(out ResourceDepositLabelController? current) &&
                current is not null && current.IsFor(terrain, renderer))
            {
                current.SetFeatureEnabled(true);
                return;
            }

            if (current is not null)
            {
                UnityEngine.Object.Destroy(current.gameObject);
            }

            GameObject owner = new GameObject("Tajs resource deposit labels");
            ResourceDepositLabelController created = owner.AddComponent<ResourceDepositLabelController>();
            created.Initialize(terrain, renderer);
            s_controller = new WeakReference<ResourceDepositLabelController>(created);
        }

        internal static void Dispose()
        {
            if (TryGetController(out ResourceDepositLabelController? controller) && controller is not null)
            {
                UnityEngine.Object.Destroy(controller.gameObject);
            }

            s_controller = null;
        }

        private static bool TryGetController(out ResourceDepositLabelController? controller)
        {
            controller = null;
            if (s_controller is null || !s_controller.TryGetTarget(out controller) || controller == null)
            {
                s_controller = null;
                controller = null;
                return false;
            }

            return true;
        }
    }

    internal sealed class ResourceDepositLabelController : MonoBehaviour
    {
        private sealed class DepositCluster
        {
            internal LooseProductProto Product = null!;
            internal readonly HashSet<Chunk2i> Chunks = new HashSet<Chunk2i>();
            internal string Key = string.Empty;
            internal Vector3 WorldCenter;
            internal float SurfaceHeight;
            internal float ResourceTop;
            internal float MaxDepth;
            internal GameObject? Label;
        }

        private const int ChunkSize = 64;
        private const int SampleStep = 4;
        private const int MinimumSampleCount = 20;
        private const float DirtyDebounceSeconds = 0.5f;
        private const int MaximumClusters = 512;

        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private TerrainManager? m_terrain;
        private ResVisBarsRenderer? m_renderer;
        private FieldInfo? m_visibilityField;
        private FieldInfo? m_chunksField;
        private readonly List<DepositCluster> m_clusters = new List<DepositCluster>();
        private readonly Dictionary<LooseProductProto, object> m_nativeChunkSets = new Dictionary<LooseProductProto, object>();
        private readonly Dictionary<LooseProductProto, HashSet<Chunk2i>> m_productChunks = new Dictionary<LooseProductProto, HashSet<Chunk2i>>();
        private readonly HashSet<ProductProto> m_visibleProducts = new HashSet<ProductProto>();
        private readonly HashSet<ProductProto> m_previousVisibleProducts = new HashSet<ProductProto>();
        private readonly HashSet<Chunk2i> m_dirtyChunks = new HashSet<Chunk2i>();
        private readonly object m_dirtyGate = new object();
        private bool m_enabled;
        private bool m_cacheBuilt;
        private bool m_wasActive;
        private bool m_hasDirty;
        private float m_dirtyTimer;
        private int m_areaStartX;
        private int m_areaStartY;
        private int m_areaEndX;
        private int m_areaEndY;

        internal void Initialize(TerrainManager terrain, ResVisBarsRenderer renderer)
        {
            m_terrain = terrain;
            m_renderer = renderer;
            m_visibilityField = typeof(ResVisBarsRenderer).GetField("m_productsVisibility", InstanceFlags);
            m_chunksField = typeof(ResVisBarsRenderer).GetField("m_chunksWithProducts", InstanceFlags);
            m_enabled = true;
        }

        internal bool IsFor(TerrainManager terrain, ResVisBarsRenderer renderer) =>
            ReferenceEquals(m_terrain, terrain) && ReferenceEquals(m_renderer, renderer);

        internal bool IsForRenderer(ResVisBarsRenderer renderer) => ReferenceEquals(m_renderer, renderer);

        internal void MarkDirty(Chunk2i chunk)
        {
            lock (m_dirtyGate)
            {
                m_dirtyChunks.Add(chunk);
                m_hasDirty = true;
            }
        }

        internal void SetFeatureEnabled(bool featureEnabled)
        {
            if (m_enabled == featureEnabled)
            {
                return;
            }

            m_enabled = featureEnabled;
            if (!featureEnabled)
            {
                HideLabels();
            }
            else
            {
                m_cacheBuilt = false;
            }
        }

        private void Update()
        {
            if (!m_enabled || m_terrain is null || m_renderer is null || m_visibilityField is null || m_chunksField is null)
            {
                return;
            }

            if (!m_renderer.IsActive)
            {
                if (m_wasActive)
                {
                    HideLabels();
                    m_wasActive = false;
                }

                return;
            }

            bool justActivated = !m_wasActive;
            m_wasActive = true;
            ReadVisibleProducts();
            bool visibilityChanged = !m_visibleProducts.SetEquals(m_previousVisibleProducts);
            if (!m_cacheBuilt || visibilityChanged)
            {
                m_previousVisibleProducts.Clear();
                m_previousVisibleProducts.UnionWith(m_visibleProducts);
                ClearDirty();
                RefreshClusters(null);
                m_cacheBuilt = true;
                ShowLabels();
                return;
            }

            if (justActivated)
            {
                ConsumeDirty();
                ShowLabels();
                return;
            }

            if (m_hasDirty)
            {
                m_dirtyTimer += Time.deltaTime;
                if (m_dirtyTimer >= DirtyDebounceSeconds)
                {
                    m_dirtyTimer = 0f;
                    ConsumeDirty();
                    ShowLabels();
                }
            }
        }

        private void LateUpdate()
        {
            if (!m_enabled || m_clusters.Count == 0)
            {
                return;
            }

            Camera? camera = Camera.main;
            if (camera is null)
            {
                return;
            }

            float scale = Mathf.Clamp(TajsTweaksRuntimeState.ResourceOverlayLabelScale, 50, 200) / 100f;
            foreach (DepositCluster cluster in m_clusters)
            {
                if (cluster.Label is null)
                {
                    continue;
                }

                cluster.Label.transform.rotation = camera.transform.rotation;
                cluster.Label.transform.localScale = Vector3.one * scale;
                cluster.Label.transform.position = new Vector3(
                    cluster.WorldCenter.x,
                    Math.Max(cluster.SurfaceHeight, cluster.ResourceTop) * 2f +
                        (float)TajsTweaksRuntimeState.ResourceOverlayLabelHeight,
                    cluster.WorldCenter.z);
            }
        }

        private void ConsumeDirty()
        {
            HashSet<Chunk2i> dirty;
            lock (m_dirtyGate)
            {
                if (m_dirtyChunks.Count == 0)
                {
                    m_hasDirty = false;
                    return;
                }

                dirty = new HashSet<Chunk2i>(m_dirtyChunks);
                m_dirtyChunks.Clear();
                m_hasDirty = false;
            }

            RefreshClusters(dirty);
        }

        private void ClearDirty()
        {
            lock (m_dirtyGate)
            {
                m_dirtyChunks.Clear();
                m_hasDirty = false;
            }

            m_dirtyTimer = 0f;
        }

        private void ReadVisibleProducts()
        {
            m_visibleProducts.Clear();
            if (m_visibilityField?.GetValue(m_renderer) is not IEnumerable entries)
            {
                return;
            }

            foreach (object entry in entries)
            {
                if (ReadMember(entry, "Key") is ProductProto product && Convert.ToInt32(ReadMember(entry, "Value") ?? 0, CultureInfo.InvariantCulture) > 0)
                {
                    m_visibleProducts.Add(product);
                }
            }
        }

        private void RefreshClusters(HashSet<Chunk2i>? dirty)
        {
            if (m_terrain is null || m_chunksField?.GetValue(m_renderer) is not IEnumerable entries)
            {
                return;
            }

            RectangleTerrainArea2i area = m_terrain.TerrainArea;
            m_areaStartX = area.Origin.X;
            m_areaStartY = area.Origin.Y;
            m_areaEndX = m_areaStartX + area.Size.X;
            m_areaEndY = m_areaStartY + area.Size.Y;

            if (dirty is not null && m_cacheBuilt)
            {
                RefreshDirtyClusters(entries, dirty);
                return;
            }

            List<DepositCluster> shells = BuildShells(entries);
            Dictionary<string, DepositCluster> oldByKey = new Dictionary<string, DepositCluster>(StringComparer.Ordinal);
            foreach (DepositCluster old in m_clusters)
            {
                    if (!oldByKey.ContainsKey(old.Key))
                    {
                        oldByKey.Add(old.Key, old);
                    }
            }

            List<DepositCluster> next = new List<DepositCluster>(Math.Min(shells.Count, MaximumClusters));
            HashSet<DepositCluster> reused = new HashSet<DepositCluster>();
            foreach (DepositCluster shell in shells)
            {
                if (next.Count >= MaximumClusters)
                {
                    break;
                }

                oldByKey.TryGetValue(shell.Key, out DepositCluster? old);
                bool sameTopology = old is not null && SameChunks(shell.Chunks, old.Chunks);
                bool touchesDirty = dirty is not null && Intersects(shell.Chunks, dirty);
                if (sameTopology && !touchesDirty)
                {
                    reused.Add(old!);
                    next.Add(old!);
                }
                else if (SampleCluster(shell))
                {
                    next.Add(shell);
                }
            }

            foreach (DepositCluster old in m_clusters)
            {
                if (!reused.Contains(old) && !next.Contains(old))
                {
                    DestroyLabel(old);
                }
            }

            m_clusters.Clear();
            m_clusters.AddRange(next);
        }

        private List<DepositCluster> BuildShells(IEnumerable entries)
        {
            List<DepositCluster> result = new List<DepositCluster>();
            m_nativeChunkSets.Clear();
            m_productChunks.Clear();
            HashSet<Chunk2i> all = new HashSet<Chunk2i>();
            HashSet<Chunk2i> visited = new HashSet<Chunk2i>();
            Queue<Chunk2i> queue = new Queue<Chunk2i>();

            foreach (object entry in entries)
            {
                if (ReadMember(entry, "Key") is not LooseProductProto product ||
                    !m_visibleProducts.Contains(product) ||
                    ReadMember(entry, "Value") is not IEnumerable chunkEntries)
                {
                    continue;
                }

                m_nativeChunkSets[product] = chunkEntries;
                all.Clear();
                foreach (object chunkEntry in chunkEntries)
                {
                    if (chunkEntry is Chunk2i chunk)
                    {
                        all.Add(chunk);
                    }
                }

                m_productChunks[product] = new HashSet<Chunk2i>(all);

                visited.Clear();
                foreach (Chunk2i start in all)
                {
                    if (!visited.Add(start))
                    {
                        continue;
                    }

                    DepositCluster cluster = new DepositCluster
                    {
                        Product = product,
                    };
                    queue.Clear();
                    queue.Enqueue(start);
                    while (queue.Count > 0)
                    {
                        Chunk2i current = queue.Dequeue();
                        cluster.Chunks.Add(current);
                        EnqueueNeighbor(current.PlusXNeighbor, all, visited, queue);
                        EnqueueNeighbor(current.MinusXNeighbor, all, visited, queue);
                        EnqueueNeighbor(current.PlusYNeighbor, all, visited, queue);
                        EnqueueNeighbor(current.MinusYNeighbor, all, visited, queue);
                    }

                    cluster.Key = MakeKey(product, cluster.Chunks);
                    result.Add(cluster);
                }
            }

            return result;
        }

        private void RefreshDirtyClusters(IEnumerable entries, HashSet<Chunk2i> dirty)
        {
            RefreshNativeChunkSetReferences(entries);
            foreach (ProductProto visible in m_visibleProducts)
            {
                if (visible is not LooseProductProto product)
                {
                    continue;
                }

                if (!m_productChunks.TryGetValue(product, out HashSet<Chunk2i>? chunks))
                {
                    chunks = new HashSet<Chunk2i>();
                    m_productChunks[product] = chunks;
                }

                m_nativeChunkSets.TryGetValue(product, out object? nativeSet);
                if (nativeSet is null)
                {
                    chunks.Clear();
                    continue;
                }

                foreach (Chunk2i dirtyChunk in dirty)
                {
                    if (ContainsNativeChunk(nativeSet, dirtyChunk))
                    {
                        chunks.Add(dirtyChunk);
                    }
                    else
                    {
                        chunks.Remove(dirtyChunk);
                    }
                }
            }

            HashSet<DepositCluster> affected = new HashSet<DepositCluster>();
            foreach (DepositCluster cluster in m_clusters)
            {
                if (TouchesDirty(cluster.Chunks, dirty))
                {
                    affected.Add(cluster);
                }
            }

            Dictionary<LooseProductProto, HashSet<Chunk2i>> seeds = new Dictionary<LooseProductProto, HashSet<Chunk2i>>();
            foreach (Chunk2i dirtyChunk in dirty)
            {
                AddDirtySeeds(dirtyChunk, seeds);
            }
            foreach (DepositCluster cluster in affected)
            {
                if (!m_productChunks.TryGetValue(cluster.Product, out HashSet<Chunk2i>? chunks))
                {
                    continue;
                }

                if (!seeds.TryGetValue(cluster.Product, out HashSet<Chunk2i>? productSeeds))
                {
                    productSeeds = new HashSet<Chunk2i>();
                    seeds[cluster.Product] = productSeeds;
                }

                foreach (Chunk2i chunk in cluster.Chunks)
                {
                    if (chunks.Contains(chunk))
                    {
                        productSeeds.Add(chunk);
                    }
                }
            }

            List<DepositCluster> next = new List<DepositCluster>(m_clusters.Count + seeds.Count);
            foreach (DepositCluster existing in m_clusters)
            {
                if (!affected.Contains(existing))
                {
                    next.Add(existing);
                }
            }

            foreach (KeyValuePair<LooseProductProto, HashSet<Chunk2i>> pair in seeds)
            {
                if (!m_productChunks.TryGetValue(pair.Key, out HashSet<Chunk2i>? chunks))
                {
                    continue;
                }

                HashSet<Chunk2i> visited = new HashSet<Chunk2i>();
                Queue<Chunk2i> queue = new Queue<Chunk2i>();
                foreach (Chunk2i start in pair.Value)
                {
                    if (!chunks.Contains(start) || !visited.Add(start))
                    {
                        continue;
                    }

                    DepositCluster cluster = new DepositCluster { Product = pair.Key };
                    queue.Enqueue(start);
                    while (queue.Count > 0)
                    {
                        Chunk2i current = queue.Dequeue();
                        cluster.Chunks.Add(current);
                        EnqueueNeighbor(current.PlusXNeighbor, chunks, visited, queue);
                        EnqueueNeighbor(current.MinusXNeighbor, chunks, visited, queue);
                        EnqueueNeighbor(current.PlusYNeighbor, chunks, visited, queue);
                        EnqueueNeighbor(current.MinusYNeighbor, chunks, visited, queue);
                    }

                    cluster.Key = MakeKey(pair.Key, cluster.Chunks);
                    if (SampleCluster(cluster))
                    {
                        next.Add(cluster);
                    }
                }
            }

            List<DepositCluster> limited = next.Take(MaximumClusters).ToList();
            foreach (DepositCluster old in m_clusters)
            {
                if (!limited.Contains(old))
                {
                    DestroyLabel(old);
                }
            }

            m_clusters.Clear();
            m_clusters.AddRange(limited);
        }

        private void RefreshNativeChunkSetReferences(IEnumerable entries)
        {
            m_nativeChunkSets.Clear();
            foreach (object entry in entries)
            {
                if (ReadMember(entry, "Key") is LooseProductProto product &&
                    m_visibleProducts.Contains(product) &&
                    ReadMember(entry, "Value") is object nativeSet)
                {
                    m_nativeChunkSets[product] = nativeSet;
                }
            }
        }

        private void AddDirtySeeds(Chunk2i chunk, Dictionary<LooseProductProto, HashSet<Chunk2i>> seeds)
        {
            foreach (KeyValuePair<LooseProductProto, HashSet<Chunk2i>> pair in m_productChunks)
            {
                if (!ContainsNativeChunk(pair.Value, chunk) &&
                    !ContainsNativeChunk(pair.Value, chunk.PlusXNeighbor) &&
                    !ContainsNativeChunk(pair.Value, chunk.MinusXNeighbor) &&
                    !ContainsNativeChunk(pair.Value, chunk.PlusYNeighbor) &&
                    !ContainsNativeChunk(pair.Value, chunk.MinusYNeighbor))
                {
                    continue;
                }

                if (!seeds.TryGetValue(pair.Key, out HashSet<Chunk2i>? productSeeds))
                {
                    productSeeds = new HashSet<Chunk2i>();
                    seeds[pair.Key] = productSeeds;
                }

                AddIfPresent(productSeeds, pair.Value, chunk);
                AddIfPresent(productSeeds, pair.Value, chunk.PlusXNeighbor);
                AddIfPresent(productSeeds, pair.Value, chunk.MinusXNeighbor);
                AddIfPresent(productSeeds, pair.Value, chunk.PlusYNeighbor);
                AddIfPresent(productSeeds, pair.Value, chunk.MinusYNeighbor);
            }
        }

        private static void AddIfPresent(HashSet<Chunk2i> target, HashSet<Chunk2i> chunks, Chunk2i candidate)
        {
            if (chunks.Contains(candidate))
            {
                target.Add(candidate);
            }
        }

        private static bool TouchesDirty(HashSet<Chunk2i> cluster, HashSet<Chunk2i> dirty)
        {
            foreach (Chunk2i chunk in dirty)
            {
                if (cluster.Contains(chunk) || cluster.Contains(chunk.PlusXNeighbor) || cluster.Contains(chunk.MinusXNeighbor) ||
                    cluster.Contains(chunk.PlusYNeighbor) || cluster.Contains(chunk.MinusYNeighbor))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsNativeChunk(object? nativeSet, Chunk2i chunk)
        {
            if (nativeSet is null)
            {
                return false;
            }
            if (nativeSet is ICollection<Chunk2i> collection)
            {
                return collection.Contains(chunk);
            }

            MethodInfo? contains = nativeSet.GetType().GetMethod("Contains", InstanceFlags, null, new[] { typeof(Chunk2i) }, null);
            if (contains?.Invoke(nativeSet, new object[] { chunk }) is bool result)
            {
                return result;
            }

            if (nativeSet is IEnumerable enumerable)
            {
                foreach (object value in enumerable)
                {
                    if (value is Chunk2i candidate && candidate.Equals(chunk))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void EnqueueNeighbor(Chunk2i neighbor, HashSet<Chunk2i> all, HashSet<Chunk2i> visited, Queue<Chunk2i> queue)
        {
            if (all.Contains(neighbor) && visited.Add(neighbor))
            {
                queue.Enqueue(neighbor);
            }
        }

        private static string MakeKey(LooseProductProto product, HashSet<Chunk2i> chunks)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            foreach (Chunk2i chunk in chunks)
            {
                if (chunk.X < minX || (chunk.X == minX && chunk.Y < minY))
                {
                    minX = chunk.X;
                    minY = chunk.Y;
                }
            }

            return product.Id.Value + "|" + minX.ToString(CultureInfo.InvariantCulture) + "_" + minY.ToString(CultureInfo.InvariantCulture);
        }

        private bool SampleCluster(DepositCluster cluster)
        {
            if (m_terrain is null)
            {
                return false;
            }

            int count = 0;
            float sumX = 0f;
            float sumY = 0f;
            float surface = float.MinValue;
            float top = float.MinValue;
            float maxDepth = 0f;
            foreach (Chunk2i chunk in cluster.Chunks)
            {
                Tile2i origin = chunk.Tile2i;
                int startX = Math.Max(origin.X, m_areaStartX);
                int startY = Math.Max(origin.Y, m_areaStartY);
                int endX = Math.Min(origin.X + ChunkSize, m_areaEndX);
                int endY = Math.Min(origin.Y + ChunkSize, m_areaEndY);
                for (int y = startY; y < endY; y += SampleStep)
                {
                    for (int x = startX; x < endX; x += SampleStep)
                    {
                        Tile2i tile = new Tile2i(x, y);
                        if (m_terrain.IsOcean(tile))
                        {
                            continue;
                        }

                        Tile2iIndex index = m_terrain.GetTileIndex(tile);
                        float surfaceHeight = m_terrain.GetHeight(index).Value.ToFloat();
                        ThicknessTilesF below = ThicknessTilesF.Zero;
                        bool containsPrimary = false;
                        float primaryTop = float.MinValue;
                        float primaryBottom = float.MaxValue;
                        foreach (TerrainMaterialThicknessSlim layer in m_terrain.EnumerateLayers(index))
                        {
                            if (layer.IsNone || layer.Thickness.IsNotPositive)
                            {
                                below += layer.Thickness;
                                continue;
                            }

                            TerrainMaterialProto material = m_terrain.ResolveSlimMaterial(layer.SlimId);
                            if (material == m_terrain.Bedrock)
                            {
                                break;
                            }

                            if (material.MinedProduct == cluster.Product)
                            {
                                containsPrimary = true;
                                float resourceTop = surfaceHeight - below.Value.ToFloat();
                                primaryTop = Math.Max(primaryTop, resourceTop);
                                primaryBottom = Math.Min(primaryBottom, resourceTop - layer.Thickness.Value.ToFloat());
                            }

                            below += layer.Thickness;
                        }

                        if (!containsPrimary)
                        {
                            continue;
                        }

                        count++;
                        sumX += x;
                        sumY += y;
                        surface = Math.Max(surface, surfaceHeight);
                        top = Math.Max(top, primaryTop);
                        maxDepth = Math.Max(maxDepth, surfaceHeight - primaryBottom);
                    }
                }
            }

            if (count < MinimumSampleCount)
            {
                return false;
            }

            cluster.WorldCenter = new Vector3(sumX / count * 2f, 0f, sumY / count * 2f);
            cluster.SurfaceHeight = surface;
            cluster.ResourceTop = top;
            cluster.MaxDepth = maxDepth;
            return true;
        }

        private void ShowLabels()
        {
            foreach (DepositCluster cluster in m_clusters)
            {
                if (cluster.Label is null)
                {
                    cluster.Label = CreateLabel(cluster);
                }
                else
                {
                    cluster.Label.SetActive(true);
                }
            }
        }

        private GameObject CreateLabel(DepositCluster cluster)
        {
            GameObject label = new GameObject("Tajs resource deposit " + cluster.Product.Id.Value);
            label.transform.SetParent(transform, worldPositionStays: true);
            Type? textMeshType = Type.GetType("UnityEngine.TextMesh, UnityEngine.TextRenderingModule", false);
            if (textMeshType is null)
            {
                UnityEngine.Object.Destroy(label);
                return label;
            }

            Component text = label.AddComponent(textMeshType);
            SetTextProperty(text, "fontSize", 48);
            SetTextProperty(text, "characterSize", 0.12f);
            SetTextProperty(text, "fontStyle", Enum.Parse(textMeshType.GetProperty("fontStyle")!.PropertyType, "Bold"));
            SetTextProperty(text, "alignment", Enum.Parse(textMeshType.GetProperty("alignment")!.PropertyType, "Center"));
            SetTextProperty(text, "anchor", Enum.Parse(textMeshType.GetProperty("anchor")!.PropertyType, "MiddleCenter"));
            SetTextProperty(text, "color", new Color(1f, 1f, 1f, Mathf.Clamp(TajsTweaksRuntimeState.ResourceOverlayLabelAlpha, 0, 100) / 100f));
            SetTextProperty(text, "text", cluster.Product.Id.Value +
                "\nsurface " + cluster.SurfaceHeight.ToString("F1", CultureInfo.InvariantCulture) +
                "  top " + cluster.ResourceTop.ToString("F1", CultureInfo.InvariantCulture) +
                "\nmax depth " + cluster.MaxDepth.ToString("F1", CultureInfo.InvariantCulture));
            return label;
        }

        private static void SetTextProperty(Component component, string name, object value)
        {
            PropertyInfo? property = component.GetType().GetProperty(name, InstanceFlags);
            if (property?.CanWrite == true)
            {
                property.SetValue(component, value);
            }
        }

        private void HideLabels()
        {
            foreach (DepositCluster cluster in m_clusters)
            {
                if (cluster.Label is not null)
                {
                    cluster.Label.SetActive(false);
                }
            }
        }

        private static bool SameChunks(HashSet<Chunk2i> left, HashSet<Chunk2i> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (Chunk2i chunk in left)
            {
                if (!right.Contains(chunk))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Intersects(HashSet<Chunk2i> cluster, HashSet<Chunk2i> dirty)
        {
            foreach (Chunk2i chunk in dirty)
            {
                if (cluster.Contains(chunk))
                {
                    return true;
                }
            }

            return false;
        }

        private static object? ReadMember(object value, string name)
        {
            Type type = value.GetType();
            return type.GetProperty(name, InstanceFlags)?.GetValue(value) ??
                type.GetField(name, InstanceFlags)?.GetValue(value);
        }

        private static void DestroyLabel(DepositCluster cluster)
        {
            if (cluster.Label is not null)
            {
                UnityEngine.Object.Destroy(cluster.Label);
                cluster.Label = null;
            }
        }

        private void OnDestroy()
        {
            foreach (DepositCluster cluster in m_clusters)
            {
                DestroyLabel(cluster);
            }

            m_clusters.Clear();
        }
    }
}
