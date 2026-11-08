# AI103.ChatApp.DocumentTool

A small console tool that creates the **vector store** used by File Search and uploads documents into it.
It lives in `tools/AI103.ChatApp.DocumentTool` and is deliberately **not part of the API**: the API never uploads,
changes or deletes documents. It only searches a store by ID.

Authentication is Microsoft Entra ID only (browser sign-in). No API keys are used anywhere.

---

## 1. What it does

| Command | Type | What it does |
|---|---|---|
| `list` | read-only | Lists the vector stores visible to the signed-in identity. |
| `status <vs_id>` | read-only | Shows one store: name, status and file counts (total, completed, in progress, failed, cancelled). |
| `create <name>` | **write** | Creates an empty vector store and prints its ID (`vs_...`). |
| `upload <vs_id> <file> [<file> ...]` | **write** | Uploads each file to the Foundry resource (purpose `assistants`), attaches it to the store, then checks every 3 seconds (up to 5 minutes) until each file is `completed`, `failed` or `cancelled`. |

Safety behaviour:

- **Every command asks `[y/N]` before the browser sign-in.** Write commands say so explicitly. Anything other than `y` cancels.
- Arguments and file paths are validated **before** any sign-in happens.
- Errors print only the HTTP status and message. Tokens and headers are never printed.
- There is no delete command (see [Limitations](#8-limitations)).

Exit codes:

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | You answered `n` at the confirmation prompt |
| 2 | Bad command, wrong arguments, or a file was not found |
| 3 | The service returned an HTTP error |
| 4 | Entra ID sign-in failed |

---

## 2. Prerequisites

- .NET 10 SDK (`dotnet --version` shows 10.x).
- A Microsoft Foundry resource with an OpenAI-compatible endpoint, for example
  `https://<resource>.services.ai.azure.com/openai/v1/`.
- Your Entra ID tenant ID (directory ID) and an account in that tenant that is allowed to use the resource.
  The account needs a role that permits creating vector stores and uploading files (for example Foundry User).
  This is verified at the first `create` or `upload`; a missing permission shows up as HTTP 401 or 403.
- A document in a format File Search can index. A Markdown file (`.md`) is verified to work. `.docx` has not been verified.

---

## 3. One-time configuration

The tool reads these files from its output folder, later files overriding earlier ones:

1. `appsettings.json` (committed, contains placeholders only)
2. `appsettings.Development.json` (**git-ignored**, your real values, optional)
3. Environment variables

### Step 3.1: create your local settings file

Create `tools/AI103.ChatApp.DocumentTool/appsettings.Development.json`:

```json
{
  "Foundry": {
    "OpenAIEndpoint": "https://<resource>.services.ai.azure.com/openai/v1/",
    "TokenScope": "https://cognitiveservices.azure.com/.default"
  },
  "EntraAuth": {
    "Mode": "InteractiveBrowser",
    "TenantId": "<your-tenant-id-guid>"
  }
}
```

Notes:

- `ModelDeploymentName` is not used by this tool, so you do not need to set it.
- `appsettings.Development.json` is listed in `.gitignore`, so your real tenant and endpoint are never committed.
- Instead of the file you can use environment variables, for example
  `$env:Foundry__OpenAIEndpoint = "..."` and `$env:EntraAuth__TenantId = "..."` (double underscore = section separator).
- `EntraAuth:Mode` can also be `AzureCli` (uses the account from `az login`) if you prefer that to a browser prompt.

### Step 3.2: build

From the repository root:

```powershell
dotnet build tools/AI103.ChatApp.DocumentTool/AI103.ChatApp.DocumentTool.csproj
```

Expected: `0 Warning(s)`, `0 Error(s)`.

Run the tool with:

```powershell
dotnet run --project tools/AI103.ChatApp.DocumentTool -- <command> [arguments]
```

Everything after the lone `--` goes to the tool. Running it with no arguments prints the usage text.

---

## 4. Step-by-step: index a document for File Search

### Step 1: (optional) see what already exists

```powershell
dotnet run --project tools/AI103.ChatApp.DocumentTool -- list
```

Answer `y`, complete the browser sign-in. Output looks like:

```
0 vector store(s) visible.
```

Stores that you created in the Foundry portal may not appear here. The portal and this API can show different scopes.

### Step 2: create the vector store

```powershell
dotnet run --project tools/AI103.ChatApp.DocumentTool -- create ai103-chatapp-docs
```

Answer `y`, sign in. Output:

```
Created vector store: vs_xxxxxxxxxxxxxxxxxxxxxxxx  (ai103-chatapp-docs)
```

**Copy the `vs_...` ID.** Create the store only once. Running `create` again makes a second, separate store.

### Step 3: upload your document

```powershell
dotnet run --project tools/AI103.ChatApp.DocumentTool -- upload vs_xxxxxxxxxxxxxxxxxxxxxxxx "C:\path\to\MyGuide.md"
```

Several files can be listed in one command. Answer `y`, sign in. Output:

```
Uploaded MyGuide.md -> assistant-xxxxxxxxxxxxxxxxxxxxxx
assistant-xxxxxxxxxxxxxxxxxxxxxx: completed
```

`completed` means the file has been chunked, embedded and is searchable. If a file shows `failed`, the error code and message
are printed. If it times out after 5 minutes, check later with `status`.

### Step 4: confirm

```powershell
dotnet run --project tools/AI103.ChatApp.DocumentTool -- status vs_xxxxxxxxxxxxxxxxxxxxxxxx
```

You should see `completed=1` (or the number of files you uploaded) and `failed=0`.

### Step 5: point the API at the store

Add the ID to the API's **git-ignored** local settings, `src/AI103.ChatApp.Api/appsettings.Development.json`:

```json
{
  "Foundry": {
    "FileSearch": {
      "Enabled": true,
      "VectorStoreIds": [ "vs_xxxxxxxxxxxxxxxxxxxxxxxx" ]
    }
  }
}
```

Merge this into the existing `Foundry` section rather than replacing it. The base `appsettings.json` keeps File Search
disabled with an empty ID list. Optionally add `"MaxResultCount": 5` (allowed range 1 to 50) to cap retrieved passages.

### Step 6: test through the API

Start the API (`dotnet run --project src/AI103.ChatApp.Api --launch-profile http`), then:

```powershell
$b = @{ message = "According to the guide, how do I get an access token?" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5265/api/chat/file-search/message `
  -ContentType "application/json" -Body $b | ConvertTo-Json -Depth 6
```

The response contains `answer`, `responseId`, `conversationId` and `documents` (the cited file names).
The streaming variant is `POST /api/chat/file-search/stream` (Server-Sent Events: `delta` events, then `completed`).
In the Angular widget, tick **Search my documents**.

---

## 5. Where the data is stored

Both objects are stored inside your Foundry resource, reached through the same OpenAI-compatible endpoint the chat API uses:

| Object | Example ID | Description |
|---|---|---|
| File | `assistant-...` | The uploaded document (purpose `assistants`). |
| Vector store | `vs_...` | The searchable index of the file's chunks and embeddings. |

Your original file on disk is not moved or changed. I could not confirm where, or whether, the Foundry portal lists this store.
Use `list` and `status` to check what the API sees.

---

## 6. Adding or replacing documents later

- **Add:** run `upload` again with the same `vs_...` ID and the new file.
- **Replace:** there is no delete command. Uploading the same file again creates a **duplicate** file in the store.
  To start clean, create a new store with `create`, upload to it, and update `VectorStoreIds` in the API settings.
  Remove the old files or store in the Foundry portal if it offers that, or through the REST API.

---

## 7. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| `Service returned HTTP 401` or `403` | The signed-in account lacks permission on the resource. Check the role assignment and that you chose the right account in the browser. |
| `Service returned HTTP 404` | Wrong endpoint, or the `vs_...` ID does not exist in this resource. Check `Foundry:OpenAIEndpoint` (it should end in `/openai/v1/`) and the ID. |
| `Entra ID sign-in failed` | Wrong tenant ID, or the browser sign-in was cancelled. Check `EntraAuth:TenantId`. |
| `File not found: ...` | Check the path and quote it if it contains spaces. Nothing was uploaded and no sign-in was triggered. |
| File shows `failed` | The format or content could not be indexed. Try the file as Markdown or plain text. |
| Timed out waiting | Indexing is slow. Run `status <vs_id>` again later. |
| API returns 404 "File search is disabled" | `Foundry:FileSearch:Enabled` is not `true` in the settings the API loaded (it must be running in the Development environment for `appsettings.Development.json` to apply). |
| API returns 502 on a file-search request | `VectorStoreIds` is empty or an entry is not of the form `vs_...`, or the store ID does not exist in this resource. The API log has the detail. |

---

## 8. Limitations

- Only four commands: `list`, `status`, `create`, `upload`. No delete, rename or listing of files inside a store.
- `upload` is not idempotent: it does not detect files that are already in the store.
- Each run signs in again through the browser. Tokens are not cached between runs.
- The citation list in the API response contains any file the search returned, not only the files the answer actually used.
  A question the documents cannot answer can still show the file name.
- `.docx` support is unverified. Only a Markdown upload has been tested end to end.

---

## 9. Security notes

- Never commit real tenant IDs, endpoints or vector store IDs. Keep them in the git-ignored `appsettings.Development.json`
  or in environment variables.
- Retrieved document text is untrusted data. The API's instructions tell the model not to follow instructions found in documents.
- Do not put secrets or personal data in documents that you upload unless you are allowed to store them in the Foundry resource.
