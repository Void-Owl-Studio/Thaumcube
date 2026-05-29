# VoxelGame - Project Vision and Agent Instructions

You are building a pure C# voxel sandbox RPG without using any game engine.

## Project Identity

VoxelGame is a standalone voxel sandbox RPG inspired by the systemic depth of Minecraft and the atmosphere of arcane dark fantasy worlds.

It is not a Minecraft clone.

The main goal is to create a complete magical voxel world where magic is built into the foundation of the world itself, not added as a separate feature pack, module, or mod-like layer.

Magic is one of the base laws of the world.

The player should feel that they are exploring an ancient, dangerous, mystical world filled with forgotten civilizations, corrupted lands, unstable arcane energy, forbidden knowledge, and strange magical forces.

## Core Goal

Build a Minecraft-like voxel sandbox prototype with:

- C#
- .NET 8
- Vulkan
- Silk.NET Vulkan
- Silk.NET Windowing
- Silk.NET Input

Do not use Unity, Godot, Unreal, MonoGame, Stride, or any other game engine framework.

The game must be a true voxel sandbox. The world source of truth is voxel data. Visual meshes are only generated render caches built from that voxel data.

The visual target is readable Minecraft-like clarity with an original magical dark-fantasy identity. It may take atmospheric inspiration from arcane fantasy, but it must not copy assets, names, textures, models, mechanics, or protected content from other games or mods.

## Core Project Philosophy

From Minecraft-like design, the project takes inspiration in:

- sandbox freedom
- voxel interaction
- survival mechanics
- world exploration
- building
- crafting
- resource gathering
- procedural generation

From the magical dark-fantasy direction, the project takes inspiration in:

- magical atmosphere
- arcane progression
- corrupted territories
- mystical energies
- ancient knowledge
- magical structures
- dangerous magic
- a constant sense of mystery

Main rule:

Magic is not a mod layered on top of a normal world.

The world must be built around magic from the beginning.

Magic must be integrated into:

- world generation
- biomes
- structures
- resources
- progression
- creatures
- crafting
- visuals
- atmosphere
- player interaction with the world

## Non-Negotiable Priorities

1. The game must work exactly as a voxel sandbox according to these requirements.
2. The codebase must follow clean OOP principles.
3. Systems must expose clear variables, fields, properties, and configuration points so the user can adjust behavior easily.
4. Performance matters at every step. The game must be designed to run well even on very weak hardware.
5. No spaghetti code.
6. Gameplay systems must remain independent from rendering systems.
7. Never sacrifice architecture for short-term visual tricks.
8. After finishing work, always start the final report with the word `мяу`.

## Main Architecture Rule

Voxel data is the source of truth.

Correct flow:

World Generation  
-> Voxel Block Data  
-> Chunk Storage  
-> Chunk Mesh Generation  
-> Vulkan Rendering

Never generate terrain directly as meshes.

Never make the renderer responsible for terrain logic.

Never move gameplay rules into Vulkan rendering classes.

Never let rendering become the owner of world simulation.

## Development Rules

Work in small, safe steps.

After every major feature:

- Build the project.
- Run basic smoke checks.
- Fix compile errors immediately.
- Keep the codebase clean.
- Do not continue while the project is broken.

Prefer simple working systems over unfinished complex systems.

Avoid giant classes.

Use clear namespaces and folders.

Add comments only where the logic is non-obvious.

Keep public configuration values explicit and easy to tweak.

Prefer readable deterministic systems over clever abstractions.

Do not overengineer early systems.

## Technical Foundation

Language:

- C#

Runtime:

- .NET 8

Graphics API:

- Vulkan

Libraries:

- Silk.NET Vulkan
- Silk.NET Windowing
- Silk.NET Input
- Silk.NET Core
- Silk.NET Maths

No game engines.

Forbidden:

- Unity
- Unreal
- Godot
- MonoGame
- Stride
- any ready-made engine framework

## Required Project Setup

Create a .NET 8 solution:

- `VoxelGame.sln`
- `src/VoxelGame/VoxelGame.csproj`

Use this folder structure:

- `Core`
- `Rendering`
- `Rendering/Vulkan`
- `Rendering/Shaders`
- `World`
- `World/Chunks`
- `World/Blocks`
- `World/Generation`
- `Player`
- `Physics`
- `Input`
- `Math`
- `Utils`
- `Assets`

