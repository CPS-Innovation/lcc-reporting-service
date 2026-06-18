# LCC Reporting Service

## Introduction

The LCC Reporting Service will deliver data on the Large and Complex Cases (LCC) application, supporting BI reporting on tool success rates and offering insights into benefits realisation.

## CICD

### Adding Configuration Variables

App settings are configured in the App Service environment during the deployment pipeline, by calling the step defined in [fa-config-steps.yml](devops-pipelines/templates/fa-config-steps.yml). 

New variable blocks can be added to the `appSettings` list in the task's input variables. The "slotSetting" field should always be set to false.

**Secret values:** 

For settings such as api keys, passwords, certificates, etc., the "value" field should contain a [Key Vault reference](https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references?tabs=azure-cli). For example:
```json
{
  "name": "Api__AccessKey",
  "value": "@Microsoft.KeyVault(VaultName=${{ parameters.keyVaultName }};SecretName=Api--AccessKey)",
  "slotSetting": false
},
``` 
❗ The key-value pairs for these secrets must be added to the Key Vaults by a team member with appropriate access.

**Non-sensitive cross-environment values:**

Non-sensitive values that remain consistent across all environments can be added in plain text. For example:
```json
{
  "name": "FUNCTIONS_EXTENSION_VERSION",
  "value": "~4",
  "slotSetting": false
},
```

**Sensitive values that don't require storing in Key Vault:** 

- Values such as UUIDs, internally-facing urls, etc., that shouldn't be exposed in a public repository, should be added as variable references. For Example:
  ```json
  {
    "name": "BlobStorageAccountUrl",
    "value": "$(BlobStorageAccountUrl)",
    "slotSetting": false
  },
  ```
  ❗ The key-value pairs must be added to the relevant variable groups in the Azure DevOps Library.

**Non-sensitive environment-specific values** 

Values that may differ by environment (e.g., feature flags) should also be added as variable references.

  The key-value pairs should be added to the following **variable template** yaml files:
  - [fa-config-dev.yml](devops-pipelines/templates/variables/fa-config-dev.yml)
  - [fa-config-staging.yml](devops-pipelines/templates/variables/fa-config-staging.yml)
  - [fa-config-prod.yml](devops-pipelines/templates/variables/fa-config-prod.yml)

  For example:
  ```yaml
  # File: fa-config-dev.yml
  variables:
  - name: TimeRangeInDays
    value: "1.0"
  ```
