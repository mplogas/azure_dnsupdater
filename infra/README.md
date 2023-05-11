# Provisioning dyndns API

## Pre Requisites

* [azure-cli](https://learn.microsoft.com/en-us/cli/azure/)

## Steps

### Steps for OldMacBlog

```bash
az account set -s "OldMacBlog"

az deployment sub create --template-file deploy-log-analytics.bicep --parameters environment=test --what-if
az deployment sub create --template-file deploy-log-analytics.bicep --parameters environment=test

az deployment sub create --template-file deploy-function.bicep --location norwayeast --parameters environment=test --parameters logAnalyticsSubscription=<id> --what-if
az deployment sub create --template-file deploy-function.bicep --location norwayeast --parameters environment=test --parameters logAnalyticsSubscription=<id>
```

Most likely you want to have a release from github actions. this release will require access to open the network access restrictions during the release. To do that job you will need a managed identity (MI). To create a managed identity do

```bash
az deployment group create --template-file deploy-cicd-user.bicep --parameters environment=test -g rg-dyndns-test
```

Finish off by reating the access rights as described in [Use GitHub Actions with User-Assigned Managed Identity](https://yourazurecoach.com/2022/12/29/use-github-actions-with-user-assigned-managed-identity/)
