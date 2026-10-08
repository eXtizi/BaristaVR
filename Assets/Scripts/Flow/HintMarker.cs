using System.Collections.Generic;
using UnityEngine;

namespace Barista
{
    /// <summary>
    /// A coach sign for the current step, with an arrow onto the thing to touch.
    /// The sign is placed once, at a fixed reading distance on the line from the trainee's eyes to
    /// the target, then nudged to whichever side keeps the target and the step's readouts (gauge,
    /// dose screen, tamp card, thermometer, cup) uncovered. It then stays put in the room. It is
    /// only re-placed when the step's target changes, or if the trainee has turned so the sign has
    /// been out of view for a while although the target is in view.
    /// </summary>
    public class HintMarker : MonoBehaviour
    {
        [Tooltip("Distance from the eyes to the sign. Close enough to read, never inside the machine.")]
        public float readingDistance = 0.62f;
        public float signWidth = 0.3f;
        [Tooltip("Seconds the sign may be out of view, with the target in view, before it is moved back.")]
        public float lostSeconds = 1.5f;

        Transform m_Target;
        Transform m_Placed;
        string m_Line = "";
        string m_Shown;
        float m_Lost;
        readonly List<Transform> m_KeepClear = new List<Transform>();
        bool m_KeepClearFound;
        Portafilter m_Portafilter;
        GroupHead m_GroupHead;
        PressButton m_BrewButton;
        ResultDial m_Result;
        Transform m_WandTip;

        Transform m_Bubble;
        Transform m_Shaft;
        Transform m_Head;
        TextMesh m_Text;
        MeshRenderer m_Panel;

        // Offsets from the line of sight, in metres on the sign's plane, tried in order.
        static readonly Vector2[] k_Slots =
        {
            new Vector2(0f, 0.15f),
            new Vector2(-0.22f, 0.1f),
            new Vector2(0.22f, 0.1f),
            new Vector2(-0.26f, -0.02f),
            new Vector2(0.26f, -0.02f),
            new Vector2(0f, -0.15f),
        };

        // Used only when a normal slot would leave the dose, brew, or result card unreadable.
        // Dose always goes on the left, clear of the machine, the group head, and the BREW button.
        static readonly Vector2[] k_DoseLeft = { new Vector2(-0.3f, 0.06f), new Vector2(-0.32f, -0.06f), new Vector2(-0.34f, 0.18f), new Vector2(-0.22f, 0.2f) };
        // Brew sits further right so the steam pipe does not cut the left of the words.
        static readonly Vector2[] k_BrewRight = { new Vector2(0.55f, 0.04f), new Vector2(0.64f, 0.12f), new Vector2(0.5f, -0.1f) };
        // The "shot done" card is the steam sign. It sits closer than the group head, above or below it.
        static readonly Vector2[] k_SteamFront = { new Vector2(0f, 0.22f), new Vector2(0f, -0.2f), new Vector2(0.14f, 0.2f), new Vector2(-0.14f, 0.2f) };
        static readonly Vector2[] k_ResultClear = { new Vector2(-0.42f, 0.14f), new Vector2(-0.36f, 0.3f), new Vector2(0.42f, 0.14f) };

        public void Follow(Transform target, string line)
        {
            m_Target = target;
            if (!string.IsNullOrEmpty(line))
                m_Line = line;
            gameObject.SetActive(target != null);
            if (m_Bubble != null)
                m_Bubble.gameObject.SetActive(target != null);
        }

        void Awake() => BuildBubble();

        void LateUpdate()
        {
            if (m_Target == null || m_Bubble == null)
                return;

            var flow = ShiftFlow.Instance;
            var note = flow != null ? flow.Note : "";
            var body = string.IsNullOrEmpty(note) ? m_Line : note;
            if (body != m_Shown)
            {
                m_Shown = body;
                m_Text.text = TextUtil.Wrap(body, 30);
                m_Text.color = string.IsNullOrEmpty(note)
                    ? new Color(0.16f, 0.1f, 0.06f)
                    : new Color(0.55f, 0.12f, 0.08f);
                FitPanel();
            }

            var cam = Camera.main;
            if (cam == null)
                return;

            if (!m_KeepClearFound)
                FindKeepClear();

            if (m_Placed != m_Target)
            {
                PlaceSign(cam);
            }
            else
            {
                var signSeen = InView(cam, m_Bubble.position, 0.02f);
                var targetSeen = InView(cam, m_Target.position, 0.1f);
                m_Lost = !signSeen && targetSeen ? m_Lost + Time.deltaTime : 0f;
                if (m_Lost > lostSeconds)
                    PlaceSign(cam);
            }

            UpdateArrow();
        }

