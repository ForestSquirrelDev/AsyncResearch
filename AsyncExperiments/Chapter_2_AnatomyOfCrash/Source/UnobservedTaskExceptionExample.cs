namespace AsyncResearch.AsyncExperiments.Chapter_2_AnatomyOfCrash.Source
{
    public static class UnobservedTaskExceptionExample
    {
        public static void TestCaller()
        {
            TaskScheduler.UnobservedTaskException += OnUnobservedException;
            
            _ = Test();
            Thread.Sleep(500);
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Console.WriteLine("GC finished");
        }
        
        private static void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Console.WriteLine($"Unobserved exception: {e.Exception}, sender {sender?.GetType()}");
        }

        private static async Task Test()
        {
            Console.WriteLine("Start");
            await DoWorkAsync();
            throw new Exception("HORY SHET!");
        }
        
        private static async Task DoWorkAsync()
        {
            var localVariable = 42;
            await Task.Delay(100);
            Console.WriteLine("Resumed with " + localVariable);
        }
    }
}