// NAnt - A .NET build tool
// Copyright (C) 2001-2003 Gerry Shaw
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
//
// Clayton Harbour (claytonharbour@sporadicism.com)

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Globalization;

using NAnt.Core;
using NAnt.Core.Attributes;
using NAnt.Core.Tasks;
using NAnt.Core.Types;
using NAnt.Core.Util;

namespace NAnt.SourceControl.Tasks {
    /// <summary>
    /// A base class for creating tasks for executing CVS client commands on a 
    /// CVS repository.
    /// </summary>
    public abstract class AbstractCvsTask : AbstractSourceControlTask {

        #region Protected Static Fields

        /// <summary>
        /// Default value for the recursive directive.  Default is <code>false</code>.
        /// </summary>
        protected const bool DefaultRecursive = false;
        /// <summary>
        /// Default value for the quiet command.
        /// </summary>
        protected const bool DefaultQuiet = false;
        /// <summary>
        /// Default value for the really quiet command.
        /// </summary>
        protected const bool DefaultReallyQuiet = false;

        /// <summary>
        /// An environment variable that holds path information about where
        ///     cvs is located.
        /// </summary>
        protected const string CvsHome = "CVS_HOME";
        /// <summary>
        /// Name of the password file that cvs stores pserver 
        ///     cvsroot/ password pairings.
        /// </summary>
        protected const String CvsPassfile = ".cvspass";
        /// <summary>
        /// The default compression level to use for cvs commands.
        /// </summary>
        protected const int DefaultCompressionLevel = 3;
        /// <summary>
        /// The default use of binaries, defaults to use sharpcvs
        ///     <code>true</code>.
        /// </summary>
        protected const bool DefaultUseSharpCvsLib = true;

        /// <summary>
        /// The name of the cvs executable.
        /// </summary>
        protected const string CvsExe = "cvs.exe";
        /// <summary>
        /// The temporary name of the sharpcvslib binary file, to avoid 
        ///     conflicts in the path variable.
        /// </summary>
        protected const string SharpCvsExe = "scvs.exe";
        /// <summary>
        /// Environment variable that holds the executable name that is used for
        ///     ssh communication.
        /// </summary>
        protected const string CvsRsh = "CVS_RSH";
        /// <summary>
        /// Property name used to specify on a project level whether sharpcvs is
        ///     used or not.
        /// </summary>
        protected const string UseSharpCvsLibProp = "sourcecontrol.usesharpcvslib";

        #endregion

        #region Private Instance Fields

        private string _module;
        private bool _useSharpCvsLib = DefaultUseSharpCvsLib;
        private bool _isUseSharpCvsLibSet = false;
        private FileInfo _cvsFullPath;

        private string _sharpcvslibExeName;

        #endregion Private Instance Fields

        #region Private Static Fields

        private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        #endregion Private Static Fields

        #region Protected Instance Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="AbstractCvsTask" /> 
        /// class.
        /// </summary>
        protected AbstractCvsTask () : base() {
            _sharpcvslibExeName = 
                Path.Combine (System.AppDomain.CurrentDomain.BaseDirectory, SharpCvsExe);
        }

        #endregion Protected Instance Constructors

        #region Protected Instance Properties

        /// <summary>
        /// The environment name for the ssh variable.
        /// </summary>
        protected override string SshEnv {
            get {return CvsRsh;}
        }

        /// <summary>
        /// The name of the cvs binary, or <code>cvs.exe</code> at the time this 
        ///     was written.
        /// </summary>
        protected override string VcsExeName {
            get {return CvsExe;}
        }

        /// <summary>
        /// The name of the pass file, or <code>.cvspass</code> at the time
        ///     of this writing.
        /// </summary>
        protected override string PassFileName {
            get {return CvsPassfile;}
        }

        /// <summary>
        /// The name of the version control system specific home environment 
        ///     variable.
        /// </summary>
        protected override string VcsHomeEnv {
            get {return CvsHome;}
        }

        /// <summary>
        /// Specify if the module is needed for this cvs command.  It is
        /// only needed if there is no module information on the local file
        /// system.
        /// </summary>
        protected virtual bool IsModuleNeeded {
            get {return true;}
        }

        /// <summary>
        /// Specify if the cvs root should be used for this cvs command.  It is
        /// only needed if there is no module information on the local file
        /// system, there fore is not needed for a cvs update.
        /// </summary>
        protected virtual bool IsCvsRootNeeded {
            get {return true;}
        }

        #endregion

        #region Public Instance Properties

        /// <summary>
        /// The name of the cvs executable.
        /// </summary>
        public override string ExeName {
            get {
                if (null != CvsFullPath) {
                    return CvsFullPath.FullName;
                }
                string _exeNameTemp;
                if (UseSharpCvsLib) {
                    _exeNameTemp = _sharpcvslibExeName;
                } else {
                    _exeNameTemp = DeriveVcsFromEnvironment().FullName;
                }
                Logger.Debug("_sharpcvslibExeName: " + _sharpcvslibExeName);
                Logger.Debug("_exeNameTemp: " + _exeNameTemp);
                Properties[PropExeName] = _exeNameTemp;
                return _exeNameTemp;
            }
        }

