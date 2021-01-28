﻿//#define enable_test_commands

using OrbitalShell.Component.CommandLine.CommandBatch;
using OrbitalShell.Component.CommandLine.CommandModel;
using OrbitalShell.Component.CommandLine.Data;
using OrbitalShell.Component.CommandLine.Parsing;
using OrbitalShell.Component.CommandLine.Pipeline;
using OrbitalShell.Component.CommandLine.Variable;
using OrbitalShell.Component.CommandLine.Module;
using OrbitalShell.Console;
using OrbitalShell.Lib;
using lib = OrbitalShell.Lib;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using static OrbitalShell.Component.CommandLine.Parsing.CommandLineParser;
using static OrbitalShell.DotNetConsole;
using cmdlr = OrbitalShell.Component.CommandLine.CommandLineReader;
using cons = System.Console;
using static OrbitalShell.Component.EchoDirective.Shortcuts;
using OrbitalShell.Component.EchoDirective;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OrbitalShell.Component.CommandLine.Processor
{
    public class CommandLineProcessor
    {
        static object _logFileLock = new object();

        #region attributes

        public CommandLineProcessorSettings Settings { get; protected set; }

        public CancellationTokenSource CancellationTokenSource;

        /// <summary>
        /// shell args
        /// </summary>
        public string[] Args => (string[])_args?.Clone();

        string[] _args;

        bool _isInitialized = false;

        readonly SyntaxAnalyser _syntaxAnalyzer = new SyntaxAnalyser();

        public CommandsHistory CmdsHistory { get; protected set; }

        public CommandsAlias CommandsAlias { get; protected set; }

        public cmdlr.CommandLineReader CommandLineReader { get; set; }

        public CommandEvaluationContext CommandEvaluationContext { get; protected set; }

        public CommandBatchProcessor CommandBatchProcessor { get; protected set; }

        CommandLineProcessorSettings _settings;

        CommandEvaluationContext _commandEvaluationContext = null;

        public readonly ModuleManager ModuleManager;

        #endregion

        #region cli methods

        public string Arg(int n)
        {
            if (_args == null) return null;
            if (_args.Length <= n) return null;
            return _args[n];
        }

        public bool HasArgs => _args != null && _args.Length > 0;

        public const string OPT_ENV = "--env";
        public const string OPT_NAME_VALUE_SEPARATOR = ":";

        void SetArgs(
            string[] args,
            CommandEvaluationContext context,
            List<string> appliedSettings)
        {
            _args = (string[])args?.Clone();

            // parse and apply any -env:{VarName}={VarValue} argument
            foreach (var arg in args)
            {
                if (arg.StartsWith(OPT_ENV + OPT_NAME_VALUE_SEPARATOR))
                {
                    try
                    {
                        var t = arg.Split(':');
                        var t2 = t[1].Split('=');
                        if (t.Length == 2 && t[0] == OPT_ENV && t2.Length == 2)
                        {
                            SetVariable(context, t2[0], t2[1]);
                            appliedSettings.Add(arg);
                        }
                        else
                            Error($"shell arg set error: syntax error: {arg}", true);
                    }
                    catch (Exception ex)
                    {
                        Error($"shell arg set error: {arg} (error is: {ex.Message})", true);
                    }
                }
            }
        }

        /// <summary>
        /// set a typed variable from a string value<br/>
        /// don't set the value if conversion has failed
        /// </summary>
        /// <param name="name">name including namespace</param>
        /// <param name="value">value that must be converted to var type an assigned to the var</param>
        void SetVariable(CommandEvaluationContext context, string name, string value)
        {
            var tn = VariableSyntax.SplitPath(name);
            var t = new ArraySegment<string>(tn);
            if (context.ShellEnv.Get(t, out var o) && o is DataValue val)
            {
                if (ValueTextParser.ToTypedValue(value, val.ValueType, out var v, out _))
                    val.SetValue(v);
            }
            else
                Error($"variable not found: {Variables.Nsp(VariableNamespace.env, context.ShellEnv.Name, name)}", true);
        }

        #endregion

        #region command engine operations

        public CommandLineProcessor(
            string[] args,
            CommandLineProcessorSettings settings = null,
            CommandEvaluationContext commandEvaluationContext = null
            )
        {
            ModuleManager = new ModuleManager(_syntaxAnalyzer);
            _args = args;
            _commandEvaluationContext = commandEvaluationContext;
            settings ??= new CommandLineProcessorSettings();
            _settings = settings;
            CommandBatchProcessor = new CommandBatchProcessor();
        }

        /// <summary>
        /// shell init actions sequence<br/>
        /// use system in,out,err TODO: plugable to any stream - just add parameters
        /// </summary>
        /// <param name="args">orbsh args</param>
        /// <param name="settings">(launch) settings object</param>
        /// <param name="context">shell default command evaluation context.Provides null to build a new one</param>
        void ShellInit(
            string[] args,
            CommandLineProcessorSettings settings,
            CommandEvaluationContext context = null)
        {
            _args = (string[])args?.Clone();
            Settings = settings;

            context ??= new CommandEvaluationContext(
                this,
                /*settings.Out,
                settings.In,
                settings.Err,*/
                Out,
                In,
                Err,
                null
            );
            CommandEvaluationContext = context;
            Settings.Initialize(context);

            // pre console init
            if (DefaultForeground != null) cons.ForegroundColor = DefaultForeground.Value;

            // apply orbsh command args -env:{varName}={varValue}
            var appliedSettings = new List<string>();
            SetArgs(args, CommandEvaluationContext, appliedSettings);

            // init from settings
            ShellInitFromSettings();

            ConsoleInit(CommandEvaluationContext);

            if (settings.PrintInfo) PrintInfo(CommandEvaluationContext);

            // load kernel modules

            var a = Assembly.GetExecutingAssembly();
            Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"loading kernel module: '{a}' ... ", true, false);
            var moduleSpecification = ModuleManager.RegisterModule(CommandEvaluationContext, a);
            Done(moduleSpecification.Info.GetDescriptor(context));

            // can't reference by type an external module for which we have not a project reference
            a = Assembly.LoadWithPartialName(settings.KernelCommandsModuleAssemblyName);
            Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"loading kernel commands module: '{a}' ... ", true, false);
            moduleSpecification = ModuleManager.RegisterModule(CommandEvaluationContext, a);
            Done(moduleSpecification.Info.GetDescriptor(context));

            var lbr = false;

            Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"init user profile from: '{Settings.AppDataFolderPath}' ... ", true, false);

            lbr = InitUserProfileFolder(lbr);

            Done();
            Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"restoring user history file: '{Settings.HistoryFilePath}' ... ", true, false);

            lbr |= CreateRestoreUserHistoryFile(lbr);

            Done();
            Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"loading user aliases: '{Settings.CommandsAliasFilePath}' ... ", true, false);

            lbr |= CreateRestoreUserAliasesFile(lbr);

            Done();
            if (appliedSettings.Count > 0) Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"shell args: {string.Join(" ", appliedSettings)}");

            // end inits
            if (lbr) Out.Echoln();

            Out.Echoln();
        }

        void ShellInitFromSettings()
        {
            var ctx = CommandEvaluationContext;
            Out.EnableAvoidEndOfLineFilledWithBackgroundColor = ctx.ShellEnv.GetValue<bool>(ShellEnvironmentVar.settings_console_enableAvoidEndOfLineFilledWithBackgroundColor);
            var prompt = ctx.ShellEnv.GetValue<string>(ShellEnvironmentVar.settings_console_prompt);
            CommandLineReader.SetDefaultPrompt(prompt);
        }

        /// <summary>
        /// init the console. basic init that generally occurs before any display
        /// </summary>
        void ConsoleInit(CommandEvaluationContext context)
        {
            var oWinWidth = context.ShellEnv.GetDataValue(ShellEnvironmentVar.settings_console_initialWindowWidth);
            var oWinHeight = context.ShellEnv.GetDataValue(ShellEnvironmentVar.settings_console_initialWindowHeight);

            if (context.ShellEnv.IsOptionSetted(ShellEnvironmentVar.settings_console_enableCompatibilityMode))
            {
                oWinWidth.SetValue(2000);
                oWinHeight.SetValue(2000);
            }

            var WinWidth = (int)oWinWidth.Value;
            var winHeight = (int)oWinHeight.Value;
            try
            {
                if (WinWidth > -1) System.Console.WindowWidth = WinWidth;
                if (winHeight > -1) System.Console.WindowHeight = winHeight;
                System.Console.Clear();
            }
            catch { }
        }

        public bool CreateRestoreUserAliasesFile(bool lbr)
        {
            // create/restore user aliases
            CommandsAlias = new CommandsAlias();
            var createNewCommandsAliasFile = !File.Exists(Settings.CommandsAliasFilePath);
            if (createNewCommandsAliasFile)
                Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"creating user commands aliases file: '{Settings.CommandsAliasFilePath}' ... ", false);
            try
            {
                if (createNewCommandsAliasFile)
                {
                    var defaultAliasFilePath = Path.Combine(Settings.DefaultsFolderPath, Settings.CommandsAliasFileName);
                    File.Copy(defaultAliasFilePath, Settings.CommandsAliasFilePath);
                    lbr |= true;
                    Success();
                }
            }
            catch (Exception createUserProfileFileException)
            {
                Fail(createUserProfileFileException);
            }
            return lbr;
        }

        public bool CreateRestoreUserHistoryFile(bool lbr)
        {
            // create/restore commands history
            CmdsHistory = new CommandsHistory();
            var createNewHistoryFile = !File.Exists(Settings.HistoryFilePath);
            if (createNewHistoryFile)
                Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"creating user commands history file: '{Settings.HistoryFilePath}' ... ", false);
            try
            {
                if (createNewHistoryFile)
#pragma warning disable CS0642 // Possibilité d'instruction vide erronée
                    using (var fs = File.Create(Settings.HistoryFilePath)) ;
#pragma warning restore CS0642 // Possibilité d'instruction vide erronée
                CmdsHistory.Init(Settings.AppDataFolderPath, Settings.HistoryFileName);
                if (createNewHistoryFile) Success();
            }
            catch (Exception createUserProfileFileException)
            {
                Fail(createUserProfileFileException);
            }
            lbr |= createNewHistoryFile;
            return lbr;
        }

        public bool InitUserProfileFolder(bool lbr)
        {
            // assume the application folder ($Env.APPDATA/OrbitalShell) exists and is initialized

            // creates user app data folders
            if (!Directory.Exists(Settings.AppDataFolderPath))
            {
                Settings.LogAppendAllLinesErrorIsEnabled = false;
                Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"creating user shell folder: '{Settings.AppDataFolderPath}' ... ", true, false);
                try
                {
                    Directory.CreateDirectory(Settings.AppDataFolderPath);
                    Success();
                }
                catch (Exception createAppDataFolderPathException)
                {
                    Fail(createAppDataFolderPathException);
                }
                lbr = true;
            }

            // initialize log file
            if (!File.Exists(Settings.LogFilePath))
            {
                Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"creating log file: '{Settings.LogFilePath}' ... ", true, false);
                try
                {
                    var logError = Log($"file created on {System.DateTime.Now}");
                    if (logError == null)
                        Success();
                    else
                        throw logError;
                }
                catch (Exception createLogFileException)
                {
                    Settings.LogAppendAllLinesErrorIsEnabled = false;
                    Fail(createLogFileException);
                }
                lbr = true;
            }

            // initialize user profile
            if (!File.Exists(Settings.UserProfileFilePath))
            {
                Info(CommandEvaluationContext.ShellEnv.Colors.Log + $"creating user profile file: '{Settings.UserProfileFilePath}' ... ", true, false);
                try
                {
                    var defaultProfileFilePath = Path.Combine(Settings.DefaultsFolderPath, Settings.UserProfileFileName);
                    File.Copy(defaultProfileFilePath, Settings.UserProfileFilePath);
                    Success();
                }
                catch (Exception createUserProfileFileException)
                {
                    Fail(createUserProfileFileException);
                }
                lbr = true;
            }

            return lbr;
        }

        /// <summary>
        /// run init scripts
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            ShellInit(_args, _settings, _commandEvaluationContext);

            // late init of settings from the context
            _settings.Initialize(CommandEvaluationContext);

            // run user profile
            try
            {
                CommandBatchProcessor.RunBatch(CommandEvaluationContext, Settings.UserProfileFilePath);
            }
            catch (Exception ex)
            {
                Warning($"Run 'user profile file' skipped. Reason is : {ex.Message}");
            }

            // run user aliases
            try
            {
                CommandsAlias.Init(CommandEvaluationContext, Settings.AppDataFolderPath, Settings.CommandsAliasFileName);
            }
            catch (Exception ex)
            {
                Warning($"Run 'user aliases' skipped. Reason is : {ex.Message}");
            }
            _isInitialized = true;
        }

        #region log screeen + file methods

        private string _LogMessage(string message, string prefix, string postfix = " : ")
            => (string.IsNullOrWhiteSpace(prefix)) ? message : (prefix + (message == null ? "" : $"{postfix}{message}"));

        void Success(string message = null, bool log = true, bool lineBreak = true, string prefix = "Success")
        {
            var logMessage = CommandEvaluationContext.ShellEnv.Colors.Success + _LogMessage(message, prefix);
            Out.Echoln(logMessage);
            if (log) Log(logMessage);
        }

        void Done(string message = null, bool log = true, bool lineBreak = true, string prefix = "Done")
        {
            var logMessage = CommandEvaluationContext.ShellEnv.Colors.Success + _LogMessage(message, prefix);
            Out.Echoln(logMessage);
            if (log) Log(logMessage);
        }

        void Info(string message, bool log = true, bool lineBreak = true, string prefix = "")
        {
            var logMessage = CommandEvaluationContext.ShellEnv.Colors.Log + _LogMessage(message, prefix);
            Out.Echo(logMessage, lineBreak);
            if (log) Log(logMessage);
        }

        void Fail(string message = null, bool log = true, bool lineBreak = true, string prefix = "Fail")
        {
            var logMessage = CommandEvaluationContext.ShellEnv.Colors.Error + _LogMessage(message, prefix, "");
            Out.Echo(logMessage, lineBreak);
            if (log) Log(logMessage);
        }

        void Warning(string message = null, bool log = true, bool lineBreak = true, string prefix = "Warning")
        {
            var logMessage = CommandEvaluationContext.ShellEnv.Colors.Warning + _LogMessage(message, prefix);
            Out.Echo(logMessage, lineBreak);
            if (log) LogWarning(logMessage);
        }

        void Fail(Exception exception, bool log = true, bool lineBreak = true, string prefix = "Fail : ")
        {
            var logMessage = CommandEvaluationContext.ShellEnv.Colors.Error + prefix + exception?.Message;
            Out.Echo(logMessage, lineBreak);
            if (log) LogError(logMessage);
        }

        void Error(string message = null, bool log = false, bool lineBreak = true, string prefix = "")
        {
            var logMessage = CommandEvaluationContext.ShellEnv.Colors.Error + prefix + (message == null ? "" : $"{message}");
            Out.Echo(logMessage, lineBreak);
            if (log) LogError(logMessage);
        }

        #endregion

        public void AssertCommandLineProcessorHasACommandLineReader()
        {
            if (CommandLineReader == null) throw new Exception("a command line reader is required by the command line processor to perform this action");
        }

        public void PrintInfo(CommandEvaluationContext context)
        {
            context.Out.Echoln($"{CommandEvaluationContext.ShellEnv.Colors.Label}{Uon} {Settings.AppLongName} ({Settings.AppName}) version {Assembly.GetExecutingAssembly().GetName().Version}" + ("".PadRight(18, ' ')) + Tdoff);
            context.Out.Echoln($" {Settings.AppEditor}");
            context.Out.Echoln($" {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture} - {RuntimeInformation.FrameworkDescription} - {lib.RuntimeEnvironment.OSType}");

            if (context.ShellEnv.GetValue<bool>(ShellEnvironmentVar.settings_console_banner_isEnabled))
            {
                try
                {
                    var banner =
                        File.ReadAllLines(
                            context.ShellEnv.GetValue<string>(ShellEnvironmentVar.settings_console_banner_path)
                        );
                    int c = context.ShellEnv.GetValue<int>(ShellEnvironmentVar.settings_console_banner_startColorIndex);
                    foreach (var line in banner)
                    {
                        context.Out.SetForeground(c);
                        context.Out.Echoln(line);
                        c += context.ShellEnv.GetValue<int>(ShellEnvironmentVar.settings_console_banner_colorIndexStep); ;
                    }
                    context.Out.Echoln();
                }
                catch (Exception ex)
                {
                    Error("banner error: " + ex.Message, true);
                }
            }
        }

        #region shell log operations

        public Exception Log(string text)
        {
            return LogInternal(text);
        }

        public Exception LogError(string text)
        {
            return LogInternal(text, CommandEvaluationContext.ShellEnv.Colors.Error + "ERR");
        }

        public Exception LogWarning(string text)
        {
            return LogInternal(text, CommandEvaluationContext.ShellEnv.Colors.Warning + "ERR");
        }

        Exception LogInternal(string text, string logPrefix = "INF")
        {
            var str = $"{logPrefix} [{Process.GetCurrentProcess().ProcessName}:{Process.GetCurrentProcess().Id},{Thread.CurrentThread.Name}:{Thread.CurrentThread.ManagedThreadId}] {System.DateTime.Now}.{System.DateTime.Now.Millisecond} | {text}";
            lock (_logFileLock)
            {
                try
                {
                    File.AppendAllLines(Settings.LogFilePath, new List<string> { str });
                    return null;
                }
                catch (Exception logAppendAllLinesException)
                {
                    if (Settings.LogAppendAllLinesErrorIsEnabled)
                        Errorln(logAppendAllLinesException.Message);
                    return logAppendAllLinesException;
                }
            }
        }

        #endregion



        #endregion

        #region commands operations

        /// <summary>
        /// parse and evaluate a command line<br/>
        /// 1. parse command line<br/>
        /// error or:<br/>
        /// 2. execute command<br/>
        ///     A. internal command (modules) or alias<br/>
        ///     B. underlying shell command (found in scan paths)<br/>
        ///      file: <br/>
        ///         C. file (batch)<br/>
        ///         D. non executable file<br/>
        ///     not a file:<br/>
        ///         E. unknown command<br/>
        /// </summary>
        /// <param name="context">command evaluation context</param>
        /// <param name="commandMethodInfo">method info of the invoked command</param>
        /// <param name="args">args are added to the command name to build the command line</param>
        /// <param name="outputX">begin x location of the command line expression in the console if applyable</param>
        /// <param name="postAnalysisPreExecOutput">text to be outputed before any analysis output</param>
        /// <returns>data returned by the analysis and the evaluation of an expression (analysis error or commmand returns or command error)</returns>
        public ExpressionEvaluationResult Eval(
            CommandEvaluationContext context,
            MethodInfo commandMethodInfo,
            string args,
            int outputX,
            string postAnalysisPreExecOutput = null)    // TODO: an evel options object would be nice
        {
            var comSpec = ModuleManager.ModuleCommandManager.GetCommandSpecification(commandMethodInfo);
            if (comSpec == null)
                throw new Exception($"command method info not found: dt={commandMethodInfo.DeclaringType.FullName} method name={commandMethodInfo.Name}");
            var expr = comSpec.Name + (!string.IsNullOrWhiteSpace(args) ? (" " + args) : "");
            return Eval(context, expr, outputX, postAnalysisPreExecOutput);
        }

        /// <summary>
        /// parse and evaluate a command line<br/>
        /// 1. parse command line<br/>
        /// error or:<br/>
        /// 2. execute command<br/>
        ///     A. internal command (modules) or alias<br/>
        ///     B. underlying shell command (found in scan paths)<br/>
        ///      file: <br/>
        ///         C. file (batch)<br/>
        ///         D. non executable file<br/>
        ///     not a file:<br/>
        ///         E. unknown command<br/>
        /// </summary>
        /// <param name="context">command evaluation context</param>
        /// <param name="expr">expression to be evaluated</param>
        /// <param name="outputX">begin x location of the command line expression in the console if applyable</param>
        /// <param name="postAnalysisPreExecOutput">text to be outputed before any analysis output</param>
        /// <returns>data returned by the analysis and the evaluation of an expression (analysis error or commmand returns or command error)</returns>
        public ExpressionEvaluationResult Eval(
            CommandEvaluationContext context,
            string expr,
            int outputX,
            string postAnalysisPreExecOutput = null)        // TODO: an evel options object would be nice
        {
            var pipelineParseResults = Parse(context, _syntaxAnalyzer, expr);
            bool allValid = true;
            var evalParses = new List<ExpressionEvaluationResult>();

            // check pipeline syntax analysis
            foreach (var pipelineParseResult in pipelineParseResults)
            {
                allValid &= pipelineParseResult.ParseResult.ParseResultType == ParseResultType.Valid;
                var evalParse = EvalParse(context, expr, outputX, pipelineParseResult.ParseResult);
                evalParses.Add(evalParse);
            }

            // eventually output the post analysis pre exec content
            if (!string.IsNullOrEmpty(postAnalysisPreExecOutput)) context.Out.Echo(postAnalysisPreExecOutput);

            if (!allValid)
            {
                // syntax error in pipeline - break exec
                var err = evalParses.FirstOrDefault();
                context.ShellEnv.UpdateVarLastCommandReturn(expr, null, err == null ? ReturnCode.OK : GetReturnCode(err), err?.SyntaxError);
                return err;
            }

            // run pipeline
            var evalRes = PipelineProcessor.RunPipeline(context, pipelineParseResults.FirstOrDefault());

            // update shell env
            context.ShellEnv.UpdateVarLastCommandReturn(expr, evalRes.Result, GetReturnCode(evalRes), evalRes.EvalErrorText, evalRes.EvalError);
            return evalRes;
        }

        ReturnCode GetReturnCode(ExpressionEvaluationResult expr)
        {
            var r = ReturnCode.Error;
            try
            {
                r = (ReturnCode)expr.EvalResultCode;
            }
            catch (Exception) { }
            return r;
        }

        /// <summary>
        /// react after parse within a parse unit result
        /// </summary>
        /// <param name="context"></param>
        /// <param name="expr"></param>
        /// <param name="outputX"></param>
        /// <param name="parseResult"></param>
        /// <returns></returns>
        ExpressionEvaluationResult EvalParse(
            CommandEvaluationContext context,
            string expr,
            int outputX,
            ParseResult parseResult
            )
        {
            ExpressionEvaluationResult r = null;
            var errorText = "";
            string[] t;
            int idx;
            string serr;

            switch (parseResult.ParseResultType)
            {
                /*
                    case ParseResultType.Valid:
                    var syntaxParsingResult = parseResult.SyntaxParsingResults.First();
                    try
                    {
                        var outputData = InvokeCommand(CommandEvaluationContext, syntaxParsingResult.CommandSyntax.CommandSpecification, syntaxParsingResult.MatchingParameters);

                        r = new ExpressionEvaluationResult(null, ParseResultType.Valid, outputData, (int)ReturnCode.OK, null);
                    } catch (Exception commandInvokeError)
                    {
                        var commandError = commandInvokeError.InnerException ?? commandInvokeError;
                        context.Errorln(commandError.Message);
                        return new ExpressionEvaluationResult(null, parseResult.ParseResultType, null, (int)ReturnCode.Error, commandError);
                    }
                    break;
                */

                case ParseResultType.Empty:
                    r = new ExpressionEvaluationResult(expr, null, parseResult.ParseResultType, null, (int)ReturnCode.OK, null);
                    break;

                case ParseResultType.NotValid:  /* command syntax not valid */
                    var perComErrs = new Dictionary<string, List<CommandSyntaxParsingResult>>();
                    foreach (var prs in parseResult.SyntaxParsingResults)
                        if (prs.CommandSyntax != null)
                        {
                            if (perComErrs.TryGetValue(prs.CommandSyntax?.CommandSpecification?.Name, out var lst))
                                lst.Add(prs);
                            else
                                perComErrs.Add(prs.CommandSyntax.CommandSpecification.Name, new List<CommandSyntaxParsingResult> { prs });
                        }

                    var errs = new List<string>();
                    var minErrPosition = int.MaxValue;
                    var errPositions = new List<int>();
                    foreach (var kvp in perComErrs)
                    {
                        var comSyntax = kvp.Value.First().CommandSyntax;
                        foreach (var prs in kvp.Value)
                        {
                            foreach (var perr in prs.ParseErrors)
                            {
                                minErrPosition = Math.Min(minErrPosition, perr.Position);
                                errPositions.Add(perr.Position);
                                if (!errs.Contains(perr.Description))
                                    errs.Add(perr.Description);
                            }
                            errorText += Br + Red + string.Join(Br + Red, errs);
                        }
                        errorText += $"{Br}{Red}for syntax: {comSyntax}{Br}";
                    }

                    errPositions.Sort();
                    errPositions = errPositions.Distinct().ToList();

                    t = new string[expr.Length + 2];
                    for (int i = 0; i < t.Length; i++) t[i] = " ";
                    foreach (var errPos in errPositions)
                    {
                        t[GetIndex(context, errPos, expr)] = Settings.ErrorPositionMarker + "";
                    }
                    serr = string.Join("", t);
                    Error(" ".PadLeft(outputX + 1) + serr, false, false);

                    Error(errorText);
                    r = new ExpressionEvaluationResult(expr, errorText, parseResult.ParseResultType, null, (int)ReturnCode.NotIdentified, null);
                    break;

                case ParseResultType.Ambiguous:
                    errorText += $"{Red}ambiguous syntaxes:{Br}";
                    foreach (var prs in parseResult.SyntaxParsingResults)
                        errorText += $"{Red}{prs.CommandSyntax}{Br}";
                    Error(errorText);
                    r = new ExpressionEvaluationResult(expr, errorText, parseResult.ParseResultType, null, (int)ReturnCode.NotIdentified, null);
                    break;

                case ParseResultType.NotIdentified:
                    t = new string[expr.Length + 2];
                    for (int j = 0; j < t.Length; j++) t[j] = " ";
                    var err = parseResult.SyntaxParsingResults.First().ParseErrors.First();
                    idx = err.Position;
                    t[idx] = Settings.ErrorPositionMarker + "";
                    errorText += Red + err.Description;
                    serr = string.Join("", t);
                    context.Errorln(" ".PadLeft(outputX) + serr);
                    context.Errorln(errorText);
                    r = new ExpressionEvaluationResult(expr, errorText, parseResult.ParseResultType, null, (int)ReturnCode.NotIdentified, null);
                    break;

                case ParseResultType.SyntaxError:
                    t = new string[expr.Length + 2];
                    for (int j = 0; j < t.Length; j++) t[j] = " ";
                    var err2 = parseResult.SyntaxParsingResults.First().ParseErrors.First();
                    idx = err2.Index;
                    t[idx] = Settings.ErrorPositionMarker + "";
                    errorText += Red + err2.Description;
                    serr = string.Join("", t);
                    context.Errorln(" ".PadLeft(outputX) + serr);
                    context.Errorln(errorText);
                    r = new ExpressionEvaluationResult(expr, errorText, parseResult.ParseResultType, null, (int)ReturnCode.NotIdentified, null);
                    break;
            }

            return r;
        }

        object InvokeCommand(
            CommandEvaluationContext context,
            CommandSpecification commandSpecification,
            MatchingParameters matchingParameters)
        {
            var parameters = new List<object>() { context };
            var pindex = 0;
            foreach (var parameter in commandSpecification.MethodInfo.GetParameters())
            {
                if (pindex > 0)
                {
                    if (matchingParameters.TryGet(parameter.Name, out var matchingParameter))
                        parameters.Add(matchingParameter.GetValue());
                    else
                        throw new InvalidOperationException($"parameter not found: '{parameter.Name}' when invoking command: {commandSpecification}");
                }
                pindex++;
            }
            var r = commandSpecification.MethodInfo
                .Invoke(commandSpecification.MethodOwner, parameters.ToArray());
            return r;
        }

        #endregion
    }
}
