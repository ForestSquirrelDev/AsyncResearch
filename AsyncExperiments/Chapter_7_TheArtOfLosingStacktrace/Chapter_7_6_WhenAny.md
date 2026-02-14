Ещё более забавный способ потерять исключение, чем `Task.WhenAll(...)` - это `Task.WhenAny(...)`. Возьмём следующий пример:
````csharp
public static async Task WhenAnyExceptionsExample()
{
    var t1 = Exception1();
    var t2 = Exception2();
    var t3 = Exception3();
    
    var any = Task.WhenAny(t1, t2, t3);
    try
    {
        await any;
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
}

private static async Task Exception1()
{
    throw new Exception("Exception1");
}

private static async Task Exception2()
{
    throw new Exception("Exception2");
}

private static async Task Exception3()
{
    throw new Exception("Exception3");
}
````

После примера с `Task.WhenAll()`, можно ожидать, что в консоль выведется первое исключение. Однако в консоль не выведется вообще ничего:
````
Process finished with exit code 0.
````

Так происходит из-за того, как написан `Task.WhenAny()`. Внутри там создаётся `CompleteOnInvokePromise`:
````csharp
internal static Task<TTask> CommonCWAnyLogic<TTask>(IList<TTask> tasks, bool isSyncBlocking = false) where TTask : Task
{
    Debug.Assert(tasks != null);

    var promise = new CompleteOnInvokePromise<TTask>(tasks, isSyncBlocking);
    
    bool checkArgsOnly = false;
    int numTasks = tasks.Count;
    for (int i = 0; i < numTasks; i++)
    {
        Task task = tasks[i];
        if (task == null) throw new ArgumentException(SR.Task_MultiTaskContinuation_NullTask, nameof(tasks));

        if (checkArgsOnly) continue;

        if (promise.IsCompleted)
        {
            checkArgsOnly = true;
        }
        else if (task.IsCompleted)
        {
            promise.Invoke(task);
            checkArgsOnly = true;
        }
        else
        {
            task.AddCompletionAction(promise, addBeforeOthers: isSyncBlocking);
            if (promise.IsCompleted)
            {
                task.RemoveContinuation(promise);
            }
        }
    }

    return promise;
}
````

Этот `CompleteOnInvokePromise` затем добавляется в `onCompletionAction` каждой из тасок, переданной в `Task.WhenAny()`. То есть когда любая из тасок завершится, она вызовет `promise.Invoke()`.
И когда это произойдёт, `CompleteOnInvokePromise` сделает следующее:
````csharp
public void Invoke(Task completingTask)
{
    int flags = _stateFlags;
    int isSyncBlockingFlag = flags & SyncBlockingFlag;
    int isCompleted = flags & CompletedFlag;

    if (isCompleted == 0 &&
        Interlocked.Exchange(ref _stateFlags, isSyncBlockingFlag | CompletedFlag) == isSyncBlockingFlag)
    {
        if (TplEventSource.Log.IsEnabled())
        {
            TplEventSource.Log.TraceOperationRelation(this.Id, CausalityRelation.Choice);
            TplEventSource.Log.TraceOperationEnd(this.Id, AsyncCausalityStatus.Completed);
        }

        if (s_asyncDebuggingEnabled)
            RemoveFromActiveTasks(this);

        // Просто сделали SetResult: не важно, были там исключения внутри или нет
        bool success = TrySetResult((TTask)completingTask);
        Debug.Assert(success, "Only one task should have gotten to this point, and thus this must be successful.");
        
        IList<TTask>? tasks = _tasks;
        Debug.Assert(tasks != null, "Should not have been nulled out yet.");
        int numTasks = tasks.Count;
        for (int i = 0; i < numTasks; i++)
        {
            TTask task = tasks[i];
            if (task != null &&
                !task.IsCompleted) task.RemoveContinuation(this);
        }
        _tasks = null;
    }
}
````
Мы просто завершили `Task`, который создали в `WhenAny()`. А исключения остались лежать внутри тех тасок, которые завершились с ошибкой. Если подписаться на `UnobservedTaskException`,
при сборке мусора все эти исключения окажутся в этом событии:
````
System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (Exception1)
 ---> System.Exception: Exception1
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAnyExample.Exception1()
   --- End of inner exception stack trace ---
Unobserved exception System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (Exception2)
 ---> System.Exception: Exception2
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAnyExample.Exception2()
   --- End of inner exception stack trace ---
Unobserved exception System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (Exception3)
 ---> System.Exception: Exception3
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAnyExample.Exception3()
   --- End of inner exception stack trace ---

Process finished with exit code 0.

````

Может показаться, что можно избежать сокрытия, ожидая не `Task`, созданный внутри `WhenAny()`, а его результат:
````csharp
...
var any = Task.WhenAny(t1, t2, t3);
try
{
    await any.Result;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}
...
````
Но если посмотреть под рантайм, окажется что `Result` - это блокирующий вызов, который синхронно выполняет задачу в том же потоке, если это возможно, либо блокирует поток до тех пор
пока задача не выполнена:
````csharp
[DebuggerBrowsable(DebuggerBrowsableState.Never)] // Future.cs, CS: 438
public TResult Result =>
    IsWaitNotificationEnabledOrNotRanToCompletion ?
        GetResultCore(waitCompletionNotification: true) :
        m_result!;
...
internal TResult GetResultCore(bool waitCompletionNotification) // Future.cs, CS: 462
{
    if (!IsCompleted) InternalWait(Timeout.Infinite, default);

    if (waitCompletionNotification) NotifyDebuggerOfWaitCompletionIfNecessary();

    if (!IsCompletedSuccessfully) ThrowIfExceptional(includeTaskCanceledExceptions: true);

    Debug.Assert(IsCompletedSuccessfully, "Task<T>.Result getter: Expected result to have been set.");

    return m_result!;
}
````

Поэтому чтобы поймать исключение, нужно сначала дождаться завершения задачи, а потом забрать её результат:
````csharp
...
var any = Task.WhenAny(t1, t2, t3);
try
{
    await any;
    any.Result.GetAwaiter().GetResult();
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}
...
````

Так мы наверняка будем знать что Result уже есть, и мы не попадём в блокировку вызывающего потока. А исключение упадёт в GetResult(), и мы его поймаем:
````csharp
System.Exception: Exception1
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAnyExample.Exception1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskWhenAnyExample.WhenAnyExceptionsExample()

Process finished with exit code 0.
````

В итоге результат получается как в примере с `Task.WhenAll()` - выбрасывается первое исключение. Вместо `GetAwaiter().GetResult()` также можно написать `await await` - `ведь WhenAny()`
возвращает нам `Task<Task>`:
````csharp
var any = Task.WhenAny(t1, t2, t3);
try
{
    await await any;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}
````

В сгенерированной компилятором стейт машине, это развернётся в то же самое: когда авейтер `any` завершится, компилятор заберёт у него `awaiter3 = awaiter2.GetResult().GetAwaiter();`,
и дождётся его завершения. Затем вызовет `awaiter3.GetResult();`.

При этом важный нюанс в том, что поскольку мы как и в примере с `Task.WhenAll()`, обработали только первое исключение - остальные два всё равно упадут в `UnobservedTaskException`.