namespace AsyncResearch.Chapter_6_AsyncGcSurvivor.Source
{
    public static class WhenAnyLeakExample
    {
        // Некий зависший сервис, который никогда не отвечает
        private static readonly TaskCompletionSource<bool> _eternalHangingService = new();

        public static async Task RunWhenAnyLeakExample()
        {
            Console.WriteLine($"[Start] Memory: {GetMemory()} KB");

            for (var i = 1; i <= 5; i++)
            {
                await ProcessWithTimeout(i);
                
                GcCollect();
                Console.WriteLine($"[Iteration {i}] Memory after GC: {GetMemory()} KB");
            }

            Console.WriteLine("\n--- Итог: Память растет, потому что задачи копятся в _eternalHangingService ---");
        }

        private static async Task ProcessWithTimeout(int id)
        {
            // 1. Создаем "тяжелую" задачу, которая якобы ждет данные
            var heavyTask = SimulatedHeavyWorkAsync(id);

            // 2. Создаем задачу таймаута (очень быструю)
            var timeoutTask = Task.Delay(100);

            // 3. Ждем, кто быстрее. Таймаут всегда победит.
            var completedTask = await Task.WhenAny(heavyTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Console.WriteLine($"[Loop {id}] Таймаут сработал. Идем дальше, бросив heavyTask на произвол судьбы...");
            }
        }

        private static async Task SimulatedHeavyWorkAsync(int id)
        {
            // Захватываем 10 МБ данных в стейт-машину
            var heavyData = new byte[1024 * 1024 * 10]; 
            
            // Ждем ответа от "вечного" сервиса
            await _eternalHangingService.Task;

            // Этот код никогда не выполнится, но компилятор держит heavyData здесь
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