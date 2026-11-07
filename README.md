# Azure DNS Updater

DynDNS for domains hosted in Azure DNS. An Azure Function accepts update requests in the [DynDNS update format](https://help.dyn.com/perform-update.html) and points an A record at the caller's new public IP. Anything that speaks that protocol can use it, for example ddclient, inadyn or the dynamic DNS feature of Ubiquiti gateways (USG, UDM).

## How it works

1. Your router notices a new public IP and calls `GET /nic/update?hostname=home.example.com&myip=203.0.113.10` with Basic authentication.
2. The function checks the credentials against the users in its configuration.
3. It looks up `home.example.com` in the Azure DNS zone `example.com` and replaces the record's IP if it changed.

Limitations:

- Only **existing A records** are updated. Create the record once in Azure DNS (with any IP) before using it.
- The zone is taken from the last two labels of the hostname: `home.example.com` updates zone `example.com`. Zones like `example.co.uk` aren't supported.
- IPv4 only, one hostname per request.
- An update replaces all IPs in the record set with `myip` and sets the TTL to 300 seconds.

## API

```
GET https://<function-app-host>/nic/update?hostname=<fqdn>&myip=<ipv4>
Authorization: Basic <base64 of user:secret>
User-Agent: <any value>
```

All of `hostname`, `myip`, `Authorization` and `User-Agent` are required. Other parameters from the DynDNS spec are ignored.

| Response body | HTTP status | Meaning |
|---|---|---|
| `good` | 200 | The A record was updated. |
| `nochg` | 200 | The record already has this IP, nothing changed. |
| `badauth` | 401 | The credentials don't match a configured user. |
| `nohost` | 409 | No record with this name exists, or it isn't an A record. |
| `notfqdn` | 409 | The hostname isn't a valid domain name or has no subdomain. |
| `othererr` | 400 | A required parameter or header is missing. |
| `othererr` | 409 | The update failed, for example because the zone doesn't exist or `myip` isn't a valid IP address. Details are in the function logs. |

## Client setup (UniFi)

Select `dyndns` as the service and enter the hostname, the user and secret from the `Authorization` settings, and the host name of your Function App as the server.

![UniFi Dynamic DNS settings with service dyndns and the function app host as server](https://user-images.githubusercontent.com/842121/170864950-cf8e85b2-8dbb-4cb9-a284-f36d4f9bee2a.png)

> *Note: after migrating from my trusty old USG to a UDM-SE I had to set the server to `yourdyndns.azurewebsites.net/\/nic/update?hostname=%h&myip=%i`. For unknown reasons the UDM always creates a custom inadyn profile without a ddns path, regardless of the provider you select in the UI.*

Use the **Default domain** shown on the Function App's overview page as the host.

## Setup

### 1. DNS zone and record

Create the DNS zone in Azure DNS (if it doesn't exist yet) and add an A record for each hostname you want to update. The IP doesn't matter, the first update replaces it.

### 2. Service principal for DNS updates

The function uses a service principal to change DNS records. Full walk-through: [Create a Microsoft Entra application and service principal](https://learn.microsoft.com/en-us/entra/identity-platform/howto-create-service-principal-portal).

1. In Microsoft Entra ID, register a new application: give it a meaningful name, select *Single tenant* and leave the Redirect URI empty. Note the *Application (client) ID* and the *Directory (tenant) ID*.
2. Create a client secret and copy its value.

   ![Creating a client secret for the app registration](https://user-images.githubusercontent.com/842121/170866392-86ad8e7a-e425-42b8-b735-f7826f9502a2.png)

3. Give the service principal the **DNS Zone Contributor** role on the DNS zone, or on the resource group if you want to update several zones ([how to assign roles](https://learn.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal)):
   1. Open *Access control (IAM)* on the DNS zone or resource group and select *Add role assignment*.
   2. Search for *DNS Zone Contributor*, select it and click *Next*.
   3. Click *Select members*, search for your app registration and select it.
   4. Click *Review + assign*.

   You can verify the assignment with the *Check access* tab:

   ![Check access showing the DNS Zone Contributor assignment](https://user-images.githubusercontent.com/842121/170866976-4086bbe0-ec17-4c70-a326-413fe17baf3a.png)

### 3. Function App

Create a Function App with these settings:

| Setting | Value |
|---|---|
| Hosting plan | Flex Consumption |
| Runtime stack | .NET |
| Version | 10 (isolated worker model) |
| Application Insights | optional, but useful for reading the logs |

The Linux Consumption plan doesn't support .NET 10 (.NET 9 is the last version it gets), so an app created there needs to [move to Flex Consumption](https://learn.microsoft.com/en-us/azure/azure-functions/migration/migrate-plan-consumption-to-flex). Windows Consumption also works.

### 4. App settings

Add these settings under *Settings > Environment variables* in the Function App:

| Setting | Value |
|---|---|
| `AzureAD__tenantId` | Directory (tenant) ID of the app registration |
| `AzureAD__clientId` | Application (client) ID of the app registration |
| `AzureAD__secret` | Client secret value |
| `AzureAD__subscriptionId` | ID of the subscription that holds the DNS zones |
| `rgName` | Name of the resource group that holds the DNS zones |
| `Authorization__0__user` | User name your DynDNS client sends |
| `Authorization__0__secret` | Password your DynDNS client sends |

To add more users, increment the index: `Authorization__1__user`, `Authorization__1__secret` and so on.

### 5. Deployment with GitHub Actions

The workflow in `.github/workflows/master_azure-dns-updater.yml` builds and deploys the function on every push to `master`. It logs in to Azure with OpenID Connect, so no deployment secret is stored in GitHub. This identity only deploys code; it is separate from the service principal in step 2.

1. Create a user-assigned managed identity and give it the **Website Contributor** role on the Function App:

   ```sh
   az identity create --name github-deploy --resource-group <RESOURCE_GROUP>

   PRINCIPAL_ID=$(az identity show --name github-deploy --resource-group <RESOURCE_GROUP> --query principalId -o tsv)
   APP_ID=$(az functionapp show --name <APP_NAME> --resource-group <RESOURCE_GROUP> --query id -o tsv)
   az role assignment create --assignee "$PRINCIPAL_ID" --role "Website Contributor" --scope "$APP_ID"
   ```

2. Let GitHub Actions runs on `master` log in as this identity:

   ```sh
   az identity federated-credential create --name github-master \
     --identity-name github-deploy --resource-group <RESOURCE_GROUP> \
     --issuer https://token.actions.githubusercontent.com \
     --subject repo:<OWNER>/<REPO>:ref:refs/heads/master \
     --audiences api://AzureADTokenExchange
   ```

3. Add these repository secrets in GitHub (*Settings > Secrets and variables > Actions*):

   | Secret | Value |
   |---|---|
   | `AZURE_CLIENT_ID` | `az identity show --name github-deploy --resource-group <RESOURCE_GROUP> --query clientId -o tsv` |
   | `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
   | `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |

4. Set `AZURE_FUNCTIONAPP_NAME` in the workflow to the name of your Function App.

More details: [Deploy to Azure Functions by using GitHub Actions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-how-to-github-actions).

## Local development

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

### Secrets

The Console and Function projects share the same user secrets, so you only set them once:

```sh
cd DNSUpdater.Console
dotnet user-secrets set "AzureAD:tenantId" "<tenant id>"
dotnet user-secrets set "AzureAD:clientId" "<client id>"
dotnet user-secrets set "AzureAD:secret" "<client secret>"
dotnet user-secrets set "AzureAD:subscriptionId" "<subscription id>"
dotnet user-secrets set "rgName" "<resource group>"
dotnet user-secrets set "Authorization:0:user" "<user>"
dotnet user-secrets set "Authorization:0:secret" "<secret>"
```

### Console app

`DNSUpdater.Console` calls the update logic directly, without the HTTP layer. Set `fqdn` and `ip` in `DNSUpdater.Console/appsettings.json`, then run:

```sh
dotnet run --project DNSUpdater.Console
```

This changes the real DNS record. The app keeps running after the update; stop it with Ctrl+C.

### Function

Install the [Azure Functions Core Tools](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local) v4 and create `DNSUpdater.Function/local.settings.json` (it is git-ignored):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
```

`UseDevelopmentStorage=true` expects the [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite) storage emulator to be running. Then start the function and send a request:

```sh
cd DNSUpdater.Function
func start

curl -u "<user>:<secret>" "http://localhost:7071/nic/update?hostname=home.example.com&myip=203.0.113.10"
```

## Project structure

| Project | Purpose |
|---|---|
| `DNSUpdater.Library` | Update logic, talks to Azure DNS through `Azure.ResourceManager.Dns` |
| `DNSUpdater.Function` | HTTP endpoint, Azure Functions isolated worker |
| `DNSUpdater.Console` | Runs the update logic locally for testing |
