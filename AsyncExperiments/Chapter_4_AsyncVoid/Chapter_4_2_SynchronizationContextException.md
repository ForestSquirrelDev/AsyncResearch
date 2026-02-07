Что будет, если `async void` выбросит исключение, но не в `ThreadPool`, а в `SynchronizationContext`?
````csharp
    SynchronizationContext? context = _synchronizationContext; // AsyncVoidMethodBuilder.cs, CS: 123
    if (context != null)
    {
        try
        {
            Task.ThrowAsync(exception, targetContext: context);
        }
        finally
        {
            NotifySynchronizationContextOfCompletion(context);
        }
    }
````

Тогда процесс не упадёт, как в сценарии с исключением в `ThreadPool`. Вместо этого, рантайм положит выброс исключения в контекст:
````csharp
var edi = ExceptionDispatchInfo.Capture(exception); // Task.cs, CS: 1906

if (targetContext != null)
{
    try
    {
        targetContext.Post(static state => ((ExceptionDispatchInfo)state!).Throw(), edi);
        return;
    }
    catch (Exception postException)
    {
        edi = ExceptionDispatchInfo.Capture(new AggregateException(exception, postException));
    }
}
````

Что будет дальше - зависит от того, как мы работаем с контекстом синхронизации. Если написать вот так:
````csharp
public static void Test()
{
    var context = new SimpleManualContext();
    SynchronizationContext.SetSynchronizationContext(context);

    DoLegitWork();
    ThrowException();
    
    for (var i = 0; i < 5; i++)
    {
        Console.WriteLine($"[Engine] Tick {i}...");
        context.ExecuteTasks();
        Thread.Sleep(100);
    }
}

private static async void DoLegitWork()
{
    await Task.Delay(100);
    Console.WriteLine("Doing legit work...");
}

private static async void ThrowException()
{
    await Task.Delay(100);
    Console.WriteLine("Throwing exception...");
    throw new Exception("HORY SHIET!");
}
````
то вывод программы будет следующим:
````csharp
[Engine] Tick 0...
[Engine] Tick 1...
[Engine] Tick 2...
Doing legit work...
Throwing exception...
[Engine] Tick 3...
Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_4_AsyncVoid.SynchronizationContextException.ThrowException()
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_0(Object state)
   at AsyncResearch.AsyncExperiments.Chapter_3_SynchronizationContext.SimpleManualContext.ExecuteTasks()
   at AsyncResearch.AsyncExperiments.Chapter_4_AsyncVoid.SynchronizationContextException.Test()
   at AsyncResearch.Program.Main(String[] args)

Process finished with exit code -532,462,766.
````

Поскольку мы разбирали контекст синхронизации блокирующим вызовом в основном потоке, исключение в очереди коллбеков привело к падению всего процесса.
Но если написать вот так:
````csharp
for (var i = 0; i < 5; i++)
{
    Console.WriteLine($"[Engine] Tick {i}...");
    try
    {
        context.ExecuteTasks();
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
    Thread.Sleep(100);
}
````

То исключение не убьёт нам процесс, а прервёт работу с очередью коллбеков внутри `SimpleManualContext`, и процесс завершится с кодом `0`.