# Feature Specification: RTS Game Engine for Programming Competition

**Feature Branch**: `001-rts-game-engine`
**Created**: 2025-12-09
**Status**: Draft
**Input**: User description: "I want you to build a game engine for a programming competition. The game should be a real time strategy game, and each user will make REST calls to manage a queue of actions to be completed. The actions will all be executed on a clock cycle. There will be endpoints to share the current game state (at least what's visible in the fog of war), and endpoints to tell your units and buildings what to work on or what to produce. The game needs to keep track of some type of score to make matches have clear winners. We don't want the game to be _too_ complicated, but it should have a few different types of units and a few different types of buildings that do different things with different upgrades. Commands do not have to be hyper-specific, they can be kind of general (e.g. units 5-10 attack building 334, or 'workers 1-7 mine ore', or 'workers 4-10 build a barracks at tile 392')."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Bot Connects and Views Initial Game State (Priority: P1)

A competitor's bot program connects to a new game match and retrieves the initial game state to understand their starting position, visible map area, and available resources.

**Why this priority**: This is the foundational capability required for any bot to participate in a match. Without being able to see the game state, no other actions are possible. This forms the minimum viable game engine that can host matches.

**Independent Test**: Can be fully tested by starting a new match, making a REST call to retrieve game state, and verifying the response contains player starting units, visible map tiles, initial resources, and fog of war boundaries. Success means bots can "see" the game world.

**Acceptance Scenarios**:

1. **Given** a new match has started with two players, **When** player 1's bot requests current game state, **Then** the response includes their starting units, resource amounts, visible map tiles within vision range, and indicators of fog of war
2. **Given** player 1 has units at position (10,10) with vision range 5, **When** they request game state, **Then** they see all tiles and entities within 5 tiles of (10,10) but not tiles at distance 6 or greater
3. **Given** player 2's units are outside player 1's vision range, **When** player 1 requests game state, **Then** player 2's units are not included in the response (hidden by fog of war)
4. **Given** a match is in progress, **When** a bot requests game state with invalid authentication, **Then** the system rejects the request with appropriate error message

---

### User Story 2 - Bot Queues Basic Unit Commands (Priority: P2)

A competitor's bot issues commands to control their units (move, attack, gather resources) by queuing actions that will execute on the next game tick.

**Why this priority**: Command execution is the core interaction mechanism. Once bots can see the game state, they need to control their units. This enables basic gameplay and strategic decision-making.

**Independent Test**: Start a match, retrieve game state to identify unit IDs, queue movement and resource gathering commands via REST API, wait for next game tick, then retrieve updated game state to verify units moved to commanded positions and began gathering resources.

**Acceptance Scenarios**:

1. **Given** player 1 has worker units 1-5 at position (10,10) and ore deposits at (15,10), **When** they queue command "workers 1-5 mine ore at (15,10)", **Then** on next game tick, workers move toward (15,10) and begin mining when they arrive
2. **Given** player 1 has combat units 10-15 and player 2 has a visible building at position (50,50), **When** player 1 queues command "units 10-15 attack building at (50,50)", **Then** on next game tick, units move toward target and attack when in range
3. **Given** player 1 queues multiple commands for the same unit in one tick, **When** the game tick executes, **Then** only the most recently queued command applies (later commands override earlier ones)
4. **Given** player 1 tries to command unit IDs they don't own, **When** the command is processed, **Then** the system rejects the command and returns an error
5. **Given** player 1 queues a command to move units to coordinates outside the map boundary, **When** the command is processed, **Then** the system rejects the command with a validation error

---

### User Story 3 - Bot Manages Buildings and Production (Priority: P3)

A competitor's bot constructs buildings at specified locations and queues production of new units from those buildings.

**Why this priority**: Building construction and unit production enable strategic depth and economic gameplay. This allows bots to expand their capabilities beyond their starting units.

**Independent Test**: Start with workers and resources, queue a building construction command at a valid tile, wait for construction to complete over multiple ticks, then queue unit production from that building and verify new units spawn.

**Acceptance Scenarios**:

1. **Given** player 1 has workers 1-7 at position (20,20) with sufficient resources, **When** they queue command "workers 1-7 build barracks at tile (22,22)", **Then** workers move to (22,22), begin construction, and after N ticks the barracks becomes operational
2. **Given** player 1 has an operational barracks building, **When** they queue command "barracks 101 produce soldier", **Then** if sufficient resources are available, soldier production begins and completes after M ticks
3. **Given** player 1 has a barracks producing a soldier, **When** production completes, **Then** a new soldier unit spawns adjacent to the barracks and appears in the next game state response
4. **Given** player 1 tries to build on a tile already occupied by another building or unit, **When** the build command is processed, **Then** the system rejects the command with collision error
5. **Given** player 1 tries to produce a unit without sufficient resources, **When** the production command is processed, **Then** the system rejects the command and returns insufficient resources error

