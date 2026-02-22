В обычной ситуации, когда асинхронная операция предполагает упаковку стейт машины в управляемую кучу - методы вроде `Task.Delay()` используют у токена отмены 
метод `token.Register()`, тот переводит таску в состояние `Canceled`, и она пинает `MoveNext()` ожидающей стейт машины.

Но если мы написали логику, выполняющуюся синхронно, без упаковки в управляемую кучу, нам может понадобиться вручную отменить эту работу. Например:
````csharp
public static async Task RunHeavyWorkCancellationTokenExample()
{
    using var cts = new CancellationTokenSource();
    await Task.Yield();
    
    cts.Cancel();
    
    var heavyWorkResult = PerformHeavyWork(cts.Token);
    Console.WriteLine($"Heavy work: {heavyWorkResult}");
}

public static double PerformHeavyWork(CancellationToken ct)
{
    double accumulator = 0;
    const int iterations = 100_000_000;

    for (int i = 1; i <= iterations; i++)
    {
        ct.ThrowIfCancellationRequested();
        accumulator += Math.Exp(Math.Sqrt(i)) / Math.Sin(Math.Log(i + 1));
        if (i % 1000 == 0)
        {
            accumulator = Math.Pow(accumulator, 0.999999);
        }
    }

    return accumulator;
}
````
В методе `PerformHeavyWork` мы выполняем условную тяжёлую работу. Поскольку мы здесь не ждём чего-то, а выполняемся в конкретном потоке, единственный способ отреагировать на токен отмены - 
это вручную проверить его. Можно - через `ThrowIfCancellationRequested`, можно - через `token.IsCancellationRequested`. `ThrowIfCancellationRequested` отменит все таски по цепочке
ожидания, а `IsCancellationRequested` даст нам выйти из цикла.