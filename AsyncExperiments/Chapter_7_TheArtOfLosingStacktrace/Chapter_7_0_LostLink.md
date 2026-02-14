Простой способ потерять Stacktrace - не вызвать `awaiter.GetResult()` у асинхронного метода, внутри стейт машины которого было выброшено исключение. Возьмём пример:
````csharp
public static async Task AsyncTaskExceptionTest()
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
    await Layer4();
}

private static async Task Layer4()
{
    await Task.Delay(100);
    throw new Exception("HORY SHIET!");
}
````

Здесь цепочка вложенных `async Task` методов вызывает друг друга, и в конце последний - выбрасывает исключение. Вывод программы будет следующим:
````
Unobserved task exception System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. As a result, the unobserved exception was rethrown by the finalizer thread. (HORY SHIET!)
 ---> System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer4()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer3()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer2()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer0()
   --- End of inner exception stack trace ---
````

Можно наблюдать целостность стактрейса вплоть до `Layer0()`. Так происходит, потому что `async Task` методы, возвращая `awaiter` внутри скомпилированных стейт машин, каждый раз при вызове
`GetResult()` выбрасывают исключение. И поскольку стейт машины внутри делают `try-catch`, каждая из них вызывает `GetResult()`, ловит исключение, и через `SetException()` честно пробрасывает
исключение дальше: `Layer4()` -> `Layer3()` -> `Layer2()` -> `Layer1()` -> `Layer0()`.

При этом можно заметить, что стактрейс сохранился лишь до `Layer0()`. Это потому, что мы не ожидали `Layer0()` в методе `AsyncTaskExceptionTest()`, и поэтому поймать исключение
было просто некому - стейт машина `AsyncTaskExceptionTest()` в декомпилированном виде выглядит вот так:
````csharp
...
if (num1 != 0)
{
  TaskScheduler.UnobservedTaskException += AsyncTaskNestedException.<>c.<>9__0_0 ?? (AsyncTaskNestedException.<>c.<>9__0_0 = new EventHandler<UnobservedTaskExceptionEventArgs>((object) AsyncTaskNestedException.<>c.<>9, __methodptr(<AsyncTaskExceptionTest>b__0_0)));
  // Вызвали Layer0(), но проигнорировали его Task, и не забрали у него awaiter
  AsyncTaskNestedException.Layer0();
  awaiter = Task.Delay(1000).GetAwaiter();
  if (!awaiter.IsCompleted)
  {
    this.<>1__state = num2 = 0;
    this.<>u__1 = awaiter;
    this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter, AsyncTaskNestedException.<AsyncTaskExceptionTest>d__0>(ref awaiter, ref this);
    return;
  }
}
...
````

Если бы мы ожидали `Layer0()`, стейт машина выглядела бы уже вот так: 
````csharp
...
if (num1 != 0)
{
  if (num1 != 1)
  {
    TaskScheduler.UnobservedTaskException += AsyncTaskNestedException.<>c.<>9__0_0 ?? (AsyncTaskNestedException.<>c.<>9__0_0 = new EventHandler<UnobservedTaskExceptionEventArgs>((object) AsyncTaskNestedException.<>c.<>9, __methodptr(<AsyncTaskExceptionTest>b__0_0)));
    // Забрали awaiter...
    awaiter = AsyncTaskNestedException.Layer0().GetAwaiter();
    if (!awaiter.IsCompleted)
    {
      this.<>1__state = num2 = 0;
      this.<>u__1 = awaiter;
      this.<>t__builder.AwaitUnsafeOnCompleted<TaskAwaiter, AsyncTaskNestedException.<AsyncTaskExceptionTest>d__0>(ref awaiter, ref this);
      return;
    }
  }
  else
  {
    awaiter = this.<>u__1;
    this.<>u__1 = new TaskAwaiter();
    this.<>1__state = num2 = -1;
    goto label_9;
  }
}
...
// ...и на втором MoveNext() вызвали у него GetResult()
awaiter.GetResult();
awaiter = Task.Delay(1000).GetAwaiter();
````

Мы забрали awaiter у `Layer0()`, и вызвали `GetResult()`, обернув его в `try-catch`. Теперь метод `AsyncTaskExceptionTest` появится в стактрейсе, как и метод `Main()`, который его вызывает:
````

Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer4()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer3()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer2()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.Layer0()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncTaskNestedException.AsyncTaskExceptionTest()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)

Process finished with exit code -532,462,766.
````

Аналогичным образом можно потерять стактрейс, если выбросить исключение в `async void` методе - у `AsyncVoidMethodBuilder` внутри есть `Task`, но его никто не может ожидать. 
В похожем примере: 
````csharp
public static async Task AsyncVoidExceptionTest()
{
    _ = Layer0();
    await Task.Delay(3000);
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
    throw new Exception("HORY SHIET!");
}
````

Стактрейс потеряется в том месте, где мы перестали брать `awaiter` и вызывать у него `GetResult()`. То есть - на методе `Layer4()`:
````
Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.AsyncVoidNestedException.Layer4()
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_1(Object state)
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()

Process finished with exit code -532,462,766.
````

Таким образом, мы потеряли всю цепочку вызовов, и увидели только сам метод, в котором было выброшено исключение.

Аналогичная утрата произойдёт, и если у нас есть предоставленный SynchronizationContext, и `async void` сделает `Post(exception)` в него. Просто стактрейс будет начинаться не с ThreadPool,
а с контекста синхронизации:
````
Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AsyncVoidSynchronizationContextException.Layer4()
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_0(Object state)
   at AsyncResearch.AsyncExperiments.Chapter_3_SynchronizationContext.Source.SimpleManualContext.ExecuteTasks()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.AsyncVoidSynchronizationContextException.AsyncVoidExceptionTest()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)
````