---

### User Story 4 - Bot Researches Upgrades (Priority: P4)

A competitor's bot invests resources into upgrades that enhance unit or building capabilities, providing strategic advantages.

**Why this priority**: Upgrades add strategic depth and create decision points between immediate unit production and long-term capability improvements. This is enhancement beyond core gameplay.

**Independent Test**: With an operational building capable of research, queue an upgrade command, wait for completion, then verify upgraded units have improved stats when queried in game state.

**Acceptance Scenarios**:

1. **Given** player 1 has a tech building and sufficient resources, **When** they queue command "building 200 research weapon-upgrade-1", **Then** upgrade begins and completes after N ticks
2. **Given** player 1 has completed weapon-upgrade-1, **When** they produce new combat units, **Then** those units have increased attack damage in game state
3. **Given** an upgrade is in progress, **When** queried in game state, **Then** the response shows upgrade name and completion percentage
4. **Given** player 1 tries to research an upgrade without meeting prerequisites, **When** the research command is processed, **Then** the system rejects the command with prerequisite error

---

### User Story 5 - Match Concludes with Score and Winner (Priority: P5)

A match runs until victory conditions are met, at which point the system determines the winner based on scoring rules and provides final match results.

**Why this priority**: Scoring and victory conditions make matches competitive and conclusive. While essential for competitions, the core engine can function without this for testing earlier stories.

**Independent Test**: Run a match to completion (either by destruction of all opponent units/buildings or by time limit), then query match results endpoint to verify winner is declared with score breakdown.

**Acceptance Scenarios**:

1. **Given** player 1 destroys all of player 2's units and buildings, **When** the game tick processes this state, **Then** match ends and player 1 is declared winner with score based on destruction points
2. **Given** match time limit is reached, **When** final tick executes, **Then** player with higher score (based on units alive, buildings operational, resources gathered) wins
3. **Given** a match has concluded, **When** either player queries game state, **Then** response includes match status "completed", winner ID, final scores, and match duration
4. **Given** match is concluded, **When** players attempt to queue new commands, **Then** system rejects commands indicating match is finished

---

### Edge Cases

- What happens when two players queue commands for their units to occupy the same tile on the same tick? System should resolve collision using defined priority rules (e.g., existing occupant maintains position, or first-submitted command wins).
- How does the system handle a bot that fails to respond or queue commands for multiple consecutive ticks? Bot should remain in match with no action taken for that tick; units maintain previous orders or idle.
- What happens when a unit is given a movement command but is then destroyed before completing the movement? Unit is removed from game state immediately upon destruction; queued commands are discarded.
- How does system handle simultaneous attacks where units destroy each other in the same tick? Both units are destroyed simultaneously; combat resolution applies damage in parallel, not sequentially.
- What happens when a player queues a command for a building that is under construction (not yet operational)? Command is rejected with error indicating building not yet operational.
- How does fog of war update when units move? On each tick, after unit positions update, visibility recalculates and next game state request reflects new vision.
- What happens when a player tries to queue more actions than the allowed limit per tick? Players can queue commands up to a configurable maximum queue size (e.g., 500 commands total). Each tick processes up to a configurable number of commands from the front of the queue (e.g., first 100 commands). Unprocessed commands remain in queue for subsequent ticks. If player attempts to queue beyond maximum queue size, system rejects new commands with queue-full error.
- How does resource collection work when multiple players' workers target the same resource deposit? First-come-first-served or proportional gathering; deposit has total capacity that depletes.

## Requirements *(mandatory)*

### Functional Requirements

#### Match Management

- **FR-001**: System MUST support creating new game matches between two players with unique match IDs
- **FR-002**: System MUST initialize each match with a grid-based map of defined dimensions (e.g., 100x100 tiles)
- **FR-003**: System MUST provide starting positions for each player's base and initial units (balanced and symmetrical or mirrored)
- **FR-004**: System MUST assign each player an initial resource amount at match start
- **FR-005**: System MUST execute game ticks at a defined interval (e.g., every 1 second), processing all queued commands synchronously
- **FR-006**: System MUST track match status (active, completed) and match duration in ticks

#### Game State Visibility

