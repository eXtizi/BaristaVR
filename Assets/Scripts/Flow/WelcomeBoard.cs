using UnityEngine;

namespace Barista
{
    /// <summary>Title and how-to-play board shown behind the machine until the trainee presses START.</summary>
    public class WelcomeBoard : MonoBehaviour
    {
        public GameObject board;

        ShiftFlow m_Flow;

        void Start()
        {
            m_Flow = ShiftFlow.Instance;
            m_Flow.StepChanged += Refresh;
            Refresh(m_Flow.Current);
        }

        void OnDestroy()
        {
            if (m_Flow != null)
                m_Flow.StepChanged -= Refresh;
        }

        void Refresh(Step step) => board.SetActive(step == Step.Briefing);
    }
}
