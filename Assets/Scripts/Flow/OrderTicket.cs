using System.Text;
using UnityEngine;

namespace Barista
{
    /// <summary>
    /// The paper order ticket clipped to the rail. It is the only instruction source: the order,
    /// the house recipe, a live checklist, and what to do right now.
    /// </summary>
    public class OrderTicket : MonoBehaviour
    {
        public TextMesh checklist;
        public TextMesh nowLine;
        public PressButton startButton;
        public int wrapChars = 34;

        static readonly string[] k_Checklist =
        {
            "Read the order",
            "Unlock the portafilter",
            "Dose 18 g of grounds",
            "Tamp level and firm",
            "Lock into the group head",
            "Pull the double shot",
            "Steam milk to 65°C",
            "Pour the flat white",
            "Review your shot",
        };

        ShiftFlow m_Flow;

        void Start()
        {
            m_Flow = ShiftFlow.Instance;
            m_Flow.StepChanged += Refresh;
            if (startButton != null)
                startButton.Pressed += _ => m_Flow.Complete(Step.Briefing);
            Refresh(m_Flow.Current);
        }

        void OnDestroy()
        {
            if (m_Flow != null)
                m_Flow.StepChanged -= Refresh;
        }

        void Refresh(Step current)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ShiftFlow.Attempt > 1 ? $"ORDER #042   (shot {ShiftFlow.Attempt})" : "ORDER #042");
            sb.AppendLine("DOUBLE SHOT FLAT WHITE");
            sb.AppendLine("House recipe: 18 g in, 36 g out,");
            sb.AppendLine("25-30 s at about 9 bar. Milk 65°C.");
            sb.AppendLine("--------------------------------");
            for (var i = 0; i < k_Checklist.Length; i++)
            {
                var mark = i < (int)current ? "[x]" : i == (int)current ? "[>]" : "[  ]";
                sb.AppendLine($"{mark} {k_Checklist[i]}");
            }
            checklist.text = sb.ToString();

            if (startButton != null)
                startButton.gameObject.SetActive(current == Step.Briefing);
        }

        void Update()
        {
            var text = "NOW: " + ShiftFlow.Instruction(m_Flow.Current);
            var note = m_Flow.Note;
            if (!string.IsNullOrEmpty(note))
                text += "\n\n" + note;
            nowLine.text = TextUtil.Wrap(text, wrapChars);
        }
    }
}