        /// <summary>True when <paramref name="point"/> lands on the sign from the camera, even if it is in front of the card.</summary>
        static bool Overlaps(Camera cam, Vector3 pos, Quaternion rotation, float halfW, float halfH, Vector3 point)
        {
            var centre = cam.WorldToViewportPoint(pos);
            var sample = cam.WorldToViewportPoint(point);
            if (centre.z <= 0f || sample.z <= 0f)
                return false;
            var edge = cam.WorldToViewportPoint(pos + rotation * Vector3.right * halfW);
            var top = cam.WorldToViewportPoint(pos + rotation * Vector3.up * halfH);
            return Mathf.Abs(sample.x - centre.x) < Mathf.Abs(edge.x - centre.x)
                && Mathf.Abs(sample.y - centre.y) < Mathf.Abs(top.y - centre.y);
        }

        void PlaceSign(Camera cam)
        {
            m_Placed = m_Target;
            m_Lost = 0f;

            var eye = cam.transform.position;
            var toTarget = m_Target.position - eye;
            var flow = ShiftFlow.Instance;
            var distance = Mathf.Clamp(Mathf.Min(readingDistance, toTarget.magnitude * 0.8f), 0.4f, readingDistance);
            // In front of the group head, which sticks out and was cutting the shot-done card.
            if (flow != null && flow.IsAt(Step.Steam))
                distance = Mathf.Min(distance, 0.42f);
            var forward = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : cam.transform.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            right = right.sqrMagnitude > 0.0001f ? right.normalized : cam.transform.right;
            var up = Vector3.Cross(forward, right);
            var centre = eye + forward * distance;

            var halfW = signWidth * 0.5f + 0.015f;
            var halfH = m_Panel.transform.localScale.y * 0.5f + 0.015f;

            var slots = k_Slots;
            Vector2[] extra = null;
            if (flow != null)
            {
                if (flow.IsAt(Step.Dose)) slots = k_DoseLeft;
                else if (flow.IsAt(Step.Brew)) slots = k_BrewRight;
                else if (flow.IsAt(Step.Steam)) slots = k_SteamFront;
                else if (flow.IsAt(Step.Result)) extra = k_ResultClear;
            }

            var best = centre + up * k_Slots[0].y;
            var bestScore = int.MaxValue;
            void Consider(Vector2 slot)
            {
                var pos = centre + right * slot.x + up * slot.y;
                var rotation = Quaternion.LookRotation(pos - eye, Vector3.up);
                var score = ScoreSlot(cam, eye, pos, rotation, halfW, halfH, flow);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = pos;
                }
            }

            foreach (var slot in slots)
            {
                Consider(slot);
                if (bestScore == 0)
                    break;
            }
            if (bestScore != 0 && extra != null)
            {
                foreach (var slot in extra)
                {
                    Consider(slot);
                    if (bestScore == 0)
                        break;
                }
            }

            // TextMesh faces its local -Z: point that side back at the eyes.
            m_Bubble.SetPositionAndRotation(best, Quaternion.LookRotation(best - eye, Vector3.up));
        }

        int ScoreSlot(Camera cam, Vector3 eye, Vector3 pos, Quaternion rotation, float halfW, float halfH, ShiftFlow flow)
        {
            var score = 0;
            if (Covers(eye, pos, rotation, halfW, halfH, m_Target.position))
                score += 100;

            // These three cards are the ones geometry cuts or hides. Other steps keep their slots.
            if (flow != null && flow.IsAt(Step.Dose))
            {
                if (m_BrewButton != null && HitsDisc(cam, pos, rotation, halfW, halfH, m_BrewButton.transform.position, 0.03f))
                    score += 100;
                if (m_GroupHead != null && HitsDisc(cam, pos, rotation, halfW, halfH, m_GroupHead.transform.position, 0.05f))
                    score += 100;
                score += 30 * Blocked(eye, pos, rotation, halfW, halfH);
            }
            if (flow != null && flow.IsAt(Step.Brew))
            {
                if (m_GroupHead != null
                    && HitsDisc(cam, pos, rotation, halfW, halfH, m_GroupHead.transform.position, 0.05f))
                    score += 100;
                if (m_Portafilter != null)
                {
                    var pf = m_Portafilter.transform;
                    if (Overlaps(cam, pos, rotation, halfW, halfH, pf.TransformPoint(0f, -0.008f, -0.11f))
                        || Overlaps(cam, pos, rotation, halfW, halfH, pf.TransformPoint(0f, -0.008f, -0.16f))
                        || Overlaps(cam, pos, rotation, halfW, halfH, pf.position))
                        score += 100;
                }
                if (WandHits(cam, pos, rotation, halfW, halfH))
                    score += 100;
                score += 30 * Blocked(eye, pos, rotation, halfW, halfH);
            }
            if (flow != null && flow.IsAt(Step.Steam))
            {
                if (m_GroupHead != null && HitsDisc(cam, pos, rotation, halfW, halfH, m_GroupHead.transform.position, 0.05f))
                    score += 100;
                score += 30 * Blocked(eye, pos, rotation, halfW, halfH);
            }
            if (flow != null && flow.IsAt(Step.Result))
            {
                var dial = m_Result != null && m_Result.needlePivot != null
                    ? m_Result.needlePivot.position
                    : m_Target.position + Vector3.up * 0.17f;
                if (HitsDisc(cam, pos, rotation, halfW, halfH, dial, 0.08f))
                    score += 100;
            }

            foreach (var keep in m_KeepClear)
                if (keep != null && keep.gameObject.activeInHierarchy && Covers(eye, pos, rotation, halfW, halfH, keep.position))
                    score += 10;
            if (!InView(cam, pos, 0.04f))
                score += 5;
            return score;
        }

