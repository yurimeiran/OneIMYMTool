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

## Full example

```powershell
Import-Module "C:\Users\Administrator\OneIMModule\OneIMModule.psd1"

Connect-OneIM `
    -ConnectionString "Data Source=SQLSERVER\INSTANCE;Initial Catalog=OneIM;Integrated Security=True" `
    -AuthenticationString "Module=DialogUser;User=viadmin;Password=P@ssw0rd" `
    -Verbose

Get-OneIMSession

Invoke-OneIMCompile -All -Verbose

Disconnect-OneIM
```
