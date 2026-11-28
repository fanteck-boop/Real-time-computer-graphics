# Exercise 9: Voxel Nisse

The Nisse scan rebuilt from 11,436 small cubes coloured from his texture. Click him and the cubes near the click burst out, spin and spring back. Press B and he scatters, then rebuilds from the boots up along his main PCA axis.

Libraries: PyVista 0.49.3 (VTK 9.7.2), SciPy 1.17.1 (`cKDTree`), trimesh 5.2.2, NumPy 2.4.6, Pillow, Matplotlib (colormap only), imageio-ffmpeg (video). Python 3.11 in the conda environment `rtcg_ex9`.

## Run

Double-click `run_ex9.bat`, or from an Anaconda Prompt:

```
conda activate rtcg_ex9
cd Ex9_Voxels
python nisse_voxels.py
```

Without activating: `C:\Users\trist\anaconda3\envs\rtcg_ex9\python.exe nisse_voxels.py`

Other modes:

- `python nisse_voxels.py --capture` renders everything offscreen (no window) and writes the PNGs and `demo.mp4` to `captures/`. Takes about 20 s.
- `python nisse_voxels.py --selftest` opens the window, clicks the centre of the window by itself, starts a scatter, prints a short report and closes after 8 s.

Recreate the environment: `conda env create -f environment.yml`

Startup takes about 2 s (loading the 17 MB OBJ, the texture, voxelising and colouring).

## Controls

| Input | Action |
|---|---|
| Left click (without dragging) | Blast at the cube under the mouse |
| Left drag | Rotate the camera (normal PyVista behaviour) |
| Right drag / wheel | Zoom |
| Middle drag / Shift + left drag | Pan |
| Space | Blast at the cube under the mouse cursor (same as a click) |
| B | Scatter the whole Nisse and rebuild him from the boots up |
| Up / Down | Bigger / smaller blast radius (0.04 to 0.40, steps of 0.02) |
| M | Colour mode: texture colours or distance from the last click (inferno colormap) |
| K | KD-tree view: shows the query sphere and lights up the neighbours it found |
| X | PCA axes on/off (the cubes turn see-through so the arrows show) |
| N | Reset all cubes and forget the last query |
| Q | Quit |

A click counts as a click when the mouse moves at most 3 pixels between press and release. A longer movement is a drag and only rotates the camera, so blasting and rotating do not fight.

The text in the top left shows the frame rate, voxel count, pitch, radius, how many neighbours the last query found, and how long that query took with the KD-tree and with brute force (median of 30 runs, in ms).

## Files

- `nisse_voxels.py` the whole program
- `environment.yml` the conda environment with the installed versions
- `run_ex9.bat` starts the program in the environment
- `NOTES.md` how it works, theory, parameters, problems, what is not verified
- `captures/` frames from `--capture` and `demo.mp4` (13 s, 1280x720)

The mesh and texture are read from `../Assets/Nisse/`. Nothing in the Unity project is changed.
