/** A document cited by the file search endpoint. Only the name is shown; the file ID stays on the server side. */
export interface DocumentRef {
  fileName: string;
}

/** Keeps only entries with a non-empty string file name. The values come from the server, so they are rendered as plain text only. */
export function toDocuments(raw: unknown): DocumentRef[] {
  if (!Array.isArray(raw)) return [];
  const seen = new Set<string>();
  const result: DocumentRef[] = [];
  for (const item of raw) {
    const fileName = typeof item?.fileName === 'string' ? item.fileName.trim() : '';
    if (!fileName || seen.has(fileName)) continue;
    seen.add(fileName);
    result.push({ fileName });
  }
  return result;
}
