Аналогичного эффекта с `Task.Run()`, можно добиться через `Task.Factory.StartNew()`:
````csharp
public static async Task RunFactoryStartNewExample()
{
    await Task.Factory.StartNew(() =>
    {
        Console.WriteLine("Hello World!");
    });
}
````
Там рантайм также вызывает `InternalStartNew`:
````csharp
public Task StartNew(Action action) // TaskFactory.cs, CS: 276
{
    Task? currTask = Task.InternalCurrent;
    return Task.InternalStartNew(currTask, action, null, m_defaultCancellationToken, GetDefaultScheduler(currTask),
        m_defaultCreationOptions, InternalTaskOptions.None);
}
````
Если сравнить с вызовом внутри `Task.Run()`, можно увидеть отличия:
````csharp
public static Task Run(Action action) // Task.cs, CS: 5427
{
    return InternalStartNew(null, action, null, default, TaskScheduler.Default,
        TaskCreationOptions.DenyChildAttach, InternalTaskOptions.None);
}
````
**Во-первых,** `Factory.StartNew()` захватывает `Task.InternalCurrent`, и передаёт его внутрь создаваемой таски. `InternalCurrent` - это статическое поле, в которое рантайм присваивает
текущую выполняющуюся таску:
````csharp
private void ExecuteWithThreadLocal(ref Task? currentTaskSlot, Thread? threadPoolThread = null) // Task.cs, CS: 2304
{
    ...
    try
    {
        // place the current task into TLS.
        currentTaskSlot = this;
        ...
    }
    ...
}
````
Если этот таск не равен `null`, и в `InternalStartNew` передали опцию `AttachedToParent` - таск присвоит себе его как родителя:
````csharp
internal Task(Delegate action, object? state, Task? parent, CancellationToken cancellationToken, // Task.cs, CS: 508
    TaskCreationOptions creationOptions, InternalTaskOptions internalOptions, TaskScheduler? scheduler)
{
    if (action == null)
    {
        ThrowHelper.ThrowArgumentNullException(ExceptionArgument.action);
    }

    // Keep a link to the parent if attached
    if (parent != null && (creationOptions & TaskCreationOptions.AttachedToParent) != 0)
    {
        EnsureContingentPropertiesInitializedUnsafe().m_parent = parent;
    }

    TaskConstructorCore(action, state, cancellationToken, creationOptions, internalOptions, scheduler);

    Debug.Assert(m_contingentProperties == null || m_contingentProperties.m_capturedContext == null,
        "Captured an ExecutionContext when one was already captured.");
    CapturedContext = ExecutionContext.Capture();
}
````

Это приведёт к вызову у "родительской" таски метода `AddNewChild()`:
````csharp
internal void AddNewChild() // Task.cs, CS: 875
{
    Debug.Assert(InternalCurrent == this, "Task.AddNewChild(): Called from an external context");

    ContingentProperties props = EnsureContingentPropertiesInitialized();

    if (props.m_completionCountdown == 1)
    {
        // A count of 1 indicates so far there was only the parent, and this is the first child task
        // Single kid => no fuss about who else is accessing the count. Let's save ourselves 100 cycles
        props.m_completionCountdown++;
    }
    else
    {
        // otherwise do it safely
        Interlocked.Increment(ref props.m_completionCountdown);
    }
}
````
До тех пор, пока "дочерние" таски не будут завершены, "родительская" таска тоже будет считаться незавершённой:
````csharp
...
if ((props.m_completionCountdown == 1) || // Task.cs, CS: 2007
    Interlocked.Decrement(ref props.m_completionCountdown) == 0)
{
    FinishStageTwo();
}
...
````
**Во-вторых,** `Task.Run()` всегда передаёт `TaskCreationOptions.DenyChildAttach` в `InternalStartNew`. Этот флаг запретит другим таскам стать дочерними для этой таски:
````csharp
ContingentProperties? props = m_contingentProperties;
if (props != null)
{
    Task? parent = props.m_parent;
    if (parent != null
        && ((creationOptions & TaskCreationOptions.AttachedToParent) != 0)
        // Проверяем у "родителя" наличие флага DenyChildAttach. Если он есть - не привязываемся
        && ((parent.CreationOptions & TaskCreationOptions.DenyChildAttach) == 0))
    {
        parent.AddNewChild();
    }
}
````
**В-третьих,** `Task.Run()` всегда передаст в `InternalStartNew` - `TaskScheduler.Default`, а `Factory.StartNew()` имеет перегрузку, в которую можно передать другой планировщик:
````csharp
public Task StartNew(Action action, CancellationToken cancellationToken, TaskCreationOptions creationOptions, TaskScheduler scheduler) // TaskFactory.cs, CS: 368
{
    return Task.InternalStartNew(
        Task.InternalCurrentIfAttached(creationOptions), action, null, cancellationToken, scheduler, creationOptions,
        InternalTaskOptions.None);
}
````

