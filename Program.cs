using AsyncResearch.AsyncExperiments.Chapter_5_Additionals;
using BenchmarkDotNet.Running;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            BenchmarkRunner.Run<StateMachineSizeBenchmark>();
        }
    }
}