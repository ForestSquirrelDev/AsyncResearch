using AsyncResearch.Chapter_5_Instruments.Source;
using AsyncResearch.Chapter_8_Benchmarks.Source;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await SocketServerExample.RunMemoryTestServer();
            //BenchmarkDotNet.Running.BenchmarkRunner.Run<ValueTaskBenchmarks>();
        }
    }
}