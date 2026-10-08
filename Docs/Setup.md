# Barista VR: setup, run and build

## 1. Install Unity (once per machine)

1. Download **Unity Hub** from https://unity.com/download and sign in (Personal licence is fine for students).
2. Hub > **Installs** > **Install Editor** > choose the newest **Unity 6 LTS** (6000.0.x).
3. Tick these modules:
   - Microsoft Visual Studio Community (skip if you already use Visual Studio, Rider or Cursor)
   - **Windows Build Support (IL2CPP)**
4. Leave about 25 GB free. The download is 8-15 GB.

## 2. Open the project

1. Hub > **Projects** > **Add** > **Add project from disk** > choose `D:\Vidushan\ar vr\BaristaVR`.
   If Hub offers to open it with your newer 6000.0.x editor, accept.
2. First import takes a few minutes. If Unity asks **"Enable the new input system backends?"**, click **Yes**; the editor restarts.
3. If the Package Manager reports a version it cannot find, open `Packages/manifest.json`, or use
   Window > Package Manager, and pick the version it does offer for that package. Patch versions do not matter.

## 3. One-time project configuration

1. **XR samples**: Window > Package Manager > *XR Interaction Toolkit* > **Samples** tab > import
   **Starter Assets** and **XR Device Simulator**. If asked to fix interaction layers or project validation, accept.
2. **OpenXR**: Edit > Project Settings > **XR Plug-in Management** > PC tab > tick **OpenXR**.
   Under OpenXR > *Enabled Interaction Profiles*, add **Oculus Touch Controller Profile**,
   **Valve Index Controller Profile**, **HTC Vive Controller Profile** and **Khronos Simple Controller Profile**.
   Fix anything Project Validation flags.
3. Menu **Barista > Build Cafe Scene**. This creates `Assets/Scenes/CafeCounter.unity`, generated materials,
   a URP asset if none is assigned, the XR rig (seated, device origin, snap turn only) and the Device Simulator.
   Re-run it whenever you want a clean scene; it overwrites the scene file.

## 4. Test in the Editor (XR Device Simulator)

Press **Play**. The simulator's control panel appears in the bottom-left corner; expand it to see the key
bindings for your XRI version. You need four things from it:

- how to switch the mouse between the head, the left controller and the right controller
- how to move a controller in depth (toward the machine)
- how to rotate a controller (to tilt the pitcher when pouring, and to keep the tamper level)
- the **Grip** key (Select): grab tools, press buttons, drag the grind dial and the portafilter handle

Every interaction in the café is a Select, so the trigger is never required.

## 5. Build for Windows (graded exported-build check)

1. Menu **Barista > Build Windows Player**. The output is `Builds/Windows/BaristaVR.exe`.
2. Run the exe **without a headset**. The XR Device Simulator is stripped from builds (it is tagged EditorOnly), so
   the keyboard-and-mouse fallback switches on and a help panel appears above the result station:
   - Mouse moves the right hand along the cursor ray; **scroll** changes reach
   - **Hold left click** = Select (grab, press buttons, drag dials)
   - **Space** tilts the hand forward (pouring)
   - **Hold right click** + move = look
   - **Q / E** = 45 degree snap turn
3. Record in the presentation that it launched and that a full shift could be completed with the mouse.

These keys are added at runtime as extra bindings on the same XRI actions the interactors use
(`XRI Right Interaction/Select`, `Select Value`, `Activate`, `Activate Value`, `XRI Right Locomotion/Snap Turn`),
in the `KeyboardMouse` group. See `Assets/Scripts/Input/DesktopRigDriver.cs`.

## 6. Troubleshooting

- **Pink materials**: the URP asset is not assigned. Edit > Project Settings > Graphics > Default Render Pipeline >
  `Assets/Settings/BaristaURP`, then run Barista > Build Cafe Scene again.
- **"XR rig prefab not found"**: the Starter Assets sample is not imported (step 3.1).
- **Hands do nothing in the Editor**: make sure the XR Device Simulator sample is imported and rebuild the scene.
- **Text too big or small**: adjust `letterHeight` in the `Label(...)` calls in `Assets/Editor/CafeSceneBuilder.cs` and rebuild.
- **Compile errors after an XRI upgrade**: XRI 3.x renamed several namespaces. Send the Console message to whoever owns that script.

## Project layout

```
Assets/
  Editor/CafeSceneBuilder.cs     scene builder + Windows build menu
  Scripts/Flow/                  ShiftFlow (step machine), OrderTicket, HintMarker, TextUtil
  Scripts/Coffee/                Portafilter, GrinderStation, GrindDial, Tamper, TamperPress,
                                 GroupHead, ExtractionModel, MilkPitcher, SteamWand, MilkPour
  Scripts/Interaction/           PressButton, DragControl, ToolSocket, Haptics
  Scripts/UI/                    MachineGauge, ResultDial
  Scripts/Audio/                 AudioKit (procedural 3D sound), AmbientCafe
  Scripts/Input/                 DesktopRigDriver (keyboard/mouse fallback)
  Settings/BaristaInputActions.inputactions
Docs/                            this file, presentation notes
```
