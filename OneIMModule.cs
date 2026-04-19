using System;
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
}
