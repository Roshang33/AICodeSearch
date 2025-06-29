# repo_fetcher.py
import logging
import os
import requests
from azure.data.tables import TableServiceClient

def fetch_repos(github_token, org_or_user):
    headers = {
        "Authorization": f"Bearer {github_token}",
        "Accept": "application/vnd.github.v3+json"
    }
    repos = []
    page = 1
    while True:
        url = f"https://api.github.com/orgs/{org_or_user}/repos?per_page=100&page={page}"
        r = requests.get(url, headers=headers)
        if r.status_code == 404:
            url = f"https://api.github.com/users/{org_or_user}/repos?per_page=100&page={page}"
            r = requests.get(url, headers=headers)
        if r.status_code != 200:
            raise Exception(f"GitHub API error: {r.status_code} - {r.text}")
        data = r.json()
        if not data:
            break
        repos.extend(data)
        page += 1
    return repos

def store_minimal_metadata(repos, conn_string, table_name="repometadata"):
    service = TableServiceClient.from_connection_string(conn_string)
    table_client = service.create_table_if_not_exists(table_name=table_name)

    for repo in repos:
        entity = {
            "PartitionKey": "GitHub",
            "RowKey": str(repo["id"]),
            "name": repo["name"],
            "full_name": repo["full_name"],
            "html_url": repo["html_url"],
            "processing": "No"
        }
        try:
            table_client.upsert_entity(entity)
        except Exception as e:
            logging.error(f"Failed to insert repo {repo['name']}: {str(e)}")

if __name__ == "__main__":
    github_token = os.environ["GITHUB_TOKEN"]
    org_or_user = os.environ["GITHUB_USER_OR_ORG"]
    azure_table_conn = os.environ["AZURE_TABLE_CONN"]

    repos = fetch_repos(github_token, org_or_user)
    store_minimal_metadata(repos, azure_table_conn)
    print(f"✅ Stored {len(repos)} GitHub repositories to Azure Table.")