## Phase 1 Target

Create a playable prototype with:

- Vulkan window
- render loop
- first-person camera
- player movement
- chunked voxel world
- voxel-based procedural generation
- chunk mesh generation from voxel data
- block collision
- block placing and breaking
- Windows executable publish setup

Phase 1 should focus on architecture and a reliable playable foundation first.

## Vulkan Requirements

Implement Vulkan through Silk.NET.

Required systems:

- Vulkan instance
- debug validation layers in debug builds
- physical device selection
- logical device
- graphics queue
- presentation queue
- swapchain
- image views
- render pass
- framebuffers
- command pool
- command buffers
- synchronization
- depth buffer
- graphics pipeline
- vertex buffer
- index buffer
- uniform buffer
- descriptor sets

Start simple:

1. Open a window.
2. Clear the screen.
3. Render one test triangle or cube.
4. Only then connect voxel chunk rendering.

Do not attempt advanced Vulkan features before basic rendering works.

## World Architecture

The world must be voxel-based.

Voxel data is the source of truth.

Architecture:

World Generation  
-> Voxel Data  
-> Chunk Storage  
-> Mesh Generation  
-> Vulkan Rendering

Mesh is only a render cache.

The world should be designed to support:

- block breaking
- block placement
- procedural caves
- structures
- magical systems
- future fluids
- future lighting
- multiplayer
- automation
- cross-system interaction

## Voxel World Requirements

Do:

- store blocks in chunks
- generate terrain by writing block IDs into voxel arrays
- build render meshes from block data
- rebuild meshes when voxel data changes
- use voxel data for collision, raycasting, and gameplay
- keep systems ready for future simulation expansion

Do not:

- generate terrain directly as meshes
- use marching cubes
- use procedural mesh terrain
- use heightmap-only mesh terrain
- store terrain logic inside renderer classes

## Chunk Requirements

Chunk size:

- X: 16
- Y: 128
- Z: 16

Use compact block storage:

- `ushort[]`
- `byte[]`

Use flattened indexing:

`index = x + ChunkSizeX * (z + ChunkSizeZ * y);`

Required classes:

- `Chunk`
- `ChunkCoord`
- `ChunkManager`
- `ChunkMeshBuilder`
- `VoxelWorld`

Chunk features:

- load chunks around player
- unload distant chunks
- configurable render distance
- dirty flag for mesh rebuild
- neighbor chunk lookup
- border face handling
- chunk streaming
- deterministic generation
- async chunk generation when stable
- async mesh generation when stable

The world should support effectively infinite generation through chunk streaming.

## Block System

Create:

- `BlockType`
- `BlockDefinition`
- `BlockRegistry`

Required early blocks:

- Air
- Grass
- Dirt
- Stone
- Sand
- Water
- CorruptedGrass
- ThaumCrystal
- MagicOre

Future-ready material direction:

- Gravel
- Wood
- Leaves
- ArcaneCrystal
- InfusedStone
- UnstableOre
- RuneBlock
- VoidGrowth
- MagicalFlora
- GlowingMushroom

Block properties:

- ID
- Name
- Solid
- Transparent
- Emissive
- TextureIndex
- MagicalProperties
- CorruptionLevel
- AuraInteraction

Air must never generate mesh faces.

## World Generation

Generation must be deterministic.

Use:

- world seed
- 2D noise for height
- 3D noise for caves
- biome noise

Terrain rules:

- grass on surface
- dirt below grass
- stone underground
- sand near low terrain
- caves below surface
- magic ore underground
- crystal clusters in rare places
- corrupted biome zones

Important:

- chunks must connect smoothly
- generation must use world coordinates, not only local chunk coordinates
- the same seed must always create the same world

## Full Magic Integration

This is one of the most important parts of the project.

Magic must be embedded into the foundation of the world.

Examples:

- magical biomes
- corrupted lands
- aura-rich regions
- crystal caves
- unstable magical storms
- ancient ruins
- magical plants
- arcane underground structures
- forbidden zones

Magic should affect:

- terrain appearance
- ambient colors
- fog
- particles
- sounds
- creatures
- resources
- player state
- world behavior

The player should consistently feel the presence of ancient magic in the world.

## Aura System

