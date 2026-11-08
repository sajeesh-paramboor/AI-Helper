# MoMo Developer Assistant: running locally and deploying to the Developer Portal

This guide covers two things: running the chat widget against the .NET API on your machine, and what is needed
to deploy it as an Azure API Management (APIM) Developer Portal custom widget, following the pattern of
`azure-api-management-widget-cw-callback-url-configuration`.

Items marked **(not verified)** are things I could not confirm from the files in this repository. Check them
before relying on them.

---

## 1. Architecture

```
Browser (Developer Portal page)
  +-- iframe: custom widget (Angular, static files in the portal's blob storage)
        |  POST /api/chat/stream  (Server-Sent Events)
        v
  ASP.NET Core API  (this repo: src/AI103.ChatApp.Api)
        |  IConversationService -> IChatService (FoundryChatService)
        |  Entra ID token (InteractiveBrowserCredential locally, managed identity in Azure)
        v
  Azure AI Foundry  /openai/v1/responses  (gpt deployment)
```

- The widget knows only the chat API. It has no Azure, model or token code.
- The API holds the conversation. The widget keeps one `conversationId`, and the server chains each turn to the
  model's previous response with `previous_response_id`.
- Nothing in the widget is a secret. Azure credentials never leave the API.

## 2. Run it locally

You need the .NET 10 SDK and Node 22.22+ or 24.15+ (Node 24.18 was used).

**Terminal 1: the API** (from the repository root)

```powershell
dotnet run --project src/AI103.ChatApp.Api --launch-profile https
```

On the first chat message a browser window opens for the Entra ID sign-in (`EntraAuth:Mode` =
`InteractiveBrowser`, tenant pinned in `appsettings.Development.json`).

**Terminal 2: the widget**

```powershell
cd web/chat-widget
npm install        # first time only
npm start
```

Open `http://localhost:4200`. The dev server proxies `/api/*` to `https://localhost:7072` (see
`web/chat-widget/proxy.conf.json`), so no CORS configuration is needed locally.

**Checks that need no Azure call**

| Request | Expected |
|---|---|
| `GET /api/chat/health` | 200 `{"status":"Healthy",...}` |
| `POST /api/chat/message` with `{"message":""}` | 400 validation problem |
| `POST /api/chat/message` with an unknown `conversationId` | 404 "Conversation not found" |

## 3. API contract used by the widget

| Endpoint | Purpose |
|---|---|
| `POST /api/chat/stream` | Streams the answer as Server-Sent Events |
| `POST /api/chat/message` | Returns the whole answer in one response |
| `GET /api/chat/conversations/{id}` | Returns the stored messages of a conversation |
| `GET /api/chat/health` | Liveness check |

Request body: `{ "message": "...", "conversationId": null }`. Send the `conversationId` from the previous
answer to continue a conversation. `previousResponseId` is still accepted when no `conversationId` is sent.

Stream events (`data: {...}` followed by a blank line):

```
{"type":"delta","text":"..."}                                    repeated
{"type":"completed","responseId":"...","conversationId":"..."}   final event
{"type":"error","message":"..."}                                 only if the model fails mid-stream
```

Errors before streaming starts are RFC 9457 `ProblemDetails` JSON (400 validation, 404 unknown conversation,
429 model busy, 502 model failure, 500 other). Internal details are logged on the server, not returned.

**Conversations are held in memory.** They are lost when the API restarts or runs on more than one instance, and
they expire after 2 hours idle or 24 hours total. The widget handles a 404 by starting a new conversation on the
next message. Use a persistent `IConversationStore` before scaling out.

## 4. What the reference widget does (the pattern to follow)

Read from `azure-api-management-widget-cw-callback-url-configuration/`:

| Piece | Reference widget | Notes for the chat widget |
|---|---|---|
| Build tool | Vite, `root: ./src`, output `./dist`, two entries: `index.html` (the widget) and `editor.html` (the admin editor) | Angular outputs `dist/portal/browser/` via `npm run build:portal` |
| Widget identity | `static/config.msapim.json`: `displayName`, `technology`, `name` | Needs its own file with a new `name` |
| Deploy | `deploy.js` calls `deployNodeJS(serviceInformation, name, fallbackConfigPath, config)` from `@azure/api-management-custom-widgets-tools`; it signs you in interactively and uploads the contents of `./dist/` to the portal's blob storage | For Angular, set `rootLocal` (see section 6) |
| Target service | `serviceInformation.resourceId` points to one APIM instance | Use the APIM instance that hosts your portal |
| Settings | `values.ts` defaults, edited in `editor.html` via `buildOnChange`, read in the widget via `getValues` | Use this for the API base URL (section 5) |
| Portal context | `askForSecrets("app")` returns `managementApiUrl`, `apiVersion`, `token` and `userId` from the parent portal page | Not used by the chat widget yet |
| Backend call | `fetch` to an APIM gateway URL with the header `Ocp-Apim-Subscription-Key` | Not used yet (see section 7) |
| Hosting | Loaded by the portal in an iframe | See the layout caveat in section 8 |

