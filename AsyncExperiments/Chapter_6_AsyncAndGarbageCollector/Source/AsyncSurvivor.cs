namespace AsyncResearch.AsyncExperiments.Chapter_6_AsyncAndGarbageCollector.Source
{
    public class AsyncSurvivorCaller
    {
        public static async Task Test()
        {
            var tcs = new TaskCompletionSource();
            WeakReference weakRef = null;

            // Ограничиваем область видимости, чтобы локальная переменная 'victim' исчезла
            new Action(() => {
                var tcs = new TaskCompletionSource<bool>(); // Локальный TCS
                var victim = new AsyncSurvivor("Призрак");
                _ = victim.StayAliveAsync(tcs.Task);
                // Мы выходим из Action, ссылки на tcs и victim теряются. 
                // Никто не может вызвать tcs.SetResult().
            })();

            // Пытаемся вызвать GC несколько раз
            Console.WriteLine("\n--- Первая попытка GC (задача еще не завершена) ---");
            CollectGarbage();

            if (weakRef?.IsAlive ?? false)
                Console.WriteLine("Результат: Объект ЖИВ. Стейт-машина держит его зубами.");
            else
                Console.WriteLine("Результат: Объект умер. (Этого не должно случиться)");

            // Теперь завершаем задачу
            Console.WriteLine("\n--- Завершаем задачу (SetResult) ---");
            tcs.SetResult();
        
            // Даем немного времени стейт-машине дойти до конца
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

            // В этот момент стейт-машина захватывает 'this' 
            // и пакуется в кучу (Box), ожидая завершения tcs.Task
            await task;

            Console.WriteLine($"[{_name}] Я дождался! Мое имя всё еще: {_name}");
        }
    }
}