using UnityEngine;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class PitchDetector : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI frequencyText;

    [Header("Settings")]
    [Tooltip("Higher = faster reaction, but more jitter. Lower = smoother.")]
    public float smoothing = 10f;
    [Tooltip("How long to hold the last note after sound stops (s).")]
    public float holdDuration = 1.5f;

    [Header("Voice Band Filter")]
    [Tooltip("High-pass cutoff (Hz)")]
    public float hpCutoff = 80f;
    [Tooltip("Low-pass cutoff (Hz) — raise to 2000 if whistle reads low")]
    public float lpCutoff = 1500f;

    [Header("Adaptive Gate")]
    [Tooltip("How much louder than background before detecting (1.5–2 typical)")]
    public float gateRatio = 1.8f;
    [Tooltip("How fast the noise floor adapts (0.005–0.03)")]
    public float noiseAdapt = 0.01f;

    // Frequency limits for human voice/whistle
    private const float minFrequency = 50f;
    private const float maxFrequency = 1000f;

    // Peak selection params
    private const float peakNormThreshold = 0.3f; // first peak needs >= 30% of corr[0]
    private const float clipFrac = 0.3f;          // center-clipping at 30% of peak amplitude

    private AudioSource audioSource;
    private string micName;
    private int sampleRate;

    // Buffers
    private float[] audioBuffer = new float[2048];
    private float[] procBuffer;   // clipped/processed copy
    private float[] corrBuffer;   // autocorrelation

    // Filter + gate states
    private float hpState = 0f;
    private float lpState = 0f;
    private float noiseFloor = 0.01f;

    // Display state
    private float displayedFrequency = 0f;
    private float silenceTimer = 0f;
    private string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    void Start()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone detected!");
            return;
        }

        micName = Microphone.devices[0];
        sampleRate = AudioSettings.outputSampleRate;

        if (!TryGetComponent(out audioSource))
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.clip = Microphone.Start(micName, true, 1, sampleRate);
        audioSource.loop = true;
        while (Microphone.GetPosition(micName) <= 0) { }
        audioSource.Play();

        procBuffer = new float[audioBuffer.Length];
        corrBuffer = new float[audioBuffer.Length / 2];
    }

    void Update()
    {
        if (audioSource == null || !audioSource.isPlaying) return;

        audioSource.GetOutputData(audioBuffer, 0);

        // ---------- Step 1: Band-pass filter (voice range) ----------
        float hpRC = 1f / (2f * Mathf.PI * hpCutoff);
        float lpRC = 1f / (2f * Mathf.PI * lpCutoff);
        float dt = 1f / sampleRate;
        float hpAlpha = hpRC / (hpRC + dt);
        float lpAlpha = dt / (lpRC + dt);

        float energy = 0f;
        for (int i = 0; i < audioBuffer.Length; i++)
        {
            float x = audioBuffer[i];
            float prev = (i > 0) ? audioBuffer[i - 1] : 0f;

            // 1-pole high-pass
            float hp = hpAlpha * (hpState + x - prev);
            hpState = hp;

            // 1-pole low-pass
            lpState = lpState + lpAlpha * (hp - lpState);

            procBuffer[i] = lpState;
            energy += lpState * lpState;
        }
        energy = Mathf.Sqrt(energy / audioBuffer.Length);

        // ---------- Step 2: Adaptive gate ----------
        noiseFloor = Mathf.Lerp(noiseFloor, energy, noiseAdapt);
        bool isVoice = energy > noiseFloor * gateRatio;

        if (isVoice)
        {
            silenceTimer = 0f;

            float pitch = DetectPitch_AutoCorr_FirstPeak(procBuffer, sampleRate, corrBuffer);

            if (pitch > minFrequency && pitch < maxFrequency)
                displayedFrequency = Mathf.Lerp(displayedFrequency, pitch, Time.deltaTime * smoothing);

            string note = FrequencyToNote(displayedFrequency);
            frequencyText.text = $"Frequency: {displayedFrequency:F2} Hz\nNote: {note}";
        }
        else
        {
            silenceTimer += Time.deltaTime;
            if (silenceTimer > holdDuration)
                frequencyText.text = "Listening...";
        }
    }

    // ---------- Autocorrelation with center-clipping, first-peak pick + parabolic interp ----------
    float DetectPitch_AutoCorr_FirstPeak(float[] buffer, int sr, float[] corr)
    {
        int N = buffer.Length;
        int half = N / 2;

        // Center-clipping (reduces harmonic dominance, avoids period-doubling)
        float maxAbs = 0f;
        for (int i = 0; i < N; i++)
        {
            float a = Mathf.Abs(buffer[i]);
            if (a > maxAbs) maxAbs = a;
        }
        float clipLevel = clipFrac * maxAbs;

        // Build a clipped copy in-place in procBuffer (already in buffer)
        for (int i = 0; i < N; i++)
        {
            float s = buffer[i];
            if (s > clipLevel) s -= clipLevel;
            else if (s < -clipLevel) s += clipLevel;
            else s = 0f;
            procBuffer[i] = s;
        }

        // Autocorrelation for lags in the useful frequency range
        int minLag = Mathf.Max(2, Mathf.FloorToInt(sr / maxFrequency)); // e.g. ~44 for 1 kHz at 44.1k
        int maxLag = Mathf.Min(half - 2, Mathf.CeilToInt(sr / minFrequency)); // e.g. ~882 for 50 Hz

        // Compute corr[lag]
        for (int lag = 0; lag < half; lag++)
            corr[lag] = 0f;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float sum = 0f;
            for (int i = 0; i + lag < N; i++)
                sum += procBuffer[i] * procBuffer[i + lag];
            corr[lag] = sum;
        }

        float r0 = corr[minLag]; // local energy proxy
        for (int lag = minLag + 1; lag <= maxLag; lag++)
            if (corr[lag] > r0) r0 = corr[lag]; // robust r0 (avoid silence artifacts)

        // Pick the *first* significant local maximum above threshold
        int bestLag = -1;
        for (int lag = minLag + 1; lag < maxLag - 1; lag++)
        {
            if (corr[lag] > corr[lag - 1] && corr[lag] > corr[lag + 1])
            {
                float norm = (r0 > 1e-9f) ? (corr[lag] / r0) : 0f;
                if (norm >= peakNormThreshold)
                {
                    bestLag = lag;
                    break; // FIRST strong peak = fundamental
                }
            }
        }

        // Fallback: take the strongest peak if none crossed the threshold
        if (bestLag < 0)
        {
            float maxVal = -1e9f;
            for (int lag = minLag + 1; lag < maxLag - 1; lag++)
            {
                if (corr[lag] > corr[lag - 1] && corr[lag] > corr[lag + 1] && corr[lag] > maxVal)
                {
                    maxVal = corr[lag];
                    bestLag = lag;
                }
            }
            if (bestLag < 0) return 0f;
        }

        // Parabolic interpolation around the peak for sub-sample accuracy
        float y1 = corr[bestLag - 1];
        float y2 = corr[bestLag];
        float y3 = corr[bestLag + 1];
        float denom = (y1 - 2f * y2 + y3);
        float offset = 0f;
        if (Mathf.Abs(denom) > 1e-9f)
            offset = 0.5f * (y1 - y3) / denom;

        float refinedLag = Mathf.Clamp(bestLag + offset, minLag, maxLag);

        float freq = sr / refinedLag;

        // Optional octave correction guard (helps if a rare double-period slips through)
        // if (freq > 200f && freq < 350f) freq *= 2f;

        return freq;
    }

    string FrequencyToNote(float freq)
    {
        if (freq < minFrequency) return "";
        int noteIndex = Mathf.RoundToInt(12 * Mathf.Log(freq / 440f, 2)) + 69;
        int octave = (noteIndex / 12) - 1;
        int note = noteIndex % 12;
        if (note < 0) note += 12;
        return noteNames[note] + octave;
    }
}