- **FR-007**: System MUST provide a REST endpoint to retrieve current game state for a specific player
- **FR-008**: Game state MUST include player's own units with positions, types, health, and status
- **FR-009**: Game state MUST include player's own buildings with positions, types, health, construction status, and production queues
- **FR-010**: Game state MUST include current resource amounts for the requesting player
- **FR-011**: Game state MUST include visible map tiles within vision range of player's units and buildings
- **FR-012**: System MUST implement fog of war such that enemy units and buildings outside vision range are not included in game state
- **FR-013**: Game state MUST include visible enemy units and buildings that are within vision range
- **FR-014**: Game state MUST include current game tick number
- **FR-015**: Game state MUST include resource deposit locations that are visible to the player

#### Unit Commands

- **FR-016**: System MUST provide a REST endpoint to queue unit commands (move, attack, gather resources)
- **FR-017**: System MUST accept range-based unit selection (e.g., "units 5-10" or "workers 1-7")
- **FR-018**: System MUST validate that commanded units belong to the requesting player
- **FR-019**: System MUST support move commands specifying target coordinates
- **FR-020**: System MUST support attack commands specifying target unit or building ID
- **FR-021**: System MUST support resource gathering commands specifying resource deposit location
- **FR-022**: System MUST allow multiple commands to be queued in a single API call
- **FR-023**: System MUST maintain a persistent command queue per player that carries over between ticks
- **FR-024**: System MUST enforce a configurable maximum command queue size per player (e.g., 500 commands)
- **FR-025**: System MUST reject new command submissions with queue-full error when player's queue is at maximum capacity
- **FR-026**: System MUST validate command parameters (e.g., coordinates within map bounds, target exists) before adding to queue
- **FR-027**: Each game tick MUST process up to a configurable number of commands from the front of each player's queue (e.g., 100 commands per tick)
- **FR-028**: Unprocessed commands MUST remain in queue and be processed in subsequent ticks in FIFO order
- **FR-029**: When multiple queued commands target the same unit, the most recent applicable command MUST override previous commands for that unit
- **FR-030**: Game state response MUST include current command queue size for the requesting player

#### Building Commands

- **FR-031**: System MUST provide a REST endpoint to queue building construction commands
- **FR-032**: System MUST accept worker unit selection and target tile coordinates for construction
- **FR-033**: System MUST validate that target tile is unoccupied and within map bounds
- **FR-034**: System MUST deduct building cost from player resources when construction begins
- **FR-035**: Buildings MUST require N ticks to construct (construction time varies by building type)
- **FR-036**: Buildings under construction MUST show construction progress percentage in game state
- **FR-037**: System MUST provide a REST endpoint to queue unit production commands from operational buildings
- **FR-038**: System MUST validate that building is operational before accepting production commands
- **FR-039**: System MUST deduct unit production cost from player resources when production begins
- **FR-040**: Unit production MUST require M ticks to complete (production time varies by unit type)
- **FR-041**: Newly produced units MUST spawn adjacent to the producing building
- **FR-042**: System MUST support production queues where multiple units can be queued sequentially

#### Unit Types and Building Types

- **FR-043**: System MUST support at least three unit types: Worker (gathers resources, constructs buildings), Soldier (basic combat), and Scout (fast movement, extended vision range)
- **FR-044**: System MUST support at least three building types: Command Center (main base, produces workers), Barracks (produces combat units), and Resource Depot (increases resource storage capacity)
- **FR-045**: Each unit type MUST have distinct attributes: health points, movement speed, attack damage, attack range, vision range, and resource cost
- **FR-046**: Each building type MUST have distinct attributes: health points, construction time, production capabilities, and resource cost

#### Combat and Damage

- **FR-047**: System MUST process attack commands when attacker is within range of target
- **FR-048**: System MUST apply damage to target unit or building based on attacker's damage value
- **FR-049**: System MUST remove units from game when health reaches zero
- **FR-050**: System MUST remove buildings from game when health reaches zero
- **FR-051**: Destroyed buildings MUST clear their tile for future construction

#### Resource Management

- **FR-052**: System MUST place resource deposits (e.g., ore, crystals) at defined map locations
- **FR-053**: Resource deposits MUST have finite capacity that depletes as gathered
- **FR-054**: Workers gathering resources MUST automatically transfer collected resources to player's total at defined intervals
- **FR-055**: System MUST prevent actions that would result in negative resource balance

#### Upgrades

- **FR-056**: System MUST support at least two upgrade types per category (e.g., weapon damage upgrades, armor upgrades, speed upgrades)
- **FR-057**: System MUST provide a REST endpoint to queue research commands from appropriate buildings
- **FR-058**: Upgrades MUST require resource cost and completion time in ticks
- **FR-059**: Completed upgrades MUST apply effects to all current and future units of applicable types
- **FR-060**: System MUST enforce upgrade prerequisites (e.g., upgrade tier 2 requires tier 1 completion)

