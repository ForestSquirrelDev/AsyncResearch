using AsyncResearch.Chapter_3_SynchronizationContext.Source;

namespace AsyncResearch.Chapter_4_AsyncVoid.Source
{
    public static class SynchronizationContextException
    {
        public static void Test()
        {
            var context = new SimpleManualContext();
            SynchronizationContext.SetSynchronizationContext(context);

            DoLegitWork();
            ThrowException();
            
            for (var i = 0; i < 5; i++)
            {
                Console.WriteLine($"[Engine] Tick {i}...");
                try
                {
                    context.ExecuteTasks();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
                Thread.Sleep(100);
            }
        }

        private static async void DoLegitWork()
        {
            await Task.Delay(100);
            Console.WriteLine("Doing legit work...");
        }

        private static async void ThrowException()
        {
            await Task.Delay(100);
            Console.WriteLine("Throwing exception...");
            throw new Exception("HORY SHIET!");
        }
    }
}