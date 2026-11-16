# Game feel kit sounds

All sounds except horn.ogg (see the end) are by Kenney (www.kenney.nl), released under CC0 1.0 (public domain,
https://creativecommons.org/publicdomain/zero/1.0/). Crediting is optional; we do it anyway.
Files were renamed for the cue they serve; the original pack and file name are listed.
kenney.nl is not reachable from the cloud environment, so the packs were taken from GitHub
mirrors of the original downloads (license files included in each mirror).

| File | Kenney pack | Original file | Mirror |
|---|---|---|---|
| coin.ogg | Starter Kit 3D Platformer | coin.ogg | github.com/KenneyNL/Starter-Kit-3D-Platformer (same file as Assets/ThirdParty/Kenney/Sounds/coin.ogg) |
| pickup.ogg | Interface Sounds | confirmation_001.ogg | github.com/ETdoFresh/kenney.nl |
| purchase_1.ogg, purchase_2.ogg | RPG Audio | handleCoins.ogg, handleCoins2.ogg | github.com/ETdoFresh/kenney.nl |
| build_1..5.ogg | Impact Sounds | impactPlank_medium_000..004.ogg | github.com/ETdoFresh/kenney.nl |
| hit_1..5.ogg | Impact Sounds | impactPunch_medium_000..004.ogg | github.com/ETdoFresh/kenney.nl |
| kill_1..3.ogg | Impact Sounds | impactPunch_heavy_000..002.ogg | github.com/ETdoFresh/kenney.nl |
| footstep_1..5.ogg | Impact Sounds | footstep_concrete_000..004.ogg | github.com/ETdoFresh/kenney.nl |
| land_1..3.ogg | Impact Sounds | impactSoft_medium_000..002.ogg | github.com/ETdoFresh/kenney.nl |
| explosion_1..5.ogg | Sci-Fi Sounds | explosion_crunch_000..004.ogg | github.com/Boyquotes/kenney-sci-fi-sounds-for-godot |
| explosion_large_1..2.ogg | Sci-Fi Sounds | low_frequency_explosion_000..001.ogg | github.com/Boyquotes/kenney-sci-fi-sounds-for-godot |
| whoosh_1..2.ogg | RPG Audio | cloth1.ogg, cloth2.ogg | github.com/ETdoFresh/kenney.nl |
| countdown_beep.ogg | Digital Audio | tone1.ogg | github.com/ETdoFresh/kenney.nl |
| go.ogg | Voiceover Pack (male) | go.ogg | github.com/ETdoFresh/kenney.nl |
| win.ogg | Music Jingles (8-bit) | jingles_NES12.ogg | github.com/ETdoFresh/kenney.nl |
| lose.ogg | Music Jingles (8-bit) | jingles_NES07.ogg | github.com/ETdoFresh/kenney.nl |
| level_up.ogg | Digital Audio | zapThreeToneUp.ogg | github.com/ETdoFresh/kenney.nl |
| error.ogg | Interface Sounds | error_006.ogg | github.com/ETdoFresh/kenney.nl |
| alarm.ogg | Digital Audio | zapTwoTone.ogg | github.com/ETdoFresh/kenney.nl |
| jump.ogg | Digital Audio | phaseJump1.ogg | github.com/ETdoFresh/kenney.nl |

`horn.ogg` is not from Kenney: no CC0 pack we could reach has a horn, so it was synthesized for
this kit (Project Beta, own work) and is dedicated to the public domain under the same CC0 1.0.
It is a 1.4 s G3-B3-D4 chord of band-limited sawtooth voices with 5.5 Hz vibrato, a 50 ms attack
and a 0.4 s release, low-passed at 3.2 kHz and loudness-normalized to -15 LUFS (Python + ffmpeg/libvorbis).

The UI click and hover sounds live with the theme in
`Packages/com.ryan.ui/Runtime/UI/Theme/Sounds` (see the CREDITS.md there).

The robot's own footstep and landing sounds come from Unity's Space Robot Kyle sample
(`Assets/UnityTechnologies/SpaceRobotKyle/Sfx`), already wired on the RobotPlayer prefab.
