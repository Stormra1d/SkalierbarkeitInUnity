using UnityEngine;

/// <summary>
/// Enum containing all possible building types.
/// </summary>
public enum BuildingType
{
    Skyscraper,
    SmallSkyscraper,
    WideShop,
    NormalShop,
    Factory,
    FootballStadium,
    Special,
    NormalHouse,
    WideHouse,
    SuperWideHouse,
    ResidentialBlock
}

/// <summary>
/// Configuration for BuildingTypes.
/// </summary>
[System.Serializable]
public class BuildingTypeConfig
{
    public BuildingType type;
    public GameObject[] prefabs;
    [Range(0f, 1f)] public float spawnChance;
    public Vector2Int size;
}