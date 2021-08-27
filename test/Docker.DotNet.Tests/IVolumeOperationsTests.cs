using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Docker.DotNet.Tests
{
	[Collection("Test collection")]
	public class IVolumeOperationsTests
	{

		private readonly CancellationTokenSource _cts;

		private readonly TestOutput _output;
		private readonly string _repositoryName;
		private readonly string _tag = Guid.NewGuid().ToString();
		private readonly DockerClientConfiguration _dockerConfiguration;
		private readonly DockerClient _dockerClient;

		public IVolumeOperationsTests(TestFixture testFixture, ITestOutputHelper _outputHelper)
		{
			_output = new TestOutput(_outputHelper);

			_dockerConfiguration = new DockerClientConfiguration();
			_dockerClient = _dockerConfiguration.CreateClient();

			// Do not wait forever in case it gets stuck
			_cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
			_cts.Token.Register(() => throw new TimeoutException("ImageOperationTests timeout"));

			_repositoryName = testFixture.repositoryName;
			_tag = testFixture.tag;
		}

		[Fact]
		public async Task ListAsync_VolumeExists_Succeeds()
		{
			const string volumeName = "docker-dotnet-test-volume";

			await _dockerClient.Volumes.CreateAsync(new VolumesCreateParameters
			{
				Name = volumeName,
			},
			_cts.Token);

			try
			{

				var response = await _dockerClient.Volumes.ListAsync(new VolumesListParameters()
				{
					Filters = new Dictionary<string, IDictionary<string, bool>>(),
				},
				_cts.Token);

				Assert.Contains(volumeName, response.Volumes.Select(volume => volume.Name));

			}
			finally
			{
				await _dockerClient.Volumes.RemoveAsync(volumeName, force: true, _cts.Token);
			}
		}
	}
}
