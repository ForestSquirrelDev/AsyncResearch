Если нужно, чтобы у асинхронной операции был таймаут - можно воспользоваться `WaitAsync`:
````csharp
public static async Task RunWaitAsyncExample()
{
    await MyMethod().WaitAsync(TimeSpan.FromSeconds(2));
}

private static async Task MyMethod()
{
    await Task.Delay(3000);
    Console.WriteLine("Hello World!");
}
````
В данном примере, метод `MyMethod` не выполнится раньше таймаута в 2 секунды, и будет выброшено исключение `TimeoutException`.

Под капотом `WaitAsync` создаёт `CancellationPromise`. Тот создаёт системный таймер и подписывается на его выполнение:
````csharp
if (millisecondsDelay != Timeout.UnsignedInfinite) // Task.cs, CS: 2873
{
    TimerCallback callback = static state =>
    {
        var thisRef = (CancellationPromise<TResult>)state!;
        if (thisRef.TrySetException(new TimeoutException()))
        {
            thisRef.Cleanup();
        }
    };

    if (timeProvider == TimeProvider.System)
    {
        _timer = new TimerQueueTimer(callback, this, millisecondsDelay, Timeout.UnsignedInfinite, flowExecutionContext: false);
    }
    else
    {
        using (ExecutionContext.SuppressFlow())
        {
            _timer = timeProvider.CreateTimer(callback, this, TimeSpan.FromMilliseconds(millisecondsDelay), Timeout.InfiniteTimeSpan);
        }
    }
}
````
Если таймер внутри `CancellationPromise` сработает раньше - `CancellationPromise` засетит внутрь себя `TimeoutException`. Если таск завершится успехом раньше - засетит `Result`.
Аналогичным образом работает перегрузка с `CancellationToken`: если токен отменится раньше, чем таска выполнится - `CancellationPromise` засетит себе `TrySetCanceled`.

При этом MyMethod в примере это не отменит: он продолжит выполняться. Лишь `CancellationPromise` примет состояние отмены или таймаута, но на продолжение `MyMethod` это не повлияет.