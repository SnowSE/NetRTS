using FluentAssertions;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Spatial;

namespace NetRts.UnitTests.Infrastructure.Spatial;

public class QuadTreeTests
{
    [Fact]
    public void Insert_AndQueryRange_ReturnsItemsWithinRange()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100);
        var centerPosition = new Position(50, 50);

        quadTree.Insert(new Position(50, 50), "Center");
        quadTree.Insert(new Position(52, 52), "Near");
        quadTree.Insert(new Position(90, 90), "Far");

        // Act
        var results = quadTree.QueryRange(centerPosition, 5.0);

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Item2 == "Center");
        results.Should().Contain(r => r.Item2 == "Near");
        results.Should().NotContain(r => r.Item2 == "Far");
    }

    [Fact]
    public void QueryRange_WithEmptyTree_ReturnsEmptyList()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100);

        // Act
        var results = quadTree.QueryRange(new Position(50, 50), 10.0);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void QueryRange_WithZeroRange_ReturnsOnlyExactMatches()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100);
        var exactPosition = new Position(50, 50);

        quadTree.Insert(exactPosition, "Exact");
        quadTree.Insert(new Position(51, 51), "Near");

        // Act
        var results = quadTree.QueryRange(exactPosition, 0.0);

        // Assert
        results.Should().HaveCount(1);
        results.Should().Contain(r => r.Item2 == "Exact");
    }

    [Fact]
    public void Insert_MultipleItems_AllCanBeQueried()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100);

        for (int i = 0; i < 50; i++)
        {
            quadTree.Insert(new Position(i, i), i.ToString());
        }

        // Act
        var results = quadTree.QueryRange(new Position(25, 25), 10.0);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Item2 == "25");
    }

    [Fact]
    public void QueryRange_CircularRange_ReturnsCorrectItems()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100);
        var center = new Position(50, 50);

        // Insert items in cardinal directions
        quadTree.Insert(new Position(50, 53), "North-3");   // Distance 3
        quadTree.Insert(new Position(50, 47), "South-3");   // Distance 3
        quadTree.Insert(new Position(53, 50), "East-3");    // Distance 3
        quadTree.Insert(new Position(47, 50), "West-3");    // Distance 3
        quadTree.Insert(new Position(50, 56), "North-6");   // Distance 6
        quadTree.Insert(new Position(56, 50), "East-6");    // Distance 6

        // Act
        var results = quadTree.QueryRange(center, 5.0);

        // Assert
        results.Should().HaveCount(4);
        results.Should().Contain(r => r.Item2 == "North-3");
        results.Should().Contain(r => r.Item2 == "South-3");
        results.Should().Contain(r => r.Item2 == "East-3");
        results.Should().Contain(r => r.Item2 == "West-3");
        results.Should().NotContain(r => r.Item2 == "North-6");
        results.Should().NotContain(r => r.Item2 == "East-6");
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100);

        quadTree.Insert(new Position(10, 10), "Item1");
        quadTree.Insert(new Position(20, 20), "Item2");
        quadTree.Insert(new Position(30, 30), "Item3");

        // Act
        quadTree.Clear();
        var results = quadTree.QueryRange(new Position(20, 20), 50.0);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Insert_OutsideBounds_IsIgnored()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100);

        // Insert outside bounds
        quadTree.Insert(new Position(-1, -1), "OutOfBounds1");
        quadTree.Insert(new Position(101, 101), "OutOfBounds2");
        quadTree.Insert(new Position(50, 50), "InBounds");

        // Act
        var results = quadTree.QueryRange(new Position(50, 50), 100.0);

        // Assert
        results.Should().HaveCount(1);
        results.Should().Contain(r => r.Item2 == "InBounds");
        results.Should().NotContain(r => r.Item2 == "OutOfBounds1");
        results.Should().NotContain(r => r.Item2 == "OutOfBounds2");
    }

    [Fact]
    public void QueryRange_PerformanceWithManyItems_CompletesQuickly()
    {
        // Arrange
        var quadTree = new QuadTree<string>(200, 200);
        var random = new Random(42); // Seed for reproducibility

        // Insert 1000 items
        for (int i = 0; i < 1000; i++)
        {
            var x = random.Next(0, 200);
            var y = random.Next(0, 200);
            quadTree.Insert(new Position(x, y), i.ToString());
        }

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = quadTree.QueryRange(new Position(100, 100), 20.0);
        stopwatch.Stop();

        // Assert - Query should be fast (under 10ms for 1000 items)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10);
        results.Should().NotBeEmpty();
    }

    [Fact]
    public void Insert_ForcesSubdivision_MaintainsCorrectStructure()
    {
        // Arrange
        var quadTree = new QuadTree<string>(100, 100, maxDepth: 8, maxItems: 4);

        // Insert more than maxItems in same area to force subdivision
        for (int i = 0; i < 10; i++)
        {
            quadTree.Insert(new Position(50, 50 + i), i.ToString());
        }

        // Act
        var results = quadTree.QueryRange(new Position(50, 52), 5.0);

        // Assert - Should still find items correctly after subdivision
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Item2 == "0");
        results.Should().Contain(r => r.Item2 == "2");
        results.Should().Contain(r => r.Item2 == "4");
    }
}
