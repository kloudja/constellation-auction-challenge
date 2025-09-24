param([string]$Filter = "")
dotnet build
if ([string]::IsNullOrWhiteSpace($Filter)) { dotnet test } else { dotnet test --filter $Filter }
