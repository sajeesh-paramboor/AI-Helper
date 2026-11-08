import { createApplication } from '@angular/platform-browser';
import { createCustomElement } from '@angular/elements';
import { appConfig } from './app/app.config';
import { ChatWidget } from './app/chat-widget';

// Registers <momo-chat-widget>. Any page that loads the built script can use the element, e.g.
//   <momo-chat-widget api-base-url="https://api.example.com"></momo-chat-widget>
createApplication(appConfig)
  .then((app) => {
    if (!customElements.get('momo-chat-widget')) {
      customElements.define('momo-chat-widget', createCustomElement(ChatWidget, { injector: app.injector }));
    }
  })
  .catch((err) => console.error(err));
