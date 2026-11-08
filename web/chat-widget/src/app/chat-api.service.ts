import { Injectable } from '@angular/core';
import { DocumentRef, toDocuments } from './document-refs';
import { Source, toSources } from './source-links';

/** Thrown for any failure calling the chat API; the message is safe to show to the user. */
export class ChatApiError extends Error {
  constructor(
    message: string,
    /** HTTP status when the failure came from an HTTP response. */
    readonly status?: number,
  ) {
    super(message);
  }
}

interface StreamEvent {
  type: 'delta' | 'completed' | 'error';
  text?: string;
  responseId?: string;
  conversationId?: string;
  message?: string;
  /** Only sent by the web search endpoint, in the "completed" event. */
  sources?: unknown;
  /** Only sent by the file search endpoint, in the "completed" event. */
  documents?: unknown;
}

/**
 * Talks to the ASP.NET Core chat API only. It knows nothing about Azure or the AI model,
 * so the frontend stays independent of the AI integration.
 */
@Injectable({ providedIn: 'root' })
export class ChatApiService {
  /**
   * POST /api/chat/stream (or /api/chat/web-search/stream when webSearch is true,
   * or /api/chat/file-search/stream when fileSearch is true).
   * Reads Server-Sent Events and calls onDelta for every text fragment.
   * Resolves with the conversationId from the final "completed" event; onSources receives the cited sources
   * (web search only) and onDocuments the cited documents (file search only) from that same event.
   * fetch is used instead of EventSource because EventSource only supports GET.
   */
  async stream(
    baseUrl: string,
    message: string,
    conversationId: string | null,
    onDelta: (text: string) => void,
    signal: AbortSignal,
    webSearch = false,
    onSources?: (sources: Source[]) => void,
    fileSearch = false,
    onDocuments?: (documents: DocumentRef[]) => void,
  ): Promise<string> {
    const path = webSearch
      ? '/api/chat/web-search/stream'
      : fileSearch
        ? '/api/chat/file-search/stream'
        : '/api/chat/stream';
    const response = await fetch(`${baseUrl.replace(/\/+$/, '')}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
      body: JSON.stringify({ message, conversationId }),
      signal,
    }).catch((e: unknown) => {
      if (e instanceof DOMException && e.name === 'AbortError') throw e;
      throw new ChatApiError('Cannot reach the chat service. Please try again.');
    });

    if (!response.ok) {
      throw new ChatApiError(await this.readProblem(response), response.status);
    }
    if (!response.body) {
      throw new ChatApiError('The chat service returned an empty response.');
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let completedConversationId: string | null = null;

    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      // SSE events are separated by a blank line.
      let boundary: number;
      while ((boundary = buffer.indexOf('\n\n')) !== -1) {
        const raw = buffer.slice(0, boundary);
        buffer = buffer.slice(boundary + 2);
        const event = this.parseEvent(raw);

        if (event?.type === 'delta' && event.text) {
          onDelta(event.text);
        } else if (event?.type === 'completed' && event.conversationId) {
          completedConversationId = event.conversationId;
          if (onSources) onSources(toSources(event.sources));
          if (onDocuments) onDocuments(toDocuments(event.documents));
        } else if (event?.type === 'error') {
          throw new ChatApiError(event.message ?? 'The response was interrupted. Please try again.');
        }
      }
    }

    if (!completedConversationId) {
      throw new ChatApiError('The response ended unexpectedly. Please try again.');
    }
    return completedConversationId;
  }

  private parseEvent(raw: string): StreamEvent | null {
    const data = raw
      .split('\n')
      .filter((line) => line.startsWith('data:'))
      .map((line) => line.slice(5).trimStart())
      .join('\n');
    if (!data) return null;
    try {
      return JSON.parse(data) as StreamEvent;
    } catch {
      return null;
    }
  }

  /** Turns an ASP.NET Core ProblemDetails / ValidationProblemDetails body into a short message. */
  private async readProblem(response: Response): Promise<string> {
    try {
      const body = await response.json();
      if (body?.errors) {
        return (Object.values(body.errors) as string[][]).flat().join(' ');
      }
      if (typeof body?.detail === 'string') return body.detail;
      if (typeof body?.title === 'string') return body.title;
    } catch {
      /* body was not JSON */
    }
    return `The chat service returned HTTP ${response.status}.`;
  }
}
