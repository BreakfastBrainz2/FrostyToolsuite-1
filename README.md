## What's new in this fork and in what ways it is different?

- InitFS modding.
- Improved mesh importing:
  - Added support for importing meshes that require tangent space compression.
  - More detailed exceptions if something is wrong with imported mesh
- Template and Blueprint modding (RimeWidgetBlueprint modding is somewhat broken, would really like to fix that if I knew how).
- Fixed Mod Manager exit, meaning you don't have to close it manually in Task Manager anymore.
- Shadercache symlinking, meaning it should help with performance when running mods.
- Fixed `Object reference not set to an instance of an object` `IterateSubKeys` type crash at launch.
- Mod Manager now features more advanced filtering functionality: you can show or hide applied mods in `Available Mod(s)` section.
- Fixed `ealayer3.dll` type crash when attempting to open audio assets.
- Some of the new plugins that expand Editor functionality.
- Mod Manager doesn't identify itself as Editor anymore.
- Fixed splash screen not showing banner art.
- Mod Manager now shows for what game version (Volume) mod was made.

# FrostyToolsuite
The most advanced modding platform for games running on DICE's Frostbite game engine.

## Setup

1. Download the source code.
2. Open the solution (found under FrostyEditor) with Visual Studio 2022, and make sure the project is set to ``Release - Final`` and ``x64``. Close out of retarget window if prompted.
3. Only build the projects themselves, never the solution.

## License
The Content, Name, Code, and all assets are licensed under a Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License.
