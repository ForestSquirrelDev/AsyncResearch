using BenchmarkDotNet.Attributes;

namespace AsyncResearch.Chapter_8_Benchmarks.Source
{
    [MemoryDiagnoser]
    public class ValueTaskBenchmarks
    {
        private readonly int _cachedValue = 12345;
        
        [Benchmark]
        public async ValueTask ValueTaskStateMachine()
        {
        }
        
        [Benchmark]
        public async ValueTask ValueTaskStateMachineWithYield()
        {
            await Task.Yield();
        }
        
        [Benchmark]
        public async Task TaskStateMachine()
        {
        }
        
        [Benchmark]
        public async Task TaskStateMachineWithYield()
        {
            await Task.Yield();
        }
        
        [Benchmark]
        public ValueTask NonAsyncValueTask()
        {
            return ValueTask.CompletedTask;
        }

        [Benchmark]
        public Task NonAsyncEmptyTask()
        {
            return Task.CompletedTask;
        }
        
        [Benchmark]
        public Task<int> GetValueViaTask()
        {
            return Task.FromResult(_cachedValue);
        }

        [Benchmark]
        public ValueTask<int> GetValueViaValueTask()
        {
            return new ValueTask<int>(_cachedValue);
        }
    }
}