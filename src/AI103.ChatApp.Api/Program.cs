using AI103.ChatApp.Api;
using AI103.ChatApp.Application.Interfaces;
using AI103.ChatApp.Application.Services;
using AI103.ChatApp.Infrastructure;
using AI103.ChatApp.Infrastructure.Conversations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Azure AI Foundry integration (Responses API + Entra ID). Reads the "Foundry" and "EntraAuth"
// configuration sections; local values come from appsettings.Development.json or user-secrets.
builder.Services.AddFoundryChatService(builder.Configuration);

// Server-side conversation state (Option B). In-memory for now; replace IConversationStore for persistence.
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddSingleton<IConversationService, ConversationService>();

// Optional web search (separate endpoints under /api/chat/web-search; off unless Foundry:WebSearch:Enabled is true).
builder.Services.AddWebSearchChatService(builder.Configuration);
builder.Services.AddSingleton<IWebSearchConversationService, WebSearchConversationService>();
// Optional file search over a manually created vector store (separate endpoints under /api/chat/file-search; off unless Foundry:FileSearch:Enabled is true).
builder.Services.AddFileSearchChatService(builder.Configuration);
builder.Services.AddSingleton<IFileSearchConversationService, FileSearchConversationService>();


var app = builder.Build();

// Must run before the endpoints so exceptions become ProblemDetails responses.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
