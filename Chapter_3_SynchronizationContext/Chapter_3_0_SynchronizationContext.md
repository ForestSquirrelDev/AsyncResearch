Возьмём следующий пример:
````csharp
public static void Run()
{
    var context = new SimpleManualContext();
    SynchronizationContext.SetSynchronizationContext(context);

    Console.WriteLine($"[Main] Starting on thread: {Environment.CurrentManagedThreadId}");
    
    _ = DoWork();
    
    for (var i = 0; i < 5; i++)
    {
        Console.WriteLine($"[Engine] Tick {i}...");
        context.ExecuteTasks();
        Thread.Sleep(100);
    }
}

private static async Task DoWork()
{
    await Task.Run(async () => {
        Console.WriteLine($"[Background] Working on thread: {Environment.CurrentManagedThreadId}");
        await Task.Delay(20);
    });
    
    Console.WriteLine($"[Main] Finished on thread: {Environment.CurrentManagedThreadId}");
}
````

Когда `Task` уходит в ожидание через `AsyncStateMachineBox`, можно увидеть, что внутри происходит следующее:
````csharp
// If the caller wants to continue on the current context/scheduler and there is one,
// fall back to using the state machine's delegate.
if (continueOnCapturedContext) // Task.cs, CS: 2575
{
    if (SynchronizationContext.Current is SynchronizationContext syncCtx && syncCtx.GetType() != typeof(SynchronizationContext))
    {
        tc = new SynchronizationContextAwaitTaskContinuation(syncCtx, stateMachineBox.MoveNextAction, flowExecutionContext: false);
        goto HaveTaskContinuation;
    }

    if (TaskScheduler.InternalCurrent is TaskScheduler scheduler && scheduler != TaskScheduler.Default)
    {
        tc = new TaskSchedulerAwaitTaskContinuation(scheduler, stateMachineBox.MoveNextAction, flowExecutionContext: false);
        goto HaveTaskContinuation;
    }
}
````

Cами разработчики .NET в комментарии здесь и написали: "если вызывающий хочет продолжить в текущем контексте/TaskScheduler, и они не default".

Мы не модифицировали `TaskScheduler`, но `SynchronizationContext` у нас как раз будет кастомный, поэтому мы попадём в данную ветвь и создадим `SynchronizationContextAwaitTaskContinuation`:
````csharp
if (SynchronizationContext.Current is SynchronizationContext syncCtx && syncCtx.GetType() != typeof(SynchronizationContext)) // Task.cs, CS: 2577
{
    tc = new SynchronizationContextAwaitTaskContinuation(syncCtx, stateMachineBox.MoveNextAction, flowExecutionContext: false);
    goto HaveTaskContinuation;
}
````

То есть по Continuation'ам будет гулять уже не `IAsyncStateMachineBox`, а `TaskContinuation`. В результате, когда у таска вызовется `TrySetResult`, мы попадём не напрямую в `IAsyncStateMachineBox.MoveNext()`,
а в `TaskContinuation`:

````csharp
switch (continuationObject) // Task.cs, CS: 3470
{
    case IAsyncStateMachineBox stateMachineBox:
        AwaitTaskContinuation.RunOrScheduleAction(stateMachineBox, canInlineContinuations);
        LogFinishCompletionNotification();
        return;

    case Action action:
        AwaitTaskContinuation.RunOrScheduleAction(action, canInlineContinuations);
        LogFinishCompletionNotification();
        return;
        
    // Упадём вот сюда
    case TaskContinuation tc:
        tc.Run(this, canInlineContinuations);
        LogFinishCompletionNotification();
        return;

    case ITaskCompletionAction completionAction:
        RunOrQueueCompletionAction(completionAction, canInlineContinuations);
        LogFinishCompletionNotification();
        return;
}
````

Полный стактрейс будет выглядеть следующим образом:
````
SynchronizationContextAwaitTaskContinuation.Run()
Task.RunContinuations() [3]
Task<VoidTaskResult>.TrySetResult() [2]
UnwrapPromise<VoidTaskResult>.TrySetFromTask()
UnwrapPromise<VoidTaskResult>.Invoke()
Task.RunContinuations() [2]
Task<VoidTaskResult>.TrySetResult() [1]
AsyncTaskMethodBuilder<VoidTaskResult>.SetExistingTaskResult()
AsyncTaskMethodBuilder.SetResult()
async SynchronizationContextExample.<>c.<DoWork>b__1_0()
AsyncTaskMethodBuilder<VoidTaskResult>.AsyncStateMachineBox<SynchronizationContextExample.<>c.<<DoWork>b__1_0>d>.ExecutionContextCallback()
ExecutionContext.RunInternal()
AsyncTaskMethodBuilder<VoidTaskResult>.AsyncStateMachineBox<SynchronizationContextExample.<>c.<<DoWork>b__1_0>d>.MoveNext()
AsyncTaskMethodBuilder<VoidTaskResult>.AsyncStateMachineBox<SynchronizationContextExample.<>c.<<DoWork>b__1_0>d>.MoveNext()
AwaitTaskContinuation.RunOrScheduleAction()
Task.RunContinuations() [1]
Task.TrySetResult()
Task.DelayPromise.CompleteTimedOut()
TimerQueueTimer.Fire()
TimerQueue.FireNextTimers()
ThreadPoolWorkQueue.Dispatch()
PortableThreadPool.WorkerThread.WorkerThreadStart()
[Native to Managed Transition]
````

