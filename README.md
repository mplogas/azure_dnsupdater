# Azure DNS Updater
**Problem:** Domains are handled by Azure DNS. One (or multiple) subdomains are pointing to a dynamic (public) IP and need to be updated on IP change. 

**Solution:** An Azure Function that can be triggered by any authorized ddclient, basically DynDNS on Azure. ddclient is not required, as long as the [dynDNS API-specs](https://help.dyn.com/remote-access-api/perform-update/) are followed.

**Use-Case:** Ubiquiti USG / Ubiquiti UDM dynamic DNS feature.

### Setup

1. Set up you DNS Zones in Azure
2. Set up a Service Principal (https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal)
3. Use RBAC to assign the role 'DNS Zone Contributor' to the service principal in your DNS resource group (https://docs.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal)
4. Create an Azure function in Azure (consumption plan works fine)
5. Deploy to your Azure Function

### Configuration

#### local testing: 
Set up your secrets.json with the following keys. 


```json
"AzureAD": {
    "tenantId": "",
    "clientId": "",
    "secret": "",
    "subscriptionId": ""
},  
"Authorization": [{
    "user": "",
    "secret": ""
}],
"rgName": ""
```
_Note: you can provide multiple users_

#### production:
Add the following keys to your AppSettings. 
 
 ```txt
 AzureAD__tenantId
 AzureAD__clientId
 AzureAD__secret
 AzureAD__subscriptionId
 Authorization__0__user
 Authorization__0__secret
 rgName
 ```
_Note: to add multiple users, increment the array index (e.g. Authorization__0__user to Authorization__1__user)_
