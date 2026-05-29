# VoxelGame

VoxelGame is a pure C#/.NET voxel sandbox RPG prototype foundation using Silk.NET and Vulkan-facing architecture. The project is structured so voxel data is the source of truth:

```text
World Generation -> Voxel Block Data -> Chunk Storage -> Chunk Mesh Generation -> Vulkan Rendering
```

## Current Phase 1 Skeleton

- .NET project and solution layout.
- Silk.NET Windowing/Input/Vulkan package setup.
- Real Vulkan instance/device/swapchain/render pass/framebuffer/command buffer/sync clear loop.
- Prototype Vulkan UI rectangles for atmosphere, crosshair, hotbar, selected slot, and held item placeholder.
- Voxel preview rendering from real `ChunkRenderMesh` data through a temporary Vulkan immediate pass, so generated terrain is visible before the full shader pipeline lands.
- First-person player controller foundation with WASD, mouse look, sprint, jump, gravity, and AABB voxel collision.
- Voxel block registry with Air, Grass, Dirt, Stone, Sand, Water, CorruptedGrass, ArcaneCrystal, and MagicOre.
- Chunk storage using `ushort[]` and flattened indexing.
- Deterministic world generation by seed, with corruption fields, magic ore, and rare arcane crystal formations.
- Chunk mesh cache generated from voxel data with hidden face culling and border checks through world lookups.
- Block raycasting, breaking, placing, dirty chunk rebuilds, and neighbor dirty marking on borders.
- Basic hotbar/HUD state and selected block switching.
- Vulkan renderer boundary prepared for swapchain/pipeline implementation.

## Commands

```powershell
.\run.bat
.\build.bat
.\publish.bat
```

`run.bat` publishes a self-contained Windows build first, then starts `Build/Windows/VoxelGame.exe`. This keeps the project runnable on machines where the .NET 8 runtime is not installed globally.

Fast launch after a publish:

```powershell
.\run.bat -NoBuild
```

Auto-close launch check:

```powershell
.\run.bat -NoBuild --auto-close-ms 1000
```

Headless smoke check:

```powershell
dotnet run --project src/VoxelGame/VoxelGame.csproj -- --smoke-test
```

Publish output:

```text
Build/Windows
```

The publish script uses:

```powershell
dotnet publish src/VoxelGame/VoxelGame.csproj -c Release -r win-x64 --self-contained true -o Build/Windows
```

## Asset Note

The root `Assets` folder is copied into build output for future resource work. The current skeleton uses original procedural block colors and does not depend on external game assets.

## Controls

- `WASD`: move
- `Mouse`: look
- `Space`: jump
- `Left Shift`: sprint
- `1-9`: select hotbar slot
- `Left Mouse`: break targeted block
- `Right Mouse`: place selected block
- `Esc`: close

## Next Renderer Milestone

The gameplay/world foundation is deliberately independent from rendering. The current view already projects chunk mesh faces into the Vulkan frame. The next Vulkan step is to replace that temporary preview with the real GPU mesh path:

- Vertex/index/uniform buffers.
- Descriptor sets and graphics pipeline using the included shader sources.
- Uploading `ChunkRenderMesh` data into GPU buffers and drawing the generated voxel chunks.
- Depth buffer and camera matrices for the first true 3D world view.
