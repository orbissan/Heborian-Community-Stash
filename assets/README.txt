Future client assets belong to assets/<ModuleId>/ inside BepInEx/plugins/Heborian/.
Example: assets/MainMenuBackground/background.png (no background module exists yet).
Access through context.Assets.ReadBytes or LoadTexture; do not use absolute paths.
The client module host releases loaded textures on shutdown or failed initialization.
