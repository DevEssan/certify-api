using 'main.bicep'

param environmentName = 'dev'
param location = 'swedencentral'
param minReplicas = 0
param attachRegistry = true
param containerImage = readEnvironmentVariable('CERTIFY_IMAGE', 'mcr.microsoft.com/dotnet/samples:aspnetapp')
param apiKey = readEnvironmentVariable('CERTIFY_API_KEY')