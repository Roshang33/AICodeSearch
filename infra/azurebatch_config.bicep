param location string = resourceGroup().location
param batchAccountName string = 'btchh37gi2edjio3k'
param storageAccountName string
param storageContainerName string
param requirementsFileName string = 'requirements.txt'

var requirementsUrl = 'https://${storageAccountName}.blob.core.windows.net/${storageContainerName}/${requirementsFileName}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource batchAccount 'Microsoft.Batch/batchAccounts@2023-05-01' = {
  name: batchAccountName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    poolAllocationMode: 'BatchService'
    autoStorage: {
      storageAccountId: storageAccount.id
    }
  }
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(batchAccount.name, 'Storage Blob Data Reader')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1') // Storage Blob Data Reader
    principalId: batchAccount.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource pool 'Microsoft.Batch/batchAccounts/pools@2023-05-01' = {
  name: '${batchAccount.name}/linuxpythonpool'
  properties: {
    vmSize: 'STANDARD_D2s_v3'
    deploymentConfiguration: {
      virtualMachineConfiguration: {
        imageReference: {
          publisher: 'microsoft-azure-batch'
          offer: 'ubuntu-server-container'
          sku: '20-04-lts'
          version: 'latest'
        }
        nodeAgentSkuId: 'batch.node.ubuntu 20.04'
      }
    }
    scaleSettings: {
      autoScale: {
        formula: 'startingNumberOfVMs=0; maxNumberOfVMs=5; $TargetDedicatedNodes=0; $TargetLowPriorityNodes=0;'
        evaluationInterval: 'PT5M'
      }
    }
    startTask: {
      commandLine: '/bin/bash -c "sudo apt update && sudo apt install -y python3-pip && azcopy login --identity && azcopy copy \\"${requirementsUrl}\\" . && pip3 install -r requirements.txt"'
      waitForSuccess: true
      userIdentity: {
        autoUser: {
          elevationLevel: 'Admin'
          scope: 'Task'
        }
      }
    }
  }
}
