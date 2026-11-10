// Infrastructure for one environment of the Azure DNS Updater, deployed at subscription scope:
// creates the environment's resource group and everything in it (see modules/app.bicep).

targetScope = 'subscription'

@description('Environment name, used in resource names and tags.')
@allowed([
  'dev'
  'prod'
])
param environmentName string

@description('Location for all resources. The region must support the Flex Consumption plan.')
param location string

@description('Short base name for all resources.')
@maxLength(12)
param appName string = 'dnsupdater'

@description('Resource group to create for this environment.')
param resourceGroupName string = 'rg-${appName}-${environmentName}'

@description('Name of the Function App. Must be globally unique. Leave empty to generate one with a random suffix.')
param functionAppName string = ''

@description('Resource group with the Azure DNS zones the app updates.')
param dnsZoneResourceGroupName string

@description('Hostnames the app may update, for example home.contoso.com. Each needs an existing A record in a zone in dnsZoneResourceGroupName. The app identity gets DNS Zone Contributor on each record set and no access to the rest of the zones, so the app answers nohost for every other hostname.')
@minLength(1)
param dnsHostnames string[]

@description('Subscription that holds the DNS zones. Defaults to the subscription you deploy to.')
param dnsZoneSubscriptionId string = subscription().subscriptionId

@description('User name the DynDNS client sends.')
param dyndnsUser string

@description('Password the DynDNS client sends. Stored in Key Vault, the app setting only holds a reference to it.')
@secure()
param dyndnsSecret string

@description('Days a deleted Key Vault stays recoverable (7 to 90). Can only be set when the vault is created.')
@minValue(7)
@maxValue(90)
param keyVaultSoftDeleteRetentionInDays int = 90

@description('Principal ID of the existing identity GitHub Actions deploys with. Gets Website Contributor on the Function App. Leave empty if it already has access through a broader scope.')
param githubDeployPrincipalId string = ''

@description('Maximum number of instances (1 to 1000).')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 40

@description('Memory per instance in MB.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 512

var tags = {
  application: appName
  environment: environmentName
}

// Like the app, take the zone from the last two labels: home.contoso.com is record home in zone contoso.com.
var hostnameLabels = [for hostname in dnsHostnames: split(trim(hostname), '.')]
var dnsRecords = [
  for labels in hostnameLabels: {
    zoneName: join(skip(labels, length(labels) - 2), '.')
    recordName: join(take(labels, length(labels) - 2), '.')
  }
]

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module app 'modules/app.bicep' = {
  name: '${appName}-app-${environmentName}'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    appName: appName
    functionAppName: functionAppName
    tags: tags
    dnsZoneResourceGroupName: dnsZoneResourceGroupName
    dnsZoneSubscriptionId: dnsZoneSubscriptionId
    dnsRecords: dnsRecords
    dyndnsUser: dyndnsUser
    dyndnsSecret: dyndnsSecret
    keyVaultSoftDeleteRetentionInDays: keyVaultSoftDeleteRetentionInDays
    githubDeployPrincipalId: githubDeployPrincipalId
    maximumInstanceCount: maximumInstanceCount
    instanceMemoryMB: instanceMemoryMB
  }
}

output resourceGroupName string = resourceGroup.name
output functionAppName string = app.outputs.functionAppName
output functionAppHostName string = app.outputs.functionAppHostName
output keyVaultName string = app.outputs.keyVaultName
