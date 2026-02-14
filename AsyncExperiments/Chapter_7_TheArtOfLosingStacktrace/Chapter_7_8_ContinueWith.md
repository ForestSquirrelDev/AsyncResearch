Немного неочевидный способ потерять исключение - использовать `ContinueWith()`, но не проверить `Task.Exception`:
````csharp
var task = Task.Run(() => throw new Exception("Die!"))
    .ContinueWith(t => Console.WriteLine("I'm running anyway!"));
await task;
````

Результат работы программы:
````
I'm running anyway!

Process finished with exit code 0.
````

В конечном счёте исключение окажется в `UnobservedTaskException`. Чтобы этого избежать, можно вручную проверить исключение:
````csharp
var task = Task.Run(() => throw new Exception("Die!"))
    .ContinueWith(t => Console.WriteLine($"Oh no, exception occured! {t.Exception}"));
await task;
````

Вывод:
````
Oh no, exception occured! System.AggregateException: One or more errors occurred. (Die!)
 ---> System.Exception: Die!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.ContinueWithExample.<>c.<RunManualExceptionHandlingExample>b__1_0()
   at System.Threading.Tasks.Task`1.InnerInvoke()
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
--- End of stack trace from previous location ---
   at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)
   --- End of inner exception stack trace ---

Process finished with exit code 0.
````

Либо можно использовать опцию `OnlyOnRanToCompletion`: