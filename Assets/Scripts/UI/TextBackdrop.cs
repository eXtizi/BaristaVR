using UnityEngine;

namespace Barista
{
    /// <summary>
    /// Flat card kept tight behind a TextMesh. Floating captions sit on steel and pale walls,
    /// so the card is what keeps them readable when the letter colour is close to the room.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class TextBackdrop : MonoBehaviour
    {
        public Transform plate;
        public float padding = 0.008f;

        TextMesh m_Text;
        string m_Last;

        void Awake() => m_Text = GetComponent<TextMesh>();

        void LateUpdate()
        {
            if (m_Text == null || plate == null || m_Text.text == m_Last)
                return;
            if (Fit())
                m_Last = m_Text.text;
        }

        bool Fit()
        {
            var filter = m_Text.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (string.IsNullOrEmpty(m_Text.text))
            {
                plate.gameObject.SetActive(false);
                return true;
            }

            if (mesh == null || mesh.vertexCount == 0)
            {
                plate.gameObject.SetActive(false);
                return false;
            }

            plate.gameObject.SetActive(true);
            var bounds = mesh.bounds;
            plate.localRotation = Quaternion.identity;
            plate.localPosition = new Vector3(bounds.center.x, bounds.center.y, 0.0025f);
            plate.localScale = new Vector3(
                Mathf.Max(0.02f, bounds.size.x + padding * 2f),
                Mathf.Max(0.014f, bounds.size.y + padding * 2f),
                0.002f);
            return true;
        }
    }
}
