using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Docker.DotNet.Tests
{
    [Collection("Test collection")]
    public class IContainerOperationsTests
    {
        private readonly CancellationTokenSource _cts;
        private readonly DockerClient _dockerClient;
        private readonly DockerClientConfiguration _dockerClientConfiguration;
        private readonly string _imageId;
        private readonly Tests.TestOutput _output;

        public IContainerOperationsTests(TestFixture testFixture, ITestOutputHelper outputHelper)
        {
            // Do not wait forever in case it gets stuck
            _cts = CancellationTokenSource.CreateLinkedTokenSource(testFixture.cts.Token);
            _cts.CancelAfter(TimeSpan.FromMinutes(5));
            _cts.Token.Register(() => throw new TimeoutException("IContainerOperationsTest timeout"));

            _dockerClient = testFixture.dockerClient;
            _dockerClientConfiguration = testFixture.dockerClientConfiguration;
            _output = new TestOutput(outputHelper);
            _imageId = testFixture.imageId;
        }

        [Fact]
        public async Task CreateContainerAsync_CreatesContainer()
        {
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                },
                _cts.Token
            );

            Assert.NotNull(createContainerResponse);
            Assert.NotEmpty(createContainerResponse.ID);
        }

        // Timeout causing task to be cancelled
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public async Task CreateContainerAsync_TimeoutExpires_Fails(int millisecondsTimeout)
        {
            using var dockerClientWithTimeout = _dockerClientConfiguration.CreateClient();

            dockerClientWithTimeout.DefaultTimeout = TimeSpan.FromMilliseconds(millisecondsTimeout);

            _output.WriteLine($"Time available for CreateContainer operation: {millisecondsTimeout} ms'");

            var timer = new Stopwatch();
            timer.Start();

            var createContainerTask = dockerClientWithTimeout.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                },
                _cts.Token);

            _ = await Assert.ThrowsAsync<OperationCanceledException>(() => createContainerTask);

            timer.Stop();
            _output.WriteLine($"CreateContainerOperation finished after {timer.ElapsedMilliseconds} ms");

            Assert.True(createContainerTask.IsCanceled);
            Assert.True(createContainerTask.IsCompleted);
        }

        [Fact]
        public async Task GetContainerLogs_Follow_False_TaskIsCompleted()
        {
            using var containerLogsCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var logList = new List<string>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = false
                },
                _cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            containerLogsCts.CancelAfter(TimeSpan.FromSeconds(20));

            var containerLogsTask = _dockerClient.Containers.GetContainerLogsAsync(
                createContainerResponse.ID,
                new ContainerLogsParameters
                {
                    ShowStderr = true,
                    ShowStdout = true,
                    Timestamps = true,
                    Follow = true
                },
                containerLogsCts.Token,
                new Progress<string>((m) => { _output.WriteLine(m); logList.Add(m); })
            );

            await _dockerClient.Containers.StopContainerAsync(
                createContainerResponse.ID,
                new ContainerStopParameters(),
                _cts.Token
            );

            await containerLogsTask;
            Assert.True(containerLogsTask.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task GetContainerLogs_Tty_False_Follow_False_ReadsLogs()
        {
            using var containerLogsCts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            var logList = new List<string>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = false
                },
                _cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            await _dockerClient.Containers.GetContainerLogsAsync(
                createContainerResponse.ID,
                new ContainerLogsParameters
                {
                    ShowStderr = true,
                    ShowStdout = true,
                    Timestamps = true,
                    Follow = false
                },
                containerLogsCts.Token,
                new Progress<string>((m) => { logList.Add(m); _output.WriteLine(m); })
            );

            await _dockerClient.Containers.StopContainerAsync(
                createContainerResponse.ID,
                new ContainerStopParameters(),
                _cts.Token
                );

            _output.WriteLine($"Line count: {logList.Count}");

            Assert.NotEmpty(logList);
        }

        [Fact]
        public async Task GetContainerLogs_Tty_False_Follow_True_Requires_Task_To_Be_Cancelled()
        {
            using var containerLogsCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            var logList = new List<string>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = false
                },
                _cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            containerLogsCts.CancelAfter(TimeSpan.FromSeconds(5));

            // Will be cancelled after CancellationTokenSource interval, would run forever otherwise
            await Assert.ThrowsAsync<TaskCanceledException>(() => _dockerClient.Containers.GetContainerLogsAsync(
                createContainerResponse.ID,
                new ContainerLogsParameters
                {
                    ShowStderr = true,
                    ShowStdout = true,
                    Timestamps = true,
                    Follow = true
                },
                containerLogsCts.Token,
                new Progress<string>((m) => { _output.WriteLine(JsonConvert.SerializeObject(m)); logList.Add(m); })
            ));
        }

        [Fact]
        public async Task GetContainerLogs_Tty_True_Follow_True_Requires_Task_To_Be_Cancelled()
        {
            using var containerLogsCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var logList = new List<string>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = true
                },
                _cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            containerLogsCts.CancelAfter(TimeSpan.FromSeconds(10));

            var containerLogsTask = _dockerClient.Containers.GetContainerLogsAsync(
                createContainerResponse.ID,
                new ContainerLogsParameters
                {
                    ShowStderr = true,
                    ShowStdout = true,
                    Timestamps = true,
                    Follow = true
                },
                containerLogsCts.Token,
                new Progress<string>((m) => { _output.WriteLine(m); logList.Add(m); })
            );

            await Assert.ThrowsAsync<TaskCanceledException>(() => containerLogsTask);
        }

        [Fact]
        public async Task GetContainerLogs_Tty_True_Follow_True_StreamLogs_TaskIsCancelled()
        {
            using var containerLogsCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var logList = new List<string>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = true
                },
                _cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            containerLogsCts.CancelAfter(TimeSpan.FromSeconds(5));

            var containerLogsTask = _dockerClient.Containers.GetContainerLogsAsync(
                createContainerResponse.ID,
                new ContainerLogsParameters
                {
                    ShowStderr = true,
                    ShowStdout = true,
                    Timestamps = true,
                    Follow = true
                },
                containerLogsCts.Token,
                new Progress<string>((m) => { _output.WriteLine(m); logList.Add(m); })
            );

            await Task.Delay(TimeSpan.FromSeconds(10));

            await _dockerClient.Containers.StopContainerAsync(
                createContainerResponse.ID,
                new ContainerStopParameters
                {
                    WaitBeforeKillSeconds = 0
                },
                _cts.Token
            );

            await _dockerClient.Containers.RemoveContainerAsync(
                createContainerResponse.ID,
                new ContainerRemoveParameters
                {
                    Force = true
                },
                _cts.Token
            );

            await Assert.ThrowsAsync<TaskCanceledException>(() => containerLogsTask);

            _output.WriteLine(JsonConvert.SerializeObject(new
            {
                AsyncState = containerLogsTask.AsyncState,
                CreationOptions = containerLogsTask.CreationOptions,
                Exception = containerLogsTask.Exception,
                Id = containerLogsTask.Id,
                IsCanceled = containerLogsTask.IsCanceled,
                IsCompleted = containerLogsTask.IsCompleted,
                IsCompletedSuccessfully = containerLogsTask.IsCompletedSuccessfully,
                Status = containerLogsTask.Status
            }
            ));

            _output.WriteLine($"Line count: {logList.Count}");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.NotEmpty(logList);
        }

        [Fact]
        public async Task GetContainerLogs_Tty_True_ReadsLogs()
        {
            using var containerLogsCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var logList = new List<string>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = true
                },
                _cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            containerLogsCts.CancelAfter(TimeSpan.FromSeconds(5));

            var containerLogsTask = _dockerClient.Containers.GetContainerLogsAsync(
                createContainerResponse.ID,
                new ContainerLogsParameters
                {
                    ShowStderr = true,
                    ShowStdout = true,
                    Timestamps = true,
                    Follow = false
                },
                containerLogsCts.Token,
                new Progress<string>((m) => { _output.WriteLine(m); logList.Add(m); })
            );

            await Task.Delay(TimeSpan.FromSeconds(10));

            await _dockerClient.Containers.StopContainerAsync(
                createContainerResponse.ID,
                new ContainerStopParameters
                {
                    WaitBeforeKillSeconds = 0
                },
                _cts.Token
            );

            await _dockerClient.Containers.RemoveContainerAsync(
                createContainerResponse.ID,
                new ContainerRemoveParameters
                {
                    Force = true
                },
                _cts.Token
            );

            await containerLogsTask;

            _output.WriteLine(JsonConvert.SerializeObject(new
            {
                AsyncState = containerLogsTask.AsyncState,
                CreationOptions = containerLogsTask.CreationOptions,
                Exception = containerLogsTask.Exception,
                Id = containerLogsTask.Id,
                IsCanceled = containerLogsTask.IsCanceled,
                IsCompleted = containerLogsTask.IsCompleted,
                IsCompletedSuccessfully = containerLogsTask.IsCompletedSuccessfully,
                Status = containerLogsTask.Status
            }
            ));

            _output.WriteLine($"Line count: {logList.Count}");

            Assert.NotEmpty(logList);
        }

        [Fact]
        public async Task GetContainerStatsAsync_Tty_False_Stream_False_ReadsStats()
        {
            using var tcs = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var containerStatsList = new List<ContainerStatsResponse>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = false
                },
                _cts.Token
            );

            _ = await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            tcs.CancelAfter(TimeSpan.FromSeconds(10));

            await _dockerClient.Containers.GetContainerStatsAsync(
                createContainerResponse.ID,
                new ContainerStatsParameters
                {
                    Stream = false
                },
                new Progress<ContainerStatsResponse>((m) => { _output.WriteLine(m.ID); containerStatsList.Add(m); }),
                tcs.Token
            );

            await Task.Delay(TimeSpan.FromSeconds(10));

            Assert.NotEmpty(containerStatsList);
            Assert.Single(containerStatsList);
            _output.WriteLine($"ConntainerStats count: {containerStatsList.Count}");
        }

        [Fact]
        public async Task GetContainerStatsAsync_Tty_False_StreamStats()
        {
            using var tcs = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            using (tcs.Token.Register(() => throw new TimeoutException("GetContainerStatsAsync_Tty_False_StreamStats")))
            {
                _output.WriteLine($"Running test {MethodBase.GetCurrentMethod().Module}->{MethodBase.GetCurrentMethod().Name}");

                var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                    new CreateContainerParameters()
                    {
                        Image = _imageId,
                        Name = Guid.NewGuid().ToString(),
                        Tty = false
                    },
                    _cts.Token
                );

                _ = await _dockerClient.Containers.StartContainerAsync(
                    createContainerResponse.ID,
                            new ContainerStartParameters(),
                            _cts.Token
                        );

                List<ContainerStatsResponse> containerStatsList = new List<ContainerStatsResponse>();

                using var linkedCts = new CancellationTokenSource();
                linkedCts.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await _dockerClient.Containers.GetContainerStatsAsync(
                        createContainerResponse.ID,
                        new ContainerStatsParameters
                        {
                            Stream = true
                        },
                        new Progress<ContainerStatsResponse>((m) => { containerStatsList.Add(m); _output.WriteLine(JsonConvert.SerializeObject(m)); }),
                        linkedCts.Token
                    );
                }
                catch (TaskCanceledException)
                {
                    // this  is expected to  happen on task cancelaltion
                }

                _output.WriteLine($"Container stats count: {containerStatsList.Count}");
                Assert.NotEmpty(containerStatsList);
            }
        }

        [Fact]
        public async Task GetContainerStatsAsync_Tty_True_Stream_False_ReadsStats()
        {
            using var tcs = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var containerStatsList = new List<ContainerStatsResponse>();

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = true
                },
                _cts.Token
            );

            _ = await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            tcs.CancelAfter(TimeSpan.FromSeconds(10));

            await _dockerClient.Containers.GetContainerStatsAsync(
                createContainerResponse.ID,
                new ContainerStatsParameters
                {
                    Stream = false
                },
                new Progress<ContainerStatsResponse>((m) => { _output.WriteLine(m.ID); containerStatsList.Add(m); }),
                tcs.Token
            );

            await Task.Delay(TimeSpan.FromSeconds(10));

            Assert.NotEmpty(containerStatsList);
            Assert.Single(containerStatsList);
            _output.WriteLine($"ConntainerStats count: {containerStatsList.Count}");
        }

        [Fact]
        public async Task GetContainerStatsAsync_Tty_True_StreamStats()
        {
            using var tcs = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            using (tcs.Token.Register(() => throw new TimeoutException("GetContainerStatsAsync_Tty_True_StreamStats")))
            {
                _output.WriteLine($"Running test GetContainerStatsAsync_Tty_True_StreamStats");

                var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                    Tty = true
                },
                _cts.Token
            );

                _ = await _dockerClient.Containers.StartContainerAsync(
                    createContainerResponse.ID,
                            new ContainerStartParameters(),
                            _cts.Token
                        );

                List<ContainerStatsResponse> containerStatsList = new List<ContainerStatsResponse>();

                using var linkedTcs = CancellationTokenSource.CreateLinkedTokenSource(tcs.Token);
                linkedTcs.CancelAfter(TimeSpan.FromSeconds(5));

                try
                {
                    await _dockerClient.Containers.GetContainerStatsAsync(
                        createContainerResponse.ID,
                        new ContainerStatsParameters
                        {
                            Stream = true
                        },
                        new Progress<ContainerStatsResponse>((m) => { containerStatsList.Add(m); _output.WriteLine(JsonConvert.SerializeObject(m)); }),
                        linkedTcs.Token
                    );
                }
                catch (TaskCanceledException)
                {
                    // this  is expected to  happen on task cancelaltion
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
                _output.WriteLine($"Container stats count: {containerStatsList.Count}");
                Assert.NotEmpty(containerStatsList);
            }
        }

        [Fact]
        public async Task KillContainerAsync_ContainerRunning_Succeeds()
        {
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId
                },
                _cts.Token);

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            var inspectRunningContainerResponse = await _dockerClient.Containers.InspectContainerAsync(
                createContainerResponse.ID,
                _cts.Token);

            await _dockerClient.Containers.KillContainerAsync(
                createContainerResponse.ID,
                new ContainerKillParameters(),
                _cts.Token);

            var inspectKilledContainerResponse = await _dockerClient.Containers.InspectContainerAsync(
                createContainerResponse.ID,
                _cts.Token);

            Assert.True(inspectRunningContainerResponse.State.Running);
            Assert.False(inspectKilledContainerResponse.State.Running);
            Assert.Equal("exited", inspectKilledContainerResponse.State.Status);

            _output.WriteLine("Killed");
            _output.WriteLine(JsonConvert.SerializeObject(inspectKilledContainerResponse));
        }

        [Fact]
        public async Task ListContainersAsync_ContainerExists_Succeeds()
        {
            await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters()
            {
                Image = _imageId,
                Name = Guid.NewGuid().ToString()
            },
            _cts.Token);

            IList<ContainerListResponse> containerList = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["ancestor"] = new Dictionary<string, bool>
                        {
                            [_imageId] = true
                        }
                    },
                    All = true
                },
                _cts.Token
            );

            Assert.NotNull(containerList);
            Assert.NotEmpty(containerList);
        }

        [Fact]
        public async Task ListProcessesAsync_RunningContainer_Succeeds()
        {
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString()
                },
                _cts.Token
            );

            await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            var containerProcessesResponse = await _dockerClient.Containers.ListProcessesAsync(
                createContainerResponse.ID,
                new ContainerListProcessesParameters(),
                _cts.Token
            );

            _output.WriteLine($"Title  '{containerProcessesResponse.Titles[0]}' - '{containerProcessesResponse.Titles[1]}' - '{containerProcessesResponse.Titles[2]}' - '{containerProcessesResponse.Titles[3]}'");

            foreach (var processes in containerProcessesResponse.Processes)
            {
                _output.WriteLine($"Process '{processes[0]}' - ''{processes[1]}' - '{processes[2]}' - '{processes[3]}'");
            }

            Assert.NotNull(containerProcessesResponse);
            Assert.NotEmpty(containerProcessesResponse.Processes);
        }

        [Fact]
        public async Task RemoveContainerAsync_ContainerExists_Succeedes()
        {
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString()
                },
                _cts.Token
            );

            ContainerInspectResponse inspectCreatedContainer = await _dockerClient.Containers.InspectContainerAsync(
                createContainerResponse.ID,
                _cts.Token
            );

            await _dockerClient.Containers.RemoveContainerAsync(
                createContainerResponse.ID,
                new ContainerRemoveParameters
                {
                    Force = true
                },
                _cts.Token
            );

            Task inspectRemovedContainerTask = _dockerClient.Containers.InspectContainerAsync(
                createContainerResponse.ID,
                _cts.Token
            );

            Assert.NotNull(inspectCreatedContainer.State);
            await Assert.ThrowsAsync<DockerContainerNotFoundException>(() => inspectRemovedContainerTask);
        }

        [Fact]
        public async Task StartContainerAsync_ContainerExists_Succeeds()
        {
            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters()
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString()
                },
                _cts.Token
            );

            var startContainerResult = await _dockerClient.Containers.StartContainerAsync(
                createContainerResponse.ID,
                new ContainerStartParameters(),
                _cts.Token
            );

            Assert.True(startContainerResult);
        }

        [Fact]
        public async Task StartContainerAsync_ContainerNotExists_ThrowsException()
        {
            Task startContainerTask = _dockerClient.Containers.StartContainerAsync(
                Guid.NewGuid().ToString(),
                new ContainerStartParameters(),
                _cts.Token
            );

            await Assert.ThrowsAsync<DockerContainerNotFoundException>(() => startContainerTask);
        }

        [Fact]
        public async Task WaitContainerAsync_TokenIsCancelled_OperationCancelledException()
        {
            var stopWatch = new Stopwatch();

            using var waitContainerCts = new CancellationTokenSource(delay: TimeSpan.FromMinutes(5));

            var createContainerResponse = await _dockerClient.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Image = _imageId,
                    Name = Guid.NewGuid().ToString(),
                },
                waitContainerCts.Token
            );

            _output.WriteLine($"CreateContainerResponse: '{JsonConvert.SerializeObject(createContainerResponse)}'");

            var startContainerResult = await _dockerClient.Containers.StartContainerAsync(createContainerResponse.ID, new ContainerStartParameters(), waitContainerCts.Token);

            _output.WriteLine("Starting timeout to cancel WaitContainer operation.");

            TimeSpan delay = TimeSpan.FromSeconds(5);

            waitContainerCts.CancelAfter(delay);
            stopWatch.Start();

            // Will wait forever here if cancelation fails.
            var waitContainerTask = _dockerClient.Containers.WaitContainerAsync(createContainerResponse.ID, waitContainerCts.Token);

            var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => waitContainerTask);

            stopWatch.Stop();

            _output.WriteLine($"WaitContainerTask was cancelled after {stopWatch.ElapsedMilliseconds} ms");
            _output.WriteLine($"WaitContainerAsync: {stopWatch.Elapsed} elapsed");

            // Task should be cancelled when CancelAfter timespan expires
            TimeSpan tolerance = TimeSpan.FromMilliseconds(500);

            Assert.InRange(stopWatch.Elapsed, delay.Subtract(tolerance), delay.Add(tolerance));
            Assert.True(waitContainerTask.IsCanceled);
        }

        [Fact]
        public async Task CreateImageAsync_NonexistantImage_ThrowsDockerImageNotFoundException()
        {
            var parameters = new CreateContainerParameters
            {
                Image = "no-such-image-ytfghbkufhresdhtrjygvb",
            };
            Func<Task> op = async () => await _dockerClient.Containers.CreateContainerAsync(parameters);

            await Assert.ThrowsAsync<DockerImageNotFoundException>(op);
        }
    }
}
