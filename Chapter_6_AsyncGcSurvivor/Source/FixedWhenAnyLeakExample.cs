namespace AsyncResearch.Chapter_6_AsyncGcSurvivor.Source
{
    public static class FixedWhenAnyLeakExample
    {
        private static readonly TaskCompletionSource<bool> _eternalHangingService = new();

        public static async Task RunFixedWhenAnyLeakExample()
        {
            Console.WriteLine($"[Start] Memory: {GetMemory()} KB");

            for (var i = 1; i <= 5; i++)
            {
                await ProcessWithTimeout(i);
                
                GcCollect();
                Console.WriteLine($"[Iteration {i}] Memory after GC: {GetMemory()} KB");
            }

            Console.WriteLine("\n--- Итог: Память больше не растёт ---");
        }

        private static async Task ProcessWithTimeout(int id)
        {
            using var cts = new CancellationTokenSource();
            var heavyTask = SimulatedHeavyWorkAsync(cts.Token);
            var timeoutTask = Task.Delay(100, cts.Token);

            var completedTask = await Task.WhenAny(heavyTask, timeoutTask);

            await cts.CancelAsync();

            if (completedTask == timeoutTask)
            {
                Console.WriteLine($"[Loop {id}] Таймаут сработал. Идем дальше, бросив heavyTask на произвол судьбы...");
            }
        }

        private static async Task SimulatedHeavyWorkAsync(CancellationToken ctsToken)
        {
            var heavyData = new byte[1024 * 1024 * 10]; 
    
            try 
            {
                await _eternalHangingService.Task.WaitAsync(ctsToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Галя, у нас отмена!");
                return; 
            }

            Console.WriteLine(heavyData.Length); 
        }

        private static long GetMemory() => GC.GetTotalMemory(true) / 1024;

        private static void GcCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}