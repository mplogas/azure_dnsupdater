param tags object
param location string
param name string

resource asp 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Serverless'
    size: 'Y1'
    family: 'Y'
    capacity: 1
}
  kind: 'elastic'
  properties: {
    perSiteScaling: false
    elasticScaleEnabled: true
    maximumElasticWorkerCount: 1
    isSpot: false
    reserved: true
    isXenon: false
    hyperV: false
    targetWorkerCount: 0
    targetWorkerSizeId: 0
    zoneRedundant: false
  }
}

output asp object = {
  id: asp.id
  name: asp.name
}
