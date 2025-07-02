param($Request, $TriggerMetadata)

$githubToken = $env:GITHUB_TOKEN
$orgOrUser = $env:GITHUB_ORG
$azureTableConn = $env:AZURE_TABLE_CONN

function Write-Response($statusCode, $message) {
    return @{
        statusCode = $statusCode
        body = $message
    }
}

# Import needed module if available
# Import-Module AzTable -Force

Function Get-GitHubRepos {
        param (
            [string]$Token,
            [string]$OrgOrUser
        )

        $headers = @{
            Authorization = "Bearer $Token"
            Accept        = "application/vnd.github.v3+json"
        }

        $repos = @()
        $page = 1

        do {
            $url = "https://api.github.com/orgs/$OrgOrUser/repos?per_page=100&page=$page"
            $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ErrorAction SilentlyContinue

            # If org not found, try as user
            if ($response -eq $null) {
                $url = "https://api.github.com/users/$OrgOrUser/repos?per_page=100&page=$page"
                $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
            }

            if ($response.Count -eq 0) { break }

            $repos += $response
            $page++
        } while ($true)

        return $repos
    }

function Store-ReposInTable {
    param (
        [array]$Repos,
        [string]$ConnString
    )

    $tableClient = [Microsoft.Azure.Cosmos.Table.CloudStorageAccount]::Parse($ConnString).CreateCloudTableClient()
    $table = $tableClient.GetTableReference($tableName)
    $table.CreateIfNotExists() | Out-Null

    foreach ($repo in $Repos) {
        $entity = New-Object -TypeName PSObject -Property @{
            PartitionKey = "GitHub"
            RowKey       = $repo.id.ToString()
            name         = $repo.name
            full_name    = $repo.full_name
            html_url     = $repo.html_url
        }

        $tableEntity = New-AzTableRow -table $table -partitionKey $entity.PartitionKey -rowKey $entity.RowKey `
            -property @{"name"=$entity.name; "full_name"=$entity.full_name; "html_url"=$entity.html_url}
        $null = $table.Execute([Microsoft.Azure.Cosmos.Table.TableOperation]::InsertOrReplace($tableEntity))
    }
}

try {
   
    $repos = Get-GitHubRepos -Token $githubToken -OrgOrUser $orgOrUser
    Store-ReposInTable -Repos $repos -ConnString $azureTableConn

    Write-Response -statusCode 200 -message "Success"
} catch {
    Write-Response -statusCode 500 -message $_.Exception.Message
}
