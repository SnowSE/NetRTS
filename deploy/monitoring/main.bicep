// Uptime monitoring for netrts.snowse.io, in the production resource group (rg-netrts).
// deploy/monitoring/setup.sh deploys it. See docs/deploying.md, "Health and uptime alerts".
//
//   Application Insights standard test (every 5 min, from 3 US regions) ──GET──► /health
//        │  fails when /health isn't 200 (503 = a check is unhealthy), or the certificate is near expiry
//        ▼
//   Metric alert: 2 regions failing ──► action group ──► email (and an "is resolved" email after)
//
// Cost: standard tests are $0.0005 per run per region: 3 regions every 5 minutes is about $13 a month.
// Fewer regions or a longer interval costs less (failedLocations must stay at most the region count).

@description('Where the test lives: the Application Insights resource\'s region.')
param location string = resourceGroup().location

@description('The Application Insights resource the server sends telemetry to.')
param appInsightsName string = 'netrts-insights'

@description('What the test fetches.')
param url string = 'https://netrts.snowse.io/health'

@description('Who the alert emails. Kept out of the repo: setup.sh passes it in.')
param alertEmail string

@description('Seconds between runs, in each region: 300, 600 or 900.')
@allowed([300, 600, 900])
param frequency int = 300

@description('The regions the test runs from (Application Insights location ids).')
param regions array = [
  'us-ca-sjc-azr' // West US
  'us-tx-sn1-azr' // South Central US
  'us-va-ash-azr' // East US
]

@description('How many regions must fail at once before the alert fires (one failing is usually the network).')
param failedLocations int = 2

resource insights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource email 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'netrts-alerts'
  location: 'global'
  properties: {
    groupShortName: 'netrts'
    enabled: true
    emailReceivers: [
      {
        name: 'admin'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

resource healthTest 'Microsoft.Insights/webtests@2022-06-15' = {
  name: 'netrts-health'
  location: location
  // Links the test to the Application Insights resource (its Availability page lists it).
  tags: {
    'hidden-link:${insights.id}': 'Resource'
  }
  kind: 'standard'
  properties: {
    SyntheticMonitorId: 'netrts-health'
    Name: 'netrts.snowse.io /health'
    Description: 'The site answers /health with 200: healthy or degraded. 503 means a check is unhealthy (database, a frozen match).'
    Enabled: true
    Kind: 'standard'
    Frequency: frequency
    Timeout: 30
    RetryEnabled: true
    Locations: [for id in regions: { Id: id }]
    Request: {
      RequestUrl: url
      HttpVerb: 'GET'
      ParseDependentRequests: false
    }
    ValidationRules: {
      ExpectedHttpStatusCode: 200
      SSLCheck: true
      // The App Service managed certificate renews itself; this says so if it stops.
      SSLCertRemainingLifetimeCheck: 7
    }
  }
}

resource down 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'netrts-down'
  location: 'global'
  properties: {
    description: 'netrts.snowse.io/health is failing from ${failedLocations} or more regions: the site is down, or a check is unhealthy. Open https://netrts.snowse.io/health to see which.'
    severity: 1
    enabled: true
    scopes: [
      healthTest.id
      insights.id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: healthTest.id
      componentId: insights.id
      failedLocationCount: failedLocations
    }
    autoMitigate: true
    actions: [
      {
        actionGroupId: email.id
      }
    ]
  }
}

output testId string = healthTest.id
output alertId string = down.id
