// Everything inside the environment's resource group: the Function App on the Flex Consumption plan,
// its storage, monitoring and Key Vault, and the user-assigned identity the app runs as.

targetScope = 'resourceGroup'

param environmentName string
param location string
param appName string
param tags object

@description('Name of the Function App. Must be globally unique.')
param functionAppName string = 'func-${appName}-${environmentName}-${uniqueString(resourceGroup().id)}'

param dnsZoneResourceGroupName string
param dnsZoneSubscriptionId string
param dyndnsUser string

@secure()
param dyndnsSecret string

param keyVaultSoftDeleteRetentionInDays int
param githubDeployPrincipalId string
param maximumInstanceCount int
param instanceMemoryMB int

var resourceToken = uniqueString(resourceGroup().id)
var storageAccountName = take(toLower('st${replace(appName, '-', '')}${environmentName}${resourceToken}'), 24)
var keyVaultName = take('kv-${appName}-${environmentName}-${resourceToken}', 24)
var deploymentContainerName = 'app-package'

// Built-in role definition IDs.
var roles = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  monitoringMetricsPublisher: '3913510d-42f4-4e42-8a64-420c390055eb'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  websiteContributor: 'de139f84-1756-47ae-9be6-808fbbe84772'
}

// The identity the app runs as. It exists and has all its roles before the app is created,
// so storage, Key Vault references and DNS access work from the first start.

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}-${environmentName}'
  location: location
  tags: tags
}

// Monitoring

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}-${environmentName}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}-${environmentName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    DisableLocalAuth: true
  }
}

// Storage for the Functions host and the deployment package, accessed only with Entra ID

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }

  resource blobService 'blobServices' = {
    name: 'default'

    resource deploymentContainer 'containers' = {
      name: deploymentContainerName
    }
  }
}

// Key Vault for the DynDNS password

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    softDeleteRetentionInDays: keyVaultSoftDeleteRetentionInDays
    publicNetworkAccess: 'Enabled'
  }

  resource dyndnsSecretEntry 'secrets' = {
    name: 'dyndns-secret'
    properties: {
      value: dyndnsSecret
    }
  }
}

// Roles for the app identity

// Blob Data Owner is the minimum for the Functions host; Table Data Contributor lets it write diagnostic events.
var storageRoleIds = [
  roles.storageBlobDataOwner
  roles.storageTableDataContributor
]

resource storageRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in storageRoleIds: {
    name: guid(storage.id, appIdentity.id, roleId)
    scope: storage
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
      principalId: appIdentity.properties.principalId
      principalType: 'ServicePrincipal'
    }
  }
]

resource appInsightsRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(appInsights.id, appIdentity.id, roles.monitoringMetricsPublisher)
  scope: appInsights
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.monitoringMetricsPublisher)
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource secretReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault::dyndnsSecretEntry.id, appIdentity.id, roles.keyVaultSecretsUser)
  scope: keyVault::dyndnsSecretEntry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

module dnsAccess 'dns-zone-contributor.bicep' = {
  name: 'dns-zone-contributor-${environmentName}'
  scope: resourceGroup(dnsZoneSubscriptionId, dnsZoneResourceGroupName)
  params: {
    principalId: appIdentity.properties.principalId
    identityResourceId: appIdentity.id
  }
}

// Function App

resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'plan-${appName}-${environmentName}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-11-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  // Wait for every role of the app identity, so the app has its permissions when it first starts.
  dependsOn: [
    storageRoleAssignments
    appInsightsRoleAssignment
    secretReaderRoleAssignment
    dnsAccess
  ]
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: appIdentity.id
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__clientId'
          value: appIdentity.properties.clientId
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: storage.properties.primaryEndpoints.blob
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: storage.properties.primaryEndpoints.queue
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: storage.properties.primaryEndpoints.table
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING'
          value: 'Authorization=AAD;ClientId=${appIdentity.properties.clientId}'
        }
        // Read by DNSUpdater.Library to find the DNS zones and sign in to Azure DNS with the app identity.
        {
          name: 'AzureAD__subscriptionId'
          value: dnsZoneSubscriptionId
        }
        {
          name: 'AzureAD__managedIdentityClientId'
          value: appIdentity.properties.clientId
        }
        {
          name: 'rgName'
          value: dnsZoneResourceGroupName
        }
        {
          name: 'Authorization__0__user'
          value: dyndnsUser
        }
        {
          // Versionless, so a new secret version is picked up without changing this setting.
          name: 'Authorization__0__secret'
          value: '@Microsoft.KeyVault(SecretUri=${keyVault::dyndnsSecretEntry.properties.secretUri})'
        }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: appIdentity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
  }
}

// Lets the existing GitHub Actions identity deploy code to this app. It gets no access to storage data,
// the secret or DNS.
resource deployRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(githubDeployPrincipalId)) {
  name: guid(functionApp.id, githubDeployPrincipalId, roles.websiteContributor)
  scope: functionApp
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.websiteContributor)
    principalId: githubDeployPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
output keyVaultName string = keyVault.name
