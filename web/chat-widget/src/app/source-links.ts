/** A cited web source returned by the web search endpoint. */
export interface Source {
  title: string;
  url: string;
  host: string;
}

/**
 * Keeps only well-formed http(s) sources. The values come from the server (and ultimately from web pages),
 * so they are treated as untrusted: anything that is not an absolute http/https URL is dropped.
 */
export function toSources(raw: unknown): Source[] {
  if (!Array.isArray(raw)) return [];
  const result: Source[] = [];
  for (const item of raw) {
    const title = typeof item?.title === 'string' ? item.title.trim() : '';
    const url = typeof item?.url === 'string' ? item.url : '';
    let parsed: URL;
    try {
      parsed = new URL(url);
    } catch {
      continue;
    }
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') continue;
    result.push({ title: title || parsed.hostname, url: parsed.href, host: parsed.hostname });
  }
  return result;
}

// The inline form the model writes for a citation, e.g. " ([learn.microsoft.com](https://learn.microsoft.com/x))".
const INLINE_LINK = /\s?\(\[[^\]\r\n]*\]\((https?:\/\/[^\s)]+)\)\)/g;

/**
 * Streamed text arrives before the citations are known, so it can contain inline links that the Sources list repeats.
 * Removes an inline link only when its URL is one of the sources. Everything else is left as written.
 */
export function stripInlineSourceLinks(text: string, sources: Source[]): string {
  if (sources.length === 0) return text;
  const known = new Set(sources.map((s) => s.url));
  return text.replace(INLINE_LINK, (match: string, url: string) => {
    try {
      return known.has(new URL(url).href) ? '' : match;
    } catch {
      return match;
    }
  });
}
