
@allowed([
  'test'
  'qa'
  'prod'
])
param environment string = 'test'
param lastReview string = utcNow('yyyy-MM-dd')
param location string = 'norwayeast'

targetScope = 'subscription'

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: 'rg-log-common-${environment}'
  location: location
}


module la './resources/la.bicep' = {
  name: 'log-analytics-${environment}'
  params: {
    name: 'log-common-${environment}'
    location: location
  }
  scope: rg
}
