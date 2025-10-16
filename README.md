#Simple Pitch Detector (Audio Analysis Focus)

## Challenge Completed
**Option B — Simple Pitch Detector (Audio Analysis Focus)**  
A Unity tool that detects the **fundamental frequency (pitch)** of real-time microphone input (e.g., humming, whistling).  
Includes a **bonus feature**: works even with background music using adaptive noise gating and band-pass filtering.

---

## Unity Version
Unity **6.0.0 (6000.0.47f1)** LTS — Windows.

---

## Project Overview
**Main Script:** `PitchDetector.cs`  
Key components:
1. **Microphone Input** – continuous recording via `Microphone.Start()`.  
2. **Pre-Filtering** – 1-pole high-pass (80 Hz) + low-pass (1500 Hz) isolates voice frequencies.  
3. **Adaptive Noise Gate** – learns background RMS level; runs pitch detection only when input exceeds it.  
4. **Autocorrelation** – estimates the fundamental frequency.  
5. **Note Mapping** – converts frequency to musical note (A4 = 440 Hz).  
6. **UI** – displays frequency + note via TextMeshPro.

**Scene Layout**
PitchDetectorScene
├── Main Camera
├── Directional Light
├── Canvas
│ └── Text (TMP) – Frequency + Note
└── PitchDetectorController (AudioSource + Script)


---

## How to Run
1. Open project in **Unity 6.0.0 LTS**.  
2. Load `PitchDetectorScene.unity`.  
3. Connect a microphone and allow permission.  
4. Press **Play**  
5. Hum or whistle → frequency and note appear.  
6. Optional: play background music — the detector focuses on your voice.

---

## Demo Video
Short demo: **PitchDetector_Demo.mp4**  
Shows:  
- “Listening…” state  
- Real-time pitch + note display  
- Background-music rejection

---

## Notes & Improvements
- Works best for single-tone input (voice, whistle).  
- Possible extensions:
  - FFT-based multi-pitch detection  
  - Visual tuner bar / confidence meter  
  - ML-based pitch classification

---

**Author:** *Tabish Khan*  
Part of the *Neptune Audio Analysis Challenge*
