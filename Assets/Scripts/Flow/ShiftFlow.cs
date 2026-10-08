using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Barista
{
    public enum Step
    {
        Briefing,
        Detach,
        Dose,
        Tamp,
        Lock,
        Brew,
        Steam,
        Pour,
        Result,
    }

    /// <summary>
    /// Owns the single order the trainee works through. Stations report completion with
    /// <see cref="Complete"/>; only the current step can complete, so each step unlocks the next.
    /// </summary>
    public class ShiftFlow : MonoBehaviour
    {
        public static ShiftFlow Instance { get; private set; }
        public static int Attempt { get; private set; }

        [Tooltip("Where the hint marker points for each step, indexed by Step.")]
        public Transform[] stepTargets = new Transform[9];
        public HintMarker hint;
        public ResultDial resultDial;

        public Step Current { get; private set; }
        public event Action<Step> StepChanged;

        public ShotResult Shot { get; private set; }
        public bool HasShot { get; private set; }
        public float MilkTempC { get; private set; }
        public float CupFill { get; private set; }

        public string Note => Time.time < m_NoteUntil ? m_Note : string.Empty;

        string m_Note = string.Empty;
        float m_NoteUntil;

        static readonly string[] k_Instructions =
        {
            "Press the green START button on the board above the machine.",
            "Grab the black handle under the machine. Swing it to your LEFT. The metal basket comes off in your hand.",
            "Put that basket into the two prongs on the black grinder, then hold the green button until the screen says about 18 g.",
            "Set the basket on the black mat. Pick up the brown tamper, press to about 15 kg, and hold until the card fills. A quick touch does not count.",
            "Put the basket back under the machine and let go. Then swing the handle to your RIGHT until it locks.",
            "Put the white cup on the dark ring under the spouts, then press the red BREW button. A good shot ends between 25 and 30 seconds, with the needle on 9.",
            "Put the silver jug on the black ring under the thin pipe. Press STEAM, and press it again when the jug says 65.",
            "Pick up the jug and tip it near the white cup. It moves sideways so you can see the stream. Pour until full; on a keyboard, hold Space.",
            "Read the board on your right. It says if the coffee was sour, bitter, or just right. Press PULL ANOTHER SHOT to try again.",
        };

        public static string Instruction(Step s) => k_Instructions[(int)s];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Attempt = 0;

        void Awake()
        {
            Instance = this;
            Attempt++;
            Current = Attempt > 1 ? Step.Detach : Step.Briefing;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Start() => Enter(Current);

        public bool IsAt(Step s) => Current == s;

        public void Complete(Step s)
        {
            if (s != Current || s == Step.Result)
                return;
            AudioKit.PlayAt(Sfx.Chime, hint != null ? hint.transform.position : transform.position, 0.35f);
            Enter(s + 1);
        }

        void Enter(Step s)
        {
            Current = s;
            if (hint != null)
            {
                var target = stepTargets != null && (int)s < stepTargets.Length ? stepTargets[(int)s] : null;
                if (s == Step.Result && resultDial != null)
                    target = resultDial.transform;
                hint.Follow(target, Coach(s, Instruction(s)));
            }
            StepChanged?.Invoke(s);
            if (s == Step.Result && resultDial != null)
                resultDial.Show(Shot, MilkTempC, CupFill, Attempt);
        }

        /// <summary>Move the speech bubble onto the next thing to touch, with a plain-language line.</summary>
        public void PointAt(Transform target, string line)
        {
            if (hint != null)
                hint.Follow(target, Coach(Current, line));
        }

        static string Coach(Step s, string line) => $"{(int)s + 1} of 9\n{line}";

        public void Say(string text, float seconds = 5f)
        {
            m_Note = text;
            m_NoteUntil = Time.time + seconds;
        }

        public void RecordShot(ShotResult result)
        {
            Shot = result;
            HasShot = true;
        }

        public void RecordMilk(float tempC) => MilkTempC = tempC;

        public void RecordPour(float cupFill) => CupFill = cupFill;

        public static void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
