namespace AsyncResearch.Chapter_6_AsyncGcSurvivor.Source
{
    public static class AsyncSurvivorExample
    {
        public static async Task RunAsyncSurvivorExample()
        {
            var tcs = new TaskCompletionSource();
            WeakReference weakRef = null;

            void CreateAndReleaseVictim()
            {
                var victim = new AsyncSurvivor("Призрак");
                _ = victim.StayAliveAsync(tcs.Task);
                weakRef = new WeakReference(victim);
            }
            CreateAndReleaseVictim();

            Console.WriteLine("\n--- Первая попытка GC (задача еще не завершена) ---");
            CollectGarbage();

            if (weakRef?.IsAlive ?? false)
                Console.WriteLine("Результат: Объект ЖИВ. Стейт-машина держит его зубами.");
            else
                Console.WriteLine("Результат: Объект умер. (Этого не должно случиться)");

            Console.WriteLine("\n--- Завершаем задачу (SetResult) ---");
            tcs.SetResult();
        
            await Task.Yield();

            Console.WriteLine("\n--- Вторая попытка GC (задача завершена) ---");
            CollectGarbage();

            if (weakRef?.IsAlive ?? false)
                Console.WriteLine("Результат: Объект всё еще жив? (Странно)");
            else
                Console.WriteLine("Результат: Объект УНИЧТОЖЕН. Цепочка разорвана.");
        }

        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    public class AsyncSurvivor
    {
        private readonly string _name;

        public AsyncSurvivor(string name) => _name = name;

        ~AsyncSurvivor() => Console.WriteLine($"[GC] {_name} был уничтожен!");

        public async Task StayAliveAsync(Task task)
        {
            Console.WriteLine($"[{_name}] Начинаю работу и жду сигнала...");
            
            await task;

            Console.WriteLine($"[{_name}] Я дождался! Мое имя всё еще: {_name}");
        }
    }
}