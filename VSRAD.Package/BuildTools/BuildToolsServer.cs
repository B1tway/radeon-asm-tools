﻿using Microsoft.VisualStudio.ProjectSystem;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VSRAD.BuildTools;
using VSRAD.DebugServer.IPC.Commands;
using VSRAD.Package.ProjectSystem;
using VSRAD.Package.Server;
using VSRAD.Package.Utils;
using static VSRAD.BuildTools.IPCBuildResult;

namespace VSRAD.Package.BuildTools
{
    [Flags]
    public enum BuildSteps
    {
        Skip = 1,
        Preprocessor = 2,
        Disassembler = 4,
        FinalStep = 8
    }

    [Export]
    [AppliesTo(Constants.ProjectCapability)]
    public sealed class BuildToolsServer
    {
        public const string ErrorPreprocessorFileNotCreated = "Preprocessor output file is missing.";
        public const string ErrorPreprocessorFileUnchanged = "Preprocessor output file is unchanged after running the command.";

        public string PipeName => IPCBuildResult.GetIPCPipeName(_project.RootPath);

        private readonly ICommunicationChannel _channel;
        private readonly IOutputWindowManager _outputWindow;
        private readonly IFileSynchronizationManager _deployManager;
        private readonly IBuildErrorProcessor _errorProcessor;
        private readonly CancellationTokenSource _serverLoopCts = new CancellationTokenSource();

        private IProject _project;
        private BuildSteps? _buildStepsOverride;

        [ImportingConstructor]
        public BuildToolsServer(
            ICommunicationChannel channel,
            IOutputWindowManager outputWindow,
            IBuildErrorProcessor errorProcessor,
            IFileSynchronizationManager deployManager)
        {
            _channel = channel;
            _outputWindow = outputWindow;
            _errorProcessor = errorProcessor;
            _deployManager = deployManager;
        }

        public void SetProjectOnLoad(IProject project)
        {
            _project = project;
            VSPackage.TaskFactory.RunAsync(RunServerLoopAsync);
        }

        public void OnProjectUnloading()
        {
            _serverLoopCts.Cancel();
        }

        public void OverrideStepsForNextBuild(BuildSteps steps)
        {
            _buildStepsOverride = steps;
        }

        public async Task RunServerLoopAsync()
        {
            while (!_serverLoopCts.Token.IsCancellationRequested)
                using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    await server.WaitForConnectionAsync(_serverLoopCts.Token).ConfigureAwait(false);

                    byte[] message;
                    try
                    {
                        var buildResult = await BuildAsync().ConfigureAwait(false);
                        if (buildResult.TryGetResult(out var result, out var error))
                            message = result.ToArray();
                        else
                            message = new IPCBuildResult { ServerError = error.Message }.ToArray();
                    }
                    catch (Exception e)
                    {
                        message = new IPCBuildResult { ServerError = e.Message }.ToArray();
                    }

                    await server.WriteAsync(message, 0, message.Length);
                }
        }

        private BuildSteps GetBuildSteps(Options.BuildProfileOptions buildOptions)
        {
            if (_buildStepsOverride is BuildSteps overriddenSteps)
            {
                _buildStepsOverride = null;
                return overriddenSteps;
            }
            var steps = BuildSteps.Skip;
            if (buildOptions.RunPreprocessor)
                steps |= BuildSteps.Preprocessor;
            if (buildOptions.RunDisassembler)
                steps |= BuildSteps.Disassembler;
            if (!string.IsNullOrEmpty(buildOptions.Executable))
                steps |= BuildSteps.FinalStep;
            return steps;
        }