Дефолтный планировщик - это `ThreadPoolTaskScheduler`. То есть если бы мы заменили планировщик на собственный, кастомный, или - на `SynchronizationContextTaskScheduler` 
через вызов `TaskScheduler.FromCurrentSynchronizationContext()`, `Factory.StartNew()` может взять его, если мы попросим. А `Task.Run()` обязуется выполниться в `ThreadPool`.

`Factory.StartNew()` может взять переопределённый планировщик даже если мы не вызвали перегрузку с явным указанием `TaskScheduler`:
````csharp
private TaskScheduler GetDefaultScheduler(Task? currTask) // Task.cs, CS: 276
{
    return
        m_defaultScheduler ??
        (currTask != null && (currTask.CreationOptions & TaskCreationOptions.HideScheduler) == 0 ? currTask.ExecutingTaskScheduler! :
         TaskScheduler.Default);
}
````
Factory.StartNew() возьмёт планировщик у текущей таски, если она есть, если не предоставлен `m_defaultScheduler`, и если мы явно не укажем игнорировать переопределённый планировщик.
У вызова статического `Task.Factory`, `m_defaultScheduler` всегда будет `null`.

`В-четвёртых,` `Factory.StartNew()`, в отличие от `Task.Run()`, имеет перегрузку с опциями:
````csharp
public Task StartNew(Action action, TaskCreationOptions creationOptions) // TaskFactory.cs, CS: 329
{
    Task? currTask = Task.InternalCurrent;
    return Task.InternalStartNew(currTask, action, null, m_defaultCancellationToken, GetDefaultScheduler(currTask), creationOptions,
        InternalTaskOptions.None);
}
````
Например, если мы считаем что наша таска долгоживущая, можно передать в опции `TaskCreationOptions.LongRunning`, и `ThreadPoolTaskScheduler` создаст под эту таску отдельный поток:
````csharp
protected internal override void QueueTask(Task task) // ThreadPoolTaskScheduler.cs, CS: 42
{
    TaskCreationOptions options = task.Options;
    if (Thread.IsThreadStartSupported && (options & TaskCreationOptions.LongRunning) != 0)
    {
        // Run LongRunning tasks on their own dedicated thread.
        new Thread(s_longRunningThreadWork)
        {
            IsBackground = true,
            Name = ".NET Long Running Task"
        }.UnsafeStart(task);
    }
    else
    {
        // Normal handling for non-LongRunning tasks.
        ThreadPool.UnsafeQueueUserWorkItemInternal(task, (options & TaskCreationOptions.PreferFairness) == 0);
    }
}
````

