Разработчики .NET добавили возможность настраивать поведение `TaskAwaiter` через обращение к [ConfigureAwait](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.configureawait?view=net-10.0).

Возьмём следующий пример:
````
public static async Task Test()
{
    Console.WriteLine("Test: Start");
    try
    {
        await DoWorkAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
    Console.WriteLine("Test: End");
}

private static async Task DoWorkAsync()
{
    var localVariable = 42;
    await Task.Delay(1000);
    Console.WriteLine("Resumed with " + localVariable);
    throw new Exception("DoWorkAsync: HORY SHIET!");
}
````

Внутри стейт машины `DoWorkAsync()` мы выбрасываем исключение, а в стейт машине `Test()` - отлавливаем его. Но в результате выполнения программы, исключение выведено в консоль не будет:
````
Test: Start
Resumed with 42
Test: End

Process finished with exit code 0.
````

Когда мы вызвали `.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)`, `Task` вернул для нас объект `ConfiguredTaskAwaitable`:
````csharp
public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) // Task.cs, CS: 2453
{
    return new ConfiguredTaskAwaitable(this, continueOnCapturedContext ? ConfigureAwaitOptions.ContinueOnCapturedContext : ConfigureAwaitOptions.None);
}
````

Когда стейт машина `Test()` вызвала у `ConfiguredTaskAwaitable` метод `GetResult()`, где должно было выброситься сохранённое в `Task` исключение, там произошла проверка:
````csharp
[StackTraceHidden]
public void GetResult() // TaskAwaiter.cs, CS: 430
{
    TaskAwaiter.ValidateEnd(m_task, m_options);
}
...
if (!task.IsCompletedSuccessfully) // TaskAwaiter.cs, CS: 114
{
    if ((options & ConfigureAwaitOptions.SuppressThrowing) == 0)
    {
        ThrowForNonSuccess(task);
    }

    task.MarkExceptionsAsHandled();
}
````
Метод `HandleNonSuccessAndDebuggerNotification` структуры `TaskAwaiter` увидел, что в options указано `SuppressThrowing`, и просто пометил исключение как обработанное.

Если бы мы убрали `ConfigureAwait`, результат выполнения программы был бы следующим:
````
Test: Start
Resumed with 42
System.Exception: DoWorkAsync: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_X_Additionals.ConfigureAwaitExample.DoWorkAsync() in D:\work\AsyncResearch\AsyncExperiments\Chapter_X_Additionals\ConfigureAwaitExample.cs:line 24
   at AsyncResearch.AsyncExperiments.Chapter_X_Additionals.ConfigureAwaitExample.Test() in D:\work\AsyncResearch\AsyncExperiments\Chapter_X_Additionals\ConfigureAwaitExample.cs:line 10
Test: End

Process finished with exit code 0.
````

Аналогичным образом, ConfigureAwait позволяет настраивать ещё две вещи:
- ConfigureAwaitOptions.ContinueOnCapturedContext;
- ConfigureAwaitOptions.ForceYielding.

#### ConfigureAwaitOptions.ContinueOnCapturedContext

Если вызвать `.ConfigureAwait(false)`, или `.ConfigureAwait(ConfigureAwaitOptions...)` без указания опции `ContinueOnCapturedContext`, контекст синхронизации не будет захвачен
при создании задачи, а её завершение (`TrySetResult`) будет передано в `ThreadPool`, а не контексту синхронизации:
````csharp
public void UnsafeOnCompleted(Action continuation) // TaskAwaiter.cs, CS: 420
{
    TaskAwaiter.OnCompletedInternal(m_task, continuation, (m_options & ConfigureAwaitOptions.ContinueOnCapturedContext) != 0, flowExecutionContext: false);
}
````

Если опции `ContinueOnCapturedContext` не будет, в `OnCompletedInternal` мы передадим `continueOnCapturedContext: false`.

В результате мы не попадём в эту ветвь в Task.cs:
````csharp
if (continueOnCapturedContext) // Task.cs, CS: 2496
{
    if (SynchronizationContext.Current is SynchronizationContext syncCtx && syncCtx.GetType() != typeof(SynchronizationContext))
    {
        tc = new SynchronizationContextAwaitTaskContinuation(syncCtx, continuationAction, flowExecutionContext);
        goto HaveTaskContinuation;
    }
    if (TaskScheduler.InternalCurrent is TaskScheduler scheduler && scheduler != TaskScheduler.Default)
    {
        tc = new TaskSchedulerAwaitTaskContinuation(scheduler, continuationAction, flowExecutionContext);
        goto HaveTaskContinuation;
    }
}
````

И в методе `Task.RunContinuations` мы выполнимся как `AwaitTaskContinuation.RunOrScheduleAction`, попадя в ThreadPool:
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
    
    // Если бы не continueOnCapturedContext: false, мы бы попали вот сюда
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

#### ConfigureAwaitOptions.ForceYielding

Вызов `.ConfigureAwait(ConfigureAwaitOptions.ForceYielding)` приведёт к тому, что `ConfiguredTaskAwaiter` в свойстве `IsCompleted` всегда будет возвращать false:
````csharp
public bool IsCompleted => ((m_options & ConfigureAwaitOptions.ForceYielding) == 0) && m_task.IsCompleted; // TaskAwaiter.cs, CS: 403
````

Это работает, т.к. `TaskAwaiter` является контрактом для стейт машины:
````csharp
awaiter = ConfigureAwaitExample.DoWorkAsync().GetAwaiter();
// Вот эта проверка всегда вернёт false
if (!awaiter.IsCompleted)
{
  this.<>1__state = num2 = 0;
  this.<>u__1 = awaiter;
  this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter, ConfigureAwaitExample.<Test>d__0>(ref awaiter, ref this);
  return;
}
````

Стейт машина, видя, что awaiter не завершён, заставит рантайм .NET создать `AsyncStateMachineBox` и не выполнится синхронно.

Здесь возникает два забавных момента. 

Во-первых, если вызвать ForceYielding на уже завершённом таске, возникает вопрос: стейт машина упаковалась в управляемую кучу, но Task
уже завершён, и TrySetResult() никто не вызовет. Что тогда будет со стейт машиной? Она что, навсегда останется лежать в куче и создаст нам утечку памяти?

Ответ - не останется. Видя, что Task уже завершён, рантайм сразу поставит коллбэк (MoveNext стейт машины) в очередь на выполнение:
````
// Здесь будет false, если не сам авейтер, а его Task уже завершён
if (!AddTaskContinuation(continuationAction, addBeforeOthers: false)) // Task.cs, CS: 2534
{
    AwaitTaskContinuation.UnsafeScheduleAction(continuationAction, this);
}
````

Второй забавный момент - ручное обращение к состоянию `IsCompleted` у Awaiter. Если написать вот так, то мы попадём в бесконечный цикл:
````csharp
public static async Task Test()
{
    var task = Task.CompletedTask;
    var awaiter = task.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
    while (!awaiter.GetAwaiter().IsCompleted)
    {
        Console.WriteLine("Oh no! Infinite loop!");
        await Task.Delay(100);
    }
}
````

Так произошло, потому что `ConfiguredTaskAwaiter` с переданной опцией `ForceYielding` всегда вернёт `false`. Истинное состояние завершённости в данном случае хранится внутри `task`.