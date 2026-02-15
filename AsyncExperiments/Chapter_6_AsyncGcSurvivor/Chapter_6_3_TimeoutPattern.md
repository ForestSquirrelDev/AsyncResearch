Чуть менее очевидный способ получить утечку памяти - прибегнуть к паттерну "таймаута" через `Task.WhenAny()`.

Если мы хотим, чтобы у асинхронной операции был таймаут, у нас может возникнуть желание написать `Task.WhenAny(timeoutTask, workTask)`.
Если workTask по каким-то причинам никогда не завершится, это может привести к утечке. Возьмём пример:
````csharp
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
````

Здесь мы сделали вид, что есть некий зависший сервис, который никогда не завершит `_eternalHangingService.Task`. Task.WhenAny() запускает несколько `SimulatedHeavyWorkAsync`, те
захватывают массив байтов, и завязываются на "внешний сервис". Поскольку внешний сервис в нашем случае - это GC Root (статическая переменная), таски никогда не завершатся, и не станут
Eligible for GC.

В .NET 6.0+ это можно исправить через метод `WaitAsync()`, о котором в документации .NET написано:
`Gets a Task<TResult> that will complete when this Task<TResult> completes or when the specified CancellationToken has cancellation requested`.

Теперь мы можем отменить ожидание:
````csharp
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
````

Вывод программы: 
````
[Start] Memory: 275 KB
Галя, у нас отмена!
[Loop 1] Таймаут сработал. Идем дальше, бросив heavyTask на произвол судьбы...
[Iteration 1] Memory after GC: 300 KB
Галя, у нас отмена!
[Loop 2] Таймаут сработал. Идем дальше, бросив heavyTask на произвол судьбы...
[Iteration 2] Memory after GC: 301 KB
Галя, у нас отмена!
[Loop 3] Таймаут сработал. Идем дальше, бросив heavyTask на произвол судьбы...
[Iteration 3] Memory after GC: 301 KB
Галя, у нас отмена!
[Loop 4] Таймаут сработал. Идем дальше, бросив heavyTask на произвол судьбы...
[Iteration 4] Memory after GC: 302 KB
Галя, у нас отмена!
[Loop 5] Таймаут сработал. Идем дальше, бросив heavyTask на произвол судьбы...
[Iteration 5] Memory after GC: 302 KB

--- Итог: Память больше не растёт ---

Process finished with exit code 0.
````
