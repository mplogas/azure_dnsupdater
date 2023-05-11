
@allowed([
  'test'
  'qa'
  'prod'
])
param environment string = 'test'
param lastReview string = utcNow('yyyy-MM-dd')
param location string = 'norwayeast'

targetScope = 'resourceGroup'


var tags = {
  'last-review': lastReview
  environment: environment
}

resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-githubactions-${environment}'
  location: location
  tags: tags
}
