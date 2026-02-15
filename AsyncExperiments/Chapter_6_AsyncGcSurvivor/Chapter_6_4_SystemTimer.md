Способ создать утечку, весьма приближённый к реальности - это привязать стейт машину к `Task.Delay()`. Возьмём следующий пример:
````csharp
public static async Task RunSystemTimerExample()
{
    Console.WriteLine($"Start Memory: {GetMemory()}");
    for (int i = 0; i < 10; i++)
    {
        _ = Leak();
        Console.WriteLine($"Iteration {i}, memory: {GetMemory()}");
    }

    await Task.Delay(1000);
    GcCollect();
    Console.WriteLine($"Memory after gc collect: {GetMemory()}");
}

private static async Task Leak()
{
    var heavyData = new byte[1024 * 1024 * 100];
    await Task.Delay(120000);
    Console.WriteLine(heavyData.Length);
}

private static void GcCollect()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

private static long GetMemory() => GC.GetTotalMemory(true) / 1024;
````

Метод `Leak` разворачивается в стейт машину, которая захватывает массив байтов и уходит в ожидание на 120 секунд через системный таймер.
Мы вызываем этот метод 10 раз в цикле. При этом ни разу его не ожидая - т.е. мы не держим на него явной ссылки. Можно предположить, что раз мы не держим на методы ссылки, они
просто соберутся GC. Но это не так. Вывод программы будет следующим:
````
Start Memory: 275
Iteration 0, memory: 102687
Iteration 1, memory: 205083
Iteration 2, memory: 307484
Iteration 3, memory: 409884
Iteration 4, memory: 512288
Iteration 5, memory: 614685
Iteration 6, memory: 717085
Iteration 7, memory: 819485
Iteration 8, memory: 921885
Iteration 9, memory: 1024286
Memory after gc collect: 1024290

Process finished with exit code 0.
````

Ни один из аллоцированных массивов байтов не был собран GC, даже после того как мы зафорсили сборку. Почему?

Ответ на вопрос кроется в том, как реализован метод `Task.Delay()`. Первым делом он создаст `DelayPromise`:
````csharp
private static Task Delay(uint millisecondsDelay, TimeProvider timeProvider, CancellationToken cancellationToken) => // Task.cs, CS: 5675
    cancellationToken.IsCancellationRequested ? FromCanceled(cancellationToken) :
    millisecondsDelay == 0 ? CompletedTask :
    cancellationToken.CanBeCanceled ? new DelayPromiseWithCancellation(millisecondsDelay, timeProvider, cancellationToken) :
    new DelayPromise(millisecondsDelay, timeProvider);
````

Дальше будет создан таймер:
````csharp
...
if (millisecondsDelay != Timeout.UnsignedInfinite) // Task.cs, CS: 5708
{
    if (timeProvider == TimeProvider.System)
    {
        _timer = new TimerQueueTimer(s_timerCallback, this, millisecondsDelay, Timeout.UnsignedInfinite, flowExecutionContext: false);
    }
    else
    {
        using (ExecutionContext.SuppressFlow())
        {
            _timer = timeProvider.CreateTimer(s_timerCallback, this, TimeSpan.FromMilliseconds(millisecondsDelay), Timeout.InfiniteTimeSpan);
        }
    }

    if (IsCompleted)
    {
        _timer.Dispose();
    }
}
...
````
Это приведёт к созданию `TimerQueueTimer`, который в конечном счёте положит себя в очередь:
````csharp
internal TimerQueueTimer(TimerCallback timerCallback, object? state, uint dueTime, uint period, bool flowExecutionContext) // Timer.cs, CS: 501
{
    _timerCallback = timerCallback;
    _state = state;
    _dueTime = Timeout.UnsignedInfinite;
    _period = Timeout.UnsignedInfinite;
    if (flowExecutionContext)
    {
        _executionContext = ExecutionContext.Capture();
    }
    _associatedTimerQueue = TimerQueue.Instances[(uint)Thread.GetCurrentProcessorId() % TimerQueue.Instances.Length];
    
    if (dueTime != Timeout.UnsignedInfinite)
        Change(dueTime, period);
}
...
private void LinkTimer(TimerQueueTimer timer) // Timer.cs, CS: 382
{
    ref TimerQueueTimer? listHead = ref timer._short ? ref _shortTimers : ref _longTimers;
    timer._next = listHead;
    if (timer._next != null)
    {
        timer._next._prev = timer;
    }
    timer._prev = null;
    listHead = timer;
}
````
Обращаем внимание: таймер записывает к себе `timerCallback`, которым является `DelayPromise`. `DelayPromise` наследуется от `Task`, и поскольку в нашем примере 
стейт машина `Leak()` записалась к `DelayPromise` в `m_continuations`, он будет её держать. А `DelayPromise` будет держать очередь системных таймеров. Получается цепочка зависимостей,
которая не даёт собрать стейт машину с массивом байтов.

Избежать утечки достаточно просто: нужно использовать перегрузку `Delay` с токеном отмены:
````csharp
public static async Task RunFixedSystemTimerExample()
{
    var cts = new CancellationTokenSource();
    
    Console.WriteLine($"Start Memory: {GetMemory()}");
    
    for (int i = 0; i < 10; i++)
    {
        _ = Leak(cts.Token);
        Console.WriteLine($"Iteration {i}, memory: {GetMemory()}");
    }

    await cts.CancelAsync();
    await Task.Delay(1000);
    GcCollect();
    
    Console.WriteLine($"Memory after gc collect: {GetMemory()}");
}

private static async Task Leak(CancellationToken token)
{
    var heavyData = new byte[1024 * 1024 * 100];
    try
    {
        await Task.Delay(120000, token);
        Console.WriteLine(heavyData.Length);
    }
    catch (OperationCanceledException e)
    {
        Console.WriteLine("Галя, у нас отмена!");
    }
}
````

Вывод программы:
````
Start Memory: 275
Iteration 0, memory: 102683
Iteration 1, memory: 205084
Iteration 2, memory: 307484
Iteration 3, memory: 409885
Iteration 4, memory: 512285
Iteration 5, memory: 614685
Iteration 6, memory: 717086
Iteration 7, memory: 819486
Iteration 8, memory: 921887
Iteration 9, memory: 1024287
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Галя, у нас отмена!
Memory after gc collect: 302

Process finished with exit code 0.
````

После отмены токена - `DelayPromise` были удалены из очереди таймеров, и GC смог собрать стейт машины вместе с захваченными массивами байтов.