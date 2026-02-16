Ещё один неочевидный способ потерять исключение - использовать механику "либо выполнение, либо таймаут". Возьмём пример:
````csharp
public static async Task RunWaitAsyncExample()
{
    var heavyTask = DoHeavyWorkAsync();

    try
    {
        await heavyTask.WaitAsync(TimeSpan.FromMilliseconds(500));
        Console.WriteLine("Работа завершена вовремя");
    }
    catch (TimeoutException)
    {
        Console.WriteLine("Упс, таймаут ожидания! Уходим...");
    }
}

private static async Task DoHeavyWorkAsync()
{
    await Task.Delay(1000); // Она работает 1 секунду
    throw new InvalidOperationException("OH MY GOD!");
}
`````

В этом примере, под капотом в WaitAsync(), рантайм создаёт CancellationPromise.

В зависимости от того, как будут развиваться события, `CancellationPromise` сделает одно из трех:
1. Завершение оригинальной задачи (`task`) - с результатом или исключением:
````csharp
void ITaskCompletionAction.Invoke(Task completingTask) // Task.cs, CS: 2918
{
    Debug.Assert(completingTask.IsCompleted);

    bool set = completingTask.Status switch
    {
        TaskStatus.Canceled => TrySetCanceled(completingTask.CancellationToken, completingTask.GetCancellationExceptionDispatchInfo()),
        TaskStatus.Faulted => TrySetException(completingTask.GetExceptionDispatchInfos()),
        _ => completingTask is Task<TResult> taskTResult ? TrySetResult(taskTResult.Result) : TrySetResult(),
    };

    if (set)
    {
        Cleanup();
    }
}
````
2. Таймаут:
````csharp
...
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
...
````
3. Отмена:
````csharp
...
_registration = token.UnsafeRegister(static (state, cancellationToken) => // Task.cs, CS: 2898
{
    var thisRef = (CancellationPromise<TResult>)state!;
    if (thisRef.TrySetCanceled(cancellationToken))
    {
        thisRef.Cleanup();
    }
}, this);
...
````

Если мы отменим переданный токен, `TaskCanceledException` выбросится раньше завершения таски. 
Если таймаут истечёт раньше, чем закончится таска, нам прилетит `TimeoutException`.

Но в обоих случаях, таска на самом деле продолжает работать. Таймаут может истечь через 3000мс, а таска упадёт с ичклюением спустя 3100мс.
Вывод программы будет следующим:
````
Упс, таймаут ожидания! Уходим...

Process finished with exit code 0.
````

Несмотря на то, что в методе `DoHeavyWorkAsync` мы выбросили исключение, мы его не увидим - только событие `UnobservedTaskException`.