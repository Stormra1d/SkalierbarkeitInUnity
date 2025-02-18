using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Handles generating a road network.
/// </summary>
public class RoadNetworkGenerator : MonoBehaviour
{
    public GameObject[] roadLanePrefab;
    public GameObject[] roadCornerPrefab;
    public GameObject[] roadIntersectionPrefab;
    public GameObject[] tIntersectionPrefab;
    public GameObject[] roadEndPrefab;
    public GameObject[] roadCleanupPrefab;

    public const float roadTileSize = 20f;
    public static int MacroCellsPerChunk = 10;
    [Range(0.1f, 0.5f)]
    public float branchProbability = 0.2f;
    private int chunkSeed;
    private System.Random seededRandom;

    private const int BORDER_ZONE_SIZE = 1;

    public Dictionary<Vector2Int, GameObject> roadNetwork = new Dictionary<Vector2Int, GameObject>();
    private Dictionary<Vector2Int, HashSet<Vector2Int>> connectionMap = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
    private Dictionary<Vector2Int, MacroCell> gridCells;
    private static Dictionary<Vector2Int, HashSet<Vector2Int>> globalConnectionPoints = new Dictionary<Vector2Int, HashSet<Vector2Int>>();

    private Vector2Int currentChunkCoord;

    [Header("Road Variations")]
    [Range(0f, 1f)] public float curveProbability = 0.8f;
    public int maxCurveSegments = 30;
    public float branchTerminationChance = 0.2f;

    public static Vector2Int? highlightedRoadPosition = null;
    public GameObject highlightedRoadPrefab = null;

    private Dictionary<Vector2Int, RoadNode> networkNodes = new Dictionary<Vector2Int, RoadNode>();
    private const int MIN_ROAD_SPACING = 2;
    private const float MAIN_ROAD_SPACING = 3f;
    private const float SECONDARY_ROAD_SPACING = 3f;
    private const float DEAD_END_PROBABILITY = 0.1f;
    private const float BORDER_CONNECTION_RANGE = 3f;
    private const int MAX_CONNECTIONS_PER_NODE = 4;
    private const float CURVE_PROBABILITY = 0.3f;
    private const float Z_FIGHTING_THRESHOLD = 0.05f;
    private const float MIN_VALID_DISTANCE = 0.9f;
    private const string ROAD_TAG = "Road";

    private NativeArray<int2> nativeNodePositions;
    private NativeArray<int> nativeMainArteryFlags;
    private NativeParallelHashMap<int2, int> nodePositionMap;

    /// <summary>
    /// Job to find nearest nodes in parallel
    /// </summary>
    [BurstCompile]
    private struct FindNearestNodesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> nodePositions;
        [ReadOnly] public NativeArray<int> mainArteryFlags;
        public int2 targetPosition;
        public float maxDistance;

        [WriteOnly] public NativeArray<float> distances;
        [WriteOnly] public NativeArray<int> validIndices;

