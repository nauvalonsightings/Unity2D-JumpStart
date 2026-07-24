using UnityEngine;

namespace Unity2DJumpStart
{
    /// <summary>
    /// Displays a configurable label above a GameObject while Gizmos are enabled.
    /// </summary>
    public sealed class GizmoGameObjectLabel : MonoBehaviour
    {
        [Header("Label Content")]
        [SerializeField] private bool showGameObjectName = true;
        [SerializeField] private string customMessage = "Custom Message";

        [Header("Label Appearance")]
        [SerializeField] private Color containerColor = new Color(0f, 0f, 0f, 0.75f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Vector3 positionOffset = new Vector3(0f, 0.5f, 0f);

#if UNITY_EDITOR
        private GUIStyle labelStyle;
        private Texture2D containerTexture;
        private Color cachedContainerColor;
        private Color cachedTextColor;
        private bool hasCachedColors;

        private void OnDrawGizmos()
        {
            if (!UnityEditor.Handles.ShouldRenderGizmos())
            {
                return;
            }

            string labelText = showGameObjectName ? gameObject.name : customMessage;
            if (string.IsNullOrEmpty(labelText))
            {
                return;
            }

            EnsureLabelStyle();
            UnityEditor.Handles.Label(transform.position + positionOffset, new GUIContent(labelText), labelStyle);
        }

        private void OnValidate()
        {
            RebuildLabelStyle();
        }

        private void OnDisable()
        {
            DestroyContainerTexture();
        }

        private void EnsureLabelStyle()
        {
            if (!hasCachedColors || cachedContainerColor != containerColor || cachedTextColor != textColor || labelStyle == null)
            {
                RebuildLabelStyle();
            }
        }

        private void RebuildLabelStyle()
        {
            DestroyContainerTexture();

            containerTexture = new Texture2D(1, 1)
            {
                name = "Gizmo GameObject Label Background",
                hideFlags = HideFlags.HideAndDontSave
            };
            containerTexture.SetPixel(0, 0, containerColor);
            containerTexture.Apply();

            labelStyle = new GUIStyle(UnityEditor.EditorStyles.label)
            {
                normal =
                {
                    background = containerTexture,
                    textColor = textColor
                },
                padding = new RectOffset(6, 6, 3, 3),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };

            cachedContainerColor = containerColor;
            cachedTextColor = textColor;
            hasCachedColors = true;
        }

        private void DestroyContainerTexture()
        {
            if (containerTexture != null)
            {
                DestroyImmediate(containerTexture);
                containerTexture = null;
            }
        }
#endif
    }
}
