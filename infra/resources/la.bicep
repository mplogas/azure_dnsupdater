param name string
param location string

resource la 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: name
  location: location
}
