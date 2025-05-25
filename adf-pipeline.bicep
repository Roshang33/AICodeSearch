// ARM Template converted to Bicep to deploy Azure Data Factory pipeline and dependencies
// Required resources: Key Vault, Storage Account, Batch Account, ADF factory, Linked Services, Pipeline

param location string = resourceGroup().location
param factoryName string = 'adf-github-faiss'
param storageAccountName string = 'githubfaissstorage'
param batchAccountName string = 'githubfaissbatch'
param kvName string = 'githubfaisskv'

resource kv 'Microsoft.KeyVault/vaults@2022-07-01' = {
  name: kvName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      name: 'standard'
      family: 'A'
    }
    accessPolicies: [] // Add policy manually or via separate module
    enabledForDeployment: true
    enabledForTemplateDeployment: true
    enableSoftDelete: true
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {}
}

resource batch 'Microsoft.Batch/batchAccounts@2023-05-01' = {
  name: batchAccountName
  location: location
  properties: {
    autoStorage: {
      storageAccountId: storage.id
    }
  }
}

resource adf 'Microsoft.DataFactory/factories@2018-06-01' = {
  name: factoryName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

resource lsStorage 'Microsoft.DataFactory/factories/linkedservices@2018-06-01' = {
  parent: adf
  name: 'AzureStorageLinkedService'
  properties: {
    type: 'AzureBlobStorage'
    typeProperties: {
      connectionString: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=core.windows.net'
    }
  }
}

resource lsBatch 'Microsoft.DataFactory/factories/linkedservices@2018-06-01' = {
  parent: adf
  name: 'AzureBatchLinkedService'
  properties: {
    type: 'AzureBatch'
    typeProperties: {
      batchUri: 'https://${batch.name}.${location}.batch.azure.com'
      poolName: 'github-faiss-pool'
      linkedServiceName: {
        referenceName: 'AzureStorageLinkedService'
        type: 'LinkedServiceReference'
      }
    }
  }
}

resource lsKV 'Microsoft.DataFactory/factories/linkedservices@2018-06-01' = {
  parent: adf
  name: 'AzureKeyVaultLinkedService'
  properties: {
    type: 'AzureKeyVault'
    typeProperties: {
      baseUrl: 'https://${kv.name}.vault.azure.net/'
    }
  }
}

resource pipeline 'Microsoft.DataFactory/factories/pipelines@2018-06-01' = {
  parent: adf
  name: 'GitHubRepoMetadataPipeline'
  properties: {
    activities: [
      {
        name: 'RunPythonInBatch'
        type: 'Custom'
        linkedServiceName: {
          referenceName: 'AzureBatchLinkedService'
          type: 'LinkedServiceReference'
        }
        typeProperties: {
          command: 'python parse_repos.py'
          resourceLinkedService: {
            referenceName: 'AzureStorageLinkedService'
            type: 'LinkedServiceReference'
          }
          referenceObjects: {
            filePaths: [
              {
                filePath: 'scripts/parse_repos.py'
                type: 'FilePath'
              },
              {
                filePath: 'scripts/requirements.txt'
                type: 'FilePath'
              }
            ]
          }
          environmentVariables: {
            GITHUB_TOKEN: {
              type: 'AzureKeyVaultSecret'
              store: {
                referenceName: 'AzureKeyVaultLinkedService'
                type: 'LinkedServiceReference'
              }
              secretName: 'github-token'
            }
            AZURE_TABLE_CONN: {
              type: 'AzureKeyVaultSecret'
              store: {
                referenceName: 'AzureKeyVaultLinkedService'
                type: 'LinkedServiceReference'
              }
              secretName: 'azure-table-conn'
            }
          }
        }
      }
    ]
  }
}
