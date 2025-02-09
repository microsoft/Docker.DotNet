using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Docker.DotNet.Tests
{
    [Collection(nameof(TestCollection))]
    public class ISystemOperationsTests
    {
        private readonly CancellationTokenSource _cts;

        private readonly TestOutput _output;
        private readonly string _repositoryName;
        private readonly string _tag;
        private readonly DockerClient _dockerClient;

        public ISystemOperationsTests(TestFixture testFixture, ITestOutputHelper outputHelper)
        {
            _output = new TestOutput(outputHelper);

            _dockerClient = testFixture.DockerClient;

            // Do not wait forever in case it gets stuck
            _cts = CancellationTokenSource.CreateLinkedTokenSource(testFixture.Cts.Token);
            _cts.CancelAfter(TimeSpan.FromMinutes(5));
            _cts.Token.Register(() => throw new TimeoutException("SystemOperationsTests timeout"));

            _repositoryName = testFixture.Repository;
            _tag = testFixture.Tag;
        }

        [Fact(Skip = "docker.exe not always a live process")]
        public void Docker_IsRunning()
        {
            var dockerProcess = Process.GetProcesses().FirstOrDefault(_ => _.ProcessName.Equals("docker", StringComparison.InvariantCultureIgnoreCase) || _.ProcessName.Equals("dockerd", StringComparison.InvariantCultureIgnoreCase));
            Assert.NotNull(dockerProcess); // docker is not running
        }

        [Fact]
        public async Task GetSystemInfoAsync_Succeeds()
        {
            var info = await _dockerClient.System.GetSystemInfoAsync();
            Assert.NotNull(info.Architecture);
        }

        [Fact]
        public async Task GetVersionAsync_Succeeds()
        {
            var version = await _dockerClient.System.GetVersionAsync();
            Assert.NotNull(version.APIVersion);
        }

        [Fact]
        public async Task MonitorEventsAsync_EmptyContainersList_CanBeCancelled()
        {
            var progress = new Progress<Message>();

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Task.Delay(1);

            await Assert.ThrowsAsync<TaskCanceledException>(() => _dockerClient.System.MonitorEventsAsync(new ContainerEventsParameters(), progress, cts.Token));

        }

        [Fact]
        public async Task MonitorEventsAsync_NullParameters_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _dockerClient.System.MonitorEventsAsync(null, null));
        }

        [Fact]
        public async Task MonitorEventsAsync_NullProgress_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => _dockerClient.System.MonitorEventsAsync(new ContainerEventsParameters(), null));
        }

        [Fact]
        public async Task MonitorEventsAsync_Succeeds()
        {
            var newTag = $"MonitorTests-{Guid.NewGuid().ToString().Substring(1, 10)}";

            var wasProgressCalled = false;

            var progressMessage = new Progress<Message>((m) =>
            {
                _output.WriteLine($"MonitorEventsAsync_Succeeds: Message - {m.Action} - {m.Status} {m.From} - {m.Type}");
                wasProgressCalled = true;
                Assert.NotNull(m);
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            var task = _dockerClient.System.MonitorEventsAsync(
                new ContainerEventsParameters(),
                progressMessage,
                cts.Token);

            await _dockerClient.Images.TagImageAsync($"{_repositoryName}:{_tag}", new ImageTagParameters { RepositoryName = _repositoryName, Tag = newTag }, _cts.Token);

            await _dockerClient.Images.DeleteImageAsync(
                name: $"{_repositoryName}:{newTag}",
                new ImageDeleteParameters
                {
                    Force = true
                },
                _cts.Token);

            // Give it some time for output operation to complete before cancelling task
            await Task.Delay(TimeSpan.FromSeconds(1));

            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => task).ConfigureAwait(false);

            Assert.True(wasProgressCalled);
        }

        [Fact]
        public async Task MonitorEventsAsync_IsCancelled_NoStreamCorruption()
        {
            var rand = new Random();
            var sw = new Stopwatch();

            for (int i = 0; i < 20; ++i)
            {
                try
                {
                    // (1) Create monitor task
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

                    string newImageTag = Guid.NewGuid().ToString();

                    var monitorTask = _dockerClient.System.MonitorEventsAsync(
                        new ContainerEventsParameters(),
                        new Progress<Message>((value) => _output.WriteLine($"DockerSystemEvent: {JsonConvert.SerializeObject(value)}")),
                        cts.Token);

                    // (2) Wait for some time to make sure we get into blocking IO call
                    await Task.Delay(100);

                    // (3) Invoke another request that will attempt to grab the same buffer
                    var listImagesTask1 = _dockerClient.Images.TagImageAsync(
                        $"{_repositoryName}:{_tag}",
                        new ImageTagParameters
                        {
                            RepositoryName = _repositoryName,
                            Tag = newImageTag,
                            Force = true
                        },
                        default);

                    // (4) Wait for a short bit again and cancel the monitor task - if we get lucky, we the list images call will grab the same buffer while
                    sw.Restart();
                    var iterations = rand.Next(15000000);

                    for (int j = 0; j < iterations; j++)
                    {
                        // noop
                    }
                    _output.WriteLine($"Waited for {sw.Elapsed.TotalMilliseconds} ms");

                    cts.Cancel();

                    listImagesTask1.GetAwaiter().GetResult();

                    _dockerClient.Images.TagImageAsync(
                        $"{_repositoryName}:{_tag}",
                        new ImageTagParameters
                        {
                            RepositoryName = _repositoryName,
                            Tag = newImageTag,
                            Force = true
                        }
                    ).GetAwaiter().GetResult();

                    monitorTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // Exceptions other than this causes test to fail
                }
            }
        }

        [Fact]
        public async Task MonitorEventsFiltered_Succeeds()
        {
            string newTag = $"MonitorTests-{Guid.NewGuid().ToString().Substring(1, 10)}";
            string newImageRespositoryName = Guid.NewGuid().ToString();

            await _dockerClient.Images.TagImageAsync(
                $"{_repositoryName}:{_tag}",
                new ImageTagParameters
                {
                    RepositoryName = newImageRespositoryName,
                    Tag = newTag
                },
                _cts.Token
            );

            ImageInspectResponse image = await _dockerClient.Images.InspectImageAsync(
                $"{newImageRespositoryName}:{newTag}",
                _cts.Token
            );

            var progressCalledCounter = 0;

            var eventsParams = new ContainerEventsParameters()
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>()
                {
                    {
                        "event", new Dictionary<string, bool>()
                        {
                            {
                                "tag", true
                            },
                            {
                                "untag", true
                            }
                        }
                    },
                    {
                        "type", new Dictionary<string, bool>()
                        {
                            {
                                "image", true
                            }
                        }
                    },
                    {
                        "image", new Dictionary<string, bool>()
                        {
                            {
                                image.ID, true
                            }
                        }
                    }
                }
            };

            var progress = new Progress<Message>((m) =>
            {
                Interlocked.Increment(ref progressCalledCounter);
                Assert.True(m.Status == "tag" || m.Status == "untag");
                _output.WriteLine($"MonitorEventsFiltered_Succeeds: Message received: {m.Action} - {m.Status} {m.From} - {m.Type}");
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var task = Task.Run(() => _dockerClient.System.MonitorEventsAsync(eventsParams, progress, cts.Token));

            await _dockerClient.Images.TagImageAsync($"{_repositoryName}:{_tag}", new ImageTagParameters { RepositoryName = _repositoryName, Tag = newTag });
            await _dockerClient.Images.DeleteImageAsync($"{_repositoryName}:{newTag}", new ImageDeleteParameters());

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters { Image = $"{_repositoryName}:{_tag}" });
            await _dockerClient.Containers.RemoveContainerAsync(createContainerResponse.ID, new ContainerRemoveParameters(), cts.Token);

            await Task.Delay(TimeSpan.FromSeconds(1));
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => task);

            Assert.Equal(2, progressCalledCounter);
            Assert.True(task.IsCanceled);
        }

        [Fact]
        public async Task PingAsync_Succeeds()
        {
            await _dockerClient.System.PingAsync();
        }
    }
}
