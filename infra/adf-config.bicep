@description('Name of the Data Factory instance')
param dataFactoryName string

@description('Location for the Data Factory')
param location string = resourceGroup().location

@description('Managed Identity type: SystemAssigned, UserAssigned, or None')
@allowed([
  'SystemAssigned'
  'None'
])
param identityType string = 'SystemAssigned'

resource dataFactory 'Microsoft.DataFactory/factories@2018-06-01' = {
  name: dataFactoryName
  location: location
  identity: {
    type: identityType
  }
  tags: {
    environment: 'dev'
  }
}
