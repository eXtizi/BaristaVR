using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace Barista.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/CafeCounter.unity from code: room, counter, machine, grinder, tools,
    /// world-space panels, audio and the XR rig (from the XRI Starter Assets sample).
    /// Re-run it any time; it overwrites the scene and the generated materials.
    /// </summary>
    public static class CafeSceneBuilder
    {
        const string k_ScenePath = "Assets/Scenes/CafeCounter.unity";
        const string k_MatDir = "Assets/Materials";
        const string k_SettingsDir = "Assets/Settings";
        const string k_ActionsPath = "Assets/Settings/BaristaInputActions.inputactions";
        const string k_BuildPath = "Builds/Windows/BaristaVR.exe";

        /// <summary>Counter-top height. The trainee is seated with eyes at about 1.2 m.</summary>
        const float Top = 0.84f;

        /// <summary>
        /// Front face of the back wall. Wall boards hang flat against it: angling them toward the seat
        /// pushes their far edge into the wall, and the shelf cups end at 1.54 m, so boards start above that.
        /// </summary>
        const float k_WallZ = 1.185f;

        /// <summary>Welcome and credits boards share one spot above the machine, out of reach.</summary>
        const float k_InfoBoardY = 1.85f;
        const float k_InfoBoardZ = k_WallZ;
        static readonly Vector2 k_InfoBoardSize = new Vector2(0.84f, 0.5f);
        static readonly Vector3 k_Eye = new Vector3(0f, 1.2f, 0f);

        static Font s_Font;
        static Shader s_Shader;
        static readonly Dictionary<string, Material> s_Mats = new Dictionary<string, Material>();

        [MenuItem("Barista/Build Cafe Scene", priority = 0)]
        public static void Build() => BuildScene();

        static bool BuildScene()
        {
            var rigPrefab = FindPrefab("XR Interaction Setup") ?? FindPrefab("XR Origin (XR Rig)");
            if (rigPrefab == null)
            {
                EditorUtility.DisplayDialog("Barista",
                    "XR rig prefab not found.\n\nOpen Window > Package Manager > XR Interaction Toolkit > Samples and import " +
                    "'Starter Assets' and 'XR Device Simulator', then run Barista > Build Cafe Scene again.", "OK");
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            EnsureFolder("Assets/Scenes");
            EnsureFolder(k_MatDir);
            EnsureFolder(k_SettingsDir);
            EnsureUrp();
            ConfigurePhysics();

            s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            s_Shader = GraphicsSettings.defaultRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                : null;
            if (s_Shader == null)
                s_Shader = Shader.Find("Standard");
            s_Mats.Clear();
            s_TextMat = null;
            s_ToolSlide = null;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildRoom();
            var parts = new Parts();
            BuildMachine(parts);
            BuildGrinder(parts);
            BuildTampStation(parts);
            BuildCupAndPitcher(parts);
            BuildTicket(parts);
            BuildResultStation(parts);
            BuildStationPanel(parts);
            BuildFlow(parts);
            BuildRig();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, k_ScenePath))
            {
                EditorUtility.DisplayDialog("Barista", $"Could not save {k_ScenePath}. Check the Console.", "OK");
                return false;
            }
            AddSceneToBuild();
            AssetDatabase.SaveAssets();

            Debug.Log("Barista: CafeCounter scene built. Press Play to test with the XR Device Simulator.");
            return true;
        }

        [MenuItem("Barista/Build Windows Player", priority = 20)]
        public static void BuildWindowsPlayer()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Barista", "Stop Play mode first, then build.", "OK");
                return;
            }

            // The player is built from the scene file on disk, so regenerate and save it first.
            if (!BuildScene())
                return;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { k_ScenePath },
                locationPathName = k_BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            var summary = report.summary;
            var fullPath = Path.GetFullPath(k_BuildPath);
            Debug.Log($"Barista: Windows build {summary.result}, {summary.totalSize / (1024 * 1024)} MB at {fullPath}");
            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("Barista", $"Windows build finished at {DateTime.Now:HH:mm}.\n\n{fullPath}", "Show in Explorer");
                EditorUtility.RevealInFinder(fullPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Barista", $"Windows build {summary.result}. Check the Console for errors.", "OK");
            }
        }

        class Parts
        {
            public PressButton startButton, brewButton, steamButton, grindButton;
            public DragControl lockLever;
            public ToolSocket fork, tampMat, steamRest;
            public Transform bayonet;
            public GroupHead groupHead;
            public Portafilter portafilter;
            public Tamper tamper;
            public MilkPitcher pitcher;
            public MilkPour cup;
            public MachineGauge gauge;
            public ExtractionModel extraction;
            public SteamWand steamWand;
            public ResultDial resultDial;
            public GameObject creditsBoard;
            public GameObject desktopHelp;
            public SessionControls session;
        }

        // ------------------------------------------------------------------ setup

        static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
                return;
            try
            {
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, $"{k_SettingsDir}/BaristaURP_Renderer.asset");
                var asset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(asset, $"{k_SettingsDir}/BaristaURP.asset");
                AssetDatabase.SaveAssets();
                GraphicsSettings.defaultRenderPipeline = asset;
                QualitySettings.renderPipeline = asset;
                Debug.Log("Barista: created and assigned a URP asset.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Barista: could not create a URP asset automatically, using the built-in pipeline. " + e.Message);
            }
        }

        static void BuildLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.46f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.31f, 0.27f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.1f);

            var sun = new GameObject("Window Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.78f);
            sun.intensity = 0.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, 20f, 0f);

            foreach (var x in new[] { -0.3f, 0.45f })
            {
                var lamp = new GameObject("Pendant Light").AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.82f, 0.62f);
                lamp.intensity = 0.7f;
                lamp.range = 4f;
                lamp.transform.position = new Vector3(x, 2.35f, 0.55f);

                var shade = Cyl("Pendant Shade", null, new Vector3(x, 2.48f, 0.55f), 0.22f, 0.14f, Mat("DarkSteel", new Color(0.12f, 0.12f, 0.13f), 0.8f, 0.5f), false);
                Prim(PrimitiveType.Sphere, "Bulb", shade.transform.parent, new Vector3(x, 2.4f, 0.55f), Vector3.one * 0.07f,
                    Mat("Bulb", new Color(1f, 0.9f, 0.7f), 0f, 0.15f, new Color(0.7f, 0.5f, 0.28f)), false);
                Cyl("Cord", null, new Vector3(x, 2.6f, 0.7f), 0.008f, 0.8f, Mat("Plastic", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f), false);
            }
        }

        // ------------------------------------------------------------------ environment

        static void BuildRoom()
        {
            var room = new GameObject("Cafe Room").transform;
            var plaster = Mat("Plaster", new Color(0.82f, 0.76f, 0.68f), 0f, 0.15f);
            var tiles = Mat("Tiles", new Color(0.9f, 0.9f, 0.88f), 0f, 0.7f);
            var floor = Mat("Floor", new Color(0.36f, 0.25f, 0.17f), 0f, 0.35f);
            var wood = Mat("Wood", new Color(0.52f, 0.34f, 0.2f), 0f, 0.35f);
            var ceramic = Mat("Ceramic", new Color(0.95f, 0.95f, 0.93f), 0f, 0.8f);
            var fabric = Mat("Fabric", new Color(0.25f, 0.35f, 0.3f), 0f, 0.1f);

            Prim(PrimitiveType.Cube, "Floor", room, new Vector3(0f, -0.05f, -0.9f), new Vector3(5f, 0.1f, 4.5f), floor);
            Prim(PrimitiveType.Cube, "Ceiling", room, new Vector3(0f, 3.05f, -0.9f), new Vector3(5f, 0.1f, 4.5f), plaster, false);
            Prim(PrimitiveType.Cube, "Back Wall", room, new Vector3(0f, 1.5f, 1.25f), new Vector3(5f, 3f, 0.1f), tiles);
            Prim(PrimitiveType.Cube, "Front Wall", room, new Vector3(0f, 1.5f, -3.1f), new Vector3(5f, 3f, 0.1f), plaster);
            Prim(PrimitiveType.Cube, "Left Wall", room, new Vector3(-2.5f, 1.5f, -0.9f), new Vector3(0.1f, 3f, 4.5f), plaster);
            Prim(PrimitiveType.Cube, "Right Wall", room, new Vector3(2.5f, 1.5f, -0.9f), new Vector3(0.1f, 3f, 4.5f), plaster);

            // Work counter in front of the seated trainee.
            Prim(PrimitiveType.Cube, "Work Counter", room, new Vector3(0f, (Top - 0.04f) * 0.5f, 0.62f), new Vector3(3f, Top - 0.04f, 0.8f), Mat("Cabinet", new Color(0.2f, 0.17f, 0.15f), 0f, 0.3f));
            Prim(PrimitiveType.Cube, "Counter Top", room, new Vector3(0f, Top - 0.02f, 0.62f), new Vector3(3.05f, 0.04f, 0.84f), wood);

            // Back-wall shelf with cups, menu board.
            Prim(PrimitiveType.Cube, "Cup Shelf", room, new Vector3(0f, 1.45f, 1.11f), new Vector3(2.4f, 0.02f, 0.18f), wood);
            for (var i = 0; i < 10; i++)
                Cyl("Shelf Cup", room, new Vector3(-1.05f + i * 0.23f, 1.5f, 1.1f), 0.08f, 0.08f, ceramic, false);

            var menu = Panel("Menu Board", room, new Vector3(1.0f, 2.03f, k_WallZ), Quaternion.identity, new Vector2(0.62f, 0.42f), Mat("Chalk", new Color(0.08f, 0.09f, 0.08f), 0f, 0.1f));
            var chalk = new Color(0.95f, 0.93f, 0.85f);
            Label(menu, "Menu Title", "MENU", new Vector3(0f, 0.15f, -0.006f), 0.032f, chalk);
            Label(menu, "Menu Items", "Espresso\nFlat White\nCappuccino\nLatte",
                new Vector3(-0.24f, 0.09f, -0.006f), 0.026f, chalk, TextAnchor.UpperLeft, TextAlignment.Left);
            Label(menu, "Menu Prices", "3.00\n4.50\n4.50\n4.80",
                new Vector3(0.24f, 0.09f, -0.006f), 0.026f, chalk, TextAnchor.UpperRight, TextAlignment.Right);

            // Customer side behind the trainee (visible with snap turn).
            Prim(PrimitiveType.Cube, "Service Counter", room, new Vector3(0f, 0.5f, -1.0f), new Vector3(2.4f, 1f, 0.5f), wood);
            Prim(PrimitiveType.Cube, "Till", room, new Vector3(0.6f, 1.08f, -1.0f), new Vector3(0.3f, 0.16f, 0.25f), Mat("Plastic", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f));
            for (var i = 0; i < 3; i++)
            {
                var x = -1.4f + i * 1.4f;
                Cyl("Table", room, new Vector3(x, 0.74f, -2.3f), 0.7f, 0.03f, wood);
                Cyl("Table Leg", room, new Vector3(x, 0.37f, -2.3f), 0.06f, 0.72f, Mat("DarkSteel", Color.black));
                Prim(PrimitiveType.Cube, "Chair", room, new Vector3(x - 0.45f, 0.45f, -2.3f), new Vector3(0.4f, 0.06f, 0.4f), fabric);
                Prim(PrimitiveType.Cube, "Chair", room, new Vector3(x + 0.45f, 0.45f, -2.3f), new Vector3(0.4f, 0.06f, 0.4f), fabric);
            }

            var ambience = new GameObject("Cafe Ambience");
            ambience.transform.position = new Vector3(0f, 1.4f, -2.2f);
            ambience.AddComponent<AmbientCafe>();
        }

        // ------------------------------------------------------------------ espresso machine

        static void BuildMachine(Parts p)
        {
            var steel = Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f);
            var dark = Mat("DarkSteel", new Color(0.12f, 0.12f, 0.13f), 0.8f, 0.5f);
            var accent = Mat("MachineRed", new Color(0.55f, 0.08f, 0.06f), 0.3f, 0.6f);

            var machine = new GameObject("Espresso Machine").transform;
            Prim(PrimitiveType.Cube, "Body", machine, new Vector3(0.05f, Top + 0.225f, 0.84f), new Vector3(0.62f, 0.45f, 0.34f), steel);
            Prim(PrimitiveType.Cube, "Side Panel L", machine, new Vector3(-0.262f, Top + 0.225f, 0.84f), new Vector3(0.006f, 0.44f, 0.33f), accent, false);
            Prim(PrimitiveType.Cube, "Side Panel R", machine, new Vector3(0.362f, Top + 0.225f, 0.84f), new Vector3(0.006f, 0.44f, 0.33f), accent, false);
            Prim(PrimitiveType.Cube, "Cup Warmer", machine, new Vector3(0.05f, Top + 0.455f, 0.84f), new Vector3(0.6f, 0.01f, 0.3f), dark);
            Prim(PrimitiveType.Cube, "Drip Tray", machine, new Vector3(0.05f, Top + 0.0125f, 0.585f), new Vector3(0.58f, 0.025f, 0.17f), dark);
            Label(machine, "Nameplate", "XR CONTINUUM", new Vector3(0.05f, Top + 0.16f, 0.66f), 0.03f, new Color(1f, 0.93f, 0.78f), backdrop: true);

            // Group head with bayonet mount and lock lever.
            var ghGo = new GameObject("Group Head");
            ghGo.transform.SetParent(machine, false);
            ghGo.transform.localPosition = new Vector3(-0.05f, Top + 0.32f, 0.6f);
            Prim(PrimitiveType.Cube, "Neck", ghGo.transform, new Vector3(0f, 0.04f, 0.035f), new Vector3(0.1f, 0.06f, 0.07f), steel);
            Cyl("Head", ghGo.transform, Vector3.zero, 0.095f, 0.06f, steel);

            var bayonet = new GameObject("Bayonet").transform;
            bayonet.SetParent(ghGo.transform, false);
            bayonet.localPosition = new Vector3(0f, -0.04f, 0f);
            p.bayonet = bayonet;

            var lever = new GameObject("Lock Lever");
            lever.transform.SetParent(bayonet, false);
            lever.transform.localPosition = new Vector3(0f, -0.008f, -0.095f);
            var leverCol = lever.AddComponent<BoxCollider>();
            leverCol.size = new Vector3(0.05f, 0.05f, 0.14f);
            lever.AddComponent<XRSimpleInteractable>();
            var drag = lever.AddComponent<DragControl>();
            drag.dragFrame = ghGo.transform;
            drag.localAxis = Vector3.right;
            drag.metresForFullRange = 0.08f;
            drag.tickStep = 0.25f;
            drag.value = 1f;
            p.lockLever = drag;

            p.portafilter = BuildPortafilter(bayonet.position);

            p.groupHead = ghGo.AddComponent<GroupHead>();
            p.groupHead.bayonet = bayonet;
            p.groupHead.lockLever = drag;
            p.groupHead.portafilter = p.portafilter;

            var stream = Cyl("Espresso Stream", machine, new Vector3(-0.05f, Top + 0.1375f, 0.6f), 0.006f, 0.215f, Mat("Espresso", new Color(0.25f, 0.13f, 0.06f), 0f, 0.8f), false);

            // Buttons.
            p.brewButton = Button("Brew Button", machine, new Vector3(-0.05f, Top + 0.41f, 0.66f), 0.04f, accent, "BREW");
            p.steamButton = Button("Steam Button", machine, new Vector3(0.25f, Top + 0.41f, 0.66f), 0.04f, dark, "STEAM");
            var knob = new GameObject("Valve Knob").transform;
            knob.SetParent(p.steamButton.transform, false);
            knob.localPosition = new Vector3(0f, 0f, -0.012f);
            Prim(PrimitiveType.Cube, "Knob Bar", knob, new Vector3(0f, 0.012f, 0f), new Vector3(0.008f, 0.03f, 0.006f), steel, false);

            // Steam wand: tip sits inside a pitcher parked on the steam rest.
            var wand = new GameObject("Steam Wand").transform;
            wand.SetParent(machine, false);
            Prim(PrimitiveType.Cube, "Wand Arm", wand, new Vector3(0.25f, Top + 0.365f, 0.635f), new Vector3(0.02f, 0.02f, 0.07f), steel, false);
            Cyl("Wand Pipe", wand, new Vector3(0.25f, Top + 0.22f, 0.6f), 0.012f, 0.29f, steel, false);
            var tip = new GameObject("Wand Tip").transform;
            tip.SetParent(wand, false);
            tip.localPosition = new Vector3(0.25f, Top + 0.075f, 0.6f);
            var puff = Prim(PrimitiveType.Sphere, "Steam Puff", tip, new Vector3(0f, -0.02f, 0f), new Vector3(0.05f, 0.07f, 0.05f), Mat("Steam", new Color(0.92f, 0.92f, 0.95f), 0f, 0.9f), false);
            p.steamRest = Socket("Steam Rest", machine, new Vector3(0.25f, Top + 0.025f, 0.6f), SocketAccepts.Pitcher, 0.07f);
            Cyl("Steam Rest Ring", machine, new Vector3(0.25f, Top + 0.026f, 0.6f), 0.09f, 0.002f, Mat("Rubber", new Color(0.03f, 0.03f, 0.03f), 0f, 0.2f), false);

            // Pressure gauge on top of the machine.
            var gaugePos = new Vector3(0.05f, Top + 0.55f, 0.75f);
            var gaugePanel = Panel("Gauge Panel", machine, gaugePos, Facing(gaugePos), new Vector2(0.5f, 0.17f), dark);
            var face = Cyl("Gauge Face", gaugePanel, new Vector3(-0.16f, 0f, -0.006f), 0.11f, 0.004f, Mat("GaugeFace", new Color(0.95f, 0.94f, 0.9f), 0f, 0.6f), false);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var needlePivot = new GameObject("Needle Pivot").transform;
            needlePivot.SetParent(gaugePanel, false);
            needlePivot.localPosition = new Vector3(-0.16f, 0f, -0.01f);
            Prim(PrimitiveType.Cube, "Needle", needlePivot, new Vector3(0f, 0.02f, 0f), new Vector3(0.004f, 0.042f, 0.002f), Mat("NeedleRed", new Color(0.85f, 0.1f, 0.08f), 0f, 0.5f), false);
            var marks = new[] { (0, 120f), (3, 60f), (6, 0f), (9, -60f), (12, -120f) };
            foreach (var (bar, angle) in marks)
            {
                var a = angle * Mathf.Deg2Rad;
                var pos = new Vector3(-0.16f - Mathf.Sin(a) * 0.04f, Mathf.Cos(a) * 0.04f, -0.01f);
                Label(gaugePanel, "Mark " + bar, bar.ToString(), pos, 0.009f, bar == 9 ? new Color(0.1f, 0.55f, 0.2f) : new Color(0.15f, 0.15f, 0.15f));
            }
            Label(gaugePanel, "Bar Label", "BAR", new Vector3(-0.16f, -0.03f, -0.01f), 0.008f, new Color(0.3f, 0.3f, 0.3f));
            var readout = Label(gaugePanel, "Readout", "0.0 bar", new Vector3(-0.06f, 0.01f, -0.01f), 0.014f, new Color(1f, 0.95f, 0.8f), TextAnchor.MiddleLeft, TextAlignment.Left);
            p.gauge = gaugePanel.gameObject.AddComponent<MachineGauge>();
            p.gauge.needlePivot = needlePivot;
            p.gauge.readout = readout;

            p.extraction = machine.gameObject.AddComponent<ExtractionModel>();
            p.extraction.groupHead = p.groupHead;
            p.extraction.brewButton = p.brewButton;
            p.extraction.gauge = p.gauge;
            p.extraction.espressoStream = stream;

            p.steamWand = machine.gameObject.AddComponent<SteamWand>();
            p.steamWand.valveButton = p.steamButton;
            p.steamWand.valveKnob = knob;
            p.steamWand.tip = tip;
            p.steamWand.steamRest = p.steamRest;
            p.steamWand.gauge = p.gauge;
            p.steamWand.steamPuff = puff;
        }

        static Portafilter BuildPortafilter(Vector3 position)
        {
            var steel = Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f);
            var plastic = Mat("Plastic", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f);

            var go = new GameObject("Portafilter");
            go.transform.position = position;
            Cyl("Basket Floor", go.transform, new Vector3(0f, -0.016f, 0f), 0.07f, 0.008f, steel);
            Ring(go.transform, 0.031f, -0.012f, 0.022f, 10, 0.004f, steel, true);
            var bed = Cyl("Coffee Bed", go.transform, new Vector3(0f, -0.011f, 0f), 0.062f, 0.002f, Mat("Coffee", new Color(0.3f, 0.18f, 0.1f), 0f, 0.1f), false);
            Prim(PrimitiveType.Cube, "Neck", go.transform, new Vector3(0f, -0.008f, -0.045f), new Vector3(0.02f, 0.015f, 0.03f), steel);
            Prim(PrimitiveType.Cube, "Handle", go.transform, new Vector3(0f, -0.008f, -0.11f), new Vector3(0.026f, 0.024f, 0.11f), plastic);
            Cyl("Spout", go.transform, new Vector3(0f, -0.027f, 0f), 0.02f, 0.014f, steel, false);
            Prim(PrimitiveType.Cube, "Lug L", go.transform, new Vector3(-0.04f, 0.004f, 0f), new Vector3(0.012f, 0.006f, 0.02f), steel, false);
            Prim(PrimitiveType.Cube, "Lug R", go.transform, new Vector3(0.04f, 0.004f, 0f), new Vector3(0.012f, 0.006f, 0.02f), steel, false);

            // Pivot is the basket centre; the spout hangs 34 mm below it.
            var grab = MakeGrabbable(go, 0.5f, 0.034f);
            go.GetComponent<ToolSettle>().home = new Vector3(-0.12f, Top + 0.034f, 0.32f);
            grab.enabled = true;
            var pf = go.AddComponent<Portafilter>();
            pf.bed = bed.transform;
            return pf;
        }

        // ------------------------------------------------------------------ grinder

        static void BuildGrinder(Parts p)
        {
            var body = Mat("GrinderBody", new Color(0.1f, 0.1f, 0.11f), 0.4f, 0.4f);
            var steel = Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f);
            var grinder = new GameObject("Grinder").transform;

            Prim(PrimitiveType.Cube, "Body", grinder, new Vector3(-0.5f, Top + 0.18f, 0.78f), new Vector3(0.18f, 0.36f, 0.22f), body);
            Cyl("Hopper", grinder, new Vector3(-0.5f, Top + 0.44f, 0.78f), 0.13f, 0.16f, Mat("Smoke", new Color(0.25f, 0.2f, 0.17f), 0f, 0.9f));
            Cyl("Beans", grinder, new Vector3(-0.5f, Top + 0.522f, 0.78f), 0.125f, 0.004f, Mat("Beans", new Color(0.22f, 0.12f, 0.06f), 0f, 0.4f), false);
            Prim(PrimitiveType.Cube, "Chute", grinder, new Vector3(-0.5f, Top + 0.2f, 0.645f), new Vector3(0.035f, 0.04f, 0.05f), steel, false);
            Prim(PrimitiveType.Cube, "Fork L", grinder, new Vector3(-0.542f, Top + 0.115f, 0.635f), new Vector3(0.008f, 0.008f, 0.07f), steel, false);
            Prim(PrimitiveType.Cube, "Fork R", grinder, new Vector3(-0.458f, Top + 0.115f, 0.635f), new Vector3(0.008f, 0.008f, 0.07f), steel, false);
            var grounds = Cyl("Grounds Stream", grinder, new Vector3(-0.5f, Top + 0.16f, 0.6f), 0.01f, 0.04f, Mat("Coffee", new Color(0.3f, 0.18f, 0.1f), 0f, 0.1f), false);

            p.fork = Socket("Grinder Fork", grinder, new Vector3(-0.5f, Top + 0.13f, 0.6f), SocketAccepts.Portafilter, 0.06f);
            p.grindButton = Button("Grind Button", grinder, new Vector3(-0.44f, Top + 0.29f, 0.66f), 0.032f, Mat("Green", new Color(0.1f, 0.5f, 0.2f), 0f, 0.5f), "HOLD TO\nGRIND", -0.05f, 0.012f);

            // Grind dial: drag sideways to turn.
            var dialRoot = new GameObject("Grind Dial");
            dialRoot.transform.SetParent(grinder, false);
            dialRoot.transform.localPosition = new Vector3(-0.56f, Top + 0.29f, 0.66f);
            var pivot = new GameObject("Knob Pivot").transform;
            pivot.SetParent(dialRoot.transform, false);
            var knob = Cyl("Knob", pivot, Vector3.zero, 0.045f, 0.02f, steel);
            knob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Prim(PrimitiveType.Cube, "Pointer", pivot, new Vector3(0f, 0.013f, -0.011f), new Vector3(0.005f, 0.016f, 0.003f), Mat("NeedleRed", new Color(0.85f, 0.1f, 0.08f), 0f, 0.5f), false);
            dialRoot.AddComponent<XRSimpleInteractable>();
            var dialDrag = dialRoot.AddComponent<DragControl>();
            dialDrag.dragFrame = grinder;
            dialDrag.localAxis = Vector3.right;
            dialDrag.metresForFullRange = 0.15f;
            dialDrag.tickStep = 1f / 90f;
            var dialLabel = Label(grinder, "Grind Label", "GRIND 6.5", new Vector3(-0.56f, Top + 0.24f, 0.66f), 0.012f, Color.white, backdrop: true);
            var dial = dialRoot.AddComponent<GrindDial>();
            dial.drag = dialDrag;
            dial.knobPivot = pivot;
            dial.label = dialLabel;

            var screenPos = new Vector3(-0.5f, Top + 0.345f, 0.671f);
            Prim(PrimitiveType.Cube, "Screen", grinder, screenPos, new Vector3(0.15f, 0.045f, 0.004f), Mat("Screen", new Color(0.02f, 0.05f, 0.04f), 0f, 0.9f), false);
            var display = Label(grinder, "Dose Display", "DOSE 0.0 g", screenPos + new Vector3(0f, 0f, -0.006f), 0.016f, new Color(0.6f, 1f, 0.75f));

            var station = grinder.gameObject.AddComponent<GrinderStation>();
            station.fork = p.fork;
            station.grindButton = p.grindButton;
            station.display = display;
            station.groundsStream = grounds;
        }

        // ------------------------------------------------------------------ tamping

        static void BuildTampStation(Parts p)
        {
            var station = new GameObject("Tamp Station").transform;
            var rubber = Mat("Rubber", new Color(0.03f, 0.03f, 0.03f), 0f, 0.2f);
            Prim(PrimitiveType.Cube, "Tamp Mat", station, new Vector3(-0.22f, Top + 0.006f, 0.45f), new Vector3(0.16f, 0.012f, 0.16f), rubber);
            // Basket colliders reach 20 mm below the pivot. Seat it 4 mm clear of the mat, so lifting it out
            // starts from free air instead of from a contact.
            p.tampMat = Socket("Tamp Socket", station, new Vector3(-0.22f, Top + 0.036f, 0.45f), SocketAccepts.Portafilter, 0.06f);

            var cardPos = new Vector3(-0.22f, Top + 0.09f, 0.56f);
            var card = Panel("Tamp Card", station, cardPos, Facing(cardPos), new Vector2(0.2f, 0.08f), Mat("Panel", new Color(0.1f, 0.1f, 0.11f), 0f, 0.3f));
            card.Find("Backing").gameObject.AddComponent<BoxCollider>();
            var readout = Label(card, "Tamp Readout", "TAMP", new Vector3(0f, 0f, -0.008f), 0.016f, new Color(1f, 0.95f, 0.8f), backdrop: true);

            p.tamper = BuildTamper(new Vector3(-0.37f, Top, 0.42f));
            p.tamper.GetComponent<TamperGrabTransformer>().mat = p.tampMat;

            var press = station.gameObject.AddComponent<TamperPress>();
            press.mat = p.tampMat;
            press.tamper = p.tamper;
            press.readout = readout;
        }

        static Tamper BuildTamper(Vector3 position)
        {
            var steel = Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f);
            var wood = Mat("Wood", new Color(0.52f, 0.34f, 0.2f), 0f, 0.35f);
            var go = new GameObject("Tamper");
            go.transform.position = position;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            Cyl("Base", visual, new Vector3(0f, 0.006f, 0f), 0.057f, 0.012f, steel, false);
            Cyl("Shaft", visual, new Vector3(0f, 0.032f, 0f), 0.018f, 0.04f, steel, false);
            Cyl("Handle", visual, new Vector3(0f, 0.082f, 0f), 0.042f, 0.06f, wood, false);
            Prim(PrimitiveType.Sphere, "Knob", visual, new Vector3(0f, 0.112f, 0f), Vector3.one * 0.042f, wood, false);
            // Colliders are siblings of the visual, so the tamp spring can lift the mesh without
            // pulling the colliders out of the way. Round discs, not the cylinder's bounding box:
            // a box on the base is wider at the corners than the basket opening, so it could never
            // sit in the basket and it cut into the portafilter handle.
            SolidDisc("Base Hit", go.transform, new Vector3(0f, 0.006f, 0f), 0.052f, 0.012f);
            SolidDisc("Shaft Hit", go.transform, new Vector3(0f, 0.032f, 0f), 0.018f, 0.04f);
            SolidDisc("Handle Hit", go.transform, new Vector3(0f, 0.086f, 0f), 0.04f, 0.068f);

            var grab = MakeGrabbable(go, 0.45f);
            // Orientation and the basket guide come from the transformer; physics still blocks it.
            grab.useDynamicAttach = false;
            grab.smoothPosition = false;
            grab.smoothRotation = false;
            go.AddComponent<TamperGrabTransformer>();
            var tamper = go.AddComponent<Tamper>();
            tamper.visual = visual;
            return tamper;
        }

        // ------------------------------------------------------------------ cup and pitcher

        static void BuildCupAndPitcher(Parts p)
        {
            var ceramic = Mat("Ceramic", new Color(0.95f, 0.95f, 0.93f), 0f, 0.8f);
            var steel = Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f);
            var milk = Mat("Milk", new Color(0.97f, 0.96f, 0.92f), 0f, 0.6f);

            // Pitcher, waiting on the counter to the right.
            var pitcherGo = new GameObject("Milk Pitcher");
            pitcherGo.transform.position = new Vector3(0.47f, Top, 0.42f);
            Cyl("Floor", pitcherGo.transform, new Vector3(0f, 0.003f, 0f), 0.084f, 0.006f, steel);
            Ring(pitcherGo.transform, 0.039f, 0.006f, 0.114f, 12, 0.004f, steel, true);
            Prim(PrimitiveType.Cube, "Handle", pitcherGo.transform, new Vector3(0f, 0.065f, -0.055f), new Vector3(0.014f, 0.07f, 0.016f), Mat("Plastic", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f));
            var spout = new GameObject("Spout").transform;
            spout.SetParent(pitcherGo.transform, false);
            spout.localPosition = new Vector3(0f, 0.12f, 0.046f);
            Prim(PrimitiveType.Cube, "Spout Lip", spout, Vector3.zero, new Vector3(0.022f, 0.006f, 0.02f), steel, false);
            var surface = Cyl("Milk Surface", pitcherGo.transform, new Vector3(0f, 0.07f, 0f), 0.076f, 0.003f, milk, false);
            Prim(PrimitiveType.Cube, "Thermometer", pitcherGo.transform, new Vector3(-0.026f, 0.1f, -0.044f), new Vector3(0.036f, 0.02f, 0.004f), Mat("Screen", new Color(0.02f, 0.05f, 0.04f), 0f, 0.9f), false);
            var thermo = Label(pitcherGo.transform, "Thermometer Text", "5Â°C", new Vector3(-0.026f, 0.1f, -0.05f), 0.016f, Color.white, backdrop: true);

            var pitcherGrab = MakeGrabbable(pitcherGo, 0.6f);
            // The pour transformer keeps the jug beside the sight line while its spout is over the cup.
            pitcherGrab.useDynamicAttach = false;
            var pourGuide = pitcherGo.AddComponent<PitcherPourTransformer>();
            pourGuide.spout = spout;
            p.pitcher = pitcherGo.AddComponent<MilkPitcher>();
            p.pitcher.milkSurface = surface.transform;
            p.pitcher.spout = spout;
            p.pitcher.thermometer = thermo;

            // Mark on the drip tray. Letting go inside it slides the cup onto the spouts.
            var cupMark = new Vector3(-0.05f, Top + 0.027f, 0.6f);
            Cyl("Cup Ring", null, cupMark, 0.1f, 0.003f, Mat("Rubber", new Color(0.03f, 0.03f, 0.03f), 0f, 0.2f), false);

            // Cup, under the group head on the drip tray.
            var cupGo = new GameObject("Cup");
            cupGo.transform.position = new Vector3(-0.05f, Top + 0.025f, 0.6f);
            Cyl("Floor", cupGo.transform, new Vector3(0f, 0.003f, 0f), 0.07f, 0.006f, ceramic);
            Ring(cupGo.transform, 0.038f, 0.006f, 0.069f, 12, 0.005f, ceramic, true);
            Prim(PrimitiveType.Cube, "Handle", cupGo.transform, new Vector3(0.05f, 0.04f, 0f), new Vector3(0.012f, 0.04f, 0.012f), ceramic);
            var liquid = Cyl("Coffee", cupGo.transform, new Vector3(0f, 0.006f, 0f), 0.074f, 0.003f, Mat("Espresso", new Color(0.25f, 0.13f, 0.06f), 0f, 0.8f), false);
            var pourStream = Cyl("Milk Stream", null, cupGo.transform.position + Vector3.up * 0.15f, 0.008f, 0.1f, milk, false);

            MakeGrabbable(cupGo, 0.3f);
            p.cup = cupGo.AddComponent<MilkPour>();
            p.cup.pitcher = p.pitcher;
            p.cup.liquid = liquid.transform;
            p.cup.pourStream = pourStream.transform;
            pourGuide.cup = p.cup;

            p.extraction.cup = p.cup;
            p.steamWand.pitcher = p.pitcher;
        }

        // ------------------------------------------------------------------ ticket and results

        static void BuildTicket(Parts p)
        {
            // Left of the machine, clear of the portafilter and grinder handles.
            var pos = new Vector3(-1.0f, 1.2f, 0.45f);
            var ticket = Panel("Order Ticket", null, pos, Facing(pos), new Vector2(0.56f, 0.76f), Mat("Paper", new Color(0.98f, 0.96f, 0.9f), 0f, 0.05f));
            Prim(PrimitiveType.Cube, "Ticket Rail", ticket, new Vector3(0f, 0.39f, 0.004f), new Vector3(0.6f, 0.025f, 0.02f), Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f), false);
            var ink = new Color(0.08f, 0.06f, 0.04f);
            var list = Label(ticket, "Checklist", "ORDER", new Vector3(-0.25f, 0.35f, -0.008f), 0.017f, ink, TextAnchor.UpperLeft, TextAlignment.Left);
            Prim(PrimitiveType.Cube, "Now Rule", ticket, new Vector3(0f, -0.02f, -0.004f), new Vector3(0.48f, 0.003f, 0.002f), Mat("InkRule", ink, 0f, 0.1f), false);
            var now = Label(ticket, "Now", "NOW:", new Vector3(-0.25f, -0.05f, -0.008f), 0.02f, new Color(0.45f, 0.08f, 0.04f), TextAnchor.UpperLeft, TextAlignment.Left);
            // Above the machine against the back wall: its bottom edge clears the gauge sight line, so the
            // trainee still sees the whole work area. Pressed with the pointer ray; it hides after START.
            var welcomePos = new Vector3(0.05f, k_InfoBoardY, k_InfoBoardZ);
            var welcome = Panel("Welcome Board", null, welcomePos, Quaternion.identity, k_InfoBoardSize, Mat("MatteBoard", new Color(0.11f, 0.1f, 0.09f), 0f, 0f));
            Prim(PrimitiveType.Cube, "Title Rule", welcome, new Vector3(0f, 0.165f, -0.004f), new Vector3(0.6f, 0.004f, 0.002f),
                Mat("Brass", new Color(0.72f, 0.58f, 0.32f), 0.4f, 0.45f), false);
            var cream = new Color(0.96f, 0.94f, 0.88f);
            Label(welcome, "Welcome Title", "BARISTA CALIBRATION TRAINER", new Vector3(0f, 0.2f, -0.01f), 0.03f, cream);
            Label(welcome, "Welcome Team", "XR Continuum", new Vector3(0f, 0.14f, -0.01f), 0.018f, new Color(0.78f, 0.74f, 0.66f));
            Label(welcome, "Welcome Text",
                "Make one Double Shot Flat White.\n" +
                "1  Follow the order ticket on your left.\n" +
                "2  Touch whatever the yellow marker floats over.\n" +
                "3  Grip (left mouse on desktop) grabs and presses.\n" +
                "4  Your shot report appears on your right.\n" +
                "Station panel, top left: move, reset, credits, exit.",
                new Vector3(-0.37f, 0.1f, -0.01f), 0.021f, cream, TextAnchor.UpperLeft, TextAlignment.Left);
            p.startButton = Button("Start Button", welcome, new Vector3(0.28f, -0.17f, -0.012f), 0.07f, Mat("Green", new Color(0.1f, 0.5f, 0.2f), 0f, 0.5f), "START", 0.055f, 0.026f);
            welcome.gameObject.AddComponent<WelcomeBoard>().board = welcome.gameObject;

            var ot = ticket.gameObject.AddComponent<OrderTicket>();
            ot.checklist = list;
            ot.nowLine = now;
            ot.startButton = p.startButton;
            ot.wrapChars = 36;

            // Right of the menu, clear of the cup shelf. One line each, large enough to read from the seat.
            var helpPos = new Vector3(1.78f, 1.7f, k_WallZ);
            var help = Panel("Desktop Help", null, helpPos, Quaternion.identity, new Vector2(0.7f, 0.52f), Mat("MatteBoard", new Color(0.11f, 0.1f, 0.09f), 0f, 0f));
            var helpInk = new Color(0.96f, 0.94f, 0.88f);
            Label(help, "Help Title", "DESKTOP MODE", new Vector3(0f, 0.2f, -0.01f), 0.034f, helpInk);
            Label(help, "Help Keys", "Mouse\nLeft click\nScroll\nSpace\nRight click\nQ and E",
                new Vector3(-0.3f, 0.14f, -0.01f), 0.028f, helpInk, TextAnchor.UpperLeft, TextAlignment.Left);
            Label(help, "Help Actions", "moves the hand\ngrab or press\nreach in or out\ntilt to pour\nlook around\nturn",
                new Vector3(0.3f, 0.14f, -0.01f), 0.028f, new Color(0.78f, 0.74f, 0.66f), TextAnchor.UpperRight, TextAlignment.Right);
            p.desktopHelp = help.gameObject;
        }

        static void BuildResultStation(Parts p)
        {
            var pos = new Vector3(1.0f, 1.26f, 0.45f);
            var root = new GameObject("Result Station").transform;
            root.SetPositionAndRotation(pos, Facing(pos));

            var panel = Panel("Result Panel", root, pos, Facing(pos), new Vector2(0.56f, 0.7f), Mat("Panel", new Color(0.08f, 0.08f, 0.09f), 0f, 0.2f));

            var dialFace = Cyl("Dial Face", panel, new Vector3(0f, 0.17f, -0.004f), 0.17f, 0.004f, Mat("GaugeFace", new Color(0.95f, 0.94f, 0.9f), 0f, 0.6f), false);
            dialFace.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var zones = new[] { ("SOUR", 60f, new Color(0.95f, 0.75f, 0.2f)), ("BALANCED", 0f, new Color(0.2f, 0.7f, 0.3f)), ("BITTER", -60f, new Color(0.45f, 0.25f, 0.12f)) };
            foreach (var (name, angle, color) in zones)
            {
                var a = angle * Mathf.Deg2Rad;
                var dir = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
                var marker = Prim(PrimitiveType.Cube, name + " Zone", panel, new Vector3(0f, 0.17f, -0.008f) + dir * 0.07f, new Vector3(0.03f, 0.008f, 0.002f), Mat("Zone" + name, color, 0f, 0.4f), false);
                marker.transform.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
                Label(panel, name + " Label", name, new Vector3(0f, 0.17f, -0.012f) + dir * 0.05f, 0.014f, new Color(0.08f, 0.08f, 0.08f));
            }
            var needlePivot = new GameObject("Needle Pivot").transform;
            needlePivot.SetParent(panel, false);
            needlePivot.localPosition = new Vector3(0f, 0.17f, -0.011f);
            Prim(PrimitiveType.Cube, "Needle", needlePivot, new Vector3(0f, 0.03f, 0f), new Vector3(0.004f, 0.065f, 0.002f), Mat("NeedleRed", new Color(0.85f, 0.1f, 0.08f), 0f, 0.5f), false);

            var verdict = Label(panel, "Verdict", "VERDICT", new Vector3(0f, 0.02f, -0.01f), 0.022f, Color.white);
            var detail = Label(panel, "Detail", "", new Vector3(-0.25f, -0.01f, -0.01f), 0.018f, new Color(1f, 0.96f, 0.88f), TextAnchor.UpperLeft, TextAlignment.Left);
            var retry = Button("Retry Button", panel, new Vector3(-0.12f, -0.3f, -0.01f), 0.05f, Mat("Green", new Color(0.1f, 0.5f, 0.2f), 0f, 0.5f), "PULL ANOTHER SHOT", 0.055f, 0.016f);
            var credits = Button("Credits Button", panel, new Vector3(0.12f, -0.3f, -0.01f), 0.05f, Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f), "CREDITS", 0.055f, 0.016f);

            var creditsPos = new Vector3(0.05f, k_InfoBoardY, k_InfoBoardZ - 0.02f);
            var board = Panel("Credits Board", null, creditsPos, Quaternion.identity, new Vector2(0.9f, 0.62f), Mat("MatteBoard", new Color(0.11f, 0.1f, 0.09f), 0f, 0f));
            Label(board, "Credits Title", "CREDITS", new Vector3(0f, 0.26f, -0.01f), 0.028f, new Color(0.96f, 0.94f, 0.88f));
            Label(board, "Credits Text",
                "Team XR Continuum  ·  INTE 42312\n\n" +
                "FROM OUTSIDE\n" +
                "Headset and hand models: XR Origin and XR Device Simulator,\n" +
                "Unity XR Interaction Toolkit 3.6.1 Starter Assets.\n" +
                "Unity Technologies. Unity Companion License.\n" +
                "Also used: OpenXR Plugin, Input System, URP.\n\n" +
                "MADE IN THIS PROJECT\n" +
                "Café models: cubes, cylinders and spheres, not a model pack.\n" +
                "Audio: no recordings. All sounds are generated in AudioKit.cs.\n" +
                "Font: Unity built-in LegacyRuntime (Arial).\n" +
                "AI-tool use is disclosed in the presentation.",
                new Vector3(-0.41f, 0.21f, -0.01f), 0.016f, new Color(0.96f, 0.94f, 0.88f), TextAnchor.UpperLeft, TextAlignment.Left);
            Label(board, "Credits Close", "Press CREDITS again to close", new Vector3(0f, -0.27f, -0.01f), 0.014f, new Color(0.78f, 0.74f, 0.66f));

            p.resultDial = root.gameObject.AddComponent<ResultDial>();
            p.resultDial.panelRoot = panel.gameObject;
            p.resultDial.needlePivot = needlePivot;
            p.resultDial.verdictText = verdict;
            p.resultDial.detailText = detail;
            p.resultDial.retryButton = retry;
            p.resultDial.creditsButton = credits;
            p.resultDial.creditsBoard = board.gameObject;
            p.creditsBoard = board.gameObject;
        }

        /// <summary>
        /// Station controls live on the back wall, well beyond arm's reach, so grabbing tools can never
        /// press them by accident. They are used with the pointer ray. Reset and Exit ask for a second press.
        /// </summary>
        static void BuildStationPanel(Parts p)
        {
            var pos = new Vector3(-0.95f, 1.82f, k_WallZ);
            var panel = Panel("Station Panel", null, pos, Quaternion.identity, new Vector2(0.62f, 0.4f), Mat("MatteBoard", new Color(0.11f, 0.1f, 0.09f), 0f, 0f));
            var cream = new Color(0.96f, 0.94f, 0.88f);
            Label(panel, "Station Title", "STATION", new Vector3(0f, 0.165f, -0.01f), 0.022f, cream);

            var steel = Mat("Steel", new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.75f);
            var dark = Mat("DarkSteel", new Color(0.12f, 0.12f, 0.13f), 0.8f, 0.5f);
            PressButton Make(string name, float x, float y, string label, Material mat) =>
                Button(name, panel, new Vector3(x, y, -0.012f), 0.06f, mat, label, 0.05f, 0.017f);

            var session = panel.gameObject.AddComponent<SessionControls>();
            session.slideLeft = Make("Slide Left", -0.22f, 0.02f, "SLIDE LEFT", steel);
            session.turnLeft = Make("Turn Left", -0.075f, 0.02f, "TURN LEFT", steel);
            session.turnRight = Make("Turn Right", 0.075f, 0.02f, "TURN RIGHT", steel);
            session.slideRight = Make("Slide Right", 0.22f, 0.02f, "SLIDE RIGHT", steel);
            session.resetButton = Make("Reset", -0.17f, -0.13f, "RESET", Mat("MachineRed", new Color(0.55f, 0.08f, 0.06f), 0.3f, 0.6f));
            session.creditsButton = Make("Credits", 0f, -0.13f, "CREDITS", Mat("Green", new Color(0.1f, 0.5f, 0.2f), 0f, 0.5f));
            session.exitButton = Make("Exit", 0.17f, -0.13f, "EXIT", dark);
            session.creditsBoard = p.creditsBoard;
            p.session = session;
        }

        static void BuildFlow(Parts p)
        {
            var hintGo = Prim(PrimitiveType.Cube, "Hint Marker", null, Vector3.zero, Vector3.one * 0.022f,
                Mat("Hint", new Color(1f, 0.85f, 0.2f), 0f, 0.5f, new Color(1.6f, 1.2f, 0.2f)), false);
            hintGo.transform.rotation = Quaternion.Euler(45f, 0f, 45f);
            var hint = hintGo.AddComponent<HintMarker>();

            var flowGo = new GameObject("Shift Flow");
            var flow = flowGo.AddComponent<ShiftFlow>();
            flow.hint = hint;
            flow.resultDial = p.resultDial;
            flow.stepTargets = new Transform[]
            {
                p.startButton.transform,      // Briefing
                p.lockLever.transform,        // Detach
                p.fork.transform,             // Dose
                p.tamper.transform,           // Tamp
                p.bayonet,                    // Lock
                p.brewButton.transform,       // Brew
                p.steamRest.transform,        // Steam
                p.pitcher.transform,          // Pour
                null,                         // Result
            };

            var driverGo = new GameObject("Desktop Rig Driver");
            var driver = driverGo.AddComponent<DesktopRigDriver>();
            driver.desktopActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(k_ActionsPath);
            driver.desktopHelp = p.desktopHelp;
            if (driver.desktopActions == null)
                Debug.LogWarning("Barista: " + k_ActionsPath + " did not import as an Input Action Asset.");
        }

        // ------------------------------------------------------------------ XR rig

        static void BuildRig()
        {
            var rigPrefab = FindPrefab("XR Interaction Setup") ?? FindPrefab("XR Origin (XR Rig)");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var origin = rig.GetComponentInChildren<XROrigin>(true);
            if (origin != null)
            {
                // Seated, device-referenced: the counter is placed relative to where the headset starts.
                origin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
                origin.CameraYOffset = k_Eye.y;
            }

            ConfigureComfortLocomotion(rig);

            if (Object.FindAnyObjectByType<XRInteractionManager>() == null)
                new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

            if (Object.FindAnyObjectByType<InputActionManager>() == null)
            {
                var xriActions = FindAsset<InputActionAsset>("XRI Default Input Actions");
                var iam = new GameObject("Input Action Manager").AddComponent<InputActionManager>();
                if (xriActions != null)
                    iam.actionAssets = new List<InputActionAsset> { xriActions };
            }

            var vignettePresent = rig.GetComponentsInChildren<MonoBehaviour>(true).Any(m => m != null && m.GetType().Name == "TunnelingVignetteController");
            var vignettePrefab = FindPrefab("TunnelingVignette");
            if (!vignettePresent && vignettePrefab != null && origin != null && origin.Camera != null)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(vignettePrefab, origin.Camera.transform);
                v.transform.localPosition = Vector3.zero;
                v.transform.localRotation = Quaternion.identity;
            }

            var simPrefab = FindPrefab("XR Device Simulator");
            if (simPrefab != null)
            {
                var sim = (GameObject)PrefabUtility.InstantiatePrefab(simPrefab);
                PrefabUtility.UnpackPrefabInstance(sim, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Editor-only: stripped from the Windows build, where the keyboard/mouse fallback takes over.
                sim.tag = "EditorOnly";
            }
            else
            {
                Debug.LogWarning("Barista: XR Device Simulator prefab not found. Import the 'XR Device Simulator' sample to test in the Editor.");
            }
        }

        /// <summary>
        /// Slow slide along the counter and 45 degree snap turn. No flying, no continuous turn.
        /// SessionControls also clamps the rig so the trainee stays behind the counter.
        /// </summary>
        static void ConfigureComfortLocomotion(GameObject rig)
        {
            foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                switch (mb.GetType().Name)
                {
                    case "ContinuousMoveProvider":
                    case "DynamicMoveProvider":
                        mb.enabled = true;
                        SetSerialized(mb, "m_MoveSpeed", 0.7f);
                        SetSerialized(mb, "m_EnableStrafe", true);
                        SetSerialized(mb, "m_EnableFly", false);
                        break;
                    case "ContinuousTurnProvider":
                    case "TeleportationProvider":
                    case "GrabMoveProvider":
                    case "TwoHandedGrabMoveProvider":
                    case "ClimbProvider":
                    case "ClimbTeleportInteractor":
                        mb.enabled = false;
                        break;
                    case "SnapTurnProvider":
                        mb.enabled = true;
                        SetSerialized(mb, "m_TurnAmount", 45f);
                        SetSerialized(mb, "m_EnableTurnAround", false);
                        break;
                    case "ControllerInputActionManager":
                        SetSerialized(mb, "m_SmoothMotionEnabled", true);
                        SetSerialized(mb, "m_SmoothTurnEnabled", false);
                        SetSerializedObject(mb, "m_TeleportInteractor", null);
                        SetSerializedObject(mb, "m_TeleportMode", null);
                        break;
                }
            }

            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("Teleport Interactor"))
                    t.gameObject.SetActive(false);
            }
        }

        static void SetSerialized(Object target, string property, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null)
                return;
            switch (value)
            {
                case float f: prop.floatValue = f; break;
                case bool b: prop.boolValue = b; break;
                case int i: prop.intValue = i; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetSerializedObject(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                return;
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ helpers

        static Quaternion Facing(Vector3 position) => Quaternion.LookRotation((position - k_Eye).normalized, Vector3.up);

        /// <summary>
        /// Held tools are driven by physics velocity so the counter, machine and other tools block them.
        /// What made that feel wild before is tamed here: capped depenetration (no popping out of
        /// overlaps), capped velocity changes (no flinging), more solver iterations, and a 90 Hz physics
        /// step (see ConfigurePhysics). Tools never fall freely when let go: see ToolSettle.
        /// </summary>
        static XRGrabInteractable MakeGrabbable(GameObject go, float mass, float baseHeight = 0f)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxDepenetrationVelocity = 0.25f;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 2f;
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = true;
            grab.velocityDamping = 1f;
            grab.velocityScale = 1f;
            grab.angularVelocityDamping = 1f;
            grab.angularVelocityScale = 1f;
            grab.limitLinearVelocity = true;
            grab.maxLinearVelocityDelta = 3f;
            grab.limitAngularVelocity = true;
            grab.maxAngularVelocityDelta = 12f;
            grab.smoothPosition = true;
            grab.smoothPositionAmount = 16f;
            grab.tightenPosition = 0.5f;
            grab.smoothRotation = true;
            grab.smoothRotationAmount = 14f;
            grab.tightenRotation = 0.5f;
            // Near-frictionless, so a tool slides along the counter or out of a socket instead of dragging.
            var slide = ToolSlideMaterial();
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                col.sharedMaterial = slide;
            go.AddComponent<ToolSettle>().baseHeight = baseHeight;
            return grab;
        }

        static PhysicsMaterial s_ToolSlide;

        static PhysicsMaterial ToolSlideMaterial()
        {
            if (s_ToolSlide != null)
                return s_ToolSlide;
            var path = $"{k_MatDir}/ToolSlide.physicMaterial";
            s_ToolSlide = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (s_ToolSlide == null)
            {
                s_ToolSlide = new PhysicsMaterial("ToolSlide");
                AssetDatabase.CreateAsset(s_ToolSlide, path);
            }
            s_ToolSlide.dynamicFriction = 0.05f;
            s_ToolSlide.staticFriction = 0.05f;
            s_ToolSlide.bounciness = 0f;
            s_ToolSlide.frictionCombine = PhysicsMaterialCombine.Minimum;
            s_ToolSlide.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(s_ToolSlide);
            return s_ToolSlide;
        }

        static void ConfigurePhysics()
        {
            // Match the headset refresh rate so held tools move every frame instead of every other one.
            Time.fixedDeltaTime = 1f / 90f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.defaultMaxDepenetrationVelocity = 0.25f;
            Physics.defaultContactOffset = 0.002f;
        }

        static ToolSocket Socket(string name, Transform parent, Vector3 position, SocketAccepts accepts, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = radius;
            var socket = go.AddComponent<ToolSocket>();
            socket.accepts = accepts;
            socket.recycleDelayTime = 0.5f;
            return socket;
        }

        static PressButton Button(string name, Transform parent, Vector3 localPosition, float diameter, Material mat, string label, float labelOffsetY = 0.045f, float letterHeight = 0.016f)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            var cap = Cyl("Cap", root.transform, Vector3.zero, diameter, 0.02f, mat);
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Label(root.transform, "Label", label, new Vector3(0f, labelOffsetY, -0.016f), letterHeight, Color.white, backdrop: true);
            root.AddComponent<XRSimpleInteractable>();
            var button = root.AddComponent<PressButton>();
            button.cap = cap.transform;
            return button;
        }

        static Transform Panel(string name, Transform parent, Vector3 position, Quaternion rotation, Vector2 size, Material mat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, rotation);
            Prim(PrimitiveType.Cube, "Backing", root, Vector3.zero, new Vector3(size.x, size.y, 0.006f), mat, false);
            return root;
        }

        static TextMesh Label(Transform parent, string name, string text, Vector3 localPosition, float letterHeight, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, TextAlignment alignment = TextAlignment.Center, bool backdrop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = s_Font;
            tm.fontSize = 96;
            tm.characterSize = letterHeight * 10f / tm.fontSize;
            tm.anchor = anchor;
            tm.alignment = alignment;
            tm.color = color;
            tm.richText = false;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
                mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TextMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            if (backdrop)
            {
                var plate = Prim(PrimitiveType.Cube, "Backdrop", go.transform, new Vector3(0f, 0f, 0.003f),
                    new Vector3(0.08f, 0.03f, 0.002f), LabelPlateMaterial(), false);
                var back = go.AddComponent<TextBackdrop>();
                back.plate = plate.transform;
                back.padding = Mathf.Max(0.006f, letterHeight * 0.55f);
            }

            return tm;
        }

        static Material LabelPlateMaterial()
        {
            const string name = "LabelPlate";
            if (s_Mats.TryGetValue(name, out var cached))
                return cached;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = s_Shader;
            var path = $"{k_MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            var ink = new Color(0.05f, 0.045f, 0.04f, 1f);
            mat.color = ink;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", ink);
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 0f);
            EditorUtility.SetDirty(mat);
            s_Mats[name] = mat;
            return mat;
        }

        static Material s_TextMat;

        static Material TextMaterial()
        {
            if (s_TextMat != null)
                return s_TextMat;

            var shader = Shader.Find("Barista/WorldText");
            if (shader == null)
                shader = s_Font.material.shader;
            var path = $"{k_MatDir}/WorldText.mat";
            s_TextMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (s_TextMat == null)
            {
                s_TextMat = new Material(shader);
                AssetDatabase.CreateAsset(s_TextMat, path);
            }
            else
            {
                s_TextMat.shader = shader;
            }

            s_TextMat.mainTexture = s_Font.material.mainTexture;
            if (s_TextMat.HasProperty("_Color"))
                s_TextMat.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(s_TextMat);
            return s_TextMat;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>Cylinder by diameter and height, with a box collider instead of Unity's capsule.</summary>
        static GameObject Cyl(string name, Transform parent, Vector3 localPosition, float diameter, float height, Material mat, bool collider = true)
        {
            var go = Prim(PrimitiveType.Cylinder, name, parent, localPosition, new Vector3(diameter, height * 0.5f, diameter), mat, false);
            if (collider)
                go.AddComponent<BoxCollider>();
            return go;
        }

        /// <summary>Invisible convex cylinder. Matches a round tool, so corners of a box collider cannot cut into a neighbour.</summary>
        static void SolidDisc(string name, Transform parent, Vector3 localPosition, float diameter, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Object.DestroyImmediate(go.GetComponent<Renderer>());
            var hit = go.AddComponent<MeshCollider>();
            hit.convex = true;
            hit.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        }

        /// <summary>Thin-walled open cylinder (basket, cup, pitcher) built from flat segments.</summary>
        static void Ring(Transform parent, float innerRadius, float y0, float height, int segments, float thickness, Material mat, bool colliders)
        {
            var width = 2f * (innerRadius + thickness) * Mathf.Tan(Mathf.PI / segments) + 0.002f;
            for (var i = 0; i < segments; i++)
            {
                var rot = Quaternion.Euler(0f, i * 360f / segments, 0f);
                var wall = Prim(PrimitiveType.Cube, "Wall " + i, parent,
                    rot * Vector3.forward * (innerRadius + thickness * 0.5f) + Vector3.up * (y0 + height * 0.5f),
                    new Vector3(width, height, thickness), mat, colliders);
                wall.transform.localRotation = rot;
            }
        }

        static Material Mat(string name, Color color, float metallic = 0f, float smoothness = 0.4f, Color? emission = null)
        {
            if (s_Mats.TryGetValue(name, out var cached))
                return cached;

            var path = $"{k_MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(s_Shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = s_Shader;
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            s_Mats[name] = mat;
            return mat;
        }

        static GameObject FindPrefab(string fileName) => FindAsset<GameObject>(fileName, "t:Prefab");

        static T FindAsset<T>(string fileName, string filter = null) where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets(filter ?? "t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == fileName)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                    if (asset != null)
                        return asset;
                }
            }
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void AddSceneToBuild()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != k_ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(k_ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
