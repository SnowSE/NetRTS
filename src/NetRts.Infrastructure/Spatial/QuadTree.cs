using NetRts.Domain.ValueObjects;

namespace NetRts.Infrastructure.Spatial;

/// <summary>
/// QuadTree spatial indexing structure for efficient range queries.
/// Used for fog of war calculations with O(log n) lookup performance.
/// </summary>
public class QuadTree<T> where T : class
{
    private readonly int _maxDepth;
    private readonly int _maxItems;
    private readonly QuadTreeNode<T> _root;

    public QuadTree(int mapWidth, int mapHeight, int maxDepth = 8, int maxItems = 10)
    {
        _maxDepth = maxDepth;
        _maxItems = maxItems;
        _root = new QuadTreeNode<T>(0, 0, mapWidth, mapHeight, 0, _maxDepth, _maxItems);
    }

    /// <summary>
    /// Insert an item at a position.
    /// </summary>
    public void Insert(Position position, T item)
    {
        _root.Insert(position, item);
    }

    /// <summary>
    /// Query items within a range of a position.
    /// </summary>
    public List<(Position, T)> QueryRange(Position center, double range)
    {
        var results = new List<(Position, T)>();
        _root.QueryRange(center, range, results);
        return results;
    }

    /// <summary>
    /// Clear all items from the tree.
    /// </summary>
    public void Clear()
    {
        _root.Clear();
    }
}

internal class QuadTreeNode<T> where T : class
{
    private readonly int _x;
    private readonly int _y;
    private readonly int _width;
    private readonly int _height;
    private readonly int _depth;
    private readonly int _maxDepth;
    private readonly int _maxItems;

    private readonly List<(Position, T)> _items;
    private QuadTreeNode<T>[]? _children;

    public QuadTreeNode(int x, int y, int width, int height, int depth, int maxDepth, int maxItems)
    {
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _depth = depth;
        _maxDepth = maxDepth;
        _maxItems = maxItems;
        _items = new List<(Position, T)>();
    }

    public void Insert(Position position, T item)
    {
        // Check if position is within bounds
        if (position.X < _x || position.X >= _x + _width ||
            position.Y < _y || position.Y >= _y + _height)
        {
            return;
        }

        // If we have children, insert into appropriate child
        if (_children != null)
        {
            var childIndex = GetChildIndex(position);
            _children[childIndex].Insert(position, item);
            return;
        }

        // Add to this node
        _items.Add((position, item));

        // Subdivide if necessary
        if (_items.Count > _maxItems && _depth < _maxDepth)
        {
            Subdivide();

            // Redistribute items to children
            var itemsToRedistribute = new List<(Position, T)>(_items);
            _items.Clear();

            foreach (var (pos, itm) in itemsToRedistribute)
            {
                var childIndex = GetChildIndex(pos);
                _children![childIndex].Insert(pos, itm);
            }
        }
    }

    public void QueryRange(Position center, double range, List<(Position, T)> results)
    {
        // Check if this node intersects with the query range
        if (!IntersectsCircle(center, range))
        {
            return;
        }

        // Add items within range from this node
        foreach (var (position, item) in _items)
        {
            if (position.DistanceTo(center) <= range)
            {
                results.Add((position, item));
            }
        }

        // Query children if they exist
        if (_children != null)
        {
            foreach (var child in _children)
            {
                child.QueryRange(center, range, results);
            }
        }
    }

    public void Clear()
    {
        _items.Clear();
        if (_children != null)
        {
            foreach (var child in _children)
            {
                child.Clear();
            }
            _children = null;
        }
    }

    private void Subdivide()
    {
        var halfWidth = _width / 2;
        var halfHeight = _height / 2;
        var nextDepth = _depth + 1;

        _children = new QuadTreeNode<T>[4];

        // Top-left
        _children[0] = new QuadTreeNode<T>(_x, _y, halfWidth, halfHeight, nextDepth, _maxDepth, _maxItems);

        // Top-right
        _children[1] = new QuadTreeNode<T>(_x + halfWidth, _y, halfWidth, halfHeight, nextDepth, _maxDepth, _maxItems);

        // Bottom-left
        _children[2] = new QuadTreeNode<T>(_x, _y + halfHeight, halfWidth, halfHeight, nextDepth, _maxDepth, _maxItems);

        // Bottom-right
        _children[3] = new QuadTreeNode<T>(_x + halfWidth, _y + halfHeight, halfWidth, halfHeight, nextDepth, _maxDepth, _maxItems);
    }

    private int GetChildIndex(Position position)
    {
        var halfWidth = _width / 2;
        var halfHeight = _height / 2;
        var midX = _x + halfWidth;
        var midY = _y + halfHeight;

        var isRight = position.X >= midX;
        var isBottom = position.Y >= midY;

        if (!isRight && !isBottom) return 0; // Top-left
        if (isRight && !isBottom) return 1;  // Top-right
        if (!isRight && isBottom) return 2;  // Bottom-left
        return 3;                             // Bottom-right
    }

    private bool IntersectsCircle(Position center, double radius)
    {
        // Find closest point in rectangle to circle center
        var closestX = Math.Clamp(center.X, _x, _x + _width);
        var closestY = Math.Clamp(center.Y, _y, _y + _height);

        // Calculate distance from circle center to closest point
        var distanceX = center.X - closestX;
        var distanceY = center.Y - closestY;
        var distanceSquared = (distanceX * distanceX) + (distanceY * distanceY);

        return distanceSquared <= (radius * radius);
    }
}
