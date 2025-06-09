param location string = resourceGroup().location
param storageAccountName string = 'batchstor${uniqueString(resourceGroup().id)}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
  }
}

var fullName = 'btch${uniqueString(resourceGroup().id)}'

// Optional: truncate if needed (e.g., use substring)
var safeName = toLower(substring(fullName, 0, 17))

resource batchAccount 'Microsoft.Batch/batchAccounts@2023-05-01' = {
  name: safeName
  location: location
  properties: {
    autoStorage: {
      storageAccountId: storageAccount.id
    }
  }
}

resource batchPool 'Microsoft.Batch/batchAccounts/pools@2023-05-01' = {
  name: '${batchAccount.name}/pythoncompute'
  properties: {
    vmSize: 'STANDARD_D2_v3'
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
      commandLine: '/bin/bash -c "apt-get update && apt-get install -y python3-pip && pip3 install -r /mnt/batch/tasks/startup/wd/requirements.txt"'
      waitForSuccess: true
      userIdentity: {
        autoUser: {
          elevationLevel: 'Admin'
        }
      }
      resourceFiles: []
    }
  }
}
