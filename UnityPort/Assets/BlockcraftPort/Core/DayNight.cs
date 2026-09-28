using UnityEngine;

namespace BlockcraftPort
{
    public sealed class DayNight : MonoBehaviour
    {
        public static float NightFactor { get; private set; }
        public static float DayAmount { get; private set; } = 1f;
        public static float SunsetAmount { get; private set; }
        public static float WorldLight { get; private set; } = 1f;
        public static Color SkyColor { get; private set; } = new Color(.47f, .71f, 1f, 1f);
        public static float DayTime01 { get; private set; } = .25f;

        public Light Sun;
        public float DayLength = 1200f; // main.js Wl
        float t = .25f;

        public void SetTime01(float value)
        {
            t=Mathf.Repeat(value,1f);
            DayTime01=t;
        }

        void Update()
        {
            t = (t + Time.deltaTime / Mathf.Max(1f, DayLength)) % 1f;
            DayTime01 = t;

            float wave = Mathf.Sin(t * Mathf.PI * 2f);
            float day = Mathf.Clamp01((wave + .14f) / .24f);
            float light = .22f + .78f * day;
            Color sky = new Color(
                .02f + (.47f - .02f) * day,
                .03f + (.71f - .03f) * day,
                .08f + (1f - .08f) * day,
                1f);
            float sunset = Mathf.Max(0f, 1f - Mathf.Abs(wave) * 4f) * day;
            if (sunset > 0f)
            {
                sky.r = Mathf.Lerp(sky.r, .93f, sunset * .55f);
                sky.g = Mathf.Lerp(sky.g, .49f, sunset * .55f);
                sky.b = Mathf.Lerp(sky.b, .27f, sunset * .55f);
            }

            DayAmount = day;
            SunsetAmount = sunset;
            WorldLight = light;
            SkyColor = sky;
            NightFactor = 1f - day;

            if (Sun != null)
            {
                float angle = t * 360f - 90f;
                Sun.transform.rotation = Quaternion.Euler(angle, 35f, 0f);
                Sun.intensity = .12f + day * .9f;
            }
            RenderSettings.ambientLight = Color.Lerp(new Color(.08f, .1f, .16f), new Color(.68f, .72f, .78f), day);
        }
    }
}
