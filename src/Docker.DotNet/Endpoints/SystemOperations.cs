using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;

namespace Docker.DotNet
{
    internal class SystemOperations : ISystemOperations
    {
        private readonly DockerClient _client;

        internal SystemOperations(DockerClient client)
        {
            _client = client;
        }

        public Task AuthenticateAsync(AuthConfig authConfig, CancellationToken cancellationToken = default)
        {
            var data = new JsonRequestContent<AuthConfig>(authConfig ?? throw new ArgumentNullException(nameof(authConfig)), _client.JsonSerializer);
            return _client.MakeRequestAsync(_client.NoErrorHandlers, HttpMethod.Post, "auth", null, data, cancellationToken);
        }

        public async Task<SystemInfoResponse> GetSystemInfoAsync(CancellationToken cancellationToken = default)
        {
            var response = await _client.MakeRequestAsync(_client.NoErrorHandlers, HttpMethod.Get, "info", cancellationToken).ConfigureAwait(false);
            return _client.JsonSerializer.DeserializeObject<SystemInfoResponse>(response.Body);
        }

        public async Task<VersionResponse> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            var response = await _client.MakeRequestAsync(_client.NoErrorHandlers, HttpMethod.Get, "version", cancellationToken).ConfigureAwait(false);
            return _client.JsonSerializer.DeserializeObject<VersionResponse>(response.Body);
        }

        public Task<Stream> MonitorEventsAsync(ContainerEventsParameters parameters, CancellationToken cancellationToken)
        {
            IQueryString queryParameters = new QueryString<ContainerEventsParameters>(parameters ?? throw new ArgumentNullException(nameof(parameters)));
            return _client.MakeRequestForStreamAsync(_client.NoErrorHandlers, HttpMethod.Get, "events", queryParameters, cancellationToken);
        }

        public Task MonitorEventsAsync(ContainerEventsParameters parameters, IProgress<Message> progress, CancellationToken cancellationToken = default)
        {
            return StreamUtil.MonitorStreamForMessagesAsync(
                MonitorEventsAsync(parameters, cancellationToken),
                cancellationToken,
                progress ?? throw new ArgumentNullException(nameof(progress)));
        }

        public Task PingAsync(CancellationToken cancellationToken = default)
        {
            return _client.MakeRequestAsync(_client.NoErrorHandlers, HttpMethod.Get, "_ping", cancellationToken);
        }
    }
}
