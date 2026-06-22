# Code

## Inline code

Use `dotnet build` then `dotnet test`. A path like `C:\Temp\file.md` and a flag
like `--recursive` should not be altered.

## Plain fenced block

```
no language hint
just monospaced text
  preserving   spacing
```

## C#

```csharp
public sealed class Greeter
{
    public string Hello(string name) => $"Hello, {name}!";
}
```

## SQL

```sql
SELECT u.id, u.name
FROM users u
JOIN roles r ON r.id = u.role_id
WHERE u.active = 1
ORDER BY u.name;
```

## JSON

```json
{
  "name": "awiki",
  "version": "1.0.0",
  "tags": ["wiki", "export"]
}
```

## PowerShell

```powershell
Get-ChildItem -Recurse -Filter *.md |
  Where-Object { $_.Length -gt 0 } |
  Select-Object FullName, Length
```

## Bash

```bash
#!/usr/bin/env bash
set -euo pipefail
for f in *.md; do
  echo "Processing $f"
done
```

## XML / YAML

```xml
<root>
  <item id="1">value</item>
</root>
```

```yaml
service:
  name: awiki
  ports:
    - 1433
    - 5432
```
