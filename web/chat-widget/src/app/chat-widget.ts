import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { ChatApiError, ChatApiService } from './chat-api.service';
import { DocumentRef } from './document-refs';
import { Source, stripInlineSourceLinks } from './source-links';

interface Message {
  role: 'user' | 'assistant';
  text: string;
  /** Web sources cited for this answer (web search only). */
  sources?: Source[];
  /** Documents cited for this answer (file search only). */
  documents?: DocumentRef[];
}

/**
 * Embeddable chat widget. Rendered in a Shadow DOM so the host page's CSS cannot affect it
 * (and its CSS cannot leak out), which is what makes it safe to drop into a developer portal.
 * Registered as the <momo-chat-widget> custom element in main.ts.
 */
@Component({
  selector: 'momo-chat-widget',
  templateUrl: './chat-widget.html',
  styleUrl: './chat-widget.css',
  encapsulation: ViewEncapsulation.ShadowDom,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatWidget {
  /** Base URL of the chat API. Empty means same origin (the dev proxy handles it locally). */
  readonly apiBaseUrl = input('');

  protected readonly suggestions = [
    'How to generate an access token?',
    'How to create an invoice?',
    'Explain the RequestToPay API flow?',
    'Explain callback configuration?',
    'Why am I getting HTTP 401/403?',
  ];

  protected readonly open = signal(false);
  protected readonly messages = signal<Message[]>([]);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Off by default: web search adds cost and sends the model's search query to a public search service. */
  protected readonly webSearch = signal(false);
  /** Off by default: file search adds cost and the question text is used to search the indexed documents. */
  protected readonly fileSearch = signal(false);

  private readonly api = inject(ChatApiService);
  private readonly list = viewChild<ElementRef<HTMLElement>>('list');
  /** The server's conversation ID. The server remembers the model's context; the widget only holds this ID. */
  private conversationId: string | null = null;
  private abort: AbortController | null = null;

  constructor() {
    // Keep the newest text in view while the answer streams in.
    afterRenderEffect(() => {
      this.messages();
      const el = this.list()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLInputElement).value);
  }

  protected async send(text?: string): Promise<void> {
    const message = (text ?? this.draft()).trim();
    if (!message || this.busy()) return;

    this.draft.set('');
    this.error.set(null);
    this.messages.update((m) => [...m, { role: 'user', text: message }, { role: 'assistant', text: '' }]);
    this.busy.set(true);
    this.abort = new AbortController();

    try {
      this.conversationId = await this.api.stream(
        this.apiBaseUrl(),
        message,
        this.conversationId,
        (delta) => this.appendToAnswer(delta),
        this.abort.signal,
        this.webSearch(),
        (sources) => this.attachSources(sources),
        this.fileSearch(),
        (documents) => this.attachDocuments(documents),
      );
    } catch (e) {
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        if (e instanceof ChatApiError && e.status === 404 && this.conversationId) {
          // The server no longer has this conversation (expired or restarted). The next message starts a new one.
          this.conversationId = null;
        }
        this.error.set(e instanceof ChatApiError ? e.message : 'Something went wrong. Please try again.');
        // Drop the empty answer bubble so only a real (partial) answer stays visible.
        this.messages.update((m) => (m.at(-1)?.text === '' ? m.slice(0, -1) : m));
      }
    } finally {
      this.busy.set(false);
      this.abort = null;
    }
  }

  protected newConversation(): void {
    this.abort?.abort();
    this.conversationId = null;
    this.messages.set([]);
    this.draft.set('');
    this.error.set(null);
  }

  protected close(): void {
    this.newConversation();
    this.open.set(false);
  }

  // Web search and file search are mutually exclusive here until the combined endpoint exists.
  protected toggleWebSearch(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.webSearch.set(checked);
    if (checked) this.fileSearch.set(false);
  }

  protected toggleFileSearch(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.fileSearch.set(checked);
    if (checked) this.webSearch.set(false);
  }

  /** Shows the cited document names under the answer. */
  private attachDocuments(documents: DocumentRef[]): void {
    if (documents.length === 0) return;
    this.messages.update((list) => {
      const copy = list.slice();
      const last = copy[copy.length - 1];
      copy[copy.length - 1] = { ...last, documents };
      return copy;
    });
  }

  /** Shows the cited sources under the answer and removes the inline links that the Sources list repeats. */
  private attachSources(sources: Source[]): void {
    if (sources.length === 0) return;
    this.messages.update((list) => {
      const copy = list.slice();
      const last = copy[copy.length - 1];
      copy[copy.length - 1] = { ...last, text: stripInlineSourceLinks(last.text, sources), sources };
      return copy;
    });
  }

  private appendToAnswer(delta: string): void {
    this.messages.update((list) => {
      const copy = list.slice();
      const last = copy[copy.length - 1];
      copy[copy.length - 1] = { ...last, text: last.text + delta };
      return copy;
    });
  }
}
