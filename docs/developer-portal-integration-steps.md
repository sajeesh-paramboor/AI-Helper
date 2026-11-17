# Azure APIM Developer Portal Widget Integration Guide

This guide describes how to integrate the custom Angular-based **MoMo Chat Assistant** widget into the Azure API Management (APIM) Developer Portal.

---

## 1. Prerequisites

Before you begin, ensure you have:
- Node.js (version 22.22+ or 24.15+) installed.
- .NET 10 SDK installed (for running and hosting the backend API).
- Administrator access to the target Azure API Management (APIM) instance.
- Azure CLI or subscription-level permission to run deployment scripts that authenticate with Microsoft Entra ID.

---

## 2. Configuration & Integration Files

We have set up the following files to make the Angular widget portal-ready:

| File | Path | Description |
|---|---|---|
| **Main Bootstrap** | [`web/chat-widget/src/main.ts`](../web/chat-widget/src/main.ts) | Integrates `@azure/api-management-custom-widgets-tools` to fetch values dynamically from APIM. |
| **Identity Manifest** | [`web/chat-widget/public/config.msapim.json`](../web/chat-widget/public/config.msapim.json) | Identifies the custom widget name, display name, and Angular technology to APIM. |
| **Admin Editor** | [`web/chat-widget/public/editor.html`](../web/chat-widget/public/editor.html) | Simple admin panel loaded by the APIM Portal Designer to configure the API base URL. |
| **Deploy Script** | [`web/chat-widget/deploy.js`](../web/chat-widget/deploy.js) | Node.js script that connects to Azure and uploads the compiled assets to APIM blob hosting. |

---

## 3. Step-by-Step Integration & Deployment

### Step 3.1: Install APIM Custom Widget Dependencies
From the `web/chat-widget` directory, run:
```powershell
npm install
```

### Step 3.2: Configure your APIM Service Details
Open [`web/chat-widget/deploy.js`](../web/chat-widget/deploy.js) and update the `serviceInformation` block with your Azure details:
```javascript
const serviceInformation = {
  "resourceId": "subscriptions/<your-subscription-id>/resourceGroups/<your-resource-group>/providers/Microsoft.ApiManagement/service/<your-apim-instance-name>",
  "managementApiEndpoint": "https://management.azure.com"
};
```

### Step 3.3: Build the Portal Bundle
Compile the Angular widget with relative asset paths. This makes the compiled files hostable on APIM's blob storage:
```powershell
npm run build:portal
```
*This command executes `ng build --base-href ./ --output-path dist/portal` and compiles code directly to `dist/portal/browser/`.*

### Step 3.4: Deploy to Azure APIM Portal
Run the deployment script. It will open an interactive browser window to authenticate with your Microsoft Entra ID account:
```powershell
node deploy.js
```
Upon successful login, the script uploads all files from `./dist/portal/browser/` directly into your APIM Developer Portal's custom widget repository.

---

## 4. Setting up inside the Developer Portal Designer

Once the deployment is complete, place the widget onto the portal page:

1. In the Azure Portal, open your **API Management** instance.
2. Under the **Developer portal** section on the left-hand menu, click **Developer portal** to load the designer.
3. Navigate to the page where you wish to embed the assistant (such as the `Home` or `APIs` page).
4. Select the **Widgets** sidebar menu, or click the **Add section (+)** button inside a page container.
5. Search or scroll down to find the **Custom widgets** category.
6. Drag and drop the **MoMo Chat Assistant** widget onto the page layout.
7. Click the newly placed widget to open its properties:
   - In the text field, input your deployed ASP.NET Core Chat API's base URL (e.g., `https://momo-assistant-api.azurewebsites.net`).
8. Click **Save** and then click **Publish** from the top menu.

---

## 5. Security and Cross-Origin (CORS) Configuration

Because the custom widget resides in an iframe hosted on the developer portal, you must configure CORS on your backend ASP.NET Core Chat API to allow incoming requests:

1. Identify the origin of your APIM Developer Portal (e.g., `https://<your-apim-service-name>.developer.azure-api.net`).
2. Add this origin to the allowed list in your backend API configuration or Azure App Service CORS settings.
3. Since the widget and the parent portal run on different origins, **never expose security credentials, access keys, or tenant IDs in the frontend**. The widget connects safely to your ASP.NET Core backend, which acts as a secure, authenticated boundary calling Azure AI Foundry.

---

## 6. Layout Sizing Notes
The chat launcher uses fixed positioning within the iframe. In order to ensure the chat panel opens fully and doesn't appear cut off:
- Set the custom widget container width to `100%` or at least `400px`.
- Set the custom widget container height to at least `600px` (or use a dynamic CSS wrapper) to accommodate the full open chat interface.