        /// <summary>
        /// The full path to the cvs binary used.  The cvs tasks will attempt to
        /// "guess" the location of your cvs binary based on your path.  If the
        /// task is unable to resolve the location, or resolves it incorrectly
        /// this can be used to manually specify the path.
        /// </summary>
        /// <value>
        /// A full path (i.e. including file name) of your cvs binary:
        ///     On Windows: c:\vcs\cvs\cvs.exe 
        ///     On *nix: /usr/bin/cvs
        /// </value>
        [TaskAttribute("cvsfullpath", Required=false)]
        public FileInfo CvsFullPath {
            get {return _cvsFullPath;}
            set {_cvsFullPath = value;}
        }

        /// <summary>
        /// <para>
        /// The cvs root variable has the following components uses the following format:
        ///     <para>
        ///         <code>[protocol]:[username]@[servername]:[server path]</code>
        ///         <br />
        ///         <ul>
        ///             <li>protocol:       ext, pserver, ssh (sharpcvslib); if you are not using sharpcvslib consult your cvs documentation.</li>
        ///             <li>username:       [username]</li>
        ///             <li>servername:     cvs.sourceforge.net</li>
        ///             <li>server path:    /cvsroot/nant</li>
        ///         </ul>
        ///     </para>
        /// </para>
        /// </summary>
        /// <example>
        ///   <para>NAnt anonymous cvsroot:</para>
        ///   <code>
        ///   :pserver:anonymous@cvs.sourceforge.net:/cvsroot/nant
        ///   </code>
        /// </example>
        [TaskAttribute("cvsroot", Required=true)]
        [StringValidator(AllowEmpty=false)]
        public override string Root {
            get { return base.Root; }
            set { base.Root = StringUtils.ConvertEmptyToNull(value); }
        }

        /// <summary>
        /// The module to perform an operation on.
        /// </summary>
        /// <value>
        /// The module to perform an operation on.  This is a normal file/ folder
        ///     name without path information.
        /// </value>
        /// <example>
        ///   <para>In Nant the module name would be:</para>
        ///   <code>nant</code>
        /// </example>
        [TaskAttribute("module", Required=true)]
        [StringValidator(AllowEmpty=false, Expression=@"^[A-Za-z0-9][A-Za-z0-9._\-]*$")]
        public string Module {
            get { return _module; }
            set { _module = StringUtils.ConvertEmptyToNull(value); }
        }

        /// <summary>
        /// <code>true</code> if the SharpCvsLib binaries that come bundled with 
        ///     NAnt should be used to perform the cvs commands, <code>false</code>
        ///     otherwise.
        ///     
        ///     You may also specify an override value for all cvs tasks instead
        ///     of specifying a value for each.  To do this set the property
        ///     <code>sourcecontrol.usesharpcvslib</code> to <code>false</code>.
        ///     
        ///     <warn>If you choose not to use SharpCvsLib to checkout from 
        ///         cvs you will need to include a cvs.exe binary in your
        ///         path.</warn>
        /// </summary>
        /// <example>
        ///     To use a cvs client in your path instead of sharpcvslib specify
        ///         the property:
        ///     &gt;property name="sourcecontrol.usesharpcvslib" value="false"&lt;
        ///     
        ///     The default settings is to use sharpcvslib and the setting closest
        ///     to the task execution is used to determine which value is used
        ///     to execute the process.
        ///     
        ///     For instance if the attribute usesharpcvslib was set to false 
        ///     and the global property was set to true, the usesharpcvslib is 
        ///     closes to the point of execution and would be used and is false. 
        ///     Therefore the sharpcvslib binary would NOT be used.
        /// </example>
        [TaskAttribute("usesharpcvslib", Required=false)]
        public bool UseSharpCvsLib {
            get {return _useSharpCvsLib;}
            set {
                _isUseSharpCvsLibSet = true;
                _useSharpCvsLib = value;
            }
        }

        /// <summary>
        /// The executable to use for ssh communication.
        /// </summary>
        [TaskAttribute("cvsrsh", Required=false)]
        public override FileInfo Ssh {
            get {return base.Ssh;}
            set {base.Ssh = value;}
        }

        /// <summary>
        /// Indicates if the output from the cvs command should be supressed.  Defaults to 
        ///     <code>false</code>.
        /// </summary>
        [TaskAttribute("quiet", Required=false)]
        [BooleanValidator()]
        public bool Quiet {
            get {return ((Option)GlobalOptions["quiet"]).IfDefined;}
            set {SetGlobalOption("quiet", "-q", value);}
        }

        /// <summary>
        /// Indicates if the output from the cvs command should be stopped.  Default to 
        ///     <code>false</code>.
        /// </summary>
        [TaskAttribute("reallyquiet", Required=false)]
        [BooleanValidator()]
        public bool ReallyQuiet {
            get {return ((Option)GlobalOptions["reallyquiet"]).IfDefined;}
            set {SetGlobalOption("reallyquiet", "-Q", value);}
        }

