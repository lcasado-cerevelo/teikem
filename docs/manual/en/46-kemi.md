# 46 — Kemi: the Teikem assistant (staff web and client portal), with voice

Batch 218 (2026-10-24, dictated by Luis). Kemi is Teikem's mascot and does the same thing in the two places where she lives: the **staff web** and the **client portal** (the client app, on the web and on the phone). The only thing that changes is **what she knows**: each one has its own help index. Consignees with their own access do not get Kemi.

## 1. What Kemi does (the same in both)
| What | How |
|---|---|
| **Floats** at the bottom right, alive (blinks, looks around, winks, makes faces; on the web she looks at the pointer) | She can be **dragged** somewhere else; she remembers it per person on that device. “Send Kemi back to the corner” puts her back |
| **Tells you** with a bubble when something new arrives and hops | Staff web: notices (the bell) and messages. Portal: new **messages** from the customer service team (the portal's notice inbox has no “unread”). “See →” opens what arrived. Once per session she greets you with what is pending |
| **Options panel** when you tap her | “Ask me how to use the system (or the portal)”, “My messages”, “See my notices”, send her back to her corner and “Put Kemi to sleep” |
| **Sleep and wake** | Asleep, she hides next to the menu (or in the top corner on the phone) and shows no bubbles. One tap wakes her. She remembers it when you reload |
| **Ask me how to use…** | By typing **or speaking** (§2). Kemi looks for the words in the manual and the FAQ and brings up to 5 options; when you open one she shows the answer and asks “Did that answer your question?” (**No** = she looks somewhere else, without repeating what was already shown; if there is nothing more, she says so) |
| **Knows which screen you are on** | What is documented for the screen you are on goes up in the options |
| **Language** | The manual is in Spanish: in English Kemi searches with a glossary and warns that she answers in Spanish |

## 2. Talking to Kemi
- Next to the question field there is a **microphone**. You tap it, say the question, and what Kemi understands **fills the field while you speak**; when you finish speaking she **asks by herself**. You can tap the microphone again to stop listening. It is the same as if you had typed it.
- **Where it works:** on the web, with the browsers that include speech recognition (**Chrome, Edge and Safari**; Firefox does not: there the microphone is not offered). On the phone (client app), with the device's speech recognition; the first time it asks permission for the **microphone** and **speech recognition**.
- **Language:** she listens in the language of the screen (Puerto Rican Spanish or US English).
- **Privacy:** the audio is processed by the browser's or the phone's speech service; **Teikem only receives the text** of the question and does not store the audio.
- **Messages when something fails:** “I don't have permission to use the microphone…” (you have to turn it on in the browser or device settings), “I didn't hear you…”, “This browser/device can't recognize speech…”, “I couldn't reach the voice service…”, “I couldn't understand you…”.

## 3. What Kemi knows: one index per audience
- `npm run help:index` (in `web-app`, also run by `npm run check`) builds **two** indexes from `docs/manual`: the **staff** one (the whole manual and the FAQ, `web-app/src/features/notifications/helpIndex.json`) and the **portal** one (`app-cliente/src/features/kemi/helpIndex.json`), which carries **only what the client can see**.
- **Rule for whoever writes the manual:** a chapter or a section of the FAQ **reaches the portal only if you mark it** with the comment `<!-- kemi:portal -->` (invisible when reading): in a chapter, on any line (it applies to the whole chapter); in the FAQ, right below the “## ”. A section or a question with `<!-- kemi:staff -->` inside is left out even if its chapter belongs to the portal, as are the “Endpoints” sections. **What is not marked does not reach the client**: that way staff internals (permissions, driver settlement, roles…) do not leak.
- The FAQ is indexed with the question **on its own line in bold** and the answer below.
- Chapters marked today: 24, 25, 26, 27, 28, 29, 30, 34, 35 and 37, and the portal sections of the FAQ (secure access, legal document, client app, language and theme, messages with the team, new shipment, consignee notices, invoices and notices, profile, warehouse and create/edit/cancel, Batch 213).

## 4. One brain (for the artificial intelligence to come)
Every question goes through **one interface** (`shared/kemi/brain.ts`, `KemiBrain.ask`) that today searches the index of the audience of whoever is asking. When Kemi gets AI, it will be **one brain on the server**, behind **one** endpoint, which decides the audience (staff or portal) from the session —never from what the browser sends— and answers both screens **without changing them** (neither the question box, nor the microphone, nor the bubbles). The shared code lives in `shared/` (search engine, face and bubble rules, voice and brain) and is used by the web and the portal.

## 5. Where everything is
- Staff web: `web-app/src/features/notifications/` (`Kemi.tsx`, `KemiAsk.tsx`, `useKemiVoice.ts`).
- Client portal: `app-cliente/src/features/kemi/` (`KemiFloating.tsx`, `KemiAsk.tsx`, `useKemiVoice.ts`, `KemiProvider.tsx`, `KemiMascot.tsx`), mounted in `app-cliente/src/app/_layout.tsx`.
- Shared: `shared/kemi/` (`helpSearch.ts`, `rules.ts`, `brain.ts`, `speech.ts`).
- Screens with screenshots: [frontend/l218-kemi.md](frontend/l218-kemi.md).
