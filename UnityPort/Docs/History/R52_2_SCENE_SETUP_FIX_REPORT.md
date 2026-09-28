# R52.2 — UNITY SCENE AUTO-SETUP FIX

Target: **Unity 2022.3.62f3**

This hotfix addresses the reported Unity Editor error:

`Parent directory must exist before creating asset at: Assets/Scenes/PortDemo.unity`

## Root cause

`PortAutoSetup.Setup()` called `EditorSceneManager.SaveScene(scene, "Assets/Scenes/PortDemo.unity")` on a clean project before ensuring that `Assets/Scenes` existed.

## Fix

`Assets/BlockcraftPort/Editor/PortAutoSetup.cs` now:

1. Defines `Assets/Scenes` as the scene folder.
2. Checks whether Unity already recognizes it through `AssetDatabase.IsValidFolder`.
3. Creates it with `AssetDatabase.CreateFolder("Assets", "Scenes")` when it does not exist on disk.
4. Calls `AssetDatabase.Refresh()` when the directory exists physically but has not yet been registered by the AssetDatabase.
5. Only then creates/saves `Assets/Scenes/PortDemo.unity`.

The archive also contains an empty `Assets/Scenes/` directory, while the code remains self-healing if that directory is deleted before import.

## Scope

Relative to R52.1, the only production C# behavior change is the scene-folder guard in:

- `Assets/BlockcraftPort/Editor/PortAutoSetup.cs`

No gameplay, rendering, AI, biome, world generation, inventory, chest, furnace, crafting, survival, or ship behavior was changed.

## Validation

- R32 water/light: **31/31 PASS**
- R49 current renderer/source: **30/30 PASS**
- R49 shipwreck/render: **41/41 PASS**
- R50 ship runtime: **59/59 PASS**
- R51 survival/items/TNT: **71/71 PASS**
- R52 static functional: **87/87 PASS**
- R52.1 compile hotfix: **8/8 PASS**
- R52.2 scene setup: **9/9 PASS**

Total current compatible validation: **336/336 PASS**.

`validate_r52_2_scene_setup.py` additionally confirms that there is only one `EditorSceneManager.SaveScene` site under `Assets`, that its folder guard occurs before the save, and that the project archive includes `Assets/Scenes/`.

## Environment limitation

This environment does not contain Unity Editor, so the fix cannot be executed in a real Editor session here. The exact precondition that caused the reported exception is now enforced before `SaveScene` is called.
