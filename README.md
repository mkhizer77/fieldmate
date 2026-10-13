# Fieldmate

An AI-assisted mixed-reality maintenance companion for Meta Quest 3. A digital twin of an industrial pump skid stands
in your real room; a small holographic mate walks you through a real maintenance procedure, checks every action you
take on the machine, stops you when something is unsafe, and answers questions by voice from the machine's manual.

Built with Unity 6, OpenXR (Meta feature group), XR Interaction Toolkit 3 and XR Hands. Work in progress: v0.2.0
adds camera-based "What's this?" and a German mode; see the issues and milestones.

![The hologram mate](docs/ui/2026-09-30-hologram-mate.png)

## What happens in the headset

1. **Setup, one step at a time.** The headset checks the room scan; the mate materialises in front of you, introduces
   itself, and you point it to where it should stay. It then asks for the machine: you point at the floor and the
   skid appears, sized to fit the free space. The mate briefs the job and step 1 starts.
2. **The procedure** (replace a stuck relief valve cartridge, 10 steps): inspect by looking, lock out the breaker
   with both hands, close the inlet, read zero on the gauge, take the cover off, seat the new cartridge, restore and
   verify. Steps complete only from what you actually do; out-of-order actions are recorded, and parts guarded by a
   safety rule (lockout before the inlet valve or the cover) stay still until the rule is met.
3. **The mate** speaks each next step, interrupts on a violation, and answers questions ("why is the pressure high?",
   "where is the breaker?", "what is what?") with tool calls: it highlights parts, reads live telemetry, opens manual
   sections, logs notes. Its lips follow the voice; a speech bubble shows the words as they are spoken. Without a
   network it falls back to scripted answers from the manual.
4. **"What's this?"** Look at something and ask. The app takes one camera frame, asks a vision model which of the
   nearby machine parts (or what else) it is, and pins the answer as a label on the spot for 20 seconds. If the model
   is unsure, the twin's own part data answers instead. A red "Camera" pill shows every time a frame is taken.
5. **Deutsch, bitte.** Ask the mate to speak German, or flip Language in the hand menu: its voice, the step guidance,
   the manual, the labels and every panel switch to German on the spot, mid-procedure.
6. **A supervisor can watch.** The Observer button in the hand menu shows a join code; the Fieldmate Observer app on
   a Mac joins with it and sees the twin's telemetry, the procedure and the conversation live. From there the
   supervisor can inject or clear a fault, nudge the trainee with a hint (the step's part lights up and the mate says
   it) or send a message the mate speaks. The headset stays in charge: it validates every request and never waits on
   the observer.
7. **Scan the machine's tag.** Instead of pointing at the floor, lay the machine's printed tag where it stands and
   press Scan tag: the headset finds the tag in one camera frame (on the device, nothing uploaded), looks the machine
   up by the tag's id through Addressables, and anchors the twin on it. Tags and print instructions: `docs/markers`.
8. **Controls stay out of the way**: a hand menu on the left hand (palm towards you, or the left menu button) holds
   Start/Restart, Move machine, occlusion, performance stats, part labels, the camera (local-only mode), the
   language, the observer and the tag scanner.

## Architecture

```mermaid
flowchart LR
  subgraph Headset["Quest 3 app (Unity)"]
    Mic[Mic + speech gate] --> Session
    subgraph Core["Fieldmate.Core (pure C#, unit-tested)"]
      Session[AssistantSession<br/>prompt + per-turn context] --> Tools[Tool registry<br/>highlight · telemetry · manual · steps · labels]
      Runner[ProcedureRunner<br/>steps · safety rules · scoring]
      Twin[Telemetry twin<br/>faults]
      Manual[Manual retriever]
    end
    Tools --> Scene[Scene: highlighter, step card, labels]
    Interactables[Valves · breaker · cover · cartridge] --> Runner
    Runner --> Narrator[Narrator] --> Session
    Session --> Speaker[Streaming audio] --> Mate[Hologram mate<br/>lip-sync]
    Tools --> Vision[What's this?<br/>one frame · gaze · twin candidates] --> Labels[3D label]
  end
  Vision -->|"JPEG, on request"| Proxy
  Session <-->|HTTPS| Proxy["Proxy (Cloudflare Worker)<br/>keys, limits"]
  Proxy --> STT[Speech-to-text]
  Proxy --> LLM[Chat model]
  Proxy --> TTS[Text-to-speech]
  Runner -.->|"10 Hz state, transcript"| Observer["Observer app (Mac)<br/>Netcode + Relay"]
  Observer -.->|"fault · hint · message"| Runner
```

- **`Fieldmate.Core`**: procedures, AI orchestration (session, tools, prompts, fallback, eval), manual retrieval
  and the telemetry model. No UnityEngine beyond maths; covered by EditMode tests.
- **`Fieldmate.Unity`**: XR (placement, presence, pointers, hand menu, occlusion), interaction, the assistant (voice
  loop, hologram, speech bubble) and UI, all built in code with a small UI kit.
- **`Fieldmate.Editor`**: builders that generate the bench scene and prefabs (nothing hand-edited), project setup,
  the AI eval runner.
- **`Fieldmate.Net`**: the remote observer over Netcode for GameObjects (one named message, no networked prefabs)
  and Unity Relay, with a local-network fallback. The protocol, the trainee-authoritative server and the observer's
  mirror live in `Fieldmate.Core/Net` and are tested over an in-memory link; a PlayMode test runs both ends over real
  Netcode on localhost.
