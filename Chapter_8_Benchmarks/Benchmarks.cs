using BenchmarkDotNet.Attributes;

namespace AsyncResearch.AsyncExperiments.Chapter_8_Benchmarks
{
    [MemoryDiagnoser]
    public class StateMachineSizeBenchmark
    {
        [Benchmark]
        public async Task MeasureBareTask()
        {
            await Task.Yield();
        }
        
        [Benchmark]
        public async Task MeasureTaskDelay()
        {
            await Task.Delay(1);
        }
        
        [Benchmark]
        public async Task MeasureTaskDelayWithLong()
        {
            long myData = 42;
            await Task.Delay(1);
            _ = myData;
        }

        [Benchmark]
        public async Task MeasureTenTasks()
        {
            for (int i = 0; i < 10; i++)
            {
                await SimpleTask();
            }
        }

        private async Task SimpleTask()
        {
            await Task.Yield();
        }
    }
}