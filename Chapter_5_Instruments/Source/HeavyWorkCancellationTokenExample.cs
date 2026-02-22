namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class HeavyWorkCancellationTokenExample
    {
        public static async Task RunHeavyWorkCancellationTokenExample()
        {
            using var cts = new CancellationTokenSource();
            await Task.Yield();
            
            cts.Cancel();
            
            var heavyWorkResult = PerformHeavyWork(cts.Token);
            Console.WriteLine($"Heavy work: {heavyWorkResult}");
        }
        
        public static double PerformHeavyWork(CancellationToken ct)
        {
            double accumulator = 0;
            const int iterations = 100_000_000;

            for (int i = 1; i <= iterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                accumulator += Math.Exp(Math.Sqrt(i)) / Math.Sin(Math.Log(i + 1));
                if (i % 1000 == 0)
                {
                    accumulator = Math.Pow(accumulator, 0.999999);
                }
            }

            return accumulator;
        }
    }
}