// The nightly recap show's Azure resources, for the production resource group (rg-netrts).
// deploy/recap/setup.sh deploys it (twice: once without the job, to build its image, then with it)
// and wires the web app to it. See docs/deploying.md, "Nightly recap show".
//
//   Container Apps job (nightly) ──► game server: the night's brief, publish the episode
//        │  managed identity
//        ├──► Microsoft Foundry: an Azure OpenAI model writes the script; Azure AI Speech voices the hosts
//        ├──► Storage: the "recaps" blob container (private; the server hands out short-lived links)
//        └──► Container registry: the job's image (built with `az acr build`, no Docker needed)
//
// Azure OpenAI rather than Claude: Claude on Foundry is an Azure Marketplace purchase, which education
// and sponsored subscriptions can't make. Azure OpenAI models are billed to the subscription itself.

@description('Where everything runs.')
param location string = resourceGroup().location

@description('The game server the job talks to.')
param serverUrl string = 'https://netrts.snowse.io'

@description('The web app\'s system-assigned identity (principal id): it reads the videos to make links to them.')
param serverPrincipalId string

@description('The shared key the job sends the server (the server\'s Recap__ApiKey).')
@secure()
param recapKey string

@description('False on the first pass, before the job\'s image exists in the registry.')
param deployJob bool = true

@description('The job\'s image tag in the registry (setup.sh builds latest; CI builds sha-<commit>).')
param imageTag string = 'latest'

@description('When the job runs, in UTC. 09:00 UTC is 2 a.m. MST / 3 a.m. MDT.')
param schedule string = '0 9 * * *'

@description('The model that writes the script, from the Foundry catalog (az cognitiveservices model list).')
param writerModel string = 'gpt-5.5'

@description('Its version in the catalog.')
param writerModelVersion string = '2026-04-24'

@description('Its capacity in thousands of tokens per minute. A script is one request of ~20K tokens a night.')
param writerCapacity int = 50

@description('GitHub Actions\' identity (principal id), so CI can build images and move the job to them. Empty to skip.')
param githubPrincipalId string = ''

@description('An existing Log Analytics workspace in this resource group for the job\'s logs.')
param logAnalyticsWorkspaceName string = 'netrts-logs'

var suffix = uniqueString(resourceGroup().id)
var roles = {
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  cognitiveServicesUser: 'a97b65f3-24c7-4388-baec-2e87135dc908'
  cognitiveServicesOpenAiUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  acrPush: '8311e382-0749-4cb8-b61a-304f252e45ec'
  contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
}

// ------------------------------------------------------------------ storage

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'netrtsrecaps${take(suffix, 10)}'
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // Entra ID only: the job and the server sign in with their identities
    supportsHttpsTrafficOnly: true
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource recaps 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'recaps'
  properties: { publicAccess: 'None' }
}

// ------------------------------------------------------------------ registry

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'netrtsrecap${take(suffix, 8)}'
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

// ------------------------------------------------------------------ Foundry (the writer + Speech)

resource ai 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: 'netrts-ai'
  location: location
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: 'netrts-ai'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true // no keys: managed identities only
    allowProjectManagement: true
  }
}

resource writer 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: ai
  name: 'recap-writer'
  sku: { name: 'GlobalStandard', capacity: writerCapacity }
  properties: {
    model: { format: 'OpenAI', name: writerModel, version: writerModelVersion }
  }
}

// ------------------------------------------------------------------ the job

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-netrts-recap'
  location: location
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'netrts-recap-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource job 'Microsoft.App/jobs@2024-03-01' = if (deployJob) {
  name: 'netrts-recap'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: { cronExpression: schedule, parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 7200 // a 10-minute show renders in well under an hour on 2 vCPUs
      replicaRetryLimit: 1
      secrets: [ { name: 'recap-key', value: recapKey } ]
      registries: [ { server: registry.properties.loginServer, identity: identity.id } ]
    }
    template: {
      containers: [
        {
          name: 'recap'
          image: '${registry.properties.loginServer}/netrts-recap:${imageTag}'
          resources: { cpu: json('2'), memory: '4Gi' }
          env: [
            { name: 'NETRTS_URL', value: serverUrl }
            { name: 'RECAP_KEY', secretRef: 'recap-key' }
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
            { name: 'AZURE_OPENAI_ENDPOINT', value: 'https://${ai.properties.customSubDomainName}.openai.azure.com/' }
            { name: 'RECAP_MODEL', value: writer.name }
            { name: 'SPEECH_ENDPOINT', value: 'https://${ai.properties.customSubDomainName}.cognitiveservices.azure.com/' }
            { name: 'RECAP_BLOB_URL', value: storage.properties.primaryEndpoints.blob }
          ]
        }
      ]
    }
  }
  dependsOn: [ jobPullsImages ]
}

// ------------------------------------------------------------------ access

resource jobWritesVideos 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, roles.storageBlobDataContributor)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
  }
}

// The server lists the episodes in storage and signs read links with a user delegation key.
resource serverReadsVideos 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, serverPrincipalId, roles.storageBlobDataContributor)
  properties: {
    principalId: serverPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
  }
}

resource jobUsesSpeech 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: ai
  name: guid(ai.id, identity.id, roles.cognitiveServicesUser)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesUser)
  }
}

resource jobUsesTheWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: ai
  name: guid(ai.id, identity.id, roles.cognitiveServicesOpenAiUser)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesOpenAiUser)
  }
}

resource jobPullsImages 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, roles.acrPull)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
  }
}

// CI builds the image in the registry (az acr build) and points the job at it.
resource ciBuildsImages 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(githubPrincipalId)) {
  scope: registry
  name: guid(registry.id, githubPrincipalId, roles.contributor)
  properties: {
    principalId: githubPrincipalId
    principalType: 'ServicePrincipal'
    // az acr build queues a run on the registry, which AcrPush alone doesn't allow.
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.contributor)
  }
}

resource ciUpdatesJob 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(githubPrincipalId) && deployJob) {
  scope: job
  name: guid(resourceGroup().id, 'netrts-recap', githubPrincipalId, roles.contributor)
  properties: {
    principalId: githubPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.contributor)
  }
}

@description('Set this on the web app as ConnectionStrings__recaps.')
output blobUrl string = storage.properties.primaryEndpoints.blob
output registry string = registry.name
output foundryResource string = ai.name
