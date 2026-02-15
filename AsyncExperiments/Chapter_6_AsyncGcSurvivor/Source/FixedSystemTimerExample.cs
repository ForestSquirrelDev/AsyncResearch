namespace AsyncResearch.AsyncExperiments.Chapter_6_AsyncGcSurvivor.Source
{
    public static class FixedSystemTimerExample
    {
        public static async Task RunFixedSystemTimerExample()
        {
            var cts = new CancellationTokenSource();
            
            Console.WriteLine($"Start Memory: {GetMemory()}");
            
            for (int i = 0; i < 10; i++)
            {
                _ = Leak(cts.Token);
                Console.WriteLine($"Iteration {i}, memory: {GetMemory()}");
            }

            await cts.CancelAsync();
            await Task.Delay(1000);
            GcCollect();
            
            Console.WriteLine($"Memory after gc collect: {GetMemory()}");
        }

        private static async Task Leak(CancellationToken token)
        {
            var heavyData = new byte[1024 * 1024 * 100];
            try
            {
                await Task.Delay(120000, token);
                Console.WriteLine(heavyData.Length);
            }
            catch (OperationCanceledException e)
            {
                Console.WriteLine("Галя, у нас отмена!");
            }
        }
        
        private static void GcCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        
        private static long GetMemory() => GC.GetTotalMemory(true) / 1024;
    }
}