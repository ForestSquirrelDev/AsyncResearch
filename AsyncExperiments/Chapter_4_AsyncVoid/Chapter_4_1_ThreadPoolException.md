Разработчики .NET в комментариях к коду (`AsyncVoidMethodBuilder.cs, CS: 139`) пишут, что выброс исключения в Thread Pool приведёт к паденю всего процеса. Почему?

Возьмём следующий пример:
````csharp
public class Program
{
    public static void Main(string[] args)
    {
        ThreadPoolException.Test();
        while (true)
        {
            Console.WriteLine("Hello World!");
            Thread.Sleep(100);
        }
    }
}
public class ThreadPoolException
{
    public static void Test()
    {
        Console.WriteLine("Test: Start");
        DoWorkAsync();
        Console.WriteLine("Test: End");
    }
    
    private static async void DoWorkAsync()
    {
        var localVariable = 42;
        await Task.Delay(1000);
        Console.WriteLine("Resumed with " + localVariable);
        throw new Exception("DoWorkAsync: HORY SHIET!");
    }
}
````

В упрощённом виде, мы прикинулись программой, которая крутит какой-то цикл в управляющем потоке.

В результате работы программы мы получим следующий вывод:
````csharp
Test: Start
Test: End
Hello World!
Hello World!
Hello World!
Hello World!
Hello World!
Hello World!
Hello World!
Hello World!
Hello World!
Hello World!
Resumed with 42
Unhandled exception. System.Exception: DoWorkAsync: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_4_AsyncVoid.ThreadPoolException.DoWorkAsync()
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_1(Object state)
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
Hello World!
Hello World!

Process finished with exit code -532,462,766.

````

Несмотря на то, что непосредственно внутри нашего main loop мы не выбрасывали исключений, процесс всё равно завершился с кодом ошибки 0xE0434352.
И нам даже говорят, откуда прилетело исключение: `at System.Threading.ThreadPoolWorkQueue.Dispatch()`.

Если посмотреть в `AsyncVoidMethodBuilder`, у которого стейт машина вызвала `SetException()`, там будет следующая цепочка вызовов:
````csharp
...
else // AsyncVoidMethodBuilder.cs, CS: 137
{
    Task.ThrowAsync(exception, targetContext: null);
}
...

internal static void ThrowAsync(Exception exception, SynchronizationContext? targetContext) // Task.cs, CS: 1902
{
    var edi = ExceptionDispatchInfo.Capture(exception);

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

#if NATIVEAOT
    RuntimeExceptionHelpers.ReportUnhandledException(edi.SourceException);
#else
    ThreadPool.QueueUserWorkItem(static state => ((ExceptionDispatchInfo)state!).Throw(), edi);

#endif
}    
...
public static bool QueueUserWorkItem(WaitCallback callBack, object? state) // ThreadPoolWorkQueue.cs, CS: 1612
{
    if (callBack == null)
    {
        ThrowHelper.ThrowArgumentNullException(ExceptionArgument.callBack);
    }

    ExecutionContext? context = ExecutionContext.Capture();

    object tpcallBack = (context == null || context.IsDefault) ?
        new QueueUserWorkItemCallbackDefaultContext(callBack!, state) :
        (object)new QueueUserWorkItemCallback(callBack!, state, context);

    s_workQueue.Enqueue(tpcallBack, forceGlobal: true);

    return true;
}
````

То есть, не найдя контекста синхронизации, `Task` запланировал исключение прямо в Thread Pool. И в конечном счёте мы попали в метод `DispatchWorkItem`:
````csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static void DispatchWorkItem(object workItem, Thread currentThread) // ThreadPoolWorkQueue.cs, CS: 1100
{
    if (workItem is Task task)
    {
        task.ExecuteFromThreadPool(currentThread);
    }
    else
    {
        Debug.Assert(workItem is IThreadPoolWorkItem);
        Unsafe.As<IThreadPoolWorkItem>(workItem).Execute();
    }
}
````

`workItem` в данном случае - это метод ThrowAsync. Он выбросит исключение и поток, не имея try-catch внутри, остановит свою работу.
Видя это, Common Language Runtime вызовет событие `AppDomain.CurrentDomain.UnhandledException` и начнёт остановку процесса.

В этом можно убедиться, если подписаться на данное событие:
````csharp
AppDomain.CurrentDomain.UnhandledException += (sender, e) => 
{
    Console.WriteLine($"\nCLR Caught unhandled exception!");
    Console.WriteLine($"Is process terminating? {e.IsTerminating}");
    Console.WriteLine($"Error: '{((Exception)e.ExceptionObject).Message}'");
};
````

Как [пишут сами Microsoft](https://learn.microsoft.com/en-us/dotnet/api/system.unhandledexceptioneventargs?view=net-9.0), свойство `IsTerminating` указывает на то,
завершает ли CLR работу процесса. И в нашем случае оно будет `true`:
````csharp
CLR Caught unhandled exception!
Is process terminating? True
Error: 'DoWorkAsync: HORY SHIET!'
````

Таким образом, необработанное исключение в `async void` стейт машине убивает процесс. 

Однако зачем? Умер ведь один поток из пула, а не все сразу - "ну умер и умер, бог бы с ним"?
Если чуть копнуть в историю, окажется, что раньше так и было: CLR тихо убивал поток, в котором выбросилось исключение.

Но такое поведение приводило к непредсказуемым последствиям: в [статье](https://learn.microsoft.com/en-us/archive/msdn-magazine/2005/july/unhandled-exceptions-and-tracing-in-the-net-framework-2-0)
разработчик .NET Джон Роббинс рассказывает, как было "до" и "после". До .NET Framework 2.0, потоки могли тихо умирать один за другим, и это невозможно было заметить: 
процесс деградировал, а сообщений об ошибках не было. 

Джон Роббинс пишет, что переход к политике "fail fast", когда CLR убивает всё приложение с UnhandledException - это "mandatory upgrade".