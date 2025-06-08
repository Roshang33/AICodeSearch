@description('Name of the Azure Batch account')
param batchAccountName string = 'mybatchaccount'

@description('Name of the Azure Batch pool')
param batchPoolName string = 'mybatchpool'

@description('Location for the Batch account and pool')
param location string = resourceGroup().location

@description('VM size to use in the pool')
param vmSize string = 'STANDARD_D2_v3'

@description('Maximum number of compute nodes in the pool')
param maxNodes int = 10

@description('Node agent SKU')
param nodeAgentSkuId string = 'batch.node.ubuntu 20.04'

@description('VM image reference')
param imagePublisher string = 'Canonical'
param imageOffer string = 'UbuntuServer'
param imageSku string = '20_04-lts'
param imageVersion string = 'latest'

@description('Storage account for Batch auto storage')
param storageAccountId string

resource batchAccount 'Microsoft.Batch/batchAccounts@2023-05-01' = {
  name: batchAccountName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    autoStorage: {
      storageAccountId: storageAccountId
    }
  }
}

resource batchPool 'Microsoft.Batch/batchAccounts/pools@2023-05-01' = {
  name: '${batchAccount.name}/${batchPoolName}'
  properties: {
    vmSize: vmSize
    deploymentConfiguration: {
      virtualMachineConfiguration: {
        imageReference: {
          publisher: imagePublisher
          offer: imageOffer
          sku: imageSku
          version: imageVersion
        }
        nodeAgentSkuId: nodeAgentSkuId
      }
    }
    scaleSettings: {
      autoScale: {
        formula: '''
startingNumberOfVMs = 0;
maxNumberOfVMs = ${maxNodes};
pendingTasks = $PendingTasks.GetSample(1);
runningTasks = $RunningTasks.GetSample(1);
activeTasks = pendingTasks + runningTasks;

targetVMs = max(min(maxNumberOfVMs, activeTasks), startingNumberOfVMs);

$TargetDedicatedNodes = targetVMs;
$NodeDeallocationOption = taskcompletion;
'''
        evaluationInterval: 'PT5M'
      }
    }
    startTask: {
      commandLine: '/bin/bash -c "sudo apt update && sudo apt install -y python3-pip"'
      waitForSuccess: true
      userIdentity: {
        autoUser: {
          scope: 'pool'
          elevationLevel: 'admin'
        }
      }
    }
  }
  dependsOn: [
    batchAccount
  ]
}