        /// <summary>
        /// <code>true</code> if the sandbox files should be checked out in
        ///     read only mode.
        /// </summary>
        [TaskAttribute("read", Required=false)]
        [BooleanValidator()]
        public bool Read {
            get {return ((Option)GlobalOptions["readonly-attribute"]).IfDefined;}
            set {
                SetGlobalOption("readonly-attribute", "-r", value);
                this.ReadWrite = !value;
            }
        }

        /// <summary>
        /// <code>true</code> if the sandbox files should be checked out in 
        ///     read/ write mode.
        ///     
        ///     Defaults to <code>true</code>.
        /// </summary>
        [TaskAttribute("readwrite", Required=false)]
        [BooleanValidator()]
        public bool ReadWrite {
            get {return ((Option)GlobalOptions["readwrite"]).IfDefined;}
            set {
                this.Read = !value;
                SetGlobalOption("readwrite", "-w", value);
            }
        }

        #endregion Public Instance Properties

        #region Override Task Implementation

        /// <summary>
        /// Build up the command line arguments, determine which executable is being
        ///     used and find the path to that executable and set the working 
        ///     directory.
        /// </summary>
        /// <param name="process">The process to prepare.</param>
        protected override void PrepareProcess (Process process) {
            // Although a global property can be set, take the property closest
            //  to the task execution, which is the attribute on the task itself.
            if (!_isUseSharpCvsLibSet &&
                (null == Properties || null == Properties[UseSharpCvsLibProp])) {
                // if not set and the global property is null then use the default
                _useSharpCvsLib = UseSharpCvsLib;
            } else if (!_isUseSharpCvsLibSet &&
                null != Properties[UseSharpCvsLibProp]){
                try {
                    _useSharpCvsLib =
                        System.Convert.ToBoolean(Properties[UseSharpCvsLibProp]);
                } catch (Exception) {
                    throw new BuildException (UseSharpCvsLib + " must be convertable to a boolean.");
                }
            }

            Logger.Debug("number of arguments: " + Arguments.Count);
            if (IsCvsRootNeeded) {
                Arguments.Add(new Argument(String.Format(CultureInfo.InvariantCulture,"-d{0}", Root)));
            }
            AppendGlobalOptions();
            Arguments.Add(new Argument(CommandName));

            AppendCommandOptions();

            Log(Level.Debug, String.Format(CultureInfo.InvariantCulture,
                "{0} commandline args are null: {1}", 
                LogPrefix, ((null == CommandLineArguments) ? "yes" : "no")));
            Log(Level.Debug, String.Format(CultureInfo.InvariantCulture,
                "{0} commandline: {1}", 
                LogPrefix, CommandLineArguments));
            if (null != CommandLineArguments) {
                Arguments.Add(new Argument(CommandLineArguments));
            }

            AppendFiles();
            if (IsModuleNeeded) {
                Arguments.Add(new Argument(Module));
            }

            if (!Directory.Exists(DestinationDirectory.FullName)) {
                Directory.CreateDirectory(DestinationDirectory.FullName);
            }
            base.PrepareProcess(process);
            process.StartInfo.FileName = ExeName;

            process.StartInfo.WorkingDirectory = 
                DestinationDirectory.FullName;

            Log(Level.Info, String.Format(CultureInfo.InvariantCulture,"{0} working directory: {1}", 
                LogPrefix, process.StartInfo.WorkingDirectory));
            Log(Level.Info, String.Format(CultureInfo.InvariantCulture,"{0} executable: {1}", 
                LogPrefix, process.StartInfo.FileName));
            Log(Level.Info, String.Format(CultureInfo.InvariantCulture,"{0} arguments: {1}", 
                LogPrefix, process.StartInfo.Arguments));

        }

        #endregion

        #region Private Instance Methods

        private void AppendGlobalOptions () {
            foreach (Option option in GlobalOptions.Values) {
                //              Log(Level.Verbose, 
                //                  String.Format(CultureInfo.InvariantCulture,"{0} Type '{1}'.", LogPrefix, optionte.GetType()));
                if (!option.IfDefined || option.UnlessDefined) {
                    // skip option
                    continue;
                }
                AddArg(option.Value);
            }
        }

        /// <summary>
        /// Append the command line options or commen names for the options
        ///     to the generic options collection.  This is then piped to the
        ///     command line as a switch.
        /// </summary>
        private void AppendCommandOptions () {
            foreach (Option option in CommandOptions.Values) {
                if (!option.IfDefined || option.UnlessDefined) {
                    // skip option
                    continue;
                }
                AddArg(option.Value);
            }
        }

        /// <summary>
        /// Add the given argument to the command line options.
        /// </summary>
        /// <param name="arg"></param>
        protected void AddArg (String arg) {
            Arguments.Add(new Argument(String.Format(CultureInfo.InvariantCulture,"{0}",
                arg)));
        }

        #endregion Private Instance Methods
    }
}