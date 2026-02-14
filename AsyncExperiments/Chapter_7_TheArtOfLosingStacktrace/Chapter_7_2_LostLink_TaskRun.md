Ещё более явный пример механики потери стактрейса, описанной в главе 7_0 - это `Task.Run(...)`. Возьмём пример:
````csharp
public static async Task TaskRunExceptionTest()
{
    TaskScheduler.UnobservedTaskException += (sender, args) => Console.WriteLine($"Unobserved task exception {args.Exception.Flatten()}");
    _ = Layer0();
    await Task.Delay(1000);
    GC.Collect();
}

private static async Task Layer0()
{
    await Task.Delay(100);
    await Layer1();
}

private static async Task Layer1()
{
    await Task.Delay(100);
    await Layer2();
}

private static async Task Layer2()
{
    await Task.Delay(100);
    await Layer3();
}

private static async Task Layer3()
{
    await Task.Delay(100);
    Layer4();
}

private static async void Layer4()
{
    await Task.Delay(100);
    Task.Run(() =>
    {
        Console.WriteLine("Oh no!");
        throw new Exception("HORY SHIET!");
    });
}
````

Вывод программы будет следующим:
````
Oh no!
Unobserved task exception System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (HORY SHIET!)
 ---> System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TaskRunException.<>c.<Layer4>b__5_0()
   at System.Threading.Tasks.Task`1.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
--- End of stack trace from previous location ---
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   --- End of inner exception stack trace ---

Process finished with exit code 0.
````

В стактрейсе мы не получили даже метода `Layer4()`, откуда была запланирована задача. Это потому что с точки зрения стека исполнения, здесь точкой входа был не `Layer4()`,
а `ThreadPool`, который выполнил эту задачу, что и видно в стактрейсе.