        private async Task<Result<IPCBuildResult>> BuildAsync()
        {
            await VSPackage.TaskFactory.SwitchToMainThreadAsync();
            var evaluator = await _project.GetMacroEvaluatorAsync(default);
            var buildOptions = await _project.Options.Profile.Build.EvaluateAsync(evaluator);
            var preprocessorOptions = await _project.Options.Profile.Preprocessor.EvaluateAsync(evaluator);
            var disassemblerOptions = await _project.Options.Profile.Disassembler.EvaluateAsync(evaluator);
            var buildSteps = GetBuildSteps(buildOptions);

            if (buildSteps == BuildSteps.Skip)
                return new IPCBuildResult { Skipped = true };

            await _deployManager.SynchronizeRemoteAsync().ConfigureAwait(false);
            var executor = new RemoteCommandExecutor("Build", _channel, _outputWindow);

            string preprocessedSource = null;
            if ((buildSteps & BuildSteps.Preprocessor) == BuildSteps.Preprocessor)
            {
                var ppResult = await RunPreprocessorAsync(executor, preprocessorOptions);
                if (!ppResult.TryGetResult(out var ppData, out var error))
                    return new Error("Preprocessor: " + error.Message);
                var (ppSource, ppExitCode, ppMessages) = ppData;
                if (ppExitCode != 0 || ppMessages.Any())
                    return new IPCBuildResult { ExitCode = ppExitCode, ErrorMessages = ppMessages.ToArray() };
                preprocessedSource = ppSource;
            }
            if ((buildSteps & BuildSteps.Disassembler) == BuildSteps.Disassembler)
            {
                var disasmCommand = new Execute
                {
                    Executable = disassemblerOptions.Executable,
                    Arguments = disassemblerOptions.Arguments,
                    WorkingDirectory = disassemblerOptions.WorkingDirectory
                };
                var disasmResult = await RunStepAsync(executor, disasmCommand, preprocessedSource);
                if (!disasmResult.TryGetResult(out var disasmData, out var error))
                    return new Error("Disassembler: " + error.Message);
                var (disasmExitCode, disasmMessages) = disasmData;
                if (disasmExitCode != 0 || disasmMessages.Any())
                    return new IPCBuildResult { ExitCode = disasmExitCode, ErrorMessages = disasmMessages.ToArray() };
            }
            if ((buildSteps & BuildSteps.FinalStep) == BuildSteps.FinalStep)
            {
                var finalStepCommand = new Execute
                {
                    Executable = buildOptions.Executable,
                    Arguments = buildOptions.Arguments,
                    WorkingDirectory = buildOptions.WorkingDirectory
                };
                var finalStepResult = await RunStepAsync(executor, finalStepCommand, preprocessedSource);
                if (!finalStepResult.TryGetResult(out var finalStepData, out var error))
                    return new Error("Final step: " + error.Message);
                var (finalStepExitCode, finalStepMessages) = finalStepData;
                return new IPCBuildResult { ExitCode = finalStepExitCode, ErrorMessages = finalStepMessages.ToArray() };
            }
            return new IPCBuildResult();
        }

        private async Task<Result<(string, int, IEnumerable<Message>)>> RunPreprocessorAsync(RemoteCommandExecutor executor, Options.PreprocessorProfileOptions options)
        {
            var command = new Execute { Executable = options.Executable, Arguments = options.Arguments, WorkingDirectory = options.WorkingDirectory };
            var response = await executor.ExecuteWithResultAsync(command, options.RemoteOutputFile, checkExitCode: false);
            if (!response.TryGetResult(out var result, out var error))
                return error;

            var preprocessedSource = Encoding.UTF8.GetString(result.Item2);
            var messages = await _errorProcessor.ExtractMessagesAsync(result.Item1.Stderr, null);

            return (preprocessedSource, result.Item1.ExitCode, messages);
        }

        private async Task<Result<(int, IEnumerable<Message>)>> RunStepAsync(RemoteCommandExecutor executor, Execute command, string preprocessed)
        {
            var response = await executor.ExecuteAsync(command, checkExitCode: false);
            if (!response.TryGetResult(out var result, out var error))
                return error;

            var messages = await _errorProcessor.ExtractMessagesAsync(result.Stderr, preprocessed);
            return (result.ExitCode, messages);
        }
    }
}
