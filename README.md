# Azure DNS Updater
**Problem:** Domains are handled by Azure DNS. One (or multiple) subdomains are pointing to a dynamic (public) IP and need to be updated on IP change. 

**Solution:** An Azure Function that can be triggered by any authorized ddclient, basically DynDNS on Azure. ddclient is not required, as long as the [dynDNS API-specs](https://help.dyn.com/remote-access-api/perform-update/) are followed.

**Use-Case:** Ubiquiti USG / Ubiquiti UDM dynamic DNS feature.

    ![image](https://user-images.githubusercontent.com/842121/170864950-cf8e85b2-8dbb-4cb9-a284-f36d4f9bee2a.png)


### Setup

1. Set up you DNS Zones in Azure
2. Set up your Azure Function App (v4/.NET6, consumption plan, Application Insights enabled)

    ![image](https://user-images.githubusercontent.com/842121/170865030-fdb026b2-fb98-4d1f-af53-73e8c2f1657d.png)

3. Deploy this Azure Function to your Function App resource and configure Application Settings accordingly

### Configuration

#### configure a Service Principal

_detailed walk-through:_ https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal

##### quick guide

1. Register a new application in your AAD tenant and take note of the application id (a.k.a clientId)
    
    1. Give it a meaningful name
    2. Select Single tenant
    3. Do not provide a Redirect URI

2. Create a client secret and copy the value for later use (a.k.a secret)

    ![image](https://user-images.githubusercontent.com/842121/170866392-86ad8e7a-e425-42b8-b735-f7826f9502a2.png)

#### assign DNS Zone contributor permission to the Service Principal

_detailed walk-through:_ https://docs.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal?tabs=current

##### quick guide

1. Select "Access control (IAM)" in your DNS resource (or resource group if you have multiple DNS Zones that you want to modify)
2. Click on "Add role assignment"
3. Search for "DNS Zone Contributor", select it and click "Next"
4. Click "Select Members" and search for your Service Principal (either by name or object id) and select it
5. Click "Next" and then "Review + assign"

You can double check the success of your operation by providing your Service Principal name to the "Check access" form

    ![image](https://user-images.githubusercontent.com/842121/170866976-4086bbe0-ec17-4c70-a326-413fe17baf3a.png)

#### getting the remaining configuration items

- **tenantId** - you can get this from your AAD Overview page
- **subscriptionId** - the GUID of your subscription, can be found in the overview page of any resource
- **rgName** - the name of the resource group that holds your DNS Zone resources

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
