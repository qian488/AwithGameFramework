using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SlotShape", menuName = "BBads/Backpack Slot Shape", order = 50)]
public class BackpackSlotSO : ScriptableObject
{
    [Tooltip("形状显示名，如 SlotH2")]
    public string shapeId;

    [Tooltip("相对占用格；(0,0)=左上，y 向下")]
    public List<Vector2Int> cells = new List<Vector2Int> { Vector2Int.zero };

    public int CellCount => cells != null ? cells.Count : 0;

    public bool TryBuildFootprint(out int width, out int height, out HashSet<Vector2Int> normalized)
    {
        width = 0;
        height = 0;
        normalized = new HashSet<Vector2Int>();
        if (cells == null || cells.Count == 0) return false;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in cells)
        {
            if (!normalized.Add(c)) continue;
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        if (normalized.Count == 0) return false;

        if (minX != 0 || minY != 0)
        {
            var shifted = new HashSet<Vector2Int>();
            foreach (var c in normalized)
                shifted.Add(new Vector2Int(c.x - minX, c.y - minY));
            normalized = shifted;
            maxX -= minX;
            maxY -= minY;
        }

        width = maxX + 1;
        height = maxY + 1;
        return width > 0 && height > 0;
    }

    public bool TryBuildGridCells(out List<Vector2Int> gridCells)
    {
        gridCells = new List<Vector2Int>();
        if (!TryBuildFootprint(out _, out int height, out HashSet<Vector2Int> normalized))
            return false;
        foreach (Vector2Int cell in normalized)
            gridCells.Add(new Vector2Int(cell.x, height - 1 - cell.y));
        gridCells.Sort((a, b) =>
        {
            int y = b.y.CompareTo(a.y);
            return y != 0 ? y : a.x.CompareTo(b.x);
        });
        return gridCells.Count > 0;
    }
}
