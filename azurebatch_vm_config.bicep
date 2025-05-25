param location string = resourceGroup().location
param batchAccountName string
param poolName string = 'github-faiss-pool'

resource batchAccount 'Microsoft.Batch/batchAccounts@2023-05-01' existing = {
  name: batchAccountName
}

resource batchPool 'Microsoft.Batch/batchAccounts/pools@2023-05-01' = {
  parent: batchAccount
  name: poolName
  properties: {
    vmSize: 'STANDARD_D2_V2'
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
      fixedScale: {
        targetDedicatedNodes: 2
        targetLowPriorityNodes: 0
        resizeTimeout: 'PT10M'
      }
    }
    startTask: {
      commandLine: "/bin/bash -c 'sudo apt-get update && sudo apt-get install -y python3 python3-pip && pip3 install --upgrade pip'"
      waitForSuccess: true
      userIdentity: {
        autoUser: {
          scope: 'pool'
          elevationLevel: 'admin'
        }
      }
    }
  }
}
