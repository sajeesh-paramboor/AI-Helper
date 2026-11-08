using System.ClientModel;
using System.ClientModel.Primitives;
using AI103.ChatApp.Infrastructure.Authentication;
using AI103.ChatApp.Infrastructure.Configuration;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Files;
using OpenAI.VectorStores;

// Manual admin tool: creates a vector store and uploads documents for the File Search feature.
// Deliberately NOT part of the API. Entra ID only (no keys). Every write asks for confirmation.
//
//   list                                   read-only: lists vector stores visible to the signed-in identity
//   status <vs_id>                         read-only: shows a store and its file processing status
//   create <name>                          WRITE: creates an empty vector store
//   upload <vs_id> <file> [<file> ...]     WRITE: uploads files and attaches them to the store, then polls

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true) // local-only real values, git-ignored
    .AddEnvironmentVariables()
    .Build();

var foundry = config.GetSection(FoundryOptions.SectionName).Get<FoundryOptions>() ?? new FoundryOptions();
var entra = config.GetSection(EntraAuthOptions.SectionName).Get<EntraAuthOptions>() ?? new EntraAuthOptions();

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();
if (command is not ("list" or "status" or "create" or "upload"))
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    PrintUsage();
    return 2;
}

// Validate arguments BEFORE any sign-in is triggered.
switch (command)
{
    case "status" when args.Length != 2:
    case "create" when args.Length != 2:
    case "upload" when args.Length < 3:
        PrintUsage();
        return 2;
}

if (command == "upload")
{
    var missing = args.Skip(2).Where(f => !File.Exists(f)).ToList();
    if (missing.Count > 0)
    {
        foreach (var f in missing)
        {
            Console.Error.WriteLine($"File not found: {f}");
        }

        return 2;
    }
}

Console.WriteLine($"Endpoint: {foundry.OpenAIEndpoint}");
Console.WriteLine($"Auth:     {entra.Mode} (tenant {entra.TenantId})");
if (command is "create" or "upload")
{
    Console.WriteLine($"Action:   {command} (WRITE operation)");
    if (!Confirm("Proceed? This will trigger Entra ID sign-in and change data in Foundry."))
    {
        Console.WriteLine("Cancelled.");
        return 1;
    }
}
else if (!Confirm("This is read-only but will trigger Entra ID sign-in. Proceed?"))
{
    Console.WriteLine("Cancelled.");
    return 1;
}

var credential = TokenCredentialFactory.Create(entra);
var endpoint = foundry.OpenAIEndpoint.EndsWith('/') ? foundry.OpenAIEndpoint : foundry.OpenAIEndpoint + "/";
var openAi = new OpenAIClient(
    new BearerTokenPolicy(credential, foundry.TokenScope),
    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
var vectorStores = openAi.GetVectorStoreClient();

try
{
    switch (command)
    {
        case "list":
            await ListAsync();
            break;
        case "status":
            await StatusAsync(args[1]);
            break;
        case "create":
            await CreateAsync(args[1]);
            break;
        case "upload":
            await UploadAsync(args[1], args.Skip(2).ToArray());
            break;
    }

    return 0;
}
catch (ClientResultException ex)
{
    // Status and message only: never print tokens or request headers.
    Console.Error.WriteLine($"Service returned HTTP {ex.Status}: {ex.Message}");
    return 3;
}
catch (Exception ex) when (ex is Azure.Identity.AuthenticationFailedException or Azure.Identity.CredentialUnavailableException)
{
    Console.Error.WriteLine($"Entra ID sign-in failed: {ex.Message}");
    return 4;
}

async Task ListAsync()
{
    var count = 0;
    await foreach (var store in vectorStores.GetVectorStoresAsync())
    {
        count++;
        Console.WriteLine($"{store.Id}  {store.Name}  status={store.Status}  files={store.FileCounts.Total}");
    }

    Console.WriteLine($"{count} vector store(s) visible.");
}

async Task StatusAsync(string storeId)
{
    VectorStore store = (await vectorStores.GetVectorStoreAsync(storeId)).Value;
    Console.WriteLine($"{store.Id}  {store.Name}  status={store.Status}");
    Console.WriteLine(
        $"files: total={store.FileCounts.Total}, completed={store.FileCounts.Completed}, " +
        $"in_progress={store.FileCounts.InProgress}, failed={store.FileCounts.Failed}, cancelled={store.FileCounts.Cancelled}");
}

async Task CreateAsync(string name)
{
    VectorStore store = (await vectorStores.CreateVectorStoreAsync(new VectorStoreCreationOptions { Name = name })).Value;
    Console.WriteLine($"Created vector store: {store.Id}  ({store.Name})");
    Console.WriteLine("Put this ID in Foundry:FileSearch:VectorStoreIds (user-secrets).");
}

async Task UploadAsync(string storeId, string[] paths)
{
    var files = openAi.GetOpenAIFileClient();
    var fileIds = new List<string>();

    foreach (var path in paths)
    {
        await using var stream = File.OpenRead(path);
        OpenAIFile uploaded = (await files.UploadFileAsync(stream, Path.GetFileName(path), FileUploadPurpose.Assistants)).Value;
        Console.WriteLine($"Uploaded {Path.GetFileName(path)} -> {uploaded.Id}");

        await vectorStores.AddFileToVectorStoreAsync(storeId, uploaded.Id);
        fileIds.Add(uploaded.Id);
    }

    // Poll until each file is processed (indexed) or has failed.
    var deadline = DateTime.UtcNow.AddMinutes(5);
    var pending = new HashSet<string>(fileIds);
    while (pending.Count > 0 && DateTime.UtcNow < deadline)
    {
        foreach (var id in pending.ToList())
        {
            VectorStoreFile assoc = (await vectorStores.GetVectorStoreFileAsync(storeId, id)).Value;
            if (assoc.Status == VectorStoreFileStatus.Completed)
            {
                Console.WriteLine($"{id}: completed");
                pending.Remove(id);
            }
            else if (assoc.Status == VectorStoreFileStatus.Failed
                     || assoc.Status == VectorStoreFileStatus.Cancelled)
            {
                Console.Error.WriteLine($"{id}: {assoc.Status} - {assoc.LastError?.Code} {assoc.LastError?.Message}");
                pending.Remove(id);
            }
        }

        if (pending.Count > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    if (pending.Count > 0)
    {
        Console.Error.WriteLine($"Timed out waiting for: {string.Join(", ", pending)}. Run 'status {storeId}' later.");
    }
}

static bool Confirm(string question)
{
    Console.Write($"{question} [y/N] ");
    return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
}

static void PrintUsage()
{
    Console.WriteLine("""
        AI103.ChatApp.DocumentTool - manage the File Search vector store (Entra ID, no keys)

          list                                 list vector stores (read-only)
          status <vs_id>                       show a store and its file counts (read-only)
          create <name>                        create an empty vector store (WRITE)
          upload <vs_id> <file> [<file> ...]   upload files into a store and wait for indexing (WRITE)

        Every command asks for confirmation before the Entra ID browser sign-in.
        """);
}