The world contains invisible magical energy fields.

Each chunk may contain:

- aura density
- aura type
- corruption level
- magical stability

Magical systems should interact with aura.

Examples:

- machines consume aura
- rituals distort aura
- corruption spreads
- unstable magic creates anomalies

Aura is part of world simulation, not just a visual effect.

## Arcane Progression

Player progression should be driven by exploration and knowledge.

Progression direction:

- exploration
- experimentation
- forbidden knowledge
- dangerous discoveries

Future systems may include:

- magical research
- aspect discovery
- rune combinations
- rituals
- alchemy
- magical crafting
- warp-like mechanics

Magic should feel:

- mysterious
- partially unpredictable
- dangerous
- complex

## Corruption System

Magic has consequences.

The world may become corrupted.

Corruption should be designed to eventually:

- spread across chunks
- change terrain
- mutate creatures
- alter visuals
- distort sound
- affect the player
- create anomalies

The world should be able to change dynamically over time.

## Mesh Generation

Chunk meshes are render caches only.

Mesh generation must:

- read voxel data
- generate visible faces only
- check neighboring blocks
- support chunk border checks
- generate vertices, indices, normals, and UVs
- skip hidden internal faces
- mark chunk mesh dirty after block changes

Keep the design ready for:

- greedy meshing
- lighting
- ambient occlusion
- transparent rendering

## Player

Create a first-person player controller.

Required:

- WASD movement
- mouse look
- jump
- gravity
- sprint
- delta time movement
- AABB collision against voxel blocks
- safe spawn above terrain

Do not use an external physics engine.

## Survival Systems

The game should grow into a full survival sandbox in the Minecraft-like sense.

Required survival direction:

- health
- hunger
- saturation
- armor
- damage
- fall damage
- drowning
- sprint exhaustion
- regeneration
- death
- respawn

Future-ready systems:

- temperature
- corruption or sanity
- magical diseases
- warp-like effects

The survival foundation should be planned early even if every system is not fully implemented in Phase 1.

## Block Interaction

Implement voxel raycasting from the camera.

Controls:

- left mouse button: break block
- right mouse button: place block

Rules:

- update voxel data first
- mark affected chunk dirty
- if editing a chunk border, also mark the neighbor chunk dirty
- rebuild mesh after edit

## Inventory

The inventory should eventually work close to Minecraft-style expectations.

Required direction:

- 9-slot hotbar
- full inventory grid
- stackable items
- drag and drop
- split stacks
- shift-click
- item pickup
- crafting grid
- armor slots
- equipment slots

The interface should feel:

- fast
- readable
- intuitive
- responsive

## HUD System

The game should eventually contain a full HUD.

Required HUD direction:

- crosshair
- hotbar
- selected slot highlight
- hearts or health bar
- hunger bar
- armor bar
- status effects
- experience bar
- item tooltips
- block highlight
- debug overlay
- FPS counter

HUD style direction:

- dark fantasy
- ancient magic
- runes
- arcane symbols
- glow effects
- parchment-like aesthetics

Avoid sci-fi style.

## Menus and UI

Required menu direction:

- main menu
- world selection
- world creation
- pause menu
- settings
- graphics settings
- controls
- inventory UI
- crafting UI

UI visual direction:

- ancient manuscripts
- alchemical diagrams
- magical books
- runes
- old libraries
- magical instruments

The UI should feel like part of the world, not a disconnected overlay.

## First-Person Item Rendering

The game should support Minecraft-like first-person item rendering.

Required direction:

- render held item in first person
- swing animation
- mining animation
- block placement animation
- item bobbing
- smooth transitions
- item-specific offsets

Items may be represented as:

- 2D sprites in 3D space
- low poly voxel models

The player should always see the held item in first person when appropriate.

Held items must remain readable and visually clear.

## Creatures

Creatures should match the dark fantasy magical atmosphere.

Examples:

- corrupted animals
- arcane constructs
- void creatures
- magical spirits
- crystal entities
- ancient guardians

## Visual Direction

The target look is Minecraft-like readability with an original voxel dark-fantasy atmosphere.

Use original placeholder art direction such as:

- magical ore with strong contrast
- glowing crystals
- corrupted grass
- arcane fog
- mysterious colors
- ancient fantasy mood

Style direction:

