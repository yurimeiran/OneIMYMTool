using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading;
using VI.Base.Logging;
using VI.DB;
using VI.DB.Auth;
using VI.DB.Compile;
using VI.DB.Entities;

namespace OneIMModule
{
    /// <summary>
    /// Module-level session state shared across all cmdlets.
    /// </summary>
    public static class OneIMSessionStore
    {
        public static ISession Current { get; set; }
        public static ISessionFactory Factory { get; set; }

        public static void EnsureConnected()
        {
            if (Current == null || Current.IsDisposed)
                throw new InvalidOperationException(
                    "Not connected to One Identity Manager. Run Connect-OneIM first.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Connect-OneIM
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticates to One Identity Manager and opens a session.
    ///
    /// SYNOPSIS
    ///   Connect-OneIM -ConnectionString "Data Source=SERVER;Initial Catalog=DB;..." `
    ///                 -AuthenticationString "Module=DialogUser;User=admin;Password=secret"
    ///
    /// Authentication string examples:
    ///   DialogUser (system user)  : Module=DialogUser;User=viadmin;Password=P@ssw0rd
    ///   Windows / AD SSO          : Module=DomainAndUser
    ///   AD with explicit user     : Module=ADSAccount;User=DOMAIN\username;Password=P@ssw0rd
    /// </summary>
    [Cmdlet(VerbsCommunications.Connect, "OneIM")]
    [OutputType(typeof(ISession))]
    public class ConnectOneIMCmdlet : PSCmdlet
    {
        /// <summary>SQL Server connection string to the One Identity Manager database.</summary>
        [Parameter(Mandatory = true, Position = 0, HelpMessage =
            "SQL Server connection string, e.g. \"Data Source=SERVER;Initial Catalog=OneIM;Integrated Security=True\"")]
        public string ConnectionString { get; set; }

        /// <summary>
        /// One Identity Manager authentication string.
        /// Format: Module=&lt;ModuleIdent&gt;[;User=&lt;user&gt;][;Password=&lt;pass&gt;]
        /// </summary>
        [Parameter(Mandatory = true, Position = 1, HelpMessage =
            "Auth string, e.g. \"Module=DialogUser;User=viadmin;Password=secret\"")]
        public string AuthenticationString { get; set; }

        /// <summary>When set, returns the ISession object to the pipeline.</summary>
        [Parameter]
        public SwitchParameter PassThru { get; set; }

        protected override void ProcessRecord()
        {
            // Dispose any previous session cleanly.
            DisconnectOneIMCmdlet.DisconnectCurrent();

            WriteVerbose("Building session factory...");
            try
            {
                // SessionFactoryConfiguration has an internal constructor — use Activator to instantiate it.
                var config = (SessionFactoryConfiguration)Activator.CreateInstance(
                    typeof(SessionFactoryConfiguration), nonPublic: true);
                config.ConnectionString = ConnectionString;
                config.Using(new ViSqlFactory());

                var factory = config.BuildSessionFactory();

                WriteVerbose("Authenticating...");
                var session = SessionFactoryExtensions
                    .OpenAsync(factory, AuthenticationString, CancellationToken.None)
                    .GetAwaiter().GetResult();

                OneIMSessionStore.Factory = factory;
                OneIMSessionStore.Current = session;

                WriteVerbose(string.Format("Connected. Session: {0}  User: {1}", session.Id, session.Display));

                if (PassThru.IsPresent)
                    WriteObject(session);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "OneIM.ConnectFailed", ErrorCategory.AuthenticationError, ConnectionString));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Disconnect-OneIM
    // ─────────────────────────────────────────────────────────────────────────

    [Cmdlet(VerbsCommunications.Disconnect, "OneIM")]
    public class DisconnectOneIMCmdlet : PSCmdlet
    {
        protected override void ProcessRecord()
        {
            DisconnectCurrent();
            WriteVerbose("Disconnected from One Identity Manager.");
        }

        internal static void DisconnectCurrent()
        {
            try { if (OneIMSessionStore.Current != null) OneIMSessionStore.Current.Dispose(); } catch { }
            try
            {
                var f = OneIMSessionStore.Factory as IDisposable;
                if (f != null) f.Dispose();
            }
            catch { }
            OneIMSessionStore.Current = null;
            OneIMSessionStore.Factory = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Get-OneIMSession
    // ─────────────────────────────────────────────────────────────────────────

    [Cmdlet(VerbsCommon.Get, "OneIMSession")]
    [OutputType(typeof(ISession))]
    public class GetOneIMSessionCmdlet : PSCmdlet
    {
        protected override void ProcessRecord()
        {
            var s = OneIMSessionStore.Current;
            if (s == null || s.IsDisposed)
                WriteWarning("No active One Identity Manager session.");
            else
                WriteObject(s);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Invoke-OneIMCompile
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles the One Identity Manager database using the current session.
    ///
    /// SYNOPSIS
    ///   Invoke-OneIMCompile [-All] [-WaitForCompiler] [-IgnoreErrors]
    ///
    /// The -All switch forces a full recompile instead of changed-only.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Invoke, "OneIMCompile")]
    public class InvokeOneIMCompileCmdlet : PSCmdlet
    {
        /// <summary>Force full recompile of all objects (default: changed only).</summary>
        [Parameter]
        public SwitchParameter All { get; set; }

        /// <summary>Block until a currently-running compiler finishes before starting.</summary>
        [Parameter]
        public SwitchParameter WaitForCompiler { get; set; }

        /// <summary>Continue even if compilation errors occur.</summary>
        [Parameter]
        public SwitchParameter IgnoreErrors { get; set; }

        protected override void ProcessRecord()
        {
            try { OneIMSessionStore.EnsureConnected(); }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "OneIM.NotConnected", ErrorCategory.InvalidOperation, null));
                return;
            }

            WriteVerbose(All.IsPresent
                ? "Starting full database compilation..."
                : "Starting incremental database compilation (changed objects only)...");

            try
            {
                var compiler = new UnattendedCompiler
                {
                    WaitForCompiler = WaitForCompiler.IsPresent,
                    IgnoreErrors    = IgnoreErrors.IsPresent,
                    CompileOnlyChanged = !All.IsPresent
                };

                var logger = new Logger();
                var result = compiler
                    .CompileAsync(OneIMSessionStore.Current, logger, CancellationToken.None)
                    .GetAwaiter().GetResult();

                WriteVerbose("Compilation finished.");
                WriteObject(result);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "OneIM.CompileFailed", ErrorCategory.OperationStopped, null));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Invoke-OneIMMethod
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes a method on a One Identity Manager entity.
    ///
    /// SYNOPSIS
    ///   Invoke-OneIMMethod -Table "Person" -Key "abc-123" -Method "MethodName" `
    ///                      [-Parameters @{ param1 = "value"; param2 = 42 }]
    ///
    /// Use [ordered]@{} when parameter order matters.
    /// Returns the method result if any; void methods produce no output.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Invoke, "OneIMMethod")]
    [OutputType(typeof(object))]
    public class InvokeOneIMMethodCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, HelpMessage = "OIM table name, e.g. \"Person\"")]
        public string Table { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Primary key value of the entity")]
        public string Key { get; set; }

        [Parameter(Mandatory = true, Position = 2, HelpMessage = "Method name to invoke")]
        public string Method { get; set; }

        [Parameter(Position = 3, HelpMessage = "Method parameters as a hashtable. Use [ordered]@{} when order matters.")]
        public System.Collections.Hashtable Parameters { get; set; }

        protected override void ProcessRecord()
        {
            try { OneIMSessionStore.EnsureConnected(); }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "OneIM.NotConnected", ErrorCategory.InvalidOperation, null));
                return;
            }

            try
            {
                IEntitySource source = OneIMSessionStore.Current.Resolve<IEntitySource>();

                IEntity entity = EntitySourceExtensions
                    .GetAsync(source, Table, Key, EntityLoadType.Interactive, CancellationToken.None)
                    .GetAwaiter().GetResult();

                object[] paramValues;
                Type[]   paramTypes;

                if (Parameters != null && Parameters.Count > 0)
                {
                    paramValues = new object[Parameters.Count];
                    paramTypes  = new Type[Parameters.Count];
                    int i = 0;
                    foreach (System.Collections.DictionaryEntry entry in Parameters)
                    {
                        paramValues[i] = entry.Value;
                        paramTypes[i]  = entry.Value != null ? entry.Value.GetType() : typeof(object);
                        i++;
                    }
                }
                else
                {
                    paramValues = new object[0];
                    paramTypes  = new Type[0];
                }

                WriteVerbose(string.Format("Invoking method '{0}' on {1} [{2}]", Method, Table, Key));

                object result = entity
                    .CallFunctionAsync(Method, paramTypes, paramValues, CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (result != null)
                    WriteObject(result);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "OneIM.InvokeMethodFailed", ErrorCategory.OperationStopped, Method));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Get-OneIMEntity
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries entities from an OIM table and returns them as PSObjects.
    ///
    /// SYNOPSIS
    ///   Get-OneIMEntity -Table "Person" [-Filter "LastName = 'Smith'"] [-Take 50] [-Skip 0]
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "OneIMEntity")]
    [OutputType(typeof(PSObject))]
    public class GetOneIMEntityCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, HelpMessage = "OIM table name, e.g. \"Person\"")]
        public string Table { get; set; }

        [Parameter(Position = 1, HelpMessage = "SQL WHERE clause, e.g. \"LastName = 'Smith'\"")]
        public string Filter { get; set; }

        private int _take = 100;
        [Parameter(HelpMessage = "Maximum rows to return (default 100)")]
        public int Take { get { return _take; } set { _take = value; } }

        private int _skip = 0;
        [Parameter(HelpMessage = "Rows to skip (default 0)")]
        public int Skip { get { return _skip; } set { _skip = value; } }

        protected override void ProcessRecord()
        {
            try { OneIMSessionStore.EnsureConnected(); }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "OneIM.NotConnected", ErrorCategory.InvalidOperation, null));
                return;
            }

            try
            {
                IEntitySource source = OneIMSessionStore.Current.Resolve<IEntitySource>();

                // Build query — ISelect is implemented by Query so casts are safe
                Query query = (Query)Query.From(Table);
                query = query.SelectAll();
                if (!string.IsNullOrEmpty(Filter))
                    query = query.Where(Filter);
                query = (Query)((ISelect)query).Take(_take);
                if (_skip > 0)
                    query = (Query)((ISelect)query).Skip(_skip);

                IEntityCollection collection = EntitySourceExtensions
                    .GetCollectionAsync(source, query, CancellationToken.None)
                    .GetAwaiter().GetResult();

                List<string> colNames = new List<string>(collection.ColumnIndices.Keys);

                foreach (IEntity entity in collection)
                {
                    PSObject obj = new PSObject();
                    foreach (string colName in colNames)
                    {
                        object val;
                        try   { val = entity.Columns[colName].GetValue(); }
                        catch { val = null; }
                        obj.Properties.Add(new PSNoteProperty(colName, val));
                    }
                    WriteObject(obj);
                }
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex, "OneIM.GetEntityFailed", ErrorCategory.OperationStopped, Table));
            }
        }
    }
}
