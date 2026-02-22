Возьмём пример:
````csharp
public static async Task RunTaskDelayCancellationTokenExample()
{
    var tokenSource = new CancellationTokenSource();
    var t = CancellableTaskDelay(tokenSource.Token);
    tokenSource.Cancel();
    await t;
}

private static async Task CancellableTaskDelay(CancellationToken token)
{
    await Task.Delay(1000, token);
}
````

Здесь мы вызываем перегрузку `Task.Delay` с токеном отмены, тут же отменяем токен, и дожидаемся `CancellableTaskDelay`.
Можно ожидать, что `await t` приведёт к выбросу `TaskCanceledException`. Так и оказывается. Вывод программы:
````
Unhandled exception. System.Threading.Tasks.TaskCanceledException: A task was canceled.
   at AsyncResearch.Chapter_5_Instruments.Source.TaskDelayCancellationTokenExample.CancellableTaskDelay(CancellationToken token)
   at AsyncResearch.Chapter_5_Instruments.Source.TaskDelayCancellationTokenExample.RunTaskDelayCancellationTokenExample()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)

Process finished with exit code -532,462,766.
````

Но как именно мы к этому пришли?

Отличия начались ещё в моменте вызова перегрузки `Delay` с токеном отмены. Вместо обычного `DelayPromise`, мы создали `DelayPromiseWithCancellation`:
````csharp
private static Task Delay(uint millisecondsDelay, TimeProvider timeProvider, CancellationToken cancellationToken) => // Task.cs, CS: 5675
    cancellationToken.IsCancellationRequested ? FromCanceled(cancellationToken) :
    millisecondsDelay == 0 ? CompletedTask :
    cancellationToken.CanBeCanceled ? new DelayPromiseWithCancellation(millisecondsDelay, timeProvider, cancellationToken) :
    new DelayPromise(millisecondsDelay, timeProvider);
````

Внутри конструктора, `DelayPromiseWithCancellation` регистрирует отмену себя же внутрь токена. Классики наследуются друг от друга в последовательности
`DelayPromiseWithCancellation --> DelayPromise --> Task`, поэтому `DelayPromiseWithCancellation` может напрямую добавить в регистрацию метод `Task.TrySetCanceled`:
````csharp
internal DelayPromiseWithCancellation(uint millisecondsDelay, TimeProvider timeProvider, CancellationToken token) : base(millisecondsDelay, timeProvider) // Task.cs, CS: 5757
{
    ...
    _registration = token.UnsafeRegister(static (state, cancellationToken) =>
    {
        var thisRef = (DelayPromiseWithCancellation)state!;
        
        thisRef.AtomicStateUpdate((int)TaskCreationOptions.RunContinuationsAsynchronously, 0);

        if (thisRef.TrySetCanceled(cancellationToken))
        {
            thisRef.Cleanup();
        }
    }, this);
    if (IsCompleted)
    {
        _registration.Dispose();
    }
}
````

Здесь интересен тот факт, что `DelayPromiseWithCancellation` сохраняет к себе значение, вернувшееся от `token.UnsafeRegister`:
````csharp
private readonly CancellationTokenRegistration _registration; // Task.cs, CS: 5755
````

И при любом исходе вызовет `Cleanup`. Если таймер завершился успехом - вызовется там:
````csharp
private void CompleteTimedOut() // Task.cs, CS: 5735
{
    if (TrySetResult())
    {
        Cleanup();
        ...
    }
}
````
Если токен был отменён раньше завершения таймера - то вызовется в самой отмене. Это нужно потому, что запись таска в регистрации токена отмены создаёт сильную ссылку для GC.
Если на `CancellationTokenSource` будут держать ссылку снаружи, `CTS` по цепочке удержит Task и стейт машину. В `Chapter_6_2_CancellationToken` есть пример такого поведения.

Так вот, мы вызвали `tcs.Cancel()`. Поскольку `DelayPromiseWithCancellation` добавил делегат со ссылкой на себя в `registrations`, произойдёт следующее. `CancellationTokenSource`
возьмёт `registrations` и вызовет коллбеки:
````csharp
...
else // CancellationTokenSource.cs, CS: 804
{
    node.ExecuteCallback();
}
...
````

И там уже `DelayPromiseWithCancellation` вызовет `TrySetCanceled`. Перед этим `DelayPromiseWithCancellation` проставил себе флаг `TaskCreationOptions.RunContinuationsAsynchronously` - 
это понадобится в дальнейшем:
````csharp
thisRef.AtomicStateUpdate((int)TaskCreationOptions.RunContinuationsAsynchronously, 0); // Task.cs, CS: 5776
````

Вызывая `TrySetCanceled` в коллбеке `registrations`, мы попадаем в `Task.RunContinuations`, где рантайм обращает внимание на флаг `RunContinuationsAsynchronously`:
````csharp
bool canInlineContinuations = // Task.cs, CS: 3466
    (m_stateFlags & (int)TaskCreationOptions.RunContinuationsAsynchronously) == 0 &&
    RuntimeHelpers.TryEnsureSufficientExecutionStack();
````

Наш `continuationObject` - это `IAsyncStateMachineBox` метода `CancellableTaskDelay`, поэтому мы попадаем в `RunOrScheduleAction`:
````csharp
case IAsyncStateMachineBox stateMachineBox: // Task.cs, CS: 3476
    AwaitTaskContinuation.RunOrScheduleAction(stateMachineBox, canInlineContinuations);
    LogFinishCompletionNotification();
    return;
````

И т.к. `DelayPromiseWithCancellation` запретил выполнение в том же потоке, в котором мы вызвали `Cancel()` у `CTS`, выполнение `MoveNext()` будет запланировано в `ThreadPool`:
````csharp
...
if (!allowInlining || !IsValidLocationForInlining) // TaskContinuation.cs, CS: 769
{
    if (TplEventSource.Log.IsEnabled())
    {
        UnsafeScheduleAction(box.MoveNextAction, prevCurrentTask);
    }
    else
    {
        ThreadPool.UnsafeQueueUserWorkItemInternal(box, preferLocal: true);
    }
    return;
}
...
````

Дальше `ThreadPool` вызывает `MoveNext()`, стейт машина дёргает `GetResult()` у `TaskAwaiter`, и там выбрасывается `TaskCanceledException`:
````csharp
private static void ThrowForNonSuccess(Task task) // TaskAwaiter.cs, CS: 127
{
    Debug.Assert(task.IsCompleted, "Task must have been completed by now.");
    Debug.Assert(task.Status != TaskStatus.RanToCompletion, "Task should not be completed successfully.");

    switch (task.Status)
    {
        case TaskStatus.Canceled:
            ExceptionDispatchInfo? oceEdi = task.GetCancellationExceptionDispatchInfo();
            if (oceEdi != null)
            {
                oceEdi.Throw();
                Debug.Fail("Throw() should have thrown");
            }
            throw new TaskCanceledException(task);
        ...
    }
}
````