#### Scoring and Victory

- **FR-061**: System MUST track score for each player based on: units destroyed, buildings destroyed, resources gathered, and units/buildings remaining
- **FR-062**: System MUST end match when one player's Command Center is destroyed (elimination victory)
- **FR-063**: System MUST end match when maximum match duration is reached (time limit victory)
- **FR-064**: On match conclusion, system MUST determine winner based on highest score if both players still have units/buildings
- **FR-065**: System MUST provide a REST endpoint to retrieve match results including winner, final scores, and match statistics

#### API and Authentication

- **FR-066**: System MUST authenticate API requests using player tokens or match-specific credentials
- **FR-067**: System MUST reject commands from players not participating in the requested match
- **FR-068**: System MUST return appropriate error codes and messages for invalid requests
- **FR-069**: System MUST support concurrent matches without interference

### Assumptions

- **Map Terrain**: Assuming uniform passable terrain with no obstacles initially. Future versions may include impassable tiles or terrain effects.
- **Tick Rate**: Assuming 1-second tick interval provides good balance between real-time feel and bot response time. This can be configured.
- **Maximum Match Duration**: Assuming 1800 ticks (30 minutes at 1-second intervals) for time-limit victory.
- **Vision Range**: Assuming standard vision range of 5 tiles for most units, 8 tiles for scouts, 6 tiles for buildings.
- **Starting Resources**: Assuming each player starts with 500 resource units.
- **Command Queue System**: Assuming maximum queue size of 500 commands per player with 100 commands processed per tick. These values are configurable to balance strategic planning depth with system performance. Queue persists between ticks allowing multi-tick strategic planning.
- **Resource Types**: Assuming single unified resource type (e.g., "ore") rather than multiple resource types, to keep game complexity moderate.
- **Unit Capacity**: Assuming no hard cap on total units per player, but resource constraints naturally limit growth.
- **Simultaneous Actions**: Combat and movement resolve simultaneously each tick, not sequentially.

### Key Entities

- **Match**: Represents a single game instance between two players. Attributes include match ID, player IDs, current tick number, match status, map dimensions, and start time.

- **Player**: Represents a competitor in a match. Attributes include player ID, authentication token, current resource amount, score components, and list of controlled units and buildings.

- **Unit**: Represents a controllable game entity. Attributes include unit ID, owner player ID, unit type, position (x, y coordinates), health points, current command/status, and type-specific stats (movement speed, attack damage, attack range, vision range).

- **Building**: Represents a structure owned by a player. Attributes include building ID, owner player ID, building type, position, health points, construction status, production queue, and type-specific capabilities.

- **Command**: Represents a queued action to be executed on next tick. Attributes include command type (move/attack/gather/build/produce/research), target unit/building IDs, target coordinates, and tick when queued.

- **Resource Deposit**: Represents a gatherable resource location. Attributes include deposit ID, position, resource type, remaining capacity, and visibility.

- **Upgrade**: Represents a research improvement. Attributes include upgrade ID, upgrade type, owner player ID, research progress, completion status, and effects applied.

- **Map Tile**: Represents a location on the game grid. Attributes include coordinates (x, y), occupancy status, visibility per player (fog of war), and terrain type.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two competing bots can successfully connect to a new match and retrieve initial game state within 2 seconds of match creation

- **SC-002**: Bots can queue commands and observe their execution results in game state within one tick cycle (demonstrating command -> execution -> state update loop)

- **SC-003**: Fog of war correctly hides enemy positions, verified by one bot unable to see opponent units outside vision range in 100% of test cases

- **SC-004**: A complete match from start to victory condition (elimination or time limit) completes successfully with winner determined in under 30 minutes

- **SC-005**: System can host 10 concurrent matches without performance degradation (all ticks process within 1 second)

- **SC-006**: 95% of valid command submissions receive acknowledgment response within 100 milliseconds

- **SC-007**: Bot participants can implement basic strategies (resource gathering, unit production, attack coordination) using provided API endpoints, demonstrated by at least 3 working sample bots

- **SC-008**: Match results provide clear winner with score breakdown showing points for destruction, economy, and survival categories

- **SC-009**: Invalid commands (targeting non-owned units, out-of-bounds coordinates, insufficient resources) are rejected with informative error messages in 100% of cases

- **SC-010**: Unit pathfinding and command execution feels responsive, with units beginning commanded actions within 1 tick (1 second) of command submission
