using 'main.bicep'

param environmentName = 'dev'
param location = 'westeurope'

// Matches the AZURE_FUNCTIONAPP_NAME variable of the GitHub environment.
param functionAppName = 'azure-dnsupdater-v2-dev'

// Resource group with the dev DNS zone, in the dev subscription. Dev never writes to the prod zones.
param dnsZoneResourceGroupName = 'dns'

// Comma-separated hostnames the app may update, from the DNS_HOSTNAMES secret of the GitHub environment,
// so the public repo doesn't point at them.
param dnsHostnames = split(readEnvironmentVariable('DNS_HOSTNAMES'), ',')

// Principal ID of the existing identity the GitHub workflow deploys with.
// Leave empty if it already has access to this resource group through a broader scope.
param githubDeployPrincipalId = ''

// Optional custom domain with a TLS certificate from Key Vault, from secrets of the GitHub environment.
// Without CUSTOM_DOMAIN the app only has its default *.azurewebsites.net host name.
param customDomainName = readEnvironmentVariable('CUSTOM_DOMAIN', '')
param customDomainKeyVaultId = readEnvironmentVariable('CUSTOM_DOMAIN_KEY_VAULT_ID', '')
param customDomainCertificateName = readEnvironmentVariable('CUSTOM_DOMAIN_CERTIFICATE', '')

// Credentials for the DynDNS client, read from the shell so they never end up in git.
param dyndnsUser = readEnvironmentVariable('DYNDNS_USER')
param dyndnsSecret = readEnvironmentVariable('DYNDNS_SECRET')

// Dev environments get torn down more often; a short retention frees the vault name sooner.
param keyVaultSoftDeleteRetentionInDays = 7
