const { deployNodeJS } = require("@azure/api-management-custom-widgets-tools");

const serviceInformation = {
  // Configured with subscription ID, resource group 'Testsajeesh', and APIM service 'APIM-developerP' from Azure Portal
  "resourceId": "subscriptions/9e7339fa-c0bf-4f4d-a4af-77944b7f143f/resourceGroups/Testsajeesh/providers/Microsoft.ApiManagement/service/APIM-developerP",
  "managementApiEndpoint": "https://management.azure.com"
};

const name = "momo-chat-assistant";
const fallbackConfigPath = "./public/config.msapim.json";
const config = {
  "interactiveBrowserCredentialOptions": {
    "redirectUri": "http://localhost:1337",
    // Configured with your verified Microsoft Entra Tenant ID GUID
    "tenantId": "0c12ca4c-e626-415d-aeca-80e4e9239d9a"
  },
  "rootLocal": "./dist/portal/browser/"
};

console.log(`Starting deployment of custom APIM widget: "${name}"...`);
deployNodeJS(serviceInformation, name, fallbackConfigPath, config)
  .then(() => console.log("Deployment completed successfully!"))
  .catch((err) => console.error("Deployment failed:", err));
