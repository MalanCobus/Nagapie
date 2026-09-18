#!/usr/bin/env bash
# Run once in Azure Cloud Shell (Bash) as an administrator able to create/assign roles.
set -euo pipefail

read -r -p 'Application (client) ID of Nagapie-GitHub-Deploy: ' client_id
if [[ ! "$client_id" =~ ^[[:xdigit:]]{8}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{12}$ ]]; then
  echo 'Enter the Application (client) ID from the app registration Overview.' >&2
  exit 1
fi

server_id=$(az sql server show --resource-group nagapie-cobus --name nagapie-cobus-sql --query id -o tsv)
principal_id=$(az ad sp show --id "$client_id" --query id -o tsv)
test -n "$server_id" && test -n "$principal_id"
group_scope="${server_id%/providers/Microsoft.Sql/servers/*}"
role_name='Nagapie Deployment SQL Firewall'
role_file=$(mktemp)
trap 'rm -f -- "$role_file"' EXIT

jq -n --arg scope "$group_scope" --arg name "$role_name" '{
  Name: $name, IsCustom: true,
  Description: "Manage SQL firewall rules for Nagapie deployments; no database or server deletion permission.",
  Actions: ["Microsoft.Sql/servers/read", "Microsoft.Sql/servers/firewallRules/read",
    "Microsoft.Sql/servers/firewallRules/write", "Microsoft.Sql/servers/firewallRules/delete"],
  NotActions: [], DataActions: [], NotDataActions: [], AssignableScopes: [$scope]
}' > "$role_file"

role_id=$(az role definition list --name "$role_name" --query '[0].name' -o tsv)
if [ -z "$role_id" ]; then
  role_id=$(az role definition create --role-definition "$role_file" --query name -o tsv)
fi
az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
  --role "$role_id" --scope "$server_id" --output none
echo 'Firewall permission assigned on nagapie-cobus-sql. No firewall rules or database contents were changed.'
