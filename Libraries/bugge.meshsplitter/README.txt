# Mesh Splitter
This library helps split mesh files (.fbx, .obj and .dmx) into their separate parts/children/submeshes, and then combines them in a prefab file. \
This mimics the way Unity handles their mesh files, and allows you to manipulate each object of the mesh individually.

## How to use
To use the library just right click a mesh file in your `Assets/models` folder, and an option called `Split Mesh` should appear. This will prompt you to select a location of the resulting prefab, and the model files (.vmdl) files will be placed in a subfolder with the same name as the initial mesh file.