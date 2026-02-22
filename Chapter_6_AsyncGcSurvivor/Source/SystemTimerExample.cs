namespace AsyncResearch.Chapter_6_AsyncGcSurvivor.Source
{
    public static class SystemTimerExample
    {
        public static async Task RunSystemTimerExample()
        {
            Console.WriteLine($"Start Memory: {GetMemory()}");
            for (int i = 0; i < 10; i++)
            {
                _ = Leak();
                Console.WriteLine($"Iteration {i}, memory: {GetMemory()}");
            }

            await Task.Delay(1000);
            GcCollect();
            Console.WriteLine($"Memory after gc collect: {GetMemory()}");
        }

        private static async Task Leak()
        {
            var heavyData = new byte[1024 * 1024 * 100];
            await Task.Delay(120000);
            Console.WriteLine(heavyData.Length);
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