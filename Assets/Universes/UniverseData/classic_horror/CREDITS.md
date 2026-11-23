# Classic Horror credits

## Audio (`Audio/`)

| File | Source | Licence |
| --- | --- | --- |
| door-slam-1.ogg, door-slam-2.ogg | Kenney, *RPG Audio* (`doorClose_4.ogg`, `doorClose_2.ogg`) | CC0 1.0 |
| creak-1.ogg .. creak-3.ogg | Kenney, *RPG Audio* (`creak1..3.ogg`) | CC0 1.0 |
| journal-open.ogg, journal-close.ogg, journal-page-1.ogg, journal-page-2.ogg | Kenney, *RPG Audio* (`bookOpen.ogg`, `bookClose.ogg`, `bookFlip1.ogg`, `bookFlip2.ogg`) | CC0 1.0 |
| chapel-bell-1.ogg, chapel-bell-2.ogg | Kenney, *Impact Sounds* (`impactBell_heavy_000.ogg`, `impactBell_heavy_001.ogg`) | CC0 1.0 |
| wood-thud.ogg | Kenney, *Impact Sounds* (`impactWood_heavy_000.ogg`) | CC0 1.0 |
| heartbeat.ogg, breathing.ogg, radio-static.ogg | Synthesised for this project by `Audio/Source~/synth_horror_audio.py` | CC0 1.0 (project-owned) |
| objective-collect.mp3 | Existing project asset | as originally added |
| Voice/*.ogg (68 voice lines) | Spoken by Piper TTS (MIT licence, github.com/rhasspy/piper) with the `en_US-libritts_r-medium` voice, trained on LibriTTS-R; script in `Voice/Source~/lines.tsv`, processing (pitch, whisper, reverb, radio) in `Voice/Source~/gen_voices.py` | voice model CC BY 4.0 (LibriTTS-R, Koizumi et al. 2023); generated clips project-owned |

Kenney packs (www.kenney.nl) were taken from the GitHub mirror github.com/ETdoFresh/kenney.nl, because
kenney.nl is not reachable from the cloud environment. Credit is not required by CC0 but given gladly.

## Other assets used by the game

- **Flooded Grounds** (`Flooded_Grounds/`): third-party environment pack (map, props, `DeepRattle.mp3`,
  `WindHowl.mp3`); see `Flooded_Grounds/Readme.pdf`. Not modified.
- **Cursed Priest** (`Cursed Priest/`): third-party character model used for the Presence.
- Horror music and the jumpscare sting come from `Packages/com.ryan.ui/Runtime/Client/ClientAudio`.
- Particle texture `fx_soft_dot.png` (flashlight dust) and the shared sound cues come from the game-feel
  kit in `Packages/com.ryan.gameframework/Runtime/GameFeel` (see its `Audio/CREDITS.md`).
- Lantern posts (`Prefabs/SearchLantern.prefab`) use `post_lantern.fbx` from Kay Lousberg's *KayKit Halloween Bits*
  (CC0 1.0), already in the project; `Materials/SearchLanternGlass.mat` is a copy of its atlas material with emission.
- The night's items (`Prefabs/Items/`): the fuel can, toolbox and bell are built from Unity primitives; the ferry
  lantern is `lantern_standing.fbx` (*KayKit Halloween Bits*), the hymnal and photograph are `book_single.fbx` and
  `pictureframe_standing_A.fbx` (*KayKit Furniture Bits*), the candle and music box are `candle_thin.fbx` and
  `box_small_decorated.fbx` (*KayKit Dungeon Remastered*), all Kay Lousberg, CC0 1.0, tinted copies of their atlases.
- The ferry, jetty, vestry wardrobes, altar table and Lily's stone (built by **Build Story Set**) are Flooded Grounds props.
- Lantern post wood uses `Materials/SearchLanternPost.mat`, a darker, matte copy of the KayKit atlas material.
- `Data/EncounterFace.png` is rendered in the Editor (`Editor/CH_EncounterFace.cs`) from the shared `PresenceFace`
  prefab (the Cursed Priest model above); no outside image.
- `Materials/Particles/` are copies of Flooded Grounds particle materials, fixed for URP alpha blending.
