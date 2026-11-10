// Binds a custom domain to the Function App with a TLS certificate from Key Vault (SNI).
// Flex Consumption uses site-scoped certificates, so the certificate belongs to this app only.
// The certificate is imported without a version, so App Service picks up renewals in Key Vault.
//
// Before the deployment:
// - the App Service resource provider needs Key Vault Certificate User on the vault (see README.md)
// - DNS needs the TXT record asuid.<subdomain> with the app's custom domain verification ID.
//   The CNAME can still point elsewhere; switch it to the app once the binding exists.

targetScope = 'resourceGroup'

param functionAppName string
param location string

@description('Custom host name, for example dyndns.contoso.com.')
param hostName string

@description('Resource ID of the Key Vault that holds the certificate.')
param keyVaultId string

@description('Name of the certificate in that Key Vault. It must cover hostName.')
param certificateName string

resource functionApp 'Microsoft.Web/sites@2024-11-01' existing = {
  name: functionAppName
}

resource certificate 'Microsoft.Web/sites/certificates@2024-11-01' = {
  parent: functionApp
  name: certificateName
  location: location
  properties: {
    keyVaultId: keyVaultId
    keyVaultSecretName: certificateName
  }
}

resource hostNameBinding 'Microsoft.Web/sites/hostNameBindings@2024-11-01' = {
  parent: functionApp
  name: hostName
  properties: {
    siteName: functionApp.name
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: certificate.properties.thumbprint
  }
}
