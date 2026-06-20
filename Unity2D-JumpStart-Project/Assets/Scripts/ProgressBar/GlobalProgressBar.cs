using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Unity2DJumpStart
{
    public sealed class GlobalProgressBar : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private Slider slider;
        [SerializeField] private SpriteRenderer fillRenderer;
        [SerializeField] private Text legacyText;
        [SerializeField] private TMP_Text tmpText;

        [Header("Value Range")]
        [SerializeField] private float minValue = 0f;
        [SerializeField] private float maxValue = 100f;
        [SerializeField] private float startValue = 0f;

        [Header("Text Format")]
        [SerializeField] private bool showCurrentAndMax = true;
        [SerializeField] private string valueFormat = "{0:0}/{1:0}";
        [SerializeField] private string percentageFormat = "{0:0}%";

        private Coroutine _changeRoutine;
        private float _currentValue;
        private bool _isInitialized;
        private Vector3 _fillOriginalScale;
        private bool _hasFillOriginalScale;

        private void OnEnable()
        {
            StopAllCoroutines();
            _changeRoutine = null;

            InitializeIfNeeded();
            ApplyValue(_currentValue);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            _changeRoutine = null;
        }

        public void ChangeValue(float value)
        {
            ChangeValue(value, 0f, null);
        }

        public void ChangeValue(float value, float duration)
        {
            ChangeValue(value, duration, null);
        }

        public void ChangeValue(float value, Action onFull)
        {
            ChangeValue(value, 0f, onFull);
        }

        public void ChangeValue(float value, float duration, Action onFull)
        {
            InitializeIfNeeded();

            float clampedValue = Mathf.Clamp(value, minValue, maxValue);

            if (_changeRoutine != null)
            {
                StopCoroutine(_changeRoutine);
                _changeRoutine = null;
            }

            if (duration <= 0f)
            {
                ApplyValue(clampedValue);
                InvokeFullIfNeeded(clampedValue, onFull);
                return;
            }

            _changeRoutine = StartCoroutine(ChangeValueRoutine(_currentValue, clampedValue, duration, onFull));
        }

        public void SetValueImmediate(float value)
        {
            InitializeIfNeeded();
            ApplyValue(Mathf.Clamp(value, minValue, maxValue));
        }

        public void Initialize(float maxValue)
        {
            Initialize(0f, maxValue, 0f);
        }

        public void Initialize(float minValue, float maxValue)
        {
            Initialize(minValue, maxValue, minValue);
        }

        public void Initialize(float minValue, float maxValue, float startValue)
        {
            this.minValue = minValue;
            this.maxValue = maxValue;
            this.startValue = startValue;
            _isInitialized = false;
            InitializeIfNeeded();
            ApplyValue(_currentValue);
        }

        private void InitializeIfNeeded()
        {
            if (_isInitialized)
            {
                return;
            }

            if (slider != null)
            {
                slider.minValue = minValue;
                slider.maxValue = maxValue;
            }

            CacheFillScale();
            _currentValue = Mathf.Clamp(startValue, minValue, maxValue);
            _isInitialized = true;
        }

        private System.Collections.IEnumerator ChangeValueRoutine(float from, float to, float duration, Action onFull)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = EaseOutSine(t);
                float value = Mathf.LerpUnclamped(from, to, easedT);
                ApplyValue(value);
                yield return null;
            }

            ApplyValue(to);
            InvokeFullIfNeeded(to, onFull);
            _changeRoutine = null;
        }

        private void ApplyValue(float value)
        {
            _currentValue = value;

            if (slider != null)
            {
                slider.value = value;
            }

            ApplyFillRenderer(value);
            UpdateText(value);
        }

        private void ApplyFillRenderer(float value)
        {
            if (fillRenderer == null)
            {
                return;
            }

            float normalized = maxValue > minValue ? Mathf.InverseLerp(minValue, maxValue, value) : 0f;
            Vector3 scale = _hasFillOriginalScale ? _fillOriginalScale : fillRenderer.transform.localScale;
            scale.x = (_hasFillOriginalScale ? _fillOriginalScale.x : 1f) * normalized;
            fillRenderer.transform.localScale = scale;
        }

        private void CacheFillScale()
        {
            if (fillRenderer == null || _hasFillOriginalScale)
            {
                return;
            }

            _fillOriginalScale = fillRenderer.transform.localScale;
            _hasFillOriginalScale = true;
        }

        private void UpdateText(float value)
        {
            string textValue;

            if (showCurrentAndMax)
            {
                textValue = string.Format(valueFormat, value, maxValue);
            }
            else
            {
                float normalized = maxValue > minValue ? Mathf.InverseLerp(minValue, maxValue, value) * 100f : 0f;
                textValue = string.Format(percentageFormat, normalized);
            }

            if (tmpText != null)
            {
                tmpText.text = textValue;
                return;
            }

            if (legacyText != null)
            {
                legacyText.text = textValue;
            }
        }

        private void InvokeFullIfNeeded(float value, Action onFull)
        {
            if (onFull == null)
            {
                return;
            }

            if (value >= maxValue)
            {
                onFull.Invoke();
            }
        }

        private static float EaseOutSine(float t)
        {
            return Mathf.Sin(t * 1.57079637f);
        }
    }
}
