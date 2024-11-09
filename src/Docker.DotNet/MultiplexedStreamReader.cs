using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;

namespace Docker.DotNet
{
    public class MultiplexedStreamReader
    {
        private readonly MultiplexedStream _stream;

        public MultiplexedStreamReader(MultiplexedStream stream)
        {
            _stream = stream;
        }

        public async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {

            var line = new List<byte>();

            var buffer = new byte[1];

            while (!cancellationToken.IsCancellationRequested)
            {
                var res = await _stream.ReadOutputAsync(buffer, 0, 1, cancellationToken);

                if (res.Count == 0)
                {
                    return null;
                }

                else if (buffer[0] == '\n')
                {
                    break;
                }

                else
                {
                    line.Add(buffer[0]);
                }
            }

            return Encoding.UTF8.GetString(line.ToArray());
        }
    }
}
