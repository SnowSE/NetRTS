# ADR 0001: Spatial Indexing for Fog of War and Collision Detection

## Status
Accepted

## Context
The game engine needs to perform frequent spatial queries, such as:
- Calculating visibility (Fog of War) for units and buildings.
- Finding units within range for combat.
- Checking for collisions during movement and building placement.

With up to 100 concurrent matches and hundreds of entities per match, a naive O(N^2) approach for distance checks would be too slow to maintain a 1-second tick interval.

## Decision
We will use a **QuadTree** data structure for spatial indexing.

A QuadTree is a tree data structure in which each internal node has exactly four children. It is used to partition a two-dimensional space by recursively subdividing it into four quadrants or regions.

## Alternatives Considered
1. **Naive O(N^2)**: Too slow for the required scale.
2. **Grid-based Partitioning**: Simple to implement but less efficient for unevenly distributed entities.
3. **R-Tree**: Good for range queries but more complex to implement and maintain than a QuadTree for point-based entities.

## Consequences
- **Pros**:
    - Reduces query complexity from O(N) to O(log N) on average.
    - Efficiently handles clusters of units and sparse regions.
    - Well-suited for 2D game worlds.
- **Cons**:
    - Requires rebuilding or updating the tree every tick as units move.
    - Slightly more memory overhead than a simple list or grid.
