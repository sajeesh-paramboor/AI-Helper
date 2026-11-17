import { createApplication } from '@angular/platform-browser';
import { createCustomElement } from '@angular/elements';
import { getValues } from '@azure/api-management-custom-widgets-tools';
import { appConfig } from './app/app.config';
import { ChatWidget } from './app/chat-widget';

const defaultValues = {
  apiBaseUrl: ''
};

try {
  // Retrieve configuration from APIM Developer Portal context synchronously
  const values = getValues(defaultValues);
  if (values && values.apiBaseUrl) {
    const widgetElement = document.querySelector('momo-chat-widget');
    if (widgetElement) {
      widgetElement.setAttribute('api-base-url', values.apiBaseUrl);
    }
  }
} catch (err) {
  console.warn('Failed to load APIM custom widget values, using defaults.', err);
}

// Registers <momo-chat-widget>. Any page that loads the built script can use the element
createApplication(appConfig)
  .then((app) => {
    if (!customElements.get('momo-chat-widget')) {
      customElements.define('momo-chat-widget', createCustomElement(ChatWidget, { injector: app.injector }));
    }
  })
  .catch((err: unknown) => console.error('Angular custom elements bootstrap failed:', err));
