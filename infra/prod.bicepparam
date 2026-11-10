using 'main.bicep'

param environmentName = 'prod'
param location = 'westeurope'

// Matches the AZURE_FUNCTIONAPP_NAME variable of the GitHub environment.
param functionAppName = 'azure-dnsupdater-v2-prod'

// Resource group that holds your Azure DNS zones. Set dnsZoneSubscriptionId as well if the zones
// are in a different subscription than the one you deploy to.
param dnsZoneResourceGroupName = 'dns'

// Comma-separated hostnames the app may update, from the DNS_HOSTNAMES secret of the GitHub environment,
// so the public repo doesn't point at them.
param dnsHostnames = split(readEnvironmentVariable('DNS_HOSTNAMES'), ',')

// Principal ID of the existing identity the GitHub workflow deploys with.
// Leave empty if it already has access to this resource group through a broader scope.
param githubDeployPrincipalId = ''

// Credentials for the DynDNS client, read from the shell so they never end up in git.
param dyndnsUser = readEnvironmentVariable('DYNDNS_USER')
param dyndnsSecret = readEnvironmentVariable('DYNDNS_SECRET')
