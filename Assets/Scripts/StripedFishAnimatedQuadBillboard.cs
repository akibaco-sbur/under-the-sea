using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Uses the original SkinnedMeshRenderer as the animation source. Each frame it
/// bakes the animated vertices and flattens each original quad toward the camera.
/// It does not reposition bones or discard the authored vertex animation.
/// </summary>
public sealed class StripedFishAnimatedQuadBillboard : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] bool hideOriginalRenderer = true;
    [SerializeField] bool flattenQuadsToCamera = true;
    [SerializeField, Min(1f)] float maxQuadStretch = 1.6f;
    [SerializeField] string finMaterialNameContains = "Material";
    [SerializeField, Range(0f, 0.1f)] float finRootBehindBodyMargin = 0.01f;

    struct Triangle
    {
        public int a, b, c, submesh;
        public Triangle(int a, int b, int c, int submesh)
        { this.a = a; this.b = b; this.c = c; this.submesh = submesh; }
    }

    struct PairCandidate
    {
        public int first, second;
        public float sharedEdgeLength;
        public PairCandidate(int first, int second, float length)
        { this.first = first; this.second = second; sharedEdgeLength = length; }
    }

    sealed class Quad
    {
        public int[] sourceVertices;
        public int submesh;
        public int[] triangleIndices;
        public int[] boundaryEdges;
    }

    sealed class DoubleSidedMaterialEntry
    {
        public Material material;
        public int referenceCount;
    }

    static readonly Dictionary<Material, DoubleSidedMaterialEntry> doubleSidedMaterialCache =
        new Dictionary<Material, DoubleSidedMaterialEntry>();

    SkinnedMeshRenderer source;
    MeshRenderer outputRenderer;
    Mesh outputMesh;
    Mesh bakedMesh;
    Material[] outputMaterials;
    readonly List<Material> acquiredMaterialSources = new List<Material>();
    Transform outputTransform;
    bool originalForceRenderingOff;
    bool originalUpdateWhenOffscreen;
    int outputSubmeshCount;
    int[] outputVertexSource;
    int[][] outputTriangles;
    int[][] quadBoundaryEdges;
    int[] quadTriangleOffsets;
    int[] quadSubmeshes;
    int finSubmeshIndex = -1;
    bool[] quadGetsFinRootCorrection;
    bool loggedOutputStats;
    readonly List<Vector3> bakedVertices = new List<Vector3>();
    readonly List<Vector3> projectedVertices = new List<Vector3>();

    void Start()
    {
        source = FindAnimatedRenderer();
        if (!source)
        {
            Debug.LogError("No SkinnedMeshRenderer with a mesh and assigned bones was found on this object or its children.", this);
            enabled = false;
            return;
        }
        originalForceRenderingOff = source.forceRenderingOff;
        originalUpdateWhenOffscreen = source.updateWhenOffscreen;
        if (!targetCamera) targetCamera = FindTargetCamera();
        if (!source.sharedMesh || !targetCamera)
        {
            Debug.LogError("Assign a SkinnedMeshRenderer mesh and a target camera.", this);
            enabled = false;
            return;
        }
        Debug.Log("StripedFishAnimatedQuadBillboard source: " + GetPath(source.transform) +
            " (mesh=" + source.sharedMesh.name + ", bones=" + source.bones.Length + ")", this);

        bakedMesh = new Mesh { name = "StripedFish_AnimatedBake" };
        bakedMesh.MarkDynamic();
        source.BakeMesh(bakedMesh);
        bakedVertices.Clear();
        bakedMesh.GetVertices(bakedVertices);

        if (!BuildDisplayMesh())
        {
            enabled = false;
            return;
        }

        var go = new GameObject("StripedFish Camera-Facing Mesh");
        go.layer = gameObject.layer;
        // BakeMesh vertices are in source-renderer local space. Keeping the
        // output as an identity child avoids a world/local round trip through
        // potentially scaled or animated parent transforms.
        go.transform.SetParent(source.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        // BakeMesh returns vertices in renderer space with the renderer's
        // Transform scale already represented in the baked coordinates for
        // this FBX import. The source object is scaled by 100, so inheriting
        // that scale again would move the fish hundreds of units away.
        Vector3 sourceScale = source.transform.lossyScale;
        if (Mathf.Abs(sourceScale.x) < 1e-6f || Mathf.Abs(sourceScale.y) < 1e-6f || Mathf.Abs(sourceScale.z) < 1e-6f)
        {
            Debug.LogError("The source renderer has a zero world scale; cannot compensate the output mesh transform.", this);
            Destroy(go);
            enabled = false;
            return;
        }
        go.transform.localScale = new Vector3(1f / sourceScale.x, 1f / sourceScale.y, 1f / sourceScale.z);
        outputTransform = go.transform;

        go.AddComponent<MeshFilter>().sharedMesh = outputMesh;
        outputRenderer = go.AddComponent<MeshRenderer>();
        outputMaterials = CreateDoubleSidedMaterials(source.sharedMaterials);
        outputRenderer.sharedMaterials = outputMaterials;
        outputRenderer.shadowCastingMode = source.shadowCastingMode;
        outputRenderer.receiveShadows = source.receiveShadows;
        outputRenderer.lightProbeUsage = source.lightProbeUsage;
        outputRenderer.reflectionProbeUsage = source.reflectionProbeUsage;
        outputRenderer.sortingLayerID = source.sortingLayerID;
        outputRenderer.sortingOrder = source.sortingOrder;

        // Keep the SkinnedMeshRenderer active as the live animation source.
        if (hideOriginalRenderer)
        {
            // A hidden source renderer must still update its skinning data.
            source.updateWhenOffscreen = true;
            source.forceRenderingOff = true;
        }
    }

    SkinnedMeshRenderer FindAnimatedRenderer()
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer candidate in renderers)
            if (candidate.sharedMesh && candidate.bones != null && candidate.bones.Length > 0)
                return candidate;
        return null;
    }

    Camera FindTargetCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera) return mainCamera;
        Camera[] cameras = Camera.allCameras;
        return cameras.Length > 0 ? cameras[0] : null;
    }

    string GetPath(Transform target)
    {
        string path = target.name;
        while (target.parent && target.parent != transform)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }
        return path;
    }

    bool BuildDisplayMesh()
    {
        Mesh mesh = source.sharedMesh;
        if (!mesh.isReadable)
        {
            Debug.LogError("The source FBX mesh is not readable. Enable Read/Write in the FBX Model Import Settings, then reimport the asset.", this);
            return false;
        }
        Material[] sourceMaterials = source.sharedMaterials;
        for (int i = 0; i < sourceMaterials.Length && i < mesh.subMeshCount; i++)
        {
            Material material = sourceMaterials[i];
            if (!material) continue;
            if (finSubmeshIndex < 0 && !string.IsNullOrEmpty(finMaterialNameContains) &&
                material.name.IndexOf(finMaterialNameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                finSubmeshIndex = i;
        }
        var triangles = new List<Triangle>();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (mesh.GetTopology(sub) != MeshTopology.Triangles)
            {
                Debug.LogError("The source mesh contains a non-triangle submesh.", this);
                return false;
            }
            int[] indices = mesh.GetIndices(sub);
            for (int i = 0; i + 2 < indices.Length; i += 3)
                triangles.Add(new Triangle(indices[i], indices[i + 1], indices[i + 2], sub));
        }

        // Unity triangulates each source quad. Pair the two triangles across
        // their longest shared edge (the quad diagonal), even if Unity welded
        // coincident vertices between neighboring islands during import.
        var candidates = new List<PairCandidate>();
        float[] longestSharedEdge = new float[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        for (int j = i + 1; j < triangles.Count; j++)
        {
            Triangle a = triangles[i], b = triangles[j];
            if (a.submesh != b.submesh) continue;
            int sharedA, sharedB;
            if (!GetSharedEdge(a, b, out sharedA, out sharedB)) continue;
            float length = (bakedVertices[sharedA] - bakedVertices[sharedB]).sqrMagnitude;
            candidates.Add(new PairCandidate(i, j, length));
            if (length > longestSharedEdge[i]) longestSharedEdge[i] = length;
            if (length > longestSharedEdge[j]) longestSharedEdge[j] = length;
        }
        candidates.Sort((x, y) => y.sharedEdgeLength.CompareTo(x.sharedEdgeLength));

        bool[] used = new bool[triangles.Count];
        var quads = new List<Quad>();
        foreach (PairCandidate candidate in candidates)
        {
            int a = candidate.first, b = candidate.second;
            if (used[a] || used[b]) continue;
            float tolerance = 0.995f;
            if (candidate.sharedEdgeLength < longestSharedEdge[a] * tolerance ||
                candidate.sharedEdgeLength < longestSharedEdge[b] * tolerance) continue;

            Triangle ta = triangles[a], tb = triangles[b];
            int sharedA, sharedB;
            if (!GetSharedEdge(ta, tb, out sharedA, out sharedB)) continue;
            var unique = new HashSet<int> { ta.a, ta.b, ta.c, tb.a, tb.b, tb.c };
            if (unique.Count != 4) continue;

            int[] sourceIndices = new int[4];
            unique.CopyTo(sourceIndices);
            var localIndex = new Dictionary<int, int>();
            for (int i = 0; i < 4; i++) localIndex[sourceIndices[i]] = i;
            var boundaryEdges = new List<int>(8);
            AddBoundaryEdge(boundaryEdges, ta.a, ta.b, sharedA, sharedB, localIndex);
            AddBoundaryEdge(boundaryEdges, ta.b, ta.c, sharedA, sharedB, localIndex);
            AddBoundaryEdge(boundaryEdges, ta.c, ta.a, sharedA, sharedB, localIndex);
            AddBoundaryEdge(boundaryEdges, tb.a, tb.b, sharedA, sharedB, localIndex);
            AddBoundaryEdge(boundaryEdges, tb.b, tb.c, sharedA, sharedB, localIndex);
            AddBoundaryEdge(boundaryEdges, tb.c, tb.a, sharedA, sharedB, localIndex);
            quads.Add(new Quad
            {
                sourceVertices = sourceIndices,
                submesh = ta.submesh,
                boundaryEdges = boundaryEdges.ToArray(),
                triangleIndices = new[] {
                    localIndex[ta.a], localIndex[ta.b], localIndex[ta.c],
                    localIndex[tb.a], localIndex[tb.b], localIndex[tb.c]
                }
            });
            used[a] = used[b] = true;
        }

        if (quads.Count != 64)
        {
            Debug.LogError("Could reconstruct " + quads.Count + " quads from the imported mesh; expected 64. " +
                "The Unity FBX importer may have changed the triangulation. No display mesh was created.", this);
            return false;
        }

        outputSubmeshCount = mesh.subMeshCount;
        outputVertexSource = new int[quads.Count * 4];
        quadTriangleOffsets = new int[quads.Count];
        quadSubmeshes = new int[quads.Count];
        quadBoundaryEdges = new int[quads.Count][];
        var triangleLists = new List<int>[outputSubmeshCount];
        for (int i = 0; i < triangleLists.Length; i++) triangleLists[i] = new List<int>();
        projectedVertices.Clear();

        for (int q = 0; q < quads.Count; q++)
        {
            Quad quad = quads[q];
            int baseVertex = q * 4;
            quadSubmeshes[q] = quad.submesh;
            quadTriangleOffsets[q] = triangleLists[quad.submesh].Count;
            quadBoundaryEdges[q] = new int[quad.boundaryEdges.Length];
            for (int e = 0; e < quad.boundaryEdges.Length; e++)
                quadBoundaryEdges[q][e] = baseVertex + quad.boundaryEdges[e];
            for (int v = 0; v < 4; v++)
            {
                outputVertexSource[baseVertex + v] = quad.sourceVertices[v];
                projectedVertices.Add(Vector3.zero);
            }
            for (int i = 0; i < quad.triangleIndices.Length; i++)
                triangleLists[quad.submesh].Add(baseVertex + quad.triangleIndices[i]);
        }
        outputTriangles = new int[outputSubmeshCount][];
        for (int i = 0; i < outputSubmeshCount; i++) outputTriangles[i] = triangleLists[i].ToArray();

        // In this FBX, the dorsal, tail, and ventral fin islands share the
        // Material submesh. The chest-fin islands use the separate black
        // submesh and are intentionally excluded from the root correction.
        quadGetsFinRootCorrection = new bool[quads.Count];
        int finQuadCount = 0;
        for (int q = 0; q < quads.Count; q++)
        {
            quadGetsFinRootCorrection[q] = quadSubmeshes[q] == finSubmeshIndex;
            if (quadGetsFinRootCorrection[q]) finQuadCount++;
        }
        if (string.IsNullOrEmpty(finMaterialNameContains))
        {
            // This fish has no fin-specific depth correction.
        }
        else if (finSubmeshIndex < 0 || finQuadCount == 0)
            Debug.LogWarning("No fin submesh matched material name '" + finMaterialNameContains +
                "'. Fin-root depth correction is disabled; check the FBX material names/order in the Inspector.", this);
        else
            Debug.Log("Fin-root correction targets submesh " + finSubmeshIndex + " ('" +
                sourceMaterials[finSubmeshIndex].name + "'): " + finQuadCount +
                " quad islands. Chest-fin islands on the black submesh are excluded.", this);

        outputMesh = new Mesh { name = mesh.name + "_CameraFacing" };
        outputMesh.MarkDynamic();
        outputMesh.SetVertices(projectedVertices);
        outputMesh.subMeshCount = outputSubmeshCount;
        for (int i = 0; i < outputSubmeshCount; i++) outputMesh.SetTriangles(outputTriangles[i], i, false);

        // BakeMesh produces a readable runtime snapshot, so UVs can be copied
        // without requiring Read/Write on the imported FBX asset.
        var sourceUv = new List<Vector2>();
        bakedMesh.GetUVs(0, sourceUv);
        if (sourceUv.Count == bakedVertices.Count)
        {
            var outputUv = new List<Vector2>(outputVertexSource.Length);
            for (int i = 0; i < outputVertexSource.Length; i++)
                outputUv.Add(sourceUv[outputVertexSource[i]]);
            outputMesh.SetUVs(0, outputUv);
        }
        else
        {
            Debug.LogWarning("The baked source mesh has no UV0 data; textured materials may not display as expected.", this);
        }

        outputMesh.RecalculateNormals();
        outputMesh.RecalculateBounds();
        return true;
    }

    void LateUpdate()
    {
        if (!source || !targetCamera || !outputMesh || outputTransform == null) return;
        source.BakeMesh(bakedMesh);
        bakedVertices.Clear();
        bakedMesh.GetVertices(bakedVertices);

        Vector3 worldNormal = -targetCamera.transform.forward;
        Vector3 localNormal = source.transform.localToWorldMatrix.transpose.MultiplyVector(worldNormal);
        if (!IsSane(localNormal) || localNormal.sqrMagnitude < 1e-12f)
        {
            Debug.LogError("Camera-facing projection failed: the source transform has a singular scale.", this);
            enabled = false;
            return;
        }
        localNormal.Normalize();

        for (int i = 0; i < bakedVertices.Count; i++)
        {
            if (!IsSane(bakedVertices[i]))
            {
                Debug.LogError("BakeMesh returned an invalid vertex at index " + i + ": " + bakedVertices[i], this);
                Debug.LogError("Bake diagnostics: source mesh bounds=" + source.sharedMesh.bounds +
                    ", renderer bounds=" + source.bounds + ", renderer scale=" + source.transform.lossyScale +
                    ", rootBone=" + (source.rootBone ? source.rootBone.name : "<none>") +
                    ", bones=" + source.bones.Length + ". Disable this component to check whether the original skinned fish animates normally.", this);
                enabled = false;
                return;
            }
        }

        // Diagnostic mode: show the exact baked animation without the camera
        // projection. If this is visible, the issue is in the flattening step.
        if (!flattenQuadsToCamera)
        {
            for (int i = 0; i < outputVertexSource.Length; i++)
                projectedVertices[i] = bakedVertices[outputVertexSource[i]];
            outputMesh.SetVertices(projectedVertices);
            outputMesh.RecalculateNormals();
            outputMesh.RecalculateBounds();
            LogOutputStats();
            return;
        }

        for (int q = 0; q < outputVertexSource.Length / 4; q++)
        {
            int baseVertex = q * 4;
            Vector3 center = Vector3.zero;
            for (int v = 0; v < 4; v++)
                center += bakedVertices[outputVertexSource[baseVertex + v]];
            center *= 0.25f;

            for (int v = 0; v < 4; v++)
            {
                int outputIndex = baseVertex + v;
                Vector3 local = bakedVertices[outputVertexSource[outputIndex]];
                projectedVertices[outputIndex] = center + Vector3.ProjectOnPlane(local - center, localNormal);
                if (!IsSane(projectedVertices[outputIndex]))
                {
                    Debug.LogError("Projection produced an invalid vertex at index " + outputIndex, this);
                    enabled = false;
                    return;
                }
            }
        }

        // Flattening an oblique quad reduces its area. Recover that loss from
        // the current camera projection; the cap avoids turning edge-on fins
        // into enormous patches.
        for (int q = 0; q < quadTriangleOffsets.Length; q++)
        {
            int sub = quadSubmeshes[q];
            int[] tris = outputTriangles[sub];
            int offset = quadTriangleOffsets[q];
            float sourceArea = TriangleArea(
                bakedVertices[outputVertexSource[tris[offset]]],
                bakedVertices[outputVertexSource[tris[offset + 1]]],
                bakedVertices[outputVertexSource[tris[offset + 2]]]) +
                TriangleArea(
                bakedVertices[outputVertexSource[tris[offset + 3]]],
                bakedVertices[outputVertexSource[tris[offset + 4]]],
                bakedVertices[outputVertexSource[tris[offset + 5]]]);
            float projectedArea = TriangleArea(
                projectedVertices[tris[offset]], projectedVertices[tris[offset + 1]], projectedVertices[tris[offset + 2]]) +
                TriangleArea(
                projectedVertices[tris[offset + 3]], projectedVertices[tris[offset + 4]], projectedVertices[tris[offset + 5]]);
            if (sourceArea <= 1e-10f || projectedArea <= 1e-10f) continue;

            float areaRecovery = Mathf.Sqrt(sourceArea / projectedArea);
            float scale = Mathf.Min(Mathf.Max(1f, maxQuadStretch), areaRecovery);
            int baseVertex = q * 4;
            Vector3 center = (projectedVertices[baseVertex] + projectedVertices[baseVertex + 1] +
                              projectedVertices[baseVertex + 2] + projectedVertices[baseVertex + 3]) * 0.25f;
            for (int v = 0; v < 4; v++)
                projectedVertices[baseVertex + v] = center + (projectedVertices[baseVertex + v] - center) * scale;
        }

        // Correct only fin roots. Select the perimeter edge (short or long)
        // nearest to the body in the camera plane, then calculate the exact
        // depth needed to put it just behind the nearest body tile. This makes
        // the correction respond to the current camera angle and swim pose.
        if (finSubmeshIndex >= 0)
        {
            var bodyCenters = new List<Vector3>();
            for (int q = 0; q < quadBoundaryEdges.Length; q++)
            {
                if (quadGetsFinRootCorrection[q]) continue;
                int baseVertex = q * 4;
                bodyCenters.Add((projectedVertices[baseVertex] + projectedVertices[baseVertex + 1] +
                                 projectedVertices[baseVertex + 2] + projectedVertices[baseVertex + 3]) * 0.25f);
            }

            for (int q = 0; q < quadBoundaryEdges.Length; q++)
            {
                if (!quadGetsFinRootCorrection[q]) continue;
                int[] edges = quadBoundaryEdges[q];
                int selectedEdge = -1;
                float nearestBodyDistance = float.PositiveInfinity;
                Vector3 nearestBodyCenter = Vector3.zero;
                for (int e = 0; e < edges.Length; e += 2)
                {
                    int a = edges[e], b = edges[e + 1];
                    Vector3 midpoint = (projectedVertices[a] + projectedVertices[b]) * 0.5f;
                    float edgeDistance = float.PositiveInfinity;
                    Vector3 edgeNearestBodyCenter = Vector3.zero;
                    for (int body = 0; body < bodyCenters.Count; body++)
                    {
                        Vector3 planarDelta = Vector3.ProjectOnPlane(midpoint - bodyCenters[body], localNormal);
                        float distance = planarDelta.sqrMagnitude;
                        if (distance < edgeDistance)
                        {
                            edgeDistance = distance;
                            edgeNearestBodyCenter = bodyCenters[body];
                        }
                    }
                    if (edgeDistance < nearestBodyDistance)
                    {
                        nearestBodyDistance = edgeDistance;
                        selectedEdge = e;
                        nearestBodyCenter = edgeNearestBodyCenter;
                    }
                }

                if (selectedEdge >= 0)
                {
                    int a = edges[selectedEdge], b = edges[selectedEdge + 1];
                    Vector3 midpoint = (projectedVertices[a] + projectedVertices[b]) * 0.5f;
                    float finDepth = Vector3.Dot(midpoint, localNormal);
                    float bodyDepth = Vector3.Dot(nearestBodyCenter, localNormal);
                    float pushBackDistance = finDepth - (bodyDepth - finRootBehindBodyMargin);
                    if (pushBackDistance > 0f)
                    {
                        Vector3 pushBack = -localNormal * pushBackDistance;
                        projectedVertices[a] += pushBack;
                        projectedVertices[b] += pushBack;
                    }
                }
            }
        }

        // Some FBX import paths reverse winding while triangulating. Face the
        // visible side toward this camera so backface-culling materials render.
        bool[] changedSubmesh = new bool[outputSubmeshCount];
        for (int q = 0; q < quadTriangleOffsets.Length; q++)
        {
            int sub = quadSubmeshes[q];
            int offset = quadTriangleOffsets[q];
            int[] tris = outputTriangles[sub];
            Vector3 p0 = projectedVertices[tris[offset]];
            Vector3 p1 = projectedVertices[tris[offset + 1]];
            Vector3 p2 = projectedVertices[tris[offset + 2]];
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), localNormal) < 0f)
            {
                Swap(tris, offset + 1, offset + 2);
                Swap(tris, offset + 4, offset + 5);
                changedSubmesh[sub] = true;
            }
        }
        for (int sub = 0; sub < changedSubmesh.Length; sub++)
            if (changedSubmesh[sub]) outputMesh.SetTriangles(outputTriangles[sub], sub, false);

        outputMesh.SetVertices(projectedVertices);
        outputMesh.RecalculateNormals();
        outputMesh.RecalculateBounds();
        LogOutputStats();
    }

    void LogOutputStats()
    {
        if (loggedOutputStats) return;
        loggedOutputStats = true;
        Vector3 min = projectedVertices[0], max = projectedVertices[0];
        for (int i = 1; i < projectedVertices.Count; i++)
        {
            min = Vector3.Min(min, projectedVertices[i]);
            max = Vector3.Max(max, projectedVertices[i]);
        }
        ulong indexCount = 0;
        for (int i = 0; i < outputMesh.subMeshCount; i++) indexCount += outputMesh.GetIndexCount(i);
        Debug.Log("Fish billboard output stats: baked=" + bakedVertices.Count +
            ", outputVertices=" + outputMesh.vertexCount + ", indices=" + indexCount +
            ", localVertexRange=" + min + " to " + max +
            ", meshBounds=" + outputMesh.bounds +
            ", rendererBounds=" + (outputRenderer ? outputRenderer.bounds.ToString() : "<none>") +
            ", rendererEnabled=" + (outputRenderer && outputRenderer.enabled) +
            ", materials=" + (outputRenderer ? outputRenderer.sharedMaterials.Length : 0), this);
    }

    Material[] CreateDoubleSidedMaterials(Material[] sourceMaterials)
    {
        var result = new Material[sourceMaterials.Length];
        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            Material sourceMaterial = sourceMaterials[i];
            if (!sourceMaterial) continue;

            if (doubleSidedMaterialCache.TryGetValue(sourceMaterial, out DoubleSidedMaterialEntry cached))
            {
                cached.referenceCount++;
                result[i] = cached.material;
                acquiredMaterialSources.Add(sourceMaterial);
                continue;
            }

            Material material = new Material(sourceMaterial);
            material.name = sourceMaterial.name + " (Double Sided)";
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            else
                Debug.LogWarning("Material '" + sourceMaterial.name +
                    "' has no _Cull property; its shader may still hide back faces.", this);

            if (material.HasProperty("_DoubleSidedEnable"))
            {
                material.SetFloat("_DoubleSidedEnable", 1f);
                material.EnableKeyword("_DOUBLESIDED_ON");
            }
            doubleSidedMaterialCache.Add(sourceMaterial,
                new DoubleSidedMaterialEntry { material = material, referenceCount = 1 });
            acquiredMaterialSources.Add(sourceMaterial);
            result[i] = material;
        }
        return result;
    }

    static bool GetSharedEdge(Triangle a, Triangle b, out int first, out int second)
    {
        first = second = -1;
        int count = 0;
        int[] av = { a.a, a.b, a.c };
        int[] bv = { b.a, b.b, b.c };
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            if (av[i] == bv[j])
            {
                if (count == 0) first = av[i];
                else if (count == 1) second = av[i];
                count++;
            }
        return count == 2;
    }

    static void AddBoundaryEdge(List<int> edges, int a, int b, int sharedA, int sharedB,
        Dictionary<int, int> localIndex)
    {
        if ((a == sharedA && b == sharedB) || (a == sharedB && b == sharedA)) return;
        edges.Add(localIndex[a]);
        edges.Add(localIndex[b]);
    }

    static float TriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    static bool IsSane(Vector3 v)
    {
        const float limit = 1000000f;
        return !float.IsNaN(v.x) && !float.IsInfinity(v.x) && Mathf.Abs(v.x) < limit &&
               !float.IsNaN(v.y) && !float.IsInfinity(v.y) && Mathf.Abs(v.y) < limit &&
               !float.IsNaN(v.z) && !float.IsInfinity(v.z) && Mathf.Abs(v.z) < limit;
    }

    static void Swap(int[] values, int a, int b)
    {
        int temp = values[a];
        values[a] = values[b];
        values[b] = temp;
    }

    void OnDisable()
    {
        // If validation fails and disables this component, reveal the original
        // renderer so the user can still inspect the authored skinned animation.
        if (source)
        {
            source.forceRenderingOff = originalForceRenderingOff;
            source.updateWhenOffscreen = originalUpdateWhenOffscreen;
        }
        if (outputRenderer) outputRenderer.forceRenderingOff = true;
    }

    void OnDestroy()
    {
        if (source)
        {
            source.forceRenderingOff = originalForceRenderingOff;
            source.updateWhenOffscreen = originalUpdateWhenOffscreen;
        }
        if (outputTransform) Destroy(outputTransform.gameObject);
        if (outputMesh) Destroy(outputMesh);
        if (bakedMesh) Destroy(bakedMesh);
        ReleaseDoubleSidedMaterials();
    }

    void ReleaseDoubleSidedMaterials()
    {
        for (int i = 0; i < acquiredMaterialSources.Count; i++)
        {
            Material sourceMaterial = acquiredMaterialSources[i];
            if (!doubleSidedMaterialCache.TryGetValue(sourceMaterial, out DoubleSidedMaterialEntry entry))
                continue;
            entry.referenceCount--;
            if (entry.referenceCount <= 0)
            {
                doubleSidedMaterialCache.Remove(sourceMaterial);
                if (entry.material) Destroy(entry.material);
            }
        }
        acquiredMaterialSources.Clear();
    }
}
