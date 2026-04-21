# OneIMModule — PowerShell Module for One Identity Manager

## Requirements

- Windows PowerShell 5.1 or PowerShell 7+
- One Identity Manager installed at `C:\Program Files\One Identity\One Identity Manager`
- .NET Framework 4.8

## Installation

```powershell
Import-Module "C:\Users\Administrator\OneIMModule\OneIMModule.psd1"
```

On first import the module compiles `OneIMModule.dll` from the C# source. Subsequent imports load the cached DLL.

---

## Cmdlets

### `Connect-OneIM`

Authenticates to the OIM database and opens a session.

**Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `ConnectionString` | string | Yes | SQL Server connection string to the OIM database |
| `AuthenticationString` | string | Yes | OIM auth string (see examples below) |
| `PassThru` | switch | No | Return the `ISession` object to the pipeline |

**Authentication string formats**

```
Module=DialogUser;User=viadmin;Password=P@ssw0rd
Module=DomainAndUser
Module=ADSAccount;User=DOMAIN\username;Password=P@ssw0rd
```

**Examples**

```powershell
# Connect with a Dialog (system) user
Connect-OneIM `
    -ConnectionString "Data Source=SQLSERVER;Initial Catalog=OneIM;Integrated Security=True" `
    -AuthenticationString "Module=DialogUser;User=viadmin;Password=P@ssw0rd" `
    -Verbose

# Connect and capture the session object
$session = Connect-OneIM `
    -ConnectionString "Data Source=SQLSERVER;Initial Catalog=OneIM;Integrated Security=True" `
    -AuthenticationString "Module=DialogUser;User=viadmin;Password=P@ssw0rd" `
    -PassThru
```

---

### `Disconnect-OneIM`

Closes the current session and disposes the session factory.

```powershell
Disconnect-OneIM
```

---

### `Get-OneIMSession`

Returns the current active `ISession` object.

```powershell
$session = Get-OneIMSession
Write-Host "Session: $($session.Id)  User: $($session.Display)"
```

---

### `Invoke-OneIMCompile`

Compiles the One Identity Manager database.

**Parameters**

| Parameter | Type | Description |
|---|---|---|
| `All` | switch | Force full recompile (default: changed objects only) |
| `WaitForCompiler` | switch | Wait for any currently-running compiler to finish first |
| `IgnoreErrors` | switch | Continue even if compilation errors occur |

**Examples**

```powershell
# Incremental compile (only changed objects)
Invoke-OneIMCompile -Verbose

# Full recompile
Invoke-OneIMCompile -All -Verbose

# Full recompile, wait for any running compiler, ignore errors
Invoke-OneIMCompile -All -WaitForCompiler -IgnoreErrors
```

---

### `Get-OneIMEntity`

Queries entities from an OIM table and returns them as `PSObject` instances.

**Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `Table` | string | Yes | OIM table name, e.g. `Person` |
| `Filter` | string | No | SQL WHERE clause, e.g. `LastName = 'Smith'` |
| `Take` | int | No | Maximum rows to return (default 100) |
| `Skip` | int | No | Rows to skip for pagination (default 0) |

**Examples**

```powershell
# Get first 100 rows from Person
Get-OneIMEntity -Table "Person"

# Filter by last name, return up to 10 rows
Get-OneIMEntity -Table "Person" -Filter "LastName = 'Smith'" -Take 10

# Paginate — get rows 101-200
Get-OneIMEntity -Table "Person" -Take 100 -Skip 100

# Pipeline into Select-Object
Get-OneIMEntity -Table "Person" -Take 5 | Select-Object FirstName, LastName, DefaultEMailAddress
```

---

### `Invoke-OneIMMethod`

Invokes a method on an OIM entity identified by table and primary key.

**Parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `Table` | string | Yes | OIM table name, e.g. `Person` |
| `Key` | string | Yes | Primary key value of the entity |
| `Method` | string | Yes | Method name to invoke |
| `Parameters` | Hashtable | No | Method parameters. Use `[ordered]@{}` when order matters. |

**Examples**

```powershell
# Invoke a method with no parameters
Invoke-OneIMMethod -Table "Person" -Key "abc-123" -Method "Reactivate"

# Invoke with parameters (use [ordered] to preserve order)
Invoke-OneIMMethod -Table "Person" -Key "abc-123" -Method "SendEmail" `
    -Parameters ([ordered]@{ Subject = "Hello"; Body = "World" })

# Capture return value
$result = Invoke-OneIMMethod -Table "Person" -Key "abc-123" -Method "GetStatus"
```

---

## Full example

```powershell
Import-Module "C:\Users\Administrator\OneIMModule\OneIMModule.psd1"

Connect-OneIM `
    -ConnectionString "Data Source=SQLSERVER\INSTANCE;Initial Catalog=OneIM;Integrated Security=True" `
    -AuthenticationString "Module=DialogUser;User=viadmin;Password=P@ssw0rd" `
    -Verbose

Get-OneIMSession

# Query entities
Get-OneIMEntity -Table "Person" -Filter "IsInActive = 0" -Take 20 | Select-Object FirstName, LastName

# Invoke a method on an entity
Invoke-OneIMMethod -Table "Person" -Key "<uid>" -Method "Reactivate"

Invoke-OneIMCompile -All -Verbose

Disconnect-OneIM
```
