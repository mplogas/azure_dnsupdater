// Grants DNS Zone Contributor on single A record sets in the resource group this module is deployed to,
// which can be a different resource group than the Function App's. Scoped to the record set, the role
// covers only that record: the identity can't read or change anything else in the zone.

targetScope = 'resourceGroup'

@export()
@description('An A record set the app may update. It must exist before the deployment.')
type dnsARecord = {
  @description('DNS zone, for example contoso.com.')
  zoneName: string

  @description('Record set name relative to the zone, for example home for home.contoso.com.')
  recordName: string
}

@description('Principal ID of the managed identity that may update the record sets.')
param principalId string

@description('Resource ID of the same identity. Used to name the role assignments: unlike the principal ID it is known before the deployment runs, so what-if can preview them.')
param identityResourceId string

@description('A record sets in this resource group the identity may update.')
@minLength(1)
param records dnsARecord[]

var dnsZoneContributorRoleId = 'befefa01-2a29-4197-83a8-272ff33ce314'

resource aRecords 'Microsoft.Network/dnsZones/A@2018-05-01' existing = [
  for record in records: {
    name: '${record.zoneName}/${record.recordName}'
  }
]

resource roleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (record, i) in records: {
    name: guid(aRecords[i].id, identityResourceId, dnsZoneContributorRoleId)
    scope: aRecords[i]
    properties: {
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', dnsZoneContributorRoleId)
      principalId: principalId
      principalType: 'ServicePrincipal'
    }
  }
]
