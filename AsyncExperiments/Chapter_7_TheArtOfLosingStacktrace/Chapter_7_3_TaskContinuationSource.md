Менее очевидный способ потерять оригинальный stacktrace - вызвать ожидание `await` не из того места, где было создано исключение. Возьмём пример:
````csharp
public static class TcsException
{
    public static async Task TcsExceptionTest()
    {
        var tcs = new TaskCompletionSource();
        var myEx = new Exception("HORY SHIET!");
        
        tcs.SetException(myEx);
        Victim(tcs);
        
        await Task.Delay(3000);
    }

    private static async void Victim(TaskCompletionSource tcs)
    {
        await tcs.Task;
    }
}
````

В данном примере мы создали `TaskCompletionSource` и исключение в методе `TcsExceptionTest()`, но не сделали `throw` или `await` у `TaskCompletionSource`. Это привело к тому, что
исключение просто лежало в объекте `Task`, дожидаясь пока его вызовут через `TaskAwaiter.GetResult()`, или `~Finalizer`.

И тем, кто вызвал исключение через `TaskAwaiter.GetResult()`, стал метод `Victim()`:
````csharp
Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TcsException.Victim(TaskCompletionSource tcs)
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_1(Object state)
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()

Process finished with exit code -532,462,766.
````

Оригинальное место, где мы создали исключение - утеряно. Стактрейс выглядит так, будто исключение появилось в `Victim()`: рантайм .NET захватит стактрейс исключения не в момент его создания,
а в момент выброса.

Даже попытка использовать `ExceptionDispatchInfo.Capture(new Exception(...))` не спасёт ситуацию. `EDI` лишь копирует существующий стек. Если инструкция `throw` не выполнялась, 
`EDI` захватит пустоту, и стактрейс всё равно будет сформирован только в момент await `tcs.Task`:
````csharp
public static async Task TcsExceptionTest()
{
    var tcs = new TaskCompletionSource();
    var myEx = ExceptionDispatchInfo.Capture(new Exception("HORY SHIET!"));
    
    tcs.SetException(myEx.SourceException);
    Victim(tcs);
    
    await Task.Delay(3000);
}

private static async void Victim(TaskCompletionSource tcs)
{
    // Вывод программыбудет аналогичным предыдущему
    await tcs.Task;
}
````

Единственный способ захватить стактрейс в данном случае - это честно выбросить исключение:
````csharp
public static async Task TcsExceptionTest()
{
    var tcs = new TaskCompletionSource();
    try
    {
        throw new Exception("HORY SHIET!");
    }
    catch (Exception ex)
    {
        tcs.SetException(ex);
    }
    
    Victim(tcs);
    await Task.Delay(3000);
}

private static async void Victim(TaskCompletionSource tcs)
{
    await tcs.Task;
}
````

Вывод программы:
````
Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TcsException.TcsExceptionTest()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.TcsException.Victim(TaskCompletionSource tcs)
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_1(Object state)
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()

Process finished with exit code -532,462,766.

````