- **`Fieldmate.Core/Markers`**: machine tags from scratch: a 32-code dictionary with error correction, adaptive
  threshold + contour + quad detection with sub-pixel corners, and planar pose from the homography; tested on
  synthetic camera frames with known poses.
- **`tools/proxy`**: the Worker that holds the provider keys; the app never ships them.

## "What's this?": the vision pipeline

```mermaid
flowchart TD
  Ask["User: “What's this?”"] --> Tool["identify_view tool call"]
  Tool --> Gate{"Camera allowed?<br/>local-only off · permission · ≥ 1 s since last"}
  Gate -->|no| Explain["Assistant says why<br/>(local-only, permission)"]
  Gate -->|yes| Capture["CameraFrameSource<br/>one CPU frame + intrinsics + capture pose"]
  Capture --> Pill["Red “Camera” pill"]
  Capture --> Gaze["Gaze ray at capture time<br/>→ twin part under the gaze, nearby parts"]
  Gaze --> Query["VisionQuery: JPEG ≤ 640 px<br/>+ gaze pixel (PixelToRay) + candidate part names + language"]
  Query -->|HTTPS| Proxy["Proxy /v1/vision"] --> Model["Vision model"]
  Model --> Answer["label · part id · confidence · box"]
  Answer --> Fuse["AnswerFusion<br/>unsure model → the twin's gazed part"]
  Fuse --> Ray["PixelToRay<br/>box centre → world ray (intrinsics, camera mount, capture pose)"]
  Ray --> Label["LabelAnchor: 3D label for 20 s<br/>chip: Model data / Not sure / Camera"]
  Fuse --> Speak["Tool result → the mate answers"]
```

- One frame per request, only on request; nothing is stored. The pure maths (`PixelToRay`, `AnswerFusion`,
  `CaptureSettings`, `VisionQuery`) lives in `Fieldmate.Core` and is unit-tested; the camera and labels are in
  `Fieldmate.Unity`.
- The label lands where the model saw the part, cast from the camera pose at capture time, so turning your head while
  the model answers doesn't move it.

## Privacy: what leaves the headset

| What | When | Goes to |
|---|---|---|
| Your voice (a short WAV clip) | Only while you hold the talk button or pinch | Proxy → speech-to-text (Deepgram) |
| The transcript, matching manual sections and the machine's state (step, telemetry, the part you look at) | Each assistant turn | Proxy → chat model (Anthropic) |
| The assistant's reply text | Each spoken reply | Proxy → text-to-speech (ElevenLabs) |
| **One camera frame** (JPEG, at most 640 px) and the names of nearby parts | **Only when you ask "What's this?"**, never in the background. A red "Camera" pill shows every time | Proxy → vision model (Anthropic) |
| Telemetry, procedure progress and the conversation transcript (no audio, no camera) | Only while you share with the Observer button | The observer's Mac: directly on the local network, or through Unity Relay (DTLS-encrypted) |

- **Stays on the headset:** the passthrough video, the room scan and scene mesh, spatial anchors, hand tracking.
  A captured frame lives only in memory, overwritten by the next one; nothing is written to storage.
- **The proxy** (`tools/proxy`) holds the provider keys and stores no content. It keeps daily request counts for its
  cost caps and uses the caller's IP address only for the per-minute rate limit. Providers process requests under
  their API terms.
- **Local-only mode** (Camera button in the hand menu): the camera is never read and no image leaves the headset;
  "What's this?" answers from the twin's own part data. Voice still uses the cloud. Without a proxy configured, the
  assistant runs on scripted answers and nothing leaves the headset at all.
- **Scan tag** reads one camera frame and finds the tag on the headset; that frame never leaves it. The Camera pill
  shows for it too, and local-only mode turns it off with the rest of the camera.
- The camera needs the Horizon OS `HEADSET_CAMERA` permission. Deny it and everything else keeps working.

Decisions live in [`docs/adr/`](docs/adr): OpenXR route (001), Unity version (002), voice providers and proxy (003).
The performance budget and measurements are in [`docs/perf/`](docs/perf).

## Build and run

Requires Unity 6000.4.9f1 with Android Build Support (see `ProjectSettings/ProjectVersion.txt` and ADR-002) and a
Quest 3 on Horizon OS v74 or later.

```
tools/test.sh      # EditMode + PlayMode tests
tools/build.sh     # Android APK -> Builds/
tools/install.sh   # adb install -r + launch
```

The assistant needs a proxy: copy `Assets/_Project/Secrets/keys.json.template` to `keys.json` and set the proxy URL
(`tools/proxy` deploys one). Without it the app runs with scripted answers. After changing project settings by hand,
`Fieldmate > Configure Project for Quest` re-applies the Quest defaults; `Fieldmate > Build Assistant Bench Scene`
regenerates the scene.

**Remote observer (Mac):** `tools/build-observer.sh` builds `Builds/FieldmateObserver.app`. In the headset, press
Observer in the hand menu and type the code it shows into the app. Codes look like `K7Q2XB` through Unity Relay
(works across networks; the Unity project must be linked to a Unity Gaming Services project, Relay's free tier is
enough) or `192.168.1.20:7788` on the same Wi-Fi when Relay isn't set up. To try the dashboard without a headset,
start it with a simulated trainee: `open Builds/FieldmateObserver.app --args -preview`.

`Fieldmate > Run AI Eval` sends the 30 utterances (25 English, 5 German) in `Assets/Fieldmate.Tests/AIEval/utterances.json` to the real
model and writes a report to `docs/eval/`; CI runs the same cases against a mocked model on every pull request.

## Licence
MIT for the code in this repository. Third-party assets are not included; see [`docs/ASSETS.md`](docs/ASSETS.md).
