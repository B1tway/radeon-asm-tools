﻿using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using VSRAD.DebugServer.IPC.Commands;
using VSRAD.Package.Options;
using VSRAD.Package.ProjectSystem;
using VSRAD.Package.ProjectSystem.Macros;
using VSRAD.Package.Server;
using Xunit;

namespace VSRAD.PackageTests.Server
{
    public class FileSynchronizationManagerTests
    {
        private static readonly string _fixturesDir = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.FullName + @"\Server\Fixtures";
        private static readonly string _projectRoot = _fixturesDir + @"\Project";

        private const string _deployDirectory = "/home/kyubey/projects";

        [Fact]
        public async Task SynchronizeProjectTestAsync()
        {
            var channel = new MockCommunicationChannel();
            var sourceManager = new Mock<IProjectSourceManager>(MockBehavior.Strict);
            sourceManager
                .Setup((m) => m.SaveDocumentsAsync(DocumentSaveType.OpenDocuments))
                .Returns(Task.CompletedTask).Verifiable();
            var (project, syncer) = MakeProjectWithSyncer((opts) =>
                {
                    opts.General.DeployDirectory = _deployDirectory;
                    opts.General.CopySources = false;
                },
                channel.Object, sourceManager.Object);
            project.Setup((p) => p.SaveOptions()).Verifiable();

            await syncer.SynchronizeRemoteAsync();

            sourceManager.Verify(); // saves project documents
            project.Verify(); // saves project options
        }

        [Fact]
        public async Task DeployProjectFilesTestAsync()
        {
            var channel = new MockCommunicationChannel();
            var sourceManager = new Mock<IProjectSourceManager>();
            sourceManager
                .Setup(m => m.ListProjectFilesAsync())
                .ReturnsAsync(MakeSources("source.txt", "Include/include.txt"));

            var (project, syncer) = MakeProjectWithSyncer((opts) =>
                {
                    opts.General.DeployDirectory = _deployDirectory;
                    opts.General.CopySources = true;
                },
                channel.Object, sourceManager.Object);
            project.Setup((p) => p.SaveOptions());

            byte[] archive = null;
            channel.ThenExpect<Deploy>((deploy) =>
            {
                Assert.Equal(_deployDirectory, deploy.Destination);
                archive = deploy.Data;
            });

            await syncer.SynchronizeRemoteAsync();

            Assert.NotNull(archive);
            var deployedItems = ReadZipItems(archive);
            Assert.Equal(new HashSet<string> { "source.txt", "Include/include.txt" }, deployedItems);

            // does not redeploy when nothing is changed 
            archive = null;
            channel.ThenExpect<Deploy>((deploy) => archive = deploy.Data);
            await syncer.SynchronizeRemoteAsync();
            Assert.Null(archive);

            File.SetLastWriteTime($@"{_projectRoot}\source.txt", DateTime.Now);

            await syncer.SynchronizeRemoteAsync();
            Assert.NotNull(archive);
            deployedItems = ReadZipItems(archive);
            Assert.Equal(new HashSet<string> { "source.txt" }, deployedItems);
        }

        [Fact]
        public async Task DeployFilesWithAdditionalSourcesTestAsync()
        {
            var channel = new MockCommunicationChannel();
            var sourceManager = new Mock<IProjectSourceManager>();
            sourceManager
                .Setup(m => m.ListProjectFilesAsync())
                .ReturnsAsync(MakeSources("source.txt", "Include/include.txt"));

            var (project, syncer) = MakeProjectWithSyncer((opts) =>
                {
                    opts.General.DeployDirectory = _deployDirectory;
                    opts.General.CopySources = true;
                    opts.General.AdditionalSources = $@"{_fixturesDir}\AdditionalSources;{_fixturesDir}\separate.txt";
                },
                channel.Object, sourceManager.Object);
            project.Setup((p) => p.SaveOptions());

            byte[] archive = null;
            channel.ThenExpect<Deploy>((deploy) => archive = deploy.Data);

            await syncer.SynchronizeRemoteAsync();

            Assert.NotNull(archive);
            var deployedItems = ReadZipItems(archive);
            var expectedItems = new HashSet<string> { "source.txt", "Include/include.txt", "separate.txt", "notice.txt", "Nested/message.txt" };
            Assert.Equal(expectedItems, deployedItems);

            // does not redeploy when nothing is changed
            archive = null;
            channel.ThenExpect<Deploy>((deploy) => archive = deploy.Data);
            await syncer.SynchronizeRemoteAsync();
            Assert.Null(archive);

            // profile changed
            channel.RaiseConnectionStateChanged();

            await syncer.SynchronizeRemoteAsync();
            Assert.NotNull(archive);
            deployedItems = ReadZipItems(archive);
            Assert.Equal(expectedItems, deployedItems);
        }

        private static (Mock<IProject>, FileSynchronizationManager) MakeProjectWithSyncer(Action<ProfileOptions> setupProfile, ICommunicationChannel channel, IProjectSourceManager sourceManager = null)
        {
            TestHelper.InitializePackageTaskFactory();

            var project = new Mock<IProject>(MockBehavior.Strict);
            var options = new ProjectOptions();
            options.SetProfiles(new Dictionary<string, ProfileOptions> { { "Default", new ProfileOptions() } }, activeProfile: "Default");
            setupProfile(options.Profiles["Default"]);
            project.Setup((p) => p.Options).Returns(options);
            project.Setup((p) => p.RootPath).Returns(_projectRoot);

            var evaluator = new Mock<IMacroEvaluator>(MockBehavior.Strict);
            evaluator.Setup((e) => e.EvaluateAsync(_deployDirectory)).Returns(Task.FromResult(_deployDirectory));
            evaluator.Setup((e) => e.EvaluateAsync("$(ProjectDir)")).Returns(Task.FromResult(""));
            evaluator.Setup((e) => e.EvaluateAsync("$(" + RadMacros.DeployDirectory + ")")).Returns(Task.FromResult(""));
            project.Setup((p) => p.GetMacroEvaluatorAsync(It.IsAny<uint[]>(), It.IsAny<string[]>())).Returns(Task.FromResult(evaluator.Object));

            sourceManager = sourceManager ?? new Mock<IProjectSourceManager>().Object;
            var syncer = new FileSynchronizationManager(channel, project.Object, sourceManager);
            return (project, syncer);
        }

        private static IEnumerable<(string, string)> MakeSources(params string[] files) =>
            files.Select(f => (_projectRoot + "/" + f, f)).ToList();

        private static HashSet<string> ReadZipItems(byte[] zipBytes)
        {
            using (var stream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(stream))
                return archive.Entries.Select(entry => entry.FullName).ToHashSet();
        }
    }
}