**Пятое,** и, возможно, ключевое отличие - это отсутствие Unwrap у `Factory.StartNew()`. Если написать вот так:
````csharp
var task = Task.Factory.StartNew(async () =>
{
    await Task.Delay(99999);
    Console.WriteLine("Hello World!");
});
````
возвращаемый тип будет `Task<Task>`. Созданный таким образом таск, будучи запланированным в `ThreadPool`, после вызова `ExecuteWithThreadLocal` в `Task.cs`, попадает `InnerInvoke`:
````csharp
internal override void InnerInvoke() // Future.cs, CS: 495
{
    Debug.Assert(m_action != null);
    if (m_action is Func<TResult> func)
    {
        m_result = func();
        return;
    }

    if (m_action is Func<object?, TResult> funcWithState)
    {
        m_result = funcWithState(m_stateObject);
        return;
    }
    Debug.Fail("Invalid m_action in Task<TResult>");
}
````
Там он присваивает результат вызова `func()` (т.е. запуск нашей лямбды с `Task.Delay(99999)`) к себе в результат, возвращается в `ExecuteWithThreadLocal`, и помечает себя как завершённый:
````csharp
private void ExecuteWithThreadLocal(ref Task? currentTaskSlot, Thread? threadPoolThread = null) // Task.cs, CS: 2304
{
    ...
    try
    {
        ...
        try
        {
            ExecutionContext? ec = CapturedContext;
            if (ec == null)
            {
                InnerInvoke();
            }
            else
            {
                ...
            }
        }
        catch (Exception exn)
        {
            ...
        }
        ...
        Finish(true);
    }
    finally
    {
        ...
    }
}
````
Получается, что если мы будем делать await внешнего таска, мы будем дожидаться только запуска внутреннего таска в ThreadPool. Чтобы этого избежать, можно написать
`await await Factory.StartNew(...)`.

`Task.Run()` же делает иначе: он сразу делает внутри себя Unwrap, возвращая `UnwrapPromise`:
````csharp
public static Task Run(Func<Task?> function, CancellationToken cancellationToken) // Task.cs, CS: 5510
{
    if (function == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.function);

    if (cancellationToken.IsCancellationRequested)
        return FromCanceled(cancellationToken);

    Task<Task?> task1 = Task<Task?>.Factory.StartNew(function, cancellationToken, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
    
    UnwrapPromise<VoidTaskResult> promise = new UnwrapPromise<VoidTaskResult>(task1, lookForOce: true);

    return promise;
}
````
`UnwrapPromise` выступает чем-то вроде проводника между родительской и дочерней таской у `Task<Task>`: когда родительская таска завершается, UnwrapPromise подписывается на завершение
дочерней таски:
````csharp
private void ProcessInnerTask(Task? task) // Task.cs, CS: 7171
{
    if (task == null)
    {
        TrySetCanceled(default);
        _state = STATE_DONE;
    }

    else if (task.IsCompleted)
    {
        TrySetFromTask(task, lookForOce: false);
        _state = STATE_DONE;
    }
        
    else
    {
        task.AddCompletionAction(this);
    }
}
````
Когда завершится дочерняя таска, завершится и `UnwrapPromise`. При этом `UnwrapPromise`, наследуясь от `Task<TResult>`, в методе `TrySetFromTask` сможет принять в себя результат 
либо исключения внутренней таски (_либо внешней, если внешняя упала раньше чем внутренняя запустилась_):
````csharp
private bool TrySetFromTask(Task task, bool lookForOce) // Task.cs, CS: 7124
{
    Debug.Assert(task != null && task.IsCompleted, "TrySetFromTask: Expected task to have completed.");

    if (TplEventSource.Log.IsEnabled())
        TplEventSource.Log.TraceOperationRelation(this.Id, CausalityRelation.Join);

    bool result = false;
    switch (task.Status)
    {
        case TaskStatus.Canceled:
            result = TrySetCanceled(task.CancellationToken, task.GetCancellationExceptionDispatchInfo());
            break;

        case TaskStatus.Faulted:
            List<ExceptionDispatchInfo> edis = task.GetExceptionDispatchInfos();
            ExceptionDispatchInfo oceEdi;
            if (lookForOce && edis.Count > 0 &&
                (oceEdi = edis[0]) != null &&
                oceEdi.SourceException is OperationCanceledException oce)
            {
                result = TrySetCanceled(oce.CancellationToken, oceEdi);
            }
            else
            {
                result = TrySetException(edis);
            }
            break;

        case TaskStatus.RanToCompletion:
            if (TplEventSource.Log.IsEnabled())
                TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);

            if (s_asyncDebuggingEnabled)
                RemoveFromActiveTasks(this);

            result = TrySetResult(task is Task<TResult> taskTResult ? taskTResult.Result : default);
            break;
    }
    return result;
}
````

Таким образом, `Task.Factory.StartNew()` - это версия `Task.Run()` с возможностью более гибкой настройки и отсутствием Unwrap по умолчанию.