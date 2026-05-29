# Prototype declarations

Content can be declared in `.tcproto` files with a DM-like path and predefined vars:

```text
/block/dirt
numeric_id = 2
name = "Dirt"
desc = "Regular dirt."
block_texture = "/textures/block/dirt.png"
block_model = "/models/blocks/dirt"
```

Child paths inherit parent vars. For example `/block/dirt` inherits defaults from `/block`.

Common block vars:

- `numeric_id`: stable integer id stored in chunks and saves.
- `name`, `desc`: display metadata.
- `solid`: whether mesh and collision treat the block as solid.
- `block_texture`: texture for every cube face.
- `block_top_texture`, `block_side_texture`, `block_bottom_texture`: face-specific overrides.
- `block_model`: optional model path for future non-cube renderers.
- `behavior_script`: optional script path; if set, `PrototypeDatabase.create("/block/dirt")` instantiates it and calls `on_new(instance)` when present.

Common item vars:

- `numeric_id`: stable integer item id.
- `name`, `desc`: display metadata.
- `icon_texture`: inventory icon.
- `stack_size`: max stack size.
- `behavior_script`: optional behavior script for instances.

Path aliases map to project assets:

- `/textures/...` -> `res://assets/textures/...`
- `/models/...` -> `res://assets/models/...`
- `/scripts/...` -> `res://scripts/...`
- `/scenes/...` -> `res://scenes/...`