        /// <summary>How many of the sign's corners and centre have solid scenery between them and the eyes.</summary>
        static int Blocked(Vector3 eye, Vector3 pos, Quaternion rotation, float halfW, float halfH)
        {
            var count = 0;
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                var point = pos + rotation * new Vector3(x * halfW, y * halfH, 0f);
                var ray = point - eye;
                foreach (var hit in Physics.RaycastAll(eye, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                        continue;
                    count++;
                    break;
                }
            }
            return count;
        }

        bool WandHits(Camera cam, Vector3 pos, Quaternion rotation, float halfW, float halfH)
        {
            if (m_WandTip == null)
                return false;
            var tip = m_WandTip.position;
            return HitsDisc(cam, pos, rotation, halfW, halfH, tip, 0.02f)
                || HitsDisc(cam, pos, rotation, halfW, halfH, tip + Vector3.up * 0.14f, 0.02f)
                || HitsDisc(cam, pos, rotation, halfW, halfH, tip + Vector3.up * 0.28f, 0.02f);
        }

        static bool HitsDisc(Camera cam, Vector3 pos, Quaternion rotation, float halfW, float halfH, Vector3 center, float radius)
        {
            if (Overlaps(cam, pos, rotation, halfW, halfH, center))
                return true;
            return Overlaps(cam, pos, rotation, halfW, halfH, center + Vector3.right * radius)
                || Overlaps(cam, pos, rotation, halfW, halfH, center + Vector3.left * radius)
                || Overlaps(cam, pos, rotation, halfW, halfH, center + Vector3.up * radius)
                || Overlaps(cam, pos, rotation, halfW, halfH, center + Vector3.down * radius);
        }

        /// <summary>Would a sign at <paramref name="pos"/> hide <paramref name="point"/> from the eyes?</summary>
        static bool Covers(Vector3 eye, Vector3 pos, Quaternion rotation, float halfW, float halfH, Vector3 point)
        {
            var normal = rotation * Vector3.forward;
            var ray = point - eye;
            var denom = Vector3.Dot(ray, normal);
            if (denom <= 0.0001f)
                return false;
            var t = Vector3.Dot(pos - eye, normal) / denom;
            if (t <= 0f || t >= 1f)
                return false;
            var local = Quaternion.Inverse(rotation) * (eye + ray * t - pos);
            return Mathf.Abs(local.x) < halfW && Mathf.Abs(local.y) < halfH;
        }

        static bool InView(Camera cam, Vector3 point, float margin)
        {
            var v = cam.WorldToViewportPoint(point);
            return v.z > 0f && v.x > margin && v.x < 1f - margin && v.y > margin && v.y < 1f - margin;
        }

