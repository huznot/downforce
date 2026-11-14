![Downforce](Media/title.png)

# Downforce

A short open-wheel racing game made in Unity. One wet race from the cockpit against a full grid, with free practice and a T-cam.

## Demo
[Watch the playthrough](https://www.youtube.com/watch?v=3Vs1ufC0Vrs)

Play in the browser: [huznot.github.io/downforce-site](https://huznot.github.io/downforce-site/)

## Download and play
1. Download `Downforce.zip` from [Releases](https://github.com/huznot/downforce/releases)
2. Unzip it
3. Open the `Downforce` folder and run `Downforce.exe`

Controls: WASD or arrow keys, any gamepad, or mouse steering (Settings). C changes camera, R resets, F11 toggles fullscreen.

## Run from source
1. Install Unity 6000.2.12f1 and [Git LFS](https://git-lfs.com) (the race scene is stored with LFS)
2. Clone this repo, then add the folder in Unity Hub
3. Open `Assets/_Game/Scenes/Race.unity` and press Play

Some trackside props use the free *Lowpoly Track Environment Free* pack by BEDRILL from the Unity Asset Store. It isn't included here; import it into `Assets/BEDRILL` to get them.

## Layout
| Path | What's in it |
| --- | --- |
| `Assets/_Game` | scenes, scripts, shaders, art and audio made for this game |
| `Assets/_Game/External` | CC0 textures (Poly Haven, ambientCG) and MIT-licensed WebXR hands |
| `Assets/Settings` | render pipeline settings |
| `Assets/WebGLTemplates` | the web page used for the browser build |
| `Tools` | build-time scripts (audio, trailer) |

## License
MIT, see [LICENSE](LICENSE).
