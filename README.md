# azure_dnsupdater
An Azure Function that allows you to trigger an IP update (basically DynDNS on Azure, w/o the UI)

1. Set up you DNS Zones
2. Set up a Service Principal (https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal)
3. Use RBAC to assign the role 'DNS Zone Contributor' to the service principal in your DNS resource group (https://docs.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal)
4. Create an Azure function
5. Set up deployment and use the following setup

## For local testing: 
set up secrets.json and add following keys:

```json
  "AzureAD": {
    "tenantId": "",
    "clientId": "",
    "secret": "",
    "subscriptionId": ""
  },
  "secret": "",
  "rgName": ""
```

## for production:
 either use Azure KeyVault or configure your AppSettings
 
 ```txt
 AzureAD__tenantId
 AzureAD__clientId
 AzureAD__secret
 AzureAD__subscriptionId
 secret
 rgName
 ```
