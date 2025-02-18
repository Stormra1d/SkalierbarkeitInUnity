using UnityEngine;

/// <summary>
/// Enum for all possible cell types.
/// </summary>
public enum CellType
{
    Empty,
    Road,
    Park,
    Building,
    Reserved
}

/// <summary>
/// Defines MacroCells with parameters.
/// </summary>
public struct MacroCell
{
    public CellType Type;
    public Vector2Int Position;
    public Vector3 WorldPosition;
    public GameObject Prefab;
    public bool isReserved;
    public SubCell[,] SubCells;

    public MacroCell(Vector2Int pos, Vector3 worldPos)
    {
        Position = pos;
        WorldPosition = worldPos;
        Type = CellType.Empty;
        Prefab = null;
        isReserved = false;
        SubCells = new SubCell[2, 2];
        InitializeSubCells();
    }

    /// <summary>
    /// Initializes 4 subcells per macro cell.
    /// </summary>
    private void InitializeSubCells()
    {
        for (int x = 0; x < CityChunk.MACRO_CELL_SUBDIVISIONS; x++)
        {
            for (int y = 0; y < CityChunk.MACRO_CELL_SUBDIVISIONS; y++)
            {
                SubCells[x, y] = new SubCell();
            }
        }
    }
}

/// <summary>
/// Defines subcells with parameters.
/// </summary>
public struct SubCell
{
    public CellType Type;
    public Vector2Int Position;
    public Vector3 WorldPosition;
    public bool isReserved;
}