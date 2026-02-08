namespace AsyncResearch.AsyncExperiments.Chapter_3_SynchronizationContext
{
    public static class SynchronizationContextExample
    {
        public static void Run()
        {
            var context = new SimpleManualContext();
            SynchronizationContext.SetSynchronizationContext(context);

            Console.WriteLine($"[Main] Starting on thread: {Environment.CurrentManagedThreadId}");
            
            _ = DoWork();
            
            for (var i = 0; i < 5; i++)
            {
                Console.WriteLine($"[Engine] Tick {i}...");
                context.ExecuteTasks();
                Thread.Sleep(100);
            }
        }

        private static async Task DoWork()
        {
            await Task.Run(async () => {
                Console.WriteLine($"[Background] Working on thread: {Environment.CurrentManagedThreadId}");
                await Task.Delay(20);
            });
            
            Console.WriteLine($"[Main] Finished on thread: {Environment.CurrentManagedThreadId}");
        }
    }
}