        public void Execute(int index)
        {
            float2 nodePos = new float2(nodePositions[index].x, nodePositions[index].y);
            float2 targetPos = new float2(targetPosition.x, targetPosition.y);
            float dist = math.distance(nodePos, targetPos);

            distances[index] = dist;
            validIndices[index] = (dist <= maxDistance && mainArteryFlags[index] == 1) ? 1 : 0;
        }
    }

    /// <summary>
    /// Job to process secondary connections in parallel
    /// </summary>
    [BurstCompile]
    private struct ProcessSecondaryConnectionsJob : IJobParallelFor
    {
        public int seed;
        public float branchProbability;
        public int macroCellsPerChunk;
        public int2 chunkOffset;
        public int minRoadSpacing;

        [WriteOnly] public NativeArray<int2> positionsToProcess;

        public void Execute(int index)
        {
            int x = (index % macroCellsPerChunk) * minRoadSpacing * 2;
            int y = (index / macroCellsPerChunk) * minRoadSpacing * 2;

            Unity.Mathematics.Random rand = new Unity.Mathematics.Random((uint)(seed + x + y * macroCellsPerChunk));
            if (rand.NextFloat() < branchProbability)
            {
                positionsToProcess[index] = new int2(
                    x + chunkOffset.x,
                    y + chunkOffset.y
                );
            }
            else
            {
                positionsToProcess[index] = new int2(-1, -1);
            }
        }
    }

    /// <summary>
    /// Initialize native data structures
    /// </summary>
    private void InitializeNativeData()
    {
        if (nativeNodePositions.IsCreated) nativeNodePositions.Dispose();
        if (nativeMainArteryFlags.IsCreated) nativeMainArteryFlags.Dispose();
        if (nodePositionMap.IsCreated) nodePositionMap.Dispose();

        var nodes = networkNodes.Values.ToList();
        nativeNodePositions = new NativeArray<int2>(nodes.Count, Allocator.Persistent);
        nodePositionMap = new NativeParallelHashMap<int2, int>(nodes.Count, Allocator.Persistent);
        nativeMainArteryFlags = new NativeArray<int>(nodes.Count, Allocator.Persistent);

        for (int i = 0; i < nodes.Count; i++)
        {
            nativeNodePositions[i] = new int2(
                nodes[i].Position.x,
                nodes[i].Position.y
            );
            nodePositionMap.TryAdd(nativeNodePositions[i], i);
        }
    }

    /// <summary>
    /// Find nearest nodes using Jobs.
    /// </summary>
    private List<RoadNode> FindNearestNodesOptimized(Vector2Int position, int count, float maxDistance)
    {
        int nodeCount = nativeNodePositions.Length;
        var distances = new NativeArray<float>(nodeCount, Allocator.TempJob);
        var validIndices = new NativeArray<int>(nodeCount, Allocator.TempJob);
        int2 targetPos = new int2(position.x, position.y);

        var job = new FindNearestNodesJob
        {
            nodePositions = nativeNodePositions,
            mainArteryFlags = nativeMainArteryFlags,
            targetPosition = targetPos,
            maxDistance = maxDistance,
            distances = distances,
            validIndices = validIndices
        };

        JobHandle handle = job.Schedule(nodeCount, 64);
        handle.Complete();

        // Filter and sort results
        var results = new List<RoadNode>();
        for (int i = 0; i < nodeCount; i++)
        {
            if (validIndices[i] == 1)
            {
                Vector2Int nodePos = new Vector2Int(
                    nativeNodePositions[i].x,
                    nativeNodePositions[i].y
                );
                results.Add(networkNodes[nodePos]);
            }
        }

        distances.Dispose();
        validIndices.Dispose();

        return results.OrderBy(n => Vector2Int.Distance(n.Position, position))
                      .Take(count)
                      .ToList();
    }

    /// <summary>
    ///Generates secondary connections using jobs.
    /// </summary>
    private void CreateSecondaryConnectionsOptimized(Vector2Int chunkOffset)
    {
        int gridSize = MacroCellsPerChunk / (MIN_ROAD_SPACING * 2);
        int totalPositions = gridSize * gridSize;

        var positionsToProcess = new NativeArray<int2>(totalPositions, Allocator.TempJob);

        var job = new ProcessSecondaryConnectionsJob
        {
            seed = chunkSeed,
            branchProbability = branchProbability,
            macroCellsPerChunk = MacroCellsPerChunk,
            chunkOffset = new int2(chunkOffset.x, chunkOffset.y),
            minRoadSpacing = MIN_ROAD_SPACING,
            positionsToProcess = positionsToProcess
        };

        JobHandle handle = job.Schedule(totalPositions, 64);
        handle.Complete();

        // Process results on main thread
        for (int i = 0; i < totalPositions; i++)
        {
            int2 posInt2 = positionsToProcess[i];
            Vector2Int pos = new Vector2Int(posInt2.x, posInt2.y);
            if (pos.x < 0 || pos.y < 0) continue;
            Vector2Int roadPos = new Vector2Int(pos.x, pos.y);

            if (IsSpaceAvailable(pos, SECONDARY_ROAD_SPACING))
            {
                var newNode = new RoadNode { Position = pos };
                networkNodes.Add(newNode.Position, newNode);

                var nearestNodes = FindNearestNodesOptimized(pos, 3, MacroCellsPerChunk / 2f);
                foreach (var nearNode in nearestNodes)
                {
                    ConnectNodesWithSpacing(newNode, nearNode);
                }
            }
        }

        positionsToProcess.Dispose();
    }

    /// <summary>
    /// References a road node.
    /// </summary>
    private class RoadNode
    {
        public Vector2Int Position { get; set; }
        public List<RoadNode> Connections { get; set; } = new List<RoadNode>();
        public bool IsMainArtery { get; set; }
        public bool IsBorderConnection { get; set; }
    }

    /// <summary>
    /// Initializes the generator and its components.
    /// </summary>
    /// <param name="chunkCoord">The coordinates of the current chunk.</param>
    /// <param name="grid">The grid dictionary.</param>
    /// <param name="seed">The seed of the chunk.</param>
    public void Initialize(Vector2Int chunkCoord, Dictionary<Vector2Int, MacroCell> grid, int seed)
    {
        chunkSeed = seed;
        gridCells = grid;
        currentChunkCoord = chunkCoord;
        roadNetwork.Clear();
        connectionMap.Clear();
        networkNodes.Clear();

        UnityEngine.Random.InitState(chunkSeed);
        seededRandom = new System.Random(chunkSeed);

        if (!globalConnectionPoints.ContainsKey(chunkCoord))
        {
            globalConnectionPoints[chunkCoord] = new HashSet<Vector2Int>();
        }

        InitializeNativeData();
    }

    /// <summary>
    /// Cleanup native data when done.
    /// </summary>
    private void OnDestroy()
    {
        if (nativeNodePositions.IsCreated) nativeNodePositions.Dispose();
        if (nativeMainArteryFlags.IsCreated) nativeMainArteryFlags.Dispose();
        if (nodePositionMap.IsCreated) nodePositionMap.Dispose();
    }

    /// <summary>
    /// Initial method to kickstart generation.
    /// </summary>
    /// <param name="parent">Parent that will be assigned to the new object</param>
    /// <param name="chunkCoord">Coordinates of the chunk,</param>
    public void GenerateAndPlaceRoads(Transform parent, Vector2Int chunkCoord)
    {
        Vector2Int chunkOffset = chunkCoord * MacroCellsPerChunk;
        // Generate network plan
        GenerateNetworkPlan(chunkOffset);

        // Place road tiles based on plan
        BuildRoadsFromPlan(parent, chunkOffset);

        // Clean up and validate
        CleanupRoadNetwork(parent, chunkOffset);
    }

    /// <summary>
    /// Generates a network plan.
    /// </summary>
    /// <param name="chunkOffset">The offset of the chunk.</param>
    private void GenerateNetworkPlan(Vector2Int chunkOffset)
    {
        float startTime = Time.realtimeSinceStartup;
        float timeout = 5f;

        try
        {
            // Create primary road network
            CreatePrimaryRoads(chunkOffset);
            if (Time.realtimeSinceStartup - startTime > timeout) throw new TimeoutException("Primary roads generation timed out");

            // Add secondary connections
            CreateSecondaryConnections(chunkOffset);
            if (Time.realtimeSinceStartup - startTime > timeout) throw new TimeoutException("Secondary connections timed out");

            // Ensure chunk connectivity
            EnsureChunkConnectivity(chunkOffset);
            if (Time.realtimeSinceStartup - startTime > timeout) throw new TimeoutException("Chunk connectivity timed out");
        }
        catch (TimeoutException e)
        {
            Debug.LogError($"Road generation timed out: {e.Message}");
            EmergencyCleanup();
            CreateFallbackRoads(chunkOffset);
        }
    }

    /// <summary>
    /// Create a fallback road network.
    /// </summary>
    /// <param name="chunkOffset">The offset of the chunk.</param>
    private void CreateFallbackRoads(Vector2Int chunkOffset)
    {
        int mid = MacroCellsPerChunk / 2;
        var centerNode = new RoadNode
        {
            Position = new Vector2Int(mid, mid) + chunkOffset,
            IsMainArtery = true
        };
        networkNodes.Add(centerNode.Position, centerNode);

        for (int i = 0; i < MacroCellsPerChunk; i += MIN_ROAD_SPACING)
        {
            if (i != mid)
            {
                var horizontalNode = new RoadNode
                {
                    Position = new Vector2Int(i, mid) + chunkOffset,
                    IsMainArtery = true
                };
                var verticalNode = new RoadNode
                {
                    Position = new Vector2Int(mid, i) + chunkOffset,
                    IsMainArtery = true
                };
                networkNodes.Add(horizontalNode.Position, horizontalNode);
                networkNodes.Add(verticalNode.Position, verticalNode);
            }
        }
    }

    /// <summary>
    /// Cleans up the road network.
    /// </summary>
    private void EmergencyCleanup()
    {
        networkNodes.Clear();
        foreach (var road in roadNetwork.Values)
        {
            if (road != null)
            {
                Destroy(road);
            }
        }
        roadNetwork.Clear();
        connectionMap.Clear();
    }

    /// <summary>
    /// Handles creation of primary roads.
    /// </summary>
    /// <param name="chunkOffset">The offset of the chunk.</param>
    private void CreatePrimaryRoads(Vector2Int chunkOffset)
    {
        // Create three main roads
        int[] positions = new int[] {
        MacroCellsPerChunk / 4,
        MacroCellsPerChunk / 2,
        3 * MacroCellsPerChunk / 4
    };

        foreach (int pos in positions)
        {
            // Always create both horizontal and vertical roads
            CreateStraightRoad(0, MacroCellsPerChunk - 1, pos, true, chunkOffset);
            CreateStraightRoad(0, MacroCellsPerChunk - 1, pos, false, chunkOffset);
        }
    }

    /// <summary>
    /// Handles creating straight roads.
    /// </summary>
    /// <param name="start">The starting coordinate along the road's axis.</param>
    /// <param name="end">The ending coordinate along the road's axis.</param>
    /// <param name="fixedPos">The fixed coordinate perpendicular to the road's axis.</param>
    /// <param name="horizontal">Whether the road is horizontal (true) or vertical (false).</param>
    /// <param name="chunkOffset">The offset for positioning within a chunk.</param>
    private void CreateStraightRoad(int start, int end, int fixedPos, bool horizontal, Vector2Int chunkOffset)
    {
        RoadNode prevNode = null;

        for (int i = start; i <= end; i += (int)MAIN_ROAD_SPACING)
        {
            var nodePos = horizontal ?
                new Vector2Int(i, fixedPos) + chunkOffset :
                new Vector2Int(fixedPos, i) + chunkOffset;

            var node = new RoadNode
            {
                Position = nodePos,
                IsMainArtery = true
            };

            networkNodes.Add(node.Position, node);

            if (prevNode != null)
            {
                prevNode.Connections.Add(node);
                node.Connections.Add(prevNode);
            }
            prevNode = node;
        }
    }

    /// <summary>
    /// Checks for distance to other roads for available space.
    /// </summary>
    /// <param name="position">Position to check for.</param>
    /// <param name="minDistance">Minimal distance required to keep.</param>
    private bool IsSpaceAvailable(Vector2Int position, float minDistance)
    {
        return !networkNodes.Values.Any(n =>
            n.Position != position &&
            UnityEngine.Vector2.Distance(n.Position, position) < minDistance);
    }

    /// <summary>
    /// Handles creating secondary road connections.
    /// </summary>
    /// <param name="chunkOffset">The offset of the chunk.</param>
    private void CreateSecondaryConnections(Vector2Int chunkOffset)
    {
        CreateSecondaryConnectionsOptimized(chunkOffset);
    }

    /// <summary>
    /// Attempts to create a border connection by finding the nearest main artery node.
    /// </summary>
    /// <param name="position">The border position where the connection should be placed.</param>
    /// <param name="direction">The intended direction of the border connection.</param>
    private void TryCreateBorderConnection(Vector2Int position, Vector2Int direction)
    {
        if (networkNodes.ContainsKey(position)) return;

        var nearestNode = FindNearestNode(position, n =>
            n.IsMainArtery &&
            UnityEngine.Vector2.Distance(n.Position, position) < BORDER_CONNECTION_RANGE * MacroCellsPerChunk &&
            !IsOppositeDirection(direction, NormalizeVector2Int(n.Position - position)));

        if (nearestNode != null)
        {
            var borderNode = new RoadNode
            {
                Position = position,
                IsBorderConnection = true
            };
            networkNodes.Add(borderNode.Position, borderNode);

            ConnectNodesWithCurve(borderNode, nearestNode, direction);
        }
    }

    /// <summary>
    /// Checks two vectors if they face the same direction.
    /// </summary>
    private bool IsOppositeDirection(Vector2Int dir1, Vector2Int dir2)
    {
        return dir1 == -dir2;
    }

    /// <summary>
    /// Creates a curved connection between two road nodes using a quadratic Bezier curve approach.
    /// </summary>
    /// <param name="start">The starting road node.</param>
    /// <param name="end">The ending road node.</param>
    /// <param name="initialDirection">The initial direction of the curve.</param>
    private void ConnectNodesWithCurve(RoadNode start, RoadNode end, Vector2Int initialDirection)
    {
        List<Vector2Int> pathPoints = new List<Vector2Int>();
        Vector2Int current = start.Position;
        int maxPoints = MacroCellsPerChunk * 2;

        Vector2Int control = start.Position + initialDirection * MIN_ROAD_SPACING * 2;
        float t = 0;
        int iterations = 0;

        while (t <= 1 && iterations < maxPoints)
        {
            Vector2Int newPos = Vector2Int.RoundToInt(UnityEngine.Vector2.Lerp(
                UnityEngine.Vector2.Lerp(start.Position, control, t),
                UnityEngine.Vector2.Lerp(control, end.Position, t),
                t
            ));

            if (!pathPoints.Contains(newPos) && UnityEngine.Vector2.Distance(newPos, current) >= MIN_ROAD_SPACING)
            {
                pathPoints.Add(newPos);
                current = newPos;
            }

            t += 0.1f;
            iterations++;
        }

        if (iterations >= maxPoints)
        {
            Debug.LogWarning("ConnectNodesWithCurve reached maximum points limit");
            return;
        }

        // Create nodes along the curve
        RoadNode previousNode = start;
        foreach (var point in pathPoints)
        {
            if (!networkNodes.TryGetValue(point, out RoadNode currentNode))
            {
                currentNode = new RoadNode { Position = point };
                networkNodes.Add(point, currentNode);
            }

            // Connect nodes
            previousNode.Connections.Add(currentNode);
            currentNode.Connections.Add(previousNode);

            previousNode = currentNode;
        }

        // Connect to end node
        previousNode.Connections.Add(end);
        end.Connections.Add(previousNode);
    }

    /// <summary>
    /// Finds the nearest road nodes to a given position.
    /// </summary>
    /// <param name="position">The position from which to search for nodes.</param>
    /// <param name="count">The number of closest nodes to retrieve.</param>
    /// <returns>A list of the closest road nodes.</returns>
    private List<RoadNode> FindNearestNodes(Vector2Int position, int count)
    {
        return FindNearestNodesOptimized(position, count, float.MaxValue);
    }

    /// <summary>
    /// Ensures connectivity between chunks for a smooth road network.
    /// </summary>
    /// <param name="chunkOffset">The offset of the chunk.</param>
    private void EnsureChunkConnectivity(Vector2Int chunkOffset)
    {
        var arteryNodes = networkNodes.Values.Where(n => n.IsMainArtery).ToList();
        var nodesToProcess = new List<(Vector2Int position, Vector2Int direction)>();

        // Randomize the number of border connections
        int maxBorderConnections = seededRandom.Next(2, 5);

        foreach (var node in arteryNodes)
        {
            Vector2Int localPos = node.Position - chunkOffset;
            if (IsNearChunkBoundary(localPos))
            {
                if (seededRandom.NextDouble() < 0.7f &&
                    nodesToProcess.Count < maxBorderConnections)
                {
                    Vector2Int boundaryPos = GetNearestBoundaryPosition(node.Position, chunkOffset);
                    Vector2Int direction = GetDirectionToBoundary(localPos);

                    boundaryPos = new Vector2Int(
                        Mathf.Clamp(boundaryPos.x, 0, MacroCellsPerChunk - 1),
                        Mathf.Clamp(boundaryPos.y, 0, MacroCellsPerChunk - 1)
                    );

                    nodesToProcess.Add((boundaryPos, direction));
                }
            }
        }

        foreach (var (position, direction) in nodesToProcess)
        {
            TryCreateBorderConnection(position, direction);
        }
    }

    /// <summary>
    /// Checks if a position is near a chunk boundary.
    /// </summary>
    /// <param name="localPos">Position to check.</param>
    private bool IsNearChunkBoundary(Vector2Int localPos)
    {
        return localPos.x <= MIN_ROAD_SPACING || localPos.x >= MacroCellsPerChunk - MIN_ROAD_SPACING ||
               localPos.y <= MIN_ROAD_SPACING || localPos.y >= MacroCellsPerChunk - MIN_ROAD_SPACING;
    }

    /// <summary>
    /// Retrieves the nearest boundary.
    /// </summary>
    /// <param name="position">Current position to check for.</param>
    /// <param name="chunkOffset">The offset of the chunk.</param>
    /// <returns></returns>
    private Vector2Int GetNearestBoundaryPosition(Vector2Int position, Vector2Int chunkOffset)
    {
        Vector2Int localPos = position - chunkOffset;
        Vector2Int boundaryPos = localPos;

        if (localPos.x <= MIN_ROAD_SPACING) boundaryPos.x = 0;
        else if (localPos.x >= MacroCellsPerChunk - MIN_ROAD_SPACING) boundaryPos.x = MacroCellsPerChunk - 1;
        if (localPos.y <= MIN_ROAD_SPACING) boundaryPos.y = 0;
        else if (localPos.y >= MacroCellsPerChunk - MIN_ROAD_SPACING) boundaryPos.y = MacroCellsPerChunk - 1;

        return boundaryPos + chunkOffset;
    }

    /// <summary>
    /// Returns direction to nearest boundary.
    /// </summary>
    /// <param name="localPos">Position to check for.</param>
    /// <returns>Vector pointing to next boundary.</returns>
    private Vector2Int GetDirectionToBoundary(Vector2Int localPos)
    {
        if (localPos.x <= MIN_ROAD_SPACING) return Vector2Int.left;
        if (localPos.x >= MacroCellsPerChunk - MIN_ROAD_SPACING) return Vector2Int.right;
        if (localPos.y <= MIN_ROAD_SPACING) return Vector2Int.down;
        return Vector2Int.up;
    }

    /// <summary>
    /// Finds the nearest road node to a given position that satisfies a specified condition.
    /// </summary>
    /// <param name="position">The position to search from.</param>
    /// <param name="predicate">A condition that the node must satisfy.</param>
    /// <returns>The nearest road node that meets the condition</returns>
    private RoadNode FindNearestNode(Vector2Int position, Func<RoadNode, bool> predicate)
    {
        return networkNodes.Values
            .Where(predicate)
            .OrderBy(n => UnityEngine.Vector2.Distance(n.Position, position))
            .FirstOrDefault();
    }

    /// <summary>
    /// Connects two road nodes with a spaced-out sequence of intermediary nodes.
    /// </summary>
    /// <param name="node1">The starting node.</param>
    /// <param name="node2">The ending node.</param>
    private void ConnectNodesWithSpacing(RoadNode node1, RoadNode node2)
    {
        Vector2Int current = node1.Position;
        Vector2Int step = Vector2Int.zero;

        while (current != node2.Position)
        {
            // Calculate direction towards node2
            Vector2Int dir = node2.Position - current;
            step.x = Mathf.Clamp(dir.x, -1, 1);
            step.y = Mathf.Clamp(dir.y, -1, 1);

            current += step;

            // Check if node exists at current position
            if (!networkNodes.TryGetValue(current, out RoadNode currentNode))
            {
                currentNode = new RoadNode { Position = current };
                networkNodes[current] = currentNode;
            }

            if (!node1.Connections.Contains(currentNode))
                node1.Connections.Add(currentNode);
            if (!currentNode.Connections.Contains(node1))
                currentNode.Connections.Add(node1);

            node1 = currentNode;
        }
    }

    /// <summary>
    /// Places an intersection prefab at the specified road node based on its connections.
    /// </summary>
    /// <param name="node">The road node where the intersection is placed.</param>
    /// <param name="parent">The parent transform for the road tile.</param>
    /// <param name="usedPositions">A set of already used positions to prevent overlap.</param>
    /// <param name="chunkOffset">The chunk offset applied to the position.</param>
    private void PlaceIntersection(RoadNode node, Transform parent, HashSet<Vector2Int> usedPositions, Vector2Int chunkOffset)
    {
        List<Vector2Int> connections = node.Connections
            .Select(n => NormalizeVector2Int(n.Position - node.Position))
            .Where(v => v != Vector2Int.zero)
            .Distinct()
            .ToList();

        GameObject prefab;
        switch (connections.Count)
        {
            case 1:
                prefab = HelperFunctions.SelectBasedOnIndexPriority(roadEndPrefab);
                break;
            case 2:
                prefab = IsCorner(connections) ?
                    HelperFunctions.SelectBasedOnIndexPriority(roadCornerPrefab) :
                    HelperFunctions.SelectBasedOnIndexPriority(roadLanePrefab);
                break;
            case 3:
                prefab = HelperFunctions.SelectBasedOnIndexPriority(tIntersectionPrefab);
                break;
            case 4:
                prefab = HelperFunctions.SelectBasedOnIndexPriority(roadIntersectionPrefab);
                break;
            default:
                prefab = HelperFunctions.SelectBasedOnIndexPriority(roadLanePrefab);
                break;
        }

        PlaceRoadTile(node.Position, prefab, connections, parent, usedPositions, chunkOffset);
    }

    /// <summary>
    /// Connects two road nodes with a series of road segments.
    /// </summary>
    /// <param name="node1">The starting node.</param>
    /// <param name="node2">The ending node.</param>
    /// <param name="parent">The parent transform for the road tiles.</param>
    /// <param name="usedPositions">A set of already used positions to prevent overlap.</param>
    /// <param name="chunkOffset">The chunk offset applied to the position.</param>
    private void ConnectNodesWithRoads(RoadNode node1, RoadNode node2, Transform parent, HashSet<Vector2Int> usedPositions, Vector2Int chunkOffset)
    {
        Vector2Int direction = NormalizeVector2Int(node2.Position - node1.Position);
        Vector2Int current = node1.Position + direction;

        int maxIterations = MacroCellsPerChunk * 2;
        int iterations = 0;

        while (current != node2.Position && iterations < maxIterations)
        {
            if (!usedPositions.Contains(current))
            {
                PlaceRoadTile(current,
                    HelperFunctions.SelectBasedOnIndexPriority(roadLanePrefab),
                    new List<Vector2Int> { direction, -direction },
                    parent, usedPositions, chunkOffset);
            }
            current += direction;
            iterations++;
        }

        if (iterations >= maxIterations)
        {
            Debug.LogWarning($"ConnectNodesWithRoads reached iteration limit connecting {node1.Position} to {node2.Position}");
        }
    }

    /// <summary>
    /// Cleans up the road network by removing invalid road tiles and ensuring valid connections.
    /// </summary>
    /// <param name="parent">The parent transform of the road network.</param>
    /// <param name="chunkOffset">The chunk offset applied to positions.</param>
    private void CleanupRoadNetwork(Transform parent, Vector2Int chunkOffset)
    {
        // Remove invalid tiles
        var positions = new HashSet<Vector2Int>(roadNetwork.Keys);
        foreach (var pos in positions)
        {
            if (!HasValidConnections(pos))
            {
                RemoveRoadTile(pos);
            }
        }

        ValidateAndCleanupRoadNetwork(parent, chunkOffset);
    }

    /// <summary>
    /// Checks if a road tile at a given position has at least one valid connection.
    /// </summary>
    /// <param name="position">The position of the road tile.</param>
    private bool HasValidConnections(Vector2Int position)
    {
        int connectedNeighbors = 0;
        foreach (var dir in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
        {
            if (roadNetwork.ContainsKey(position + dir))
            {
                connectedNeighbors++;
            }
        }
        return connectedNeighbors > 0;
    }

    /// <summary>
    /// Constructs roads based on the existing network plan.
    /// </summary>
    /// <param name="parent">The parent transform for the road network.</param>
    /// <param name="chunkOffset">The chunk offset applied to positions.</param>
    private void BuildRoadsFromPlan(Transform parent, Vector2Int chunkOffset)
    {
        HashSet<Vector2Int> usedPositions = new HashSet<Vector2Int>();

        foreach (var kvp in networkNodes)
        {
            PlaceIntersection(kvp.Value, parent, usedPositions, chunkOffset);
        }

        foreach (var kvp in networkNodes)
        {
            foreach (var connection in kvp.Value.Connections)
            {
                if (kvp.Value.Position.x <= connection.Position.x && kvp.Value.Position.y <= connection.Position.y)
                {
                    ConnectNodesWithRoads(kvp.Value, connection, parent, usedPositions, chunkOffset);
                }
            }
        }
    }

    /// <summary>
    /// Places a road tile at the specified position within the chunk, ensuring no overlap or boundary issues.
    /// </summary>
    /// <param name="position">The position in the world grid where the road tile should be placed.</param>
    /// <param name="prefab">The road prefab to instantiate.</param>
    /// <param name="connections">A list of positions that this road tile is connected to.</param>
    /// <param name="parent">The parent transform to attach the road tile to.</param>
    /// <param name="usedPositions">A set of positions that have already been used to place roads.</param>
    /// <param name="chunkOffset">The offset of the current chunk in world space.</param>
    private void PlaceRoadTile(Vector2Int position, GameObject prefab, List<Vector2Int> connections,
    Transform parent, HashSet<Vector2Int> usedPositions, Vector2Int chunkOffset)
    {
        if (usedPositions.Contains(position))
            return;

        Vector2Int localPos = position - chunkOffset;

        if (localPos.x < 0 || localPos.x >= MacroCellsPerChunk ||
            localPos.y < 0 || localPos.y >= MacroCellsPerChunk)
        {
            Debug.LogWarning($"Attempted to place road outside chunk boundaries at position {position} (local: {localPos})");
            return;
        }

        // Check for existing road in the roadNetwork dictionary
        if (roadNetwork.ContainsKey(position))
        {
            Debug.LogWarning($"Duplicate road at {position}, skipping placement.");
            usedPositions.Add(position);
            return;
        }

        // Physics check for overlapping roads
        var worldPos = new UnityEngine.Vector3(position.x * roadTileSize, 0.1f, position.y * roadTileSize);
        var overlaps = FindOverlappingRoads(worldPos);
        if (overlaps.Count > 0)
        {
            foreach (var road in overlaps)
            {
                Vector2Int roadPos = WorldToGridPosition(road.transform.position);
                Destroy(road);
                roadNetwork.Remove(roadPos);
                connectionMap.Remove(roadPos);
            }
        }

        if (!gridCells.ContainsKey(localPos))
        {
            // Initialize new grid cell if it doesn't exist
            gridCells[localPos] = new MacroCell(localPos, worldPos);
        }

        // Check if this exact position already has a road
        if (roadNetwork.TryGetValue(position, out GameObject existingRoad))
        {
            Debug.LogWarning($"Road already exists at position {position}. Skipping placement.");
            usedPositions.Add(position);
            return;
        }

        var validConnections = new HashSet<Vector2Int>(connections);
        CleanupInvalidConnections(position, validConnections);

        // If we have no valid connections, don't place the road
        if (validConnections.Count == 0)
        {
            return;
        }

        GameObject selectedPrefab = SelectCleanupRoadPrefab(position, connections);
        if (selectedPrefab == null)
            selectedPrefab = prefab;

        // Place the road
        GameObject roadTile = Instantiate(selectedPrefab, worldPos, UnityEngine.Quaternion.identity, parent);
        roadTile.tag = ROAD_TAG;
        AlignRoadTile(roadTile, validConnections.ToList());

        roadNetwork[position] = roadTile;
        usedPositions.Add(position);
        connectionMap[position] = validConnections;

        // Update grid cell type and subcells
        var cell = gridCells[localPos];
        cell.Type = CellType.Road;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                cell.SubCells[x, y].Type = CellType.Road;
                cell.SubCells[x, y].isReserved = true;
            }
        }

        gridCells[localPos] = cell;
    }

    /// <summary>
    /// Updates an existing road tile at the specified position based on the current connections.
    /// </summary>
    /// <param name="position">The position of the road to update.</param>
    /// <param name="connections">A list of positions that this road tile is connected to.</param>
    /// <param name="parent">The parent transform to attach the updated road tile to.</param>
    /// <param name="chunkCoord">The coordinates of the chunk the road tile is in.</param>
    private void UpdateRoadTile(Vector2Int position, HashSet<Vector2Int> connections, Transform parent, Vector2Int chunkCoord)
    {
        Vector2Int chunkOffset = chunkCoord * MacroCellsPerChunk;
        if (roadNetwork.ContainsKey(position))
        {
            Destroy(roadNetwork[position]);

            bool isChunkBoundary = IsChunkBoundary(position, chunkOffset);
            bool shouldBeEndTile = connections.Count == 1 && !isChunkBoundary;

            GameObject prefab;
            if (shouldBeEndTile)
            {
                prefab = HelperFunctions.SelectBasedOnIndexPriority(roadEndPrefab);
            }
            else
            {
                prefab = SelectAppropriateRoadPrefab(position, new List<Vector2Int>(connections), chunkOffset);
            }

            UnityEngine.Vector3 worldPosition = WorldPositionForCell(position);
            GameObject roadTile = Instantiate(prefab, worldPosition, UnityEngine.Quaternion.identity, parent);

            AlignRoadTile(roadTile, new List<Vector2Int>(connections));

            roadNetwork[position] = roadTile;
            connectionMap[position] = connections;

            Vector2Int localGridPos = position - chunkOffset;
            if (gridCells.ContainsKey(localGridPos))
            {
                MacroCell macroCell = gridCells[localGridPos];
                macroCell.Type = CellType.Road;
                macroCell.Prefab = prefab;
                gridCells[localGridPos] = macroCell;
            }
        }
    }

    /// <summary>
    /// Calculates the world position for a given grid cell position.
    /// </summary>
    /// <param name="gridPosition">The grid position of the cell.</param>
    /// <returns>The corresponding world position.</returns>
    private UnityEngine.Vector3 WorldPositionForCell(Vector2Int gridPosition)
    {
        // Adjust the position calculation to match the grid initialization
        return new UnityEngine.Vector3(
            gridPosition.x * roadTileSize,
            0.1f,
            gridPosition.y * roadTileSize
        );
    }

    /// <summary>
    /// Selects the appropriate road prefab based on the position and connections.
    /// </summary>
    /// <param name="position">The position of the road tile.</param>
    /// <param name="connections">A list of positions that this road tile is connected to.</param>
    /// <param name="chunkOffset">The offset of the current chunk in world space.</param>
    /// <returns>The selected road prefab.</returns>
    private GameObject SelectAppropriateRoadPrefab(Vector2Int position, List<Vector2Int> connections, Vector2Int chunkOffset)
    {
        if (IsChunkBoundary(position, chunkOffset))
        {
            if (connections.Count == 1)
                return HelperFunctions.SelectBasedOnIndexPriority(roadLanePrefab);

            if (connections.Count == 2 && !IsCorner(connections))
                return HelperFunctions.SelectBasedOnIndexPriority(roadLanePrefab);
        }

        switch (connections.Count)
        {
            case 1:
                return HelperFunctions.SelectBasedOnIndexPriority(roadEndPrefab);
            case 2:
                return IsCorner(connections) ? HelperFunctions.SelectBasedOnIndexPriority(roadCornerPrefab) : HelperFunctions.SelectBasedOnIndexPriority(roadLanePrefab);
            case 3:
                return HelperFunctions.SelectBasedOnIndexPriority(tIntersectionPrefab);
            case 4:
                return HelperFunctions.SelectBasedOnIndexPriority(roadIntersectionPrefab);
            default:
                return HelperFunctions.SelectBasedOnIndexPriority(roadLanePrefab);
        }
    }

    /// <summary>
    /// Checks if the given connections list represents a corner (two directions).
    /// </summary>
    /// <param name="connections">The list of connections.</param>
    private bool IsCorner(List<Vector2Int> connections)
    {
        if (connections.Count != 2) return false;
        return connections[0].x != -connections[1].x && connections[0].y != -connections[1].y;
    }

    /// <summary>
    /// Aligns the road tile to the correct rotation based on the provided connections.
    /// </summary>
    /// <param name="roadTile">The road tile to align.</param>
    /// <param name="connections">The list of connections for the road tile.</param>
    private void AlignRoadTile(GameObject roadTile, List<Vector2Int> connections)
    {
        float rotation = 0f;
        connections = connections.OrderBy(d => d.x + d.y * 2).ToList();

        switch (connections.Count)
        {
            case 1: // End piece
                if (connections[0] == Vector2Int.up) rotation = 180f;
                else if (connections[0] == Vector2Int.right) rotation = 270f;
                else if (connections[0] == Vector2Int.down) rotation = 0f;
                else if (connections[0] == Vector2Int.left) rotation = 90f;
                break;

            case 2 when IsCorner(connections): // Corner
                Vector2Int a = connections[0];
                Vector2Int b = connections[1];

                if (a == Vector2Int.down && b == Vector2Int.right) rotation = 0f;
                else if (a == Vector2Int.left && b == Vector2Int.down) rotation = 90f;
                else if (a == Vector2Int.up && b == Vector2Int.left) rotation = 180f;
                else if (a == Vector2Int.right && b == Vector2Int.up) rotation = 270f;
                break;

            case 2: // Straight
                rotation = connections.Contains(Vector2Int.left) ? 90f : 0f;
                break;

            case 3: // T-Intersection
                var allDirections = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
                var missing = allDirections.Except(connections).First();

                if (missing == Vector2Int.up) rotation = 180f;
                else if (missing == Vector2Int.right) rotation = 270f;
                else if (missing == Vector2Int.down) rotation = 0f;
                else if (missing == Vector2Int.left) rotation = 90f;
                break;

            case 4: // Intersection
                rotation = 0f;
                break;
        }

        roadTile.transform.rotation = UnityEngine.Quaternion.Euler(0, rotation, 0);
    }

    /// <summary>
    /// Sets the random seed for road generation.
    /// </summary>
    /// <param name="seed">The seed value to initialize the random number generator.</param>
    public void SetSeed(int seed)
    {
        UnityEngine.Random.InitState(seed);
    }

    /// <summary>
    /// Determines if the given position is at the boundary of the chunk.
    /// </summary>
    /// <param name="position">The position to check.</param>
    /// <param name="chunkOffset">The chunk offset for boundary checks.</param>
    private bool IsChunkBoundary(Vector2Int position, Vector2Int chunkOffset)
    {
        Vector2Int globalPos = position;
        return globalPos.x == 0 || globalPos.x == (MacroCellsPerChunk * currentChunkCoord.x) - 1 ||
               globalPos.y == 0 || globalPos.y == (MacroCellsPerChunk * currentChunkCoord.y) - 1;
    }

    /// <summary>
    /// Updates the connections of a road tile by checking its neighboring positions.
    /// </summary>
    /// <param name="position">The position of the road tile to update.</param>
    /// <param name="parent">The parent transform to which the road tile belongs.</param>
    /// <param name="chunkOffset">The chunk offset used to update connections.</param>
    private void UpdateRoadTileConnections(Vector2Int position, Transform parent, Vector2Int chunkOffset)
    {
        HashSet<Vector2Int> connections = new HashSet<Vector2Int>();

        // Check all four directions for valid connections
        foreach (var dir in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
        {
            Vector2Int neighborPos = position + dir;
            if (roadNetwork.ContainsKey(neighborPos))
            {
                connections.Add(dir);
            }
        }

        UpdateRoadTile(position, connections, parent, chunkOffset / MacroCellsPerChunk);
    }

    /// <summary>
    /// Normalizes a Vector2Int to either 1 or -1 based on its direction.
    /// </summary>
    /// <param name="vector">The Vector2Int to normalize.</param>
    /// <returns>The normalized Vector2Int.</returns>
    private Vector2Int NormalizeVector2Int(Vector2Int vector)
    {
        return new Vector2Int(
            vector.x != 0 ? vector.x / Mathf.Abs(vector.x) : 0,
            vector.y != 0 ? vector.y / Mathf.Abs(vector.y) : 0
        );
    }

    /// <summary>
    /// Finds all overlapping road tiles at a given world position.
    /// </summary>
    /// <param name="worldPosition">The world position to check for overlapping roads.</param>
    /// <returns>A list of overlapping road tiles.</returns>
    private List<GameObject> FindOverlappingRoads(UnityEngine.Vector3 worldPosition)
    {
        Vector2Int gridPos = WorldToGridPosition(worldPosition);
        return roadNetwork
            .Where(kvp => kvp.Key == gridPos)
            .Select(kvp => kvp.Value)
            .ToList();
    }

    /// <summary>
    /// Checks if a road connection is valid based on its neighboring connections.
    /// </summary>
    /// <param name="position">The position of the road to check.</param>
    /// <param name="direction">The direction of the potential connection.</param>
    /// <param name="isEndPiece">True if the road is an end piece, otherwise false.</param>
    private bool IsValidRoadConnection(Vector2Int position, Vector2Int direction, bool isEndPiece)
    {
        Vector2Int neighborPos = position + direction;
        if (!roadNetwork.ContainsKey(neighborPos))
            return true;

        if (!connectionMap.TryGetValue(neighborPos, out HashSet<Vector2Int> neighborConnections))
            return true;

        if (networkNodes.Values.Any(n => n.Position == position && n.IsMainArtery))
            return true;

        if (neighborConnections.Count < 4)
            return true;

        return neighborConnections.Contains(-direction);
    }

    /// <summary>
    /// Cleans up any invalid road connections by removing them from the given connections list.
    /// </summary>
    /// <param name="position">The position of the road to check.</param>
    /// <param name="connections">The set of connections to clean up.</param>
    private void CleanupInvalidConnections(Vector2Int position, HashSet<Vector2Int> connections)
    {
        var invalidConnections = new HashSet<Vector2Int>();
        bool isEndPiece = connections.Count == 1;

        foreach (var direction in connections)
        {
            if (!IsValidRoadConnection(position, direction, isEndPiece))
            {
                invalidConnections.Add(direction);
            }
        }

        foreach (var invalid in invalidConnections)
        {
            connections.Remove(invalid);
        }
    }

    /// <summary>
    /// Selects a cleanup prefab for a road tile based on its connections.
    /// </summary>
    /// <param name="position">The position of the road tile.</param>
    /// <param name="connections">The connections of the road tile.</param>
    /// <returns>The selected cleanup prefab.</returns>
    private GameObject SelectCleanupRoadPrefab(Vector2Int position, List<Vector2Int> connections)
    {
        // Only use cleanup prefab for specific cases
        if (connections.Count <= 2)
            return null; // Use normal prefabs for simple connections

        foreach (var direction in connections)
        {
            Vector2Int neighborPos = position + direction;
            if (!roadNetwork.ContainsKey(neighborPos))
                continue;

            var neighborConnections = connectionMap[neighborPos];

            // Use cleanup prefab if connecting to the side of a straight road
            if (!neighborConnections.Contains(-direction) &&
                neighborConnections.Count == 2 &&
                !IsCorner(new List<Vector2Int>(neighborConnections)))
            {
                return HelperFunctions.SelectBasedOnIndexPriority(roadCleanupPrefab);
            }
        }
        return null;
    }

    /// <summary>
    /// Validates and cleans up the road network by removing invalid tiles and updating connections.
    /// </summary>
    /// <param name="parent">The parent transform for the road tiles.</param>
    /// <param name="chunkOffset">The offset of the chunk to validate.</param>
    private void ValidateAndCleanupRoadNetwork(Transform parent, Vector2Int chunkOffset)
    {
        var positions = new HashSet<Vector2Int>(roadNetwork.Keys);

        foreach (var pos in positions)
        {
            if (!connectionMap.ContainsKey(pos) || connectionMap[pos].Count == 0)
            {
                RemoveRoadTile(pos);
                continue;
            }

            var connections = GetValidConnections(pos);
            if (connections.Count > 0)
            {
                UpdateRoadTileConnections(pos, parent, chunkOffset);
            }
            else
            {
                RemoveRoadTile(pos);
            }
        }
    }

    /// <summary>
    /// Removes a road tile at a certain position.
    /// </summary>
    /// <param name="position">The position of the road tile.</param>
    private void RemoveRoadTile(Vector2Int position)
    {
        if (roadNetwork.TryGetValue(position, out GameObject roadTile))
        {
            // Unreserve subcells
            Vector2Int localPos = position - (currentChunkCoord * MacroCellsPerChunk);
            if (gridCells.TryGetValue(localPos, out MacroCell cell))
            {
                cell.Type = CellType.Empty;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        cell.SubCells[x, y].Type = CellType.Empty;
                        cell.SubCells[x, y].isReserved = false;
                    }
                }
                gridCells[localPos] = cell;
            }
            Destroy(roadTile);
            roadNetwork.Remove(position);
            connectionMap.Remove(position);
        }
    }

    /// <summary>
    /// Gets a valid connection for a given position.
    /// </summary>
    /// <param name="position">The position to get a valid connection for.</param>
    /// <returns>HashSet of a 2D Vector containing the direction.</returns>
    private HashSet<Vector2Int> GetValidConnections(Vector2Int position)
    {
        var connections = new HashSet<Vector2Int>();

        foreach (var dir in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
        {
            Vector2Int neighborPos = position + dir;
            if (!roadNetwork.ContainsKey(neighborPos))
                continue;

            // Check if this is a valid connection
            if (IsValidRoadConnection(position, dir, false))
            {
                connections.Add(dir);
            }
        }

        return connections;
    }

    /// <summary>
    /// Converts world position to grid position.
    /// </summary>
    /// <param name="worldPosition">World position to calculate grid position for.</param>
    private Vector2Int WorldToGridPosition(UnityEngine.Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x / roadTileSize),
            Mathf.RoundToInt(worldPosition.z / roadTileSize)
        );
    }
}