По стак трейсу мы можем наблюдать, что в `TrySetResult` мы попали в `RunContinuations`, а тот, видя, что `m_continuationObject` у таска - это `SynchronizationContextAwaitTaskContinuation`,
вызывает `internal sealed override void Run(Task task, bool canInlineContinuationTask)` класса `SynchronizationContextAwaitTaskContinuation`:
````csharp
internal sealed override void Run(Task task, bool canInlineContinuationTask) // TaskContinuation.cs, CS: 392
{
    if (canInlineContinuationTask &&
        m_syncContext == SynchronizationContext.Current)
    {
        RunCallback(GetInvokeActionCallback(), m_action, ref Task.t_currentTask);
    }
    else
    {
        TplEventSource log = TplEventSource.Log;
        if (log.IsEnabled())
        {
            m_continuationId = Task.NewId();
            log.AwaitTaskContinuationScheduled((task.ExecutingTaskScheduler ?? TaskScheduler.Default).Id, task.Id, m_continuationId);
        }
        RunCallback(GetPostActionCallback(), this, ref Task.t_currentTask);
    }
}
````

Далее - вызывается `RunCallback()`, куда передаётся:
1. `s_postActionCallback`. Данный делегат - это по сути инструкция к `SynchronizationContext`: `Post`. Метод говорит контексту синхронизации: что положить (`m_action`), и как это вызвать (`s_postCallback` - инструкция по вызову `Action`).
2. `this`, т.е. себя - `SynchronizationContextAwaitTaskContinuation`.
3. Текущий `Task` потока.

Внутри метода, под `try-catch`, вызывается `ContextCallback`, т.е. метод `PostAction`:
````csharp
private static void PostAction(object? state) // TaskContinuation.cs, CS: 416
{
    Debug.Assert(state is SynchronizationContextAwaitTaskContinuation);
    var c = (SynchronizationContextAwaitTaskContinuation)state;

    TplEventSource log = TplEventSource.Log;
    if (log.IsEnabled() && log.TasksSetActivityIds && c.m_continuationId != 0)
    {
        c.m_syncContext.Post(s_postCallback, GetActionLogDelegate(c.m_continuationId, c.m_action));
    }
    else
    {
        c.m_syncContext.Post(s_postCallback, c.m_action);
    }
}
````

И `Post` попадёт в наш `SimpleManualContext`:
````csharp
public override void Post(SendOrPostCallback d, object? state) // SimpleManualContext.cs, CS: 10
{
    lock (_queue)
    {
        _queue.Add((d, state));
    }
}
````

Поскольку мы сделали наш контекст "однопоточным", мы выполним Post через `lock`: сколько угодно тасок могут класть результат в `Post` из разных потоков.

В дальнейшем мы вызовем `ExecuteTasks` в управляющем потоке, и все таски, положившие свой результат (`MoveNext`) в очередь `_currentTickCallbacks`, выполнятся синхронно:
````csharp
public void ExecuteTasks() // SimpleManualContext.cs, CS: 18
{
    if (Environment.CurrentManagedThreadId != _mainThreadId)
    {
        throw new InvalidOperationException("ExecuteTasks can only be called from the main thread.");
    }

    lock (_queue)
    {
        _currentTickCallbacks.AddRange(_queue);
        _queue.Clear();
    }

    var callbacksCopy = _currentTickCallbacks.ToList();
    _currentTickCallbacks.Clear();
    
    foreach (var work in callbacksCopy)
    {
        work.callback(work.state);
    }
}
````

Таким образом, если бы мы были UI-приложением, мы могли бы безопасно завершать асинхронные стейт машины не из тех потоков, где они выполнились, а в главном потоке.

При этом преимущества многопоточного кода сохраняются: как видно в примере с потоком таймера, таска может завершаться в каком угодно потоке - она просто 
вернёт результат в управляющий.

Bottleneck в `SimpleManualContext` будет только в том случае, если асинхронные стейт машины будут выполнять логику после `await`, т.е. в главном потоке.