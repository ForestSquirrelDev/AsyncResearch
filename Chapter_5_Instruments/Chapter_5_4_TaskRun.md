Можно напрямую попросить рантайм .NET выполнить логику в `ThreadPool` через `Task.Run`:
````csharp
public static async Task RunTaskRunExample()
{
    await Task.Run(() =>
    {
        Console.WriteLine("Hello World!");
    });
}
````
Когда мы вызовем `Task.Run()`, произойдёт следующее. Рантайм создаст `Task` и прокинет туда наш делегат:
````csharp
public static Task Run(Action action) // Task.cs, CS: 5427
{
    return InternalStartNew(null, action, null, default, TaskScheduler.Default,
        TaskCreationOptions.DenyChildAttach, InternalTaskOptions.None);
}
...
internal static Task InternalStartNew( // Task.cs, CS: 1144
    Task? creatingTask, Delegate action, object? state, CancellationToken cancellationToken, TaskScheduler scheduler,
    TaskCreationOptions options, InternalTaskOptions internalOptions)
{
    if (scheduler == null)
    {
        ThrowHelper.ThrowArgumentNullException(ExceptionArgument.scheduler);
    }
    
    Task t = new Task(action, state, creatingTask, cancellationToken, options, internalOptions | InternalTaskOptions.QueuedByRuntime, scheduler);

    t.ScheduleAndStart(false);
    return t;
}
````
`Task` запишет делегат к себе в поле `m_action`. Затем вызовет:
````csharp
protected internal override void QueueTask(Task task)
{
    TaskCreationOptions options = task.Options;
    if (Thread.IsThreadStartSupported && (options & TaskCreationOptions.LongRunning) != 0)
    {
        new Thread(s_longRunningThreadWork)
        {
            IsBackground = true,
            Name = ".NET Long Running Task"
        }.UnsafeStart(task);
    }
    else
    {
        ThreadPool.UnsafeQueueUserWorkItemInternal(task, (options & TaskCreationOptions.PreferFairness) == 0);
    }
}
````

То есть по сути мы явно попросили рантайм выполнить эту таску в `ThreadPool`. Похожего эффекта можно было бы добиться в примере, написав:
````csharp
public static async Task RunTaskRunExample()
{
    await Task.Yield();
    Console.WriteLine("Hello World!");
}
````
Если у нас нет контекста синхронизации, после `await Task.Yield();` мы тоже окажемся в `ThreadPool`, и выполним работу там. Но если контекст синхронизации есть, после `Task.Yield()`
выполнение продолжится в основном потоке. А `Task.Run()` явно говорит рантайму выполнить всю логику внутри делегата в соседнем потоке.