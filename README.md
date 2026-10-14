# AstroDash

**A hyper-casual vertical space shooter for Android, built in Unity.** Steer the ship, blast meteors, collect stars and stack power-ups, then spend your stars in the shop on new ships, shot types and backgrounds.

**Stack:** Unity 6 (URP, 2D) · C# · TextMesh Pro · Android (min SDK 26)

The game started as a single-file HTML5 Canvas prototype (`index.html`, playable in any browser) to nail the game feel quickly. It was then ported to Unity for mobile.

## Highlights

- **Object pooling** (`SimplePool`, `PooledObject`). Bullets, meteors, items and explosions are recycled instead of instantiated and destroyed every frame, which avoids garbage-collector stutter on phones.
- **Data-driven tuning** (`GameConfig`). All gameplay values live in a ScriptableObject that you edit in the Inspector without touching code. It's structured so it can later be overridden remotely.
- **Difficulty curve** (`Spawner`). The meteor spawn interval tightens with difficulty. There are three meteor classes: small, large, and a red one that takes two hits and darkens after the first.
- **Stackable power-ups** (`PlayerPowerUps`). Picking up the same type adds to its duration, and different types run in parallel.
- **Shop and progression** (`Catalog`, `ShopController`, `Progress`). One catalog is the single source of truth for ships, shot types and backgrounds. Purchases, the star balance and equipped items are saved as one JSON block in `PlayerPrefs`.
- **ES/EN localization** (`Loc`, `LocalizedText`). The language is picked from the phone's system language on first launch, and text updates live through events when the player switches.
- **Automated project setup** (`Assets/Editor/AstroDashSetup.cs`). An editor script builds the whole project in batch mode: tags, procedurally generated sprites, prefabs, config, and the full scene with its UI.

## Project structure

```
index.html                     original HTML5 Canvas prototype
AstroDash/Assets/Scripts/      gameplay, UI, shop, audio, localization
AstroDash/Assets/Editor/       batch-mode project builder
AstroDash/Assets/Prefabs/      bullet, meteor, item, power-up, explosion
AstroDash/Assets/Scenes/       Main.unity
licenses/                      font licenses (SIL OFL)
```

## Running

Open the `AstroDash/` folder with **Unity 6000.5** and load `Assets/Scenes/Main.unity`. To try the original prototype, open `index.html` in a browser.

---

© Esteban Sánchez. Fonts: Rubik and Space Grotesk, under the SIL Open Font License.
