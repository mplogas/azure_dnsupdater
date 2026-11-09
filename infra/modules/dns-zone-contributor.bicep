// Grants DNS Zone Contributor on the resource group this module is deployed to,
// which can be a different resource group than the Function App's.

targetScope = 'resourceGroup'

@description('Principal ID of the managed identity that may update the DNS zones in this resource group.')
param principalId string

@description('Resource ID of the same identity. Used to name the role assignment: unlike the principal ID it is known before the deployment runs, so what-if can preview the assignment.')
param identityResourceId string

var dnsZoneContributorRoleId = 'befefa01-2a29-4197-83a8-272ff33ce314'

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, identityResourceId, dnsZoneContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', dnsZoneContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
