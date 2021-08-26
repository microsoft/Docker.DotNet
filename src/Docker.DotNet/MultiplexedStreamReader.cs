using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Docker.DotNet
{
    public class MultiplexedStreamReader
    {
        private readonly MultiplexedStream _stream;

        public MultiplexedStreamReader(MultiplexedStream stream)
        {
            _stream = stream;
        }

        public async Task<string> ReadLineAsync(CancellationToken token = default)
        {
            var line = new List<byte>();

            var buffer = new byte[1];

            while (true)
            {
                var res = await _stream.ReadOutputAsync(buffer, 0, 1, token);

                if (res.Count == 0)
                {
                    continue;
                }

                else if (buffer[0] == '\n')
                {
                    return Encoding.UTF8.GetString(line.ToArray());
                }

                else
                {
                    line.Add(buffer[0]);
                }
            }
        }
    }
}
