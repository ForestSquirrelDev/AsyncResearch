В Chapter 2_0 мы узнали, что будет, если внутри Task выбросится исключение, и мы принимаем к себе это исключение по стеку
вызовов - через `TaskAwaiter.GetResult()`.

Но что если наш метод, возвращающий Task - это Fire And Forget? Что если мы не дожидаемся его исполнения?
Тогда `GetResult()`, и соответственно `ValidateEnd()`, не вызовутся.

Возьмём пример:
````
public static void Main(string[] args)
{
    UnobservedTaskExceptionExample.TestCaller();
    Console.WriteLine("Hello, World!");
}
        
public static void TestCaller()
{
    CreateTaskAndForget();
}

private static void CreateTaskAndForget()
{
    _ = Test();
}

private static async Task Test()
{
    Console.WriteLine("Start");
    await DoWorkAsync();
    throw new Exception("HORY SHET!");
}

private static async Task DoWorkAsync()
{
    var localVariable = 42;
    await Task.Delay(100);
    Console.WriteLine("Resumed with " + localVariable);
}
````
Здесь мы вызываем асинхронный метод `Test()` без ожидания его TaskAwaiter: `TestCaller()` никогда не узнает о том, что в `Test()` было выброшено исключение.

На этот случай у внутреннего объекта `Task` - `TaskExceptionHolder`, имеется Finalizer:
````
~TaskExceptionHolder() // TaskExceptionHolder.cs, CS: 54
{
    if (m_faultExceptions != null && !m_isHandled)
    {
        AggregateException exceptionToThrow = new AggregateException(
            SR.TaskExceptionHolder_UnhandledException,
            m_faultExceptions);
        UnobservedTaskExceptionEventArgs ueea = new UnobservedTaskExceptionEventArgs(exceptionToThrow);
        TaskScheduler.PublishUnobservedTaskException(m_task, ueea);
    }
}
````

Finalizer - это такой метод, который автоматически вызовется рантаймом .NET во время сборки данного объекта GC. 
Если исключение не было обработано или выброшено, оно останется лежать в `TaskExceptionHolder` до тех пор, пока Task не соберёт Garbage Collector. 
Тогда Finalizer отработает, и вызовет событие `TaskScheduler.UnobservedTaskException`.

И поскольку событие - это просто событие, оно не повлияет на ход исполения программы: процесс не упадёт. И мы не узнаем об этом событии, ведь мы на него не подисались.
Программа отдаст вот такой output:
````
Start
Hello, World!

Process finished with exit code 0.
````

Но если мы подпишемся на `TaskScheduler.UnobservedTaskException` - то уже сможем увидеть необработанное исключение:
````
public static void TestCaller()
{
    TaskScheduler.UnobservedTaskException += OnUnobservedException;
    
    CreateTaskAndForget();
    Thread.Sleep(500);
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Console.WriteLine("GC finished");
}

private static void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs e)
{
    Console.WriteLine($"Unobserved exception: {e.Exception}, sender {sender?.GetType()}");
}
````
Важный момент: событие происходит именно в момент сборки мусора.
Если процесс завершится до того как отработает GC, мы не узнаем о необработанном исключении даже с подпиской.

Output программы:
````
Start
Resumed with 42
Unobserved exception: System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (HORY SHET!)
 ---> System.Exception: HORY SHET!
   at AsyncResearch.AsyncExperiments.Chapter_2_AnatomyOfCrash.UnobservedTaskExceptionExample.Test() in D:\work\AsyncResearch\AsyncExperiments\Chapter_2_AnatomyOfCrash\UnobservedTaskExceptionExample.cs:line 31
   --- End of inner exception stack trace ---, sender System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1[System.Threading.Tasks.VoidTaskResult,AsyncResearch.AsyncExperiments.Chapter_2_AnatomyOfCrash.UnobservedTaskExceptionExample+<Test>d__3]
GC finished
Hello, World!

Process finished with exit code 0.
````

Ещё одна интересная особенность `UnobservedTaskException` заключается в следующем:
````
public static void TestCaller()
{
    TaskScheduler.UnobservedTaskException += OnUnobservedException;
    
    // Вот так события в Debug Mode не будет
    _ = Test();
    Thread.Sleep(500);
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Console.WriteLine("GC finished");
}

private static void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs e)
{
    Console.WriteLine($"Unobserved exception: {e.Exception}, sender {sender?.GetType()}");
}
...
public static void TestCaller()
{
    TaskScheduler.UnobservedTaskException += OnUnobservedException;
    
    // А вот так - будет!
    CreateTaskAndForget();
    Thread.Sleep(500);
    
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Console.WriteLine("GC finished");
}

private static void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs e)
{
    Console.WriteLine($"Unobserved exception: {e.Exception}, sender {sender?.GetType()}");
}

private static void CreateTaskAndForget()
{
    _ = Test();
}
````

Если запустить процесс со включенным дебаггером, либо просто собранный под Debug а не Release, Task, внутри которого исключение, не соберётся раньше завершения программы.
В Debug Mode все локальные переменные удерживаются до выхода из стека выполнения, даже если они не используются. Поэтому мы увидим исключение только в Release,
либо - если вынесем вызов таски в отдельный метод.