        void UpdateArrow()
        {
            var tip = m_Target.position + Vector3.up * (0.035f + Mathf.Sin(Time.time * 3f) * 0.005f);
            // Start the arrow on the sign's edge nearest the target.
            var local = Quaternion.Inverse(m_Bubble.rotation) * (tip - m_Bubble.position);
            var halfW = signWidth * 0.5f;
            var halfH = m_Panel.transform.localScale.y * 0.5f;
            var flat = new Vector2(local.x, local.y);
            Vector3 from;
            if (flat.sqrMagnitude < 0.000001f)
            {
                from = m_Bubble.position;
            }
            else
            {
                var scale = Mathf.Min(
                    Mathf.Abs(flat.x) > 0.0001f ? halfW / Mathf.Abs(flat.x) : float.MaxValue,
                    Mathf.Abs(flat.y) > 0.0001f ? halfH / Mathf.Abs(flat.y) : float.MaxValue);
                scale = Mathf.Min(scale, 1f);
                from = m_Bubble.position + m_Bubble.rotation * new Vector3(flat.x * scale, flat.y * scale, 0f);
            }

            var span = tip - from;
            var length = span.magnitude;
            var show = length > 0.03f;
            m_Shaft.gameObject.SetActive(show);
            m_Head.gameObject.SetActive(show);
            if (!show)
                return;

            m_Shaft.position = from + span * 0.5f;
            m_Shaft.rotation = Quaternion.FromToRotation(Vector3.up, span);
            m_Shaft.localScale = new Vector3(0.004f, length * 0.5f, 0.004f);
            m_Head.position = tip;
            m_Head.rotation = Quaternion.LookRotation(span, Vector3.up);
        }

        void FindKeepClear()
        {
            m_KeepClearFound = true;
            m_KeepClear.Clear();
            m_Portafilter = FindAnyObjectByType<Portafilter>();
            m_GroupHead = FindAnyObjectByType<GroupHead>();
            m_Result = FindAnyObjectByType<ResultDial>();
            var extraction = FindAnyObjectByType<ExtractionModel>();
            m_BrewButton = extraction != null ? extraction.brewButton : null;
            var wand = FindAnyObjectByType<SteamWand>();
            m_WandTip = wand != null ? wand.tip : null;
            foreach (var g in FindObjectsByType<MachineGauge>())
                m_KeepClear.Add(g.transform);
            foreach (var g in FindObjectsByType<GrinderStation>())
                if (g.display != null)
                    m_KeepClear.Add(g.display.transform);
            foreach (var t in FindObjectsByType<TamperPress>())
                if (t.readout != null)
                    m_KeepClear.Add(t.readout.transform);
            foreach (var p in FindObjectsByType<MilkPitcher>())
            {
                m_KeepClear.Add(p.transform);
                if (p.thermometer != null)
                    m_KeepClear.Add(p.thermometer.transform);
            }
            foreach (var c in FindObjectsByType<MilkPour>())
                m_KeepClear.Add(c.transform);
            foreach (var r in FindObjectsByType<ResultDial>())
                m_KeepClear.Add(r.transform);
        }

        void FitPanel()
        {
            var lines = 1;
            foreach (var c in m_Text.text)
                if (c == '\n')
                    lines++;
            var h = 0.022f + lines * 0.022f;
            m_Panel.transform.localScale = new Vector3(signWidth, h, 0.004f);
            m_Text.transform.localPosition = new Vector3(0f, 0f, -0.004f);
        }

        void BuildBubble()
        {
            var ink = Unlit(new Color(0.98f, 0.96f, 0.9f, 1f));
            var arrow = Unlit(new Color(0.95f, 0.72f, 0.15f, 1f));

            var bubble = new GameObject("Coach Sign");
            m_Bubble = bubble.transform;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(m_Bubble, false);
            Destroy(panel.GetComponent<Collider>());
            m_Panel = panel.GetComponent<MeshRenderer>();
            m_Panel.sharedMaterial = ink;
            m_Panel.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var textGo = new GameObject("Words");
            textGo.transform.SetParent(m_Bubble, false);
            m_Text = textGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Text.font = font;
            m_Text.fontSize = 64;
            m_Text.characterSize = 0.013f * 10f / 64f;
            m_Text.anchor = TextAnchor.MiddleCenter;
            m_Text.alignment = TextAlignment.Center;
            m_Text.color = new Color(0.16f, 0.1f, 0.06f);
            m_Text.richText = false;
            var renderer = textGo.GetComponent<MeshRenderer>();
            var textShader = Shader.Find("Barista/WorldText");
            if (textShader != null && font != null)
            {
                var mat = new Material(textShader);
                mat.mainTexture = font.material.mainTexture;
                renderer.sharedMaterial = mat;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            m_Shaft = ArrowPiece("Coach Shaft", arrow);
            m_Head = ArrowPiece("Coach Head", arrow);
            m_Head.localScale = new Vector3(0.014f, 0.014f, 0.024f);
            FitPanel();
        }

        static Transform ArrowPiece(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static Material Unlit(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            mat.color = color;
            return mat;
        }
    }
}