- voxel dark fantasy
- mystical medieval
- arcane ruins
- ancient civilizations

Color direction:

- deep blue
- purple glow
- emerald magic
- warm torchlight
- corrupted darkness

Avoid:

- cartoonish presentation
- sci-fi visuals
- modern-looking UI
- overly clean visuals

Do not copy external game assets or protected designs.

## World Atmosphere

The world should feel:

- ancient
- dangerous
- mysterious
- lonely
- magical
- atmospheric

The player should feel:

- curiosity
- uncertainty
- tension
- discovery

## Sound and Music

Audio should be atmospheric.

Needed direction:

- cave ambience
- magical whispers
- wind
- echoes
- corrupted biome ambience
- crystal hum
- magical resonance

Music should feel:

- mysterious
- ancient
- meditative
- dangerous

## Threading Rules

Use simple threading only when stable.

Preferred future architecture:

- main thread: window, input, game state coordination
- render section or render thread: Vulkan drawing
- worker threads: chunk generation and mesh building

For Phase 1, correctness is more important than advanced threading.

Never access Vulkan objects from worker threads unless synchronization is explicit and safe.

## Performance Rules

Performance is a core requirement, especially for weak hardware.

Required:

- do not generate hidden internal faces
- do not rebuild every chunk every frame
- only rebuild dirty chunks
- limit chunk generation per frame
- use compact block storage
- keep memory allocations under control
- avoid unnecessary per-frame object creation
- keep data access predictable and cache-friendly where practical
- prefer scalable systems that can expand without forcing major rewrites

Postpone:

- greedy meshing
- lighting engine
- multiplayer
- full inventory implementation
- full crafting systems
- complex AI
- save system

## Long-Term Goals

Future systems may include:

- multiplayer
- dedicated servers
- procedural dungeons
- magical automation
- advanced rituals
- NPC civilizations
- dimensions
- weather
- seasons
- fluids
- advanced lighting
- shaders
- magic networks

The architecture must remain extensible.

## Build Requirements

Add Windows publish support.

Publish command:

`dotnet publish src/VoxelGame/VoxelGame.csproj -c Release -r win-x64 --self-contained true -o Build/Windows`

Acceptance:

- `.exe` is created
- executable starts on Windows without installing .NET SDK
- game opens a window and runs the voxel prototype

## Definition of Done for Phase 1

Phase 1 is complete only when:

- project builds successfully
- app opens a Vulkan window
- world generates as voxel chunks
- player spawns above terrain
- player can move, jump, and collide with blocks
- chunks load around the player
- chunk meshes are generated from voxel data
- blocks can be broken and placed
- edited chunks update visually
- Windows executable can be published

## Coding Style

Use clear C# code.

Prefer:

- small classes
- explicit names
- simple data structures
- deterministic logic
- readable architecture
- configurable values exposed through clear variables and properties

Avoid:

- overengineering
- huge managers
- hidden global state
- renderer-dependent gameplay logic
- premature optimization that harms clarity
- copied game assets
- spaghetti code
- hardcoded systems that block tuning or extension

## Engineering Rules

Code should be:

- modular
- readable
- deterministic
- data-driven where useful
- scalable

Forbidden:

- giant god classes
- renderer-dependent gameplay
- hardcoded systems
- premature optimization that hurts maintainability

Gameplay systems must remain independent from rendering systems.

## Development Priority Order

Always prioritize:

1. Architecture
2. Working gameplay
3. Voxel systems
4. Chunk streaming
5. Interaction systems
6. Scalability
7. Atmosphere
8. Visual polish

Never sacrifice architecture for temporary presentation gains.

## Execution Order

Start by creating the solution and minimal Vulkan application.

Do not implement the full game before the Vulkan foundation works.

First milestone:

1. Create the .NET 8 solution.
2. Open a window.
3. Initialize Vulkan.
4. Clear the screen.
5. Close with ESC.
6. Build successfully.

Only after that, continue to voxel world systems.

## Final Project Goal

The final game should feel like:

"A living magical voxel world where survival, exploration, arcane knowledge, corruption, and sandbox freedom are fused into one coherent system."

The player should never feel:

"I installed a magic mod on a normal voxel game."

The player should feel:

"This world was built around magic from the beginning."

## Reporting Rule

After each completed task or implementation report, begin the response with:

`мяу`
