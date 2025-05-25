import concurrent.futures
import requests
import time
import os
from azure.data.tables import TableServiceClient
from azure.core.exceptions import ResourceExistsError
from typing import List
from datetime import datetime
from your_module import get_changed_files, write_to_table  # Replace with your actual imports

GITHUB_TOKEN = os.getenv("GITHUB_TOKEN")
AZURE_STORAGE_CONN_STRING = os.getenv("AZURE_STORAGE_CONN_STRING")
TABLE_NAME = "RepoFileMetadata"

def get_repositories(token: str) -> List[str]:
    headers = {"Authorization": f"token {token}"}
    repos = []
    page = 1
    while True:
        url = f"https://api.github.com/user/repos?per_page=100&page={page}"
        response = requests.get(url, headers=headers)
        if response.status_code != 200:
            raise Exception(f"Failed to retrieve repos: {response.text}")
        data = response.json()
        if not data:
            break
        for repo in data:
            repos.append(repo["full_name"])  # e.g., "owner/repo"
        page += 1
    return repos

def scan_repo_and_update_metadata(owner: str, repo: str):
    print(f"Processing {owner}/{repo}")
    changed_files = get_changed_files(owner, repo, GITHUB_TOKEN)

    service = TableServiceClient.from_connection_string(AZURE_STORAGE_CONN_STRING)
    try:
        table_client = service.create_table_if_not_exists(table_name=TABLE_NAME)
    except ResourceExistsError:
        table_client = service.get_table_client(table_name=TABLE_NAME)

    for file_info in changed_files:
        entity = {
            "PartitionKey": owner,
            "RowKey": f"{repo}-{file_info['path']}",
            "Repo": repo,
            "FilePath": file_info["path"],
            "Status": file_info["status"],
            "Timestamp": datetime.utcnow().isoformat()
        }
        table_client.upsert_entity(entity)

def process_repo(repo_full_name: str):
    try:
        owner, repo = repo_full_name.split("/")
        scan_repo_and_update_metadata(owner, repo)
    except Exception as e:
        print(f"Error processing {repo_full_name}: {e}")

def main():
    repos = get_repositories(GITHUB_TOKEN)
    print(f"Found {len(repos)} repos")

    max_threads = min(32, len(repos))
    with concurrent.futures.ThreadPoolExecutor(max_workers=max_threads) as executor:
        futures = [executor.submit(process_repo, repo) for repo in repos]
        for future in concurrent.futures.as_completed(futures):
            pass

if __name__ == "__main__":
    main()
