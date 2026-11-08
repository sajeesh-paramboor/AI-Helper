namespace AI103.ChatApp.Application.DTOs;

/// <summary>
/// An uploaded document that the model cited through File Search. Both values come from the Responses API
/// file-citation annotations. A file citation has no URL: it identifies the file by name and ID.
/// </summary>
public sealed record DocumentReference(string FileName, string FileId);
