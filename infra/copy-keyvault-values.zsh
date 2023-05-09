#!/usr/bin/env zsh
# Source: https://www.c-sharpcorner.com/blogs/how-to-copy-all-secrets-from-one-azure-keyvault-to-another-azure-keyva

set -eo pipefail

SRC_ENV=$1
DST_ENV=$2

Source_Kv_Name=${3:-"kv-dyndns-${SRC_ENV}"}
Dest_Kv_Name=${4:-"kv-dyndns-${DST_ENV}"}

typeset -A SUBSCRIPTION_NAMES
SUBSCRIPTION_NAMES=("test" "Test Environment" "qa" "QA Environment" "prod" "Production Environment")

az account set -s "${SUBSCRIPTION_NAMES[$SRC_ENV]}"
SECRETS+=($(az keyvault secret list --vault-name $Source_Kv_Name --query "[].id" -o tsv))

for SECRET in "${SECRETS[@]}"; do
    SECRETNAME=$(echo "$SECRET" | sed 's|.*/||')

    az account set -s "${SUBSCRIPTION_NAMES[$DST_ENV]}"

    SECRET_CHECK=$(az keyvault secret list --vault-name $Dest_Kv_Name --query "[?name=='$SECRETNAME']" -o tsv)
    if [ -n "$SECRET_CHECK" ]
    then
        echo "$SECRETNAME already exists in $Dest_Kv_Name"
    else
        echo "Copying $SECRETNAME from Source KeyVault: $Source_Kv_Name to Destination KeyVault: $Dest_Kv_Name"


        echo "Setting ${SUBSCRIPTION_NAMES[$SRC_ENV]}"
        az account set -s "${SUBSCRIPTION_NAMES[$SRC_ENV]}"

        SECRET=$(az keyvault secret show --vault-name $Source_Kv_Name -n $SECRETNAME --query "value" -o tsv)


        echo "Setting ${SUBSCRIPTION_NAMES[$DST_ENV]}"
        az account set -s "${SUBSCRIPTION_NAMES[$DST_ENV]}"

        az keyvault secret set --vault-name $Dest_Kv_Name -n $SECRETNAME --value "$SECRET" >/dev/null
    fi
done