## 5. Making the Angular widget portal-ready (to do)

These steps are not done yet:

1. **Configurable API address.** The widget reads `apiBaseUrl` from an element attribute. Inside the portal, the
   address should come from the editor values (`getValues(valuesDefault)` reading the `editorData` URL
   parameter). Add `@azure/api-management-custom-widgets-tools` to `web/chat-widget`, read the value in
   `main.ts`, and set it on the element.
2. **Editor page.** Add an `editor.html` with an input for the API base URL, wired to `buildOnChange`, as in the
   reference. **(not verified)** whether the portal requires an editor page; the reference has one.
3. **Widget config.** Add `static/config.msapim.json` (copy the reference and change `name` and `displayName`).
4. **Deploy script.** Add `deploy.js` modelled on the reference.
5. **Build output.** Run `npm run build:portal`. It produces `dist/portal/browser/index.html` with relative asset
   paths (`<base href="./">`), which is what a blob-storage host needs. The reference's `rootLocal` default is
   `./dist/`, so pass `rootLocal: "./dist/portal/browser/"` in the `config` object of `deployNodeJS`.

## 6. Deployment sequence (when ready)

1. Deploy the API somewhere reachable over HTTPS (for example Azure App Service) with managed identity.
   Set `EntraAuth:Mode` to `ManagedIdentity`, and give that identity the **Foundry User** role (or **Cognitive
   Services OpenAI User**) on the Foundry resource. Supply the `Foundry:*` settings as app settings.
2. Add a CORS policy to the API that allows only the origin the widget is served from (section 8).
3. Build the widget with `npm run build:portal`, then run the deploy script. It asks you to sign in to Azure and
   uploads the files.
4. In the portal's admin area, add the custom widget to a page and set its API base URL.
5. Publish the portal.

## 7. Security: do this before any shared environment

- **The API has no caller authentication today.** Anyone who can reach it can spend model quota. Local use is
  fine; do not expose it publicly as it is.
- The reference widget authenticates its backend with the user's APIM subscription key. For the chat API, the
  natural options are to publish it behind APIM and require a subscription key or JWT there, or to validate a
  token in the API. This is a design decision for you; credentials are deliberately left out for now.
- Add rate limits (per user or key) at APIM or in the API to cap cost.
- Keep the Foundry endpoint, deployment name and tenant values in app settings or Key Vault, not in a public
  repository. `appsettings.Development.json` currently contains them.
- If APIM sits in front of the API, check how it handles streaming. Buffering can break Server-Sent Events.
  **(not verified)** Confirm the setting in the APIM documentation before relying on it.

## 8. Known caveats

- **Layout inside an iframe.** The widget's launcher and panel use `position: fixed`, which is relative to the
  iframe, not to the portal page. In a small iframe the chat will be confined to that box. Either size the
  widget area for the full chat panel, or change the layout to fill its container for the portal version.
  **(not verified)** how your portal sizes custom widget iframes.
- **CORS and origin.** An iframe served from blob storage has a different origin from both the portal and the
  API, so the API must allow that origin. **(not verified)** the exact origin for your portal; read it from the
  browser's network tab after the first deploy.
- **Script loading.** The Angular build uses `<script type="module">`. **(not verified)** that the portal's blob
  hosting serves `.js` files with a JavaScript content type, which module scripts require.
- **Plain-text answers.** Answers render as text. Markdown and code blocks are not formatted yet.
- **Unit tests.** None are included in the Angular app, at your request.

## 9. File map

| Path | What it is |
|---|---|
| `src/AI103.ChatApp.Api` | Controllers, exception handler, `Program.cs` |
| `src/AI103.ChatApp.Application` | `IChatService`, `IConversationService`, `IConversationStore`, DTOs |
| `src/AI103.ChatApp.Infrastructure` | `FoundryChatService`, Entra credential factory, in-memory store |
| `src/AI103.ChatApp.Domain` | `Conversation`, `ChatMessage`, `ChatRole` |
| `web/chat-widget` | Angular widget, registered as `<momo-chat-widget>` |
| `azure-api-management-widget-cw-callback-url-configuration` | Reference portal widget (not part of the build) |
