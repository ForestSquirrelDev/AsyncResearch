using AsyncResearch.AsyncExperiments.Chapter_3_SynchronizationContext.Source;

namespace AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class AsyncVoidSynchronizationContextException
    {
        public static void AsyncVoidExceptionTest()
        {
            var context = new SimpleManualContext();
            SynchronizationContext.SetSynchronizationContext(context);

            Console.WriteLine($"[Main] Starting on thread: {Environment.CurrentManagedThreadId}");
            
            _ = Layer0();
            
            for (var i = 0; i < 15; i++)
            {
                Console.WriteLine($"[Engine] Tick {i}...");
                context.ExecuteTasks();
                Thread.Sleep(100);
            }
        }

        private static async Task Layer0()
        {
            await Task.Delay(100);
            await Layer1();
        }

        private static async Task Layer1()
        {
            await Task.Delay(100);
            await Layer2();
        }
        
        private static async Task Layer2()
        {
            await Task.Delay(100);
            await Layer3();
        }

        private static async Task Layer3()
        {
            await Task.Delay(100);
            Layer4();
        }
        
        private static async void Layer4()
        {
            await Task.Delay(100);
            throw new Exception("HORY SHIET!");
        }
    }
}