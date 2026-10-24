using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Light))]
public class AnimateDirectionalCookieOffset : MonoBehaviour
{
    [SerializeField] private Vector2 amplitude = new Vector2(5f, 0f);
    [SerializeField] private Vector2 cyclesPerSecond = new Vector2(0.02f, 0f);

    private UniversalAdditionalLightData lightData;
    private Vector2 baseOffset;

    private void Awake()
    {
        lightData = GetComponent<UniversalAdditionalLightData>();

        if (lightData == null)
        {
            Debug.LogError("Universal Additional Light Data が見つかりません。", this);
            enabled = false;
            return;
        }

        // 再生開始時点のOffsetを中心にして動かします
        baseOffset = lightData.lightCookieOffset;
    }

    private void Update()
    {
        float t = Time.time;

        lightData.lightCookieOffset = baseOffset + new Vector2(
            amplitude.x * Mathf.Sin(2f * Mathf.PI * cyclesPerSecond.x * t),
            amplitude.y * Mathf.Sin(2f * Mathf.PI * cyclesPerSecond.y * t)
        );
    }
}