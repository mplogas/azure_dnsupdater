param environment string
param location string = resourceGroup().location
param tags object

param logAnalyticsSubscription string

var logAnalyticsIds = {
  test: '/subscriptions/${logAnalyticsSubscription}/resourceGroups/rg-log-common-test/providers/Microsoft.OperationalInsights/workspaces/log-common-test'
  qa: '/subscriptions/${logAnalyticsSubscription}/resourceGroups/rg-log-common-qa/providers/Microsoft.OperationalInsights/workspaces/log-common-qa'
  prod: '/subscriptions/${logAnalyticsSubscription}/resourcegroups/rg-log-common-test/providers/microsoft.operationalinsights/workspaces/log-common-prod'
}

var logAnalyticsExternalId = logAnalyticsIds[environment]

targetScope = 'resourceGroup'


param keyVaultName string = 'kv-dyndns-${environment}'

param applicationInsightsName string = 'appi-dyndns-${environment}'


resource ai 'microsoft.insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Redfield'
    Request_Source: 'IbizaAIExtensionEnablementBlade'
    RetentionInDays: 90
    WorkspaceResourceId: logAnalyticsExternalId
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

resource kv 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    accessPolicies: []
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enableRbacAuthorization: true
    provisioningState: 'Succeeded'
    publicNetworkAccess: 'Enabled'
    tenantId: subscription().tenantId
  }
}
