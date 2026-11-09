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

Use the **Default domain** shown on the Function App's overview page (or the `functionAppHostName` deployment output) as the host.

## Setup

### 1. DNS zone and record

Create the DNS zone in Azure DNS (if it doesn't exist yet) and add an A record for each hostname you want to update. The IP doesn't matter, the first update replaces it.

### 2. Infrastructure

`infra/main.bicep` is deployed at subscription scope and creates one environment:

- the resource group for the environment (`rg-dnsupdater-<env>` by default)
- a user-assigned managed identity the app runs as. It is created first and gets all of its roles before the app exists, so the app has its permissions from the first start.
- a Function App on the Flex Consumption plan (Linux, .NET 10 isolated worker) that uses this identity for storage, monitoring, Key Vault and DNS
- a storage account for the Functions host and the deployment package, used without account keys
- Application Insights with a Log Analytics workspace
- a Key Vault (RBAC mode) with the DynDNS password. The app setting only holds a Key Vault reference, and the app identity can read this one secret and nothing else in the vault.
- the **DNS Zone Contributor** role for the app identity on the resource group with your DNS zones, so the app needs no service principal or client secret
- optionally, the **Website Contributor** role on the Function App for your existing GitHub deploy identity (see step 3)

The per-environment values are in `infra/prod.bicepparam` and `infra/dev.bicepparam`. Before the first deployment, set `dnsZoneResourceGroupName` in both files to the resource group that holds your DNS zones, and `dnsZoneSubscriptionId` if that resource group is in a different subscription than the one you deploy to. Also check `location`: not every region offers Flex Consumption, `az functionapp list-flexconsumption-locations` lists the ones that do.

The account that deploys needs:

- on the subscription you deploy to: Contributor plus Role Based Access Control Administrator (or Owner), to create the resource group, the resources and their role assignments
- on the DNS resource group: Role Based Access Control Administrator, plus permission to create deployments there (`Microsoft.Resources/deployments/write`, which DNS Zone Contributor includes). If the zones are in the subscription you deploy to, the subscription roles already cover this.

The param files read the DynDNS credentials your router sends from `DYNDNS_USER` and `DYNDNS_SECRET`, for example from a git-ignored `.env` file:

```sh
set -a; source .env; set +a

# A subscription deployment goes to the CLI's current subscription, so pick the one for this environment first
az account set --subscription <dev-subscription-id>

az deployment sub what-if --location westeurope --parameters infra/dev.bicepparam
az deployment sub create --name dnsupdater-dev --location westeurope --parameters infra/dev.bicepparam
```

For prod, use `infra/prod.bicepparam` and the deployment name `dnsupdater-prod`. To deploy from GitHub Actions instead, see step 4. The outputs contain the resource group, the Function App's name and host name, and the Key Vault name:

```sh
az deployment sub show --name dnsupdater-dev --query properties.outputs
```

The template configures one DynDNS user and owns all app settings: settings added by hand in the portal are removed by the next deployment.

To change the DynDNS password, deploy again with the new `DYNDNS_SECRET`. This stores a new version of the secret, which the app picks up within 24 hours. To apply it right away:

```sh
APP_ID=$(az functionapp show --name <functionAppName> --resource-group rg-dnsupdater-<env> --query id -o tsv)
az rest --method post --url "$APP_ID/config/configreferences/appsettings/refresh?api-version=2022-03-01"
```

Reading the secret yourself needs a Key Vault data role such as *Key Vault Secrets User*; Owner on the resource group doesn't include data access.

> The Linux Consumption plan doesn't support .NET 10 (.NET 9 is the last version it gets). An existing app on that plan is replaced by the new Flex Consumption app, see [Migrate Consumption plan apps to the Flex Consumption plan](https://learn.microsoft.com/en-us/azure/azure-functions/migration/migrate-plan-consumption-to-flex).

### 3. Deployment with GitHub Actions

The workflow in `.github/workflows/master_azure-dns-updater.yml` builds the function and deploys it to prod on every push to `master`. The job runs in the GitHub environment `prod` (*Settings > Environments*) and takes its secrets and the app name from there. It logs in to Azure with OpenID Connect as an existing user-assigned managed identity, so no deployment secret is stored in GitHub. The Bicep template doesn't create this identity.

1. Because the job runs in the `prod` environment, the identity needs a federated credential with the subject `repo:<owner>/<repo>:environment:prod`. If the same identity also deploys the infrastructure, the credential from step 4 already covers this.

   ```sh
   az identity federated-credential create --name azure-dnsupdater-prod \
     --identity-name <identity> --resource-group <identity-resource-group> \
     --issuer https://token.actions.githubusercontent.com \
     --subject repo:<owner>/<repo>:environment:prod \
     --audiences api://AzureADTokenExchange
   ```

2. It needs **Website Contributor** (or more) on the Function App. If it doesn't already have that through the resource group or subscription, set `githubDeployPrincipalId` in the param file to its principal ID and deploy again.
3. Add its client ID, your tenant ID and the prod subscription ID as the secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` of the `prod` environment.
4. Add the variable `AZURE_FUNCTIONAPP_NAME` to the `prod` environment, set to the `functionAppName` output. The workflow stops with an error if it's missing.

Restrict the `prod` environment to the `master` branch: the workflow can also be started by hand, and without that rule it could deploy prod from any branch.

More details: [Deploy to Azure Functions by using GitHub Actions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-how-to-github-actions).

### 4. Infrastructure from GitHub Actions (optional)

`.github/workflows/infra.yml` runs the Bicep deployment from step 2 in GitHub:

- Pull requests that change `infra/` get a what-if against dev in the job summary.
- *Actions > Deploy infrastructure > Run workflow* deploys dev or prod: it runs a what-if, then the deployment, and writes the outputs to the job summary.

Setup:

1. Use a separate identity for the infrastructure, not the one that deploys the app. It creates resource groups and role assignments, so it needs **Contributor** plus **Role Based Access Control Administrator** (or Owner) on the subscription. You can limit the RBAC Administrator role with a condition to the roles the template assigns.
2. Jobs that run in a GitHub environment get the OIDC subject `repo:<owner>/<repo>:environment:<env>`, so the identity needs one federated credential per environment:

   ```sh
   for env in dev prod; do
     az identity federated-credential create --name "azure-dnsupdater-$env" \
       --identity-name <iac-identity> --resource-group <identity-resource-group> \
       --issuer https://token.actions.githubusercontent.com \
       --subject "repo:<owner>/<repo>:environment:$env" \
       --audiences api://AzureADTokenExchange
   done
   ```

3. Give both GitHub environments, `dev` and `prod`, the secrets `DYNDNS_USER` and `DYNDNS_SECRET`.
4. Add `AZURE_IAC_CLIENT_ID` (the identity's client ID), `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` as secrets of each environment, so dev and prod can use different subscriptions and identities.

The workflow records the deployment in `westeurope` (`DEPLOYMENT_LOCATION`); the resources use the `location` from the param file.

### Configuration reference

The app reads these settings. In Azure the Bicep template sets them, locally they come from user secrets (see below).

| Setting | Value |
|---|---|
| `AzureAD__subscriptionId` | ID of the subscription that holds the DNS zones |
| `rgName` | Name of the resource group that holds the DNS zones |
| `Authorization__0__user` | User name your DynDNS client sends |
| `Authorization__0__secret` | Password your DynDNS client sends. In Azure, a Key Vault reference to the `dyndns-secret` secret. |
| `AzureAD__managedIdentityClientId` | Client ID of the user-assigned managed identity the app signs in to Azure DNS with. Without it, the system-assigned identity is used. |
| `AzureAD__tenantId`, `AzureAD__clientId`, `AzureAD__secret` | Optional app registration. When `AzureAD__secret` is set, the app uses it instead of a managed identity. |

To add more users, increment the index (`Authorization__1__user`, `Authorization__1__secret` and so on). In Azure, add them to the app settings in `infra/main.bicep`, otherwise the next deployment removes them.

## Local development

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

### Service principal

Locally there is no managed identity, so the Console app, `func start` and the live test use an app registration (service principal) to change DNS records. Full walk-through: [Create a Microsoft Entra application and service principal](https://learn.microsoft.com/en-us/entra/identity-platform/howto-create-service-principal-portal).

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

### Secrets

The Console, Function and Tests projects share the same user secrets, so you only set them once:

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

## Tests

```sh
dotnet test
```

`DNSUpdater.Tests` covers the HTTP endpoint (authentication, required parameters, status codes) and the update logic against a faked Azure DNS zone, so these tests need no Azure access. The tests use xUnit v3, which needs `dotnet test` in Microsoft.Testing.Platform mode; `global.json` turns that on.

### Live test against Azure DNS

`LiveAzureDnsTests` runs the update logic against your real zone, using the secrets from above. It is skipped unless `LiveTest:fqdn` is set. Point it at a dedicated A record, because the test overwrites its IP with documentation addresses (`192.0.2.1`, then `192.0.2.2`):

```sh
dotnet user-secrets set "LiveTest:fqdn" "dnsupdater-test.example.com" --project DNSUpdater.Tests
dotnet test
```

Environment variables work as well, for example `LiveTest__fqdn`.

## Project structure

| Project | Purpose |
|---|---|
| `DNSUpdater.Library` | Update logic, talks to Azure DNS through `Azure.ResourceManager.Dns` |
| `DNSUpdater.Function` | HTTP endpoint, Azure Functions isolated worker |
| `DNSUpdater.Console` | Runs the update logic locally for testing |
| `DNSUpdater.Tests` | Unit tests, plus an opt-in live test against Azure DNS |
| `infra` | Bicep template and per-environment parameters for the Azure resources |
