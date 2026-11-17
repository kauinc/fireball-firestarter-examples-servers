# Smoke-invoke local DoofTroop CF (Functions Framework on :8080).
# This hits the CF directly — it does NOT go through Fireball router/SignalR.
param(
  [string]$BaseUrl = 'http://localhost:8080',
  [string]$MessageName = 'authenticate',
  [string]$GameId = '013313dd-af6e-48b7-9322-83e5321c6c7b',
  [string]$OperatorId = '964d5c31-043c-413d-b28d-8d0c2c0b1504'
)

$actionId = [guid]::NewGuid().ToString()
$connectionId = [guid]::NewGuid().ToString()
$playerToken = [guid]::NewGuid().ToString()
$operatorPlayerId = [guid]::NewGuid().ToString()

$message = @{
  Name           = $MessageName
  ActionId       = $actionId
  Environment    = 'development'
  GameId         = $GameId
  OperatorId     = $OperatorId
  GameMode       = 'fun'
  Currency       = 'USD'
  ConnectionId   = $connectionId
  Token          = $playerToken
  OperatorPlayerId = $operatorPlayerId
  Language       = 'en'
  Country        = 'US'
} | ConvertTo-Json -Compress

$cloudEvent = @{
  specversion     = '1.0'
  type            = 'google.cloud.pubsub.topic.v1.messagePublished'
  source          = '//pubsub.googleapis.com/projects/local/topics/dooftroop'
  id              = [guid]::NewGuid().ToString()
  time            = (Get-Date).ToUniversalTime().ToString('o')
  datacontenttype = 'application/json'
  data            = @{
    message = @{
      data       = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($message))
      messageId  = [guid]::NewGuid().ToString()
      publishTime = (Get-Date).ToUniversalTime().ToString('o')
    }
  }
} | ConvertTo-Json -Depth 6 -Compress

Write-Host "POST $BaseUrl"
Write-Host "Message: $message"

try {
  $response = Invoke-WebRequest -Uri $BaseUrl -Method POST -ContentType 'application/cloudevents+json' -Body $cloudEvent -UseBasicParsing
  Write-Host "Status: $($response.StatusCode)"
  if ($response.Content) { Write-Host $response.Content }
} catch {
  Write-Host "Request failed: $($_.Exception.Message)"
  if ($_.Exception.Response) {
    $reader = [IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    Write-Host $reader.ReadToEnd()
  }
  exit 1
}
